# Floating-origin rebasing strategy (merge, split, intra-bubble drift)

Resolves open item **P-4** (plan §"Decisions still open in plan"). Sibling to [ADR-0003](0003-multiple-concurrent-physics-bubbles.md) — that ADR commits to K concurrent bubbles with per-bubble floating origins; this one decides *how* the origin moves and what happens at the seams.

## The model (already decided elsewhere, restated for context)

Every bubble owns a `GlobalOrigin : double3` — the bubble's true position in the solar-system frame, in the authoritative sim core. Inside a bubble, Unity/PhysX simulates vessels in float32 *relative to that origin* via `local_float = (double_global − GlobalOrigin)`. Authoritative state (orbital elements, on-rails vessels, suspended vessels) never lives in Unity float32 — only the active bubble's transient local transforms do. This is the double-global / float-local split ([floating-origin reference](../../references/floating-origin.md) §5).

## Decisions

### 1. Intra-bubble rebase threshold: 1024 m (power of two)

When the centroid of a bubble's active vessels drifts more than **1024 m** from `GlobalOrigin`, translate every body in that bubble's physics scene by `-delta` and add `+delta` to `GlobalOrigin`. Velocities are **untouched** — a rebase is a translation, not a boost. World-space particles pause, trails reset, presentation consumers receive a `FloatingOriginShifted(bubbleId, delta)` event. Power-of-two threshold because float precision halves with every power of two step away from origin (the kraken math). 1024 m keeps every vessel well inside the float32 high-precision envelope with margin. Rebases will be rare under normal play; this is the *correctness* floor, not a perf knob. (Closes the "how often" question; ADR-0003 only committed to the existence of the origin.)

### 2. Bubble lifecycle: empty → destroy, not idle

A bubble exists **only** while it contains at least one vessel in active physics. When the last vessel demotes to on-rails (warp-safe + unattended, [ADR-0002](0002-living-universe-per-vessel-suspension.md) suspension path) or is suspended, the bubble and its `PhysicsScene` are destroyed. Empty bubbles do not tick. On-rails vessels (including无人 stations and satellites visible on the orbital map) are represented by closed-form Kepler propagation in the sim core — they need no bubble to exist, no `Simulate(dt)` call to be observed.

This matches Art. 4 ("universe lives when empty, nothing is faked") without spinning zombie physics worlds: the universe lives because on-rails lives, not because empty bubbles tick. Recreation on promotion is cheap (one `PhysicsScene` + a centroid origin from doubles).

### 3. Bubble origin at creation: cluster centroid in doubles

When a bubble is born (promotion of an on-rails vessel, or merge where neither side exists), its `GlobalOrigin` is set to the **centroid of its active vessels' global positions**, computed in doubles. This guarantees every member's initial float-local is small and the first tick is already inside the high-precision envelope. No anchor-vessel rule, no parent-body origin — those produce large initial float-locals (a vessel in interplanetary orbit would otherwise start at millions of km from origin and rebase on tick 0 anyway).

### 4. Merge trigger: 50 km inter-bubble distance

Two bubbles merge when the **closest pair of active vessels, one from each**, comes within **50 km** of each other (in authoritative global doubles). On merge, the *larger* bubble's `GlobalOrigin` survives; incoming vessels are re-based via `new_local = (vesselGlobalDouble − O_keep)` narrowed to float, then moved into the kept physics scene with their existing velocities. The kept scene's empty counterpart is destroyed. Each moved vessel emits a `FloatingOriginShifted` event with its delta so presentation/replication consumers (trails, client prediction) rebase rather than streak.

50 km is a wide envelope that comfortably covers orbital rendezvous convergence (a typical intercept burns and drifts over tens of km in the final phase) and matches PHYS-2's "promotion on approach" semantics. It is far enough that two parallel missions passing each other at 100+ km never merge accidentally. Smaller thresholds (5 km, docking-only) require explicit rendez-vous intent and are rejected — they push game logic into the merge decision, which belongs in the simulation layer.

### 5. Split trigger: cluster separation > 50 km

A bubble splits when its active vessels separate into connected components (by the same 50 km threshold) of size ≥ 1. The largest component keeps the existing scene and `GlobalOrigin`; each other component gets a fresh `PhysicsScene` whose `GlobalOrigin` is set to **that component's centroid recomputed from authoritative doubles** (never from the old float-locals — accumulated float drift must not propagate across a split). Re-based vessels get the same `FloatingOriginShifted` event as on merge. Velocities are unchanged.

### 6. Invariant: no float-vs-float subtraction at the seam

Subtracting two large float32s destroys precision (catastrophic cancellation). Every conversion between global double and local float happens in the wider type first: `local = (double_global − GlobalOrigin)` then `(float)local`. Never `(float)globalA − (float)globalB`. Origin choice on merge (larger bubble survives) and on split (recompute from doubles) both flow from this invariant.

## What this is not

- **Not** lag compensation. ADR-0001 + PHYS-5 mean two vessels that dock are already in the same server-side scene; no rewind is needed.
- **Not** a scaled-space rendering scheme. Camera rides the controlled vessel in the same float-local frame — no separate render-scale scene at our single-system scope.
- **Not** a precision-loss path. Centroids and origins are always recomputed from authoritative doubles, so float drift never propagates across merge or split.

## Satisfies

PHYS-1 (multiple bubbles, per-bubble floating origin) by giving the rebasing rule that ADR-0003 left implicit. PHYS-3 (demotion) by tying the bubble's life to its last active vessel. PHYS-5 (docking without authority handoff) by guaranteeing both vessels share one server-side scene before contact.

Closes P-4.