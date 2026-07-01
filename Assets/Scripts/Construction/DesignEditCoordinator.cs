#nullable enable annotations

using System;

namespace KSPClone.Construction
{
    /// <summary>
    /// Transport-facing outputs of the coordinator (M3-T04). The Server/Net layer
    /// implements this against the real replication channel; tests implement a
    /// fake to assert convergence without a transport.
    /// </summary>
    public interface IEditOpSink
    {
        /// <summary>Reply to the submitter: their client temp id maps to the server result (seq + assigned node id, or rejection).</summary>
        void Ack(Guid player, DesignId designId, long clientTempId, SubmitResult result);

        /// <summary>Deliver an accepted op to every session member (including the submitter).</summary>
        void Broadcast(System.Collections.Generic.IReadOnlyCollection<Guid> members, DesignId designId, SequencedEditOp op);

        /// <summary>Send a joining player the full-tree resync baseline (tree snapshot + current seq).</summary>
        void Snapshot(Guid player, Design design);
    }

    /// <summary>
    /// Ties the authoritative <see cref="DesignEditService"/> to editor
    /// <see cref="DesignEditorSessions"/> and the replication sink (M3-T04): on
    /// submit it acks the submitter and broadcasts the accepted op to all editors,
    /// so every replica converges on the same tree in the same order (BUILD-2).
    /// Engine-agnostic — the sink is the only transport seam.
    /// </summary>
    public sealed class DesignEditCoordinator
    {
        private readonly DesignEditService _service;
        private readonly DesignRegistry _registry;
        private readonly DesignEditorSessions _sessions;
        private readonly IEditOpSink _sink;

        public DesignEditCoordinator(DesignEditService service, DesignRegistry registry,
            DesignEditorSessions sessions, IEditOpSink sink)
        {
            _service = service;
            _registry = registry;
            _sessions = sessions;
            _sink = sink;
        }

        /// <summary>Register a player as an editor of a Design and send them the resync baseline.</summary>
        public void Join(DesignId designId, Guid player)
        {
            _sessions.Join(designId, player);
            if (_registry.TryGet(designId, out var design)) _sink.Snapshot(player, design);
        }

        public void Leave(DesignId designId, Guid player) => _sessions.Leave(designId, player);

        /// <summary>
        /// Handle one submit: sequence + apply authoritatively, ack the submitter
        /// (mapping their temp id), and broadcast the accepted op to all editors.
        /// </summary>
        public SubmitResult Submit(DesignId designId, Guid player, long clientTempId, EditOp op)
        {
            var result = _service.Submit(designId, player, op);
            _sink.Ack(player, designId, clientTempId, result);
            if (result.IsApplied && _registry.TryGetLog(designId, out var log) && log.Count > 0)
                _sink.Broadcast(_sessions.Members(designId), designId, log.Ops[log.Count - 1]);
            return result;
        }
    }
}
