using System.Collections.Generic;
using NUnit.Framework;
using KSPClone.SimCore;

namespace KSPClone.SimCore.Tests
{
    public sealed class SnapshotEmitterTests
    {
        private const double EarthMu = 3.986004418e14;

        private static (SimWorld world, SnapshotEmitter emitter, List<SnapshotBundle> received) Make(double rateHz = 25.0)
        {
            var reg = new BodyRegistry(new[]
            {
                new CelestialBody(CelestialBodyId.Planet, "Earth", EarthMu, 924_000_000.0, CelestialBodyId.Root),
            });
            var world = new SimWorld(reg);
            world.RegisterVessel(new Vessel(VesselId.New(),
                new Orbit(7e6, 0, 0, 0, 0, 0, 0, CelestialBodyId.Planet)));
            var received = new List<SnapshotBundle>();
            var emitter = new SnapshotEmitter(world, new ConnectionRegistry(), rateHz, onBundle: received.Add);
            return (world, emitter, received);
        }

        [Test]
        public void Tick_OverTenSeconds_EmitsAtNominalRate_WithinTolerance()
        {
            var (world, emitter, received) = Make(25.0);
            var scheduler = new SimScheduler(world);

            // 10 s of simulated time; the snapshot emitter shares the
            // scheduler's 60 Hz ticks.
            for (int i = 0; i < 600; i++) scheduler.Advance(SimScheduler.FixedDt);
            // The scheduler advances GameTime; the emitter needs to be
            // ticked with the same dt to count bundles.
            for (int i = 0; i < 600; i++) emitter.Tick(SimScheduler.FixedDt);

            Assert.AreEqual(250, received.Count, 25,
                $"Over 10 s at 25 Hz nominal, expected ~250 bundles, got {received.Count}.");
            // Measured rate (last full second) should be close to 25.
            Assert.AreEqual(25.0, emitter.MeasuredRateHz, 1.0,
                "Measured rate should be 25 Hz ±1 over a full second.");
        }

        [Test]
        public void Seq_IsMonotonic_AndGameTimeAdvances()
        {
            var (world, emitter, received) = Make(10.0);
            var scheduler = new SimScheduler(world);

            for (int i = 0; i < 60; i++) scheduler.Advance(SimScheduler.FixedDt);
            for (int i = 0; i < 60; i++) emitter.Tick(SimScheduler.FixedDt);

            long prev = -1;
            double prevT = double.NegativeInfinity;
            foreach (var b in received)
            {
                Assert.Greater(b.Seq, prev, "Seq must be strictly monotonic.");
                Assert.Greater(b.GameTime, prevT, "GameTime must advance between bundles.");
                prev = b.Seq;
                prevT = b.GameTime;
            }
        }

        [Test]
        public void Bundles_CarryOneSnapshotPerVessel_WithCachedState()
        {
            var (world, emitter, received) = Make(50.0);
            var scheduler = new SimScheduler(world);
            scheduler.Advance(SimScheduler.FixedDt);
            emitter.Tick(SimScheduler.FixedDt);

            Assert.IsTrue(received.Count >= 1);
            var bundle = received[0];
            Assert.AreEqual(1, bundle.Vessels.Count);
            var snap = bundle.Vessels[0];
            Assert.IsTrue(snap.Position.Length > 0.0, "Vessel's cached position should be non-zero after one tick.");
        }

        [Test]
        public void EmissionRate_IsIndependentOfSimTickRate()
        {
            // Same real-time window, two different tick rates → similar
            // number of bundles. This is the NET-5 decoupling property.
            var (w1, e1, r1) = Make(30.0);
            var s1 = new SimScheduler(w1);
            for (int i = 0; i < 60; i++) { s1.Advance(SimScheduler.FixedDt); e1.Tick(SimScheduler.FixedDt); }

            var (w2, e2, r2) = Make(30.0);
            var s2 = new SimScheduler(w2);
            // Half-rate sim tick: 1/120 s steps
            for (int i = 0; i < 30; i++) { s2.Advance(0.5 * SimScheduler.FixedDt); e2.Tick(0.5 * SimScheduler.FixedDt); }

            // Over the same 1-second window, both should produce ~30 bundles.
            Assert.AreEqual(r1.Count, r2.Count, 2);
        }
    }
}