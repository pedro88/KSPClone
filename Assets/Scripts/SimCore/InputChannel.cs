#nullable enable annotations

using System;
using System.Collections.Generic;

namespace KSPClone.SimCore
{
    /// <summary>
    /// Owns the client → server input channel for active vessels
    /// (M1-T08, NET-1). Per tick, the server feeds received
    /// <see cref="PilotInputMessage"/> into the channel via
    /// <see cref="Submit"/>; the channel applies them to the target
    /// <see cref="Vessel"/>'s command fields. The integrator then
    /// applies those fields to the rigidbody each tick (M1-T06/07).
    ///
    /// Station routing (CREW-1) lands in M2. For M1 a single seat type
    /// "Pilot" owns the throttle + attitude command set. Inputs that
    /// try to write a field the seat doesn't own are dropped with a
    /// counter bump on <see cref="RejectedInputs"/>.
    ///
    /// The input buffer (M1-T10) is held alongside the channel so the
    /// prediction loop can replay unacked inputs at reconciliation time.
    /// </summary>
    public sealed class InputChannel
    {
        public IReadOnlyDictionary<VesselId, Queue<PilotInputMessage>> PendingInputs => _buffer;
        public int RejectedInputs { get; private set; }

        public event Action<PilotInputMessage>? InputSubmitted;

        private readonly Dictionary<VesselId, Queue<PilotInputMessage>> _buffer = new();
        private readonly SimWorld _world;

        public InputChannel(SimWorld world)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
        }

        /// <summary>
        /// Apply an input to its target vessel. The Pilot seat owns
        /// throttle + attitude (CREW-1); fields outside that set are
        /// silently dropped and counted via <see cref="RejectedInputs"/>.
        /// Returns true if the input was accepted.
        /// </summary>
        public bool Submit(PilotInputMessage input)
        {
            if (!_world.Vessels.TryGetValue(input.VesselId, out var vessel))
            {
                RejectedInputs++;
                return false;
            }
            // Pilot seat authority — any field not in the seat is dropped.
            // Today the seat owns throttle + attitude only, so every
            // submitted field passes. Future station splits live here.
            vessel.ThrottleCommand = Clamp01(input.Throttle);
            vessel.AttitudeCommand = new Vector3d(input.PitchRate, input.YawRate, input.RollRate);

            if (!_buffer.TryGetValue(input.VesselId, out var q))
            {
                q = new Queue<PilotInputMessage>();
                _buffer[input.VesselId] = q;
            }
            q.Enqueue(input);

            InputSubmitted?.Invoke(input);
            return true;
        }

        /// <summary>
        /// Drop inputs for the given vessel at or before
        /// <paramref name="ackedClientTick"/> — called by the
        /// reconciler (M1-T11) once the server has acknowledged the
        /// server tick that consumed them.
        /// </summary>
        public void DiscardAcked(VesselId vesselId, long ackedClientTick)
        {
            if (!_buffer.TryGetValue(vesselId, out var q)) return;
            while (q.Count > 0 && q.Peek().ClientTick <= ackedClientTick)
                q.Dequeue();
        }

        private static double Clamp01(double v) => v < 0.0 ? 0.0 : (v > 1.0 ? 1.0 : v);
    }
}