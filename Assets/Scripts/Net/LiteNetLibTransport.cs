using System;
using System.Collections.Generic;
using LiteNetLib;

namespace KSPClone.Net
{
    /// <summary>
    /// Real UDP server transport over LiteNetLib (the transport choice per
    /// ADR-0008/0009). Same <see cref="IServerTransport"/> contract as the
    /// loopback, so <see cref="ServerNetHost"/> is unchanged. M0 sends everything
    /// ReliableOrdered for simplicity; snapshots can move to an unreliable
    /// sequenced channel when loss/bandwidth becomes a real constraint (netcode §4).
    /// </summary>
    public sealed class LiteNetLibServerTransport : IServerTransport, IDisposable
    {
        public const string ConnectionKey = "KSPClone";

        private readonly EventBasedNetListener _listener = new();
        private readonly NetManager _net;
        private readonly Dictionary<int, NetPeer> _peers = new();

        public event Action<int> PeerConnected;
        public event Action<int> PeerDisconnected;
        public event Action<int, byte[]> Received;

        public LiteNetLibServerTransport()
        {
            _net = new NetManager(_listener);
            _listener.ConnectionRequestEvent += request => request.AcceptIfKey(ConnectionKey);
            _listener.PeerConnectedEvent += peer =>
            {
                _peers[peer.Id] = peer;
                PeerConnected?.Invoke(peer.Id);
            };
            _listener.PeerDisconnectedEvent += (peer, _) =>
            {
                _peers.Remove(peer.Id);
                PeerDisconnected?.Invoke(peer.Id);
            };
            _listener.NetworkReceiveEvent += (peer, reader, _, _) =>
            {
                var bytes = reader.GetRemainingBytes();
                reader.Recycle();
                Received?.Invoke(peer.Id, bytes);
            };
        }

        public void Start(int port) => _net.Start(port);

        public void Send(int peerId, byte[] payload)
        {
            if (_peers.TryGetValue(peerId, out var peer))
                peer.Send(payload, DeliveryMethod.ReliableOrdered);
        }

        public void Poll() => _net.PollEvents();

        public void Dispose() => _net.Stop();
    }

    /// <summary>Real UDP client transport over LiteNetLib (mirror of the server side).</summary>
    public sealed class LiteNetLibClientTransport : IClientTransport, IDisposable
    {
        private readonly EventBasedNetListener _listener = new();
        private readonly NetManager _net;
        private NetPeer _server;

        public event Action Connected;
        public event Action Disconnected;
        public event Action<byte[]> Received;

        public LiteNetLibClientTransport()
        {
            _net = new NetManager(_listener);
            _listener.PeerConnectedEvent += peer => { _server = peer; Connected?.Invoke(); };
            _listener.PeerDisconnectedEvent += (_, __) => { _server = null; Disconnected?.Invoke(); };
            _listener.NetworkReceiveEvent += (_, reader, ___, ____) =>
            {
                var bytes = reader.GetRemainingBytes();
                reader.Recycle();
                Received?.Invoke(bytes);
            };
        }

        public void Connect(string host, int port)
        {
            _net.Start();
            _net.Connect(host, port, LiteNetLibServerTransport.ConnectionKey);
        }

        public void Send(byte[] payload) => _server?.Send(payload, DeliveryMethod.ReliableOrdered);

        public void Poll() => _net.PollEvents();

        public void Dispose() => _net.Stop();
    }
}
