using System;

namespace KSPClone.SimCore
{
    /// <summary>
    /// Closed-form Kepler propagator. Given an <see cref="Orbit"/> and a
    /// game-time, returns (position, velocity) in the parent body's
    /// inertial frame. No time-stepping; one evaluation per call.
    ///
    /// Implements M001 (René Schwarz) elliptic path: advance mean
    /// anomaly, solve Kepler's equation for eccentric anomaly, build the
    /// perifocal state, rotate to the inertial frame via 3-1-3 (Ω, i, ω).
    ///
    /// M0 scope: elliptic only (e in [0, 1)). Hyperbolic/parabolic are
    /// deferred to a later slice that will switch to the universal-
    /// variable formulation (§3 of docs/references/orbital-mechanics.md).
    /// </summary>
    public static class KeplerPropagator
    {
        private const double KeplerTolerance = 1e-12;
        private const int    KeplerMaxIters  = 50;

        /// <summary>
        /// Returns (position, velocity) in the parent body's inertial
        /// frame at <paramref name="gameTime"/>. μ is resolved from
        /// <paramref name="registry"/> via <see cref="Orbit.ParentBody"/>.
        /// The caller is responsible for offsetting by the parent's world
        /// position if a world-frame vector is required — see
        /// <see cref="WorldFrameStateAt"/> for the convenience wrapper.
        /// </summary>
        public static (Vector3d position, Vector3d velocity) StateAt(Orbit orbit, double gameTime, BodyRegistry registry)
        {
            var (relPos, relVel, _, _) = WorldFrameStateAt(orbit, gameTime, registry);
            return (relPos, relVel);
        }

        /// <summary>
        /// Returns the state in the parent's inertial frame AND in the
        /// world frame in one call. World frame = parent world position
        /// + relative position; world velocity = relative velocity (the
        /// parent body's own velocity is dropped for M0 because the
        /// static tree in T06 puts every body at the origin; a parent
        /// that actually orbits will need parent-frame velocity added
        /// in a later slice).
        /// </summary>
        public static (Vector3d parentFramePos, Vector3d parentFrameVel, Vector3d worldPos, Vector3d worldVel)
            WorldFrameStateAt(Orbit orbit, double gameTime, BodyRegistry registry)
        {
            var parent = registry.Get(orbit.ParentBody);
            var mu = parent.GravParameterMu;

            if (orbit.SemiMajorAxis <= 0.0 || orbit.Eccentricity >= 1.0)
                throw new NotSupportedException(
                    "M0 KeplerPropagator only supports elliptic orbits (a > 0, 0 <= e < 1). " +
                    "Hyperbolic/parabolic will be added via universal variables in a later slice.");

            var n = orbit.MeanMotion(mu);
            var dt = gameTime - orbit.EpochGameTime;
            var m = WrapTwoPi(orbit.MeanAnomalyAtEpoch + n * dt);

            var eAnom = SolveKeplerElliptic(m, orbit.Eccentricity);
            var trueAnom = EccentricToTrueAnomaly(eAnom, orbit.Eccentricity);
            var r = orbit.SemiMajorAxis * (1.0 - orbit.Eccentricity * Math.Cos(eAnom));

            var cosNu = Math.Cos(trueAnom);
            var sinNu = Math.Sin(trueAnom);
            var sqrtMuA = Math.Sqrt(mu * orbit.SemiMajorAxis);
            var perifPos = new Vector3d(r * cosNu, r * sinNu, 0.0);
            var perifVel = (sqrtMuA / r) * new Vector3d(
                -Math.Sin(eAnom),
                 Math.Sqrt(1.0 - orbit.Eccentricity * orbit.Eccentricity) * Math.Cos(eAnom),
                 0.0);

            var rot = RotationMatrix313(orbit.LongitudeOfAscendingNode, orbit.Inclination, orbit.ArgumentOfPeriapsis);
            var relPos = rot.Multiply(perifPos);
            var relVel = rot.Multiply(perifVel);

            var parentWorldPos = registry.WorldPositionOf(orbit.ParentBody, gameTime);
            return (relPos, relVel, parentWorldPos + relPos, relVel);
        }

        /// <summary>
        /// Solve M = E - e sin E for eccentric anomaly E via Newton-Raphson.
        /// Initial guess per orbital-mechanics.md §2: E0 = M for low e;
        /// E0 = π for e > 0.8 (avoids the singular corner).
        /// </summary>
        public static double SolveKeplerElliptic(double m, double eccentricity)
        {
            m = WrapTwoPi(m);
            var e = eccentricity;

            var eAnom = e > 0.8 ? Math.PI : m;
            for (int i = 0; i < KeplerMaxIters; i++)
            {
                var f = eAnom - e * Math.Sin(eAnom) - m;
                var fp = 1.0 - e * Math.Cos(eAnom);
                var delta = f / fp;
                eAnom -= delta;
                if (Math.Abs(delta) < KeplerTolerance)
                    return WrapTwoPi(eAnom);
            }
            throw new InvalidOperationException(
                $"Kepler solver failed to converge in {KeplerMaxIters} iterations (m={m:R}, e={e:R}).");
        }

        public static double EccentricToTrueAnomaly(double eAnom, double eccentricity)
        {
            var cosE = Math.Cos(eAnom);
            var sinE = Math.Sin(eAnom);
            var sqrt1pe = Math.Sqrt(1.0 + eccentricity);
            var sqrt1me = Math.Sqrt(1.0 - eccentricity);
            return 2.0 * Math.Atan2(sqrt1pe * sinE, sqrt1me * cosE);
        }

        public static double WrapTwoPi(double angle)
        {
            const double twoPi = 2.0 * Math.PI;
            var x = angle % twoPi;
            if (x < 0.0) x += twoPi;
            return x;
        }

        private static Mat3 RotationMatrix313(double raan, double inc, double argp)
        {
            var cO = Math.Cos(raan); var sO = Math.Sin(raan);
            var cI = Math.Cos(inc);  var sI = Math.Sin(inc);
            var cW = Math.Cos(argp); var sW = Math.Sin(argp);
            // Rz(-Ω) · Rx(-i) · Rz(-ω)
            return new Mat3(
                cO * cW - sO * cI * sW,  -cO * sW - sO * cI * cW,  sO * sI,
                sO * cW + cO * cI * sW,  -sO * sW + cO * cI * cW,  -cO * sI,
                sI * sW,                  sI * cW,                  cI);
        }

        private readonly struct Mat3
        {
            public readonly double M00, M01, M02, M10, M11, M12, M20, M21, M22;
            public Mat3(double m00, double m01, double m02,
                        double m10, double m11, double m12,
                        double m20, double m21, double m22)
            { M00=m00;M01=m01;M02=m02;M10=m10;M11=m11;M12=m12;M20=m20;M21=m21;M22=m22; }

            public Vector3d Multiply(Vector3d v) => new(
                M00 * v.X + M01 * v.Y + M02 * v.Z,
                M10 * v.X + M11 * v.Y + M12 * v.Z,
                M20 * v.X + M21 * v.Y + M22 * v.Z);
        }
    }
}