#nullable enable annotations

using System;
using System.Collections.Generic;

namespace KSPClone.SimCore
{
    public sealed class SimWorld
    {
        public MasterClock Clock { get; }
        public BodyRegistry? Bodies { get; }
        public IReadOnlyDictionary<VesselId, Vessel> Vessels => _vessels;

        public event Action<double>? TickRecorded;

        private readonly Dictionary<VesselId, Vessel> _vessels;

        public SimWorld(BodyRegistry? bodies = null)
        {
            Clock = new MasterClock();
            Bodies = bodies;
            _vessels = new Dictionary<VesselId, Vessel>();
        }

        public void RegisterVessel(Vessel vessel)
        {
            _vessels[vessel.Id] = vessel;
        }

        /// <summary>
        /// Remove a vessel from the world. Used by the docking merger
        /// when an absorbed vessel is consolidated into its survivor
        /// (PHYS-5).
        /// </summary>
        public bool UnregisterVessel(VesselId id) => _vessels.Remove(id);

        public void Tick(double dtSeconds)
        {
            Clock.Advance(dtSeconds);
            SyncOnRailsVessels();
            TickRecorded?.Invoke(dtSeconds);
        }

        private void SyncOnRailsVessels()
        {
            if (Bodies is null) return;
            var t = Clock.GameTimeSeconds;
            foreach (var v in _vessels.Values)
            {
                if (!v.OnRails) continue;
                v.VesselClockSeconds = t;
                try
                {
                    var (_, _, worldPos, worldVel) =
                        KeplerPropagator.WorldFrameStateAt(v.Orbit, t, Bodies);
                    v.CachedWorldPosition = worldPos;
                    v.CachedWorldVelocity = worldVel;
                }
                catch (NotSupportedException)
                {
                    // Hyperbolic/parabolic deferred; leave cached state as null.
                }
            }
        }
    }
}