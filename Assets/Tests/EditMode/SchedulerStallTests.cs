using System.Collections.Generic;
using NUnit.Framework;
using KSPClone.SimCore;

namespace KSPClone.SimCore.Tests
{
    /// <summary>
    /// NET-5 acceptance: a long render-frame stall must not change
    /// simulated tick cadence — only batch up catch-up ticks up to the
    /// spiral-of-death clamp. No tick is ever shorter or longer than
    /// FixedDt, and the mean tick rate stays at 60 Hz ±1.
    /// </summary>
    public sealed class SchedulerStallTests
    {
        [Test]
        public void Stall_TwoHundredFiftyMillis_TriggersCatchup_AndClampsToMaxAdvance()
        {
            var (scheduler, recorder) = MakeScheduler();
            scheduler.Advance(0.25); // 250 ms stall in one chunk

            // 0.25 s clamps to MaxAdvanceSeconds = 0.25 → 15 ticks exactly.
            Assert.AreEqual(15, scheduler.TickCount);
            Assert.AreEqual(15, recorder.Ticks.Count);
            foreach (var dt in recorder.Ticks)
                Assert.AreEqual(SimScheduler.FixedDt, dt, 1e-12);
        }

        [Test]
        public void Stall_GreaterThanMaxAdvance_IsClamped_CatchupEventFires()
        {
            var (scheduler, _) = MakeScheduler();
            long skipped = -1;
            scheduler.CatchupClamped += s => skipped = s;

            scheduler.Advance(2.0); // 2 s of wall time but the clamp is 0.25 s

            var expected = (long)(SimScheduler.MaxAdvanceSeconds / SimScheduler.FixedDt);
            Assert.AreEqual(expected, scheduler.TickCount);
            Assert.AreEqual((long)((2.0 - SimScheduler.MaxAdvanceSeconds) / SimScheduler.FixedDt),
                skipped,
                "CatchupClamped must report the number of ticks that were NOT produced.");
        }

        [Test]
        public void StallThenNormal_TenSeconds_MeanTickRateIs60Hz_WithinOneTick()
        {
            var (scheduler, recorder) = MakeScheduler();
            // 10 s simulated window: two stalls + nominal 1/60 chunks in between.
            scheduler.Advance(0.25);                                  // stall #1
            for (int i = 0; i < 60; i++) scheduler.Advance(SimScheduler.FixedDt);
            scheduler.Advance(0.25);                                  // stall #2
            var remaining = 10.0 - scheduler.TickCount * SimScheduler.FixedDt;
            while (remaining > 0.0)
            {
                var dt = remaining < SimScheduler.FixedDt ? remaining : SimScheduler.FixedDt;
                scheduler.Advance(dt);
                remaining -= dt;
            }

            // 10 s of simulated time → exactly 600 ticks if nothing is dropped.
            Assert.AreEqual(600, scheduler.TickCount, 1,
                $"Mean tick rate must be 60 Hz ±1 over the 10 s window (got {scheduler.TickCount} ticks).");

            // Every individual tick must equal FixedDt.
            foreach (var dt in recorder.Ticks)
                Assert.AreEqual(SimScheduler.FixedDt, dt, 1e-12,
                    "No tick should be shorter or longer than FixedDt, even right after a stall.");
        }

        [Test]
        public void MasterClock_AdvancesByExactlyTenSeconds_OverStalledRun()
        {
            var world = new SimWorld();
            var scheduler = new SimScheduler(world);

            scheduler.Advance(0.25);
            for (int i = 0; i < 60; i++) scheduler.Advance(SimScheduler.FixedDt);
            scheduler.Advance(0.25);
            var remaining = 10.0 - scheduler.TickCount * SimScheduler.FixedDt;
            while (remaining > 0.0)
            {
                var dt = remaining < SimScheduler.FixedDt ? remaining : SimScheduler.FixedDt;
                scheduler.Advance(dt);
                remaining -= dt;
            }

            Assert.AreEqual(10.0, world.Clock.GameTimeSeconds, SimScheduler.FixedDt,
                "After 10 s of simulated real time at Rate=1, GameTimeSeconds must be 10 s ±1 FixedDt.");
        }

        /// <summary>
        /// Demonstrates the diagnostic pattern requested by the ticket:
        /// measure ticks/s across 1 s windows and assert 60 except the
        /// explicit catch-up second after a stall.
        /// </summary>
        [Test]
        public void PerSecondTickRate_Diagnostic_Is60_ExceptCatchupSecond()
        {
            var scheduler = new SimScheduler(new SimWorld());
            var ticksPerSecondLog = new List<long>();

            long prevTickCount = 0;
            scheduler.Advance(0.25); // 250 ms stall in second 0 → catchup (15 ticks)
            // Finish second 0 at nominal rate. Feed in FixedDt chunks: a single
            // Advance(0.75) would trip the spiral-of-death clamp (MaxAdvanceSeconds).
            for (int i = 0; i < 45; i++) scheduler.Advance(SimScheduler.FixedDt);
            ticksPerSecondLog.Add(scheduler.TickCount - prevTickCount);
            prevTickCount = scheduler.TickCount;

            for (int s = 1; s < 5; s++)
            {
                for (int i = 0; i < 60; i++) scheduler.Advance(SimScheduler.FixedDt);
                ticksPerSecondLog.Add(scheduler.TickCount - prevTickCount);
                prevTickCount = scheduler.TickCount;
            }

            // Second 0: the catch-up second clamped at 0.25 → 15 ticks; the
            // remaining 0.75 s at 60 Hz → 45 ticks. Total = 60.
            Assert.AreEqual(60, ticksPerSecondLog[0]);
            // Seconds 1..4: nominal 60 each.
            for (int i = 1; i < ticksPerSecondLog.Count; i++)
                Assert.AreEqual(60, ticksPerSecondLog[i],
                    $"Second {i}: expected 60 ticks/s, got {ticksPerSecondLog[i]}.");
        }

        private static (SimScheduler scheduler, TickRecorder recorder) MakeScheduler()
        {
            var world = new SimWorld();
            var recorder = new TickRecorder(world);
            return (new SimScheduler(world), recorder);
        }
    }
}