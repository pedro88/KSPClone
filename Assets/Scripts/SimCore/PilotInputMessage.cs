#nullable enable annotations

namespace KSPClone.SimCore
{
    /// <summary>
    /// Pilot station input message, sent client → server each tick the
    /// pilot is actively commanding the vessel (M1-T08, NET-1).
    ///
    /// Carries:
    ///  - the vessel the pilot is commanding (the pilot may not be
    ///    seated yet in M1; we accept a free-floating vessel id and
    ///    reject inputs to vessels the sender is not authorised to
    ///    command — see <see cref="InputChannel"/>).
    ///  - the client tick number the input was generated on, for the
    ///    prediction loop's input buffer (M1-T10).
    ///  - the throttle command in [0..1] and the attitude command as a
    ///    pitch/yaw/roll triple in radians per second.
    ///
    /// Lives in SimCore (wire-agnostic data); the codec is in
    /// <c>KSPClone.Net</c> per ADR-0009.
    /// </summary>
    public readonly struct PilotInputMessage
    {
        public VesselId VesselId { get; }
        public long ClientTick { get; }
        public double Throttle { get; }
        public double PitchRate { get; }
        public double YawRate { get; }
        public double RollRate { get; }

        public PilotInputMessage(VesselId vesselId, long clientTick, double throttle, double pitchRate, double yawRate, double rollRate)
        {
            VesselId = vesselId;
            ClientTick = clientTick;
            Throttle = throttle;
            PitchRate = pitchRate;
            YawRate = yawRate;
            RollRate = rollRate;
        }
    }
}