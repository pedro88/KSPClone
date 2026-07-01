#nullable enable annotations

namespace KSPClone.Construction
{
    /// <summary>
    /// One node in a <see cref="PartTree"/>: a placed part instance. Addressed by
    /// a stable <see cref="NodeId"/> that never changes for the node's life, so
    /// edit ops and subtree locks refer to it durably. The root node has
    /// <see cref="Parent"/> = <see cref="NodeId.None"/> and an empty attach key.
    ///
    /// Immutable: mutations (M3-T02) produce new nodes / a new tree rather than
    /// editing in place, keeping op application a pure function.
    /// </summary>
    public sealed class PartNode
    {
        public NodeId Id { get; }
        public PartTypeId PartType { get; }
        public NodeId Parent { get; }
        /// <summary>Attach-point key on the parent this node hangs from (empty for the root).</summary>
        public string AttachPoint { get; }
        /// <summary>Local transform relative to the parent's attach point.</summary>
        public PartPose LocalPose { get; }

        public PartNode(NodeId id, PartTypeId partType, NodeId parent, string attachPoint, PartPose localPose)
        {
            Id = id;
            PartType = partType;
            Parent = parent;
            AttachPoint = attachPoint ?? string.Empty;
            LocalPose = localPose;
        }

        public bool IsRoot => Parent.IsNone;

        /// <summary>Copy with a new parent + attach + local pose (used by MovePartOp, M3-T02).</summary>
        public PartNode WithParent(NodeId newParent, string attachPoint, PartPose localPose) =>
            new(Id, PartType, newParent, attachPoint, localPose);
    }
}
