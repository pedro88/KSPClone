#nullable enable annotations

using System;
using System.Collections.Generic;

namespace KSPClone.SimCore
{
    /// <summary>
    /// Client-side flight coordinator (M1-T24). Ties together the pieces the
    /// client needs to fly the loop, engine-free so it is unit-testable:
    ///
    ///  - the locally-controlled vessel is *predicted* (zero perceived input
    ///    lag, NET-2) and *reconciled* against authoritative snapshots (NET-3);
    ///  - every other vessel is *interpolated* with a small delay (NET-4),
    ///    never predicted — the prediction boundary.
    ///
    /// The transport (<c>ClientNetPeer</c>) owns bytes; this owns the model.
    /// It does not send: <see cref="CaptureInput"/> returns the input message
    /// for the caller to put on the wire, keeping this class engine- and
    /// transport-agnostic (ADR-0009).
    /// </summary>
    public sealed class ClientFlightModel
    {
        public VesselId? ControlledVesselId { get; private set; }
        public ClientPredictor? Predictor { get; private set; }
        public PredictedVesselState ControlledState => Predictor?.State ?? PredictedVesselState.Identity;
        public ReconciliationDecision LastDecision { get; private set; }
        public double ServerGameTime { get; private set; }
        public double InterpolationDelay { get; set; } = 0.1;
        public long ClientTick => _clientTick;

        /// <summary>The vessel ids currently interpolated (everything not controlled).</summary>
        public IEnumerable<VesselId> InterpolatedVesselIds => _interpolators.Keys;

        /// <summary>
        /// The client render origin (ADR-0015): the controlled vessel's
        /// predicted world position, so the controlled vessel renders at ~zero
        /// in float and the camera rides its frame.
        /// </summary>
        public Vector3d RenderOrigin => ControlledState.Position;

        /// <summary>
        /// Convert a world-double position to the render-local frame. The
        /// subtraction happens in doubles before any narrow to float — the
        /// no-catastrophic-cancellation invariant (ADR-0012 §6).
        /// </summary>
        public Vector3d ToRenderLocal(Vector3d worldPosition) => worldPosition - RenderOrigin;

        private readonly IPredictionStep _step;
        private readonly ClientReconciler _reconciler;
        private readonly LatencyMonitor _latency;
        private readonly Dictionary<VesselId, VesselInterpolator> _interpolators = new();
        private long _clientTick;

        public ClientFlightModel(IPredictionStep? step = null, ClientReconciler? reconciler = null, LatencyMonitor? latency = null)
        {
            _step = step ?? new TrivialPredictionStep();
            _reconciler = reconciler ?? new ClientReconciler();
            _latency = latency ?? new LatencyMonitor();
        }

        /// <summary>
        /// Take local control of a vessel (after occupying its Pilot station).
        /// Seeds the predictor; the first authoritative snapshot then snaps the
        /// predicted state onto the server's (no closed-form seed needed).
        /// </summary>
        public void Control(VesselId vesselId, PredictedVesselState seed = default)
        {
            ControlledVesselId = vesselId;
            Predictor = new ClientPredictor(vesselId, seed, _step);
            // The controlled vessel is never interpolated (prediction boundary).
            _interpolators.Remove(vesselId);
        }

        /// <summary>
        /// Build the input for the next client tick, apply it to the predicted
        /// state immediately (zero perceived lag, NET-2), and return it so the
        /// caller can send it to the server. No-op (returns null) if nothing is
        /// controlled yet.
        /// </summary>
        public PilotInputMessage? CaptureInput(double throttle, double pitchRate, double yawRate, double rollRate)
        {
            if (ControlledVesselId is not { } id || Predictor is null) return null;
            _clientTick++;
            var msg = new PilotInputMessage(id, _clientTick, throttle, pitchRate, yawRate, rollRate);
            Predictor.SubmitInput(msg);
            return msg;
        }

        /// <summary>Feed a measured round-trip time (for NET-6 smoothing width).</summary>
        public void RecordRtt(double rttSeconds) => _latency.RecordSample(rttSeconds);

        /// <summary>Consume one authoritative snapshot bundle: reconcile the controlled vessel, buffer the rest for interpolation.</summary>
        public void OnSnapshotBundle(SnapshotBundle bundle)
        {
            ServerGameTime = bundle.GameTime;
            foreach (var snap in bundle.Vessels)
            {
                if (ControlledVesselId is { } cid && snap.VesselId.Equals(cid))
                    ReconcileControlled(snap);
                else
                    Interpolator(snap.VesselId).OnSnapshot(snap);
            }
        }

        /// <summary>Interpolated render position of a non-controlled vessel; false if none seen yet.</summary>
        public bool TrySampleOther(VesselId id, out Vector3d position)
        {
            if (_interpolators.TryGetValue(id, out var interp))
            {
                position = interp.Sample(ServerGameTime);
                return true;
            }
            position = Vector3d.Zero;
            return false;
        }

        private void ReconcileControlled(VesselSnapshot snap)
        {
            if (Predictor is null) return;
            var authoritative = new PredictedVesselState(
                snap.Position, snap.Velocity, snap.AngularVelocity, snap.LastProcessedClientTick);
            // Reset to authoritative + replay unacked inputs, then decide how to show the correction.
            var preReplay = Predictor.Reconcile(authoritative, snap.LastProcessedClientTick);
            Predictor.DiscardAcked(snap.LastProcessedClientTick);
            LastDecision = _reconciler.Decide(preReplay, Predictor.State, _latency.AverageRttSeconds);
        }

        private VesselInterpolator Interpolator(VesselId id)
        {
            if (!_interpolators.TryGetValue(id, out var interp))
            {
                interp = new VesselInterpolator { InterpolationDelay = InterpolationDelay };
                _interpolators[id] = interp;
            }
            return interp;
        }
    }
}
