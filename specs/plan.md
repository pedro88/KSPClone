# Technical Plan

*How* the spec is realized. Each decision cites the requirement(s) it satisfies and the ADR that records it. This file changes when technology changes; [spec.md](./spec.md) does not.

## Stack

| Concern | Choice | ADR | Satisfies |
|---|---|---|---|
| Engine | Unity (headless dedicated-server build) | 0008 | NET-1, NET-5 |
| Authority | Single dedicated authoritative server | 0001 | NET-1, TIME-1 |
| Sim loop | Fixed 60 Hz timestep, render-decoupled | 0006 | NET-5, NET-6 |
| Orbital | Patched conics, closed-form Kepler | 0004 | ORBIT-1/2/3 |
| Active physics | K concurrent bubbles, per-bubble floating origin | 0003 | PHYS-1/2/3/5 |
| Structure | Rigid bodies, articulation-only joints | 0005 | PHYS-4/6 |
| Living/empty | On-rails always synced; per-vessel suspension | 0002 | SUSP-1..4 |
| Netcode | Prediction + reconciliation (controlled vessel), interpolation (rest) | — | NET-2/3/4 |
| Storage | Single Postgres, hot in-memory + write-through | 0007 | PERSIST-1/2/3, PROG-1 |

## Component map

- **Sim core (engine-agnostic C#):** fixed-step scheduler, master clock, warp state machine (vote + auto-limit), patched-conics propagator, bubble manager, rigid-body integrator. Runs identically headless and in-client. *Constitution Art. 1, 2, 3.*
- **Replication layer:** authoritative snapshot emitter (20–30 Hz), client predictor/reconciler for the controlled vessel, interpolator for the rest, input channel per station.
- **Persistence layer:** in-memory world ↔ Postgres write-through; schemas for `program`, `vessel` (orbital elements / JSONB snapshot), `tech`, `science`.
- **Design system (separate path):** part-tree model, edit op-log, subtree locks, launch → instantiate. *Constitution Art. 7.*
- **Comms system:** CommNet link evaluation, blackout gating, in-game comms channel, ground-control read-model.
- **Client presentation:** rendering, input → station routing, map/telemetry views.

## Decisions still open in plan (block their slices, not the spec)

- **P-1** Snapshot wire format & delta-compression scheme (affects NET-5 throughput). *Decide before Slice 1.2.*
- **P-2** In-game comms transport (reuse replication channel vs separate) — gated by the open spec item. *Decide before M4.*
- **P-3** Identity/join: server password vs whitelist vs accounts. *Decide before any multi-client slice ships publicly; a dev stub is fine for M0–M3.*
- **P-4** Floating-origin rebasing strategy when bubbles merge/split. *Decide before Slice 1.1.*
