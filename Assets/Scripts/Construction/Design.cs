#nullable enable annotations

using System;

namespace KSPClone.Construction
{
    /// <summary>
    /// A craft blueprint (BUILD-1): a <see cref="PartTree"/> with no world state —
    /// distinct from an instantiated Vessel. The aggregate root carries the
    /// design id, display name, root node id, and the sequence number of the last
    /// applied edit op (the op-log cursor, M3-T03). It has *no* position,
    /// velocity, resources, or crew — those exist only on a launched Vessel
    /// (Constitution Art. 7).
    ///
    /// Node ids are monotonic per Design and handed out by <see cref="AllocateNodeId"/>;
    /// the server is the sole caller when it accepts an add op, so ids can never
    /// collide across concurrent editors (M3-T03).
    /// </summary>
    public sealed class Design
    {
        public DesignId Id { get; }
        public string Name { get; }
        public PartTree Tree { get; }
        public NodeId RootNodeId => Tree.Root;

        /// <summary>Sequence number of the last applied edit op (0 = only the seeded root).</summary>
        public long AppliedSeq { get; set; }

        private long _nextNodeId;

        private Design(DesignId id, string name, PartTree tree, long nextNodeId)
        {
            Id = id;
            Name = name ?? string.Empty;
            Tree = tree;
            _nextNodeId = nextNodeId;
        }

        /// <summary>
        /// Create a Design seeded with a single root part. The root gets node id 1;
        /// subsequent parts allocate 2, 3, … via <see cref="AllocateNodeId"/>.
        /// </summary>
        public static Design Create(DesignId id, string name, PartTypeId rootPartType)
        {
            var rootId = new NodeId(1);
            var root = new PartNode(rootId, rootPartType, NodeId.None, string.Empty, PartPose.Identity);
            return new Design(id, name, new PartTree(root), nextNodeId: 2);
        }

        /// <summary>
        /// Rehydrate a Design from persisted state (M3-T05): an already-built tree
        /// plus the saved node-id cursor and applied seq. No ops are replayed.
        /// </summary>
        public static Design Restore(DesignId id, string name, PartTree tree, long nextNodeId, long appliedSeq) =>
            new(id, name, tree, nextNodeId) { AppliedSeq = appliedSeq };

        /// <summary>Next monotonic node id for this Design (server-assigned on accepted adds).</summary>
        public NodeId AllocateNodeId() => new(_nextNodeId++);

        /// <summary>The id the next <see cref="AllocateNodeId"/> will return (for persistence round-trip).</summary>
        public long PeekNextNodeId => _nextNodeId;
    }
}
