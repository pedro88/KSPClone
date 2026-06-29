# CLAUDE.md

Instructions for any AI agent (and human) working in this repo. Read this first, every time.

## What this is

A 4-player **cooperative** space-program sim — a Kerbal Space Program-like where players *crew shared vessels* (pilot / engineer / navigator / ground control) instead of each flying solo. Persistent living shared universe, single master clock, dedicated authoritative server. Unity. Not a KSP clone — a co-op game that happens to share KSP's physics genre.

The hard part of this project is **not** rendering or even orbits. It's **time, replication, and authority**. Spend your attention there.

## How we work — spec-driven development

This repo runs SDD. The spec is the contract; code follows it. The pipeline and its files:

```
constitution → spec → plan → roadmap (milestone → slice → task) → implement
   (rules)     (what)  (how)         (execution)                    (code)
```

- **Before you change behaviour, change the spec first.** Edit [specs/spec.md](specs/spec.md) (EARS, testable, no tech), then code. Never the reverse.
- **Every task cites a requirement.** When you implement, name the `REQ-ID` (e.g. `NET-2`) it satisfies. Traceability: task → requirement → constitution. You should always be able to answer "why does this code exist?"
- **Work in thin vertical slices, spine first.** Server → wire → client, smallest first. Don't build a feature before the thing it stands on. See [specs/roadmap.md](specs/roadmap.md); start at M0-T01.
- **Don't over-specify ahead of code.** The spec is light on purpose and evolves. Spec the slice you're building, not the whole game.

## Read the docs before you act

This project has more design context written down than code. Use it. Don't re-derive, don't guess, don't contradict it silently.

| Need | Go to |
|---|---|
| What a term *means* (vocabulary) | [CONTEXT.md](CONTEXT.md) — the glossary. Use these exact words. |
| Why a decision was made | [docs/adr/](docs/adr/) — 8 ADRs, the load-bearing choices + rationale |
| What the system must *do* | [specs/spec.md](specs/spec.md) — EARS requirements with stable IDs |
| *How* we build it (tech ↔ requirement) | [specs/plan.md](specs/plan.md) |
| What to build next, in order | [specs/roadmap.md](specs/roadmap.md) + [specs/tasks/](specs/tasks/) (81 code-ready tickets) |
| **How to actually implement a pillar** | [docs/references/](docs/references/) — curated, implementation-oriented research, mapped to our ADRs |

### Reference docs — consult the matching one before coding a pillar

Don't implement orbital math, netcode, physics, or persistence from memory. Each has a vetted reference with formulas, parameter recommendations, gotchas, and annotated sources:

- **Orbital mechanics** → [docs/references/orbital-mechanics.md](docs/references/orbital-mechanics.md) — Kepler solve, closed-form `position(t)` (use universal variables), SOI & patch detection.
- **Netcode** → [docs/references/netcode.md](docs/references/netcode.md) — prediction/reconciliation/interpolation, snapshot/delta, our 60Hz / 20–30Hz / ≤150ms budget.
- **Unity dedicated server** → [docs/references/unity-dedicated-server.md](docs/references/unity-dedicated-server.md) — headless build, `SimulationMode.Script` + manual `Physics.Simulate`, transport choice.
- **Floating origin** → [docs/references/floating-origin.md](docs/references/floating-origin.md) — the kraken, K simultaneous origins, double-global/float-local split, the P-4 proposal.
- **Persistence (Postgres)** → [docs/references/persistence-postgres.md](docs/references/persistence-postgres.md) — JSONB snapshots, schema, write-through.
- **Rigid-body physics** → [docs/references/rigidbody-physics.md](docs/references/rigidbody-physics.md) — compound hull, ArticulationBody, runtime dock-merge/stage-split.
- **KSP MP prior art** → [docs/references/ksp-multiplayer-prior-art.md](docs/references/ksp-multiplayer-prior-art.md) — why subspaces/client-authority/docking-handoff failed; what we do instead.
- **SDD method** → [docs/references/spec-driven-development.md](docs/references/spec-driven-development.md) — the pipeline + EARS patterns.

Caveat: these assume **Unity 6 LTS**; version-specific APIs are flagged inline. Some external links were fetch-blocked — verify those in a browser.

## The constitution — non-negotiable (full text in [specs/constitution.md](specs/constitution.md))

These are not suggestions. If a change violates one, stop and flag it.

1. **One authoritative server, one master clock.** No client is ever authority for shared state.
2. **Fixed-timestep simulation, render-independent.** Same loop headless and in-client. Nothing in gameplay logic reads render framerate. Getting this wrong is the most expensive mistake in the project — it's painful to retrofit.
3. **On-rails is the default; physics is the exception.** Closed-form `position(t)` must always exist (patched conics, never n-body).
4. **The universe lives even when empty, but nothing is faked.** On-rails stays synced; active-physics craft that lose all presence are *suspended* at a snapshot, never retro-simulated or rail-snapped.
5. **Local crew always holds local authority.** A seated human can always hand-fly. The network adds capability; it never removes basic control.
6. **Control is partitioned, never contended.** Stations own disjoint systems; inputs can't conflict by construction.
7. **Design-time and flight-time are separate systems.** Construction never touches the vessel replication/physics path.
8. **One shared program.** Progression is shared, durable in a single Postgres, transactional with world state.

## House style (how to write code & talk here)

- **Be terse and direct.** No sycophancy, no "great question," no narrating what you're about to do at length. Say the substance.
- **Minimal diffs.** Change what the task needs, nothing more. Don't reformat, don't "improve" unrelated code, don't add abstractions for imagined futures.
- **No over-engineering.** At 4 players the whole universe is kilobytes. Don't add Redis, CRDTs, microservices, or a message bus because they're "scalable." We rejected them on purpose — see the ADRs. Simplest thing that satisfies the requirement wins.
- **Match the surrounding code** — its naming, idioms, comment density. Don't impose a new style.
- **Explain reasoning for non-obvious choices**, in the code or the PR, briefly. Skip the obvious.
- **Determinism hygiene:** the sim core is engine-agnostic C#, no `UnityEngine.Time` / no frame coupling / no wall-clock reads inside it. PhysX is non-deterministic and that's fine *because* the server is authoritative — don't try to make clients match bit-for-bit; reconcile instead.
- **State authoritative position in doubles, simulate locally in floats.** Never store world position as float32.
- **SimCore boundary is enforced by asmdef.** `Assets/Scripts/SimCore/KSPClone.SimCore.asmdef` has `noEngineReferences: true`. The Unity compiler will refuse any `using UnityEngine;` in that assembly. Transport-specific code (LiteNetLib, sockets, `Debug.Log`) lives in `KSPClone.Server` / `KSPClone.Client` — never in SimCore. If you find yourself adding a UnityEngine import to SimCore, the file is in the wrong assembly. See ADR-0009.
- **`MasterClock.GameTimeSeconds` has a private setter for a reason.** Single writer is `SimWorld.Tick` (Constitution Art. 1). The only legitimate caller of any other write path is `MasterClock.ClampTo`, and the only caller of `ClampTo` is `WarpAutoLimit` — it exists so the warp can land exactly on a POI when the final tick would overshoot. Don't add another writer.
- **When unsure, ask or check the docs — don't invent.** Especially Unity APIs (version drift) and orbital formulas (verify against the reference doc).
- **Commit at the end of every task.** When a ticket is implemented and its acceptance criteria are met, commit before moving on. One commit per task, message cites the ticket id (e.g. `M0-T01: stand up SimCore assembly + ServerBootstrap`). The commit is the proof the slice advanced.

## Maintain the model as you go (this is real work, not bookkeeping)

- New/sharpened term → update [CONTEXT.md](CONTEXT.md) immediately. It's a glossary, not a spec — no implementation details in it.
- Hard-to-reverse + surprising + a real trade-off → write an ADR in [docs/adr/](docs/adr/) (next number, one paragraph is fine).
- Behaviour change → spec first.
- Used a web source to decide something → fold it into the matching reference doc, annotated.

## Open decisions (don't silently pick — they're tracked)

See the table at the bottom of [specs/roadmap.md](specs/roadmap.md). Currently: **P-1** wire/delta format, **P-4** floating-origin rebasing (proposal exists), in-game comms modality, **COMMS-6** ground-control bonus mechanism, identity/join/auth. Resolve the one that gates your slice before starting it; a dev stub is fine for M0–M3.

## Build order right now

`M0 — Skeleton` first: headless fixed-step server + master clock + one on-rails vessel + a client that sees it + warp vote + Postgres write-through. No features, no rendering polish. Prove the spine end-to-end, then layer. First ticket: **M0-T01** in [specs/tasks/M0.md](specs/tasks/M0.md).
