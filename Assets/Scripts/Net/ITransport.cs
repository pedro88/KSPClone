using System;

namespace KSPClone.Net
{
    /// <summary>
    /// Server side of a poll-based transport. Byte payloads only — the codec
    /// (WireCodec) turns them into messages. A peer is an opaque int id. The
    /// real implementation (LiteNetLib) and the in-process LoopbackTransport
    /// both satisfy this so the ServerNetHost is transport-agnostic.
    /// </summary>
    public interface IServerTransport
    {
        event Action<int> PeerConnected;
        event Action<int> PeerDisconnected;
        event Action<int, byte[]> Received;
        void Send(int peerId, byte[] payload);
        void Poll();
    }

    /// <summary>Client side of the poll-based transport.</summary>
    public interface IClientTransport
    {
        event Action Connected;
        event Action Disconnected;
        event Action<byte[]> Received;
        void Send(byte[] payload);
        void Poll();
    }
}
