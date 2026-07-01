#nullable enable annotations

using System.Collections.Generic;

namespace KSPClone.SimCore
{
    /// <summary>
    /// The automation that drives a <see cref="Station"/>'s systems while it is
    /// unoccupied (CREW-4). Always "dumber than a human": it keeps a
    /// short-crewed or solo vessel flyable but degraded. Engine-agnostic — it
    /// only writes the vessel's command fields; the integrator applies them.
    /// </summary>
    public interface IStationAutomation
    {
        /// <summary>Drive this station's owned systems for one fixed tick.</summary>
        void Drive(Vessel vessel, double dtSeconds);
    }

    /// <summary>Automation that does nothing — the default for stations with no fallback yet.</summary>
    public sealed class NoOpStationAutomation : IStationAutomation
    {
        public static readonly NoOpStationAutomation Instance = new();
        public void Drive(Vessel vessel, double dtSeconds) { }
    }

    /// <summary>
    /// Empty-Pilot fallback (CREW-4, M2-T08): SAS-style attitude hold. Commands a
    /// zero body rate so an uncrewed Pilot holds attitude instead of tumbling.
    /// Under the kinematic rate control the integrator applies (ADR-0019), a zero
    /// rate command drives the angular velocity to zero — the vessel stops
    /// rotating and holds. Dumber than a human: it never manoeuvres or touches
    /// throttle. (Now that orientation is replicated, a later slice can upgrade
    /// this to hold a *captured* orientation rather than merely zero the rate.)
    /// </summary>
    public sealed class PilotSasAutomation : IStationAutomation
    {
        public void Drive(Vessel vessel, double dtSeconds)
        {
            vessel.AttitudeCommand = Vector3d.Zero; // hold: zero target rate
            // Throttle is left untouched (CREW-4: SAS holds attitude only).
        }
    }

    /// <summary>
    /// Selects each station's input source every fixed tick (M2-T07): a human
    /// when occupied, the station's <see cref="IStationAutomation"/> when empty.
    /// Runs only over active-physics vessels (on-rails/suspended vessels take no
    /// per-tick commands). Occupied stations are never driven by automation, so
    /// a human's input is never overwritten.
    /// </summary>
    public sealed class StationDriver
    {
        private readonly Dictionary<Station, IStationAutomation> _automation;

        public StationDriver(
            IStationAutomation? pilot = null,
            IStationAutomation? engineer = null,
            IStationAutomation? navigator = null)
        {
            _automation = new Dictionary<Station, IStationAutomation>
            {
                { Station.Pilot,     pilot     ?? new PilotSasAutomation() },
                { Station.Engineer,  engineer  ?? NoOpStationAutomation.Instance },
                { Station.Navigator, navigator ?? NoOpStationAutomation.Instance },
            };
        }

        public void Tick(IEnumerable<Vessel> vessels, ControlRegistry controls, double dtSeconds)
        {
            foreach (var vessel in vessels)
            {
                if (vessel.State != VesselState.ActivePhysics) continue;
                foreach (var kv in _automation)
                {
                    // Drive only unoccupied stations — an occupied station is
                    // sourced from its human, automation suppressed.
                    if (controls.Owner(vessel.Id, kv.Key) is null)
                        kv.Value.Drive(vessel, dtSeconds);
                }
            }
        }
    }
}
