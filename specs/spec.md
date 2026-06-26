# Specification

The source of truth for *what* the system does. Behavioural and testable. No technology choices (those live in [plan.md](./plan.md)).

Requirements use **EARS** (Easy Approach to Requirements Syntax):
- **Ubiquitous** — "The system shall …" (always true)
- **Event-driven** — "When <trigger>, the system shall …"
- **State-driven** — "While <state>, the system shall …"
- **Unwanted** — "If <condition>, then the system shall …"
- **Optional** — "Where <feature present>, the system shall …"

IDs are stable. Tasks cite them. Terms in *italics* are defined in [CONTEXT.md](../CONTEXT.md).

---

## TIME — clock & warp

- **TIME-1** The system shall maintain exactly one *master clock* as the authoritative *game-time*.
- **TIME-2** While no *warp* is active, the system shall advance *game-time* at 1:1 with real-time, including while the server has zero connected players.
- **TIME-3** When a player requests a *warp*, the system shall begin it only after every connected player has approved (unanimous *warp vote*).
- **TIME-4** While a *warp* is active, the system shall clamp its end *game-time* to the earliest *POI* across all vessels (*auto-limit*).
- **TIME-5** If a player disconnects while a *warp* is active, then the system shall continue the warp without that player's vote.
- **TIME-6** If a player connects while a *warp* is active, then the system shall halt the warp to baseline rate.
- **TIME-7** The system shall support two warp kinds: *physics warp* (physics still simulated) and *on-rails warp* (vessels follow analytic orbits), both gated by TIME-3.

## ORBIT — orbital model

- **ORBIT-1** The system shall model gravity as *patched conics*: each vessel is under exactly one body's gravity (its current *SOI*).
- **ORBIT-2** The system shall compute any *on-rails vessel*'s position at any future *game-time* in closed form, without step integration.
- **ORBIT-3** When an *on-rails vessel* crosses an *SOI* boundary, the system shall re-parent its *orbit* to the new body and record the crossing as a *POI*.

## PHYS — physics bubbles & vessels

- **PHYS-1** The system shall simulate active vessels in one or more concurrent *physics bubbles*, each with its own *floating origin*.
- **PHYS-2** When a player loads or approaches a vessel, or two vessels close within physics range, the system shall *promote* the affected vessel(s) to active-physics in a shared *physics bubble*.
- **PHYS-3** When an active vessel becomes *warp-safe* and has no present player, the system shall *demote* it to *on-rails*.
- **PHYS-4** The system shall simulate each vessel as a *rigid vessel*, permitting motion only at *articulation points*.
- **PHYS-5** When two vessels in the same *physics bubble* dock, the system shall join them without transferring authority between machines.
- **PHYS-6** When a structural load exceeds a part's threshold, the system shall produce a discrete *structural failure* (no continuous soft-body flex).

## SUSP — living universe & suspension

- **SUSP-1** While the server has zero connected players, the system shall keep every *on-rails vessel* synced to the *master clock*.
- **SUSP-2** When the last present player leaves a *warp-safe* vessel, the system shall *demote* it to on-rails.
- **SUSP-3** When the last present player leaves a vessel that is not *warp-safe*, the system shall *suspend* it: snapshot it and pause its *vessel clock*.
- **SUSP-4** When a player next loads a *suspended vessel*, the system shall resume it from its snapshot (no retro-simulation, no rail-snap).

## NET — networking

- **NET-1** The system shall treat server state as the sole *authoritative state*.
- **NET-2** While a player pilots a vessel, the client shall apply *client-side prediction* to that vessel only and render it with zero perceived input lag.
- **NET-3** When authoritative state diverges from a client's prediction, the client shall *reconcile*, smoothing sub-threshold corrections and hard-snapping only on large desync.
- **NET-4** The system shall display all non-controlled vessels via *interpolation* of authoritative snapshots.
- **NET-5** The server shall run its physics tick at a fixed 60 Hz and emit snapshots at 20–30 Hz.
- **NET-6** While round-trip latency is ≤150 ms, the system shall keep piloting smooth; beyond, it shall degrade gracefully.

## CREW — stations & control

- **CREW-1** The system shall partition a vessel's controllable systems across disjoint *stations* (Pilot, Engineer, Navigator).
- **CREW-2** The system shall allow a player to *occupy* at most one station at a time and to hot-swap between free stations.
- **CREW-3** If two players attempt to occupy the same station, then the system shall grant it to one and refuse the other.
- **CREW-4** While a station is unoccupied, the system shall run its *automation fallback*.
- **CREW-5** When an occupying player disconnects, the system shall vacate their station(s) to automation and reserve no seat.

## COMMS — signal & ground control

- **COMMS-1** The system shall maintain each vessel's *link* status over the *CommNet*.
- **COMMS-2** While a vessel is in *blackout*, the system shall disable its *networked capabilities*, including *in-game comms* with other players.
- **COMMS-3** While a vessel is in *blackout*, the system shall preserve onboard crew's direct hand-flight on instruments.
- **COMMS-4** If a vessel has no crew and no *link*, then the system shall make it uncommandable (dead-stick).
- **COMMS-5** The system shall provide a *ground control* mode (map, telemetry, planning, relay management) with no physics authority.
- **COMMS-6** Where a player is assigned to *ground control*, the system shall grant the program defined bonuses. *(assignment mechanism + bonus set: TBD)*

## BUILD — construction

- **BUILD-1** The system shall represent a craft as a *part tree* edited as a *Design*, distinct from an instantiated *Vessel*.
- **BUILD-2** When a player issues an *edit op*, the server shall serialize it in arrival order and broadcast it to all editors.
- **BUILD-3** Where a player holds a *subtree lock*, the system shall reject other players' *edit ops* targeting that subtree.
- **BUILD-4** When a player launches a *Design*, the system shall instantiate it as a *Vessel* in the world.

## PROG — progression

- **PROG-1** The system shall maintain one shared *space program* per world (one *tech tree*, one *science* pool, one *funds* treasury).
- **PROG-2** When a player earns *science*, the system shall add it to the shared pool.
- **PROG-3** When the program spends *science* on a *tech tree* node, the system shall unlock that node for all players.
- **PROG-4** The system shall support *game modes*: Sandbox (all unlocked), Science (tech gated by science), Career (adds funds + contracts).

## PERSIST — durability

- **PERSIST-1** The system shall store all durable state (progression and world/vessel state) in a single authoritative store.
- **PERSIST-2** When a meaningful event occurs (SOI change, demotion, suspension, committed warp endpoint, science award), the system shall write the affected state through to durable storage.
- **PERSIST-3** When the server restarts, the system shall restore the world and program exactly as last persisted.

---

## Open requirements (not yet specifiable)

- **COMMS-6** assignment mechanism & bonus set — needs design.
- **In-game comms** concrete modality (text / voice / data / pings) — needs design before COMMS-2 is fully testable.
- **Player identity / join / auth** — undecided (server password? whitelist? accounts?). No requirements written yet.
- **PROG-4 Career** contracts/funds detail — deferred past first cut.
