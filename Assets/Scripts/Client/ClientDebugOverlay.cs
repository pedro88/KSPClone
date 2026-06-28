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

        private void OnGUI()
        {
            var peer = _client != null ? _client.Peer : null;
            if (peer == null) return;

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
        }
    }
}
