#nullable enable annotations

namespace KSPClone.Construction
{
    /// <summary>
    /// A single construction action (BUILD-1/2). Immutable, and references nodes
    /// only by stable <see cref="NodeId"/> — never by index or object ref — so an
    /// op means the same thing regardless of concurrent edits. Three kinds:
    /// add / remove / move. Applied by the pure <see cref="PartTreeMutator"/>.
    /// </summary>
    public abstract class EditOp
    {
        private protected EditOp() { }
    }

    /// <summary>Attach a new part under an existing parent at a named attach point.</summary>
    public sealed class AddPartOp : EditOp
    {
        public NodeId NewNodeId { get; }
        public PartTypeId PartType { get; }
        public NodeId ParentNodeId { get; }
        public string AttachPoint { get; }
        public PartPose LocalPose { get; }

        public AddPartOp(NodeId newNodeId, PartTypeId partType, NodeId parentNodeId, string attachPoint, PartPose localPose)
        {
            NewNodeId = newNodeId;
            PartType = partType;
            ParentNodeId = parentNodeId;
            AttachPoint = attachPoint ?? string.Empty;
            LocalPose = localPose;
        }
    }

    /// <summary>Remove a node and its whole subtree.</summary>
    public sealed class RemovePartOp : EditOp
    {
        public NodeId NodeId { get; }
        public RemovePartOp(NodeId nodeId) => NodeId = nodeId;
    }

    /// <summary>Re-parent a node (its subtree follows) to a new parent + attach point.</summary>
    public sealed class MovePartOp : EditOp
    {
        public NodeId NodeId { get; }
        public NodeId NewParentNodeId { get; }
        public string NewAttachPoint { get; }
        public PartPose NewLocalPose { get; }

        public MovePartOp(NodeId nodeId, NodeId newParentNodeId, string newAttachPoint, PartPose newLocalPose)
        {
            NodeId = nodeId;
            NewParentNodeId = newParentNodeId;
            NewAttachPoint = newAttachPoint ?? string.Empty;
            NewLocalPose = newLocalPose;
        }
    }

    /// <summary>Why an <see cref="EditOp"/> was rejected (BUILD-2: callers/tests assert the exact cause).</summary>
    public enum RejectionReason
    {
        UnknownNode,          // a referenced node (target or parent) does not exist
        DuplicateNode,        // the add's NewNodeId is already in the tree
        AttachPointOccupied,  // another child already occupies that attach point
        WouldCreateCycle,     // a move whose new parent lies inside the moved subtree
        RemoveRoot,           // remove targeting the root node
        CannotMoveRoot,       // move targeting the root node
        SubtreeLocked,        // covered by another player's subtree lock (M3-T07)
    }

    /// <summary>Outcome of applying one op: applied, or rejected with a reason.</summary>
    public readonly struct EditOpResult
    {
        public bool IsApplied { get; }
        public RejectionReason Reason { get; }
        /// <summary>For <see cref="RejectionReason.SubtreeLocked"/>: the lock holder (M3-T07); else default.</summary>
        public object? RejectedBy { get; }

        private EditOpResult(bool applied, RejectionReason reason, object? rejectedBy)
        {
            IsApplied = applied;
            Reason = reason;
            RejectedBy = rejectedBy;
        }

        public static readonly EditOpResult Applied = new(true, default, null);
        public static EditOpResult Rejected(RejectionReason reason, object? rejectedBy = null) =>
            new(false, reason, rejectedBy);
    }
}
