using UnityEngine;
using KSPClone.Net;
using KSPClone.SimCore;

namespace KSPClone.Client
{
    /// <summary>
    /// Unity host for a player's client. Opens a LiteNetLib UDP connection,
    /// occupies the Pilot station of the first vessel it learns about, and runs
    /// the M1 flight loop (M1-T24): read input → predict locally same-frame →
    /// send to the authoritative server → reconcile against snapshots. Other
    /// vessels are interpolated. The server is the sole authority (NET-1); local
    /// prediction only hides input latency (NET-2).
    /// </summary>
    public sealed class ClientBootstrap : MonoBehaviour
    {
        [SerializeField] private string _host = "127.0.0.1";
        [SerializeField] private int _port = 9050;
        [SerializeField] private float _attitudeRateRadPerSec = 0.5f;

        public ClientNetPeer Peer { get; private set; }
        public ClientFlightModel Flight { get; } = new();

        private LiteNetLibClientTransport _transport;

        // Start (not Awake): if the server lives in the same scene, its Awake
        // has already started the listener by the time we connect here.
        private void Start()
        {
            _transport = new LiteNetLibClientTransport();
            Peer = new ClientNetPeer(_transport);
            Peer.HandshakeReceived += OnHandshake;
            Peer.SnapshotReceived += Flight.OnSnapshotBundle;
            _transport.Connect(_host, _port);
            Debug.Log($"[client] connecting to {_host}:{_port}");
        }

        private void OnHandshake(WorldHandshakeMessage handshake)
        {
            if (handshake.Vessels.Count == 0) return;
            // Slice convention: take control of the first vessel (ADR-0016).
            var target = handshake.Vessels[0].Id;
            Flight.Control(target);
            Peer.OccupyStation(target, Station.Pilot);
            Debug.Log($"[client] occupying Pilot of vessel {target}");
        }

        private void Update()
        {
            Peer?.Poll();
            if (Flight.ControlledVesselId is null) return;

            float throttle = Input.GetKey(KeyCode.LeftShift) ? 1f : 0f;
            float pitch = Input.GetAxis("Vertical") * _attitudeRateRadPerSec;
            float yaw = Input.GetAxis("Horizontal") * _attitudeRateRadPerSec;
            float roll = ((Input.GetKey(KeyCode.E) ? 1f : 0f) - (Input.GetKey(KeyCode.Q) ? 1f : 0f)) * _attitudeRateRadPerSec;

            var msg = Flight.CaptureInput(throttle, pitch, yaw, roll);
            if (msg.HasValue) Peer.SendPilotInput(msg.Value);
        }

        private void OnDestroy()
        {
            _transport?.Dispose();
        }
    }
}
