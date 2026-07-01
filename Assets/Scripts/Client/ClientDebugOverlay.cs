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

        // Launch-frame reference, captured once at first real position. The pad
        // is world-static while Earth orbits the Sun at ~29.8 km/s, so measuring
        // altitude against the *live* Earth centre makes it plummet even at rest.
        // We freeze the launch vertical (up0) and pad altitude here and measure
        // climb along that instead — stable for the demo (ADR-0018 approximation).
        private Vector3d? _spawn;
        private Vector3d _up0 = new(0, 1, 0);
        private double _spawnAltAsl;

        private void OnGUI()
        {
            var peer = _client != null ? _client.Peer : null;
            if (peer == null) return;

            // Nav readout for the controlled vessel (predicted state). "Up" is
            // radial from the parent body's centre, not from the world origin —
            // on a surface launch the body is ~1 AU from the Sun, so origin-radial
            // would be meaningless (ADR-0018).
            var flight = _client.Flight;
            if (flight.ControlledVesselId is not null && flight.ControlledState.Position.LengthSquared >= 1.0)
            {
                var pos = flight.ControlledState.Position;
                var vel = flight.ControlledState.Velocity;

                var bodies = _client.SeedBodies;
                var parentId = peer.World.Vessels.TryGetValue(flight.ControlledVesselId.Value, out var cv)
                    ? cv.Orbit.ParentBody : CelestialBodyId.Planet;

                // Freeze the launch vertical + pad altitude once (see field docs).
                if (_spawn is null)
                {
                    _spawn = pos;
                    if (bodies != null)
                    {
                        var ec0 = bodies.WorldPositionOf(parentId, peer.ServerGameTime);
                        var r0 = pos - ec0;
                        double d0 = r0.Length;
                        _up0 = d0 > 1e-6 ? r0 * (1.0 / d0) : new Vector3d(0, 1, 0);
                        _spawnAltAsl = d0 - SurfaceRadiusOf(parentId);
                    }
                }

                // Climb along the frozen launch vertical — Earth's orbital motion
                // doesn't leak in (unlike distance to the live body centre).
                double climb = Vector3d.Dot(pos - _spawn.Value, _up0);
                double altitude = _spawnAltAsl + climb;
                double speed = vel.Length;
                double vertical = Vector3d.Dot(vel, _up0);
                double horizontal = System.Math.Sqrt(System.Math.Max(0.0, speed * speed - vertical * vertical));
                // Flight-path angle: +90° = straight up, 0° = horizontal.
                double fpaDeg = speed > 1e-3
                    ? System.Math.Asin(System.Math.Max(-1.0, System.Math.Min(1.0, vertical / speed))) * (180.0 / System.Math.PI)
                    : 0.0;

                GUILayout.BeginArea(new Rect(Screen.width - 260, Screen.height - 170, 250, 160), GUI.skin.box);
                GUILayout.Label("== NAV ==");
                GUILayout.Label($"body      : {parentId}");
                GUILayout.Label(System.Math.Abs(altitude) < 10_000.0
                    ? $"altitude  : {altitude:F0} m ASL"
                    : $"altitude  : {altitude / 1000.0:F1} km ASL");
                GUILayout.Label($"speed     : {speed:F1} m/s");
                GUILayout.Label($"vertical  : {vertical:+0.0;-0.0;0.0} m/s");
                GUILayout.Label($"horizontal: {horizontal:F1} m/s");
                GUILayout.Label($"path angle: {fpaDeg:+0.0;-0.0;0.0}°");
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
            GUILayout.Label("Shift/Ctrl : throttle up/down   X: cut  Z: full");
            GUILayout.Label("W / S : pitch");
            GUILayout.Label("A / D : yaw");
            GUILayout.Label("Q / E : roll");
            GUILayout.Label("Right-drag : orbit camera   Wheel : zoom");
            GUILayout.Space(6);
            GUILayout.Label("cyan capsule  = your vessel");
            GUILayout.Label("grey cubes    = world reference (speed)");
            GUILayout.Label("orange plume  = thrust");
            GUILayout.Label("green grid    = ground (sinks away as you climb)");
            GUILayout.Label("Earth globe   = straight down — right-drag to look back");
            GUILayout.EndArea();
        }

        // Surface radius of a parent body (metres), for altitude-above-surface.
        private static double SurfaceRadiusOf(CelestialBodyId id) => id switch
        {
            CelestialBodyId.Sun    => WorldSeed.SunRadius,
            CelestialBodyId.Planet => WorldSeed.EarthRadius,
            CelestialBodyId.Moon   => 1.737e6,
            _ => 0.0,
        };
    }
}
