#!/usr/bin/env python3
"""
Validate the write-through contract by exercising the same SQL the
WorldRepository issues during OnSoiTransition and OnWarpCommit.

We cannot easily call the C# PersistenceEventSink from a Python script,
but the SQL is the contract: clock + vessels must be consistent in a
single transaction (atomic per PERSIST-2).

Connects to the local greenu_test database on port 5433.
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
    with psycopg2.connect(**DB) as conn:
        conn.autocommit = True
        with conn.cursor() as cur:
            for t in ("vessel", "world_clock", "program"):
                cur.execute(f"DROP TABLE IF EXISTS {t} CASCADE;")
            cur.execute(sql)

        # --- Simulate OnSoiTransition: a vessel is upserted after
        # re-parenting to a new body. We don't run the whole transition
        # here; we just verify the upsert statement changes parent_body
        # in-place.
        vid = str(uuid.uuid4())
        elements_a = dict(
            parent_body=1, semi_major_axis=7e6, eccentricity=0.0,
            inclination=0.0, raan=0.0, argp=0.0, mean_anom_epoch=0.0,
            epoch_game_time=0.0, vessel_clock=0.0, on_rails=True,
        )
        with conn.cursor() as cur:
            cur.execute(
                """
                INSERT INTO vessel (id, parent_body, semi_major_axis, eccentricity,
                                    inclination, raan, argp, mean_anom_epoch,
                                    epoch_game_time, vessel_clock, on_rails)
                VALUES (%(id)s, %(parent_body)s, %(sma)s, %(ecc)s, %(inc)s,
                        %(raan)s, %(argp)s, %(m0)s, %(epoch)s, %(vc)s, %(on_rails)s)
                """,
                dict(elements_a, id=vid, sma=elements_a.pop("semi_major_axis"),
                     ecc=elements_a.pop("eccentricity"), inc=elements_a.pop("inclination"),
                     raan=elements_a.pop("raan"), argp=elements_a.pop("argp"),
                     m0=elements_a.pop("mean_anom_epoch"), epoch=elements_a.pop("epoch_game_time"),
                     vc=elements_a.pop("vessel_clock"), on_rails=elements_a.pop("on_rails")),
            )
        conn.commit()

        # Re-parent: change parent_body to Moon (id=2) and update elements.
        with conn.cursor() as cur:
            cur.execute(
                """
                UPDATE vessel SET parent_body = 2,
                                  semi_major_axis = 1_000_000.0,
                                  eccentricity = 0.1
                WHERE id = %s
                """,
                (vid,),
            )
        conn.commit()

        with conn.cursor() as cur:
            cur.execute("SELECT parent_body, semi_major_axis, eccentricity FROM vessel WHERE id = %s;", (vid,))
            row = cur.fetchone()
        assert row == (2, 1_000_000.0, 0.1), f"SOI re-parent did not persist: {row}"

        # --- Simulate OnWarpCommit: write clock + all vessels atomically.
        # We use one transaction that does both; if any step fails,
        # neither should be visible.
        with conn.cursor() as cur:
            cur.execute("TRUNCATE vessel, world_clock, program;")
        conn.commit()

        with conn.cursor() as cur:
            cur.execute(
                """
                INSERT INTO world_clock (id, game_time, warp_rate)
                VALUES (0, 5000.0, 1.0)
                ON CONFLICT (id) DO UPDATE SET game_time = EXCLUDED.game_time,
                                              warp_rate = EXCLUDED.warp_rate;
                """
            )
            for i in range(3):
                cur.execute(
                    """
                    INSERT INTO vessel (id, parent_body, semi_major_axis, eccentricity,
                                        inclination, raan, argp, mean_anom_epoch,
                                        epoch_game_time, vessel_clock, on_rails)
                    VALUES (%s, 1, %s, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, %s, TRUE)
                    ON CONFLICT (id) DO UPDATE SET semi_major_axis = EXCLUDED.semi_major_axis,
                                                  vessel_clock = EXCLUDED.vessel_clock;
                    """,
                    (str(uuid.uuid4()), 7e6 + i * 100_000.0, 5000.0 + i),
                )
        conn.commit()

        # Verify both clock and vessels are present and consistent.
        with conn.cursor() as cur:
            cur.execute("SELECT game_time FROM world_clock WHERE id = 0;")
            gt = cur.fetchone()[0]
            cur.execute("SELECT count(*) FROM vessel;")
            n = cur.fetchone()[0]
        assert gt == 5000.0
        assert n == 3

        # --- Transaction atomicity: a failure mid-way must roll back
        # BOTH the clock and any vessel upserts in the same transaction.
        try:
            with conn.cursor() as cur:
                cur.execute(
                    "INSERT INTO world_clock (id, game_time, warp_rate) "
                    "VALUES (0, 9999.0, 1.0) ON CONFLICT (id) DO UPDATE SET game_time = 9999.0;"
                )
                # Now a deliberately bad statement: parent_body must be
                # INTEGER, not text. This rolls back the whole transaction.
                cur.execute(
                    "INSERT INTO vessel (id, parent_body, semi_major_axis, eccentricity, "
                    "inclination, raan, argp, mean_anom_epoch, epoch_game_time, vessel_clock, on_rails) "
                    "VALUES (%s, 'not-an-integer', 7e6, 0, 0, 0, 0, 0, 0, 0, TRUE);",
                    (str(uuid.uuid4()),),
                )
            conn.commit()
            raise AssertionError("Bad INSERT should have failed.")
        except psycopg2.errors.InvalidTextRepresentation:
            conn.rollback()

        # The clock must NOT have changed: the failed transaction rolled back.
        with conn.cursor() as cur:
            cur.execute("SELECT game_time FROM world_clock WHERE id = 0;")
            gt_after = cur.fetchone()[0]
        assert gt_after == 5000.0, (
            f"Atomicity violated: clock should still be 5000.0 after a failed "\
            f"transaction, got {gt_after}. The clock update was committed "\
            f"instead of rolled back together with the bad vessel insert."
        )

        print("OK: persistence event-sink contract holds (SOI + warp-commit + atomicity).")
        return 0


if __name__ == "__main__":
    sys.exit(main())