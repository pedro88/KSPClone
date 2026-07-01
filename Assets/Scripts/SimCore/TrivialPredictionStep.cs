#nullable enable annotations

using System;

namespace KSPClone.SimCore
{
    /// <summary>
    /// Minimal hand-rolled prediction step matching the server's arcade
    /// hand-flying model (ADR-0019): the pilot input's (pitch, yaw, roll) is a
    /// *target* body-frame angular rate, so the predicted orientation integrates
    /// that rate directly (and holds when the input is zero); thrust is a linear
    /// acceleration along the vessel's local +Y rotated into world by the current
    /// orientation, so tilting the craft turns the thrust just like the server.
    /// Gravity is omitted — reconciliation corrects the residual (NET-3).
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
            // Attitude: input is a target body-frame rate; integrate orientation
            // by it and carry it as the (kinematic) angular velocity.
            var bodyRate = new Vector3d(input.PitchRate, input.YawRate, input.RollRate);
            var newOrient = (current.Orientation * Quaterniond.FromAngularVelocity(bodyRate, FixedDt)).Normalized();
            var worldAngVel = newOrient.Rotate(bodyRate);

            // Thrust along the vessel's local +Y, rotated into world by the
            // current attitude — tilt the craft and the thrust tilts with it.
            var a = ThrustAccelerationPerThrottleUnit * input.Throttle;
            var thrustWorld = newOrient.Rotate(new Vector3d(0, 1, 0));
            var newVel = current.Velocity + thrustWorld * (a * FixedDt);
            var newPos = current.Position + newVel * FixedDt;

            return new PredictedVesselState(newPos, newVel, worldAngVel, newOrient, newClientTick);
        }
    }
}