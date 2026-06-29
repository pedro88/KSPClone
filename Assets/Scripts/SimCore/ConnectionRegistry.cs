#nullable enable annotations

using System;
using System.Collections.Generic;

namespace KSPClone.SimCore
{
    /// <summary>
    /// Server-side record for a connected player. Dev-stub: no auth in
    /// M0 (per plan P-3). The TransportAddress is opaque to the Sim core;
    /// the transport layer sets it.
    /// </summary>
    public sealed class PlayerSession
    {
        public PlayerId Id { get; }
        public DateTimeOffset ConnectedAt { get; }

        public PlayerSession(PlayerId id)
        {
            Id = id;
            ConnectedAt = DateTimeOffset.UtcNow;
        }
    }

    /// <summary>
    /// The server's authoritative list of connected players. All reads
    /// and mutations are server-side (NET-1). Connect/disconnect events
    /// fire on a single mutator thread so subscribers can react
    /// synchronously (e.g. the warp FSM in T16/T20).
    /// </summary>
    public sealed class ConnectionRegistry
    {
        public int ConnectedCount => _sessions.Count;
        public IReadOnlyCollection<PlayerSession> All => _sessions.Values;

        public event Action<PlayerSession>? PlayerConnected;
        public event Action<PlayerSession>? PlayerDisconnected;

        private readonly Dictionary<PlayerId, PlayerSession> _sessions = new();

        public PlayerSession Add(PlayerSession session)
        {
            _sessions[session.Id] = session;
            PlayerConnected?.Invoke(session);
            return session;
        }

        public PlayerSession AddNew()
        {
            var s = new PlayerSession(PlayerId.New());
            return Add(s);
        }

        public bool Remove(PlayerId id)
        {
            if (!_sessions.TryGetValue(id, out var s)) return false;
            _sessions.Remove(id);
            PlayerDisconnected?.Invoke(s);
            return true;
        }

        public bool TryGet(PlayerId id, out PlayerSession session) =>
            _sessions.TryGetValue(id, out session!);

        public bool Contains(PlayerId id) => _sessions.ContainsKey(id);
    }
}