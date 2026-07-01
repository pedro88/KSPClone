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
        // Ground pads keyed by vessel — the seed craft AND any vessel launched
        // from the VAB that promotes near the surface each get one (M3 launch).
        private readonly System.Collections.Generic.Dictionary<VesselId, SurfaceGroundBody> _grounds = new();

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
            foreach (var g in _grounds.Values) if (g != null) Destroy(g.gameObject);
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
            // Any vessel promoted near a surface (the seed craft or one launched
            // from the VAB) starts at rest on a ground pad. Off-surface promotions
            // (e.g. the 50-Earth-radii at-rest sandbox) keep the old seed-only
            // zeroing and get no pad.
            bool nearSurface = _surfaceMode && IsNearSurface(e.WorldPosition);
            bool zero = nearSurface || (!_surfaceMode && e.VesselId.Equals(WorldSeed.SeedVesselId));
            if (zero && _vesselBodies != null && _vesselBodies.TryGetBody(e.VesselId, out var body) && body != null)
            {
                body.Body.linearVelocity = Vector3.zero;
                body.Body.angularVelocity = Vector3.zero;
            }
            if (nearSurface) SpawnGroundUnder(e);
        }

        // Within ~2 km of Earth's surface, in the world frame.
        private bool IsNearSurface(Vector3d worldPos)
        {
            if (Sim?.World?.Bodies is null) return false;
            var earth = Sim.World.Bodies.WorldPositionOf(CelestialBodyId.Planet, Sim.World.Clock.GameTimeSeconds);
            return (worldPos - earth).Length - WorldSeed.EarthRadius < 2000.0;
        }

        // Create the static ground pad in the promoted craft's bubble scene,
        // top face at the surface directly beneath it (ADR-0018 §1/§4).
        private void SpawnGroundUnder(PromotionEvent e)
        {
            if (_grounds.ContainsKey(e.VesselId)) return; // one pad per vessel
            if (_bubbleHost == null) return;
            if (!Sim.Bubbles.TryGet(e.BubbleId, out var bubble)) return;
            var scene = _bubbleHost.TryGetScene(e.BubbleId);
            if (!scene.HasValue) return;

            var origin = bubble.GlobalOrigin;
            var craftLocal = new Vector3(
                (float)(e.WorldPosition.X - origin.X),
                (float)(e.WorldPosition.Y - origin.Y),
                (float)(e.WorldPosition.Z - origin.Z));
            _grounds[e.VesselId] = SurfaceGroundFactory.Create(scene.Value, craftLocal);
        }

        // Stand up the active-physics column: seed the demo craft's mass/engines,
        // allocate a PhysicsScene per bubble, and inject the PhysX integrator as
        // the simulation's IBubbleStepper (ADR-0014 §1).
        private void WireActivePhysics()
        {
            Sim.Masses.Set(WorldSeed.SeedVesselId, WorldSeed.CreateMass());
            // Surface-launch demo aid: the spec seed craft has TWR ~1.2 and only
            // ~2.7 km/s Δv, so it can barely lift off and can't escape Earth —
            // frustrating for a hand-flying test. Give the sandbox an arcade
            // engine (TWR ~4, ~15 km/s Δv) so liftoff is punchy and escape is
            // reachable. Not a spec behaviour; realistic seed stays in WorldSeed.
            Sim.Engines.Set(WorldSeed.SeedVesselId,
                _demoStartOnSurface ? CreateDemoLaunchEngines() : WorldSeed.CreateEngines());

            _bubbleHost = new UnityBubbleHost(Sim.Bubbles);
            var floatingOrigin = new FloatingOriginManager();
            _integrator = new BubbleIntegrator(
                Sim.World, Sim.Bubbles, _bubbleHost, floatingOrigin, Sim.Engines, Sim.Masses);
            Sim.SetBubbleStepper(new BubbleIntegratorStepper(_integrator));
            _vesselBodies = new ServerVesselBodies(Sim, _bubbleHost);
        }

        // Arcade launch engine for the surface demo (see WireActivePhysics).
        // 200 kN on the 5 t craft → TWR ~4; Isp 1000 s + 4 t propellant →
        // ~15 km/s Δv (Earth escape is 11.2 km/s). Thrust up the vessel +Y.
        private static EngineModule[] CreateDemoLaunchEngines() => new[]
        {
            new EngineModule(
                name: "demo-launch",
                thrustNewtons: 200_000.0,
                ispSeconds: 1_000.0,
                mountLocal: new Vector3d(0.0, -2.0, 0.0),
                thrustDirLocal: new Vector3d(0.0, 1.0, 0.0),
                propellantKg: 4_000.0),
        };

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
