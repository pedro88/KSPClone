namespace KSPClone.SimCore
{
    public enum ClientCommandType
    {
        RequestWarp = 0,
        ApproveWarp = 1,
        OccupyStation = 2,
    }

    /// <summary>
    /// A client → server command (wire-agnostic data). Carries warp votes
    /// (M0) and station occupancy (M1, ADR-0016). The requesting player is
    /// identified by the connection the command arrives on (the server maps
    /// peer → <see cref="PlayerId"/>), so it is not in the payload.
    /// Serialized by the transport's wire codec, never by SimCore.
    /// </summary>
    public readonly struct ClientCommand
    {
        public ClientCommandType Type { get; }
        public double Multiplier { get; }
        public WarpKind Kind { get; }

        // OccupyStation payload (default/ignored for warp commands).
        public VesselId VesselId { get; }
        public Station Station { get; }

        public ClientCommand(ClientCommandType type, double multiplier, WarpKind kind,
            VesselId vesselId = default, Station station = default)
        {
            Type = type;
            Multiplier = multiplier;
            Kind = kind;
            VesselId = vesselId;
            Station = station;
        }

        public static ClientCommand RequestWarp(double multiplier, WarpKind kind) =>
            new(ClientCommandType.RequestWarp, multiplier, kind);

        public static ClientCommand ApproveWarp() =>
            new(ClientCommandType.ApproveWarp, 0.0, default);

        public static ClientCommand OccupyStation(VesselId vesselId, Station station) =>
            new(ClientCommandType.OccupyStation, 0.0, default, vesselId, station);
    }
}
