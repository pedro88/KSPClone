#nullable enable annotations

using System;
using System.Collections.Generic;

namespace KSPClone.Construction
{
    /// <summary>
    /// Which players are currently editing which Design (M3-T04). A player joins a
    /// Design to receive its op broadcasts and leaves (or disconnects) to stop.
    /// Server-side; engine-agnostic (players are raw Guids, Art. 7).
    /// </summary>
    public sealed class DesignEditorSessions
    {
        private readonly Dictionary<DesignId, HashSet<Guid>> _members = new();
        private static readonly IReadOnlyCollection<Guid> Empty = Array.Empty<Guid>();

        public bool Join(DesignId designId, Guid player)
        {
            if (!_members.TryGetValue(designId, out var set))
                _members[designId] = set = new HashSet<Guid>();
            return set.Add(player);
        }

        public bool Leave(DesignId designId, Guid player) =>
            _members.TryGetValue(designId, out var set) && set.Remove(player);

        public IReadOnlyCollection<Guid> Members(DesignId designId) =>
            _members.TryGetValue(designId, out var set) ? set : Empty;

        public bool IsMember(DesignId designId, Guid player) =>
            _members.TryGetValue(designId, out var set) && set.Contains(player);

        /// <summary>Drop a player from every Design (on disconnect); returns the designs affected — the lock layer auto-releases their locks (M3-T06).</summary>
        public IReadOnlyList<DesignId> LeaveAll(Guid player)
        {
            var affected = new List<DesignId>();
            foreach (var kv in _members)
                if (kv.Value.Remove(player)) affected.Add(kv.Key);
            return affected;
        }
    }
}
