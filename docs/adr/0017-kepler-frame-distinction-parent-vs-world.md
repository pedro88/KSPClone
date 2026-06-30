# Kepler state queries must distinguish parent-frame from world-frame

A footgun in the closed-form Kepler API that masqueraded as a working invariant under the M0-T06 static tree, then silently broke SOI detection the moment Earth was given an orbit around the Sun (M2-T12). The fix is one method call; the rule is the ADR.

## Context

`KeplerPropagator` exposes two close cousins:

- `StateAt(orbit, t, registry)` returns `(parentFramePos, parentFrameVel)` — the orbit evaluated **relative to its parent body**, as a closed-form solution of the two-body Kepler problem.
- `WorldFrameStateAt(orbit, t, registry)` returns the same two vectors **plus** the world-frame equivalents: `worldPos = parentWorldPos + parentFramePos`, `worldVel = parentFrameVel`.

The doc comment on `StateAt` has always said "the caller is responsible for offsetting by the parent's world position if a world-frame vector is required" (KeplerPropagator.cs:27-28). The doc on `WorldFrameStateAt` says "world velocity = relative velocity (the parent body's own velocity is dropped for M0 because the static tree in T06 puts every body at the origin)" (KeplerPropagator.cs:38-44).

The latter was the load-bearing assumption. M0-T06 committed a *static* celestial tree: every `CelestialBody` had a fixed parent but no `OrbitAroundParent`. `BodyRegistry.WorldPositionOf` for any body returned the parent's world position unchanged, so `parentFramePos == worldPos` for any vessel. `StateAt` and the world-frame elements of `WorldFrameStateAt` were numerically interchangeable — and the codebase grew callers that quietly relied on that.

M2-T12 broke the static tree: Earth now carries an `OrbitAroundParent` around the Sun, so `Earth.WorldPositionOf(t) ≠ (0,0,0)` for `t > 0`. At `t = 0` the new seed places Earth at `(+EarthOrbitRadius, 0, 0)` — already 1.496e11 m away from the world origin. The first thing that fell over was `SoiScanner.DistanceMinusSoi`, which used `StateAt` to read the vessel position and `WorldPositionOf` to read the target position — and compared the two as if they were in the same frame. The crossing signal disappeared (vessel "distance to Moon" was off by 1 AU), POIs vanished, the warp auto-limit had nothing to stop on, the seed-vessel transfer test broke.

This is a class of bug, not a one-off. Any future code that asks "is vessel X near body Y?" in world coordinates must read the world frame, not the parent frame, or it will silently misbehave the first time a parent body moves.

## Decisions

### 1. The rule: world-frame comparisons must read world-frame state

When a caller needs a vector in the **world** inertial frame (for distance checks, ray casts, POI scans, anything that compares two positions without going through a shared parent's frame), it must use `KeplerPropagator.WorldFrameStateAt(...)` and take the world-frame element — *not* `StateAt(...)` and add a parent offset by hand.

The "add parent offset by hand" path is a footgun: it's easy to forget, the compiler can't catch it, and there's no test fixture that fails when you do. The Kepler API has to enforce the discipline by making the right call obvious and the wrong one impossible to confuse at the type level — at minimum by giving both methods and documenting which frame each one returns.

### 2. `StateAt` stays, but the doc is updated to a louder warning

`StateAt` remains as the parent-frame accessor. Its caller base is small (state-vector → orbit fitter, where parent-frame is exactly the right input — see `DemotionController.TryDemote` and `StateVectorToOrbit.Convert`). The doc gets a red-flag banner:

> Returns (parentFramePos, parentFrameVel). **Do not use for world-frame queries.** For world-frame state, use `WorldFrameStateAt`.

This catches reviewers who reach for `StateAt` out of habit when they're about to compare against `BodyRegistry.WorldPositionOf(...)`.

### 3. `WorldFrameStateAt`'s world-velocity caveat stays visible

The current doc on `WorldFrameStateAt` (KeplerPropagator.cs:38-44) already warns that the world velocity drops the parent's orbital velocity, on the grounds that M0 had no orbiting parents. With M2-T12, Earth now orbits the Sun at ~30 km/s — a parent-frame velocity of that magnitude is no longer negligible for, e.g., a docking approach. The warning is now load-bearing for anyone writing physics comparisons in world frame. Keep it loud; do not edit it down to "M0 simplification" once it's no longer true.

A future slice that needs parent velocity in world-frame comparisons will introduce a sibling `WorldFrameStateWithParentVel` (or extend `WorldFrameStateAt`) and migrate callers. That's a separate ADR when it's needed; for M2-T12 the simple world-pos / parent-vel split is fine because the only callers are SOI scans (distance only, no velocity term) and presentation (visual skybox, no simulation dependence).

### 4. Add a regression test

A new EditMode test in `KeplerPropagatorTests` (or `WorldSeedTests`) asserts the asymmetry directly: for the seeded vessel, `StateAt` and the world-frame element of `WorldFrameStateAt` differ by exactly `|Earth.WorldPositionOf(t)|` for any `t > 0`. If a future change makes them equal again, the test fails loudly — which is exactly what happened in reverse on M2-T12.

## What this is not

- **Not** a change to the simulation math. Kepler propagation is unchanged; only the API contract around which frame a given call returns is being made explicit.
- **Not** a fix to `BodyRegistry.WorldPositionOf`. That method is correct — it returns the world position; the bug was a caller reading the wrong frame.
- **Not** a permanent solution to the parent-velocity omission in `WorldFrameStateAt`. That will need its own ticket once any world-frame velocity comparison is wired up.

## Satisfies

ORBIT-1 (patched conics, single-body gravity at a time) by guaranteeing the world/parent-frame distinction is enforced wherever patched-conic math meets world-frame queries. TIME-4 (warp auto-limit halts on POI) by guaranteeing POI scans actually see crossings once a parent body is no longer static — the original failure mode that triggered this ADR.

Closes nothing on its own — the fix is one line in `SoiScanner.DistanceMinusSoi` (already shipped on `feat/sun-celestial-body`). The ADR exists so the next person doesn't reintroduce the same shape of bug, and so the doc comments stay loud enough to catch reviewers.