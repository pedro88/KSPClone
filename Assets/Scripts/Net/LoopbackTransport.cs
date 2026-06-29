using System;
using System.Collections.Generic;

namespace KSPClone.Net
{
    /// <summary>
    /// In-process transport pair (one client ↔ server), no sockets. Messages
    /// queue on Send and are delivered on Poll, mirroring the poll-based real
    /// transport; connection events fire on each end's first Poll. A permanent
    /// test double + editor harness, drop-in for IServerTransport/IClientTransport.
    /// </summary>
    public sealed class LoopbackTransport
    {
        public const int ClientPeerId = 1;

        private readonly Queue<byte[]> _toServer = new();
        private readonly Queue<byte[]> _toClient = new();
        private readonly ServerEnd _server;
        private readonly ClientEnd _client;

        public IServerTransport Server => _server;
        public IClientTransport Client => _client;

        public LoopbackTransport()
        {
            _server = new ServerEnd(this);
            _client = new ClientEnd(this);
        }

        /// <summary>Simulate the link dropping (raises disconnect on both ends).</summary>
        public void Disconnect()
        {
            _server.RaiseDisconnect();
            _client.RaiseDisconnect();
        }

        private sealed class ServerEnd : IServerTransport
        {
            private readonly LoopbackTransport _t;
            private bool _connectRaised;
            public ServerEnd(LoopbackTransport t) => _t = t;

            public event Action<int> PeerConnected;
            public event Action<int> PeerDisconnected;
            public event Action<int, byte[]> Received;

            public void Send(int peerId, byte[] payload) => _t._toClient.Enqueue(payload);

            public void Poll()
            {
                if (!_connectRaised) { _connectRaised = true; PeerConnected?.Invoke(ClientPeerId); }
                while (_t._toServer.Count > 0)
                    Received?.Invoke(ClientPeerId, _t._toServer.Dequeue());
            }

            public void RaiseDisconnect() => PeerDisconnected?.Invoke(ClientPeerId);
        }

        private sealed class ClientEnd : IClientTransport
        {
            private readonly LoopbackTransport _t;
            private bool _connectRaised;
            public ClientEnd(LoopbackTransport t) => _t = t;

            public event Action Connected;
            public event Action Disconnected;
            public event Action<byte[]> Received;

            public void Send(byte[] payload) => _t._toServer.Enqueue(payload);

            public void Poll()
            {
                if (!_connectRaised) { _connectRaised = true; Connected?.Invoke(); }
                while (_t._toClient.Count > 0)
                    Received?.Invoke(_t._toClient.Dequeue());
            }

            public void RaiseDisconnect() => Disconnected?.Invoke();
        }
    }
}
