using System.Collections.Generic;
using NUnit.Framework;
using KSPClone.SimCore;

namespace KSPClone.SimCore.Tests
{
    public sealed class SimSchedulerTests
    {
        [Test]
        public void Advance_OneSecond_InIrregularChunks_ProducesExactlySixtyTicks()
        {
            var (scheduler, tickRecorder) = MakeScheduler();

            var chunks = new[] { 0.003, 0.05, 0.01, 0.2, 0.04, 0.07, 0.005, 0.5, 0.122 };
            foreach (var c in chunks)
                scheduler.Advance(c);

            Assert.AreEqual(60, scheduler.TickCount, "1.0s real time -> exactly 60 ticks.");
            Assert.AreEqual(60, tickRecorder.Ticks.Count, "SimWorld.Tick called once per fixed step.");
            foreach (var dt in tickRecorder.Ticks)
                Assert.AreEqual(SimScheduler.FixedDt, dt, 1e-12, "Each tick advances by FixedDt exactly.");
        }

        [Test]
        public void Advance_RegularOneSixtiethChunks_ProducesOneTickPerCall()
        {
            var (scheduler, _) = MakeScheduler();
            for (int i = 0; i < 60; i++)
                scheduler.Advance(SimScheduler.FixedDt);
            Assert.AreEqual(60, scheduler.TickCount);
        }

        [Test]
        public void Advance_TickCount_IsMonotonic()
        {
            var (scheduler, _) = MakeScheduler();
            long prev = -1;
            for (int i = 0; i < 100; i++)
            {
                scheduler.Advance(SimScheduler.FixedDt);
                Assert.Greater(scheduler.TickCount, prev);
                prev = scheduler.TickCount;
            }
        }

        [Test]
        public void Advance_LargeChunk_ClampsToMaxAdvanceSeconds_AndFiresCatchupEvent()
        {
            var (scheduler, _) = MakeScheduler();
            long skipped = -1;
            scheduler.CatchupClamped += s => skipped = s;

            scheduler.Advance(2.0);

            var expectedMaxTicks = (long)((SimScheduler.MaxAdvanceSeconds) / SimScheduler.FixedDt);
            Assert.AreEqual(expectedMaxTicks, scheduler.TickCount,
                "Beyond the spiral-of-death clamp, no extra ticks are produced.");
            Assert.Greater(skipped, 0, "CatchupClamped event fires with the number of skipped ticks.");
        }

        [Test]
        public void Advance_NegativeDelta_Throws()
        {
            var (scheduler, _) = MakeScheduler();
            Assert.Throws<System.ArgumentOutOfRangeException>(() => scheduler.Advance(-0.001));
        }

        [Test]
        public void Advance_MasterClock_AdvancesByOneSecondAfterSixtyTicks()
        {
            var (scheduler, world) = MakeScheduler();
            for (int i = 0; i < 60; i++)
                scheduler.Advance(SimScheduler.FixedDt);

            Assert.AreEqual(1.0, world.Clock.GameTimeSeconds, 1e-9);
        }

        private static (SimScheduler scheduler, TickRecorder recorder) MakeScheduler()
        {
            var world = new SimWorld();
            var recorder = new TickRecorder(world);
            return (new SimScheduler(world), recorder);
        }
    }

    internal sealed class TickRecorder
    {
        public readonly List<double> Ticks = new();
        public TickRecorder(SimWorld world) { world.TickRecorded += dt => Ticks.Add(dt); }
    }
}