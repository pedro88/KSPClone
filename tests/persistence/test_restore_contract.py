#!/usr/bin/env python3
"""
Validate the restore contract.

The contract: after a write-through, dropping the server process and
restarting must yield a SimWorld whose clock + vessels match the
last persisted state exactly.

We exercise this by:
1. Applying the schema, seeding a world (clock + vessels) via the
   same SQL the PersistenceEventSink runs.
2. Re-reading everything via the same SQL the WorldRestorer runs.
3. Asserting round-trip exactness.
"""
import sys
import uuid
from pathlib import Path

import psycopg2

DB = dict(
    host="localhost",
    port=5433,
    user="greenu",
    password="greenu",
    dbname="greenu_test",
)
MIGRATION = Path("Assets/Scripts/Persistence/Migrations/001_init.sql")


def main() -> int:
    sql = MIGRATION.read_text()

    # Seed
    with psycopg2.connect(**DB) as conn:
        conn.autocommit = True
        with conn.cursor() as cur:
            for t in ("vessel", "world_clock", "program"):
                cur.execute(f"DROP TABLE IF EXISTS {t} CASCADE;")
            cur.execute(sql)

        # Populate world state
        with conn.cursor() as cur:
            cur.execute(
                "INSERT INTO program (id, mode, science, funds) VALUES (0, 'sandbox', 0.0, 0.0);"
            )
            cur.execute(
                "INSERT INTO world_clock (id, game_time, warp_rate) "
                "VALUES (0, 12345.678901234, 1.0);"
            )
            seeded = []
            for i in range(3):
                vid = str(uuid.uuid4())
                # Each vessel orbits a different body, with different
                # orbital elements, to ensure the round-trip carries
                # every column precisely.
                parent = (i % 2) + 1  # 1 or 2
                sma = 7_000_000.0 + i * 50_000.123456
                ecc = 0.001 * (i + 1)
                inc = 0.1 + i * 0.05
                raan = 1.0 + i * 0.2
                argp = 2.0 + i * 0.3
                m0 = 0.01 * i
                epoch = 100.0 * i
                vc = 12345.678901234 + i
                seeded.append((vid, parent, sma, ecc, inc, raan, argp, m0, epoch, vc))
                cur.execute(
                    """
                    INSERT INTO vessel (id, parent_body, semi_major_axis, eccentricity,
                                        inclination, raan, argp, mean_anom_epoch,
                                        epoch_game_time, vessel_clock, on_rails)
                    VALUES (%s, %s, %s, %s, %s, %s, %s, %s, %s, %s, TRUE);
                    """,
                    (vid, parent, sma, ecc, inc, raan, argp, m0, epoch, vc),
                )
        conn.commit()

    # --- Simulate restart: kill process, open a new connection.
    loaded_clock = None
    loaded_vessels = []
    with psycopg2.connect(**DB) as conn:
        with conn.cursor() as cur:
            cur.execute("SELECT game_time, warp_rate FROM world_clock WHERE id = 0;")
            row = cur.fetchone()
            if row is not None:
                loaded_clock = row
            cur.execute(
                """
                SELECT id, parent_body, semi_major_axis, eccentricity, inclination,
                       raan, argp, mean_anom_epoch, epoch_game_time, vessel_clock,
                       on_rails
                FROM vessel;
                """
            )
            for row in cur.fetchall():
                loaded_vessels.append(row)

    # Verify clock.
    assert loaded_clock is not None, "Clock row missing after restart."
    gt, wr = loaded_clock
    assert gt == 12345.678901234, f"Clock game_time drift: {gt}"
    assert wr == 1.0

    # Verify vessels (set comparison since UUIDs are server-assigned).
    assert len(loaded_vessels) == len(seeded), (
        f"Vessel count mismatch after restart: seeded={len(seeded)}, "
        f"loaded={len(loaded_vessels)}"
    )

    # Index loaded by uuid for stable comparison.
    loaded_by_id = {row[0]: row for row in loaded_vessels}
    for (vid, parent, sma, ecc, inc, raan, argp, m0, epoch, vc) in seeded:
        assert vid in loaded_by_id, f"Vessel {vid} missing after restart."
        row = loaded_by_id[vid]
        (_, parent_l, sma_l, ecc_l, inc_l, raan_l, argp_l, m0_l, epoch_l, vc_l, on_rails_l) = row
        assert parent_l == parent, f"parent_body drift: {parent_l} != {parent}"
        assert sma_l == sma, f"semi_major_axis drift: {sma_l} != {sma}"
        assert ecc_l == ecc, f"eccentricity drift: {ecc_l} != {ecc}"
        assert inc_l == inc
        assert raan_l == raan
        assert argp_l == argp
        assert m0_l == m0
        assert epoch_l == epoch
        assert vc_l == vc
        assert on_rails_l is True

    print("OK: restore contract holds — clock + every vessel round-trips exactly.")
    return 0


if __name__ == "__main__":
    sys.exit(main())