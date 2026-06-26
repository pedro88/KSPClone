# Reference docs

Curated, implementation-oriented references for each technical pillar. Each doc maps web sources to *our* decisions (ADRs) and requirements (spec.md), in *our* vocabulary (CONTEXT.md). Not link dumps — concepts, formulas, parameter recommendations, gotchas, and an annotated link list per topic.

| Doc | Covers | Ties to |
|---|---|---|
| [orbital-mechanics.md](./orbital-mechanics.md) | Keplerian elements, solving Kepler's eq (elliptic+hyperbolic), closed-form position(t) via universal variables, SOI & patch detection, maneuver nodes | ADR-0004 · ORBIT-1/2/3, TIME-4 |
| [netcode.md](./netcode.md) | Authoritative server, client-side prediction + reconciliation, interpolation, snapshot/delta replication, tick sync, lag-comp (argued against) | ADR-0001/0006 · NET-1..6 · P-1 |
| [unity-dedicated-server.md](./unity-dedicated-server.md) | Dedicated Server build, fixed timestep, NGO vs Netcode-for-Entities vs custom+transport, manual `Physics.Simulate`, Hetzner deploy | ADR-0008/0006 · NET-1/5 |
| [floating-origin.md](./floating-origin.md) | Float precision / the kraken, origin shifting, KSP Krakensbane, K simultaneous origins, double-global/float-local split, P-4 proposal | ADR-0003 · PHYS-1 · P-4 |
| [persistence-postgres.md](./persistence-postgres.md) | JSONB vessel snapshots, schema sketch, write-through from in-memory sim, restart/restore, Npgsql | ADR-0007 · PERSIST-1/2/3, PROG-1 |
| [rigidbody-physics.md](./rigidbody-physics.md) | Rigidbody thrust/forces, compound bodies, ArticulationBody hinges, runtime dock-merge/stage-split, joint-break failure, PhysX non-determinism | ADR-0005 · PHYS-4/5/6 |
| [ksp-multiplayer-prior-art.md](./ksp-multiplayer-prior-art.md) | DMP subspaces, Luna MP, KSP2 cautionary tale, KSP warp/on-rails model, the docking handoff problem | all ADRs (comparative) |
| [spec-driven-development.md](./spec-driven-development.md) | SDD pipeline, Spec Kit, Kiro, EARS 5 patterns, traceability, AI-agent consumption | specs/README.md |

## Caveats baked into these docs

- **Unity version drift** — Unity 6 LTS assumed in unity-dedicated-server.md & rigidbody-physics.md; version-specific APIs flagged inline (e.g. `Physics.autoSimulation` → `Physics.simulationMode`). Confirm against your installed version.
- **Some sources blocked automated fetch** (official KSP wiki, a couple of Valve/Unity blog pages) — those docs note it and used equivalent authoritative sources; verify those specific links in a browser.
- **ksp-multiplayer-prior-art.md** was written from established community knowledge under a session limit (web agents were rate-limited); its repo links are primary but verify formula/wiki links.

## Open items these docs feed

- **P-1** wire/delta snapshot format → netcode.md
- **P-4** floating-origin rebasing on bubble merge/split → floating-origin.md (has a concrete proposal)
- Server-side controlled physics setup → unity-dedicated-server.md + rigidbody-physics.md
