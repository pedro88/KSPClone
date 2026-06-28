using UnityEngine;
using KSPClone.SimCore;
using KSPClone.Net;
using KSPClone.Persistence;

namespace KSPClone.Server
{
    /// <summary>
    /// Unity host for the authoritative server (Constitution Art. 1). Assembles
    /// the engine-agnostic <see cref="ServerSimulation"/> spine, restores or seeds
    /// the world from Postgres, wires write-through persistence, and drives the
    /// fixed-step sim from the host update loop (Art. 2).
    /// </summary>
    public sealed class ServerBootstrap : MonoBehaviour
    {
        [SerializeField]
        private string _connectionString =
            "Host=localhost;Port=5433;Username=greenu;Password=greenu;Database=greenu";

        [SerializeField] private int _port = 9050;

        public ServerSimulation Sim { get; private set; }

        private WorldRepository _repo;
        private PersistenceEventSink _persistence;
        private LiteNetLibServerTransport _transport;
        private ServerNetHost _host;

        private void Awake()
        {
            // Headless dedicated server: keep ticking unfocused and don't throttle
            // the host loop to a (non-existent) display. The sim cadence is the
            // fixed-step accumulator (Art. 2), so a 60 fps host loop is plenty.
            Application.runInBackground = true;
            if (Application.isBatchMode)
            {
                QualitySettings.vSyncCount = 0;
                Application.targetFrameRate = 60;
                Debug.Log("[server] batchmode: headless dedicated server (no renderer)");
            }

            var bodies = WorldSeed.CreateBodies();
            var world = RestoreOrSeed(bodies);
            Sim = new ServerSimulation(world);
            WirePersistence();

            _transport = new LiteNetLibServerTransport();
            _transport.Start(_port);
            _host = new ServerNetHost(_transport, Sim);

            Debug.Log($"[server] spine up: {world.Vessels.Count} vessel(s), " +
                      $"gameTime={world.Clock.GameTimeSeconds:F1}, " +
                      $"persistence={(_repo is null ? "off" : "on")}, listening on :{_port}, authoritative=true");
        }

        private void Update()
        {
            Sim.Advance(Time.unscaledDeltaTime);
            _host.Poll();
        }

        private void OnDestroy()
        {
            _transport?.Dispose();
        }

        private SimWorld RestoreOrSeed(BodyRegistry bodies)
        {
            try
            {
                _repo = new WorldRepository(_connectionString);
                _repo.Migrate();
                return new WorldRestorer(_repo).RestoreOrSeed(bodies, WorldSeed.Seed);
            }
            catch (System.Exception e)
            {
                // No DB reachable: run the living universe in-memory (no durability).
                Debug.LogWarning($"[server] Postgres unavailable ({e.Message}); seeding in-memory, persistence disabled.");
                _repo = null;
                var world = new SimWorld(bodies);
                WorldSeed.Seed(world);
                return world;
            }
        }

        private void WirePersistence()
        {
            if (_repo is null) return;
            _persistence = new PersistenceEventSink(_repo, Sim.World);
            Sim.VesselReParented += _persistence.OnSoiTransition;
            Sim.WarpCommitted += _persistence.OnWarpCommit;
        }
    }
}
