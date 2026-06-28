#nullable enable annotations

using System;
using System.Collections.Generic;

namespace KSPClone.SimCore
{
    /// <summary>
    /// Reason a vessel was promoted from on-rails to active-physics
    /// (PHYS-2). Surfaced on the promotion event for diagnostics,
    /// replication, and persistence.
    /// </summary>
    public enum PromotionReason
    {
        /// <summary>A seated player explicitly loaded the vessel.</summary>
        PlayerLoad,
        /// <summary>The vessel entered R_phys of another active-physics vessel.</summary>
        Proximity
    }

    /// <summary>
    /// Emitted when a vessel is promoted. The Unity host uses the seed
    /// state to instantiate the rigidbody in the target bubble's scene;
    /// persistence writes the new state.
    /// </summary>
    public readonly struct PromotionEvent
    {
        public VesselId VesselId { get; }
        public BubbleId BubbleId { get; }
        public Vector3d WorldPosition { get; }
        public Vector3d WorldVelocity { get; }
        public PromotionReason Reason { get; }
        public double MasterClockAtPromotion { get; }

        public PromotionEvent(VesselId vesselId, BubbleId bubbleId, Vector3d worldPosition, Vector3d worldVelocity, PromotionReason reason, double masterClockAtPromotion)
        {
            VesselId = vesselId;
            BubbleId = bubbleId;
            WorldPosition = worldPosition;
            WorldVelocity = worldVelocity;
            Reason = reason;
            MasterClockAtPromotion = masterClockAtPromotion;
        }
    }

    /// <summary>
    /// Decides which on-rails vessels should be promoted to
    /// active-physics (PHYS-2). Engine-agnostic: reads the world state
    /// and emits decisions via events. The Unity host reacts by
    /// instantiating rigidbodies; the bubble manager picks the vessel
    /// up on its next clustering pass.
    ///
    /// Promotion triggers (PHYS-2):
    ///  - A seated player explicitly loads a vessel (<see cref="RequestPlayerLoad"/>).
    ///  - A vessel is within R_phys of any other active-physics vessel
    ///    and is not already promoted.
    /// </summary>
    public sealed class PromotionController
    {
        public event Action<PromotionEvent>? VesselPromoted;

        private readonly SimWorld _world;
        private readonly BubbleManager _bubbles;
        private readonly BubbleRegistry _registry;
        private readonly HashSet<VesselId> _playerLoadRequests = new();

        public PromotionController(SimWorld world, BubbleManager bubbles, BubbleRegistry registry)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _bubbles = bubbles ?? throw new ArgumentNullException(nameof(bubbles));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        /// <summary>Mark a vessel for promotion because a player is loading it.</summary>
        public void RequestPlayerLoad(VesselId vesselId) => _playerLoadRequests.Add(vesselId);

        public void CancelPlayerLoad(VesselId vesselId) => _playerLoadRequests.Remove(vesselId);

        /// <summary>
        /// Scan the world for promotion candidates and promote them in
        /// place. Idempotent within a tick.
        /// </summary>
        public void RunPass(double masterClockSeconds)
        {
            var activeSet = CollectActiveIds();

            foreach (var vessel in _world.Vessels.Values)
            {
                if (vessel.State != VesselState.OnRails) continue;
                if (!IsOrbitElliptic(vessel)) continue;

                PromotionReason? reason = null;
                if (_playerLoadRequests.Contains(vessel.Id)) reason = PromotionReason.PlayerLoad;
                else if (IsInRangeOfAnyActive(vessel, activeSet)) reason = PromotionReason.Proximity;

                if (reason is null) continue;

                var (worldPos, worldVel) = SampleOnRailsState(vessel, masterClockSeconds);
                var bubble = AssignToBubble(vessel, worldPos);

                vessel.State = VesselState.ActivePhysics;
                vessel.BubbleId = bubble.Id;
                vessel.VesselClockSeconds = masterClockSeconds;
                vessel.CachedWorldPosition = worldPos;
                vessel.CachedWorldVelocity = worldVel;
                vessel.CachedLocalPosition = worldPos - bubble.GlobalOrigin;
                vessel.CachedLocalVelocity = worldVel;

                bubble.Add(vessel.Id);

                VesselPromoted?.Invoke(new PromotionEvent(
                    vessel.Id, bubble.Id, worldPos, worldVel, reason.Value, masterClockSeconds));

                _playerLoadRequests.Remove(vessel.Id);
            }
        }

        private HashSet<VesselId> CollectActiveIds()
        {
            var ids = new HashSet<VesselId>();
            foreach (var v in _world.Vessels.Values)
                if (v.State == VesselState.ActivePhysics && v.CachedWorldPosition.HasValue)
                    ids.Add(v.Id);
            return ids;
        }

        private bool IsInRangeOfAnyActive(Vessel candidate, HashSet<VesselId> activeIds)
        {
            if (!candidate.CachedWorldPosition.HasValue) return false;
            var cp = candidate.CachedWorldPosition.Value;
            foreach (var v in _world.Vessels.Values)
            {
                if (!activeIds.Contains(v.Id)) continue;
                if (!v.CachedWorldPosition.HasValue) continue;
                var dx = v.CachedWorldPosition.Value.X - cp.X;
                var dy = v.CachedWorldPosition.Value.Y - cp.Y;
                var dz = v.CachedWorldPosition.Value.Z - cp.Z;
                if (dx * dx + dy * dy + dz * dz <= _bubbles.PhysicsRangeRadiusSquared)
                    return true;
            }
            return false;
        }

        private static bool IsOrbitElliptic(Vessel vessel)
        {
            return vessel.Orbit.SemiMajorAxis > 0.0 && vessel.Orbit.Eccentricity < 1.0;
        }

        private (Vector3d worldPos, Vector3d worldVel) SampleOnRailsState(Vessel vessel, double masterClockSeconds)
        {
            if (_world.Bodies is { } bodies)
            {
                var (_, _, worldPos, worldVel) =
                    KeplerPropagator.WorldFrameStateAt(vessel.Orbit, masterClockSeconds, bodies);
                return (worldPos, worldVel);
            }
            return (vessel.CachedWorldPosition ?? Vector3d.Zero,
                    vessel.CachedWorldVelocity ?? Vector3d.Zero);
        }

        /// <summary>
        /// Reuse the bubble the vessel is already in (rare, after a
        /// mid-tick demotion), reuse an existing bubble within R_phys,
        /// or create a fresh one anchored at the vessel's world
        /// position. Mirrors the rule used by the bubble manager on
        /// cluster creation (ADR-0012 §3) but for a singleton.
        /// </summary>
        private PhysicsBubble AssignToBubble(Vessel vessel, Vector3d worldPos)
        {
            foreach (var bubble in _bubbles.All)
            {
                if (bubble.MemberCount == 0) continue;
                if (bubble.Contains(vessel.Id)) return bubble;
            }
            PhysicsBubble? nearest = null;
            double nearestDistSq = double.PositiveInfinity;
            foreach (var bubble in _bubbles.All)
            {
                if (bubble.MemberCount == 0) continue;
                var d = bubble.GlobalOrigin - worldPos;
                var dsq = d.LengthSquared;
                if (dsq < nearestDistSq) { nearestDistSq = dsq; nearest = bubble; }
            }
            if (nearest is { } n && nearestDistSq <= _bubbles.PhysicsRangeRadiusSquared)
                return n;
            return _registry.Create(worldPos);
        }
    }
}