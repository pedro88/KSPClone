#nullable enable annotations

using System;

namespace KSPClone.SimCore
{
    /// <summary>
    /// Double-precision 3D vector for orbital math (engine-agnostic).
    /// UnityEngine.Vector3 is float + Unity-bound; we keep our own type to
    /// satisfy Constitution Art. 3 (state in doubles) and the noEngineReferences
    /// contract on SimCore.
    /// </summary>
    public readonly struct Vector3d : IEquatable<Vector3d>
    {
        public readonly double X;
        public readonly double Y;
        public readonly double Z;

        public static readonly Vector3d Zero = new(0, 0, 0);

        public Vector3d(double x, double y, double z) { X = x; Y = y; Z = z; }

        public double LengthSquared => X * X + Y * Y + Z * Z;
        public double Length => System.Math.Sqrt(LengthSquared);

        public static Vector3d operator +(Vector3d a, Vector3d b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Vector3d operator -(Vector3d a, Vector3d b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static Vector3d operator -(Vector3d a) => new(-a.X, -a.Y, -a.Z);
        public static Vector3d operator *(Vector3d a, double s) => new(a.X * s, a.Y * s, a.Z * s);
        public static Vector3d operator *(double s, Vector3d a) => a * s;
        public static Vector3d operator /(Vector3d a, double s) => new(a.X / s, a.Y / s, a.Z / s);

        public static double Dot(Vector3d a, Vector3d b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
        public static Vector3d Cross(Vector3d a, Vector3d b) =>
            new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);

        public Vector3d Normalized() => this * (1.0 / Length);

        public bool Equals(Vector3d other) => X == other.X && Y == other.Y && Z == other.Z;
        public override bool Equals(object? obj) => obj is Vector3d v && Equals(v);
        public override int GetHashCode() => HashCode.Combine(X, Y, Z);
        public override string ToString() => $"({X:R}, {Y:R}, {Z:R})";
    }
}