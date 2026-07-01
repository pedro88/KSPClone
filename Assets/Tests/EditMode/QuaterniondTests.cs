#nullable enable annotations

using System;
using NUnit.Framework;
using KSPClone.SimCore;

namespace KSPClone.SimCore.Tests
{
    /// <summary>
    /// The engine-agnostic double quaternion behind replicated vessel
    /// orientation (ADR-0019): rotation, angular-velocity integration, and slerp.
    /// </summary>
    public sealed class QuaterniondTests
    {
        private const double Tol = 1e-9;

        [Test]
        public void Identity_RotatesVectorUnchanged()
        {
            var v = new Vector3d(1, 2, 3);
            var r = Quaterniond.Identity.Rotate(v);
            Assert.AreEqual(v.X, r.X, Tol);
            Assert.AreEqual(v.Y, r.Y, Tol);
            Assert.AreEqual(v.Z, r.Z, Tol);
        }

        [Test]
        public void FromAngularVelocity_90DegAboutX_MapsYToZ()
        {
            // ω = 1 rad/s about +X for π/2 s → +90° about X: +Y → +Z (right-hand).
            var q = Quaterniond.FromAngularVelocity(new Vector3d(1, 0, 0), Math.PI / 2.0);
            var r = q.Rotate(new Vector3d(0, 1, 0));
            Assert.AreEqual(0.0, r.X, 1e-9);
            Assert.AreEqual(0.0, r.Y, 1e-9);
            Assert.AreEqual(1.0, r.Z, 1e-9);
        }

        [Test]
        public void FromAngularVelocity_ZeroRate_IsIdentity()
        {
            var q = Quaterniond.FromAngularVelocity(Vector3d.Zero, 1.0);
            Assert.IsTrue(q.Equals(Quaterniond.Identity));
        }

        [Test]
        public void IntegratingConstantRate_Composes_ToSingleRotation()
        {
            // Stepping a fixed body rate N times equals one rotation over N·dt.
            var rate = new Vector3d(0, 0, 1.3);
            var dt = 1.0 / 60.0;
            var q = Quaterniond.Identity;
            for (int i = 0; i < 90; i++)
                q = (q * Quaterniond.FromAngularVelocity(rate, dt)).Normalized();

            var oneShot = Quaterniond.FromAngularVelocity(rate, dt * 90);
            var a = q.Rotate(new Vector3d(1, 0, 0));
            var b = oneShot.Rotate(new Vector3d(1, 0, 0));
            Assert.AreEqual(b.X, a.X, 1e-9);
            Assert.AreEqual(b.Y, a.Y, 1e-9);
            Assert.AreEqual(b.Z, a.Z, 1e-9);
        }

        [Test]
        public void Slerp_Endpoints_ReturnInputs()
        {
            var a = Quaterniond.Identity;
            var b = Quaterniond.FromAngularVelocity(new Vector3d(0, 1, 0), 1.0);
            var s0 = Quaterniond.Slerp(a, b, 0.0);
            var s1 = Quaterniond.Slerp(a, b, 1.0);

            var va = s0.Rotate(new Vector3d(1, 0, 0));
            var ea = a.Rotate(new Vector3d(1, 0, 0));
            Assert.AreEqual(ea.X, va.X, 1e-6);
            var vb = s1.Rotate(new Vector3d(1, 0, 0));
            var eb = b.Rotate(new Vector3d(1, 0, 0));
            Assert.AreEqual(eb.X, vb.X, 1e-6);
            Assert.AreEqual(eb.Z, vb.Z, 1e-6);
        }

        [Test]
        public void Slerp_Midpoint_IsHalfTheRotation()
        {
            // Halfway through a 90° yaw should be a 45° yaw.
            var a = Quaterniond.Identity;
            var b = Quaterniond.FromAngularVelocity(new Vector3d(0, 1, 0), Math.PI / 2.0);
            var mid = Quaterniond.Slerp(a, b, 0.5);
            var r = mid.Rotate(new Vector3d(1, 0, 0)); // +X yawed 45° about +Y → (cos45, 0, -sin45)
            Assert.AreEqual(Math.Cos(Math.PI / 4), r.X, 1e-6);
            Assert.AreEqual(-Math.Sin(Math.PI / 4), r.Z, 1e-6);
        }
    }
}
