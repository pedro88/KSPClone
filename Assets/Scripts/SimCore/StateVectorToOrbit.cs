using System;

namespace KSPClone.SimCore
{
    /// <summary>
    /// Converts a Cartesian state vector (position, velocity) in the
    /// parent body's inertial frame to classical Keplerian elements.
    /// Implements René Schwarz M002 (state -> elements) including all
    /// quadrant tests. Elliptic only (M0 scope).
    /// </summary>
    public static class StateVectorToOrbit
    {
        public const double Tolerance = 1e-9;

        public static Orbit Convert(Vector3d position, Vector3d velocity, double mu, double gameTime, CelestialBodyId parentBody)
        {
            if (mu <= 0.0) throw new ArgumentOutOfRangeException(nameof(mu));
            var r = position.Length;
            var v = velocity.Length;
            if (r < 1e-3) throw new ArgumentException("Position vector too short for meaningful conversion.");
            if (v < 1e-6) throw new ArgumentException("Velocity vector too short for meaningful conversion.");

            // Angular momentum h = r × v
            var h = Vector3d.Cross(position, velocity);
            var hMag = h.Length;
            if (hMag < 1e-9) throw new InvalidOperationException("Degenerate orbit: h ≈ 0 (radial trajectory).");

            // Node vector n = ẑ × h
            var zHat = new Vector3d(0, 0, 1);
            var n = Vector3d.Cross(zHat, h);
            var nMag = n.Length;

            // Eccentricity vector e = (v × h)/μ − r̂
            var eVec = (Vector3d.Cross(velocity, h) / mu) - (position / r);
            var ecc = eVec.Length;

            // Specific orbital energy → semi-major axis
            var energy = 0.5 * v * v - mu / r;
            if (energy >= 0.0) throw new NotSupportedException("StateVectorToOrbit M0 only supports bound (elliptic) orbits.");
            var a = -mu / (2.0 * energy);

            // Inclination
            var inc = Math.Acos(Math.Clamp(h.Z / hMag, -1.0, 1.0));

            // Longitude of ascending node (undefined when equatorial)
            double raan;
            if (nMag < 1e-12)
                raan = 0.0;
            else
            {
                raan = Math.Acos(Math.Clamp(n.X / nMag, -1.0, 1.0));
                if (n.Y < 0.0) raan = 2.0 * Math.PI - raan;
            }

            // Argument of periapsis (undefined when circular)
            double argp;
            if (nMag < 1e-12 || ecc < 1e-12)
                argp = 0.0;
            else
            {
                var cosArgp = Math.Clamp(Vector3d.Dot(n, eVec) / (nMag * ecc), -1.0, 1.0);
                argp = Math.Acos(cosArgp);
                if (eVec.Z < 0.0) argp = 2.0 * Math.PI - argp;
            }

            // True anomaly (undefined when circular — use 0)
            double trueAnom;
            if (ecc < 1e-12)
                trueAnom = 0.0;
            else
            {
                var cosNu = Math.Clamp(Vector3d.Dot(eVec, position) / (ecc * r), -1.0, 1.0);
                trueAnom = Math.Acos(cosNu);
                if (Vector3d.Dot(position, velocity) < 0.0) trueAnom = 2.0 * Math.PI - trueAnom;
            }

            // Eccentric anomaly from true anomaly
            var eAnom = 2.0 * Math.Atan2(
                Math.Sqrt(1.0 - ecc) * Math.Sin(trueAnom / 2.0),
                Math.Sqrt(1.0 + ecc) * Math.Cos(trueAnom / 2.0));

            // Mean anomaly at epoch
            var m0 = KeplerPropagator.WrapTwoPi(eAnom - ecc * Math.Sin(eAnom));

            return new Orbit(a, ecc, inc, raan, argp, m0, gameTime, parentBody);
        }
    }
}