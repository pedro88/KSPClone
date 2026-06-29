-- M0 initial schema.
-- One authoritative Postgres store for the program, vessels, and master clock
-- (ADR-0007). Hybrid model: typed columns for queryable fields, JSONB reserved
-- for suspended active-vessel snapshots (SUSP-3) — null in M0.
--
-- Migration is idempotent: safe to apply on a fresh database or one that
-- already has the tables (CREATE IF NOT EXISTS). A real production pipeline
-- would use a versioned migration runner; for M0 this single file is enough.

CREATE TABLE IF NOT EXISTS program (
    id          INTEGER     PRIMARY KEY CHECK (id = 0),
    mode        TEXT        NOT NULL DEFAULT 'sandbox',
    science     DOUBLE PRECISION NOT NULL DEFAULT 0.0,
    funds       DOUBLE PRECISION NOT NULL DEFAULT 0.0,
    created_at  TIMESTAMPTZ NOT NULL DEFAULT now()
);

-- Single-row clock: the master clock is the only authoritative game-time
-- (Constitution Art. 1). The CHECK enforces the singleton invariant.
CREATE TABLE IF NOT EXISTS world_clock (
    id           INTEGER     PRIMARY KEY CHECK (id = 0),
    game_time    DOUBLE PRECISION NOT NULL DEFAULT 0.0,
    warp_rate    DOUBLE PRECISION NOT NULL DEFAULT 1.0
);

-- Vessels: orbital elements as typed columns (cheap, indexable), snapshot
-- JSONB reserved for M1 (suspended active-physics vessels, SUSP-3).
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