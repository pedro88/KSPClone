# Roadmap — Milestones · Slices · Tasks

The execution breakdown under [spec.md](./spec.md).

- **Milestone** — a demonstrable increment. Exit criterion = a named set of requirements *live and verified*.
- **Slice** — a thin vertical path (server → wire → client) delivering a subset of a milestone's requirements. The unit you build and demo.
- **Task** — atomic step inside a slice. Cites the requirement(s) it satisfies and a concrete acceptance check. Format: `[ ] Tn — description → check (REQ-IDs)`.

Ordering follows the constitution's "prove the spine first" (Art. 10): M0 is the skeleton; features layer on after. PROG (M5) may interleave once tech needs meaning.

## Implementation status (live)

Updated as milestones land. Detailed ticket status lives in [GitHub issues](https://github.com/pedro88/KSPClone/issues) (one issue per task).

- **M0 — Skeleton (the spine): ✅ COMPLETE.** The full server→wire→client spine is built, tested, and demonstrated end-to-end over a **headless dedicated-server build** (T1) talking to a client over **real LiteNetLib UDP**. Highlights: `SimCore.ServerSimulation` composes the warp-vote FSM, auto-limit, POI scan, SOI re-parenting, snapshot emitter and connection registry per fixed tick; `ServerBootstrap` restores-or-seeds (`WorldSeed`) with Postgres write-through (PERSIST-1/2/3 verified against Postgres); `KSPClone.Net` ships the P-1 wire codec (full snapshots, lossless round-trip), a loopback double (integration-tested) and the LiteNetLib transport; `KSPClone.Client` connects, interpolates snapshots, and sends warp commands; `ServerBuilder` produces the headless `-batchmode` server binary. Exit requirements TIME-1/2/3/4/5/6, ORBIT-1/2/3, NET-1/4/5, PERSIST-1/2/3 all live and verified. Known deferrals carried into M1: no on-rails orbit/SOI-change replication channel yet (client vessel `parent`/elements come from the connect handshake; position is streamed); snapshots ship ReliableOrdered (move to unreliable-sequenced when measured). Warp policy: **ADR-0010 amended** — bands contiguous (≤4× physics, >4× on-rails), no gap.
- **M1 — Physics bubble + prediction: ✅ COMPLETE (code, incl. end-to-end wiring); runtime validation pending.** Tickets T01–T19 (bricks) + Slice 1.6 T20–T25 (wiring) landed on `feat/m1`. Decisions **P-1** and **P-4** resolved up-front via ADR-0012 (floating-origin rebasing: 1024 m threshold, centroid origin, merge-keep-larger, empty bubbles destroyed) and ADR-0013 (full doubles on the wire, unreliable-sequenced snapshots + reliable-ordered events with the 11-tag event surface). SimCore data model: `PhysicsBubble`, `BubbleRegistry`, `BubbleManager` (clustering pass, 50 km radius via M1-T02), `FloatingOriginManager`, `PromotionController`, `DemotionController`, `WarpSafeEvaluator`, `RigidVesselMass`, `EngineModule` + `VesselEngineRegistry` (Tsiolkovsky-accurate mass flow), `StructuralFailureSystem`, `InputChannel`, `ClientPredictor` + `ClientReconciler` + `LatencyMonitor`, `SuspensionController` + `VesselSnapshot` + `SnapshotStore`, `DockingSystem` + `DockingMerger`. Server/Unity host: `UnityBubbleHost` (PhysicsScene per bubble), `RigidVesselBody`, `BubbleIntegrator` (60 Hz step with gravity, thrust, attitude, rebase). Net/P-1: `WireCodec` extended with `PilotInput` (MessageType 4). **Slice 1.6 wiring (ADR-0014/0015/0016):** `ServerSimulation` now composes the M1 passes in the canonical per-tick order (soi→promote→cluster→`IBubbleStepper`→demote→suspend→warp→snapshot) with the PhysX integration injected as `IBubbleStepper`; `ServerBootstrap` seeds the demo craft's mass/engines and mounts `UnityBubbleHost`+`BubbleIntegrator`+`ServerVesselBodies` (rigidbody lifecycle on promote/demote/suspend/resume); a SimCore `ControlRegistry`+`Station` gates pilot-input authority (`OccupyStation`/`SubmitPilotInput`); the snapshot carries the reconciliation ack (`LastProcessedClientTick`) + `AngularVelocity`; `ClientFlightModel` predicts the controlled vessel, reconciles, and interpolates the rest; `ClientWorldRenderer` renders into a single controlled-vessel-anchored float frame. Exit requirements PHYS-1/2/3/4/5/6, NET-1/2/3/6, SUSP-2/3/4, ART-1/3/4 all implemented; SimCore/Net logic unit-tested in EditMode (incl. composition order, control authority, snapshot ack round-trip, predict/reconcile/boundary, client render origin). Caveat: the Unity/PhysX host (T21) and the 3D scene (T25) are authored but not yet run against a live editor this cycle — runtime validation of `BubbleIntegrator.Step()`, per-bubble `PhysicsScene` allocation, and in-flight piloting happens on the next in-editor run (and the new `.cs` files need their `.meta` generated on import).
- **M2 — Multi-crew stations: ✅ COMPLETE (code) except T09; validated headless.** On `feat/m2`. Occupancy (CREW-2/3) was delivered early by M1-T22 (`Station` + `ControlRegistry`). New: `StationSystemMap` — the disjoint system partition proven by construction (CREW-1, Art. 6); `ServerSimulation.SubmitStationInput` routes per-station input gated by system ownership (CREW-1); `StationDriver` + `IStationAutomation` source unoccupied stations from automation each tick (CREW-4), with `PilotSasAutomation` (rate-damping attitude hold); disconnect vacates with no reserved seat and reconnect re-races (CREW-5). Auto-stage (M2-T09) is deferred — no staging model until the M3 part tree. Surfaced + fixed a latent M1 attitude bug (rigidbody inertia tensor unset → torque diverged). Validated in Unity batchmode: EditMode 257/257, PlayMode 3/3 (incl. SAS damping + Tsiolkovsky Δv). CREW-1/2/3/4(partial)/5 live.
- **M2.5 — Presentation & surface (post-M2, `feat/sun-celestial-body`):** procedural skybox (Sun/Earth/Moon billboards, M2.5-T01) + surface launch with ground contact (M2.5-T02, PHYS-7, ADR-0018 — landed craft is active-physics resting on a flat tangent-plane collider at the body's +Y pole so radial-up = world +Y). SimCore parts unit-tested; PhysX contact validated in-editor. See [tasks/M2.5-celestial-skybox.md](tasks/M2.5-celestial-skybox.md).
- **M3 — Collaborative VAB: ✅ COMPLETE (code); runtime/DB validation pending.** On `feat/m3-collaborative-vab` (ADR-0020). T01–T08 landed: the engine-agnostic `KSPClone.Construction` assembly (asmdef references *nothing* — machine-checkable Art. 7) holds the part-tree model (`Design`/`PartNode`/`PartTree`/`PartCatalog`), the pure `PartTreeMutator` (add/remove/move + typed rejections), the server op-log (`DesignEditService` — arrival-order seq, server-assigned node ids, fold==tree invariant), editor sessions + ordered client `DesignReplica` + `DesignEditCoordinator` (two editors converge), and `SubtreeLockManager` (claim/deny/auto-release + `PreApplyGate` enforcement). `KSPClone.Net` adds the Design replication channel codec (msg tags 5–10); `KSPClone.Launch` (references SimCore **and** Construction — the sole meeting point) instantiates a Design→Vessel; `KSPClone.Persistence` adds `DesignStore` (relational write-through + restore). BUILD-1/2/3/4 implemented; SimCore/Construction/Net logic unit-tested in EditMode. Pending: ServerNetHost/ClientNetPeer dispatch of the new message tags + DB write-through wiring (validated in-editor/against Postgres next — same staging as M1).
- **M4 — Comms & ground control:** not started.
- **M5 — Progression (Science mode):** not started.

P-1 and P-4 are **resolved**: ADR-0012 + ADR-0013.

Open decisions remaining (gating specific slices) are tracked at the bottom of this file.

---

## M0 — Skeleton (the spine)

**Goal:** one authoritative fixed-step server, master clock, one on-rails vessel, one client that sees it, warp by vote, durable across restart. No real physics, no rendering polish.
**Exit:** TIME-1/2/3/4/5/6, ORBIT-1/2/3, NET-1/4/5, PERSIST-1/2/3 live and verified.
**Demo:** client connects, sees a vessel coasting in orbit; players vote a warp; clock jumps but auto-stops at the next SOI crossing; kill the server, restart, world resumes identically.

### Slice 0.1 — Fixed-step headless server + master clock
- [x] T1 — Unity headless build target that runs with no renderer → server process starts under `-batchmode -nographics` and logs ticks (NET-1, Art.2) — `ServerBuilder` (dedicated-server subtarget) + `ServerBootstrap` batchmode config
- [x] T2 — Fixed 60 Hz scheduler decoupled from frame → tick interval stable under artificial render stalls (NET-5, Art.2)
- [x] T3 — Master clock advances 1:1 with real-time, single instance → 60 s real = 60 s game-time, ±1 tick (TIME-1, TIME-2)

### Slice 0.2 — On-rails vessel + patched conics
- [x] T4 — Body/SOI model + one vessel with orbital elements → vessel data structure persists an orbit (ORBIT-1)
- [x] T5 — Closed-form Kepler propagator `position(game-time)` → position at t+arbitrary matches numeric reference within tolerance, no stepping (ORBIT-2)
- [x] T6 — SOI-crossing detection → predicted crossing time registered as a POI, orbit re-parents on cross (ORBIT-3)

### Slice 0.3 — Client connect + observe
- [x] T7 — Client connects to server, receives world handshake → client lists the vessel and current game-time (NET-1)
- [x] T8 — Snapshot emitter at 20–30 Hz + client interpolation → client renders vessel position smoothly from snapshots (NET-4, NET-5)

### Slice 0.4 — Warp vote + auto-limit
- [x] T9 — Warp request + unanimous vote state machine → warp starts only on all-approve; non-approve blocks (TIME-3)
- [x] T10 — Two warp kinds plumbed (physics / on-rails), on-rails advances clock fast → x1000 advances game-time, vessel stays on analytic orbit (TIME-7)
- [x] T11 — Auto-limit to earliest global POI → warp halts exactly at next SOI crossing, never past (TIME-4)
- [x] T12 — Vote membership on connect/disconnect → disconnect mid-warp continues; connect mid-warp halts to baseline (TIME-5, TIME-6)

### Slice 0.5 — Persistence
- [x] T13 — Postgres schema (program, vessel, clock) + write-through on POI/warp-commit → row updates observed at events (PERSIST-1, PERSIST-2)
- [x] T14 — Restart restore → kill + restart server; clock, vessel orbit, and POIs resume identically (PERSIST-3, SUSP-1)

---

## M1 — Physics bubble + prediction (one hand-flown vessel)

**Goal:** promote a vessel to active rigid-body physics in a floating-origin bubble, hand-fly it with zero input lag, suspend it correctly on leave, dock two of them.
**Exit:** PHYS-1/2/3/4/5/6, NET-2/3/6, SUSP-2/3/4 live and verified.
**Demo:** player loads a vessel (it promotes), lights the engine and hand-flies under prediction with no lag; a second vessel approaches and they dock with no hitch; player leaves mid-burn → vessel suspends; reload resumes from snapshot.

### Slice 1.1 — Bubble + floating origin (resolve P-4 first)
- [x] T15 — Bubble manager: create/destroy bubble per vessel cluster → multiple bubbles coexist far apart in world coords (PHYS-1)
- [x] T16 — Per-bubble floating origin + rebasing → vessel at 10^9 m from origin simulates without precision artifacts (PHYS-1, Art.3)
- [x] T17 — Promotion on player approach/load → vessel switches on-rails→active at range threshold (PHYS-2)
- [x] T18 — Demotion when warp-safe + unattended → vessel returns to analytic orbit, state continuous across switch (PHYS-3)

### Slice 1.2 — Active rigid-body flight (resolve P-1 first)
- [x] T19 — Rigid-body integrator in bubble, thrust + gravity → vessel accelerates under engine, matches expected delta-v (PHYS-4)
- [x] T20 — Input channel: pilot throttle/attitude routed to server → server applies authoritative inputs (NET-1, CREW-1 partial)
- [x] T21 — Discrete structural failure at load threshold → joint/decoupler breaks as an event, no soft flex (PHYS-6)

### Slice 1.3 — Prediction + reconciliation
- [x] T22 — Client predicts controlled vessel from local inputs → stick has zero perceived lag at 80 ms simulated RTT (NET-2, NET-6)
- [x] T23 — Server reconciliation, smoothed sub-threshold / snap on large desync → injected divergence corrects without visible pop under threshold (NET-3)

### Slice 1.4 — Suspension lifecycle
- [x] T24 — Suspend non-warp-safe vessel on last-leave → snapshot taken, vessel clock pauses (SUSP-3)
- [x] T25 — Resume from snapshot on reload, no retro-sim → reloaded vessel continues exactly from snapshot state (SUSP-4)
- [x] T26 — Demote (not suspend) when warp-safe on last-leave → orbiting vessel goes on-rails instead (SUSP-2)

### Slice 1.5 — Docking
- [x] T27 — Two vessels in one bubble, docking-port latch → ports within tolerance join into one vessel (PHYS-5)
- [x] T28 — No authority handoff at contact → both vessels already share the bubble before latch; no state jump (PHYS-5, Art.1)

---

## M2 — Multi-crew stations

**Goal:** multiple players operate one vessel via disjoint stations; empty stations automate; disconnect degrades gracefully.
**Exit:** CREW-1/2/3/5 live and verified; **CREW-4 partial** (Pilot SAS hold live; Engineer auto-stage deferred — see Known gaps).
**Demo:** three players board one vessel as Pilot/Engineer/Navigator; pilot flies, engineer stages, navigator sets a node — no conflicts; one disconnects, their station auto-takes-over; another reconnects and re-seats.

**Known gaps (deferred, not bugs — each blocked on enabling work that does not exist yet; building it now would be an abstraction ahead of its code, against house style):**
- **M2-T09 — Engineer auto-stage on flameout (CREW-4):** deferred post-M3. No staging model (stage grouping / staging subsystem) exists until the M3 part tree. `EngineerAutomation` is a no-op until then.
- **M2-T08 — Pilot SAS hold target (CREW-4):** ships as angular-*rate* damping toward zero, not hold-to-a-captured-orientation, because SimCore carries no authoritative orientation field yet. Honest degradation; revisit when orientation is replicated.
- **M2-T03 — typed `OccupyResult{Granted|Refused(StationTaken)}` returned to the client + winner/loser refusal logging (CREW-3):** the server already refuses contention deterministically (`ControlRegistry.Occupy` first-writer-wins, returns bool), but there is no client-facing result channel on the wire and no refusal log. Blocked on the open identity/join + wire-result decision (see open-decisions table).

### Slice 2.1 — Station occupancy
- [x] T29 — Station model + occupy/vacate/hot-swap → player takes a free station, leaves, takes another (CREW-2)
- [x] T30 — Single-occupant enforcement → second claimant on same station refused (CREW-3)

### Slice 2.2 — Disjoint input routing
- [x] T31 — Map each station to its disjoint system set → pilot inputs never affect staging and vice-versa (CREW-1)
- [x] T32 — Concurrent multi-station inputs on one vessel → three stations act same tick without conflict (CREW-1, Art.6)

### Slice 2.3 — Automation fallback
- [~] T33 — Per-station automation when unoccupied → empty Pilot holds attitude (SAS) **done**; empty-Engineer auto-stage **deferred** (needs M3 staging model) (CREW-4)

### Slice 2.4 — Disconnect handling
- [x] T34 — Disconnect vacates station(s) to automation, no reserved seat → leaver's station automates; reconnect re-seats any free station (CREW-5)

---

## M3 — Collaborative VAB

**Goal:** multiple players build one Design concurrently; launch instantiates a Vessel.
**Exit:** BUILD-1/2/3/4 live and verified.
**Demo:** two players edit one rocket Design at once; one locks the upper stage; edits serialize cleanly; launch puts the craft on the pad as a Vessel.

### Slice 3.1 — Design model + edit op-log
- [ ] T35 — Part-tree Design model, distinct from Vessel → editing a Design never touches flight state (BUILD-1, Art.7)
- [ ] T36 — Server-serialized edit op-log + broadcast → concurrent add/remove/move from two clients converge identically (BUILD-2)

### Slice 3.2 — Subtree locks
- [ ] T37 — Claim/release advisory subtree lock → locked subtree rejects others' ops, released subtree accepts (BUILD-3)

### Slice 3.3 — Launch
- [ ] T38 — Launch instantiates Design → Vessel on pad → new Vessel appears in world with crew slots, Design unchanged (BUILD-4)

---

## M4 — Comms & ground control

**Goal:** signal gates networked capability (incl. in-game comms); blackout keeps hand-flight; ground-control mode with bonuses.
**Exit:** COMMS-1/2/3/4/5 live and verified. COMMS-6 pending design.
**Blocked on:** P-2 (comms transport), in-game comms modality, COMMS-6 mechanism. Resolve before the affected slices.

### Slice 4.1 — Link & blackout gating
- [ ] T39 — CommNet link evaluation (relays + occlusion by bodies) → link drops when vessel passes behind a body (COMMS-1)
- [ ] T40 — Blackout disables networked capabilities, preserves hand-flight → no nodes/map-assist in blackout; manual stick still works (COMMS-2, COMMS-3)
- [ ] T41 — Crewless + no-link vessel uncommandable → unmanned probe in blackout ignores commands (COMMS-4)

### Slice 4.2 — In-game comms (resolve modality + P-2 first)
- [ ] T42 — In-game comms channel carried over CommNet → players exchange comms only with link; cut in blackout (COMMS-2)

### Slice 4.3 — Ground control (resolve COMMS-6 first)
- [ ] T43 — Ground-control mode: map/telemetry/planning, no physics authority → mode reads world state, cannot command physics (COMMS-5)
- [ ] T44 — Assignment + bonuses → assigned player grants defined program bonuses (COMMS-6, design pending)

---

## M5 — Progression (Science mode)

**Goal:** one shared program; science earns and unlocks tech for all. *May interleave from M1 onward — tech needs meaning early.*
**Exit:** PROG-1/2/3/4 (Sandbox + Science) live and verified. Career deferred.
**Demo:** a science experiment adds to the shared pool; spending it unlocks a part for everyone; sandbox flag pre-unlocks all.

### Slice 5.1 — Shared program + science + tech tree
- [ ] T45 — Program/science/tech schema in Postgres, shared → all clients see one pool and tree (PROG-1, PERSIST-1)
- [ ] T46 — Earn science → pool; spend → unlock for all → experiment adds science; unlock visible to every client (PROG-2, PROG-3)

### Slice 5.2 — Modes
- [ ] T47 — Mode flag: Sandbox pre-unlocks, Science gates by science → sandbox world starts fully unlocked; science world gated (PROG-4)

> **Career** (funds + contracts) intentionally deferred — out of first cut. Data model in Slice 5.1 leaves room for it without rework.

---

## Cross-cutting open items (gate specific slices)

| Item | Blocks | Owner decision |
|---|---|---|
| ~~P-1 wire/delta format~~ | Slice 1.2 | **resolved — ADR-0013** |
| ~~P-4 floating-origin rebasing~~ | Slice 1.1 | **resolved — ADR-0012** |
| P-2 comms transport | Slice 4.2 | plan |
| In-game comms modality | Slice 4.2 | spec |
| COMMS-6 assignment + bonuses | Slice 4.3 | spec |
| Identity / join / auth | public multi-client ship | plan (dev stub OK until M4) |
