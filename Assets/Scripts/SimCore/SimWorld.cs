using System;
using System.Collections.Generic;

namespace KSPClone.SimCore
{
    public sealed class SimWorld
    {
        public MasterClock Clock { get; }
        public IReadOnlyDictionary<VesselId, Vessel> Vessels => _vessels;

        public event Action<double>? TickRecorded;

        private readonly Dictionary<VesselId, Vessel> _vessels;

        public SimWorld()
        {
            Clock = new MasterClock();
            _vessels = new Dictionary<VesselId, Vessel>();
        }

        public void RegisterVessel(Vessel vessel)
        {
            _vessels[vessel.Id] = vessel;
        }

        public void Tick(double dtSeconds)
        {
            Clock.Advance(dtSeconds);
            TickRecorded?.Invoke(dtSeconds);
        }
    }
}