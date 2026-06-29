using System;
using System.Collections.Generic;
using KSPClone.SimCore;

namespace KSPClone.Net
{
    /// <summary>
    /// Client-side connection: decodes the world handshake into a read-only
    /// <see cref="ClientWorldModel"/>, buffers incoming snapshots per vessel and
    /// interpolates them (NET-4), sends warp request/approve commands up, and —
    /// for M1 — occupies stations and sends pilot input. The server is the sole
    /// authority (NET-1); inputs are applied only if the server accepts them.
    ///
    /// Decoded messages are also surfaced via <see cref="HandshakeReceived"/> /
    /// <see cref="SnapshotReceived"/> so the M1 <c>ClientFlightModel</c> can
    /// predict the controlled vessel and reconcile against snapshots.
    /// </summary>
    public sealed class ClientNetPeer
    {
        public ClientWorldModel World { get; } = new();
        public double ServerGameTime { get; private set; }
        public double InterpolationDelay { get; set; } = 0.1;

        /// <summary>Fired when the world handshake arrives (vessel list + game time).</summary>
        public event Action<WorldHandshakeMessage>? HandshakeReceived;
        /// <summary>Fired for each authoritative snapshot bundle.</summary>
        public event Action<SnapshotBundle>? SnapshotReceived;

        private readonly IClientTransport _transport;
        private readonly Dictionary<VesselId, VesselInterpolator> _interpolators = new();

        public ClientNetPeer(IClientTransport transport)
        {
            _transport = transport;
            _transport.Received += OnReceived;
        }

        public void Poll() => _transport.Poll();

        public void RequestWarp(double multiplier, WarpKind kind) =>
            _transport.Send(WireCodec.EncodeClientCommand(ClientCommand.RequestWarp(multiplier, kind)));

        public void ApproveWarp() =>
            _transport.Send(WireCodec.EncodeClientCommand(ClientCommand.ApproveWarp()));

        /// <summary>Ask the server to occupy a station (ADR-0016). Pilot triggers promotion/resume.</summary>
        public void OccupyStation(VesselId vesselId, Station station) =>
            _transport.Send(WireCodec.EncodeClientCommand(ClientCommand.OccupyStation(vesselId, station)));

        /// <summary>Send a pilot input on the same channel as state (NET-1/NET-2).</summary>
        public void SendPilotInput(PilotInputMessage input) =>
            _transport.Send(WireCodec.EncodePilotInput(input));

        /// <summary>Interpolated render position of a vessel; false if none received yet.</summary>
        public bool TrySampleVessel(VesselId id, out Vector3d position)
        {
            if (_interpolators.TryGetValue(id, out var interp))
            {
                position = interp.Sample(ServerGameTime);
                return true;
            }
            position = Vector3d.Zero;
            return false;
        }

        private void OnReceived(byte[] data)
        {
            switch (WireCodec.PeekType(data))
            {
                case MessageType.Handshake:
                    var handshake = WireCodec.DecodeHandshake(data);
                    World.ApplyHandshake(handshake);
                    ServerGameTime = handshake.GameTimeSeconds;
                    HandshakeReceived?.Invoke(handshake);
                    break;

                case MessageType.Snapshot:
                    var bundle = WireCodec.DecodeSnapshot(data);
                    ServerGameTime = bundle.GameTime;
                    foreach (var snap in bundle.Vessels)
                    {
                        if (!_interpolators.TryGetValue(snap.VesselId, out var interp))
                        {
                            interp = new VesselInterpolator { InterpolationDelay = InterpolationDelay };
                            _interpolators[snap.VesselId] = interp;
                        }
                        interp.OnSnapshot(snap);
                    }
                    SnapshotReceived?.Invoke(bundle);
                    break;
            }
        }
    }
}
