using NUnit.Framework;
using KSPClone.SimCore;

namespace KSPClone.SimCore.Tests
{
    public sealed class SnapshotInterpolationTests
    {
        [Test]
        public void Buffer_Add_KeepsSnapshotsInOrder_AndSlidingWindow()
        {
            var buf = new SnapshotBuffer(maxLength: 3);
            buf.Add(new VesselSnapshot(default, 10.0, 1, new Vector3d(1, 0, 0), Vector3d.Zero));
            buf.Add(new VesselSnapshot(default, 20.0, 2, new Vector3d(2, 0, 0), Vector3d.Zero));
            buf.Add(new VesselSnapshot(default, 30.0, 3, new Vector3d(3, 0, 0), Vector3d.Zero));
            buf.Add(new VesselSnapshot(default, 40.0, 4, new Vector3d(4, 0, 0), Vector3d.Zero));

            Assert.AreEqual(3, buf.Count, "Buffer must respect maxLength.");
            Assert.AreEqual(20.0, buf.Snapshots[0].GameTime, "Oldest snapshot is dropped first.");
            Assert.AreEqual(40.0, buf.Snapshots[2].GameTime);
        }

        [Test]
        public void Buffer_Rejects_OutOfOrderInsertion()
        {
            var buf = new SnapshotBuffer();
            buf.Add(new VesselSnapshot(default, 20.0, 1, Vector3d.Zero, Vector3d.Zero));
            Assert.Throws<System.ArgumentException>(() =>
                buf.Add(new VesselSnapshot(default, 10.0, 0, Vector3d.Zero, Vector3d.Zero)));
        }

        [Test]
        public void Sample_AtMidpoint_ReturnsLinearInterpolation()
        {
            var interp = new VesselInterpolator { InterpolationDelay = 0.0 };
            interp.OnSnapshot(new VesselSnapshot(default, 0.0, 1, new Vector3d(0, 0, 0), Vector3d.Zero));
            interp.OnSnapshot(new VesselSnapshot(default, 1.0, 2, new Vector3d(10, 0, 0), Vector3d.Zero));

            var sample = interp.Sample(0.5);
            Assert.AreEqual(0.0, sample.X, 1e-12);
            Assert.AreEqual(5.0, sample.Y, 1e-12);
            Assert.AreEqual(0.0, sample.Z, 1e-12);
        }

        [Test]
        public void Sample_LagsLatestSnapshot_ByInterpolationDelay()
        {
            var interp = new VesselInterpolator { InterpolationDelay = 0.1 };
            // Two snapshots at server-times 0.0 and 0.04 (25 Hz).
            interp.OnSnapshot(new VesselSnapshot(default, 0.0, 1, new Vector3d(0, 0, 0), new Vector3d(100, 0, 0)));
            interp.OnSnapshot(new VesselSnapshot(default, 0.04, 2, new Vector3d(4, 0, 0), new Vector3d(100, 0, 0)));

            // At server-time 0.10, render-time = 0.0 → the older snapshot exactly.
            var at100 = interp.Sample(0.10);
            Assert.AreEqual(0.0, at100.X, 1e-9);

            // At server-time 0.14, render-time = 0.04 → the newer snapshot exactly.
            var at140 = interp.Sample(0.14);
            Assert.AreEqual(4.0, at140.X, 1e-9);
        }

        [Test]
        public void Sample_BeforeAnySnapshot_ReturnsZero()
        {
            var interp = new VesselInterpolator();
            Assert.AreEqual(Vector3d.Zero, interp.Sample(123.0));
        }

        [Test]
        public void Sample_PastNewest_ExtrapolatesWithVelocity_Briefly()
        {
            var interp = new VesselInterpolator { InterpolationDelay = 0.0 };
            interp.OnSnapshot(new VesselSnapshot(default, 0.0, 1, new Vector3d(0, 0, 0), new Vector3d(100, 0, 0)));
            interp.OnSnapshot(new VesselSnapshot(default, 0.04, 2, new Vector3d(4, 0, 0), new Vector3d(100, 0, 0)));

            // Render-time 0.10, beyond the newest (0.04). Velocity = 100 m/s.
            var sample = interp.Sample(0.10);
            Assert.AreEqual(4.0 + 100.0 * (0.10 - 0.04), sample.X, 1e-9);
        }

        [Test]
        public void Sample_FrameAt60Fps_NoStairstep_LargerThanSnapshotStep()
        {
            var interp = new VesselInterpolator { InterpolationDelay = 0.1 };
            interp.OnSnapshot(new VesselSnapshot(default, 0.0, 1, new Vector3d(0, 0, 0), new Vector3d(100, 0, 0)));
            interp.OnSnapshot(new VesselSnapshot(default, 0.04, 2, new Vector3d(4, 0, 0), new Vector3d(100, 0, 0)));

            // Sample at 60 fps over a 100 ms window; consecutive
            // samples must differ by less than one snapshot interval.
            double prev = interp.Sample(0.10).X;
            double maxJump = 0.0;
            for (int f = 1; f <= 6; f++)
            {
                var t = 0.10 + f * (1.0 / 60.0);
                var x = interp.Sample(t).X;
                var jump = System.Math.Abs(x - prev);
                if (jump > maxJump) maxJump = jump;
                prev = x;
            }
            Assert.Less(maxJump, 0.04,
                $"Per-frame jump at 60 fps must be much smaller than one snapshot interval; got {maxJump} m.");
        }
    }
}