using System.Linq;
using UnityEngine;
using KSPClone.SimCore;

namespace KSPClone.Server
{
    /// <summary>
    /// In-editor debug HUD + manual control harness for the M0 server spine.
    /// Attach to the same GameObject as <see cref="ServerBootstrap"/>. OnGUI
    /// only — no gameplay logic: it reads <see cref="ServerSimulation"/> state
    /// and pokes its connect/warp surface so you can watch the clock advance,
    /// run a warp vote, and see the auto-limit halt exactly at the next SOI
    /// crossing — all without a real client or transport.
    /// </summary>
    [RequireComponent(typeof(ServerBootstrap))]
    public sealed class ServerDebugOverlay : MonoBehaviour
    {
        [SerializeField] private double _physicsMultiplier = 4.0;
        [SerializeField] private double _onRailsMultiplier = 1000.0;

        private ServerBootstrap _server;

        private void Awake() => _server = GetComponent<ServerBootstrap>();

        private void OnGUI()
        {
            var sim = _server != null ? _server.Sim : null;
            if (sim == null) { GUILayout.Label("Server not ready."); return; }

            GUILayout.BeginArea(new Rect(10, 10, 470, 540), GUI.skin.box);

            var clock = sim.World.Clock;
            GUILayout.Label("== SERVER (authoritative) ==");
            GUILayout.Label($"game-time : {clock.GameTimeSeconds:F2} s    rate x{clock.Rate:0.###}");
            GUILayout.Label($"warp      : {sim.Warp.State}  ({sim.Warp.Vote.Approved.Count}/{sim.Warp.Vote.Required.Count} approved)");
            GUILayout.Label($"players   : {sim.Connections.ConnectedCount}");

            var nextPoi = sim.Pois.EarliestAfter(clock.GameTimeSeconds);
            GUILayout.Label(nextPoi is Poi p
                ? $"next POI  : {p.Type} -> {p.ToBody} @ {p.GameTime:F1} s  (in {p.GameTime - clock.GameTimeSeconds:F1} s)"
                : "next POI  : none in look-ahead");

            GUILayout.Space(6);
            GUILayout.Label("== VESSELS ==");
            foreach (var v in sim.World.Vessels.Values)
            {
                var rkm = v.CachedWorldPosition.HasValue ? v.CachedWorldPosition.Value.Length / 1000.0 : 0.0;
                GUILayout.Label($"  {v.Id.ToString().Substring(0, 8)}  parent={v.Orbit.ParentBody}  " +
                                $"{(v.OnRails ? "on-rails" : "physics")}  r={rkm:F0} km  e={v.Orbit.Eccentricity:0.###}");
            }

            GUILayout.Space(8);
            GUILayout.Label("== CONTROLS ==");
            if (GUILayout.Button("Connect stub player")) sim.Connect();
            if (GUILayout.Button("Disconnect last player") && sim.Connections.ConnectedCount > 0)
                sim.Disconnect(sim.Connections.All.Last().Id);

            using (new GUILayout.HorizontalScope())
            {
                if (GUILayout.Button($"Warp x{_physicsMultiplier:0.#} (Physics)"))
                    RequestWarp(_physicsMultiplier, WarpKind.Physics);
                if (GUILayout.Button($"Warp x{_onRailsMultiplier:0} (OnRails)"))
                    RequestWarp(_onRailsMultiplier, WarpKind.OnRails);
            }

            if (GUILayout.Button("Approve all pending") && sim.Warp.State == WarpState.Voting)
                foreach (var id in sim.Warp.Vote.Pending.ToList())
                    sim.ApproveWarp(id);

            GUILayout.EndArea();
        }

        private void RequestWarp(double multiplier, WarpKind kind)
        {
            var sim = _server.Sim;
            if (sim.Connections.ConnectedCount == 0)
            {
                Debug.LogWarning("[debug] Connect a stub player first.");
                return;
            }
            var requester = sim.Connections.All.First().Id;
            if (!sim.RequestWarp(new WarpRequest(requester, multiplier, kind)))
                Debug.LogWarning("[debug] Warp request refused (already warping, kind/multiplier mismatch, or not warp-safe).");
        }
    }
}
