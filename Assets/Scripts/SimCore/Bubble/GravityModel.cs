#nullable enable annotations

using System;

namespace KSPClone.SimCore
{
    /// <summary>
    /// Newtonian gravity from a single SOI body (PHYS-4, ORBIT-1).
    /// Engine-agnostic: returns an acceleration vector in the same
    /// frame as the input position (world or bubble-local — caller
    /// decides which).
    ///
    /// Formula: g(r) = -μ * r̂ / |r|²
    /// where r is the displacement from the gravitating body's centre
    /// to the point where acceleration is evaluated.
    /// </summary>
    public static class GravityModel
    {
        public const double MinDistanceMeters = 1.0;

        /// <summary>
        /// Compute gravitational acceleration at <paramref name="position"/>
        /// from a body at <paramref name="bodyCentrePosition"/> with
        /// <paramref name="gravParameterMu"/>. Returns the zero vector
        /// when the point sits at or inside <see cref="MinDistanceMeters"/>
        /// of the body centre (singularity guard — the rigid body should
        /// never be inside a planet's surface in M1).
        /// </summary>
        public static Vector3d Acceleration(Vector3d position, Vector3d bodyCentrePosition, double gravParameterMu)
        {
            var r = position - bodyCentrePosition;
            var distSq = r.LengthSquared;
            if (distSq < MinDistanceMeters * MinDistanceMeters) return Vector3d.Zero;
            var dist = Math.Sqrt(distSq);
            return r * (-gravParameterMu / (distSq * dist));
        }
    }
}