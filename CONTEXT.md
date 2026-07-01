# KSPClone

A 4-player cooperative space-program simulator: a persistent, living shared universe with a single master clock, where players crew shared vessels (pilot, engineer, navigator, ground control) rather than each flying their own ship in isolation.

## Time

**Game-time**:
The single in-universe clock all players and vessels share. Always advances — never frozen, even when the server is empty.
_Avoid_: sim time, world time

**Master clock**:
The authoritative source of game-time, owned by the dedicated server. There is exactly one. Implemented as `MasterClock` (SimCore), single `SimWorld` owns it, single writer is `SimWorld.Tick` (Constitution Art. 1). One escape hatch exists (`MasterClock.ClampTo`, called only by the warp auto-limit to land exactly on a POI).
_Avoid_: host clock

**Baseline rate**:
Game-time advances at 1:1 with real-time when no warp is active (including when the server is empty).

**Warp**:
Player-initiated acceleration of game-time above baseline. Every warp of either kind requires a consensus vote — because the single master clock means any warp advances time for everyone. Two distinct kinds below.
_Avoid_: time acceleration, fast-forward

**Warp kind**:
The category of warp requested. Two values: `Physics` (low multiplier, physics keeps stepping) and `OnRails` (high multiplier, analytic conic only). Multipliers in the unsupported gap between the two kinds are rejected outright — see ADR-0010.

**Warp state**:
The state of the warp FSM (Idle / Voting / Active). Idle is the default. A warp request transitions Idle → Voting; unanimity transitions Voting → Active; cancel / auto-limit / connection-driven halt transitions back to Idle.

**Warp end reason**:
The reason a warp was halted back to Idle: `Cancelled` (requester cancels the open vote), `AutoLimit` (clock reached the earliest global POI), `HaltedByConnection` (a player joined mid-warp — TIME-6).

**Warp vote**:
The consensus mechanism gating any warp. A player requests a warp; it begins only once **all** connected players approve (unanimous). A non-approval is "not yet," not a permanent veto. A player who disconnects mid-warp stops being a voter and warp continues; a player who connects during warp never consented, so warp halts to baseline and they join the next vote.

**Point of interest (POI)**:
Any upcoming event the warp must not skip: a maneuver node, a sphere-of-influence (SOI) transition, or an atmospheric-interface crossing. Computed across all vessels in the world.

**Auto-limit**:
Even after a warp vote passes, the server clamps the warp's actual end-time to the soonest POI across *all* vessels. The vote grants permission; the auto-limit guarantees no one silently skips their burn or transition. Implemented as `WarpAutoLimit.Tick`; the only legitimate caller of `MasterClock.ClampTo`.

**Physics warp**:
Low-multiplier warp (≤ x4) where the physics simulation still runs each step. Used in atmosphere or near docking.

**On-rails warp**:
Warp above the physics ceiling (> x4) where vessels stop being physically simulated and follow analytic (Keplerian) orbits. Contiguous with physics warp — no rejected middle range (ADR-0010).
_Avoid_: time warp (ambiguous — say which kind)

## Orbital model

**Patched conics**:
The orbital model: a vessel is under exactly one body's gravity at a time (its current SOI), so every orbit is an analytic conic. Transitions between bodies are "patched" at SOI boundaries. No n-body perturbations.

**Sphere of influence (SOI)**:
The region around a body within which that body is the sole gravitational influence on a vessel. Crossing an SOI boundary is a POI. Per body, computed once from its orbit around its parent (Laplace form, KSP-style spherical approximation).

**Orbit**:
A vessel's analytic conic around its current SOI body. Stored as six classical Keplerian elements + epoch + parent body. Because it is closed-form, position at any future game-time is solved directly (Kepler) — making on-rails propagation exact and effectively free, including jumping straight to an arbitrary future time.

**Kepler propagator**:
The closed-form solver `KeplerPropagator.StateAt(orbit, gameTime, registry)` that turns (orbit, game-time) into (position, velocity) in the parent body's inertial frame in one evaluation — no time-stepping. M0 implements the elliptic path (M001 of René Schwarz); hyperbolic / parabolic orbits throw NotSupportedException and are deferred to the universal-variable formulation of [docs/references/orbital-mechanics.md](../docs/references/orbital-mechanics.md).

**World-frame position**:
The position of a body or vessel in the shared universe frame, accounting for the parent's own position (and velocity, once we add it). For an on-rails vessel it is `parent.WorldPositionOf(t) + KeplerPropagator.StateAt(orbit, t).position`. M0 drops the parent's velocity because the static body tree of T06 places every body at the origin; richer positioning will land when bodies actually orbit.

**Parent-frame position**:
The position of a vessel relative to its current SOI body's centre — what the Kepler propagator returns directly. Preferred for on-rails replication because it is cheaper than world-frame.

**State vector**:
A (position, velocity) pair in some frame. The Kepler propagator outputs a state vector in the parent frame; the StateVectorToOrbit inverse converts a state vector back to classical elements when re-parenting at an SOI crossing.

## Vessels & simulation

**Warp-safe state**:
A vessel state that can be propagated analytically: stable orbit or landed, no active thrust, not under atmospheric forces. Required for on-rails warp and for living-while-empty.

**Active-physics vessel**:
A vessel currently simulated step-by-step (in atmosphere, under thrust, docking, or near a player). Cannot be on-rails.
_Avoid_: loaded vessel

**On-rails vessel**:
A warp-safe vessel propagated analytically rather than simulated. Cheap; the default state for everything not near a player.

**Rigid vessel**:
A vessel simulated as a single rigid body (split into separate bodies only at decouplers/stages), with no inter-part flex. Makes replication a per-vessel transform + velocity (not per-part) and keeps the server cheap. No KSP-style wobble.

**Articulation point**:
A deliberate place a vessel may flex or move: robotic hinges/rotors and docking ports. The only joints that exist; everything else is rigid.

**Structural failure**:
A discrete event (decoupler fires, joint breaks past a load threshold) rather than a continuous soft-body simulation.

**Surface**:
The solid boundary of a *celestial body* at its radius, on which a vessel can rest or land. M2.5 models it as a *ground plane* — a flat, static collider tangent to the body at the launch/landing site — not curved terrain (ADR-0018).

**Ground contact**:
Server-authoritative collision between an active-physics vessel and a body's *surface*: the ground plane's normal force balances gravity so a landed vessel holds its weight instead of falling through (PHYS-7). Simulated by PhysX inside the vessel's *physics bubble*, like any other active-physics force.

**Radial-up**:
The local "up" at a point on a body's *surface*: the unit vector from the body's centre to that point. The demo launch site is chosen at the body's +Y pole so radial-up equals world +Y, letting the client's untumbled +Y-up presentation hold without a surface-frame rotation (ADR-0018).

**Vessel clock**:
Each vessel's own "as-of game-time" stamp (`Vessel.VesselClockSeconds`). On-rails vessels stay synced to the master clock (Constitution Art. 4). An active-physics vessel that is left unattended (all players gone) is *suspended* — snapshotted, its vessel clock pauses, and it resumes from that snapshot when a player next loads it. Its vessel clock can therefore lag the master clock.
_Avoid_: vessel time

**Vessel id**:
The opaque server-assigned identifier of a vessel (`VesselId`). UUID, generated on construction. Stable across the vessel's lifetime; survives persistence (UUID primary key in the `vessel` table).

**Player id**:
The opaque server-assigned identifier of a connected player (`PlayerId`). UUID, generated on connect. Never reused within a session lifetime.

**Tick**:
One evaluation of the fixed-timestep accumulator. The server runs 60 ticks per second of real time (`SimScheduler.FixedDt = 1/60`). Decoupled from render frame rate (Constitution Art. 2).

**Tick rate**:
The measured number of ticks per real-time second. M0 target: 60 ±1, including under stalls.

**Suspended vessel**:
An active-physics vessel parked at a snapshot because no player is present to simulate it. Excluded from game-time advance until reloaded.

**Physics bubble**:
A cluster of vessels close enough to interact physically, simulated together by the server with one shared floating origin. The server runs multiple bubbles concurrently (one per cluster of nearby players/vessels), so players can run parallel missions far apart.
_Avoid_: physics range, loaded scene

**Floating origin**:
The local coordinate origin of a physics bubble, kept near its vessels so float precision holds at large world distances (prevents the "kraken"). Each bubble has its own. *Server-side*, double-anchored and rebased in discrete steps (ADR-0012).

**Client render origin**:
The client's single presentation-only origin, re-anchored every frame on the controlled vessel's predicted world position; every rendered vessel's Unity transform is `(float)(worldDouble − renderOrigin)`. Distinct from the server's per-bubble floating origin: ephemeral, re-derived each frame, never authoritative, and no discrete rebase events. The camera rides the controlled vessel's float-local frame — no scaled space (ADR-0015).
_Avoid_: floating origin (that's the server, per-bubble concept)

**Promotion / demotion**:
A vessel *promotes* from on-rails to active-physics when a player loads or approaches it, or two vessels close within physics range (forming/joining a bubble). It *demotes* back to on-rails when warp-safe and unattended. Because two converging vessels share a bubble *before* contact, docking needs no cross-machine authority handoff.

## Networking

**Authoritative state**:
The server's canonical simulation state. The single source of truth for every vessel.

**Server bootstrap**:
The MonoBehaviour (`ServerBootstrap` in the Server assembly) that constructs the authoritative `SimWorld` in `Awake()` and drives `SimScheduler.Advance(Time.unscaledDeltaTime)` from `Update()`. The single place the headless server loop is wired; everything else is engine-agnostic.

**Connection registry**:
The server's authoritative list of currently connected players (`ConnectionRegistry`). Add / Remove fire `PlayerConnected` / `PlayerDisconnected` events synchronously; the warp FSM and the membership sync subscribe to these to keep warp membership correct during play.

**Player session**:
The server-side record of one connected player (`PlayerSession`). In M0 it carries a server-assigned `PlayerId` and a `ConnectedAt` timestamp; transport-specific data (LiteNetLib peer, etc.) is held by the transport layer, not here — SimCore stays engine-agnostic.

**World handshake**:
The message the server sends to a client immediately on connect (`WorldHandshakeMessage`): the current `MasterClock.GameTimeSeconds` plus every vessel's orbital elements. Lets the client list the vessel and display game-time without waiting for the first snapshot.

**Snapshot**:
A periodic authoritative read of one vessel's state at a server-tick — `VesselSnapshot` carries `vesselId`, `gameTime`, `seq`, `position`, `velocity`, `angularVelocity`, and `lastProcessedClientTick` (the highest pilot-input tick the server has applied to this vessel — the ack the client's reconciler replays from). The server emits snapshots in bundles (`SnapshotBundle`) at 25 Hz by default (configurable 20–30 Hz), decoupled from the 60 Hz sim tick (NET-5 / ADR-0006).

**Snapshot bundle**:
The per-emission collection of vessel snapshots stamped with a single shared `gameTime` and `seq`. One bundle per emitter tick.

**Snapshot buffer**:
The per-vessel time-ordered sliding window the client keeps (`SnapshotBuffer`) for interpolation. Oldest dropped when capacity is reached.

**Interpolation**:
How a client displays everything it does *not* control (other vessels, other bubbles): replayed from authoritative snapshots with a small delay (default 100 ms ≈ 2.5× snapshot interval at 25 Hz). No prediction.

**Client-side prediction**:
The pilot's client simulates its *own* controlled vessel locally from inputs and renders immediately, so the stick has no perceived input lag. Applied only to the locally-controlled vessel. M1 territory.

**Reconciliation**:
Correcting a client's predicted state toward authoritative server state when they diverge, smoothed to avoid visible snapping. M1 territory.

**Transport**:
The wire layer (LiteNetLib or equivalent UDP library) that ships messages between client and server. Transport-specific code lives in the Server / Client assemblies, never in SimCore — the simulation must remain engine-agnostic and unit-testable.

## Construction

**Part**:
The atomic building block of a craft. Parts attach parent-child into a tree with constraints.

**Part tree**:
The tree of parts (parent-child attachment) that defines a craft's structure. Edit ops and subtree locks target a node by stable ID.

**Design**:
A blueprint edited in the VAB — a part tree with no world state. Distinct from a Vessel. You *edit* designs.
_Avoid_: craft (ambiguous), blueprint (use Design)

**Vessel**:
An instantiated craft existing in the world with position, velocity, resources, and crew. Created by launching a Design. You *fly* vessels. Construction concerns never touch the vessel replication/physics path.
_Avoid_: ship, craft

**Launch**:
Instantiating a Design into a Vessel in the world. The boundary between the design-time system and the flight/physics system.

**Edit op**:
A single construction action (add/remove/move part) sent to the authoritative server, applied in arrival order, and broadcast to all editors. The server serializes all edits; no CRDT.

**Subtree lock**:
An advisory claim a player places on a subassembly ("I've got the upper stage") so others can't edit that subtree until released. Layered on top of the op-log to prevent conflicts.

## Crew & control

**Station**:
A control seat on a vessel owning a disjoint set of systems. Canonical stations: **Pilot** (attitude, throttle), **Engineer** (staging, resources, power, abort), **Navigator** (maneuver nodes, map/transfer planning). No two stations control the same system, so player inputs never conflict.
_Avoid_: seat, role (loosely), console

**Occupying**:
A player occupies at most one station at a time and may hot-swap between free stations. Two players cannot occupy the same station at once. On disconnect a player vacates their station(s), which fall back to automation; there is no reserved seat — on reconnect they may re-occupy any free station. When the last present player leaves a vessel, it demotes to on-rails (if warp-safe) or suspends (if not).

**Automation fallback**:
The minimal autopilot an unoccupied station runs so short-crewed or solo vessels stay flyable but degraded — e.g. empty Pilot holds attitude (SAS), empty Engineer auto-stages on flameout. Always dumber than a human occupant.
_Avoid_: autopilot (alone — implies more capability than this has)

## Progression

**Space program**:
The single cooperative agency all four players share. There is one program per world — one tech tree, one treasury, one science pool. Not four competing agencies.
_Avoid_: agency (loosely), save

**Science**:
The shared resource earned by experiments/exploration that gates tech-tree unlocks. Pooled across all players.

**Tech tree**:
The shared progression graph; nodes unlocked by spending Science. One per Space program.

**Funds**:
The shared treasury (Career mode only). Second economy layered on later; not in the first cut.

**Game mode**:
Sandbox (all tech pre-unlocked — a flag), Science (tech gated by Science — the first build target), or Career (Science + Funds + contracts — added later). The progression data model is built so Career layers on without reworking Science.

## Comms & ground control

**Link / signal**:
A vessel's connection to the comms network (relays + ground stations). Can be lost by going behind a body or out of range.
_Avoid_: connection (generic)

**CommNet**:
The network of relays and ground stations that carries a vessel's link.

**Networked capabilities**:
Everything a vessel gets *only* while it has a link: map-view planning detail, maneuver-node computation assist, ground-control assist, cross-vessel telemetry, science transmission, and **in-game player-to-player communication**. Lost on signal loss.

**Blackout**:
The state of a vessel with no link. Onboard crew keep direct hand-flight on instruments always (local crew = local authority), but lose all networked capabilities — including in-game comms with other players. (Out-of-band voice such as Discord is outside the game and not gated.) A probe with no crew and no link is dead-stick.

**In-game comms**:
A real in-game communication channel/feature (text/voice/data-sharing, pings, shared markers) carried over the CommNet. A built system, not external voice. Cut during blackout.

**Ground control**:
An off-vessel mode (map, telemetry, mission planning, relay-network management) any player can open anytime; holds no physics authority. Optionally, a player can be *assigned* to ground control via a (TBD) mechanism, which grants bonuses (e.g. better telemetry/assist) — but no one is required to staff it.
_Avoid_: mission control (use for the place), tower

## World

**Living universe**:
The world's state evolves whenever game-time advances, regardless of whether anyone is online. Persistence means the universe has a fixed home (the dedicated server) and survives all disconnects — not merely that state is saved.
_Avoid_: persistent world (ambiguous)

## Persistence

**World repository**:
The single Postgres-backed write-through layer (`WorldRepository`, KSPClone.Persistence). M0 ships `UpsertProgram`, `UpsertClock`, `UpsertVessel`, `LoadClock`, `LoadVessels`, and `Migrate`. Holds no in-memory state — the in-memory `SimWorld` is the hot copy; the repository is the durable shadow.

**Persistence event sink**:
The thin orchestrator (`PersistenceEventSink`) that subscribes to the meaningful-event hooks of the SimCore (SOI transition, warp commit) and writes through to Postgres. Per docs/references/persistence-postgres.md §3, each event commits its coupled facts (clock + affected vessels) atomically.

**World restorer**:
The loader (`WorldRestorer.RestoreOrSeed`) called on server start: rebuilds the in-memory `SimWorld` from the persisted rows (clock + every vessel), or seeds a fresh world on an empty DB. Resume rate is always 1.0 (no warp-in-progress is ever persisted).

**Write-through**:
The persistence pattern this project uses: meaningful events write to Postgres in the same transaction as their in-memory effect, so the DB is always the commit point. Routine 1:1 baseline ticks are NOT written through — the 60 Hz loop stays in-memory. Clock checkpoints are written only on warp commit in M0; periodic checkpoints arrive in M1.

**Empty-DB seed**:
The bootstrap callback passed to `WorldRestorer.RestoreOrSeed` for the first run on a fresh database. The restorer runs the callback, then persists the seed so subsequent restarts find state.
