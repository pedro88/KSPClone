#nullable enable annotations

using System;
using System.Collections.Generic;

namespace KSPClone.Construction
{
    /// <summary>
    /// One attachment socket on a part type, addressed by a stable key (e.g.
    /// "top", "bottom", "radial-1"). <see cref="LocalPose"/> is where a child
    /// attached here sits, in the parent's frame.
    /// </summary>
    public readonly struct AttachPoint
    {
        public readonly string Key;
        public readonly PartPose LocalPose;

        public AttachPoint(string key, PartPose localPose)
        {
            Key = key ?? throw new ArgumentNullException(nameof(key));
            LocalPose = localPose;
        }
    }

    /// <summary>
    /// Immutable definition of a part *type* (read-only reference data). Carries
    /// its attach sockets and mass; resource slots arrive with the propulsion
    /// slice. Never holds per-instance or world state.
    /// </summary>
    public sealed class PartType
    {
        public PartTypeId Id { get; }
        public string DisplayName { get; }
        public double DryMassKg { get; }
        public IReadOnlyList<AttachPoint> AttachPoints { get; }

        // Optional propulsion (engine parts): 0 thrust = not an engine.
        public double EngineThrustN { get; }
        public double EngineIspS { get; }
        // Optional propellant this part holds (tanks and engines with built-in fuel).
        public double PropellantKg { get; }

        // Minimal geometry (metres) for stacking layout + presentation.
        public double HeightM { get; }
        public double RadiusM { get; }

        public PartType(
            PartTypeId id, double dryMassKg,
            IReadOnlyList<AttachPoint>? attachPoints = null,
            string? displayName = null,
            double engineThrustN = 0.0, double engineIspS = 0.0, double propellantKg = 0.0,
            double heightM = 1.0, double radiusM = 0.6)
        {
            Id = id;
            DisplayName = displayName ?? id.Value;
            DryMassKg = dryMassKg;
            AttachPoints = attachPoints ?? Array.Empty<AttachPoint>();
            EngineThrustN = engineThrustN;
            EngineIspS = engineIspS;
            PropellantKg = propellantKg;
            HeightM = heightM;
            RadiusM = radiusM;
        }

        public bool IsEngine => EngineThrustN > 0.0;

        public bool HasAttachPoint(string key)
        {
            foreach (var ap in AttachPoints)
                if (string.Equals(ap.Key, key, StringComparison.Ordinal)) return true;
            return false;
        }

        public bool TryAttachPoint(string key, out AttachPoint point)
        {
            foreach (var ap in AttachPoints)
                if (string.Equals(ap.Key, key, StringComparison.Ordinal)) { point = ap; return true; }
            point = default;
            return false;
        }
    }

    /// <summary>
    /// Read-only registry of part types available to Designs. Pure lookup data;
    /// the tech tree (M5) gates which entries a program may use, but that gating
    /// lives elsewhere — the catalog itself is just the parts that exist.
    /// </summary>
    public sealed class PartCatalog
    {
        private readonly Dictionary<PartTypeId, PartType> _types = new();

        public PartCatalog(IEnumerable<PartType>? types = null)
        {
            if (types is null) return;
            foreach (var t in types) _types[t.Id] = t;
        }

        public bool TryGet(PartTypeId id, out PartType type) => _types.TryGetValue(id, out type!);
        public bool Contains(PartTypeId id) => _types.ContainsKey(id);
        public int Count => _types.Count;
        /// <summary>All part types (for catalogue UI).</summary>
        public IEnumerable<PartType> All => _types.Values;
    }
}
