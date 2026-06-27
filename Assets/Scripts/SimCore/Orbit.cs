using System;

namespace KSPClone.SimCore
{
    /// <summary>
    /// Six classical Keplerian elements + epoch, relative to the current
    /// SOI body's inertial frame. All angles in radians, distances in metres.
    /// Frame convention (T07): right-handed z-up, x = vernal-equinox direction.
    /// </summary>
    public sealed class Orbit
    {
        public double SemiMajorAxis { get; }
        public double Eccentricity { get; }
        public double Inclination { get; }
        public double LongitudeOfAscendingNode { get; }
        public double ArgumentOfPeriapsis { get; }
        public double MeanAnomalyAtEpoch { get; }
        public double EpochGameTime { get; }
        public CelestialBodyId ParentBody { get; }

        public Orbit(
            double semiMajorAxis,
            double eccentricity,
            double inclination,
            double longitudeOfAscendingNode,
            double argumentOfPeriapsis,
            double meanAnomalyAtEpoch,
            double epochGameTime,
            CelestialBodyId parentBody)
        {
            if (eccentricity < 0.0)
                throw new ArgumentOutOfRangeException(nameof(eccentricity), "e must be >= 0; hyperbolic is not in M0 scope.");
            SemiMajorAxis = semiMajorAxis;
            Eccentricity = eccentricity;
            Inclination = inclination;
            LongitudeOfAscendingNode = longitudeOfAscendingNode;
            ArgumentOfPeriapsis = argumentOfPeriapsis;
            MeanAnomalyAtEpoch = meanAnomalyAtEpoch;
            EpochGameTime = epochGameTime;
            ParentBody = parentBody;
        }

        /// <summary>
        /// Mean motion n = sqrt(μ/a³) for an elliptic orbit.
        /// Returns NaN if a ≤ 0 (parabolic/hyperbolic, not in M0).
        /// </summary>
        public double MeanMotion(double mu)
        {
            if (SemiMajorAxis <= 0.0 || mu <= 0.0) return double.NaN;
            return System.Math.Sqrt(mu / (SemiMajorAxis * SemiMajorAxis * SemiMajorAxis));
        }

        /// <summary>
        /// Orbital period (s). NaN for non-elliptic.
        /// </summary>
        public double Period(double mu)
        {
            var n = MeanMotion(mu);
            return double.IsNaN(n) ? double.NaN : 2.0 * System.Math.PI / n;
        }
    }
}