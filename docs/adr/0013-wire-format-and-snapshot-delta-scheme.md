# Wire format and snapshot delta scheme (resolves P-1)

Resolves open item **P-1** (plan §"Decisions still open in plan"). Sibling to [ADR-0001](0001-dedicated-authoritative-server-single-clock.md) and [ADR-0006](0006-fixed-timestep-sim-decoupled-from-render.md) — those ADRs commit to authoritative + fixed-step; this one decides the on-wire shape and the channel layout.

## Status

M0 already ships full snapshots over a transport with reliable-ordered delivery ([roadmap §M0](../roadmap.md) "Known deferrals carried into M1: snapshots ship ReliableOrdered (move to unreliable-sequenced when measured)"). This ADR locks the M1 target: full-double full-snapshots on an unreliable-sequenced channel, a per-packet ack field prepared for the eventual delta phase, and a dedicated reliable-ordered channel for discrete structural events.

## Decisions

### 1. Wire precision: full doubles for every vessel field

Every snapshot field that names a position, velocity, or orientation is encoded as **double** on the wire (8 bytes per scalar component). One `VesselSnapshot` record carries:

- `vesselId : uint32`
- `gameTime : double` (the master-clock second at which the snapshot was emitted)
- `seq : uint32` (per-vessel monotonically increasing, wraps at 2^32)
- `position : double3`
- `velocity : double3`
- `orientation : quaternion (4 × double)`
- `angularVelocity : double3`
- `flags : uint32` (promotion/demotion/docked bits, see §5)
- `bubbleId : uint32` (the bubble that owns this vessel, or 0 for on-rails)

≈ 88 bytes per vessel. At 4 active vessels × 25 Hz this is **≈ 9 KB/s** outbound — negligible at our scale. The benefit is mechanical: the predicted own-vessel path round-trips losslessly, reconciliation drift is zero, and there is no two-quantization-regime design to maintain. Revisit only if/when the active-vessel count grows by an order of magnitude.

### 2. Snapshot scheme: full snapshots in M1; delta-against-acked-baseline deferred to phase 2

M1 ships **full snapshots only**. Each snapshot packet additionally carries a **`mostRecentAckedSeq : uint32`** field — the highest per-vessel seq the client has *acknowledged* receiving. This field is unused in M1's server emitter but is part of the wire format from day one so that the delta phase can land **without a protocol break**.

When bandwidth becomes a measured constraint (call it "phase 2"), the same packet layout will additionally carry per-vessel entries where each entry is either a full snapshot or a delta encoded relative to `seq − mostRecentAckedSeq` (5-bit baseline offset ≈ 0.5 s of history at 60 Hz, per [netcode reference](../../references/netcode.md) §4) plus a per-vessel priority accumulator. The seq/ack pair is forward-compatible: a phase-2 server can emit a mixed packet (some full, some delta), and a phase-1 client receiving such a packet falls back to "use the full ones, ignore the delta entries." This invariant is what makes the upgrade non-breaking.

### 3. Channels: unreliable-sequenced for snapshots, reliable-ordered for events

Two logical channels on the LiteNetLib transport:

- **UnreliableSequenced** carries `SnapshotBundle` (the per-tick packet of full vessel snapshots). Loss is masked by client-side interpolation buffer (≈ 100 ms at 25 Hz, sized to "lose two packets in a row" per netcode reference §3). Out-of-order delivery is masked by the per-vessel `seq`. The latest packet is always usable; the channel never blocks waiting for retransmit.
- **ReliableOrdered** carries every **discrete event** (see §5). These are atomic and may not be lost, reordered, or duplicated: a structural failure must apply exactly once; a bubble merge must apply before any subsequent snapshot that references the post-merge layout.

Input packets (pilot throttle/attitude, NET-1/NET-2) ride the **same unreliable channel** as state, with sequence numbers for the prediction loop; a redundant recent-input window rides alongside for loss tolerance (netcode reference §5).

### 4. Quantization and deterministic replay

Both ends quantize identically before each step. Because M1 ships full doubles on the wire, this is the identity function — but the rule stands for the day we add coarse quantization for high-payload-vessel scenarios: round on the wire, dequantize on both ends *before* the next sim step. Per netcode reference §2, skipping this is the canonical way to make a predicted body drift one float-ulp per tick and require continuous reconciliation correction.

### 5. Discrete event surface (reliable-ordered channel)

All atomic state changes that cannot be interpolated ride the reliable-ordered channel as tagged records. M1 ships the full set, not a subset, so the event protocol is stable from day one:

| Event tag | Trigger | Payload |
|---|---|---|
| `Staged` | Decoupler fired, stage separated | vesselId, newChildVesselId, separationImpulse |
| `StructuralFailure` | Joint exceeded load threshold (PHYS-6) | vesselId, jointId, breakingForce |
| `BubbleMerged` | Two bubbles joined (P-4 §4) | keptBubbleId, absorbedBubbleId, movedVesselIds[], per-vessel localDelta |
| `BubbleSplit` | One bubble partitioned (P-4 §5) | originalBubbleId, newBubbleIds[], movedVesselIds[], per-vessel localDelta |
| `Docked` | Two vessels latched in one bubble | vesselIdA, vesselIdB, resultingVesselId |
| `Undocked` | Composite vessel split | originalVesselId, vesselIdA, vesselIdB |
| `Promoted` | Vessel on-rails → active | vesselId, bubbleId, initialState |
| `Demoted` | Vessel active → on-rails | vesselId, finalOrbit |
| `Suspended` | Active vessel → suspended snapshot | vesselId, snapshotRef |
| `Resumed` | Suspended vessel → active (and possibly bubble birth) | vesselId, bubbleId, snapshotRef |
| `SoiChanged` | Vessel crossed SOI | vesselId, oldParentId, newParentId, newOrbit |

`BubbleMerged` and `BubbleSplit` carry per-vessel `localDelta` so clients rebase trails and predicted transforms; this couples directly to the rebasing protocol defined in [ADR-0012](0012-floating-origin-rebasing-strategy.md). `Promoted`/`Resumed` carry a `snapshotRef` into the persisted snapshot table; the client may fetch lazily if it needs the full body tree (not in M1 — M1 uses a single rigid body per vessel).

### 6. Floating-origin coupling

Snapshots and events both carry **global doubles** (or recomputed global = `bubbleOrigin + local` server-side, but the wire shape is the double), never raw float-locals. The server's intra-bubble rebases (ADR-0012 §1) and merge/split rebases (ADR-0012 §4, §5) are invisible to clients because the global view never changes; only the per-vessel `localDelta` on a `BubbleMerged`/`BubbleSplit` event rebases client-side caches. This is the netcode reference §3 gotcha ("origin rebasing must not inject a one-frame jump into interpolated positions") handled at the protocol layer.

## What this is not

- **Not** lag compensation. NET-1 + PHYS-5 mean two vessels that dock are already in the same server-side scene (see ADR-0012 closing remark); no rewind is needed.
- **Not** a scaled-space rendering trick. Rendering rides the same float-local frame as physics, anchored to the camera near the controlled vessel.
- **Not** a lockstep / deterministic-networking model. ADR-0001 keeps the server authoritative; reconciliation absorbs the residual FP drift between PhysX runs.

## Phase-2 hook (delta compression)

The reserved upgrade path: when active-vessel count × snapshot rate exceeds the bandwidth budget (call it "phase 2"), introduce per-packet entries where each entry may be flagged as a delta against `(seq − mostRecentAckedSeq)`. The priority accumulator, 5-bit baseline offset, and both-sides quantization rules from netcode reference §4 are already specified and need no new design work — only implementation, gated on a measurement. The phase-1 wire shape is a strict subset of the phase-2 wire shape; a phase-2 server emitting a mixed packet is correctly consumed by a phase-1 client (which ignores the delta entries and uses the full ones).

## Addendum (M1 wiring): input-ack field and implementation status

Two clarifications surfaced wiring the M1 predict/reconcile loop (see [ADR-0014](0014-fixed-tick-composition-and-physics-step-seam.md), [ADR-0016](0016-server-authoritative-station-occupancy.md)):

### 7. Per-vessel `lastProcessedClientTick` — a *second*, distinct ack

§2's `mostRecentAckedSeq` is a **client → server** ack: the highest *snapshot seq* the client has received, reserved for the phase-2 delta baseline. The reconciler (`ClientReconciler` / `ClientPredictor.Reconcile`) needs the **opposite-direction** ack: per controlled vessel, the highest *client input tick* the server has already applied, so the client can reset to authoritative state and replay only the still-unacked inputs. These are different fields in different directions; do not conflate them.

The M1 `VesselSnapshot` therefore carries **`lastProcessedClientTick : int64`** in addition to the §1 fields. The server stores it on `Vessel` (set in `InputChannel.Submit` to the applied `input.ClientTick`) and the emitter stamps it per vessel. For vessels with no pilot input (on-rails, other players' craft) it is 0/unused.

### 8. M1 implementation status vs the §1 record

M0 shipped a reduced `VesselSnapshot` (vesselId, gameTime, seq, position, velocity). M1 brings the wire up to the §1 shape needed by the loop: it adds **`angularVelocity`** (so reconciliation resets the full `PredictedVesselState` — position, velocity, angular velocity, ack — rather than zeroing angular state every ~40 ms) and `lastProcessedClientTick` (§7). `orientation`, `flags`, and `bubbleId` from §1 land as the discrete-event surface (§5) and full-3D client need them; M1 may ship them incrementally.

Note a deliberate asymmetry: `PredictedVesselState` carries **no orientation quaternion** — only angular velocity. So the controlled vessel's *attitude* is integrated client-side from angular velocity (not reconciled against an authoritative quaternion), while non-controlled vessels interpolate the wire `orientation` directly. Absolute-attitude reconciliation is a known deferred seam, acceptable while attitude divergence stays small over the reconciliation window.

## Satisfies

NET-5 (snapshots at 20–30 Hz) by fixing the 25 Hz shape. NET-6 (≤ 150 ms RTT smoothness) by fixing the unreliable-sequenced channel + interpolation buffer sizing. PHYS-6 (discrete structural failure) by giving it a dedicated reliable-ordered event tag rather than smuggling it through a snapshot delta. PHYS-5 (docking without authority handoff) by making `BubbleMerged` and `Docked` reliable-ordered events that clients always observe before any post-merge snapshot.

Closes P-1.