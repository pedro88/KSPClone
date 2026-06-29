using System;
using System.Collections.Generic;

namespace KSPClone.SimCore
{
    /// <summary>
    /// Scans every on-rails vessel for upcoming SOI crossings and
    /// populates the <see cref="PoiRegistry"/>. The earliest registered
    /// POI is what <see cref="WarpStateMachine"/> uses to auto-limit
    /// the warp end time (TIME-4).
    /// </summary>
    public sealed class PoiScanner
    {
        private readonly SimWorld _world;
        private readonly BodyRegistry _bodies;
        private readonly PoiRegistry _pois;
        private readonly double _lookAheadSeconds;

        public PoiScanner(SimWorld world, BodyRegistry bodies, PoiRegistry pois, double lookAheadSeconds = 7.0 * 86400.0)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _bodies = bodies ?? throw new ArgumentNullException(nameof(bodies));
            _pois = pois ?? throw new ArgumentNullException(nameof(pois));
            _lookAheadSeconds = lookAheadSeconds;
        }

        /// <summary>
        /// Clears the registry, then re-scans every on-rails vessel for
        /// its next SOI crossing within the look-ahead. Returns the
        /// number of POIs registered. Off-rails vessels are skipped
        /// (their POIs come from physics-bubble events in M1).
        /// </summary>
        public int RescanAll()
        {
            _pois.Clear();
            var now = _world.Clock.GameTimeSeconds;
            var count = 0;
            foreach (var v in _world.Vessels.Values)
            {
                if (!v.OnRails) continue;
                var next = SoiScanner.ScanNext(v, _bodies, now, _lookAheadSeconds);
                if (next is Poi p)
                {
                    _pois.Add(p);
                    count++;
                }
            }
            return count;
        }
    }
}