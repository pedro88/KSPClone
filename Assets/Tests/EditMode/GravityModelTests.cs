using NUnit.Framework;
using KSPClone.SimCore;

namespace KSPClone.SimCore.Tests
{
    [TestFixture]
    public sealed class GravityModelTests
    {
        [Test]
        public void Acceleration_PointsTowardBody_InverseSquareMagnitude()
        {
            // 7000 km from a planet with μ = 3.986e14 m³/s² (Earth-like)
            var bodyCentre = Vector3d.Zero;
            var mu = 3.986e14;
            var pos = new Vector3d(7_000_000.0, 0, 0);

            var a = GravityModel.Acceleration(pos, bodyCentre, mu);

            // |a| = μ / r²
            var expectedMag = mu / (7_000_000.0 * 7_000_000.0);
            Assert.AreEqual(expectedMag, a.Length, expectedMag * 1e-9);
            // Direction: from pos toward bodyCentre → -x
            Assert.AreEqual(-1.0, a.Normalized().X, 1e-9);
            Assert.AreEqual(0.0, a.Normalized().Y, 1e-9);
            Assert.AreEqual(0.0, a.Normalized().Z, 1e-9);
        }

        [Test]
        public void Acceleration_AtPlanetCentre_IsZero_SingularityGuarded()
        {
            var a = GravityModel.Acceleration(Vector3d.Zero, Vector3d.Zero, 3.986e14);
            Assert.AreEqual(Vector3d.Zero, a);
        }

        [Test]
        public void Acceleration_DistanceHalved_MagnitudeQuadruples()
        {
            var mu = 1.0e10;
            var a1 = GravityModel.Acceleration(new Vector3d(1000, 0, 0), Vector3d.Zero, mu).Length;
            var a2 = GravityModel.Acceleration(new Vector3d(500, 0, 0), Vector3d.Zero, mu).Length;
            Assert.AreEqual(a1 * 4.0, a2, a1 * 1e-9);
        }

        [Test]
        public void Acceleration_DifferentBodyPosition_UpdatesRelativeVector()
        {
            var mu = 1.0e10;
            // Body at (1000, 0, 0), vessel at (1500, 0, 0) → r = (500, 0, 0) → pulls toward -x
            var a = GravityModel.Acceleration(new Vector3d(1500, 0, 0), new Vector3d(1000, 0, 0), mu);
            Assert.AreEqual(-1.0, a.Normalized().X, 1e-9);
        }

        [Test]
        public void SimCore_DoesNotReferenceUnityEngine()
        {
            using var _ = new NoUnityEngineAssertion();
        }
    }
}