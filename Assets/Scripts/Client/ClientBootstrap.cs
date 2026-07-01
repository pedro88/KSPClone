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
        // Predictor thrust accel roughly matches the demo launch engine's
        // full-throttle net liftoff (~30 m/s² = 200 kN / 5 t − g); keeps
        // prediction close to the server so reconciliation barely corrects.
        public ClientFlightModel Flight { get; } = new(new TrivialPredictionStep(thrustAccel: 30.0));

        private LiteNetLibClientTransport _transport;
        private ClientNetPeer _peer;
        private ClientWorldRenderer _renderer;
        private CelestialSkyboxRenderer _skybox;
        // Deterministic seed hierarchy, reconstructed once client-side
        // (PERSIST-3). Bodies never change during a session, so we don't
        // rebuild the registry every frame.
        private BodyRegistry _seedBodies;
        public BodyRegistry SeedBodies => _seedBodies; // read-only, for the nav HUD
        private float _lastThrottle;
        private Vector3 _lastAttitude;

        // Start (not Awake): if the server lives in the same scene, its Awake
        // has already started the listener by the time we connect here.
        private void Start()
        {
            _transport = new LiteNetLibClientTransport();
            Peer = new ClientNetPeer(_transport);
            Peer.HandshakeReceived += OnHandshake;
            Peer.SnapshotReceived += Flight.OnSnapshotBundle;
            _renderer = new ClientWorldRenderer(Camera.main != null ? Camera.main.transform : null);
            _skybox = new CelestialSkyboxRenderer(Camera.main != null ? Camera.main.transform : null);
            _seedBodies = WorldSeed.CreateBodies();
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
            _lastThrottle = throttle;
            float pitch = Input.GetAxis("Vertical") * _attitudeRateRadPerSec;
            float yaw = Input.GetAxis("Horizontal") * _attitudeRateRadPerSec;
            float roll = ((Input.GetKey(KeyCode.E) ? 1f : 0f) - (Input.GetKey(KeyCode.Q) ? 1f : 0f)) * _attitudeRateRadPerSec;

            _lastAttitude = new Vector3(pitch, yaw, roll);

            var msg = Flight.CaptureInput(throttle, pitch, yaw, roll);
            if (msg.HasValue) Peer.SendPilotInput(msg.Value);
        }

        private void LateUpdate()
        {
            _renderer?.Render(Flight, _lastThrottle, _lastAttitude);
            // Skybox rides the same float-local frame as the renderer; the
            // controlled vessel anchors the camera, so bodies' world positions
            // resolve to local directions on the inverted-sphere shell.
            // The seed BodyRegistry is reconstructed client-side from
            // WorldSeed (deterministic — PERSIST-3); future custom-body systems
            // will need it serialised in the handshake.
            if (_skybox != null && Flight.ControlledVesselId is not null)
            {
                var cam = Camera.main;
                if (cam != null)
                {
                    var pos = Flight.ControlledState.Position;
                    var sunDir = _seedBodies.WorldPositionOf(CelestialBodyId.Sun, Flight.ServerGameTime)
                                 - pos;
                    _skybox.Render(_seedBodies, pos, Flight.ServerGameTime, sunDir);
                }
            }
        }

        private void OnDestroy()
        {
            _renderer?.Clear();
            _skybox?.Clear();
            _transport?.Dispose();
        }
    }
}