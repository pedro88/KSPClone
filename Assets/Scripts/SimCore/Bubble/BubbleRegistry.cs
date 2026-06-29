#nullable enable annotations

using System;
using System.Collections.Generic;

namespace KSPClone.SimCore
{
    /// <summary>
    /// Owns every live <see cref="PhysicsBubble"/>. Creates, looks up, and
    /// destroys bubbles. Bubble ids are stable for the lifetime of the bubble
    /// and are never reused. The registry is the only place bubbles are
    /// constructed or torn down; callers (promotion controller, clustering
    /// pass) request mutations through the registry's API.
    ///
    /// Engine-agnostic: holds data only. The Unity host is notified via the
    /// <see cref="BubbleCreated"/> / <see cref="BubbleDestroyed"/> events
    /// so it can create/destroy the backing <c>PhysicsScene</c> in lockstep.
    /// </summary>
    public sealed class BubbleRegistry
    {
        public int Count => _bubbles.Count;

        public event Action<PhysicsBubble>? BubbleCreated;
        public event Action<PhysicsBubble>? BubbleDestroyed;

        private readonly Dictionary<BubbleId, PhysicsBubble> _bubbles = new();

        public IEnumerable<PhysicsBubble> All => _bubbles.Values;

        public bool TryGet(BubbleId id, out PhysicsBubble bubble) => _bubbles.TryGetValue(id, out bubble!);

        public PhysicsBubble Create(Vector3d globalOrigin, BubbleSceneHandle? scene = null)
        {
            var bubble = new PhysicsBubble(BubbleId.New(), globalOrigin, scene);
            _bubbles[bubble.Id] = bubble;
            BubbleCreated?.Invoke(bubble);
            return bubble;
        }

        /// <summary>
        /// Destroy a bubble. Caller must ensure it has no members first
        /// (a bubble with active vessels must not be destroyed — promotion
        /// controller demotes its vessels first; clustering pass reassigns
        /// them). Returns true if a bubble was removed.
        /// </summary>
        public bool Destroy(BubbleId id)
        {
            if (!_bubbles.TryGetValue(id, out var bubble)) return false;
            if (bubble.MemberCount > 0)
                throw new InvalidOperationException(
                    $"Refusing to destroy bubble {id} with {bubble.MemberCount} active member(s). " +
                    "Demote or relocate vessels before destroying.");
            _bubbles.Remove(id);
            BubbleDestroyed?.Invoke(bubble);
            return true;
        }

        /// <summary>
        /// Sweep every bubble marked <see cref="BubbleLifecycle.Empty"/> and
        /// destroy it. Returns the number of bubbles removed. Called once
        /// per tick by the bubble manager after membership changes settle.
        /// </summary>
        public int CollectEmpty()
        {
            var doomed = new List<BubbleId>();
            foreach (var b in _bubbles.Values)
                if (b.Lifecycle == BubbleLifecycle.Empty && b.MemberCount == 0)
                    doomed.Add(b.Id);

            foreach (var id in doomed)
            {
                _bubbles.Remove(id, out var bubble);
                BubbleDestroyed?.Invoke(bubble!);
            }
            return doomed.Count;
        }
    }
}