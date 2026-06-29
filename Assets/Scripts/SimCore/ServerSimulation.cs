#nullable enable annotations

using System;

namespace KSPClone.SimCore
{
    /// <summary>
    /// Engine-agnostic composition of the authoritative server spine. Owns the
    /// <see cref="SimWorld"/> and drives every per-tick subsystem in fixed-step
    /// cadence (ADR-0014 §2):
    ///
    ///   SOI re-parent → promotion → clustering → <see cref="IBubbleStepper"/>
    ///   → demotion → suspension → warp auto-limit → snapshot emission
    ///
    /// plus the warp-vote FSM, vote-membership sync, and connection registry.
    /// The one engine-coupled step (active-physics integration) is injected as
    /// an <see cref="IBubbleStepper"/> — the no-op default runs headless; the
    /// Unity host supplies a PhysX adapter (ADR-0014 §1). No transport, no
    /// engine references (Constitution Art. 1/2, ADR-0009).
    /// </summary>
    public sealed class ServerSimulation
    {
        public SimWorld World { get; }
        public BodyRegistry Bodies { get; }
        public ConnectionRegistry Connections { get; }
        public PoiRegistry Pois { get; }
        public WarpStateMachine Warp { get; }
        public SnapshotEmitter Snapshots { get; }

        // --- M1 active-physics surface (ADR-0014) ---
        public BubbleRegistry Bubbles { get; }
        public BubbleManager BubbleManager { get; }
        public PromotionController Promotion { get; }
        public DemotionController Demotion { get; }
        public SuspensionController Suspension { get; }
        public SnapshotStore SuspendedSnapshots { get; }
        /// <summary>Mass + engine specs by vessel id. Owned here (ADR-0016 craft-seed decision); populated by the host from the world seed; consumed by the injected <see cref="IBubbleStepper"/>.</summary>
        public VesselMassRegistry Masses { get; }
        public VesselEngineRegistry Engines { get; }

        // --- Crew / control (ADR-0016, M2) ---
        public ControlRegistry Controls { get; }
        public InputChannel Inputs { get; }
        /// <summary>Drives unoccupied stations from their automation fallback each tick (CREW-4).</summary>
        public StationDriver Stations { get; }
        /// <summary>Pilot inputs dropped because the sender did not occupy the vessel's Pilot station (NET-1).</summary>
        public int RejectedPilotInputs { get; private set; }
        /// <summary>Station inputs dropped because the sender did not occupy the station that owns the targeted system (CREW-1, Art. 6).</summary>
        public int RejectedStationInputs { get; private set; }

        public long TickCount => _scheduler.TickCount;

        /// <summary>Fired when a vessel re-parents at an SOI crossing (for persistence write-through).</summary>
        public event Action<Vessel>? VesselReParented;
        /// <summary>Fired at a committed warp endpoint (for persistence write-through).</summary>
        public event Action<double>? WarpCommitted;
        /// <summary>Fired for each emitted snapshot bundle (the transport broadcasts it).</summary>
        public event Action<SnapshotBundle>? SnapshotEmitted;

        private readonly SimScheduler _scheduler;
        private readonly PoiScanner _poiScanner;
        private readonly SoiTransition _soiTransition;
        private readonly WarpAutoLimit _autoLimit;
        private readonly WarpSafeEvaluator _warpSafe;
        private IBubbleStepper _stepper;

        // Whether a vessel currently has a present crew member. Defaults to
        // "unoccupied"; the control layer (ADR-0016) overrides it via
        // SetOccupancyLookup so the leave → demote-or-suspend branch fires.
        private Func<VesselId, bool> _occupancy = _ => false;

        public ServerSimulation(
            SimWorld world,
            IBubbleStepper? stepper = null,
            double snapshotRateHz = SnapshotEmitter.DefaultRateHz)
        {
            World = world ?? throw new ArgumentNullException(nameof(world));
            Bodies = world.Bodies ?? throw new ArgumentException("World must carry a BodyRegistry.", nameof(world));
            _stepper = stepper ?? NullBubbleStepper.Instance;

            Connections = new ConnectionRegistry();
            Pois = new PoiRegistry();
            _poiScanner = new PoiScanner(World, Bodies, Pois);
            _soiTransition = new SoiTransition(World, Bodies, Pois);
            Warp = new WarpStateMachine(World.Clock, Connections,
                onRailsWarpSafe: () => WarpPolicy.AllWarpSafe(World.Vessels.Values));
            _autoLimit = new WarpAutoLimit(World, Pois, Warp);
            // Subscribes to connect/disconnect events; kept alive by those delegates.
            new WarpMembershipSync(Connections, Warp);
            Snapshots = new SnapshotEmitter(World, Connections, snapshotRateHz,
                onBundle: b => SnapshotEmitted?.Invoke(b));

            // M1 active-physics composition (ADR-0014 §2). Demotion takes a
            // stable forwarder so a later SetOccupancyLookup is honoured.
            Bubbles = new BubbleRegistry();
            BubbleManager = new BubbleManager(Bubbles);
            Promotion = new PromotionController(World, BubbleManager, Bubbles);
            _warpSafe = new WarpSafeEvaluator();
            Demotion = new DemotionController(World, Bubbles, _warpSafe, vid => _occupancy(vid));
            SuspendedSnapshots = new SnapshotStore();
            Suspension = new SuspensionController(World, Bubbles, SuspendedSnapshots, _warpSafe);
            Masses = new VesselMassRegistry();
            Engines = new VesselEngineRegistry();

            Controls = new ControlRegistry();
            Inputs = new InputChannel(World);
            Stations = new StationDriver();
            // A vessel is "attended" iff any of its stations is occupied; the
            // demotion/suspension passes consult this via the forwarder above.
            SetOccupancyLookup(Controls.IsOccupied);

            // Refresh POIs and arm the auto-limit each time a warp goes Active.
            Warp.WarpStarted += _ => { _poiScanner.RescanAll(); _autoLimit.Arm(); };
            _autoLimit.WarpCommitted += t => WarpCommitted?.Invoke(t);
            // A re-parented vessel changes its POIs; re-scan and surface for persistence.
            _soiTransition.VesselReParented += (id, _) =>
            {
                _poiScanner.RescanAll();
                if (World.Vessels.TryGetValue(id, out var v)) VesselReParented?.Invoke(v);
            };

            // Per-tick subsystem work runs after the clock advances + on-rails sync.
            World.TickRecorded += OnTick;

            _scheduler = new SimScheduler(World);

            // Initial POI scan over the seeded/restored world.
            _poiScanner.RescanAll();
        }

        /// <summary>Feed real elapsed seconds; runs the fixed 60 Hz step internally.</summary>
        public void Advance(double realSeconds) => _scheduler.Advance(realSeconds);

        /// <summary>
        /// Replace the occupancy predicate the demotion/suspension passes
        /// consult (ADR-0016: wired to the control registry). A vessel that
        /// returns true is treated as crewed and never demoted/suspended.
        /// </summary>
        public void SetOccupancyLookup(Func<VesselId, bool> lookup)
            => _occupancy = lookup ?? throw new ArgumentNullException(nameof(lookup));

        /// <summary>
        /// Inject the engine-coupled integration step (ADR-0014 §1). The Unity
        /// host calls this after building a <c>BubbleIntegrator</c> over this
        /// simulation's <see cref="Bubbles"/>/<see cref="Engines"/>/<see cref="Masses"/>
        /// registries (which exist only once the simulation is constructed).
        /// </summary>
        public void SetBubbleStepper(IBubbleStepper stepper)
            => _stepper = stepper ?? throw new ArgumentNullException(nameof(stepper));

        // The canonical per-tick order (ADR-0014 §2). Runs after SimWorld.Tick
        // has advanced the master clock and synced on-rails caches.
        private void OnTick(double dtSeconds)
        {
            var now = World.Clock.GameTimeSeconds;

            _soiTransition.ApplyDue(now);                  // 1. on-rails SOI re-parent (M0)
            Promotion.RunPass(now);                        // 2. on-rails → active-physics
            BubbleManager.RunClusteringPass(World.Vessels.Values); // 3. assign / merge / split bubbles
            Stations.Tick(World.Vessels.Values, Controls, dtSeconds); // 3b. automation fills unoccupied stations (CREW-4)
            _stepper.Step(dtSeconds);                      // 4. active-physics integration (injected)
            Demotion.RunPass();                            // 5. unattended + warp-safe → on-rails
            Suspension.RunSuspensionPass(vid => _occupancy(vid)); // 6. unattended + not-safe → suspended
            _autoLimit.Tick();                             // 7. warp auto-limit (M0)
            Snapshots.Tick(dtSeconds);                     // 8. emit (sees post-physics, post-transition)
        }

        // --- Connection / warp surface for the transport layer ---

        public (PlayerSession session, WorldHandshakeMessage handshake) Connect()
        {
            var session = Connections.AddNew();
            var handshake = new WorldHandshakeBuilder(World).Build();
            return (session, handshake);
        }

        public bool Disconnect(PlayerId id)
        {
            // Vacating leaves the vessel unattended, so the next tick routes it
            // to demotion (warp-safe) or suspension (not) — CONTEXT: Occupying.
            Controls.Vacate(id);
            return Connections.Remove(id);
        }

        public bool RequestWarp(WarpRequest request) => Warp.RequestWarp(request);

        public void ApproveWarp(PlayerId id) => Warp.Approve(id);

        // --- Crew / control surface for the transport layer (ADR-0016) ---

        /// <summary>
        /// Occupy a station of a vessel. Occupying the <see cref="Station.Pilot"/>
        /// seat triggers the vessel's transition into active physics — promotion
        /// for an on-rails vessel, resume for a suspended one. Returns false if
        /// the vessel is unknown or the station is already held by another player.
        /// </summary>
        public bool OccupyStation(PlayerId player, VesselId vessel, Station station)
        {
            if (!World.Vessels.TryGetValue(vessel, out var v)) return false;
            if (!Controls.Occupy(player, vessel, station)) return false;

            if (station == Station.Pilot)
            {
                if (v.State == VesselState.Suspended) Suspension.Resume(vessel);
                else if (v.State == VesselState.OnRails) Promotion.RequestPlayerLoad(vessel);
            }
            return true;
        }

        /// <summary>
        /// Apply a pilot input authoritatively (NET-1). Accepted only from the
        /// player occupying the vessel's Pilot station; otherwise dropped and
        /// counted on <see cref="RejectedPilotInputs"/>.
        /// </summary>
        public bool SubmitPilotInput(PlayerId player, PilotInputMessage input)
        {
            var owner = Controls.Owner(input.VesselId, Station.Pilot);
            if (owner is not { } o || !o.Equals(player))
            {
                RejectedPilotInputs++;
                return false;
            }
            return Inputs.Submit(input);
        }

        /// <summary>
        /// Apply an input to one controllable system, gated by the station
        /// partition (CREW-1, Art. 6): accepted only if the sender occupies the
        /// station that owns <paramref name="system"/> (per <see cref="StationSystemMap"/>);
        /// otherwise dropped and counted on <see cref="RejectedStationInputs"/>.
        /// A Pilot seated player sending a Staging input is refused because
        /// Pilot does not own Staging. Only systems with a live subsystem act
        /// today (Throttle); the rest are accepted but inert until their
        /// subsystem lands (Engineer staging, Navigator nodes — later slices).
        /// </summary>
        public bool SubmitStationInput(PlayerId player, VesselId vesselId, ControllableSystem system, double value = 0.0)
        {
            var owningStation = StationSystemMap.OwnerOf(system);
            var occupant = Controls.Owner(vesselId, owningStation);
            if (occupant is not { } o || !o.Equals(player))
            {
                RejectedStationInputs++;
                return false;
            }
            if (!World.Vessels.TryGetValue(vesselId, out var vessel))
            {
                RejectedStationInputs++;
                return false;
            }

            switch (system)
            {
                case ControllableSystem.Throttle:
                    vessel.ThrottleCommand = value < 0.0 ? 0.0 : (value > 1.0 ? 1.0 : value);
                    break;
                // Attitude rides the Pilot's PilotInputMessage bundle (a 3-axis
                // rate, not a scalar); other systems have no subsystem yet and
                // are accepted-but-inert so the disjoint-routing contract holds
                // before the subsystems exist.
                default:
                    break;
            }
            return true;
        }
    }
}
