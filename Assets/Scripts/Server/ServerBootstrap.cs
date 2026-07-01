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

        // Flight-sandbox aid (not a spec behaviour): park the demo craft far
        // enough out (~50 Earth radii) that gravity is ~0.004 m/s² — negligible
        // — and zero its orbital velocity on promotion, so it begins genuinely
        // at rest and stays put until you throttle. The client draws a visual
        // launch pad under it. Ignored when _demoStartOnSurface is on. Off → real orbit.
        [SerializeField] private bool _demoStartAtRest = true;

        // Surface launch (M2.5-T02, PHYS-7, ADR-0018): seed the craft resting on
        // Earth's surface at the +Y pole and spawn a static ground pad under it on
        // promotion, so it holds real weight (~9.82 m/s²). Takes precedence over
        // _demoStartAtRest. Off → the far-out at-rest sandbox above.
        [SerializeField] private bool _demoStartOnSurface = true;
        private bool _surfaceMode;
        private SurfaceGroundBody _ground;

        public ServerSimulation Sim { get; private set; }

        private WorldRepository _repo;
        private PersistenceEventSink _persistence;
        private LiteNetLibServerTransport _transport;
        private ServerNetHost _host;

        // Active-physics Unity column (M1-T21, ADR-0014 §1, ADR-0012).
        private UnityBubbleHost _bubbleHost;
        private BubbleIntegrator _integrator;
        private ServerVesselBodies _vesselBodies;

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
            WireActivePhysics();
            WirePersistence();
            if (_demoStartOnSurface) ApplyDemoStartOnSurface(world);
            else if (_demoStartAtRest) ApplyDemoStartAtRest(world);

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
            if (Sim?.Promotion != null) Sim.Promotion.VesselPromoted -= OnDemoPromoted;
            if (_ground != null) Destroy(_ground.gameObject);
            _vesselBodies?.Dispose();
            _bubbleHost?.Dispose();
            _transport?.Dispose();
        }

        // Park the demo craft on a circular element ~50 Earth radii out and
        // zero its orbital velocity the instant it promotes. Gravity there is
        // ~0.004 m/s² (negligible), so it floats at rest and the throttle is the
        // only force you feel. A sandbox initial condition, not an orbit; flip
        // _demoStartAtRest off for the real seed.
        private void ApplyDemoStartAtRest(SimWorld world)
        {
            if (world.Vessels.TryGetValue(WorldSeed.SeedVesselId, out var v))
            {
                var r = WorldSeed.EarthRadius * 50.0;
                v.Orbit = new Orbit(
                    r, 0.0, 0.0, 0.0, 0.0, 0.0,
                    world.Clock.GameTimeSeconds, v.Orbit.ParentBody);
            }
            // Runs after ServerVesselBodies' own promotion handler (subscribed in
            // WireActivePhysics), so the rigid body already exists here.
            Sim.Promotion.VesselPromoted += OnDemoPromoted;
        }

        // Seed the demo craft resting on Earth's surface at the +Y pole
        // (ADR-0018). Same as the at-rest sandbox but at surface radius, where
        // gravity is real (~9.82 m/s²) and a static ground pad (spawned on
        // promotion) holds its weight. Velocity is zeroed on promotion so it
        // starts genuinely at rest in contact, not on the placeholder circular
        // element used only to fix the spawn point.
        private void ApplyDemoStartOnSurface(SimWorld world)
        {
            _surfaceMode = true;
            if (world.Vessels.TryGetValue(WorldSeed.SeedVesselId, out var v))
            {
                var seed = WorldSeed.CreateSurfaceVessel();
                v.Orbit = new Orbit(
                    seed.Orbit.SemiMajorAxis, seed.Orbit.Eccentricity, seed.Orbit.Inclination,
                    seed.Orbit.LongitudeOfAscendingNode, seed.Orbit.ArgumentOfPeriapsis,
                    seed.Orbit.MeanAnomalyAtEpoch, world.Clock.GameTimeSeconds, seed.Orbit.ParentBody);
            }
            Sim.Promotion.VesselPromoted += OnDemoPromoted;
        }

        private void OnDemoPromoted(PromotionEvent e)
        {
            if (!e.VesselId.Equals(WorldSeed.SeedVesselId)) return;
            if (_vesselBodies != null && _vesselBodies.TryGetBody(e.VesselId, out var body) && body != null)
            {
                body.Body.linearVelocity = Vector3.zero;
                body.Body.angularVelocity = Vector3.zero;
            }
            if (_surfaceMode) SpawnGroundUnder(e);
        }

        // Create the static ground pad in the promoted craft's bubble scene,
        // top face at the surface directly beneath it (ADR-0018 §1/§4).
        private void SpawnGroundUnder(PromotionEvent e)
        {
            if (_ground != null) return; // one pad for the demo craft
            if (_bubbleHost == null) return;
            if (!Sim.Bubbles.TryGet(e.BubbleId, out var bubble)) return;
            var scene = _bubbleHost.TryGetScene(e.BubbleId);
            if (!scene.HasValue) return;

            var origin = bubble.GlobalOrigin;
            var craftLocal = new Vector3(
                (float)(e.WorldPosition.X - origin.X),
                (float)(e.WorldPosition.Y - origin.Y),
                (float)(e.WorldPosition.Z - origin.Z));
            _ground = SurfaceGroundFactory.Create(scene.Value, craftLocal);
        }

        // Stand up the active-physics column: seed the demo craft's mass/engines,
        // allocate a PhysicsScene per bubble, and inject the PhysX integrator as
        // the simulation's IBubbleStepper (ADR-0014 §1).
        private void WireActivePhysics()
        {
            Sim.Masses.Set(WorldSeed.SeedVesselId, WorldSeed.CreateMass());
            Sim.Engines.Set(WorldSeed.SeedVesselId, WorldSeed.CreateEngines());

            _bubbleHost = new UnityBubbleHost(Sim.Bubbles);
            var floatingOrigin = new FloatingOriginManager();
            _integrator = new BubbleIntegrator(
                Sim.World, Sim.Bubbles, _bubbleHost, floatingOrigin, Sim.Engines, Sim.Masses);
            Sim.SetBubbleStepper(new BubbleIntegratorStepper(_integrator));
            _vesselBodies = new ServerVesselBodies(Sim, _bubbleHost);
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
