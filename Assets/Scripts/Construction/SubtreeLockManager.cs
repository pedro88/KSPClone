#nullable enable annotations

using System;
using System.Collections.Generic;

namespace KSPClone.Construction
{
    /// <summary>Outcome of a lock claim: granted, or denied with the current holder.</summary>
    public readonly struct LockResult
    {
        public bool Granted { get; }
        public Guid HeldBy { get; }
        private LockResult(bool granted, Guid heldBy) { Granted = granted; HeldBy = heldBy; }
        public static readonly LockResult Grant = new(true, default);
        public static LockResult Deny(Guid heldBy) => new(false, heldBy);
    }

    /// <summary>
    /// Advisory subtree locks (M3-T06/T07, BUILD-3). A player claims a subassembly
    /// node; the server is the single authority over who holds what. Overlapping
    /// claims across different holders are denied (a node, any ancestor, or any
    /// descendant already held by someone else blocks the claim). Foreign edit
    /// ops targeting a locked subtree are rejected via <see cref="CheckOp"/>
    /// (wired into <see cref="DesignEditService.PreApplyGate"/>); the holder's own
    /// ops pass. Locks auto-release when a player leaves/disconnects.
    /// </summary>
    public sealed class SubtreeLockManager
    {
        // designId → (locked root node → holder)
        private readonly Dictionary<DesignId, Dictionary<NodeId, Guid>> _locks = new();

        public LockResult Claim(DesignId designId, PartTree tree, NodeId nodeId, Guid player)
        {
            if (!tree.Contains(nodeId)) return LockResult.Deny(default); // nothing to lock
            var designLocks = Locks(designId);
            foreach (var kv in designLocks)
            {
                if (kv.Value == player) continue; // own locks never block one another
                // Overlap iff one subtree contains the other's root.
                if (tree.IsSelfOrAncestor(kv.Key, nodeId) || tree.IsSelfOrAncestor(nodeId, kv.Key))
                    return LockResult.Deny(kv.Value);
            }
            designLocks[nodeId] = player;
            return LockResult.Grant;
        }

        /// <summary>Release a lock; only the holder (or server cleanup with the holder id) may.</summary>
        public bool Release(DesignId designId, NodeId nodeId, Guid player)
        {
            if (_locks.TryGetValue(designId, out var d) && d.TryGetValue(nodeId, out var holder) && holder == player)
                return d.Remove(nodeId);
            return false;
        }

        /// <summary>Drop every lock a player holds across all designs (on leave/disconnect).</summary>
        public void ReleaseAllForPlayer(Guid player)
        {
            foreach (var d in _locks.Values)
            {
                var drop = new List<NodeId>();
                foreach (var kv in d) if (kv.Value == player) drop.Add(kv.Key);
                foreach (var n in drop) d.Remove(n);
            }
        }

        public bool TryGetHolder(DesignId designId, NodeId nodeId, out Guid holder)
        {
            holder = default;
            return _locks.TryGetValue(designId, out var d) && d.TryGetValue(nodeId, out holder);
        }

        /// <summary>Current locks in a design (root node → holder), for broadcast / advisory UI.</summary>
        public IReadOnlyDictionary<NodeId, Guid> LocksIn(DesignId designId) =>
            _locks.TryGetValue(designId, out var d) ? d : Empty;

        /// <summary>
        /// Pre-apply gate (M3-T07): reject an op whose target lies inside a
        /// subtree locked by another player. Returns a rejection or null (allow).
        /// Wire as <c>service.PreApplyGate = mgr.CheckOp</c>.
        /// </summary>
        public EditOpResult? CheckOp(Design design, Guid player, EditOp op)
        {
            if (!_locks.TryGetValue(design.Id, out var designLocks) || designLocks.Count == 0)
                return null;

            foreach (var target in Targets(op))
            {
                var covering = FindCovering(designLocks, design.Tree, target);
                if (covering is { } c && c.holder != player)
                    return EditOpResult.Rejected(RejectionReason.SubtreeLocked, c.holder);
            }
            return null;
        }

        private static IEnumerable<NodeId> Targets(EditOp op)
        {
            switch (op)
            {
                case AddPartOp a: yield return a.ParentNodeId; break;
                case RemovePartOp r: yield return r.NodeId; break;
                case MovePartOp m: yield return m.NodeId; yield return m.NewParentNodeId; break;
            }
        }

        private static (NodeId lockNode, Guid holder)? FindCovering(
            Dictionary<NodeId, Guid> designLocks, PartTree tree, NodeId node)
        {
            if (designLocks.TryGetValue(node, out var h)) return (node, h);
            foreach (var anc in tree.Ancestors(node))
                if (designLocks.TryGetValue(anc, out var ah)) return (anc, ah);
            return null;
        }

        private Dictionary<NodeId, Guid> Locks(DesignId designId)
        {
            if (!_locks.TryGetValue(designId, out var d)) _locks[designId] = d = new Dictionary<NodeId, Guid>();
            return d;
        }

        private static readonly Dictionary<NodeId, Guid> Empty = new();
    }
}
