#nullable enable annotations

using System;
using System.Collections.Generic;

namespace KSPClone.SimCore
{
    /// <summary>
    /// Lifecycle of a bubble. A bubble exists only while it owns at
    /// least one active-physics vessel; when the last vessel demotes or
    /// is suspended, the registry destroys the bubble (ADR-0012 §2 —
    /// "empty bubble destroys; vessels fall back to on-rails/suspended").
    /// </summary>
    public enum BubbleLifecycle
    {
        /// <summary>Bubble owns ≥ 1 active vessel. Eligible for tick + merge.</summary>
        Live,
        /// <summary>Bubble owns zero active vessels. Marked for destruction on the next tick.</summary>
        Empty
    }

    /// <summary>
    /// A cluster of active-physics vessels that share one floating origin
    /// and one isolated physics scene (ADR-0003, ADR-0012). Engine-agnostic
    /// data: the actual <c>PhysicsScene</c> handle lives behind an opaque
    /// <see cref="BubbleSceneHandle"/> the Unity host provides.
    ///
    /// Authoritative state (member vessel ids, the floating origin, lifecycle)
    /// lives in the sim core; the per-tick rigidbody transforms live in the
    /// engine scene the host owns. The host writes local transforms back to
    /// <see cref="Vessel.CachedLocalPosition"/> / <see cref="Vessel.CachedLocalVelocity"/>
    /// each tick so the sim core can observe them without touching Unity types.
    /// </summary>
    public sealed class PhysicsBubble
    {
        public BubbleId Id { get; }
        public IReadOnlyCollection<VesselId> Members => _members;
        public Vector3d GlobalOrigin { get; private set; }
        public BubbleLifecycle Lifecycle { get; private set; }
        public BubbleSceneHandle? Scene { get; private set; }
        public uint MergeGeneration { get; private set; }

        private readonly HashSet<VesselId> _members;

        public PhysicsBubble(BubbleId id, Vector3d globalOrigin, BubbleSceneHandle? scene = null)
        {
            Id = id;
            GlobalOrigin = globalOrigin;
            Scene = scene;
            _members = new HashSet<VesselId>();
            Lifecycle = BubbleLifecycle.Empty;
        }

        public int MemberCount => _members.Count;

        public void Add(VesselId vessel)
        {
            if (_members.Add(vessel))
                Lifecycle = BubbleLifecycle.Live;
        }

        public bool Remove(VesselId vessel)
        {
            if (!_members.Remove(vessel)) return false;
            if (_members.Count == 0) Lifecycle = BubbleLifecycle.Empty;
            return true;
        }

        public bool Contains(VesselId vessel) => _members.Contains(vessel);

        /// <summary>
        /// Translate the floating origin by <paramref name="delta"/> in
        /// world coordinates. The host is responsible for applying the
        /// inverse delta to every rigidbody in the bubble's physics scene
        /// in the same tick — this method only updates the authoritative
        /// anchor (ADR-0012 §1, §6).
        /// </summary>
        public void Rebase(Vector3d delta) => GlobalOrigin = GlobalOrigin + delta;

        /// <summary>
        /// Adopt a new scene (used on merge when an incoming cluster moves
        /// into this bubble's existing scene — ADR-0012 §4).
        /// </summary>
        public void AttachScene(BubbleSceneHandle scene) => Scene = scene;

        public void DetachScene() => Scene = null;

        /// <summary>Mark this bubble as having been merged into another (audit only).</summary>
        public void RecordMerge() => MergeGeneration++;
    }

    /// <summary>
    /// Opaque handle to the Unity-side <c>PhysicsScene</c> backing a
    /// <see cref="PhysicsBubble"/>. Stays as <c>Guid</c>-flavoured token so
    /// the sim core does not take a dependency on UnityEngine
    /// (Constitution Art. 3 + SimCore asmdef contract).
    /// </summary>
    public readonly struct BubbleSceneHandle : IEquatable<BubbleSceneHandle>
    {
        public Guid Value { get; }

        public BubbleSceneHandle(Guid value) => Value = value;

        public static BubbleSceneHandle New() => new(Guid.NewGuid());

        public bool Equals(BubbleSceneHandle other) => Value.Equals(other.Value);
        public override bool Equals(object? obj) => obj is BubbleSceneHandle h && Equals(h);
        public override int GetHashCode() => Value.GetHashCode();
        public override string ToString() => Value.ToString();

        public static bool operator ==(BubbleSceneHandle a, BubbleSceneHandle b) => a.Equals(b);
        public static bool operator !=(BubbleSceneHandle a, BubbleSceneHandle b) => !a.Equals(b);
    }
}