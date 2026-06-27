using System;
using System.Collections.Generic;
using KSPClone.SimCore;
using Npgsql;
using NpgsqlTypes;

namespace KSPClone.Persistence
{
    /// <summary>
    /// Single authoritative Postgres-backed store for the program's
    /// durable state (ADR-0007). The in-memory <see cref="SimWorld"/>
    /// is the hot copy; the repository writes through on meaningful
    /// events (PERSIST-2) and restores from disk on restart (PERSIST-3).
    /// </summary>
    public sealed class WorldRepository
    {
        private readonly string _connectionString;

        public WorldRepository(string connectionString)
        {
            _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        }

        /// <summary>
        /// Apply the M0 initial schema. Idempotent: safe to call on a
        /// fresh or already-migrated database.
        /// </summary>
        public void Migrate()
        {
            const string sql = @"
                CREATE TABLE IF NOT EXISTS program (
                    id          INTEGER     PRIMARY KEY CHECK (id = 0),
                    mode        TEXT        NOT NULL DEFAULT 'sandbox',
                    science     DOUBLE PRECISION NOT NULL DEFAULT 0.0,
                    funds       DOUBLE PRECISION NOT NULL DEFAULT 0.0,
                    created_at  TIMESTAMPTZ NOT NULL DEFAULT now()
                );
                CREATE TABLE IF NOT EXISTS world_clock (
                    id           INTEGER     PRIMARY KEY CHECK (id = 0),
                    game_time    DOUBLE PRECISION NOT NULL DEFAULT 0.0,
                    warp_rate    DOUBLE PRECISION NOT NULL DEFAULT 1.0
                );
                CREATE TABLE IF NOT EXISTS vessel (
                    id                UUID          PRIMARY KEY,
                    parent_body       INTEGER       NOT NULL,
                    semi_major_axis   DOUBLE PRECISION NOT NULL,
                    eccentricity      DOUBLE PRECISION NOT NULL,
                    inclination       DOUBLE PRECISION NOT NULL,
                    raan              DOUBLE PRECISION NOT NULL,
                    argp              DOUBLE PRECISION NOT NULL,
                    mean_anom_epoch   DOUBLE PRECISION NOT NULL,
                    epoch_game_time   DOUBLE PRECISION NOT NULL,
                    vessel_clock      DOUBLE PRECISION NOT NULL DEFAULT 0.0,
                    on_rails          BOOLEAN       NOT NULL DEFAULT TRUE,
                    snapshot          JSONB         NULL
                );
                CREATE INDEX IF NOT EXISTS vessel_parent_body_idx ON vessel (parent_body);
            ";
            using var conn = new NpgsqlConnection(_connectionString);
            conn.Open();
            using var cmd = new NpgsqlCommand(sql, conn);
            cmd.ExecuteNonQuery();
        }

        public void UpsertProgram(string mode, double science, double funds)
        {
            const string sql = @"
                INSERT INTO program (id, mode, science, funds) VALUES (0, @mode, @science, @funds)
                ON CONFLICT (id) DO UPDATE SET mode = EXCLUDED.mode,
                                              science = EXCLUDED.science,
                                              funds = EXCLUDED.funds;
            ";
            using var conn = new NpgsqlConnection(_connectionString);
            conn.Open();
            using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("mode", mode);
            cmd.Parameters.AddWithValue("science", science);
            cmd.Parameters.AddWithValue("funds", funds);
            cmd.ExecuteNonQuery();
        }

        public void UpsertClock(double gameTime, double warpRate)
        {
            const string sql = @"
                INSERT INTO world_clock (id, game_time, warp_rate) VALUES (0, @gt, @wr)
                ON CONFLICT (id) DO UPDATE SET game_time = EXCLUDED.game_time,
                                              warp_rate = EXCLUDED.warp_rate;
            ";
            using var conn = new NpgsqlConnection(_connectionString);
            conn.Open();
            using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("gt", gameTime);
            cmd.Parameters.AddWithValue("wr", warpRate);
            cmd.ExecuteNonQuery();
        }

        public void UpsertVessel(Vessel vessel)
        {
            const string sql = @"
                INSERT INTO vessel (id, parent_body, semi_major_axis, eccentricity,
                                    inclination, raan, argp, mean_anom_epoch,
                                    epoch_game_time, vessel_clock, on_rails)
                VALUES (@id, @parent_body, @sma, @ecc, @inc, @raan, @argp, @m0,
                        @epoch, @vc, @on_rails)
                ON CONFLICT (id) DO UPDATE SET
                    parent_body     = EXCLUDED.parent_body,
                    semi_major_axis = EXCLUDED.semi_major_axis,
                    eccentricity    = EXCLUDED.eccentricity,
                    inclination     = EXCLUDED.inclination,
                    raan            = EXCLUDED.raan,
                    argp            = EXCLUDED.argp,
                    mean_anom_epoch = EXCLUDED.mean_anom_epoch,
                    epoch_game_time = EXCLUDED.epoch_game_time,
                    vessel_clock    = EXCLUDED.vessel_clock,
                    on_rails        = EXCLUDED.on_rails;
            ";
            using var conn = new NpgsqlConnection(_connectionString);
            conn.Open();
            using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.Add(new NpgsqlParameter("id", NpgsqlDbType.Uuid) { Value = vessel.Id.Value });
            cmd.Parameters.AddWithValue("parent_body", (int)vessel.Orbit.ParentBody);
            cmd.Parameters.AddWithValue("sma", vessel.Orbit.SemiMajorAxis);
            cmd.Parameters.AddWithValue("ecc", vessel.Orbit.Eccentricity);
            cmd.Parameters.AddWithValue("inc", vessel.Orbit.Inclination);
            cmd.Parameters.AddWithValue("raan", vessel.Orbit.LongitudeOfAscendingNode);
            cmd.Parameters.AddWithValue("argp", vessel.Orbit.ArgumentOfPeriapsis);
            cmd.Parameters.AddWithValue("m0", vessel.Orbit.MeanAnomalyAtEpoch);
            cmd.Parameters.AddWithValue("epoch", vessel.Orbit.EpochGameTime);
            cmd.Parameters.AddWithValue("vc", vessel.VesselClockSeconds);
            cmd.Parameters.AddWithValue("on_rails", vessel.OnRails);
            cmd.ExecuteNonQuery();
        }

        public (double gameTime, double warpRate)? LoadClock()
        {
            const string sql = "SELECT game_time, warp_rate FROM world_clock WHERE id = 0;";
            using var conn = new NpgsqlConnection(_connectionString);
            conn.Open();
            using var cmd = new NpgsqlCommand(sql, conn);
            using var rdr = cmd.ExecuteReader();
            if (!rdr.Read()) return null;
            return (rdr.GetDouble(0), rdr.GetDouble(1));
        }

        public IEnumerable<(VesselId id, Orbit orbit, double vesselClock, bool onRails)> LoadVessels()
        {
            const string sql = @"
                SELECT id, parent_body, semi_major_axis, eccentricity, inclination,
                       raan, argp, mean_anom_epoch, epoch_game_time, vessel_clock, on_rails
                FROM vessel;
            ";
            using var conn = new NpgsqlConnection(_connectionString);
            conn.Open();
            using var cmd = new NpgsqlCommand(sql, conn);
            using var rdr = cmd.ExecuteReader();
            while (rdr.Read())
            {
                var id = new VesselId(rdr.GetGuid(0));
                var orbit = new Orbit(
                    semiMajorAxis: rdr.GetDouble(2),
                    eccentricity: rdr.GetDouble(3),
                    inclination: rdr.GetDouble(4),
                    longitudeOfAscendingNode: rdr.GetDouble(5),
                    argumentOfPeriapsis: rdr.GetDouble(6),
                    meanAnomalyAtEpoch: rdr.GetDouble(7),
                    epochGameTime: rdr.GetDouble(8),
                    parentBody: (CelestialBodyId)rdr.GetInt32(1));
                yield return (id, orbit, rdr.GetDouble(9), rdr.GetBoolean(10));
            }
        }

        public void Truncate()
        {
            const string sql = "TRUNCATE vessel, world_clock, program;";
            using var conn = new NpgsqlConnection(_connectionString);
            conn.Open();
            using var cmd = new NpgsqlCommand(sql, conn);
            cmd.ExecuteNonQuery();
        }
    }
}