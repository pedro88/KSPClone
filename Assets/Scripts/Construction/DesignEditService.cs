#nullable enable annotations

using System;

namespace KSPClone.Construction
{
    /// <summary>Outcome of submitting an edit op to the authoritative service.</summary>
    public readonly struct SubmitResult
    {
        public bool IsApplied { get; }
        public long Seq { get; }
        /// <summary>Server-assigned node id for an accepted <see cref="AddPartOp"/> (else <see cref="NodeId.None"/>).</summary>
        public NodeId AssignedNodeId { get; }
        public RejectionReason Reason { get; }
        public object? RejectedBy { get; }

        private SubmitResult(bool applied, long seq, NodeId assigned, RejectionReason reason, object? rejectedBy)
        {
            IsApplied = applied; Seq = seq; AssignedNodeId = assigned; Reason = reason; RejectedBy = rejectedBy;
        }

        public static SubmitResult Applied(long seq, NodeId assignedNodeId) =>
            new(true, seq, assignedNodeId, default, null);
        public static SubmitResult Rejected(RejectionReason reason, object? rejectedBy = null) =>
            new(false, 0, NodeId.None, reason, rejectedBy);
    }

    /// <summary>
    /// Server-authoritative edit service (M3-T03, BUILD-2). Every accepted op gets
    /// a monotonic per-Design sequence number in <b>arrival order</b>, is appended
    /// to the Design's <see cref="EditOpLog"/>, and folded into the canonical
    /// <see cref="PartTree"/>. The server — not the client — assigns the stable
    /// node id for an add, so two concurrent adds can never collide (Art. 1, no
    /// CRDT). Arrival order is the call order on the single authoritative sim
    /// thread (Art. 2); a hook lets M3-T07 pre-check subtree locks.
    /// </summary>
    public sealed class DesignEditService
    {
        private readonly DesignRegistry _registry;

        /// <summary>
        /// Optional pre-apply gate (M3-T07 subtree locks): given the design, the
        /// submitting player, and the op, return a rejection reason to block it,
        /// or null to allow. Set by the lock layer; null here = no gating.
        /// </summary>
        public Func<Design, Guid, EditOp, EditOpResult?>? PreApplyGate { get; set; }

        public DesignEditService(DesignRegistry registry) => _registry = registry;

        public SubmitResult Submit(DesignId designId, Guid player, EditOp op)
        {
            if (!_registry.TryGet(designId, out var design) || !_registry.TryGetLog(designId, out var log))
                return SubmitResult.Rejected(RejectionReason.UnknownNode);

            // Lock/authority pre-check (M3-T07): reject before touching the tree.
            if (PreApplyGate?.Invoke(design, player, op) is { IsApplied: false } gate)
                return SubmitResult.Rejected(gate.Reason, gate.RejectedBy);

            // The server assigns the authoritative node id for adds; the client's
            // proposed id is only a local temp handle (mapped back in the ack, T04).
            var assigned = NodeId.None;
            var toApply = op;
            if (op is AddPartOp add)
            {
                assigned = design.AllocateNodeId();
                toApply = new AddPartOp(assigned, add.PartType, add.ParentNodeId, add.AttachPoint, add.LocalPose);
            }

            var result = PartTreeMutator.Apply(design.Tree, toApply);
            if (!result.IsApplied)
                return SubmitResult.Rejected(result.Reason, result.RejectedBy);

            var seq = log.LastSeq + 1;
            log.Append(new SequencedEditOp(seq, player, toApply));
            design.AppliedSeq = seq;
            return SubmitResult.Applied(seq, assigned);
        }

        /// <summary>
        /// Rebuild a Design's canonical tree by folding its op-log in seq order —
        /// the invariant the log must satisfy (used by persistence + tests).
        /// </summary>
        public static PartTree Fold(PartTypeId rootPartType, EditOpLog log)
        {
            var design = Design.Create(DesignId.New(), "fold", rootPartType);
            foreach (var s in log.Ops)
                PartTreeMutator.Apply(design.Tree, s.Op); // stored ops already carry server ids
            return design.Tree;
        }
    }
}
