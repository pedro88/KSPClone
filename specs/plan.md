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
| Netcode | Prediction + reconciliation (controlled vessel), interpolation (rest) | 0013, 0015 | NET-2/3/4 |
| Wire/snapshot | Full doubles, unreliable-sequenced snapshots + reliable events; orientation on the wire | 0013, 0017, 0019 | NET-2/3/6 |
| Attitude control | Kinematic rate control (arcade); orientation replicated | 0019 | PHYS-4 |
| Surface | Flat tangent-plane ground, landed = active-physics, +Y-pole launch site | 0018 | PHYS-7 |
| Construction (VAB) | Engine-agnostic `Construction` assembly; server op-log, no CRDT; `Launch` is the only flight seam | 0020 | BUILD-1/2/3/4 |
| Storage | Single Postgres, hot in-memory + write-through | 0007 | PERSIST-1/2/3, PROG-1 |

## Component map

- **Sim core (engine-agnostic C#):** fixed-step scheduler, master clock, warp state machine (vote + auto-limit), patched-conics propagator, bubble manager, rigid-body integrator. Runs identically headless and in-client. *Constitution Art. 1, 2, 3.*
- **Replication layer:** authoritative snapshot emitter (20–30 Hz), client predictor/reconciler for the controlled vessel, interpolator for the rest, input channel per station.
- **Persistence layer:** in-memory world ↔ Postgres write-through; schemas for `program`, `vessel` (orbital elements / JSONB snapshot), `design` + `design_node` (part trees), `tech`, `science`.
- **Design system (separate path):** part-tree model, edit op-log, subtree locks, launch → instantiate. Realized as the `KSPClone.Construction` assembly (references *nothing* — Art. 7 enforced by the compiler), with `KSPClone.Launch` the sole assembly bridging to flight and `DesignStore` for persistence (ADR-0020). *Constitution Art. 7.*
- **Comms system:** CommNet link evaluation, blackout gating, in-game comms channel, ground-control read-model.
- **Client presentation:** rendering, input → station routing, map/telemetry views.

## Decisions still open in plan (block their slices, not the spec)

- ~~**P-1** Snapshot wire format & delta-compression scheme.~~ **Resolved — ADR-0013** (full doubles, unreliable-sequenced snapshots + reliable-ordered events).
- **P-2** In-game comms transport (reuse replication channel vs separate) — gated by the open spec item. *Decide before M4.*
- **P-3** Identity/join: server password vs whitelist vs accounts. *Decide before any multi-client slice ships publicly; a dev stub is fine until M4.*
- ~~**P-4** Floating-origin rebasing strategy when bubbles merge/split.~~ **Resolved — ADR-0012** (1024 m threshold, centroid origin, merge-keep-larger).
