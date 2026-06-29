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
    /// Empty-Pilot fallback (CREW-4, M2-T08): SAS-style attitude hold. Damps the
    /// vessel's angular rate toward zero so an uncrewed Pilot does not let the
    /// vessel tumble. Dumber than a human: it kills rotation (holds the current
    /// attitude in the limit) and never issues a maneuver or touches throttle.
    ///
    /// It emits an attitude *rate* command opposing the measured angular
    /// velocity; the integrator turns that into a damping torque
    /// (dω/dt ≈ −gain·ω). Full hold-to-a-captured-orientation needs an
    /// authoritative orientation field, which SimCore does not yet carry —
    /// rate damping is the representable, honest M2 SAS.
    /// </summary>
    public sealed class PilotSasAutomation : IStationAutomation
    {
        public double DampingGain { get; }

        public PilotSasAutomation(double dampingGain = 2.0) => DampingGain = dampingGain;

        public void Drive(Vessel vessel, double dtSeconds)
        {
            var w = vessel.CachedAngularVelocity ?? Vector3d.Zero;
            vessel.AttitudeCommand = new Vector3d(-DampingGain * w.X, -DampingGain * w.Y, -DampingGain * w.Z);
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
