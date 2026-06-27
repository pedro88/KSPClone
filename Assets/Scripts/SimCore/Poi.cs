using System;
using System.Collections.Generic;

namespace KSPClone.SimCore
{
    public enum PoiType
    {
        SoiCrossing = 0,
        ManeuverNode = 1,
        AtmosphereInterface = 2,
    }

    /// <summary>
    /// Point Of Interest. A future game-time at which something
    /// simulationally interesting happens and the warp auto-limit (T19)
    /// must stop on it. Sorted by GameTime.
    /// </summary>
    public readonly struct Poi : IComparable<Poi>
    {
        public PoiType Type { get; }
        public double GameTime { get; }
        public VesselId VesselId { get; }
        public CelestialBodyId FromBody { get; }
        public CelestialBodyId ToBody { get; }

        public Poi(PoiType type, double gameTime, VesselId vesselId, CelestialBodyId fromBody, CelestialBodyId toBody)
        {
            Type = type;
            GameTime = gameTime;
            VesselId = vesselId;
            FromBody = fromBody;
            ToBody = toBody;
        }

        public int CompareTo(Poi other) => GameTime.CompareTo(other.GameTime);
    }

    public sealed class PoiRegistry
    {
        private readonly SortedSet<Poi> _pois = new();
        public IReadOnlyCollection<Poi> All => _pois;

        public void Add(Poi poi) => _pois.Add(poi);
        public void Remove(Poi poi) => _pois.Remove(poi);

        public Poi? EarliestAfter(double gameTime)
        {
            foreach (var p in _pois)
                if (p.GameTime > gameTime) return p;
            return null;
        }

        public void Clear() => _pois.Clear();
    }
}