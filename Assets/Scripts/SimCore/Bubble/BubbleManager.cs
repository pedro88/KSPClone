#nullable enable annotations

using System;
using System.Collections.Generic;

namespace KSPClone.SimCore
{
    /// <summary>
    /// Driven by the bubble manager each fixed tick. Surfaces membership
    /// changes so downstream consumers (promotion controller, replication,
    /// physics scene host) can react.
    /// </summary>
    public readonly struct BubbleMembershipChange
    {
        public BubbleId BubbleId { get; }
        public VesselId VesselId { get; }
        public BubbleMembershipChangeKind Kind { get; }

        public BubbleMembershipChange(BubbleId bubbleId, VesselId vesselId, BubbleMembershipChangeKind kind)
        {
            BubbleId = bubbleId;
            VesselId = vesselId;
            Kind = kind;
        }
    }

    public enum BubbleMembershipChangeKind
    {
        /// <summary>New bubble created to host this vessel (cluster had no bubble yet).</summary>
        Formed,
        /// <summary>Vessel joined an existing bubble that already owned other vessels.</summary>
        Joined,
        /// <summary>Vessel left a bubble (demotion, suspension, or reassignment to another bubble).</summary>
        Left
    }

    /// <summary>
    /// Owns the per-tick clustering pass that decides which
    /// active-physics vessels share a <see cref="PhysicsBubble"/>.
    ///
    /// Engine-agnostic: reads vessels' authoritative world positions,
    /// asks the <see cref="BubbleRegistry"/> to create / destroy
    /// bubbles, and emits membership-change events for the Unity host
    /// to instantiate / relocate rigidbodies in lockstep.
    ///
    /// Algorithm (M1-T02, PHYS-1, PHYS-2):
    /// 1. Gather every vessel in <see cref="VesselState.ActivePhysics"/>
    ///    whose authoritative world position is known.
    /// 2. Build a proximity graph: edge between two vessels iff their
    ///    world distance ≤ <see cref="PhysicsRangeRadiusMeters"/>
    ///    (default 2500 m).
    /// 3. Connected-components via union-find → clusters.
    /// 4. Map each cluster to a bubble: if every cluster member already
    ///    shares the same bubble id, reuse it; otherwise create a new
    ///    bubble anchored at the cluster's centroid (ADR-0012 §3) and
    ///    rebase incoming vessels.
    /// 5. Vessels that were active last tick but absent from this
    ///    tick's clustering output are reassigned (Left event); their
    ///    bubble is recycled or destroyed once empty (M1-T01).
    /// </summary>
    public sealed class BubbleManager
    {
        public const double DefaultPhysicsRangeRadiusMeters = 2500.0;

        public double PhysicsRangeRadiusMeters { get; }
        public double PhysicsRangeRadiusSquared => PhysicsRangeRadiusMeters * PhysicsRangeRadiusMeters;

        public event Action<PhysicsBubble>? BubbleFormed;
        public event Action<BubbleMembershipChange>? VesselJoinedBubble;
        public event Action<BubbleMembershipChange>? VesselLeftBubble;

        private readonly BubbleRegistry _registry;

        public BubbleManager(BubbleRegistry registry, double physicsRangeRadiusMeters = DefaultPhysicsRangeRadiusMeters)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            if (physicsRangeRadiusMeters <= 0.0)
                throw new ArgumentOutOfRangeException(nameof(physicsRangeRadiusMeters), "Range must be positive.");
            PhysicsRangeRadiusMeters = physicsRangeRadiusMeters;
        }

        /// <summary>
        /// Run the clustering pass. Returns the set of bubbles that
        /// gained or kept at least one active vessel this tick.
        ///
        /// <paramref name="activeVessels"/> is the world's authoritative
        /// vessel list — the manager filters to <see cref="VesselState.ActivePhysics"/>
        /// internally. Vessels without a cached world position (never
        /// yet stepped) are skipped this tick.
        /// </summary>
        public IReadOnlyList<PhysicsBubble> RunClusteringPass(IEnumerable<Vessel> activeVessels)
        {
            if (activeVessels is null) throw new ArgumentNullException(nameof(activeVessels));

            var candidates = GatherCandidates(activeVessels);
            var clusters = Cluster(candidates);

            // 1. Snapshot every current bubble member so we can drop the ones no
            //    longer claimed by a cluster this tick — including vessels that
            //    vanished from the input entirely (demoted / suspended / removed).
            var previousMembers = new Dictionary<VesselId, BubbleId>();
            foreach (var bubble in _registry.All)
                foreach (var vid in bubble.Members)
                    previousMembers[vid] = bubble.Id;

            // 2. For each cluster, resolve the bubble id (reuse or create) and assign members.
            var survivors = new HashSet<BubbleId>();
            foreach (var cluster in clusters)
            {
                var bubble = ResolveBubbleForCluster(cluster, survivors);
                foreach (var vessel in cluster.Vessels)
                {
                    // If the vessel already lived in another bubble (cluster spans multiple bubbles,
                    // or its old bubble was already claimed by another cluster this tick — a split),
                    // remove it from that one before adding to the new one.
                    if (vessel.BubbleId is { } oldBid && !oldBid.Equals(bubble.Id)
                        && _registry.TryGet(oldBid, out var oldBubble) && oldBubble.Remove(vessel.Id))
                    {
                        VesselLeftBubble?.Invoke(new BubbleMembershipChange(oldBid, vessel.Id, BubbleMembershipChangeKind.Left));
                    }

                    if (!bubble.Contains(vessel.Id))
                    {
                        bubble.Add(vessel.Id);
                        vessel.BubbleId = bubble.Id;
                        VesselJoinedBubble?.Invoke(new BubbleMembershipChange(bubble.Id, vessel.Id,
                            cluster.WasExisting ? BubbleMembershipChangeKind.Joined : BubbleMembershipChangeKind.Formed));
                    }
                    previousMembers.Remove(vessel.Id);
                }
                survivors.Add(bubble.Id);
            }

            // 3. Members not claimed by any cluster this tick (drifted out, demoted,
            //    suspended, or otherwise gone) leave their bubble.
            foreach (var kv in previousMembers)
            {
                if (_registry.TryGet(kv.Value, out var oldBubble) && oldBubble.Remove(kv.Key))
                {
                    if (FindVessel(activeVessels, kv.Key) is { } vv) vv.BubbleId = null;
                    VesselLeftBubble?.Invoke(new BubbleMembershipChange(kv.Value, kv.Key, BubbleMembershipChangeKind.Left));
                }
            }

            // 4. Garbage-collect bubbles that went empty.
            _registry.CollectEmpty();

            // 5. Return the bubbles that survived this tick.
            var result = new List<PhysicsBubble>(survivors.Count);
            foreach (var id in survivors)
                if (_registry.TryGet(id, out var b)) result.Add(b);
            return result;
        }

        private List<CandidateVessel> GatherCandidates(IEnumerable<Vessel> vessels)
        {
            var list = new List<CandidateVessel>();
            foreach (var v in vessels)
            {
                if (v.State != VesselState.ActivePhysics) continue;
                if (!v.CachedWorldPosition.HasValue) continue;
                list.Add(new CandidateVessel(v));
            }
            return list;
        }

        private static Vessel? FindVessel(IEnumerable<Vessel> source, VesselId id)
        {
            foreach (var v in source)
                if (v.Id.Equals(id)) return v;
            return null;
        }

        private List<VesselCluster> Cluster(List<CandidateVessel> candidates)
        {
            var clusters = new List<VesselCluster>();
            if (candidates.Count == 0) return clusters;

            // Union-find keyed by VesselId index.
            var indexOf = new Dictionary<VesselId, int>(candidates.Count);
            for (int i = 0; i < candidates.Count; i++) indexOf[candidates[i].Vessel.Id] = i;

            var parent = new int[candidates.Count];
            for (int i = 0; i < candidates.Count; i++) parent[i] = i;

            int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
            void Union(int a, int b) { var ra = Find(a); var rb = Find(b); if (ra != rb) parent[ra] = rb; }

            for (int i = 0; i < candidates.Count; i++)
            {
                for (int j = i + 1; j < candidates.Count; j++)
                {
                    if (CandidatesInRange(candidates[i], candidates[j]))
                        Union(i, j);
                }
            }

            // Bucket by root.
            var byRoot = new Dictionary<int, List<Vessel>>();
            for (int i = 0; i < candidates.Count; i++)
            {
                var root = Find(i);
                if (!byRoot.TryGetValue(root, out var bucket))
                {
                    bucket = new List<Vessel>();
                    byRoot[root] = bucket;
                }
                bucket.Add(candidates[i].Vessel);
            }

            foreach (var bucket in byRoot.Values)
                clusters.Add(new VesselCluster(bucket));
            return clusters;
        }

        private bool CandidatesInRange(CandidateVessel a, CandidateVessel b)
        {
            var pa = a.Vessel.CachedWorldPosition!.Value;
            var pb = b.Vessel.CachedWorldPosition!.Value;
            var dx = pa.X - pb.X; var dy = pa.Y - pb.Y; var dz = pa.Z - pb.Z;
            return dx * dx + dy * dy + dz * dz <= PhysicsRangeRadiusSquared;
        }

        private PhysicsBubble ResolveBubbleForCluster(VesselCluster cluster, HashSet<BubbleId> claimedThisTick)
        {
            // Did every member already share one bubble id?
            BubbleId? shared = null;
            foreach (var v in cluster.Vessels)
            {
                if (v.BubbleId is not { } bid) { shared = null; break; }
                if (shared is null) shared = bid;
                else if (!shared.Value.Equals(bid)) { shared = null; break; }
            }

            // (a) Whole cluster already shared one bubble AND no other cluster has
            // already claimed it this tick → reuse it. If another cluster took it
            // (the bubble split into two), this cluster falls through to a fresh one.
            if (shared is { } existing && !claimedThisTick.Contains(existing)
                && _registry.TryGet(existing, out var bubble))
            {
                cluster.WasExisting = true;
                return bubble;
            }

            // (b) Mixed: cluster spans multiple existing bubbles. Spawn a fresh
            // bubble at the cluster centroid. Incomers from other bubbles are
            // re-bubbled into it; their former bubbles lose members and may
            // become empty (CollectEmpty handles that below). Full merge
            // (one bubble absorbing the other) is a separate concern handled
            // by the merge pass wired in a later slice (T03+). The simple
            // reassignment here satisfies T02's acceptance: vessels in range
            // share one bubble within one tick of crossing R_phys.
            var centroid = ClusterCentroid(cluster);
            var fresh = _registry.Create(centroid);
            cluster.WasExisting = false;
            BubbleFormed?.Invoke(fresh);
            return fresh;
        }

        private static Vector3d ClusterCentroid(VesselCluster cluster)
        {
            double sx = 0, sy = 0, sz = 0;
            int n = 0;
            foreach (var v in cluster.Vessels)
            {
                if (!v.CachedWorldPosition.HasValue) continue;
                var p = v.CachedWorldPosition.Value;
                sx += p.X; sy += p.Y; sz += p.Z;
                n++;
            }
            if (n == 0) return Vector3d.Zero;
            return new Vector3d(sx / n, sy / n, sz / n);
        }

        private sealed class CandidateVessel
        {
            public Vessel Vessel { get; }
            public CandidateVessel(Vessel v) { Vessel = v; }
        }

        private sealed class VesselCluster
        {
            public List<Vessel> Vessels { get; }
            public bool WasExisting;
            public VesselCluster(List<Vessel> vessels) { Vessels = vessels; }
        }
    }
}