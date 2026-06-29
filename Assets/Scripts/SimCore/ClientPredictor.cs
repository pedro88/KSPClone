#nullable enable annotations

using System;
using System.Collections.Generic;

namespace KSPClone.SimCore
{
    /// <summary>
    /// Single-vessel state held by the client for prediction and
    /// reconciliation (M1-T10, NET-2). Lives entirely in doubles so
    /// the prediction and the server can compare bit-exactly when
    /// needed (the ADR-0013 §1 full-double wire format guarantees the
    /// server and the predictor are working with the same precision).
    /// </summary>
    public readonly struct PredictedVesselState
    {
        public Vector3d Position { get; }
        public Vector3d Velocity { get; }
        public Vector3d AngularVelocity { get; }
        public long LastProcessedClientTick { get; }

        public PredictedVesselState(Vector3d position, Vector3d velocity, Vector3d angularVelocity, long lastProcessedClientTick)
        {
            Position = position;
            Velocity = velocity;
            AngularVelocity = angularVelocity;
            LastProcessedClientTick = lastProcessedClientTick;
        }

        public static readonly PredictedVesselState Identity =
            new(Vector3d.Zero, Vector3d.Zero, Vector3d.Zero, 0L);
    }

    /// <summary>
    /// Pure-data single-vessel predictor (M1-T10, NET-2). Holds the
    /// client's predicted state for one controlled vessel and a
    /// queue of unacked inputs. Each client tick the predictor
    /// applies the next buffered input to the predicted state and
    /// advances.
    ///
    /// Integration step: the predictor delegates to an
    /// <see cref="IPredictionStep"/> supplied by the host. In M1 the
    /// step is a hand-rolled integration that consumes gravity +
    /// throttle + attitude, matching the server's BubbleIntegrator
    /// well enough that residual drift stays under the reconciliation
    /// threshold (NET-3). A future slice can share the integrator
    /// code directly via asmdef wiring.
    /// </summary>
    public sealed class ClientPredictor
    {
        public VesselId ControlledVesselId { get; }
        public PredictedVesselState State => _state;
        public int PendingCount => _pending.Count;
        /// <summary>Oldest unacked input, or null if the buffer is empty.</summary>
        public PilotInputMessage? OldestPending => _pending.Count > 0 ? _pending.Peek() : (PilotInputMessage?)null;
        public int MaxBufferedInputs { get; }

        public event Action<PredictedVesselState>? StatePredicted;

        private readonly IPredictionStep _step;
        private readonly Queue<PilotInputMessage> _pending;
        private PredictedVesselState _state;

        public ClientPredictor(
            VesselId controlledVesselId,
            PredictedVesselState seed,
            IPredictionStep step,
            int maxBufferedInputs = 30)
        {
            ControlledVesselId = controlledVesselId;
            _state = seed;
            _step = step ?? throw new ArgumentNullException(nameof(step));
            _pending = new Queue<PilotInputMessage>();
            MaxBufferedInputs = maxBufferedInputs;
        }

        /// <summary>
        /// Submit an input and apply it to the predicted state in the
        /// same call (NET-2: zero perceived input lag). The input is
        /// also enqueued for later replay during reconciliation.
        /// </summary>
        public void SubmitInput(PilotInputMessage input)
        {
            ApplyInput(input);
            _pending.Enqueue(input);
            if (_pending.Count > MaxBufferedInputs)
                _pending.Dequeue(); // drop oldest — the latency budget must adapt
        }

        private void ApplyInput(PilotInputMessage input)
        {
            _state = _step.Step(_state, input, _state.LastProcessedClientTick + 1);
        }

        /// <summary>
        /// Drop buffered inputs at or before the server-acked tick
        /// (NET-3 reconciler calls this once replay is done).
        /// </summary>
        public void DiscardAcked(long ackedClientTick)
        {
            while (_pending.Count > 0 && _pending.Peek().ClientTick <= ackedClientTick)
                _pending.Dequeue();
        }

        /// <summary>
        /// Snap the predicted state to a server snapshot and replay
        /// every buffered input forward. Returns the pre-replay
        /// predicted state so the reconciler can measure divergence
        /// and decide smoothing vs hard snap.
        /// </summary>
        public PredictedVesselState Reconcile(PredictedVesselState authoritative, long ackedClientTick)
        {
            var preReplay = _state;
            _state = authoritative;

            // Replay buffered inputs with clientTick > ackedClientTick.
            var replay = new List<PilotInputMessage>(_pending);
            foreach (var input in replay)
            {
                if (input.ClientTick <= ackedClientTick) continue;
                ApplyInput(input);
            }
            StatePredicted?.Invoke(_state);
            return preReplay;
        }
    }

    /// <summary>
    /// Plug-in integration step for the predictor. The Server-side
    /// integrator uses Unity PhysX; the client's predictor can use
    /// either the same engine (preferred — see ADR-0006) or a
    /// lightweight custom step (M1 ships the lightweight path; the
    /// engine coupling is wired in a later slice).
    /// </summary>
    public interface IPredictionStep
    {
        /// <summary>
        /// Advance the predicted state by one fixed tick under the
        /// given input. Returns the new state.
        /// </summary>
        PredictedVesselState Step(PredictedVesselState current, PilotInputMessage input, long newClientTick);
    }
}