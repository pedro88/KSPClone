using System;

namespace KSPClone.SimCore
{
    /// <summary>
    /// Immutable gravitational body. Stores μ (GM), SOI radius, parent id,
    /// and its own orbit around the parent for world-frame position lookup.
    /// All values in SI (metres, seconds, m³/s²).
    /// </summary>
    public sealed class CelestialBody
    {
        public CelestialBodyId Id { get; }
        public string Name { get; }
        public double GravParameterMu { get; }
        public double SoiRadius { get; }
        public CelestialBodyId? ParentId { get; }
        public Orbit? OrbitAroundParent { get; }

        public CelestialBody(
            CelestialBodyId id,
            string name,
            double gravParameterMu,
            double soiRadius,
            CelestialBodyId? parentId = null,
            Orbit? orbitAroundParent = null)
        {
            if (gravParameterMu <= 0.0) throw new ArgumentOutOfRangeException(nameof(gravParameterMu), "μ must be positive.");
            if (soiRadius <= 0.0) throw new ArgumentOutOfRangeException(nameof(soiRadius), "SOI radius must be positive.");

            Id = id;
            Name = name ?? throw new ArgumentNullException(nameof(name));
            GravParameterMu = gravParameterMu;
            SoiRadius = soiRadius;
            ParentId = parentId;
            OrbitAroundParent = orbitAroundParent;
        }
    }
}