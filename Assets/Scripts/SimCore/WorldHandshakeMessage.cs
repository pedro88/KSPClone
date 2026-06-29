using System.Collections.Generic;

namespace KSPClone.SimCore
{
    /// <summary>
    /// Sent by the server to a client immediately after connect,
    /// carrying the minimal world state the client needs to render
    /// (NET-1). Wire-agnostic: a transport adapter (LiteNetLib, etc.)
    /// is responsible for serializing this.
    /// </summary>
    public sealed class WorldHandshakeMessage
    {
        public double GameTimeSeconds { get; }
        public IReadOnlyList<HandshakeVessel> Vessels { get; }

        public WorldHandshakeMessage(double gameTimeSeconds, IReadOnlyList<HandshakeVessel> vessels)
        {
            GameTimeSeconds = gameTimeSeconds;
            Vessels = vessels;
        }
    }

    /// <summary>
    /// Per-vessel payload in the handshake. Orbital elements are sent
    /// (not the state vector) because they are cheaper, stable, and
    /// allow the client to evaluate closed-form propagation itself
    /// (matches the on-rails replication channel of netcode §7).
    /// </summary>
    public readonly struct HandshakeVessel
    {
        public VesselId Id { get; }
        public Orbit Orbit { get; }
        public bool OnRails { get; }

        public HandshakeVessel(VesselId id, Orbit orbit, bool onRails)
        {
            Id = id;
            Orbit = orbit;
            OnRails = onRails;
        }
    }

    /// <summary>
    /// Client-side mirror of the world populated from the handshake
    /// and from snapshot updates. Read-only from the client's POV;
    /// the server is the only writer.
    /// </summary>
    public sealed class ClientWorldModel
    {
        public double GameTimeSeconds { get; private set; }
        public IReadOnlyDictionary<VesselId, HandshakeVessel> Vessels => _vessels;

        private readonly Dictionary<VesselId, HandshakeVessel> _vessels = new();

        public void ApplyHandshake(WorldHandshakeMessage handshake)
        {
            GameTimeSeconds = handshake.GameTimeSeconds;
            _vessels.Clear();
            foreach (var v in handshake.Vessels)
                _vessels[v.Id] = v;
        }

        public void ApplyVesselState(VesselId id, HandshakeVessel vessel)
        {
            _vessels[id] = vessel;
        }
    }
}