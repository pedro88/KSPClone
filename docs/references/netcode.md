# Netcode Reference — Authoritative Server, Prediction, Interpolation, Snapshots

Curated reference for implementing KSPClone's networking. Distilled from primary sources
(Gaffer On Games / Glenn Fiedler, Gabriel Gambetta, Valve Source wiki, Overwatch GDC).
Terms used here are the project's own (see `../../CONTEXT.md`): *authoritative state*,
*client-side prediction*, *reconciliation*, *interpolation*.

## Why this matters for us

- We chose a **dedicated authoritative server** (ADR 0001) with a **fixed 60 Hz tick**
  decoupled from render (ADR 0006). Every technique below assumes exactly that model — the
  same model these sources were written for — so they map almost 1:1 onto NET-1..6.
- The pilot needs **zero perceived input lag** (NET-2) on a single vessel while the server
  stays the sole source of truth (NET-1): that is precisely client-side prediction +
  reconciliation, and we apply it to *one rigid body*, not a part tree (PHYS-4).
- Everyone else's vessels (NET-4) and other *physics bubbles* are shown via **interpolation
  of 20–30 Hz snapshots** (NET-5), smooth to ≤150 ms RTT (NET-6). The hard design choice this
  doc feeds is plan **P-1**: the snapshot wire format + delta scheme.

---

## 1. Authoritative server model

**In our terms.** The server holds *authoritative state* (NET-1): the canonical transform,
velocity, and resources of every vessel. Clients send *inputs* (station commands: throttle,
attitude, staging) and receive *state*. Two data flows only: input up, state down. This is the
foundation that makes everything else possible — Overwatch's lead noted the entire
rollback/replay model "simply cannot exist" without a dedicated authoritative server.

**Techniques.**
- Structure the protocol around two messages: **Input** (player commands) and **State**
  (position + velocity + resource deltas). Keep them separate; never let a client assert state.
- Server simulates active *physics bubbles* (PHYS-1) at fixed tick; on-rails vessels are
  analytic (ORBIT-2) and need no tick — they ride the *master clock* and are replicated as
  orbital elements + epoch, not per-tick transforms.
- Design every channel so the **latest** input/state is usable without waiting for a lost
  packet to be resent (favours UDP-style unreliable + redundancy over TCP).

**Parameter recs.** Server tick 60 Hz (16.67 ms) per ADR 0006. Snapshot/state emit 20–30 Hz.
At 4 players the active-vessel count is small, so per-vessel full state is cheap relative to a
KSP part-tree; spend the saved bandwidth on a higher snapshot rate (toward 30 Hz) to shrink
client interpolation delay (see §3).

**Gotchas.**
- Server must reject/clamp impossible inputs (don't trust the client) even in a coop game —
  it keeps determinism and prevents desync, not just cheating.
- Resource/structural events (staging, *structural failure* PHYS-6) are **discrete events**,
  not continuous state — replicate them as reliable events, not as something to interpolate.

---

## 2. Client-side prediction & server reconciliation

**In our terms.** The piloting client runs its *own* vessel's physics locally from inputs and
renders immediately (*client-side prediction*, NET-2). When an *authoritative state* update
arrives it *reconciles* (NET-3): snap the predicted body to the server state **as of the tick
the server had processed**, then re-simulate forward through every input the server hasn't
acknowledged yet. Applied to the locally-piloted vessel only.

**The algorithm (Gambetta / Fiedler / Overwatch all agree).**
1. Tag every input with a monotonically increasing **sequence number** (or tick number).
2. Keep a ring buffer of `{seq, input, resulting_predicted_state}` for unacked inputs.
3. Apply each input locally the instant it's generated (predict) and render.
4. Server processes inputs in order and, in each state packet, returns the **sequence number
   of the last input it applied** (the ack).
5. On receiving a state packet, the client:
   - sets the predicted body to the server's authoritative state,
   - **discards** all buffered inputs `<=` the acked seq,
   - **replays** the remaining buffered inputs on top, in order, to rebuild "now".
6. If the replayed result matches the previous prediction within a threshold, the player sees
   nothing; if it diverges, correct it — smoothly for small errors, hard-snap for large (NET-3).

Worked example (Gambetta, 250 ms RTT): two "move right" inputs. At t=250 ms the ack covers
input #1 (x=11); client drops #1, replays #2 → x=12. At t=350 ms ack covers #2; queue empties,
state already correct. No visible correction.

**Reconciliation smoothing (Fiedler, State Synchronization).** Keep a position-error and
orientation-error offset, decay it toward zero each render frame instead of teleporting:
- small error (≤ ~25 cm): blend factor ~0.95/frame (slow, invisible smoothing),
- large error (≥ ~1 m): blend factor ~0.85/frame (faster catch-up),
- beyond a hard threshold: snap instantly (a big desync looks worse smeared than snapped).
The *visual* offset decays; the *simulation* is already at the authoritative value.

**Parameter recs for us.**
- Predict at the same 60 Hz fixed dt as the server. Input buffer must hold at least
  `RTT / 16.67 ms` inputs; at 150 ms RTT that's ~9 inputs — size for ~0.5 s (≈30 inputs) for
  headroom, as Fiedler does (~0.5 s of history at 60 Hz).
- Error thresholds above are good starting points; in metres they suit human-scale craft but
  re-tune for fast orbital speeds (a "small" error at 2 km/s is many metres per tick — consider
  expressing thresholds relative to velocity, see Open Questions).

**Gotchas.**
- Reconciliation only works if client and server step the **same fixed dt with the same code**
  (§5). A render-coupled client step would diverge every frame — exactly what ADR 0006 forbids.
- **Quantize state identically on both sides** before each step (Fiedler). If the server
  rounds positions/quaternions for the wire, the client must predict from the rounded values or
  it drifts a little every tick. Pick wire precision deliberately (P-1).
- Corrections "arrive in the past" (Gambetta): apply at the acked tick, then replay — never
  apply a stale server state directly to "now".

---

## 3. Entity interpolation for non-controlled vessels

**In our terms.** Everything the client does *not* pilot — other players' vessels, vessels in
other *bubbles* — is shown via *interpolation* (NET-4): no prediction, just replay of
authoritative snapshots a fixed delay in the past, so there's always a pair of snapshots to
interpolate between.

**Techniques.**
- Buffer incoming snapshots with their tick stamps. Render at `server_time − interp_delay`,
  finding the two snapshots that bracket that render time and interpolating between them.
- **Position:** linear is acceptable; **Hermite spline using the replicated velocity** at each
  endpoint removes the subtle jitter/curvature error linear interp shows on fast bodies
  (Fiedler) — relevant for us since orbital vessels move fast and smoothly.
- **Orientation:** SLERP between quaternions; no angular velocity needed.
- On a missing snapshot, keep interpolating toward the next received one — never stall.

**Interpolation delay / buffer sizing (the key numbers).**
- Rule (Fiedler): make the delay large enough to **lose two packets in a row** and still have
  something to interpolate toward → ~3× the send interval, + jitter margin.
- Valve default: `cl_interp = 0.1 s` (100 ms) = two snapshots at the 20 Hz default updaterate —
  a constant, near-imperceptible view lag.
- Delay scales with send rate (Fiedler): 10 pps → ~350 ms; **30 pps → ~150 ms**; 60 pps → ~85 ms.

**Parameter recs for us.** At our 20–30 Hz snapshots, target an interpolation delay of
**~2 snapshot intervals + jitter margin**:
- 20 Hz (50 ms interval): ~100 ms + ~20–30 ms jitter ≈ **120–150 ms**.
- 30 Hz (33 ms interval): ~66 ms + jitter ≈ **~100 ms**.
Prefer 30 Hz to keep the other-vessel view delay near 100 ms while staying inside the ≤150 ms
RTT smoothness target (NET-6). Make `interp_delay` a tunable, derived from the measured snapshot
rate and observed jitter, not a hardcoded constant.

**Gotchas.**
- Don't interpolate **discrete events** (staging, decoupling, *structural failure*) — fire them
  on a reliable event channel at their tick; interpolating across them looks like teleporting.
- Each *physics bubble* has its own *floating origin*; interpolate other bubbles' vessels in
  **their** local frame or a shared world frame, but be consistent — origin rebasing (P-4) must
  not inject a one-frame jump into interpolated positions.
- Self is shown in the present (predicted); others in the past. That asymmetry is correct and
  expected — don't try to "fix" it.

---

## 4. Snapshot replication, delta compression, baseline/ack

**In our terms.** How *authoritative state* gets onto the wire (P-1). Because we have a *rigid
vessel* (PHYS-4), each vessel is a small fixed record — transform + linear/angular velocity +
a few resource scalars + flags — **not** a part tree. That makes both full snapshots and deltas
far cheaper than KSP.

**Two viable models (pick per object class):**

- **Snapshot interpolation (pure):** send complete visual state per snapshot; client only
  interpolates, never simulates other vessels. Simplest; what §3 assumes. Good default for
  other-bubble and other-player vessels.
- **State synchronization (delta + extrapolation):** run a light sim on the client for remote
  objects and send periodic corrections + velocity. More complex, lower bandwidth, needs
  identical quantization both sides. Probably overkill for 4 players — note it but don't start
  here.

**Delta compression / ack scheme (Fiedler, State Synchronization).**
- Each packet header carries the **most-recent acked sequence number** (a reliable ack of what
  the client has). Per-object updates are encoded **relative to that baseline** snapshot.
- Encode the baseline offset compactly: e.g. 5 bits of offset from the acked sequence gives
  ~0.5 s of history at 60 Hz — enough to always delta against something the client holds.
- **Priority accumulator**: each object accrues a priority each tick; send the highest
  accumulated ones that fit the packet budget, then reset only the *sent* objects' accumulators.
  This fairly spreads updates across many objects under a bandwidth cap. For us, with few active
  vessels, prioritise the pilot's nearby/bubble vessels over distant ones.

**Quantization (both models).** Quantize on the wire and **dequantize identically on both
ends** before simulating/predicting (§2 gotcha). Fiedler's state-sync precision (because remote
objects are *simulated*): ~4096 position steps/m, 15-bit quaternion components. Pure snapshot
interpolation tolerates coarser (~512/m, 9-bit) since it's only displayed. We can mix: coarse
for interpolated-only vessels, fine for the predicted own-vessel reconciliation path.

**Parameter recs for us.**
- Start with **pure full snapshots** for remote vessels (small records → cheap), add delta
  compression only if bandwidth becomes a real constraint. Measure first.
- 16-bit sequence numbers; per-packet "most-recent-acked" field; UDP with redundancy (resend
  unacked deltas in subsequent packets rather than reliable retransmit).
- Bandwidth sanity: Fiedler's heavy 900-cube demo fit in 256 kbps with delta+priority; our
  handful of rigid vessels at 30 Hz is comfortably under that.

**Gotchas.**
- If you delta against a baseline the client never received, you desync silently — the ack of
  most-recent-received snapshot is mandatory, not optional.
- Mixing quantization precision is fine, but the **reconciled own-vessel path must round-trip
  losslessly enough** that prediction doesn't drift; test this explicitly (P-1 decision).

---

## 5. Fixed timestep & tick synchronization

**In our terms.** ADR 0006 already mandates a fixed-dt sim decoupled from render, identical on
headless server and client. This section is the *how*, plus client↔server clock alignment.

**Fixed timestep (Fiedler, Fix Your Timestep).** Accumulator loop — render produces time, sim
consumes it in fixed `dt` chunks:
```
accumulator += frameTime
while accumulator >= dt:        # dt = 1/60 s
    previous = current
    simulate(dt)               # deterministic step
    accumulator -= dt
alpha = accumulator / dt
render(lerp(previous, current, alpha))   # smooth leftover-time blend
```
The `alpha` blend between the two latest physics states removes render stutter without changing
the sim — this is *render interpolation*, distinct from the *network interpolation* of §3
(both exist; don't conflate them).

**Tick / clock synchronization (Overwatch, Tim Ford GDC 2017).**
- The client runs its sim **ahead of the server** by roughly `½·RTT + 1 command frame`, so each
  input packet arrives just before the server reaches the tick it's stamped for. Inputs that
  arrive late are useless (the server already passed that tick).
- **Adaptive command buffer**: the server watches its per-client input queue. On **starvation**
  (queue running empty) it tells the client to *speed up* — dilate the local step (e.g. treat
  16 ms as ~15.2 ms) so it emits inputs slightly faster and refills the server-side buffer.
  When healthy, the client dilates the other way to drain excess buffer (which is latency).
- This is a closed loop: keep the smallest input buffer that survives this client's jitter.
  Quake-derived sliding-input-window technique; the buffer trades latency for loss tolerance.

**Parameter recs for us.**
- `dt = 1/60 s` server and client (ADR 0006). Snapshots 20–30 Hz are independent of tick rate.
- Implement the adaptive ahead-of-server offset: start the client `½·RTT + 1 tick` ahead,
  adjust ±~5% step rate based on a server-reported buffer-health signal. At our 20–60 ms LAN/EU
  VPS RTT this offset is tiny (~1–2 ticks).
- Send a redundant window of recent inputs each packet (Fiedler: ~120 frames of 6-bit inputs is
  ~90 bytes) so single-packet loss never starves the server.

**Gotchas.**
- Floating-point determinism across machines is **not guaranteed** (Fiedler, Overwatch). We
  don't need bit-exact lockstep — server is authoritative and reconciles — but our predicted
  own-vessel step should be *close enough* that reconciliation rarely corrects visibly. Quantize
  inputs/state to help.
- Two interpolations exist: **render-alpha** (this section, leftover fixed-step time) and
  **network interp_delay** (§3, snapshot playout). Keep them clearly separate in code.

---

## 6. Lag compensation — note: we likely DON'T need it

**In our terms.** Lag compensation is the server **rewinding other entities' positions** back
to the moment a client issued an action, to adjudicate that action fairly.

**How it works (Valve).** The server keeps ~1 s of position history per entity. When it
executes a fire command it computes:
```
command_time = current_server_time − packet_latency − client_view_interpolation
```
rewinds all other players to `command_time`, traces the shot, then restores them. This exists
specifically for **instant-hit / hitscan weapons**, where the shooter aimed at where a target
*appeared* (interpolated, in the past) and the server must honour that.

**Why it almost certainly does not apply to us.**
- KSPClone has **no hitscan**. Vessel interactions are *physics*: thrust, gravity, collisions,
  docking (PHYS-5). There is no "I clicked exactly on that pixel at time T" adjudication.
- Docking and convergence are handled by putting both vessels in the **same physics bubble
  before contact** (ADR 0001, PHYS-2/PHYS-5) — they're simulated together in one frame, so
  there's no cross-machine timing to compensate.
- Reconciliation (§2) already handles the pilot's own-vessel latency; interpolation (§3) handles
  viewing others. No rewind needed.

**When we might reconsider.** Only if we ever add an instant-hit-style interaction adjudicated
against another player's *displayed* position (e.g. a precise "grab"/laser-dock-assist aimed at
a moving target). Even then, prefer simulating both bodies in a shared bubble over rewind.
**Recommendation: do not implement lag compensation in the first cut.**

---

## 7. Our specifics

- **Predicting ONE rigid body, not a part tree.** PHYS-4 makes the predicted/replicated unit a
  single transform + velocity (+ articulation-point states, +resources). The §2 prediction loop
  runs over one rigid body's integrator — vastly simpler and cheaper than KSP's per-part network
  state, and it makes reconciliation thresholds well-defined (one position, one orientation).
- **Reconciling under fixed timestep.** Replay (§2) must use the **same fixed dt** as the
  original prediction; store inputs keyed by tick, not wall-clock, so replay reproduces the
  exact step sequence (§5). This is the direct payoff of ADR 0006.
- **Vessels in other physics bubbles.** Each bubble has its own *floating origin*. Remote-bubble
  vessels are **interpolated only** (§3) — never predicted — and we may send them at lower
  priority / coarser quantization (§4 priority accumulator). Origin rebasing on merge/split (P-4)
  must be applied as a coordinate transform that does **not** introduce a visible interpolation
  discontinuity.
- **On-rails vessels are not ticked.** They replicate as orbital elements + epoch against the
  *master clock* (ORBIT-2, SUSP-1), evaluated client-side in closed form — effectively perfect,
  loss-tolerant "interpolation" for free. Only *active-physics* vessels use §3/§4 snapshots.

---

## Recommended implementation path

1. **Lock the fixed-step core first (ADR 0006).** Identical 60 Hz `dt` sim module shared by
   headless server and client, accumulator + render-alpha (§5). Everything depends on this.
2. **Authoritative loop, no prediction yet (NET-1).** Client sends tick-stamped inputs; server
   simulates and returns full snapshots; client just renders the latest. Get the round trip and
   the input/state split right before optimising.
3. **Add entity interpolation (NET-4, §3).** Snapshot buffer + `interp_delay` (~100–150 ms,
   derived from snapshot rate). This makes *all* remote vessels smooth and is independent of
   prediction. Decide the **P-1 wire format** here (full snapshots first).
4. **Add own-vessel prediction + reconciliation (NET-2/3, §2).** Sequence-numbered inputs, ring
   buffer, snap-to-acked + replay, smoothed error decay with hard-snap threshold. Tune
   thresholds for orbital speeds.
5. **Add tick/clock sync + input redundancy (§5).** Client runs `½·RTT + 1 tick` ahead;
   adaptive command buffer driven by a server buffer-health signal; redundant recent-input
   window per packet for loss tolerance (NET-6).
6. **Optimise only if measured (§4).** Add delta-compression-against-acked-baseline + priority
   accumulator + tuned quantization once bandwidth becomes a real constraint — not before. Explicitly
   **skip lag compensation** (§6).

## Implementation status (M0)

M0 ships the wire-agnostic **data structures** (no transport yet — that lands in Slice 0.3 transport wiring). Specifically:

- **Transport choice (deferred but documented):** LiteNetLib-class UDP library. The
  M0 ticket explicitly excludes Unity high-level netcode (per ADR-0008). LiteNetLib
  is community-standard, AOT-friendly, has both reliable and unreliable channels
  built in, and no managed-code GC pressure on the hot path. The wire layer
  lives in the `KSPClone.Server` assembly — never in SimCore (see ADR-0009).
- **P-1 wire format (partially resolved):** pure full snapshots for now, no delta
  compression. The `SnapshotEmitter` emits one `SnapshotBundle` per emission tick
  with all vessel state inline. The `VesselSnapshot` record struct carries
  `vesselId`, `gameTime`, `seq` (single emitter-wide counter), `position`,
  `velocity`. Per-vessel seq and delta-against-acked-baseline arrive when the
  prediction loop lands (Slice 1.2 / Slice 1.3) and bandwidth becomes a real
  constraint — not before.
- **Snapshot rate:** 25 Hz default (`SnapshotEmitter.DefaultRateHz = 25.0`,
  configurable 20–30 Hz). At 25 Hz the client interpolation delay defaults to
  100 ms (~2.5× the snapshot interval), matching the §3 "lose two packets" rule.
- **World handshake:** the server sends a `WorldHandshakeMessage` immediately on
  connect: the current `MasterClock.GameTimeSeconds` plus every vessel's full
  `Orbit` (not the state vector — closed-form propagation makes the elements
  strictly cheaper to ship, and lets the client evaluate future state itself
  for free). The client populates a `ClientWorldModel` from the handshake.
- **Netcode split:** the simulation core (`SimCore`) is wire-agnostic — no
  UDP, no LiteNetLib, no UnityEngine. The transport layer ships these
  messages on its unreliable channel. The `SnapshotEmitter.IConnectionSink`
  interface is the seam: transport implements it, emitter doesn't know it's
  UDP.

## Open questions for our case (feed plan P-1: wire/delta format)

- **Wire record layout per vessel:** which fields (transform, lin/ang velocity, resources,
  articulation-point states, flags) and at what **quantization**? Need a precision that keeps
  the predicted own-vessel reconciliation drift-free while staying coarse for interpolated-only
  remote vessels (§4). This is the core of P-1.
- **Full snapshots vs delta-against-baseline:** start full (cheap for rigid vessels); define the
  ack field (most-recent-received sequence) now so delta can be added later without a protocol
  break. Decide before Slice 1.2.
- **Reconciliation thresholds at orbital velocity:** are absolute-metre thresholds (Fiedler's
  25 cm / 1 m) meaningful at km/s? Likely need velocity-relative or per-tick-displacement
  thresholds. Decide alongside the prediction integrator choice.
- **Floating-origin coupling to interpolation (P-4):** how does bubble merge/split rebasing
  interact with the snapshot buffer so remote vessels don't jump on rebase? Resolve P-4 and the
  interpolation frame together.
- **Snapshot rate choice 20 vs 30 Hz:** 30 Hz buys a ~100 ms interp delay (vs ~120–150 ms at
  20 Hz) for modest extra bandwidth on few vessels — likely worth it; confirm against the VPS
  bandwidth budget.
- **On-rails replication channel:** orbital-elements-on-event vs periodic — almost certainly
  event-driven (on SOI change / maneuver edit) since closed-form propagation needs no stream.
  Confirm this is a separate channel from the active-physics snapshot stream.

---

## Annotated links

Primary sources, all verified accessible at time of writing unless noted.

- **Glenn Fiedler — Snapshot Interpolation** — https://gafferongames.com/post/snapshot_interpolation/
  Interpolation buffer sizing (lose-2-packets rule → ~3× send interval), delay-vs-send-rate
  table (10/30/60 pps → 350/150/85 ms), Hermite + SLERP. Core of §3.
- **Glenn Fiedler — State Synchronization** — https://gafferongames.com/post/state_synchronization/
  Delta-against-acked-baseline, 5-bit baseline offset, priority accumulator, adaptive error
  smoothing (0.95/0.85 blend), both-sides quantization, 256 kbps budget. Core of §4 + §2 smoothing.
- **Glenn Fiedler — Networked Physics** — https://gafferongames.com/post/networked_physics_2004/
  Input/state split, store-moves + rewind-and-replay reconciliation, "corrections arrive in the
  past", floating-point determinism caveat. Core of §2.
- **Glenn Fiedler — Fix Your Timestep!** — https://gafferongames.com/post/fix_your_timestep/
  Accumulator loop, render/sim decoupling, render-alpha interpolation. Core of §5; backs ADR 0006.
- **Glenn Fiedler — Deterministic Lockstep** — https://gafferongames.com/post/deterministic_lockstep/
  Inputs-only networking, playout-delay buffer, redundant inputs (~90 bytes/120 frames), FP
  determinism is hard. Context for why we use authoritative+reconcile, not lockstep (§2, §5).
- **Gabriel Gambetta — Fast-Paced Multiplayer (series)** —
  https://www.gabrielgambetta.com/client-server-game-architecture.html
  - Client-Side Prediction & Reconciliation — https://www.gabrielgambetta.com/client-side-prediction-server-reconciliation.html
    Sequence-numbered inputs, pending queue, drop-acked + replay. Cleanest statement of §2's algorithm.
  - Entity Interpolation — https://www.gabrielgambetta.com/entity-interpolation.html
    Render others in the past, one-update-interval delay, bracket-and-interpolate. Backs §3.
- **Valve — Source Multiplayer Networking** — https://developer.valvesoftware.com/wiki/Source_Multiplayer_Networking
  Tickrate, snapshots, `cl_interp 0.1` (= 2 snapshots), `cl_updaterate`/`cl_cmdrate`, prediction,
  lag compensation. **Note:** the official wiki returned HTTP 403 to automated fetch; verbatim
  mirror used instead: https://gist.github.com/CoolOppo/fe0586836de3fb2f90f9
- **Overwatch — Gameplay Architecture and Netcode (Tim Ford, GDC 2017)** —
  https://www.gdcvault.com/play/1024001/-Overwatch-Gameplay-Architecture-and (video; GDC Vault)
  16 ms command frames, client-ahead-by-½RTT+1, adaptive command buffer (15.2 ms dilation under
  starvation), predict-everything, ECS, rollback needs authoritative server. Core of §5 clock sync.
- **Valve — Latency Compensating Methods (Bernier)** — https://developer.valvesoftware.com/wiki/Latency_Compensating_Methods_in_Client/Server_In-game_Protocol_Design_and_Optimization
  Original lag-compensation paper (server rewinds to command time for hitscan). **Note:** returned
  HTTP 403 to automated fetch; lag-comp specifics in §6 cross-checked from the Source wiki mirror
  above. Read for the rewind formula if implementing lag comp — which we recommend against (§6).
