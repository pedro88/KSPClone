#nullable enable annotations

using System;
using System.Collections.Generic;
using KSPClone.Construction;
using KSPClone.Launch;
using KSPClone.SimCore;

namespace KSPClone.Net
{
    /// <summary>
    /// Server-side VAB host (M3 wiring): owns the authoritative construction
    /// services (registry, edit service, sessions, subtree locks, coordinator)
    /// and bridges them to the transport. It implements <see cref="IEditOpSink"/>
    /// by encoding messages and sending them via a player-addressed callback, and
    /// handles inbound Design-channel messages routed from <see cref="ServerNetHost"/>.
    /// Launch reads a Design and instantiates a Vessel at the surface pad through
    /// <see cref="LaunchInstantiator"/> — the only construction→flight seam (Art. 7).
    /// </summary>
    public sealed class ServerVabHost : IEditOpSink
    {
        private readonly ServerSimulation _sim;
        private readonly Action<Guid, byte[]> _sendToPlayer;
        private readonly PartCatalog _catalog = StockParts.Catalog();
        private readonly DesignRegistry _registry = new();
        private readonly DesignEditorSessions _sessions = new();
        private readonly SubtreeLockManager _locks = new();
        private readonly DesignEditService _service;
        private readonly DesignEditCoordinator _coordinator;

        public ServerVabHost(ServerSimulation sim, Action<Guid, byte[]> sendToPlayer)
        {
            _sim = sim;
            _sendToPlayer = sendToPlayer;
            _service = new DesignEditService(_registry) { PreApplyGate = _locks.CheckOp };
            _coordinator = new DesignEditCoordinator(_service, _registry, _sessions, this);
            // Seed the shared demo Design so every client has something to join.
            _registry.Register(StockParts.CreateDemoDesign());
        }

        /// <summary>Route one Design-channel message (player resolved from the connection).</summary>
        public void Handle(Guid player, byte[] data)
        {
            switch (WireCodec.PeekType(data))
            {
                case MessageType.JoinDesign:
                    _coordinator.Join(DesignWireCodec.DecodeJoin(data), player);
                    break;
                case MessageType.LeaveDesign:
                    _coordinator.Leave(DesignWireCodec.DecodeLeave(data), player);
                    break;
                case MessageType.EditOpSubmit:
                    var s = DesignWireCodec.DecodeSubmit(data);
                    _coordinator.Submit(s.DesignId, player, s.ClientTempId, s.Op);
                    break;
                case MessageType.ClaimLock:
                    ClaimLock(player, DesignWireCodec.DecodeLockRequest(data));
                    break;
                case MessageType.ReleaseLock:
                    ReleaseLock(player, DesignWireCodec.DecodeLockRequest(data));
                    break;
                case MessageType.LaunchDesign:
                    LaunchDesign(DesignWireCodec.DecodeLaunch(data));
                    break;
            }
        }

        public void OnPlayerDisconnect(Guid player)
        {
            _locks.ReleaseAllForPlayer(player);
            _sessions.LeaveAll(player);
            // (Lock-release broadcasts on disconnect are omitted for M3; clients
            // re-sync lock state on their next snapshot/join.)
        }

        private void ClaimLock(Guid player, LockRequestMessage m)
        {
            if (!_registry.TryGet(m.DesignId, out var design)) return;
            var res = _locks.Claim(m.DesignId, design.Tree, m.NodeId, player);
            if (res.Granted)
                BroadcastToSession(m.DesignId,
                    DesignWireCodec.EncodeLockBroadcast(new LockBroadcastMessage(m.DesignId, m.NodeId, player, true)));
        }

        private void ReleaseLock(Guid player, LockRequestMessage m)
        {
            if (_locks.Release(m.DesignId, m.NodeId, player))
                BroadcastToSession(m.DesignId,
                    DesignWireCodec.EncodeLockBroadcast(new LockBroadcastMessage(m.DesignId, m.NodeId, default, false)));
        }

        private void LaunchDesign(DesignId designId)
        {
            if (!_registry.TryGet(designId, out var design)) return;

            // Pad = the surface launch site (+Y pole), epoch at 'now' so the craft
            // sits at the pole regardless of clock (ADR-0018, as ServerBootstrap
            // does for the seed craft).
            var seed = WorldSeed.CreateSurfaceVessel().Orbit;
            var orbit = new Orbit(seed.SemiMajorAxis, seed.Eccentricity, seed.Inclination,
                seed.LongitudeOfAscendingNode, seed.ArgumentOfPeriapsis, seed.MeanAnomalyAtEpoch,
                _sim.World.Clock.GameTimeSeconds, seed.ParentBody);

            LaunchInstantiator.Launch(design, _catalog, orbit, _sim.World, _sim.Masses, _sim.Engines);
            // The new vessel now flows to clients via the normal snapshot path;
            // a player occupies its Pilot station to promote + fly it (M2.5 pad).
        }

        // --- IEditOpSink ---

        public void Ack(Guid player, DesignId designId, long clientTempId, SubmitResult result) =>
            _sendToPlayer(player, DesignWireCodec.EncodeAck(new EditOpAckMessage(
                designId, clientTempId, result.IsApplied, result.Seq, result.AssignedNodeId, result.Reason)));

        public void Broadcast(IReadOnlyCollection<Guid> members, DesignId designId, SequencedEditOp op)
        {
            var bytes = DesignWireCodec.EncodeBroadcast(new EditOpBroadcastMessage(designId, op));
            foreach (var m in members) _sendToPlayer(m, bytes);
        }

        public void Snapshot(Guid player, Design design)
        {
            var nodes = new List<PartNode>();
            foreach (var id in design.Tree.Subtree(design.RootNodeId))
                if (design.Tree.TryGet(id, out var n)) nodes.Add(n);
            design.Tree.TryGet(design.RootNodeId, out var root);
            _sendToPlayer(player, DesignWireCodec.EncodeSnapshot(
                new DesignSnapshotMessage(design.Id, design.AppliedSeq, root.PartType, nodes)));
        }

        private void BroadcastToSession(DesignId designId, byte[] bytes)
        {
            foreach (var m in _sessions.Members(designId)) _sendToPlayer(m, bytes);
        }
    }
}
