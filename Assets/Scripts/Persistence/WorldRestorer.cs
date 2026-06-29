using System;
using System.Collections.Generic;
using KSPClone.Persistence;
using KSPClone.SimCore;

namespace KSPClone.Persistence
{
    /// <summary>
    /// Loads the world and clock from Postgres on server start and
    /// reconstructs the live <see cref="SimWorld"/> exactly as last
    /// persisted (PERSIST-3, SUSP-1).
    ///
    /// Restore sequence (per docs/references/persistence-postgres.md §6):
    ///   1. Load the single world_clock row → resume GameTimeSeconds,
    ///      Rate at 1.0 (we never restore a warp in progress).
    ///   2. Load every vessel row → reconstruct Vessel + Orbit.
    ///   3. Rescan POIs from the freshly-loaded vessels (POIs are
    ///      derived, not persisted).
    ///   4. Empty-DB case: seed a fresh world via a caller-provided
    ///      bootstrap callback.
    /// </summary>
    public sealed class WorldRestorer
    {
        public delegate void EmptyDatabaseBootstrap(SimWorld world);

        private readonly WorldRepository _repo;

        public WorldRestorer(WorldRepository repo)
        {
            _repo = repo;
        }

        /// <summary>
        /// Build a fresh <see cref="SimWorld"/>, populate it from
        /// Postgres, and return it. If the DB is empty, the bootstrap
        /// callback seeds it (and persists the seed via a fresh
        /// WorldRepository call so subsequent restarts find state).
        /// </summary>
        public SimWorld RestoreOrSeed(BodyRegistry bodies, EmptyDatabaseBootstrap bootstrap)
        {
            var world = new SimWorld(bodies);
            var clock = _repo.LoadClock();
            if (clock is null)
            {
                // Empty DB: seed and persist a fresh world.
                bootstrap(world);
                _repo.UpsertProgram(mode: "sandbox", science: 0.0, funds: 0.0);
                _repo.UpsertClock(world.Clock.GameTimeSeconds, warpRate: 1.0);
                foreach (var v in world.Vessels.Values)
                    _repo.UpsertVessel(v);
                return world;
            }

            // Resume the clock.
            // MasterClock has a private setter on GameTimeSeconds. We
            // bounce through Advance() to keep the single-writer rule:
            // set Rate = 1.0, advance by the desired amount.
            world.Clock.Rate = 1.0;
            world.Clock.Advance(clock.Value.gameTime);

            // Reconstruct vessels.
            foreach (var (id, orbit, vesselClock, onRails) in _repo.LoadVessels())
            {
                var vessel = new Vessel(id, orbit)
                {
                    VesselClockSeconds = vesselClock,
                    State = onRails ? VesselState.OnRails : VesselState.ActivePhysics,
                };
                world.RegisterVessel(vessel);
            }

            return world;
        }
    }
}