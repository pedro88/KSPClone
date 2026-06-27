# KSPClone

A 4-player cooperative space-program simulator. Persistent living shared universe, single authoritative server, one master clock. Players crew shared vessels (pilot, engineer, navigator, ground control) — not solo ships. Built in Unity 6 LTS, server-authoritative, on-rails by default (closed-form Kepler), patched conics, durable in Postgres.

> See [CONTEXT.md](CONTEXT.md) for the ubiquitous language, [specs/spec.md](specs/spec.md) for what the system does, [CLAUDE.md](CLAUDE.md) for the working rules, and [specs/roadmap.md](specs/roadmap.md) for current status.

## Repository layout

```
.
├── CLAUDE.md                 # house rules + constitution summary
├── CONTEXT.md                # ubiquitous language / glossary
├── docs/
│   ├── adr/                  # 11 architecture decision records
│   └── references/           # curated, implementation-oriented research
├── specs/
│   ├── spec.md               # EARS requirements (the contract)
│   ├── plan.md               # tech ↔ requirement mapping
│   ├── constitution.md       # 8 non-negotiable principles
│   ├── roadmap.md            # milestones → slices → tasks + status
│   ├── tasks/                # 81 code-ready tickets
│   └── README.md             # SDD pipeline overview
├── Assets/
│   ├── Scripts/
│   │   ├── SimCore/          # engine-agnostic C# (no UnityEngine)
│   │   ├── Server/           # Unity host MonoBehaviour
│   │   └── Persistence/      # Postgres-backed repository (no UnityEngine)
│   ├── Plugins/Npgsql/       # Npgsql 8 netstandard2.1 DLL (vendored)
│   └── Tests/EditMode/       # NUnit EditMode tests
└── tests/
    └── persistence/          # Python schema round-trip tests (no Unity)
```

### Assembly boundaries

- **`KSPClone.SimCore`** — pure C#. `noEngineReferences: true` (asmdef-enforced). The simulation core: Kepler propagator, master clock, scheduler, vessel/orbit/body model, warp FSM, connection registry, snapshot emitter / interpolator, POI scanner, vessel transition. Engine-free, testable without Unity.
- **`KSPClone.Server`** — Unity host. `ServerBootstrap` MonoBehaviour constructs the authoritative `SimWorld` and drives `SimScheduler.Advance(Time.unscaledDeltaTime)`. Transport adapters (LiteNetLib etc.) live here.
- **`KSPClone.Persistence`** — `noEngineReferences: true`, references SimCore. `WorldRepository`, `PersistenceEventSink`, `WorldRestorer`. Uses Npgsql 8 netstandard2.1.
- **`KSPClone.*.EditModeTests`** — NUnit tests, run inside Unity Editor (Test Framework 1.4.5).

## Current status

**M0 (Skeleton) — 22/23 landed** on the `develop` branch. The remaining ticket (T02, dedicated-server build target) needs Unity Editor to validate.

Everything is on `develop`:
- Headless-fixed-step server + master clock (T01, T03, T04, T05)
- On-rails vessel + patched conics — closed-form Kepler propagator (T06–T11)
- Warp FSM, kinds, POI auto-limit, connection membership (T12 partial, T16–T20)
- World handshake + snapshot emitter + client interpolation — wire-agnostic data structures (T13, T14, T15)
- Postgres schema + write-through on events + restore on restart (T21–T23)

The wire transport itself (LiteNetLib) lands in a future slice; the data structures are ready.

## Development

### Opening the project in Unity

Open this folder in Unity 6 LTS (6000.0.78f1 — `ProjectSettings/ProjectVersion.txt`). The `Assets/Plugins/Npgsql/` DLL is vendored; no NuGet restore step needed.

### Running tests

In Unity: `Window → General → Test Runner → EditMode → Run All`.

Outside Unity (schema only):
```sh
pip install psycopg2-binary
python3 tests/persistence/test_schema_roundtrip.py
python3 tests/persistence/test_event_sink_contract.py
python3 tests/persistence/test_restore_contract.py
```

These assume a local Postgres on `localhost:5433` with credentials `greenu/greenu` and a `greenu_test` database.

### Branching & tickets

- `main` — stable, merged work.
- `develop` — integration.
- `feat/<ticket-id>-<slug>` — one branch per ticket (the M0 PRs follow this pattern).

Each ticket has a GitHub issue with the same id (e.g. `M0-T01`). One commit per ticket; the commit message cites the ticket id.

## How to read this repo

If you are new here:
1. Read [CONTEXT.md](CONTEXT.md) — the terms you will see everywhere.
2. Skim the 8 articles of the constitution in [specs/constitution.md](specs/constitution.md).
3. Read the 11 ADRs in [docs/adr/](docs/adr/) — they explain *why* every load-bearing decision.
4. Open [specs/roadmap.md](specs/roadmap.md) to see where the project is and where it's going.
5. Pick a ticket in [specs/tasks/](specs/tasks/) and read its spec before touching code.