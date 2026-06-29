#nullable enable annotations

using System.Collections.Generic;
using NUnit.Framework;
using KSPClone.SimCore;

namespace KSPClone.SimCore.Tests
{
    /// <summary>
    /// Client flight loop (M1-T24, NET-2/3/4): the controlled vessel is
    /// predicted same-frame and reconciled against snapshots; every other
    /// vessel is interpolated and never predicted (the prediction boundary).
    /// </summary>
    public sealed class ClientFlightModelTests
    {
        [Test]
        public void CaptureInput_AdvancesPredictedState_SameCall_ZeroLag()
        {
            var model = new ClientFlightModel();
            var v = VesselId.New();
            model.Control(v);

            var before = model.ControlledState.Position;
            var msg = model.CaptureInput(throttle: 1.0, pitchRate: 0.0, yawRate: 0.0, rollRate: 0.0);

            Assert.IsNotNull(msg);
            Assert.AreEqual(1L, msg!.Value.ClientTick);
            Assert.AreEqual(v, msg.Value.VesselId);
            Assert.Greater(model.ControlledState.Position.X, before.X,
                "Throttle must move the predicted state on the same call (NET-2).");
        }

        [Test]
        public void Reconcile_ConvergesToAuthoritative_WhenAllInputsAcked()
        {
            var model = new ClientFlightModel();
            var v = VesselId.New();
            model.Control(v);

            model.CaptureInput(1.0, 0, 0, 0); // tick 1
            model.CaptureInput(1.0, 0, 0, 0); // tick 2
            model.CaptureInput(1.0, 0, 0, 0); // tick 3

            var authoritativePos = new Vector3d(123.0, -4.0, 7.0);
            var authoritativeVel = new Vector3d(5.0, 0.0, 0.0);
            var bundle = new SnapshotBundle(1.0, 1L, new List<VesselSnapshot>
            {
                new(v, 1.0, 1L, authoritativePos, authoritativeVel, Vector3d.Zero, lastProcessedClientTick: 3L),
            });

            model.OnSnapshotBundle(bundle);

            // All inputs acked (tick 3) → nothing to replay → state equals authoritative.
            Assert.AreEqual(authoritativePos.X, model.ControlledState.Position.X, 1e-9);
            Assert.AreEqual(authoritativePos.Y, model.ControlledState.Position.Y, 1e-9);
            Assert.AreEqual(authoritativeVel.X, model.ControlledState.Velocity.X, 1e-9);
            Assert.AreEqual(0, model.Predictor!.PendingCount, "Acked inputs are discarded.");
        }

        [Test]
        public void Reconcile_ReplaysUnackedInputs_PastTheAck()
        {
            var model = new ClientFlightModel();
            var v = VesselId.New();
            model.Control(v);

            model.CaptureInput(1.0, 0, 0, 0); // tick 1 (will be acked)
            model.CaptureInput(1.0, 0, 0, 0); // tick 2 (unacked → replayed)

            var bundle = new SnapshotBundle(1.0, 1L, new List<VesselSnapshot>
            {
                new(v, 1.0, 1L, Vector3d.Zero, Vector3d.Zero, Vector3d.Zero, lastProcessedClientTick: 1L),
            });
            model.OnSnapshotBundle(bundle);

            // Reset to origin (ack tick 1) then replay tick 2 → state moves off origin again.
            Assert.Greater(model.ControlledState.Position.X, 0.0, "Unacked input (tick 2) is replayed.");
            Assert.AreEqual(1, model.Predictor!.PendingCount, "Only the acked tick is discarded.");
        }

        [Test]
        public void NonControlledVessels_AreInterpolated_NeverPredicted()
        {
            var model = new ClientFlightModel();
            var controlled = VesselId.New();
            var other = VesselId.New();
            model.Control(controlled);

            var bundle = new SnapshotBundle(1.0, 1L, new List<VesselSnapshot>
            {
                new(controlled, 1.0, 1L, new Vector3d(50, 0, 0), Vector3d.Zero, Vector3d.Zero, 0L),
                new(other,      1.0, 1L, new Vector3d(99, 0, 0), Vector3d.Zero, Vector3d.Zero, 0L),
            });
            model.OnSnapshotBundle(bundle);

            Assert.IsTrue(model.TrySampleOther(other, out var pos), "Other vessels are interpolated.");
            Assert.AreEqual(99.0, pos.X, 1e-9);
            Assert.IsFalse(model.TrySampleOther(controlled, out _),
                "The controlled vessel is predicted, never interpolated (prediction boundary).");
        }
    }
}
