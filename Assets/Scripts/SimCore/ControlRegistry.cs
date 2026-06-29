#nullable enable annotations

using System.Collections.Generic;

namespace KSPClone.SimCore
{
    /// <summary>
    /// Server-authoritative record of which player occupies which station of
    /// which vessel (ADR-0016, Constitution Art. 6). The source of truth for
    /// pilot-input authority (only the Pilot occupant's input is applied) and
    /// for the occupancy signal the demotion/suspension passes consult (a
    /// vessel with any station occupied is "attended").
    ///
    /// Dev stub for the open identity/join item: a player is trusted to be
    /// whoever the server assigned on connect; there is no authentication.
    /// Mirrors the rules in CONTEXT: a player occupies at most one station at
    /// a time, and two players cannot occupy the same station at once.
    /// </summary>
    public sealed class ControlRegistry
    {
        private readonly Dictionary<(VesselId, Station), PlayerId> _byStation = new();
        private readonly Dictionary<PlayerId, (VesselId, Station)> _byPlayer = new();

        /// <summary>
        /// Occupy a station. Vacates the player's previous station first (one
        /// at a time). Returns false if the station is already held by a
        /// different player (contention is refused, not contended).
        /// </summary>
        public bool Occupy(PlayerId player, VesselId vessel, Station station)
        {
            var key = (vessel, station);
            if (_byStation.TryGetValue(key, out var current))
                return current.Equals(player); // idempotent for the same player, refused for another

            if (_byPlayer.TryGetValue(player, out var prev))
                _byStation.Remove(prev);

            _byStation[key] = player;
            _byPlayer[player] = key;
            return true;
        }

        /// <summary>Vacate whatever station this player occupies (on disconnect or hot-swap).</summary>
        public bool Vacate(PlayerId player)
        {
            if (!_byPlayer.TryGetValue(player, out var key)) return false;
            _byPlayer.Remove(player);
            _byStation.Remove(key);
            return true;
        }

        /// <summary>The player occupying a station, or null if it is free.</summary>
        public PlayerId? Owner(VesselId vessel, Station station)
            => _byStation.TryGetValue((vessel, station), out var p) ? p : (PlayerId?)null;

        /// <summary>True if any station of the vessel is occupied (the occupancy signal for SUSP-2/3).</summary>
        public bool IsOccupied(VesselId vessel)
        {
            foreach (var key in _byStation.Keys)
                if (key.Item1.Equals(vessel)) return true;
            return false;
        }
    }
}
