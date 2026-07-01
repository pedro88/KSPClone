#nullable enable annotations

using System.Collections.Generic;

namespace KSPClone.Construction
{
    public enum BroadcastApply { Applied, Duplicate, Buffered }

    /// <summary>
    /// A client-side replica of a Design driven purely by the server's ordered
    /// op broadcasts (M3-T04). Applies ops strictly in seq order; an out-of-order
    /// arrival is buffered until the gap fills, and a persistent gap signals the
    /// client to request a fresh snapshot resync. Because ops carry
    /// server-assigned node ids, replaying them here reproduces the server tree
    /// exactly (BUILD-2 convergence).
    /// </summary>
    public sealed class DesignReplica
    {
        private readonly Design _design;
        private readonly PartTypeId _rootPartType;
        private readonly SortedDictionary<long, SequencedEditOp> _buffer = new();

        public long AppliedSeq { get; private set; }
        public PartTree Tree => _design.Tree;
        public DesignId DesignId => _design.Id;
        public bool HasBufferedGap => _buffer.Count > 0;

        public DesignReplica(Design seed, PartTypeId rootPartType, long baselineSeq)
        {
            _design = seed;
            _rootPartType = rootPartType;
            AppliedSeq = baselineSeq;
        }

        public BroadcastApply ApplyBroadcast(SequencedEditOp op)
        {
            if (op.Seq <= AppliedSeq) return BroadcastApply.Duplicate;
            if (op.Seq > AppliedSeq + 1)
            {
                _buffer[op.Seq] = op; // out of order — hold it
                return BroadcastApply.Buffered;
            }
            ApplyInOrder(op);
            DrainBuffer();
            return BroadcastApply.Applied;
        }

        private void DrainBuffer()
        {
            while (_buffer.TryGetValue(AppliedSeq + 1, out var next))
            {
                _buffer.Remove(next.Seq);
                ApplyInOrder(next);
            }
        }

        private void ApplyInOrder(SequencedEditOp op)
        {
            PartTreeMutator.Apply(_design.Tree, op.Op); // stored op carries server ids
            AppliedSeq = op.Seq;
        }

        /// <summary>
        /// Replace this replica's baseline with a fresh authoritative snapshot
        /// (tree + seq) after a detected gap, then drain any still-relevant
        /// buffered ops. The caller rebuilds <paramref name="freshTree"/> from a
        /// server DesignSnapshot.
        /// </summary>
        public void Resync(long seq)
        {
            AppliedSeq = seq;
            // Drop buffered ops the snapshot already covers; keep/replay newer ones.
            var stale = new List<long>();
            foreach (var kv in _buffer) if (kv.Key <= seq) stale.Add(kv.Key);
            foreach (var s in stale) _buffer.Remove(s);
            DrainBuffer();
        }
    }
}
