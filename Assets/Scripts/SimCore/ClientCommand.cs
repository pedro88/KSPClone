namespace KSPClone.SimCore
{
    public enum ClientCommandType
    {
        RequestWarp = 0,
        ApproveWarp = 1,
    }

    /// <summary>
    /// A client → server command (wire-agnostic data). The only inputs M0
    /// carries: request a warp and approve an open warp vote. The requesting /
    /// approving player is identified by the connection the command arrives on
    /// (the server maps peer → <see cref="PlayerId"/>), so it is not in the
    /// payload. Serialized by the transport's wire codec, never by SimCore.
    /// </summary>
    public readonly struct ClientCommand
    {
        public ClientCommandType Type { get; }
        public double Multiplier { get; }
        public WarpKind Kind { get; }

        public ClientCommand(ClientCommandType type, double multiplier, WarpKind kind)
        {
            Type = type;
            Multiplier = multiplier;
            Kind = kind;
        }

        public static ClientCommand RequestWarp(double multiplier, WarpKind kind) =>
            new(ClientCommandType.RequestWarp, multiplier, kind);

        public static ClientCommand ApproveWarp() =>
            new(ClientCommandType.ApproveWarp, 0.0, default);
    }
}
