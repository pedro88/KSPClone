# Replicate vessel orientation; hand-fly with kinematic rate control

Hand-flying the demo craft was miserable: you steered but the craft didn't visibly turn, and it wouldn't hold a heading. Two root causes, one slice.

## Context

**1. Orientation was never replicated.** The snapshot carried position, velocity, and angular velocity — but not orientation (`VesselSnapshot`, `SnapshotEmitter`). With no authoritative attitude on the wire, the client rendered the controlled capsule *aligned to its velocity vector* (`ClientWorldRenderer.OrientControlled`). So while thrust on the server followed the rigid body's true facing, the player saw the capsule pointing along its velocity — you couldn't see where you were aiming, and on a vertical launch the capsule flipped flat the instant any sideways velocity appeared. Flying blind.

**2. Attitude was an un-damped torque.** `BubbleIntegrator.ApplyAttitude` turned the pilot's (pitch, yaw, roll) command into a torque (`AddTorque`, scaled by inertia). Torque integrates to angular *velocity*, so a tap kept the craft rotating until you applied an equal counter-tap — no natural "release to stop." Combined with (1) you couldn't even see the spin you were fighting.

The M1 note "rate damping is the representable, honest M2 SAS" (`PilotSasAutomation`) was a workaround for the same missing piece: with no orientation to hold, the empty-Pilot fallback could only damp the *rate* via an opposing torque.

## Decisions

### 1. Orientation is authoritative state, replicated in the snapshot

Add `Quaterniond` (a double, engine-agnostic unit quaternion — SimCore carries `noEngineReferences`, ADR-0009) and:

- `Vessel.CachedOrientation` — written back by the integrator each tick from the rigid body's rotation, alongside the existing angular velocity.
- `VesselSnapshot.Orientation` and the wire codec (4 doubles) — **amends ADR-0013**'s snapshot layout. Identity for on-rails vessels.
- `VesselInterpolator` slerps orientation between snapshots for non-controlled vessels; the controlled vessel predicts it (below) and reconciles against the snapshot.

Orientation is frame-independent under floating-origin rebases (they are pure translations), so no rebasing math touches it — same argument as angular velocity (ADR-0013 §8).

### 2. Hand-flying is kinematic rate control, not torque

The pilot's (pitch, yaw, roll) command is a **target body-frame angular rate**. The integrator drives the rigid body's angular velocity straight to it: hold a key → rotate at that rate; release (command 0) → rotation stops and the attitude holds. This is arcade control, chosen deliberately — the project's job here is a *flyable* co-op sim, not a reaction-wheel/RCS-authority fidelity model. A physical torque model (with per-station authority and saturation) is a later slice; when it lands it replaces this control law without touching the replication in Decision 1.

The client predictor integrates orientation by the same rule (`orientation * FromAngularVelocity(bodyRate, dt)`) and rotates the thrust vector by the predicted orientation, so prediction matches the server and reconciliation barely corrects (NET-2/3).

### 3. Empty-Pilot SAS commands a zero rate

With rate control, "hold attitude" is simply *command zero rate* — the integrator then zeroes the angular velocity and the craft stops rotating. `PilotSasAutomation` now emits `Vector3d.Zero` instead of an opposing-torque term (which, under rate control, would diverge). Now that orientation is replicated, a future slice can upgrade this to hold a *captured* orientation rather than merely zero the rate.

### 4. Render the true attitude; analog throttle

The client renders the controlled capsule at its predicted orientation (its long +Y axis is the thrust axis, so the nose points where you steer), and the exhaust plume points down the vessel's −Y. Throttle becomes analog (Shift/Ctrl ramp, X cut, Z full) instead of hold-for-full, so partial burns are possible.

## What this is not

- **Not** a physical attitude-dynamics model. Angular velocity is set kinematically; there is no torque, inertia response, or actuator saturation yet. That is a later slice and will supersede Decision 2.
- **Not** a wire-compat break handled gracefully — the snapshot layout changed (ADR-0013 amended); client and server must be built together. At 4 players with paired builds this is fine.
- **Not** orientation persistence. Suspended/on-rails vessels don't store orientation; a resumed craft starts identity. Out of scope until it matters.

## Satisfies

PHYS-4 (rigid vessel, single body) — refines how its attitude is commanded and replicated. NET-2/3/4 (predict/reconcile/interpolate) — orientation now flows through all three paths. CREW-4 (empty-station automation) — SAS hold restated for the rate-control model. Amends ADR-0013 (snapshot/wire layout) and the attitude handling introduced in ADR-0014.
