using UnityEngine;
using KSPClone.SimCore;

namespace KSPClone.Client
{
    /// <summary>
    /// Client-side debug HUD: shows what the observing client knows purely from
    /// the wire (handshake + interpolated snapshots) and lets you send warp
    /// request/approve commands up. Read-only mirror — no authority. Attach to
    /// the same GameObject as <see cref="ClientBootstrap"/>.
    /// </summary>
    [RequireComponent(typeof(ClientBootstrap))]
    public sealed class ClientDebugOverlay : MonoBehaviour
    {
        [SerializeField] private double _onRailsMultiplier = 1000.0;

        private ClientBootstrap _client;

        private void Awake() => _client = GetComponent<ClientBootstrap>();

        private Vector3d? _spawn;

        private void OnGUI()
        {
            var peer = _client != null ? _client.Peer : null;
            if (peer == null) return;

            // Flight readout for the controlled vessel (predicted state).
            var flight = _client.Flight;
            if (flight.ControlledVesselId is not null && flight.ControlledState.Position.LengthSquared >= 1.0)
            {
                var pos = flight.ControlledState.Position;
                var vel = flight.ControlledState.Velocity;
                _spawn ??= pos;   // captured only once the position is real (reconciled)
                double rmag = pos.Length;
                var up = rmag > 1e-6 ? pos * (1.0 / rmag) : new Vector3d(0, 1, 0);
                double vertical = Vector3d.Dot(vel, up);
                double altitude = rmag - _spawn.Value.Length;   // height gained since launch

                GUILayout.BeginArea(new Rect(10, 10, 240, 110), GUI.skin.box);
                GUILayout.Label("== FLIGHT ==");
                GUILayout.Label($"speed     : {vel.Length:F1} m/s");
                GUILayout.Label($"vertical  : {vertical:+0.0;-0.0;0.0} m/s");
                GUILayout.Label($"altitude  : {altitude:F0} m (since launch)");
                GUILayout.EndArea();
            }

            GUILayout.BeginArea(new Rect(Screen.width - 380, 10, 370, 420), GUI.skin.box);

            GUILayout.Label("== CLIENT (observer, no authority) ==");
            var connected = peer.World.Vessels.Count > 0;
            GUILayout.Label($"connected  : {(connected ? "yes" : "waiting for handshake...")}");
            GUILayout.Label($"server t   : {peer.ServerGameTime:F2} s");

            GUILayout.Label("vessels (interpolated from snapshots):");
            foreach (var kv in peer.World.Vessels)
            {
                var sampled = peer.TrySampleVessel(kv.Key, out var pos);
                var rkm = sampled ? pos.Length / 1000.0 : 0.0;
                GUILayout.Label($"  {kv.Key.ToString().Substring(0, 8)}  " +
                                $"parent={kv.Value.Orbit.ParentBody}  r={rkm:F0} km" +
                                (sampled ? "" : "  (no snapshot yet)"));
            }

            GUILayout.Space(8);
            GUILayout.Label("== SEND COMMAND ==");
            if (GUILayout.Button($"Request warp x{_onRailsMultiplier:0} (OnRails)"))
                peer.RequestWarp(_onRailsMultiplier, WarpKind.OnRails);
            if (GUILayout.Button("Approve warp"))
                peer.ApproveWarp();

            GUILayout.EndArea();

            // Pilot controls cheat-sheet + legend for the 3D view.
            GUILayout.BeginArea(new Rect(10, Screen.height - 230, 360, 220), GUI.skin.box);
            GUILayout.Label("== CONTROLS (Pilot) ==");
            GUILayout.Label("Shift : throttle (hold = full)");
            GUILayout.Label("W / S : pitch");
            GUILayout.Label("A / D : yaw");
            GUILayout.Label("Q / E : roll");
            GUILayout.Label("Right-drag : orbit camera   Wheel : zoom");
            GUILayout.Space(6);
            GUILayout.Label("cyan capsule  = your vessel");
            GUILayout.Label("grey cubes    = world reference (speed)");
            GUILayout.Label("orange plume  = thrust");
            GUILayout.Label("dark plane    = launch pad (hold Shift to lift off!)");
            GUILayout.EndArea();
        }
    }
}
