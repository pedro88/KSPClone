using NUnit.Framework;
using KSPClone.SimCore;

namespace KSPClone.SimCore.Tests
{
    [TestFixture]
    public sealed class ClientPredictorTests
    {
        [Test]
        public void SubmitInput_AppliesImmediately_AndBuffersForReplay()
        {
            var v = VesselId.New();
            var step = new TrivialPredictionStep();
            var predictor = new ClientPredictor(v, PredictedVesselState.Identity, step);

            predictor.SubmitInput(new PilotInputMessage(v, 1L, 1.0, 0, 0, 0));

            Assert.AreEqual(1, predictor.PendingCount);
            Assert.AreNotEqual(Vector3d.Zero, predictor.State.Position);
            Assert.AreEqual(1L, predictor.State.LastProcessedClientTick);
        }

        [Test]
        public void Reconcile_ResetsToAuthoritative_AndReplaysBufferedInputs()
        {
            var v = VesselId.New();
            var step = new TrivialPredictionStep();
            var predictor = new ClientPredictor(v, PredictedVesselState.Identity, step);

            // Submit two inputs while offline (predicting locally).
            predictor.SubmitInput(new PilotInputMessage(v, 1L, 1.0, 0, 0, 0));
            predictor.SubmitInput(new PilotInputMessage(v, 2L, 1.0, 0, 0, 0));
            var preReplay = predictor.State;

            // Server says the state at the end of client tick 1 was just the result of tick 1.
            // We replay tick 2 forward from that authoritative state.
            var authState = step.Step(PredictedVesselState.Identity,
                new PilotInputMessage(v, 1L, 1.0, 0, 0, 0), 1L);

            var pre = predictor.Reconcile(authState, ackedClientTick: 1L);

            Assert.AreEqual(preReplay.Position.X, pre.Position.X, 1e-9);
            // Post-replay: one input (tick 2) replayed from the authoritative state.
            Assert.AreEqual(authState.Position.X + (step.ThrustAccelerationPerThrottleUnit * step.FixedDt) * step.FixedDt,
                            predictor.State.Position.X, 1e-9);
        }

        [Test]
        public void DiscardAcked_DropsBufferedInputsAtOrBefore()
        {
            var v = VesselId.New();
            var step = new TrivialPredictionStep();
            var predictor = new ClientPredictor(v, PredictedVesselState.Identity, step);

            predictor.SubmitInput(new PilotInputMessage(v, 10L, 1.0, 0, 0, 0));
            predictor.SubmitInput(new PilotInputMessage(v, 11L, 1.0, 0, 0, 0));

            predictor.DiscardAcked(10L);

            Assert.AreEqual(1, predictor.PendingCount);
        }

        [Test]
        public void BufferOverflow_DropsOldestInput()
        {
            var v = VesselId.New();
            var step = new TrivialPredictionStep();
            var predictor = new ClientPredictor(v, PredictedVesselState.Identity, step, maxBufferedInputs: 2);

            for (int t = 1; t <= 5; t++)
                predictor.SubmitInput(new PilotInputMessage(v, t, 1.0, 0, 0, 0));

            Assert.AreEqual(2, predictor.PendingCount);
            Assert.AreEqual(4L, predictor.OldestPending!.Value.ClientTick);
        }

        [Test]
        public void Prediction_AppliesThrottleAsAcceleration_InFixedDt()
        {
            var v = VesselId.New();
            var step = new TrivialPredictionStep(thrustAccel: 10.0, fixedDt: 1.0 / 60.0);
            var predictor = new ClientPredictor(v, PredictedVesselState.Identity, step);

            predictor.SubmitInput(new PilotInputMessage(v, 1L, 1.0, 0, 0, 0));

            // After one tick: v += a * dt; pos += v * dt
            var expectedVx = 10.0 * (1.0 / 60.0);
            var expectedPx = expectedVx * (1.0 / 60.0);
            Assert.AreEqual(expectedVx, predictor.State.Velocity.X, 1e-9);
            Assert.AreEqual(expectedPx, predictor.State.Position.X, 1e-9);
        }

        [Test]
        public void SimCore_DoesNotReferenceUnityEngine()
        {
            using var _ = new NoUnityEngineAssertion();
        }
    }

    [TestFixture]
    public sealed class ClientReconcilerTests
    {
        [Test]
        public void TinyDivergence_NoCorrection()
        {
            var r = new ClientReconciler();
            var pre = new PredictedVesselState(new Vector3d(0, 0, 0), Vector3d.Zero, Vector3d.Zero, 5);
            var post = new PredictedVesselState(new Vector3d(0.10, 0, 0), Vector3d.Zero, Vector3d.Zero, 6);
            var d = r.Decide(pre, post, measuredRttSeconds: 0.080);
            Assert.AreEqual(ReconciliationKind.None, d.Kind);
        }

        [Test]
        public void MediumDivergence_Smooth()
        {
            var r = new ClientReconciler();
            var pre = new PredictedVesselState(new Vector3d(0, 0, 0), Vector3d.Zero, Vector3d.Zero, 5);
            var post = new PredictedVesselState(new Vector3d(0.5, 0, 0), Vector3d.Zero, Vector3d.Zero, 6);
            var d = r.Decide(pre, post, measuredRttSeconds: 0.080);
            Assert.AreEqual(ReconciliationKind.Smooth, d.Kind);
            Assert.Greater(d.SmoothingFrames, 0);
        }

        [Test]
        public void LargeDivergence_HardSnap()
        {
            var r = new ClientReconciler();
            var pre = new PredictedVesselState(new Vector3d(0, 0, 0), Vector3d.Zero, Vector3d.Zero, 5);
            var post = new PredictedVesselState(new Vector3d(5.0, 0, 0), Vector3d.Zero, Vector3d.Zero, 6);
            var d = r.Decide(pre, post, measuredRttSeconds: 0.080);
            Assert.AreEqual(ReconciliationKind.HardSnap, d.Kind);
        }

        [Test]
        public void Beyond150ms_SmoothingWindowWidens()
        {
            var r = new ClientReconciler();
            var pre = new PredictedVesselState(new Vector3d(0, 0, 0), Vector3d.Zero, Vector3d.Zero, 5);
            var post = new PredictedVesselState(new Vector3d(0.5, 0, 0), Vector3d.Zero, Vector3d.Zero, 6);

            var dNormal = r.Decide(pre, post, 0.080);
            var dHighLatency = r.Decide(pre, post, 0.300);

            Assert.Greater(dHighLatency.SmoothingFrames, dNormal.SmoothingFrames,
                "High latency must widen the smoothing window (NET-6 graceful degradation).");
        }

        [Test]
        public void SimCore_DoesNotReferenceUnityEngine()
        {
            using var _ = new NoUnityEngineAssertion();
        }
    }

    [TestFixture]
    public sealed class LatencyMonitorTests
    {
        [Test]
        public void NoSamples_UsesDefaultBufferSize()
        {
            var m = new LatencyMonitor();
            Assert.GreaterOrEqual(m.RecommendedBufferSize(), 8);
        }

        [Test]
        public void RecordedSamples_BufferSizeScalesWithRtt()
        {
            var m = new LatencyMonitor();
            m.RecordSample(0.040); // 40 ms
            var lowBuf = m.RecommendedBufferSize();
            m.RecordSample(0.300); // 300 ms
            var highBuf = m.RecommendedBufferSize();
            Assert.Greater(highBuf, lowBuf, "Higher RTT must recommend a larger input buffer.");
        }

        [Test]
        public void EmaTracksAverage_OverManySamples()
        {
            var m = new LatencyMonitor(emaAlpha: 0.5);
            for (int i = 0; i < 20; i++) m.RecordSample(0.100);
            Assert.AreEqual(0.100, m.AverageRttSeconds, 1e-9);
        }

        [Test]
        public void WorstRtt_IsMaxObserved()
        {
            var m = new LatencyMonitor();
            m.RecordSample(0.040);
            m.RecordSample(0.150);
            m.RecordSample(0.080);
            Assert.AreEqual(0.150, m.WorstRttSeconds, 1e-9);
        }

        [Test]
        public void SimCore_DoesNotReferenceUnityEngine()
        {
            using var _ = new NoUnityEngineAssertion();
        }
    }
}