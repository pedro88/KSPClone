#nullable enable annotations

using System.Collections.Generic;

namespace KSPClone.Construction
{
    /// <summary>
    /// Live Designs held in memory, keyed by design id, each paired with its
    /// append-only <see cref="EditOpLog"/> (M3-T03). The server owns this; it is
    /// the single authority for every Design's edits (Art. 1).
    /// </summary>
    public sealed class DesignRegistry
    {
        private readonly Dictionary<DesignId, Design> _designs = new();
        private readonly Dictionary<DesignId, EditOpLog> _logs = new();

        /// <summary>Create a fresh Design (seeded with a root part) and register it with an empty log.</summary>
        public Design Create(string name, PartTypeId rootPartType)
        {
            var d = Design.Create(DesignId.New(), name, rootPartType);
            _designs[d.Id] = d;
            _logs[d.Id] = new EditOpLog();
            return d;
        }

        /// <summary>Register an already-built Design (e.g. hydrated from persistence) with a log.</summary>
        public void Register(Design design, EditOpLog? log = null)
        {
            _designs[design.Id] = design;
            _logs[design.Id] = log ?? new EditOpLog();
        }

        public bool TryGet(DesignId id, out Design design) => _designs.TryGetValue(id, out design!);
        public bool TryGetLog(DesignId id, out EditOpLog log) => _logs.TryGetValue(id, out log!);
        public bool Contains(DesignId id) => _designs.ContainsKey(id);
        public IEnumerable<DesignId> DesignIds => _designs.Keys;
    }
}
