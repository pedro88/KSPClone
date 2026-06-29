# Fixed-tick composition: the bubble-physics step seam and per-tick pass order

How M1's subsystems compose into the one authoritative fixed tick (Art. 2, ADR-0006), given that most of them are engine-agnostic SimCore but the actual `PhysicsScene.Simulate(dt)` is UnityEngine-coupled and must run *in the middle* of the tick.

## Context

`SimWorld.Tick` advances the master clock, syncs on-rails vessel caches, then fires `TickRecorded`. `ServerSimulation.OnTick` is the single ordered place per-tick work runs (Art. 1: one writer). M1 adds promotion, clustering, rigid-body integration, demotion, and suspension. Promotion/clustering/demotion/suspension are engine-free (SimCore). But the integration step — apply gravity+thrust, `Simulate(dt)`, read transforms back into authoritative doubles, rebase — lives in `BubbleIntegrator` in the Server assembly because it touches PhysX (ADR-0009 forbids `UnityEngine` in SimCore).

It must execute *between* clustering (which assigns bubble membership) and demotion (which fits an orbit from post-physics state). The host loop runs the fixed step via an accumulator, so several fixed ticks may elapse per `Update`; SimCore passes and the physics step must therefore alternate one-for-one per tick, not run in two separate batches.

## Decisions

### 1. The physics step is injected as a SimCore interface, not an event

`ServerSimulation` depends on a one-method SimCore interface:

```csharp
public interface IBubbleStepper { void Step(double dtSeconds); }
```

`OnTick` calls `_stepper.Step(dt)` at the fixed point in the sequence. The default is a no-op (`NullBubbleStepper`) so headless SimCore tests run the full tick without PhysX. `ServerBootstrap` injects an adapter wrapping `BubbleIntegrator`.

Rejected: a `PrePhysics`/`PostPhysics` event pair (matches the existing output-event style, but an in-the-middle ordering enforced by event-handler registration order is fragile). Rejected: moving the accumulator into `ServerBootstrap` and driving the order from the host (duplicates the loop, risks divergence from the headless/test path — Art. 2 wants one loop).

The promotion/clustering/bubble-create *effects* that touch Unity (instantiate a `RigidVesselBody`, allocate a `PhysicsScene`) are already delivered by synchronous events (`VesselPromoted`, `BubbleFormed`, `BubbleCreated`) the host subscribes to — so they need no seam. Only the `Simulate` call did.

### 2. Canonical per-tick order

`OnTick(dt)`:

1. `soiTransition.ApplyDue()` — on-rails SOI re-parent (M0); keeps the parent body correct for the sample in step 2.
2. `promotion.RunPass(clock)` — on-rails → active-physics; samples closed-form Kepler state at the master clock.
3. `bubbles.RunClusteringPass(vessels)` — assign/merge/split bubbles by proximity.
4. `stepper.Step(dt)` — **injected** Unity physics: gravity + thrust, `Simulate(dt)`, read-back to authoritative doubles, floating-origin rebase (ADR-0012).
5. `demotion.RunPass()` — unattended **and** warp-safe → on-rails (fits orbit from post-physics doubles).
6. `suspension.RunSuspensionPass(occupancy)` — unattended **and** not warp-safe → suspended.
7. `autoLimit.Tick()` — warp auto-limit (M0).
8. `snapshots.Tick(dt)` — emit (sees post-physics, post-transition state).

Demotion precedes suspension so the two passes partition unattended active vessels with no overlap (warp-safe ones are gone before suspension scans). Snapshot emission is last so a vessel that left active-physics this tick replicates its new state-kind in the same bundle — no one-frame active/transition flicker at the boundary.

## What this is not

- **Not** a second clock writer. `IBubbleStepper.Step` mutates rigid-body transforms and writes back to `Vessel.Cached*` doubles; it never touches `MasterClock` (Art. 1, the `GameTimeSeconds` single-writer rule).
- **Not** a place for game logic. The stepper is pure integration; promotion/demotion/suspension decisions stay in their SimCore controllers.

## Satisfies

Art. 2 / ADR-0006 (one fixed-step loop, headless and in-client, render-independent) by keeping the order identical whether the stepper is the PhysX adapter or the no-op. PHYS-2/3/4 by giving promotion, integration, and demotion a defined, testable position in the tick.
