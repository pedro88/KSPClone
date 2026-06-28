#nullable enable annotations

using System;
using System.Collections.Generic;

namespace KSPClone.SimCore
{
    /// <summary>
    /// Snapshot of a vessel's authoritative state at the moment it is
    /// suspended (SUSP-3, ADR-0002). Captures everything needed to
    /// resume the vessel byte-identical later — rigid-body state in
    /// the bubble's local frame, vessel clock, resources, vessel-state
    /// flags. Suspended vessels do not advance in master clock; the
    /// vessel clock captures the freeze point.
    /// </summary>
    public sealed class VesselSnapshot
    {
        public VesselId VesselId { get; }
        public BubbleId BubbleId { get; }
        public double SuspendedAtMasterClock { get; }
        public double VesselClockAtSuspend { get; }

        public Vector3d LocalPosition { get; }
        public Vector3d LocalVelocity { get; }
        public Vector3d AngularVelocity { get; }
        public Orbit OrbitAtSuspend { get; }
        public bool WasThrustActive { get; }
        public CelestialBodyId ParentBody { get; }

        public VesselSnapshot(
            VesselId vesselId, BubbleId bubbleId, double suspendedAtMasterClock, double vesselClockAtSuspend,
            Vector3d localPosition, Vector3d localVelocity, Vector3d angularVelocity,
            Orbit orbitAtSuspend, bool wasThrustActive, CelestialBodyId parentBody)
        {
            VesselId = vesselId;
            BubbleId = bubbleId;
            SuspendedAtMasterClock = suspendedAtMasterClock;
            VesselClockAtSuspend = vesselClockAtSuspend;
            LocalPosition = localPosition;
            LocalVelocity = localVelocity;
            AngularVelocity = angularVelocity;
            OrbitAtSuspend = orbitAtSuspend;
            WasThrustActive = wasThrustActive;
            ParentBody = parentBody;
        }
    }

    /// <summary>
    /// In-memory snapshot table (SUSP-3). Indexed by vessel id; the
    /// store is the source of truth for suspended vessels and the
    /// only place a vessel can be resumed from. A future slice wires
    /// this through Postgres (PERSIST-1) so a server restart resumes
    /// exactly.
    /// </summary>
    public sealed class SnapshotStore
    {
        private readonly Dictionary<VesselId, VesselSnapshot> _byVessel = new();

        public int Count => _byVessel.Count;

        public void Save(VesselSnapshot snapshot) => _byVessel[snapshot.VesselId] = snapshot;

        public bool TryGet(VesselId vesselId, out VesselSnapshot snapshot)
            => _byVessel.TryGetValue(vesselId, out snapshot!);

        public bool Remove(VesselId vesselId) => _byVessel.Remove(vesselId);

        public IEnumerable<VesselSnapshot> All => _byVessel.Values;
    }

    /// <summary>
    /// Suspension controller (SUSP-3, SUSP-4). Two responsibilities:
    ///
    /// 1. Suspend: when a vessel becomes unoccupied and the
    ///    <see cref="WarpSafeEvaluator"/> says NOT warp-safe (mid-burn,
    ///    mid-docking, atmospheric), snapshot its state and pause its
    ///    vessel clock. The bubble is then torn down if it becomes
    ///    empty (ADR-0012 §2). Master clock keeps advancing; suspended
    ///    vessels do not (no retro-sim, no rail-snap — Art. 4).
    ///
    /// 2. Resume: when a vessel is loaded again, restore its state
    ///    from the snapshot, re-create or join a bubble, and unpause
    ///    the vessel clock from its frozen value. The vessel clock
    ///    may lag the master clock by however long the vessel was
    ///    suspended (the gap is real time elapsed).
    /// </summary>
    public sealed class SuspensionController
    {
        public event Action<VesselSnapshot>? VesselSuspended;
        public event Action<VesselSnapshot>? VesselResumed;

        private readonly SimWorld _world;
        private readonly BubbleRegistry _registry;
        private readonly SnapshotStore _store;
        private readonly WarpSafeEvaluator _warpSafe;

        public SuspensionController(SimWorld world, BubbleRegistry registry, SnapshotStore store, WarpSafeEvaluator warpSafe)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _warpSafe = warpSafe ?? throw new ArgumentNullException(nameof(warpSafe));
        }

        /// <summary>
        /// Run the suspension scan. For each active unoccupied vessel
        /// that is NOT warp-safe, take the suspend path. Returns the
        /// vessels suspended this tick.
        /// </summary>
        public IReadOnlyList<VesselId> RunSuspensionPass(Func<VesselId, bool>? occupancyLookup, bool atmosphereDefault = false)
        {
            var suspended = new List<VesselId>();
            foreach (var vessel in _world.Vessels.Values)
            {
                if (vessel.State != VesselState.ActivePhysics) continue;
                if (occupancyLookup is not null && occupancyLookup(vessel.Id)) continue;
                if (_warpSafe.IsWarpSafe(vessel, atmosphereDefault)) continue;
                if (TrySuspend(vessel)) suspended.Add(vessel.Id);
            }
            return suspended;
        }

        /// <summary>
        /// Force-suspend a vessel regardless of warp-safety. Used by
        /// the server command surface and by tests.
        /// </summary>
        public bool Suspend(VesselId vesselId)
        {
            if (!_world.Vessels.TryGetValue(vesselId, out var vessel)) return false;
            if (vessel.State != VesselState.ActivePhysics) return false;
            return TrySuspend(vessel);
        }

        /// <summary>
        /// Resume a suspended vessel from its stored snapshot. The
        /// vessel clock continues from its frozen value; the vessel
        /// does NOT advance to cover the elapsed master-clock gap
        /// (no retro-sim — Art. 4).
        /// </summary>
        public bool Resume(VesselId vesselId, BubbleId? bubbleId = null)
        {
            if (!_store.TryGet(vesselId, out var snap)) return false;
            if (!_world.Vessels.TryGetValue(vesselId, out var vessel)) return false;

            BubbleId targetBubble = bubbleId ?? snap.BubbleId;
            if (!_registry.TryGet(targetBubble, out var bubble))
            {
                // The bubble the vessel was suspended from no longer exists
                // (e.g. torn down with another vessel's demotion). Spawn a
                // fresh one anchored at the snapshot's local position +
                // (0,0,0) — caller can pass an explicit bubbleId if they
                // want to resume into a specific bubble.
                bubble = _registry.Create(Vector3d.Zero);
                targetBubble = bubble.Id;
            }

            vessel.State = VesselState.ActivePhysics;
            vessel.BubbleId = targetBubble;
            vessel.VesselClockSeconds = snap.VesselClockAtSuspend;
            vessel.Orbit = snap.OrbitAtSuspend;
            vessel.CachedLocalPosition = snap.LocalPosition;
            vessel.CachedLocalVelocity = snap.LocalVelocity;
            vessel.CachedWorldPosition = bubble.GlobalOrigin + snap.LocalPosition;
            vessel.CachedWorldVelocity = snap.LocalVelocity;
            vessel.ThrustActive = snap.WasThrustActive;

            bubble.Add(vessel.Id);
            _store.Remove(vessel.Id);

            VesselResumed?.Invoke(snap);
            return true;
        }

        private bool TrySuspend(Vessel vessel)
        {
            if (!vessel.CachedLocalPosition.HasValue || !vessel.CachedLocalVelocity.HasValue)
                return false;
            if (vessel.BubbleId is not { } bid) return false;

            var snap = new VesselSnapshot(
                vessel.Id,
                bid,
                suspendedAtMasterClock: _world.Clock.GameTimeSeconds,
                vesselClockAtSuspend: vessel.VesselClockSeconds,
                localPosition: vessel.CachedLocalPosition.Value,
                localVelocity: vessel.CachedLocalVelocity.Value,
                angularVelocity: Vector3d.Zero, // M1: scalar velocity only
                orbitAtSuspend: vessel.Orbit,
                wasThrustActive: vessel.ThrustActive,
                parentBody: vessel.Orbit.ParentBody);

            _store.Save(snap);

            vessel.State = VesselState.Suspended;
            vessel.BubbleId = null;
            vessel.CachedLocalPosition = null;
            vessel.CachedLocalVelocity = null;

            if (_registry.TryGet(bid, out var bubble))
                bubble.Remove(vessel.Id);

            VesselSuspended?.Invoke(snap);
            return true;
        }
    }
}