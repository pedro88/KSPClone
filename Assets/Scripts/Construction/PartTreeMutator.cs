#nullable enable annotations

namespace KSPClone.Construction
{
    /// <summary>
    /// Applies one <see cref="EditOp"/> to a <see cref="PartTree"/> (BUILD-1/2).
    /// Fully deterministic: no I/O, no clock, no randomness — the same (tree, op)
    /// always yields the same result. Validation happens before any mutation, so
    /// a rejected op leaves the tree byte-for-byte unchanged. On success the tree
    /// is mutated in place (it is the canonical tree the op-log folds into, T03).
    /// </summary>
    public static class PartTreeMutator
    {
        public static EditOpResult Apply(PartTree tree, EditOp op) => op switch
        {
            AddPartOp add => ApplyAdd(tree, add),
            RemovePartOp rem => ApplyRemove(tree, rem),
            MovePartOp mov => ApplyMove(tree, mov),
            _ => EditOpResult.Rejected(RejectionReason.UnknownNode),
        };

        private static EditOpResult ApplyAdd(PartTree tree, AddPartOp op)
        {
            if (tree.Contains(op.NewNodeId)) return EditOpResult.Rejected(RejectionReason.DuplicateNode);
            if (!tree.Contains(op.ParentNodeId)) return EditOpResult.Rejected(RejectionReason.UnknownNode);
            if (!AttachPointFree(tree, op.ParentNodeId, op.AttachPoint, exclude: NodeId.None))
                return EditOpResult.Rejected(RejectionReason.AttachPointOccupied);

            tree.Add(new PartNode(op.NewNodeId, op.PartType, op.ParentNodeId, op.AttachPoint, op.LocalPose));
            return EditOpResult.Applied;
        }

        private static EditOpResult ApplyRemove(PartTree tree, RemovePartOp op)
        {
            if (!tree.Contains(op.NodeId)) return EditOpResult.Rejected(RejectionReason.UnknownNode);
            if (op.NodeId.Equals(tree.Root)) return EditOpResult.Rejected(RejectionReason.RemoveRoot);

            tree.RemoveSubtree(op.NodeId);
            return EditOpResult.Applied;
        }

        private static EditOpResult ApplyMove(PartTree tree, MovePartOp op)
        {
            if (!tree.TryGet(op.NodeId, out _)) return EditOpResult.Rejected(RejectionReason.UnknownNode);
            if (op.NodeId.Equals(tree.Root)) return EditOpResult.Rejected(RejectionReason.CannotMoveRoot);
            if (!tree.Contains(op.NewParentNodeId)) return EditOpResult.Rejected(RejectionReason.UnknownNode);
            // Cycle guard: the new parent must not be the node itself or a descendant of it.
            if (tree.IsSelfOrAncestor(op.NodeId, op.NewParentNodeId))
                return EditOpResult.Rejected(RejectionReason.WouldCreateCycle);
            if (!AttachPointFree(tree, op.NewParentNodeId, op.NewAttachPoint, exclude: op.NodeId))
                return EditOpResult.Rejected(RejectionReason.AttachPointOccupied);

            tree.TryGet(op.NodeId, out var node);
            tree.Reparent(node.WithParent(op.NewParentNodeId, op.NewAttachPoint, op.NewLocalPose));
            return EditOpResult.Applied;
        }

        // True if no child of `parent` (other than `exclude`) already uses `attachPoint`.
        private static bool AttachPointFree(PartTree tree, NodeId parent, string attachPoint, NodeId exclude)
        {
            foreach (var childId in tree.Children(parent))
            {
                if (childId.Equals(exclude)) continue;
                if (tree.TryGet(childId, out var child) &&
                    string.Equals(child.AttachPoint, attachPoint, System.StringComparison.Ordinal))
                    return false;
            }
            return true;
        }
    }
}
