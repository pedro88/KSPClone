using UnityEngine;
using KSPClone.SimCore;
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

        public ServerSimulation Sim { get; private set; }

        private WorldRepository _repo;
        private PersistenceEventSink _persistence;

        private void Awake()
        {
            var bodies = WorldSeed.CreateBodies();
            var world = RestoreOrSeed(bodies);
            Sim = new ServerSimulation(world);
            WirePersistence();

            Debug.Log($"[server] spine up: {world.Vessels.Count} vessel(s), " +
                      $"gameTime={world.Clock.GameTimeSeconds:F1}, " +
                      $"persistence={(_repo is null ? "off" : "on")}, authoritative=true");
        }

        private void Update()
        {
            Sim.Advance(Time.unscaledDeltaTime);
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
