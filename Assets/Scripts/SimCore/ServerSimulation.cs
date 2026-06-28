#nullable enable annotations

using System;

namespace KSPClone.SimCore
{
    /// <summary>
    /// Engine-agnostic composition of the M0 server spine. Owns the
    /// <see cref="SimWorld"/> and drives every per-tick subsystem in fixed-step
    /// cadence: on-rails sync (via SimWorld), SOI re-parenting, warp auto-limit,
    /// and snapshot emission — plus the warp-vote FSM, vote-membership sync, and
    /// connection registry. The Unity host (<c>ServerBootstrap</c>) feeds it real
    /// time via <see cref="Advance"/> and subscribes to its events (e.g. for
    /// persistence). No transport, no engine references (Constitution Art. 1/2).
    /// </summary>
    public sealed class ServerSimulation
    {
        public SimWorld World { get; }
        public BodyRegistry Bodies { get; }
        public ConnectionRegistry Connections { get; }
        public PoiRegistry Pois { get; }
        public WarpStateMachine Warp { get; }
        public SnapshotEmitter Snapshots { get; }
        public long TickCount => _scheduler.TickCount;

        /// <summary>Fired when a vessel re-parents at an SOI crossing (for persistence write-through).</summary>
        public event Action<Vessel>? VesselReParented;
        /// <summary>Fired at a committed warp endpoint (for persistence write-through).</summary>
        public event Action<double>? WarpCommitted;

        private readonly SimScheduler _scheduler;
        private readonly PoiScanner _poiScanner;
        private readonly SoiTransition _soiTransition;
        private readonly WarpAutoLimit _autoLimit;

        public ServerSimulation(SimWorld world, double snapshotRateHz = SnapshotEmitter.DefaultRateHz)
        {
            World = world ?? throw new ArgumentNullException(nameof(world));
            Bodies = world.Bodies ?? throw new ArgumentException("World must carry a BodyRegistry.", nameof(world));

            Connections = new ConnectionRegistry();
            Pois = new PoiRegistry();
            _poiScanner = new PoiScanner(World, Bodies, Pois);
            _soiTransition = new SoiTransition(World, Bodies, Pois);
            Warp = new WarpStateMachine(World.Clock, Connections,
                onRailsWarpSafe: () => WarpPolicy.AllWarpSafe(World.Vessels.Values));
            _autoLimit = new WarpAutoLimit(World, Pois, Warp);
            // Subscribes to connect/disconnect events; kept alive by those delegates.
            new WarpMembershipSync(Connections, Warp);
            Snapshots = new SnapshotEmitter(World, Connections, snapshotRateHz);

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

        private void OnTick(double dtSeconds)
        {
            _soiTransition.ApplyDue(World.Clock.GameTimeSeconds);
            _autoLimit.Tick();
            Snapshots.Tick(dtSeconds);
        }

        // --- Connection / warp surface for the transport layer ---

        public (PlayerSession session, WorldHandshakeMessage handshake) Connect()
        {
            var session = Connections.AddNew();
            var handshake = new WorldHandshakeBuilder(World).Build();
            return (session, handshake);
        }

        public bool Disconnect(PlayerId id) => Connections.Remove(id);

        public bool RequestWarp(WarpRequest request) => Warp.RequestWarp(request);

        public void ApproveWarp(PlayerId id) => Warp.Approve(id);
    }
}
