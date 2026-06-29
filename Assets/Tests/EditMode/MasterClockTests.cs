#nullable enable annotations

using System;
using NUnit.Framework;
using KSPClone.SimCore;

namespace KSPClone.SimCore.Tests
{
    public sealed class MasterClockTests
    {
        [Test]
        public void SimWorld_ExposesExactlyOne_MasterClock()
        {
            var world = new SimWorld();
            Assert.IsNotNull(world.Clock, "SimWorld must own a MasterClock.");
            var t = world.Clock.GetType();
            Assert.AreEqual(typeof(MasterClock), t);
        }

        [Test]
        public void MasterClock_GameTime_IsReadOnly_FromOutside()
        {
            var clock = new MasterClock();
            var prop = typeof(MasterClock).GetProperty(nameof(MasterClock.GameTimeSeconds));
            Assert.IsNotNull(prop);
            Assert.IsFalse(prop!.CanWrite, "GameTimeSeconds must have a private setter (Constitution Art. 1: single writer = SimWorld.Tick).");
        }

        [Test]
        public void Clock_AdvancesOneSecondAfterSixtyFixedTicks_WithinOneTick()
        {
            var world = new SimWorld();
            var scheduler = new SimScheduler(world);
            for (int i = 0; i < 60; i++)
                scheduler.Advance(SimScheduler.FixedDt);

            Assert.AreEqual(1.0, world.Clock.GameTimeSeconds, SimScheduler.FixedDt,
                "60 ticks of 1/60s must leave GameTimeSeconds at 1.0s within ±1 FixedDt.");
        }

        [Test]
        public void Clock_AdvancesExactlySixtySeconds_AfterSixtySecondsOfRealTime()
        {
            var world = new SimWorld();
            var scheduler = new SimScheduler(world);
            var remaining = 60.0;
            while (remaining > 0.0)
            {
                var dt = Math.Min(SimScheduler.FixedDt, remaining);
                scheduler.Advance(dt);
                remaining -= dt;
            }

            Assert.AreEqual(60.0, world.Clock.GameTimeSeconds, SimScheduler.FixedDt,
                "60s of real time at Rate=1 must leave GameTimeSeconds at 60s ±1 tick.");
        }

        [Test]
        public void Clock_Advances_RegardlessOfVessels()
        {
            var world = new SimWorld();
            var scheduler = new SimScheduler(world);
            // Feed in frame-sized chunks: a single 1.0s chunk would hit the
            // spiral-of-death clamp (MaxAdvanceSeconds), which is tested separately.
            for (int i = 0; i < 60; i++)
                scheduler.Advance(SimScheduler.FixedDt);
            Assert.AreEqual(1.0, world.Clock.GameTimeSeconds, SimScheduler.FixedDt,
                "MasterClock must advance with zero vessels (Constitution Art. 4: the universe lives even when empty).");
        }

        [Test]
        public void Clock_RateMultiplier_DoublesGameTimeAdvance()
        {
            var clock = new MasterClock { Rate = 2.0 };
            clock.Advance(1.0);
            Assert.AreEqual(2.0, clock.GameTimeSeconds, 1e-9);
        }
    }
}