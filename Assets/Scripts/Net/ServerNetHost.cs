using System.Collections.Generic;
using KSPClone.SimCore;

namespace KSPClone.Net
{
    /// <summary>
    /// Glues a transport to the authoritative <see cref="ServerSimulation"/>:
    /// on peer connect it registers a player and sends the world handshake; it
    /// routes inbound client commands (warp request/approve) into the sim by
    /// the peer's player id; and it broadcasts each emitted snapshot bundle to
    /// every connected peer. Transport-agnostic (loopback or LiteNetLib).
    /// </summary>
    public sealed class ServerNetHost
    {
        private readonly IServerTransport _transport;
        private readonly ServerSimulation _sim;
        private readonly Dictionary<int, PlayerId> _peerToPlayer = new();
        private readonly Dictionary<PlayerId, int> _playerToPeer = new();

        public ServerNetHost(IServerTransport transport, ServerSimulation sim)
        {
            _transport = transport;
            _sim = sim;
            _transport.PeerConnected += OnPeerConnected;
            _transport.PeerDisconnected += OnPeerDisconnected;
            _transport.Received += OnReceived;
            _sim.SnapshotEmitted += OnSnapshot;
        }

        public void Poll() => _transport.Poll();

        private void OnPeerConnected(int peerId)
        {
            var (session, handshake) = _sim.Connect();
            _peerToPlayer[peerId] = session.Id;
            _playerToPeer[session.Id] = peerId;
            _transport.Send(peerId, WireCodec.EncodeHandshake(handshake));
        }

        private void OnPeerDisconnected(int peerId)
        {
            if (!_peerToPlayer.TryGetValue(peerId, out var pid)) return;
            _sim.Disconnect(pid);
            _peerToPlayer.Remove(peerId);
            _playerToPeer.Remove(pid);
        }

        private void OnReceived(int peerId, byte[] data)
        {
            if (WireCodec.PeekType(data) != MessageType.ClientCommand) return;
            if (!_peerToPlayer.TryGetValue(peerId, out var pid)) return;

            var cmd = WireCodec.DecodeClientCommand(data);
            switch (cmd.Type)
            {
                case ClientCommandType.RequestWarp:
                    _sim.RequestWarp(new WarpRequest(pid, cmd.Multiplier, cmd.Kind));
                    break;
                case ClientCommandType.ApproveWarp:
                    _sim.ApproveWarp(pid);
                    break;
            }
        }

        private void OnSnapshot(SnapshotBundle bundle)
        {
            if (_peerToPlayer.Count == 0) return;
            var bytes = WireCodec.EncodeSnapshot(bundle);
            foreach (var peerId in _peerToPlayer.Keys)
                _transport.Send(peerId, bytes);
        }
    }
}
