#nullable enable annotations

using System;
using System.Collections.Generic;

namespace KSPClone.Construction
{
    /// <summary>
    /// One accepted edit op with its server-assigned global sequence number and
    /// source. <see cref="SourcePlayer"/> is a raw <see cref="Guid"/>, not a
    /// flight-side PlayerId, so the Construction assembly stays free of any
    /// reference to the identity/flight assemblies (Art. 7); the networking layer
    /// maps PlayerId ↔ Guid at the boundary (M3-T04).
    /// </summary>
    public readonly struct SequencedEditOp
    {
        public long Seq { get; }
        public Guid SourcePlayer { get; }
        public EditOp Op { get; }

        public SequencedEditOp(long seq, Guid sourcePlayer, EditOp op)
        {
            Seq = seq;
            SourcePlayer = sourcePlayer;
            Op = op;
        }
    }

    /// <summary>
    /// Per-Design append-only record of accepted, sequenced edit ops (BUILD-2).
    /// The canonical <see cref="PartTree"/> is the fold of this log in seq order;
    /// the log is the ordered source of truth for replication and persistence.
    /// </summary>
    public sealed class EditOpLog
    {
        private readonly List<SequencedEditOp> _ops = new();

        public IReadOnlyList<SequencedEditOp> Ops => _ops;
        public int Count => _ops.Count;
        /// <summary>Sequence number of the last appended op, or 0 if none.</summary>
        public long LastSeq => _ops.Count == 0 ? 0 : _ops[_ops.Count - 1].Seq;

        public void Append(SequencedEditOp op)
        {
            if (op.Seq != LastSeq + 1)
                throw new ArgumentException($"Non-contiguous seq {op.Seq}; expected {LastSeq + 1}.", nameof(op));
            _ops.Add(op);
        }
    }
}
