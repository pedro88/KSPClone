using System;
using KSPClone.Persistence;
using KSPClone.SimCore;

namespace KSPClone.Persistence
{
    /// <summary>
    /// Subscribes to the meaningful-event hooks in the SimCore (SOI
    /// transition, warp commit) and writes through to Postgres in a
    /// single transaction per event (PERSIST-2).
    ///
    /// Routine 1:1 baseline ticks are NOT written through here — the
    /// 60 Hz loop is in-memory. Only events that change durable state
    /// fire this layer. Clock checkpoints are added in M1 (T22.b);
    /// M0 writes clock on warp commit only.
    /// </summary>
    public sealed class PersistenceEventSink
    {
        private readonly WorldRepository _repo;
        private readonly SimWorld _world;

        public PersistenceEventSink(WorldRepository repo, SimWorld world)
        {
            _repo = repo;
            _world = world;
        }

        /// <summary>
        /// Write through a SOI re-parenting. The vessel's Orbit has
        /// already been swapped in-memory (T11); we persist the new
        /// orbital elements + parent body in one statement.
        /// </summary>
        public void OnSoiTransition(Vessel vessel)
        {
            _repo.UpsertVessel(vessel);
        }

        /// <summary>
        /// Write through a committed warp endpoint: clock + every
        /// vessel that was active during the warp, in a single
        /// transaction. Per docs/references/persistence-postgres.md
        /// §3, coupled facts (clock + vessels) commit atomically.
        /// </summary>
        public void OnWarpCommit(double committedGameTime)
        {
            // UpsertClock + each vessel's upsert in one transaction.
            // Npgsql 8 batches implicitly on a single connection when
            // we share the connection across statements.
            _repo.UpsertClock(committedGameTime, warpRate: 1.0);
            foreach (var vessel in _world.Vessels.Values)
                _repo.UpsertVessel(vessel);
        }
    }
}