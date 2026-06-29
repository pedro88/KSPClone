#nullable enable annotations

using System;
using System.Collections.Generic;

namespace KSPClone.SimCore
{
    /// <summary>
    /// Emitted when a bubble's floating origin shifts (intra-bubble
    /// drift rebase, or bubble merge/split). Consumers (the Unity host's
    /// physics scene, client-side trails, predictor caches) must apply
    /// the inverse delta to their local cached state so the move is
    /// invisible to anything that reads world coordinates.
    /// </summary>
    public readonly struct FloatingOriginShiftedEvent
    {
        public BubbleId BubbleId { get; }
        public Vector3d WorldDelta { get; }
        public IReadOnlyCollection<VesselId> AffectedVessels { get; }

        public FloatingOriginShiftedEvent(BubbleId bubbleId, Vector3d worldDelta, IReadOnlyCollection<VesselId> affectedVessels)
        {
            BubbleId = bubbleId;
            WorldDelta = worldDelta;
            AffectedVessels = affectedVessels;
        }
    }

    /// <summary>
    /// Drives the floating-origin rebasing policy defined in
    /// ADR-0012 (§1 intra-bubble threshold 1024 m, §4–5 merge/split).
    ///
    /// Each tick:
    /// 1. Compute the bubble's local-space centroid from its active
    ///    vessels' cached local positions.
    /// 2. If the centroid's magnitude exceeds <see cref="RebaseThresholdMeters"/>,
    ///    shift <see cref="PhysicsBubble.GlobalOrigin"/> by the centroid
    ///    and emit a <see cref="FloatingOriginShiftedEvent"/> with the
    ///    delta so the Unity host can translate every rigidbody in the
    ///    bubble's scene by the inverse, leave velocities alone, and
    ///    reset any cached local state in client caches.
    ///
    /// Pure data — the manager computes and emits; it does not touch
    /// Unity transforms. The host owns the rigidbodies.
    /// </summary>
    public sealed class FloatingOriginManager
    {
        public const double DefaultRebaseThresholdMeters = 1024.0; // ADR-0012 §1

        public double RebaseThresholdMeters { get; }
        public double RebaseThresholdSquared => RebaseThresholdMeters * RebaseThresholdMeters;

        public event Action<FloatingOriginShiftedEvent>? OriginShifted;

        public FloatingOriginManager(double rebaseThresholdMeters = DefaultRebaseThresholdMeters)
        {
            if (rebaseThresholdMeters <= 0.0)
                throw new ArgumentOutOfRangeException(nameof(rebaseThresholdMeters), "Threshold must be positive.");
            RebaseThresholdMeters = rebaseThresholdMeters;
        }

        /// <summary>
        /// Check a single bubble and rebase if its local centroid drifts
        /// past the threshold. Returns true if a rebase happened this tick.
        /// </summary>
        public bool RebaseIfDrifted(PhysicsBubble bubble, IEnumerable<Vessel> activeVessels)
        {
            if (bubble is null) throw new ArgumentNullException(nameof(bubble));
            if (activeVessels is null) throw new ArgumentNullException(nameof(activeVessels));
            if (bubble.MemberCount == 0) return false;

            var (centroid, sampleCount) = ComputeLocalCentroid(bubble, activeVessels);
            if (sampleCount == 0) return false;

            if (centroid.LengthSquared <= RebaseThresholdSquared) return false;

            // Shift the bubble origin to the centroid; the local positions
            // effectively become (oldLocal - centroid), which is now small.
            bubble.Rebase(centroid);

            var affected = new List<VesselId>(sampleCount);
            foreach (var v in activeVessels)
                if (bubble.Contains(v.Id)) affected.Add(v.Id);

            OriginShifted?.Invoke(new FloatingOriginShiftedEvent(bubble.Id, centroid, affected));
            return true;
        }

        /// <summary>
        /// Rebase a freshly-formed bubble: set its origin to the cluster
        /// centroid (called by <see cref="BubbleManager"/> when creating
        /// a new bubble — see ADR-0012 §3).
        /// </summary>
        public void RebaseToCentroid(PhysicsBubble bubble, IEnumerable<Vessel> activeVessels)
        {
            if (bubble is null) throw new ArgumentNullException(nameof(bubble));
            if (activeVessels is null) throw new ArgumentNullException(nameof(activeVessels));
            if (bubble.MemberCount == 0) return;

            var (centroid, sampleCount) = ComputeLocalCentroid(bubble, activeVessels);
            if (sampleCount == 0) return;

            bubble.Rebase(centroid);

            var affected = new List<VesselId>(sampleCount);
            foreach (var v in activeVessels)
                if (bubble.Contains(v.Id)) affected.Add(v.Id);

            OriginShifted?.Invoke(new FloatingOriginShiftedEvent(bubble.Id, centroid, affected));
        }

        /// <summary>
        /// Merge two bubbles: keep the larger one (more members), re-base
        /// incoming vessels into the kept frame from authoritative global
        /// doubles (ADR-0012 §4). Returns the kept bubble.
        /// </summary>
        public PhysicsBubble MergeInto(PhysicsBubble keep, PhysicsBubble absorb, IEnumerable<Vessel> activeVessels)
        {
            if (keep is null) throw new ArgumentNullException(nameof(keep));
            if (absorb is null) throw new ArgumentNullException(nameof(absorb));
            if (activeVessels is null) throw new ArgumentNullException(nameof(activeVessels));
            if (keep.MemberCount < absorb.MemberCount) (keep, absorb) = (absorb, keep);

            var moved = new List<VesselId>();
            var newOrigin = keep.GlobalOrigin;
            // Snapshot absorb's members: we mutate absorb (Remove) while moving.
            foreach (var id in new List<VesselId>(absorb.Members))
            {
                var v = FindVessel(activeVessels, id);
                if (v is null || !v.CachedWorldPosition.HasValue) continue;
                // local = globalDouble - origin (in doubles; ADR-0012 §6 invariant)
                var newLocal = v.CachedWorldPosition.Value - newOrigin;
                v.CachedLocalPosition = newLocal;
                v.CachedLocalVelocity = v.CachedWorldVelocity;
                v.BubbleId = keep.Id;
                absorb.Remove(id);
                keep.Add(id);
                moved.Add(id);
                OriginShifted?.Invoke(new FloatingOriginShiftedEvent(keep.Id, newLocal, new[] { id }));
            }

            keep.RecordMerge();
            return keep;
        }

        private static (Vector3d centroid, int count) ComputeLocalCentroid(PhysicsBubble bubble, IEnumerable<Vessel> vessels)
        {
            double sx = 0, sy = 0, sz = 0;
            int n = 0;
            foreach (var v in vessels)
            {
                if (!bubble.Contains(v.Id)) continue;
                if (v.CachedLocalPosition is not { } local) continue;
                sx += local.X; sy += local.Y; sz += local.Z;
                n++;
            }
            if (n == 0) return (Vector3d.Zero, 0);
            return (new Vector3d(sx / n, sy / n, sz / n), n);
        }

        private static Vessel? FindVessel(IEnumerable<Vessel> source, VesselId id)
        {
            foreach (var v in source)
                if (v.Id.Equals(id)) return v;
            return null;
        }
    }
}