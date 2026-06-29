# Persistence Reference: Single Postgres Authoritative Store

> **Why this matters for us.** [ADR-0007](../adr/0007-single-postgres-authoritative-store.md) makes one Postgres the sole durable store for *both* progression (tech tree, science, funds) and world/vessel state (orbital elements as columns, suspended active-vessel snapshots as JSONB). The server keeps a hot in-memory live sim and writes **through** to Postgres on meaningful events (PERSIST-2: SOI change, demotion, suspension, committed warp endpoint, science award). On restart the world and program must be restored *exactly* as last persisted (PERSIST-3). At 4 players the entire universe is kilobytes-to-low-megabytes, so the goal is correctness and one backup story, not write throughput.

This doc is curated reference material for designing that layer. All claims are sourced to official Postgres docs, Npgsql docs, or named engineering sources; see the annotated link list at the end.

---

## 1. Postgres JSONB: storing & querying vessel snapshots

We use JSONB for the *suspended active-vessel snapshot* only (SUSP-3): an active-physics vessel parked because no player is present. Its shape is irregular (per-part transforms, resources, articulation-point state) and we read it back whole, on demand (SUSP-4 resume). That is exactly the shape JSONB is good at.

**`jsonb`, not `json`.** Postgres docs: *"most applications should prefer to store JSON data as `jsonb`, unless there are quite specialized needs, such as legacy assumptions about ordering of object keys."* `jsonb` is a decomposed binary format — slightly slower to write, much faster to read (no reparse), and the only one that supports indexing. We don't care about key order or whitespace, so `jsonb` is correct.

**Containment & existence operators** (these are the indexable ones):
- `@>` containment — *"does the left jsonb contain the right one?"* Order-independent for arrays. Our main query operator if we ever filter snapshots.
- `?` / `?|` / `?&` key-existence (top-level keys / array elements only).
- `@?` / `@@` jsonpath match (PG12+).

**GIN indexing.** A plain `GIN (snapshot)` index uses the default `jsonb_ops` operator class: indexes every key and value, supports `@>`, `?`, `?|`, `?&`, `@?`, `@@`. Larger index. The `GIN (snapshot jsonb_path_ops)` class hashes whole paths-to-value: *"usually much smaller … and the specificity of searches is better,"* but supports **only** `@>`, `@?`, `@@` (no key-existence `?`). It cannot find empty-object structures like `{"a": {}}`.

**Our recommendation: do NOT add a GIN index on the snapshot column initially.** We look snapshots up by `vessel_id` (a normal B-tree / PK), never by content. A GIN index would only pay off if we start querying *inside* snapshots, and it carries real write cost (every insert/update decomposes the whole document; engineering sources report 30–50% insert-throughput hits on write-heavy large-document tables). At 4 players, skip it until a query needs it. If one ever does, prefer `jsonb_path_ops` (containment-only is all we'd need).

**Document-size gotcha (from the docs, important for us).** *"any update acquires a row-level lock on the whole row. Consider limiting JSON documents to a manageable size."* Also: updating one key with `jsonb_set()` **rewrites the entire value** — full TOAST decompress / recompress / new row version in WAL, proportional to document size. **Implication: treat a snapshot as immutable.** Write it once on suspension, delete it on resume. Never partially mutate a stored snapshot.

**TOAST.** JSONB > ~2 KB is compressed and stored out-of-line (TOAST); reading it costs extra I/O + decompression. Fine for our access pattern (read whole snapshot once, on resume).

---

## 2. JSONB vs normalized columns — where the line goes for us

The canonical rule (Heap.io; Postgres docs; many engineering sources): **hybrid model — typed columns for the fields you query/filter/constrain on, JSONB for the irregular remainder.** *"If you find yourself writing `WHERE data->>'status' = 'active'` in most of your queries, that status field should be a column."*

Two concrete reasons this matters here:
1. **Planner statistics.** Postgres keeps no statistics on values *inside* a JSONB column; it falls back to a hardcoded 0.1% selectivity estimate, which produces bad plans. Anything we filter, join, or range-scan on (orbital elements, SOI body, vessel clock, science totals) must be a real column.
2. **Foreign keys & constraints.** You cannot FK or `CHECK` into a JSONB field. Coupled-fact integrity (science award ↔ vessel that earned it) needs real columns + real FKs.

**Mapping to our model:**
- **On-rails vessel** → a small *row*: orbital elements (semi-major axis, eccentricity, inclination, …), current SOI body, vessel clock, crew refs. All typed columns. This is the default state for everything (CONTEXT: "Cheap; the default state for everything not near a player"). ORBIT-2 closed-form propagation reads these columns directly.
- **Suspended vessel** → the same row *plus* a JSONB `snapshot` column holding the part-level physics state that has no fixed schema.
- **Progression** (program, tech tree, science, funds) → fully normalized, typed, FK-constrained. Never JSONB.

---

## 3. Transactional consistency between coupled facts

This is the headline reason ADR-0007 chose one store: *atomic consistency between coupled facts ("science was awarded" and "the vessel that earned it reached orbit")*.

Postgres gives us standard **ACID**. From the docs: *"If the transaction is committed, PostgreSQL will ensure either that all updates are done or else that none of them are done."* Multi-table writes inside one `BEGIN … COMMIT` are atomic — exactly what we need for a science-award event that touches `program.science_points`, `science_award`, and `vessel`.

**Isolation level.** Postgres default is **Read Committed**. For our coupled writes that read-then-update a shared counter (science pool, funds), Read Committed plus `UPDATE program SET science = science + :delta` (atomic increment, no read-modify-write race) is sufficient. If we ever need read-modify-write on the same row from concurrent events, use `SELECT … FOR UPDATE` or bump to `REPEATABLE READ`/`SERIALIZABLE` for that transaction. With a single authoritative server thread driving writes, contention is near-zero anyway — but the *world events* and *player progression events* may be produced by different parts of the sim, so wrap each coupled event in one transaction.

**Rule for us:** every PERSIST-2 event = one transaction that writes *all* state the event implies. A science award that depends on a vessel reaching orbit writes both the award and the vessel's new orbital row in the same `COMMIT`, or neither.

---

## 4. Write-through / write-behind from the in-memory sim — durability vs latency

ADR-0007 says **write-through on meaningful events**, not continuous streaming. The 60 Hz tick (NET-5) is in-memory; persistence is event-triggered, off the hot path.

**The key Postgres lever: `synchronous_commit`.** Postgres docs on *asynchronous commit*: with async commit *"the server returns success as soon as the transaction is logically completed, before the WAL records … have actually made their way to disk."* The risk is **lost recent commits on crash**, bounded to ~`3 × wal_writer_delay` (default `wal_writer_delay` = 200 ms, so ≲ 600 ms of commits at risk). Crucially: *"The risk … is of data loss, not data corruption"* and *"no inconsistency can be introduced"* — atomicity and consistency are preserved; only the **durability** of the last sub-second of commits is relaxed. It is **per-transaction settable** (`SET LOCAL synchronous_commit = off`).

**How to use this for us:**
- **Default `synchronous_commit = on`** (durable) for everything. Our write volume is tiny; we don't need to relax it.
- It is available as a *deliberate* knob if a specific high-frequency, low-value write ever appears (it currently does not — all PERSIST-2 events are meaningful and rare). Don't relax it for science awards or warp commits.

**Write-through vs write-behind for us:**
- **Write-through (synchronous, chosen):** on a PERSIST-2 event, commit before treating the event as durable. Simple, exact PERSIST-3 restore, no reconciliation logic. Correct default at our scale.
- **Write-behind (async/batched):** only justified under write pressure we don't have. If introduced later, the safe shape is the **outbox** below, not fire-and-forget.

**Batching when it helps.** When one event implies many rows (e.g. a committed on-rails warp endpoint that advances *every* vessel's orbit + clock at once), write them in **one transaction**. For a large bulk load (e.g. seeding/restoring the universe), Npgsql **binary `COPY`** is *"usually much faster … than using INSERT,"* via `BeginBinaryImport` / `StartRow` / `Write` / `Complete`. For a handful of statements in one round-trip, use `NpgsqlBatch`.

---

## 5. Snapshotting a live world without stalling the 60 Hz loop

NET-5 fixes the sim tick at 60 Hz; persistence must never block it. Patterns:

**Dirty-tracking.** The in-memory authoritative state marks entities dirty when a meaningful event mutates them; a *separate* persistence path drains dirty entities. The tick loop never calls the DB.

**Async writes off the hot thread.** Npgsql is fully async (`ExecuteReaderAsync`, `ExecuteNonQueryAsync`, etc.). Persist on a dedicated task/channel, not the sim thread. The sim publishes events to an in-process queue (e.g. `System.Threading.Channels`); a writer task consumes and commits. This is the in-memory analogue of write-behind but bounded to event volume.

**Outbox / dirty-queue durability.** The risk in any async-write scheme is the **dual-write problem**: the sim "knows" an event happened but the DB write hasn't landed and the process crashes. The **transactional outbox pattern** is the textbook fix — but note its classic form writes the outbox row *in the same DB transaction as the business data*, which presumes the business data already goes to the DB. For us, the authoritative copy is **in memory**, so the relevant adaptation is:
  - **Synchronous write-through (recommended at our scale):** treat the in-memory event as not-yet-final until its transaction commits. No outbox needed; the DB *is* the commit point. This is the simplest path to a correct PERSIST-3 restore.
  - **If we later go async:** persist an append-only **event/outbox row first** (the event that *will* mutate world state), commit it durably, then apply to memory + fold into normalized state. On crash, replay un-applied outbox rows. Outbox delivery is **at-least-once**, so application must be **idempotent** (carry a unique `event_id`, check before applying). Relay options: polling the outbox table, or transaction-log tailing.

**Our recommendation:** start synchronous write-through (Section 4). Keep an `event_log` table anyway (Section 6) — it is cheap, gives an audit trail, and is the seed of an outbox if we ever need async.

---

## 6. Restart / restore: loading the world exactly (PERSIST-3)

Because every PERSIST-2 event is committed atomically, restore is just: **read the committed rows back into memory.** No retro-simulation, no rail-snap (matches SUSP-4's "resume from snapshot" semantics).

Restore sequence on server start:
1. Load the single `program` row + `tech_node` unlock states + `science`/`funds` totals → reconstruct progression.
2. Load `master_clock.game_time` → the authoritative game-time (TIME-1) resumes from the last committed value.
3. Load all `vessel` rows. For each:
   - On-rails vessel → reconstruct orbit from the typed orbital-element columns; it is already synced to the master clock conceptually (SUSP-1 keeps on-rails vessels synced — on restore, advance their analytic orbit from `vessel_clock` to `game_time`, which is *closed-form and free* per ORBIT-2).
   - Suspended vessel → load its `snapshot` JSONB; do **not** advance its `vessel_clock` (it is paused, SUSP-3). It resumes exactly from the snapshot when a player loads it (SUSP-4).
4. Rebuild `crew_assignment` links.

**Exactness guarantees** rest on: (a) atomic per-event commits so no half-state is ever durable, and (b) `synchronous_commit = on` so a committed event is genuinely on disk. Together these satisfy PERSIST-3 — the worst case after a crash is losing an *uncommitted* in-flight event, never a corrupt or half-applied world.

---

## 7. .NET / C# from the Unity dedicated server: Npgsql

Per [ADR-0008](../adr/0008-unity-engine.md) the server is Unity; data access is **Npgsql** (the .NET Postgres driver).

**Use `NpgsqlDataSource` (Npgsql 7.0+), one per process.** Docs: *"You typically build a single data source, and then use that instance throughout your application; data sources are thread-safe, and (usually) correspond to a connection pool."*
```csharp
var builder = new NpgsqlDataSourceBuilder(connString);
// builder.UseNodaTime(); // if we model game-time with NodaTime
await using var dataSource = builder.Build();   // hold for app lifetime
```

**Connection pooling is on by default.** *"closing or disposing a connection doesn't close the underlying physical connection, but rather returns it to an internal pool."* So: **open late, close early.** Docs: *"keep connections open for as little time as possible … open and close connections frequently"* — pooling makes open/close cheap. Acquire a connection per unit of work (per event transaction), not one held for the server's lifetime.

**Always async, off the sim thread:**
```csharp
await using var cmd = dataSource.CreateCommand("UPDATE ...");
await cmd.ExecuteNonQueryAsync(ct);
```

**Batching / bulk:**
- `NpgsqlBatch` — several statements, one round-trip (e.g. multi-row warp-commit).
- Binary `COPY` (`BeginBinaryImport` → `StartRow`/`Write`(typed via `NpgsqlDbType`) → `Complete`) — bulk seed/restore. **Must call `Complete()` or it rolls back on dispose.**
- Prepared statements / Npgsql auto-prepare — for hot repeated statements (e.g. the per-event upserts).

**Pool sizing.** Default `Max Pool Size` is 100; at 4 players + one writer task we need only a few. A small pool is fine. Tunables exist (`Read Buffer Size` for large rows, `Min Pool Size` to keep warm connections).

**Migrations.** Three .NET options, all keep a version-tracking table:
- **EF Core Migrations** — C# migrations via `DbContext`; good if we already use EF Core as the ORM. Has up/down.
- **FluentMigrator** — C# fluent migrations, no ORM/DbContext needed.
- **DbUp** — plain SQL scripts, run-once, tracked in a `SchemaVersions` table; *no* rollback by design (you write a new forward script to revert). Best when you want to read raw SQL.
For a small, schema-stable game world where we'll likely hand-write DDL (Section 8), **DbUp or FluentMigrator** are the lightest fits; EF Core Migrations only if we adopt EF Core as the data layer.

---

## 8. Proposed schema sketch (DDL-ish, aligned to CONTEXT.md)

Terms map directly to CONTEXT.md: *space program*, *science*, *tech tree*, *vessel*, *orbit*, *suspended vessel*, *station/crew*.

```sql
-- One row, ever. The shared cooperative agency (PROG-1).
CREATE TABLE program (
    id              smallint   PRIMARY KEY DEFAULT 1 CHECK (id = 1),  -- singleton
    game_mode       text       NOT NULL CHECK (game_mode IN ('sandbox','science','career')),
    science_points  numeric    NOT NULL DEFAULT 0 CHECK (science_points >= 0),
    funds           numeric    NOT NULL DEFAULT 0,                    -- Career only; unused otherwise
    updated_at      timestamptz NOT NULL DEFAULT now()
);

-- The single master clock (TIME-1). One row.
CREATE TABLE master_clock (
    id          smallint     PRIMARY KEY DEFAULT 1 CHECK (id = 1),
    game_time   double precision NOT NULL,   -- seconds of game-time since epoch
    updated_at  timestamptz  NOT NULL DEFAULT now()
);

-- Static-ish definition of a tech tree node (could also be seeded from data files).
CREATE TABLE tech_node (
    id            text     PRIMARY KEY,            -- stable node key, e.g. 'basicRocketry'
    science_cost  numeric  NOT NULL CHECK (science_cost >= 0),
    prereqs       text[]   NOT NULL DEFAULT '{}'   -- node ids; tree edges
);

-- Per-program unlock state (PROG-3). One program here, but modeled for generality.
CREATE TABLE tech_unlock (
    program_id   smallint  NOT NULL REFERENCES program(id),
    node_id      text      NOT NULL REFERENCES tech_node(id),
    unlocked_at  timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (program_id, node_id)
);

-- A celestial body (parent of an orbit's SOI). Mostly static reference data.
CREATE TABLE body (
    id      text PRIMARY KEY,        -- 'kerbin', 'mun', ...
    mu      double precision NOT NULL,   -- gravitational parameter
    soi_radius double precision
);

-- A vessel instance in the world (CONTEXT: instantiated craft with world state).
CREATE TABLE vessel (
    id            uuid       PRIMARY KEY,
    program_id    smallint   NOT NULL REFERENCES program(id),
    name          text       NOT NULL,
    state         text       NOT NULL CHECK (state IN ('on_rails','active','suspended')),
    vessel_clock  double precision NOT NULL,   -- this vessel's as-of game-time (CONTEXT: vessel clock)

    -- ORBIT: analytic conic around its current SOI body. Typed columns (queryable, ORBIT-2).
    soi_body_id   text       REFERENCES body(id),   -- current SOI (ORBIT-1); NULL while landed/surface
    sma           double precision,   -- semi-major axis
    ecc           double precision,   -- eccentricity
    inc           double precision,   -- inclination
    lan           double precision,   -- longitude of ascending node
    argp          double precision,   -- argument of periapsis
    mna           double precision,   -- mean anomaly at epoch
    epoch         double precision,   -- game-time the elements are referenced to

    -- SUSPENDED VESSEL: irregular part-level physics snapshot (SUSP-3). Immutable once written.
    snapshot      jsonb,             -- NULL unless state = 'suspended'

    updated_at    timestamptz NOT NULL DEFAULT now(),
    CHECK ( (state = 'suspended') = (snapshot IS NOT NULL) )  -- snapshot iff suspended
);
CREATE INDEX vessel_program_idx ON vessel (program_id);
CREATE INDEX vessel_state_idx   ON vessel (state);
-- No GIN on snapshot: we fetch it by vessel PK, never query inside it (Section 1).

-- Crew assignment: which player occupies which station on which vessel (CREW-1..5).
-- Note: occupancy is runtime/transient; persist only what must survive restart
-- (e.g. crew *aboard*, not live station occupancy which falls back to automation on disconnect).
CREATE TABLE crew_member (
    id        uuid PRIMARY KEY,
    program_id smallint NOT NULL REFERENCES program(id),
    name      text NOT NULL
);
CREATE TABLE crew_assignment (
    vessel_id uuid NOT NULL REFERENCES vessel(id) ON DELETE CASCADE,
    crew_id   uuid NOT NULL REFERENCES crew_member(id),
    PRIMARY KEY (vessel_id, crew_id)
);

-- Coupled-fact example: a science award tied to the vessel that earned it (Section 3).
CREATE TABLE science_award (
    id          uuid PRIMARY KEY,
    program_id  smallint NOT NULL REFERENCES program(id),
    vessel_id   uuid     REFERENCES vessel(id),   -- the vessel that earned it
    amount      numeric  NOT NULL,
    experiment  text,
    awarded_at  timestamptz NOT NULL DEFAULT now()
);

-- Append-only event log / outbox seed (Section 5). Cheap audit + future async path.
CREATE TABLE event_log (
    id          bigserial PRIMARY KEY,
    event_id    uuid      NOT NULL UNIQUE,     -- idempotency key (at-least-once safe)
    kind        text      NOT NULL,           -- 'soi_change','demotion','suspension','warp_commit','science_award'
    payload     jsonb     NOT NULL,
    game_time   double precision NOT NULL,
    created_at  timestamptz NOT NULL DEFAULT now()
);
```

Notes on choices: orbital elements are **columns** (queryable, FK-able, planner-friendly, ORBIT-2 reads them directly); the suspended snapshot is the **only** JSONB; the `CHECK ((state='suspended') = (snapshot IS NOT NULL))` enforces ADR-0007's "suspended ⇒ snapshot, otherwise small row" invariant in the DB itself.

---

## 9. Write-through strategy: which events trigger writes

Each is **one transaction** writing all implied state (Section 3). All are PERSIST-2 "meaningful events"; none fire on the 60 Hz tick.

| Event (CONTEXT/spec term)            | Trigger                                              | Rows written (one transaction)                                                                 |
|--------------------------------------|-----------------------------------------------------|-----------------------------------------------------------------------------------------------|
| **SOI change** (ORBIT-3)             | On-rails vessel crosses an SOI boundary             | `vessel` (new `soi_body_id` + re-parented orbital elements + `epoch`); optional `event_log`    |
| **Demotion** (PHYS-3 / SUSP-2)       | Active→on-rails when warp-safe & unattended         | `vessel.state='on_rails'` + final orbital elements; clear `snapshot`; `event_log`              |
| **Suspension** (SUSP-3)              | Last player leaves a non-warp-safe vessel           | `vessel.state='suspended'` + `snapshot` JSONB + paused `vessel_clock`; `event_log`             |
| **Warp endpoint commit** (TIME-4)    | A warp's auto-limited end-time is reached/committed  | `master_clock.game_time`; every affected `vessel`'s advanced orbit + `vessel_clock` (batch / one tx) |
| **Science award** (PROG-2)           | Player earns science                                 | `program.science_points += amount`; `science_award` (with `vessel_id`); `event_log`            |
| **Tech unlock** (PROG-3)             | Program spends science on a node                     | `program.science_points -= cost`; `tech_unlock` row; `event_log`                               |
| **Launch** (BUILD-4)                 | A Design is instantiated into a Vessel               | new `vessel` row (+ crew); `event_log`                                                         |

Not persisted on event: live station occupancy (CREW — transient, falls back to automation on disconnect, CREW-5), client-prediction state (NET-2), per-tick physics state of active vessels (only captured at suspension as a snapshot).

---

## 10. Backup / restore for a small persistent world

The whole point of one store (ADR-0007): *one backup/restore story.* At kilobytes-to-low-megabytes:

- **Primary: `pg_dump -Fc` (custom format), scheduled (e.g. daily + on clean shutdown).** Logical, compressed, portable, supports parallel/selective restore via `pg_restore`. Trivially small for our data. This is the simplest correct backup for our scale.
- **Restore: `pg_restore`** into a fresh database; then start the server, which runs the PERSIST-3 load (Section 6).
- **Optional, if we want point-in-time recovery (PITR):** `pg_basebackup` + WAL archiving (`archive_mode`/`archive_command`). This lets us recover to *any* moment, not just the last dump. Docs caution: **`pg_dump` cannot be used for PITR** (it's logical and lacks WAL info). For a 4-player coop world, daily `pg_dump` is almost certainly enough; add PITR only if losing a day of progress is unacceptable.
- **The non-negotiable rule (all sources):** *a backup is only trustworthy after you restore it.* Test restore on a schedule (monthly is the common recommendation). For us, restore = "start the server against the restored DB and confirm the world loads."

---

## Recommended implementation path

1. **Schema first (Section 8) via plain SQL.** Manage it with **DbUp** (or FluentMigrator) — run-once tracked SQL scripts, no ORM coupling. Add the `CHECK` invariants from day one.
2. **One `NpgsqlDataSource` for the process lifetime** (Section 7). All DB calls async, on a dedicated persistence task fed by an in-process channel from the sim — never on the 60 Hz thread.
3. **Synchronous write-through, `synchronous_commit = on`** (Sections 4–5). One transaction per PERSIST-2 event (Section 9), writing all implied state atomically. Keep it simple and exact; we have no write-pressure problem.
4. **Maintain `event_log`** alongside (cheap audit + idempotency keys + the seed of an outbox if async ever becomes necessary).
5. **PERSIST-3 restore = read committed rows into memory** (Section 6); advance on-rails orbits closed-form to current `game_time`; leave suspended snapshots paused.
6. **Backups: daily `pg_dump -Fc` + tested `pg_restore`** (Section 10). Defer PITR unless a day of loss is unacceptable.
7. **Defer the GIN index, async/write-behind, and a second datastore** until a concrete measurement demands them (ADR-0007's explicit stance).

## Implementation status (M0)

M0 ships the schema + repository + event sink + restorer, behind the `KSPClone.Persistence` assembly (noEngineReferences=true, references SimCore). The transport layer above the SQL is plain Npgsql.

Concrete refinements applied during M0 (worth folding into the upstream pattern):

- **Npgsql 8 netstandard2.1, vendored as a single `Npgsql.dll`** in `Assets/Plugins/Npgsql/`. Npgsql 6.x netstandard2.0 was rejected — too many BCL package dependencies that fight with Mono. See ADR-0011 for the full rationale.
- **`ON CONFLICT DO UPDATE` for upserts.** `UpsertProgram`, `UpsertClock`, `UpsertVessel` are all idempotent at the SQL level. Each opens its own connection per call (acceptable at our write rate; a single `NpgsqlDataSource` will replace this in M1 when async / connection pooling become worth it).
- **Atomicity check in tests.** `tests/persistence/test_event_sink_contract.py` deliberately inserts a bad value mid-transaction and asserts that the clock row remains at its previous value — proves the "one transaction per coupled-fact event" rule holds.
- **Restore resume via `Advance`, not a setter.** `MasterClock` keeps its `GameTimeSeconds` setter private; the `WorldRestorer` resumes the clock by setting `Rate = 1.0` and calling `Advance(gameTime)`. This preserves the single-writer rule for the master clock (Constitution Art. 1). The only setter escape hatch is `MasterClock.ClampTo`, used exclusively by the warp auto-limit.
- **No `event_log` table yet.** The synchronous write-through path is sufficient at M0 write rates. Add it when async persistence arrives (the recommended path step 4).

## Open questions for our case

- **Active-vessel crash window.** An *active-physics* vessel near a player is only persisted at suspension/demotion. If the server crashes mid-flight, that vessel reverts to its last committed orbital row (or snapshot). Is that acceptable, or do we want a periodic best-effort snapshot of active vessels (e.g. every N seconds, async)? This trades a little write volume for less lost progress.
- **Warp-commit granularity.** A long on-rails warp advances every vessel's clock. Do we persist only at the *committed endpoint*, or also at intermediate auto-limit POIs? Endpoint-only is cheapest; intermediate gives finer crash recovery during a long warp.
- **`game_time` representation.** `double precision` seconds is simple but loses precision at very large game-times. Do we need a higher-precision clock (e.g. integer ticks + offset, or NodaTime)? Affects the `master_clock`/`vessel_clock`/`epoch` columns.
- **Snapshot schema discipline.** JSONB is schemaless; a snapshot written by one build must resume in a later build. Do we version the snapshot payload (a `schema_version` key) and write upgraders, given SUSP-4 must resume "no retro-simulation, no rail-snap"?
- **Crew/station persistence boundary.** Exactly which crew/station facts survive restart vs are reconstructed as automation-fallback (CREW-4/5)? Need to pin down what `crew_assignment` must hold.
- **Migration ↔ live-world coupling.** A schema migration that changes vessel columns must coexist with already-persisted worlds. Forward-only (DbUp) plus tested restore is our plan — but who runs migrations on the server's DB, and when (startup auto-migrate vs manual)?
- **Career/Funds layering (PROG-4).** Schema includes `funds` and is built so Career layers on (ADR/PROG-4 intent). Confirm contracts/funds modeling won't force a rework when added.

---

## Annotated link list (verified)

**PostgreSQL official docs (postgresql.org/docs/current):**
- [JSON Types](https://www.postgresql.org/docs/current/datatype-json.html) — `jsonb` vs `json`, containment/existence operators, GIN `jsonb_ops` vs `jsonb_path_ops`, document-size/locking guidance. *The primary source for Section 1–2.*
- [GIN Indexes](https://www.postgresql.org/docs/current/gin.html) — how GIN works; the operator classes for jsonb live here too.
- [Transaction Isolation](https://www.postgresql.org/docs/current/transaction-iso.html) — Read Committed default, the four levels, atomicity of multi-statement transactions. *Section 3.*
- [Asynchronous Commit](https://www.postgresql.org/docs/current/wal-async-commit.html) — `synchronous_commit`, the latency/durability trade, the ~`3×wal_writer_delay` risk window, "data loss not corruption." *Section 4.*
- [Continuous Archiving & PITR](https://www.postgresql.org/docs/current/continuous-archiving.html) — `pg_basebackup` + WAL archiving; why `pg_dump` can't do PITR. *Section 10.*

**Npgsql docs (npgsql.org):**
- [Basic Usage](https://www.npgsql.org/doc/basic-usage.html) — `NpgsqlDataSource` (the 7.0+ recommended pattern), single-instance/thread-safe, default pooling, "open late close early." *Section 7.*
- [Performance](https://www.npgsql.org/doc/performance.html) — prepared statements, `NpgsqlBatch` pipelining, buffer/pool tuning. *Sections 4, 7.*
- [COPY](https://www.npgsql.org/doc/copy.html) — binary `COPY` (`BeginBinaryImport`/`StartRow`/`Write`/`Complete`) for bulk; faster than INSERT. *Sections 4, 7.*

**Patterns / engineering sources:**
- [microservices.io — Transactional Outbox](https://microservices.io/patterns/data/transactional-outbox.html) — the dual-write problem, outbox-in-same-transaction, at-least-once + idempotency, polling vs log-tailing relay. *Section 5.*
- [Heap.io — When To Avoid JSONB](https://www.heap.io/blog/when-to-avoid-jsonb-in-a-postgresql-schema) — planner has no stats inside JSONB (0.1% fallback selectivity); hybrid typed-columns + JSONB rule. *Section 2.*
- [pganalyze — Understanding Postgres GIN Indexes](https://pganalyze.com/blog/gin-index) — GIN write-amplification / maintenance cost; when a GIN index hurts inserts. *Section 1.*
- [codingdroplets — EF Core vs DbUp vs FluentMigrator (2026)](https://codingdroplets.com/ef-core-migrations-vs-dbup-vs-fluentmigrator-in-net-which-database-migration-strategy-should-your-team-use-in-2026) — migration-tool trade-offs; DbUp run-once SQL + no-rollback-by-design. *Section 7.*

*Cross-references:* [ADR-0007 single Postgres](../adr/0007-single-postgres-authoritative-store.md), [ADR-0006 fixed timestep](../adr/0006-fixed-timestep-sim-decoupled-from-render.md), [ADR-0008 Unity](../adr/0008-unity-engine.md), [spec.md PERSIST-1/2/3, PROG-1..4, SUSP-1..4, ORBIT-1..3](../../specs/spec.md), [CONTEXT.md](../../CONTEXT.md).
