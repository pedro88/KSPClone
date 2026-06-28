#nullable enable annotations

using System;

namespace KSPClone.SimCore
{
    /// <summary>
    /// Minimal hand-rolled prediction step: applies throttle as a
    /// linear acceleration along the vessel's local thrust axis
    /// (X for simplicity in M1) and attitude as an angular velocity
    /// delta. Gravity is omitted — the predictor focuses on
    /// reproducing the server's response to throttle/attitude inputs
    /// for the prediction loop. A future slice wires the full
    /// server-side integrator here (ADR-0006).
    /// </summary>
    public sealed class TrivialPredictionStep : IPredictionStep
    {
        public double FixedDt { get; }
        public double ThrustAccelerationPerThrottleUnit { get; }
        public double InertiaKg { get; }

        public TrivialPredictionStep(double fixedDt = 1.0 / 60.0, double thrustAccel = 10.0, double inertiaKg = 1000.0)
        {
            FixedDt = fixedDt;
            ThrustAccelerationPerThrottleUnit = thrustAccel;
            InertiaKg = inertiaKg;
        }

        public PredictedVesselState Step(PredictedVesselState current, PilotInputMessage input, long newClientTick)
        {
            var a = ThrustAccelerationPerThrottleUnit * input.Throttle;
            var newVel = current.Velocity + new Vector3d(a * FixedDt, 0, 0);
            var newPos = current.Position + newVel * FixedDt;

            var angVel = new Vector3d(input.PitchRate, input.YawRate, input.RollRate);
            return new PredictedVesselState(newPos, newVel, angVel, newClientTick);
        }
    }
}