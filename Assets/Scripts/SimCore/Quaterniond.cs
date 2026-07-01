#nullable enable annotations

using System;

namespace KSPClone.SimCore
{
    /// <summary>
    /// Double-precision unit quaternion (engine-agnostic). Mirrors
    /// <see cref="Vector3d"/>: UnityEngine.Quaternion is float + Unity-bound,
    /// and SimCore carries the noEngineReferences contract (ADR-0009), so the
    /// authoritative vessel orientation lives here. The client converts to
    /// UnityEngine.Quaternion at the render boundary.
    ///
    /// Convention: (X, Y, Z) is the vector part, W the scalar; Hamilton product;
    /// a rotation composes on the right for body-frame deltas
    /// (<c>orientation * bodyDelta</c>) and on the left for world-frame deltas.
    /// </summary>
    public readonly struct Quaterniond : IEquatable<Quaterniond>
    {
        public readonly double X;
        public readonly double Y;
        public readonly double Z;
        public readonly double W;

        public static readonly Quaterniond Identity = new(0, 0, 0, 1);

        public Quaterniond(double x, double y, double z, double w)
        {
            X = x; Y = y; Z = z; W = w;
        }

        public double LengthSquared => X * X + Y * Y + Z * Z + W * W;
        public double Length => Math.Sqrt(LengthSquared);

        public Quaterniond Normalized()
        {
            var len = Length;
            if (len < 1e-12) return Identity;
            var inv = 1.0 / len;
            return new Quaterniond(X * inv, Y * inv, Z * inv, W * inv);
        }

        /// <summary>Hamilton product (a then b applied in a's frame: a * b).</summary>
        public static Quaterniond operator *(Quaterniond a, Quaterniond b) => new(
            a.W * b.X + a.X * b.W + a.Y * b.Z - a.Z * b.Y,
            a.W * b.Y - a.X * b.Z + a.Y * b.W + a.Z * b.X,
            a.W * b.Z + a.X * b.Y - a.Y * b.X + a.Z * b.W,
            a.W * b.W - a.X * b.X - a.Y * b.Y - a.Z * b.Z);

        /// <summary>
        /// Rotation from an angular-velocity vector applied for <paramref name="dt"/>
        /// seconds (axis = ω̂, angle = |ω|·dt). Identity for a near-zero rate.
        /// </summary>
        public static Quaterniond FromAngularVelocity(Vector3d omega, double dt)
        {
            var angle = omega.Length * dt;
            if (angle < 1e-12) return Identity;
            var axis = omega * (1.0 / omega.Length);
            var half = angle * 0.5;
            var s = Math.Sin(half);
            return new Quaterniond(axis.X * s, axis.Y * s, axis.Z * s, Math.Cos(half));
        }

        /// <summary>Rotate a vector by this quaternion (q · v · q⁻¹).</summary>
        public Vector3d Rotate(Vector3d v)
        {
            // t = 2 · (q_vec × v); v' = v + q_w · t + q_vec × t
            var qv = new Vector3d(X, Y, Z);
            var t = Cross(qv, v) * 2.0;
            return v + t * W + Cross(qv, t);
        }

        /// <summary>Spherical linear interpolation, shortest arc.</summary>
        public static Quaterniond Slerp(Quaterniond a, Quaterniond b, double t)
        {
            a = a.Normalized();
            b = b.Normalized();
            var dot = a.X * b.X + a.Y * b.Y + a.Z * b.Z + a.W * b.W;
            if (dot < 0.0) { b = new Quaterniond(-b.X, -b.Y, -b.Z, -b.W); dot = -dot; }
            if (dot > 0.9995)
            {
                // Nearly parallel — linear blend, renormalised.
                return new Quaterniond(
                    a.X + (b.X - a.X) * t,
                    a.Y + (b.Y - a.Y) * t,
                    a.Z + (b.Z - a.Z) * t,
                    a.W + (b.W - a.W) * t).Normalized();
            }
            var theta0 = Math.Acos(dot);
            var theta = theta0 * t;
            var sinTheta0 = Math.Sin(theta0);
            var s0 = Math.Sin(theta0 - theta) / sinTheta0;
            var s1 = Math.Sin(theta) / sinTheta0;
            return new Quaterniond(
                a.X * s0 + b.X * s1,
                a.Y * s0 + b.Y * s1,
                a.Z * s0 + b.Z * s1,
                a.W * s0 + b.W * s1);
        }

        private static Vector3d Cross(Vector3d a, Vector3d b) => new(
            a.Y * b.Z - a.Z * b.Y,
            a.Z * b.X - a.X * b.Z,
            a.X * b.Y - a.Y * b.X);

        public bool Equals(Quaterniond other) =>
            X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z) && W.Equals(other.W);

        public override bool Equals(object? obj) => obj is Quaterniond q && Equals(q);
        public override int GetHashCode() => HashCode.Combine(X, Y, Z, W);
        public override string ToString() => $"({X:F4}, {Y:F4}, {Z:F4}, {W:F4})";
    }
}
