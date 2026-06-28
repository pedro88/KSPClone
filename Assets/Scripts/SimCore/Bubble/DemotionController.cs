#nullable enable annotations

using System;
using System.Collections.Generic;

namespace KSPClone.SimCore
{
    /// <summary>
    /// Emitted when a vessel is demoted from active-physics to on-rails
    /// (PHYS-3). Persistence writes the new orbit; replication updates
    /// clients to the analytic stream for this vessel.
    /// </summary>
    public readonly struct DemotionEvent
    {
        public VesselId VesselId { get; }
        public BubbleId PreviousBubbleId { get; }
        public Orbit NewOrbit { get; }
        public DemotionReason Reason { get; }

        public DemotionEvent(VesselId vesselId, BubbleId previousBubbleId, Orbit newOrbit, DemotionReason reason)
        {
            VesselId = vesselId;
            PreviousBubbleId = previousBubbleId;
            NewOrbit = newOrbit;
            Reason = reason;
        }
    }

    public enum DemotionReason
    {
        /// <summary>Last player left and the vessel is warp-safe.</summary>
        UnattendedWarpSafe,
        /// <summary>External caller (e.g. server command) forced the demotion.</summary>
        Forced
    }

    /// <summary>
    /// Owns the active-physics → on-rails transition (PHYS-3, ADR-0002).
    /// Engine-agnostic. Called once per fixed tick after the bubble
    /// manager's clustering pass.
    ///
    /// The controller:
    /// 1. Asks the caller whether the vessel is currently occupied
    ///    (<see cref="OccupancyLookup"/> — plugged by the crew layer
    ///    when M2 lands).
    /// 2. If unattended and <see cref="WarpSafeEvaluator"/> reports
    ///    warp-safe, fits a closed-form orbit from the vessel's
    ///    authoritative world position+velocity (relative to its current
    ///    SOI body) and demotes it.
    /// 3. Removes the vessel from its bubble. The registry's
    ///    <see cref="BubbleRegistry.CollectEmpty"/> pass then sweeps the
    ///    bubble if it has no more members (ADR-0012 §2).
    /// </summary>
    public sealed class DemotionController
    {
        public event Action<DemotionEvent>? VesselDemoted;

        private readonly SimWorld _world;
        private readonly BubbleRegistry _registry;
        private readonly WarpSafeEvaluator _warpSafe;
        private readonly Func<VesselId, bool>? _occupancy;

        public DemotionController(
            SimWorld world,
            BubbleRegistry registry,
            WarpSafeEvaluator warpSafe,
            Func<VesselId, bool>? occupancyLookup = null)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _warpSafe = warpSafe ?? throw new ArgumentNullException(nameof(warpSafe));
            _occupancy = occupancyLookup;
        }

        /// <summary>
        /// Run the demotion scan. Returns the vessels demoted this tick.
        /// </summary>
        public IReadOnlyList<VesselId> RunPass(bool atmosphereDefault = false)
        {
            var demoted = new List<VesselId>();
            foreach (var vessel in _world.Vessels.Values)
            {
                if (vessel.State != VesselState.ActivePhysics) continue;
                if (vessel.BubbleId is not { } bid) continue;

                if (_occupancy is not null && _occupancy(vessel.Id)) continue;
                if (!_warpSafe.IsWarpSafe(vessel, atmosphereDefault)) continue;

                if (TryDemote(vessel, bid, DemotionReason.UnattendedWarpSafe))
                    demoted.Add(vessel.Id);
            }
            // Sweep any bubble that lost its last member this pass (ADR-0012 §2).
            _registry.CollectEmpty();
            return demoted;
        }

        /// <summary>
        /// Force-demote a vessel regardless of warp-safety. Used by the
        /// server command surface and by tests.
        /// </summary>
        public bool ForceDemote(VesselId vesselId)
        {
            if (!_world.Vessels.TryGetValue(vesselId, out var vessel)) return false;
            if (vessel.State != VesselState.ActivePhysics) return false;
            if (vessel.BubbleId is not { } bid) return false;
            return TryDemote(vessel, bid, DemotionReason.Forced);
        }

        private bool TryDemote(Vessel vessel, BubbleId bubbleId, DemotionReason reason)
        {
            if (!vessel.CachedWorldPosition.HasValue || !vessel.CachedWorldVelocity.HasValue)
                return false;

            var parent = vessel.Orbit.ParentBody;
            if (_world.Bodies is null) return false;
            if (!_world.Bodies.TryGet(parent, out var body)) return false;

            // Convert from world frame to parent frame for the fitter.
            var parentWorldPos = _world.Bodies.WorldPositionOf(parent, _world.Clock.GameTimeSeconds);
            var relPos = vessel.CachedWorldPosition.Value - parentWorldPos;

            Orbit newOrbit;
            try
            {
                newOrbit = StateVectorToOrbit.Convert(
                    relPos, vessel.CachedWorldVelocity.Value,
                    body.GravParameterMu, _world.Clock.GameTimeSeconds, parent);
            }
            catch (Exception)
            {
                // Hyperbolic/parabolic/radial fallback: keep the existing orbit;
                // demotion is refused so the vessel stays in physics.
                return false;
            }

            vessel.Orbit = newOrbit;
            vessel.State = VesselState.OnRails;
            vessel.BubbleId = null;
            vessel.VesselClockSeconds = _world.Clock.GameTimeSeconds;

            // Clear local caches — vessel is no longer in any bubble.
            vessel.CachedLocalPosition = null;
            vessel.CachedLocalVelocity = null;

            if (_registry.TryGet(bubbleId, out var bubble))
                bubble.Remove(vessel.Id);

            VesselDemoted?.Invoke(new DemotionEvent(vessel.Id, bubbleId, newOrbit, reason));
            return true;
        }
    }
}