# Large-World Float Precision & Floating Origin — Reference

Curated reference material on float precision at solar-system scales and floating-origin / origin-rebasing techniques, mapped onto our architecture.

**Why this matters for us:** ADR-0003 commits us to *multiple concurrent physics bubbles, each with its own floating origin*, to keep float precision sane at solar-system distances (the "kraken"). PHYS-1 requires the system to simulate active vessels in one or more concurrent physics bubbles, each with its own floating origin. **P-4** (open, *decide before Slice 1.1*) asks for the floating-origin rebasing strategy when bubbles merge/split — this doc ends with a concrete proposal for it.

> Vocabulary used below is ours (CONTEXT.md): **physics bubble** (a cluster of vessels simulated together with one shared floating origin), **floating origin** (a bubble's local coordinate origin, kept near its vessels), **the kraken** (the family of bugs caused by float-precision loss far from the origin). External terms (Krakensbane, origin shifting, scaled space) are introduced and then translated into ours.

---

## 1. The float precision problem at large coordinates (the kraken)

**Concept in our terms.** Unity stores every `Transform.position` as a `Vector3` of 32-bit floats. A float32 has ~7 significant decimal digits, so its *absolute* precision degrades as coordinate magnitude grows — precision halves with every power-of-two step away from origin. If we placed vessels at their true world coordinates (millions to billions of metres from the system barycentre), the low-order bits — the ones that encode "where this part is *this physics tick* vs. *last tick*" — get rounded away. The rigid-body integrator then sees parts jittering, snapping, or gaining spurious energy: the kraken. KSP's original sin was treating the centre of Kerbin as the fixed coordinate origin, so error scaled with distance from home.

**Concrete precision-vs-distance numbers** (worst-case spacing between adjacent representable values):

| Distance from origin | float32 precision | float64 precision |
|---|---|---|
| ~1 km (room/launchpad scale) | sub-millimetre | proton-scale |
| ~10^6 m (1,000 km) | **~1 metre** | nanometre |
| Earth circumference (~4×10^7 m) | ~2.4 m | nanometre |
| 1 AU / Sun distance (~1.5×10^11 m) | **~10 km** | hair-width (~10^-5 m) |

Rules of thumb worth memorising:
- **float32** holds ~1 mm precision only out to ~**10 km** from origin. Beyond a few km, physics is already visibly degrading.
- **float64** holds ~1 mm precision out to ~**10^10 km** — comfortably the whole solar system.
- Precision is *relative to the origin*, not absolute. This is the entire reason a *floating* origin works: keep the magnitude small and float32 is fine.

**Why we don't just switch Unity to float64.** Unity's `Transform`, `Rigidbody`, physics (PhysX), and the GPU render path are all single-precision and not configurable. You cannot make Unity simulate or render in doubles. The universal workaround is to keep *authoritative* world state in doubles (our sim core, which is engine-agnostic C# — see plan.md component map) and feed Unity only small, origin-relative float32 values for physics and rendering.

Sources: [ilikebigbits — Float or double?](https://www.ilikebigbits.com/2017_06_01_float_or_double.html), [coherence — World Origin Shifting](https://docs.coherence.io/manual/advanced-topics/big-worlds/world-origin-shifting), [Aapeli Vuorinen — Floats for coordinates](https://www.aapelivuorinen.com/blog/2023/06/30/floats-for-coordinates/).

---

## 2. Floating origin / origin shifting (recenter past a threshold)

**Concept in our terms.** A floating origin is a moving coordinate frame whose origin is kept near the action. When the active region drifts too far from (0,0,0), you *rebase*: subtract a delta from every object's position so the active region snaps back near origin, and remember the accumulated delta (in doubles) as the bubble's true global position. For us, each **physics bubble** owns one floating origin; Unity simulates the bubble's vessels in this small local frame, while the sim core tracks the bubble's global offset in float64.

**How the shift is performed (the standard recipe):**
1. Each tick (or each render frame), measure the focus object's distance from the local origin.
2. If `distance > threshold`, compute a delta and translate **every object in the frame** by `-delta`: vessels, terrain/celestial proxies, world-space particle systems, trail renderers, cameras, lights.
3. Add `+delta` to the frame's accumulated global offset (the double-precision "true" origin position).

**Thresholds seen in the wild** (pick deliberately, we tune later):
- coherence's sample shifts when `position.magnitude > 100 m`.
- Marcos Pereira's Unity implementation uses a small power-of-two threshold (e.g. 4) and advises *"pick a power of two, as float precision decreases with every successive power of two."*
- KSP's Krakensbane historically rebased at **2 km** from origin; modern KSP recenters effectively every frame.
- **Our guidance:** a per-bubble threshold on the order of **1–2 km** keeps every vessel inside the float32 "~mm precision" envelope with margin, while rebasing rarely enough that the mass-translation cost is negligible. Use a power-of-two value (e.g. 1024 m or 2048 m).

**What must move and what's fragile** (the part most naive implementations get wrong):
- **Rigidbodies:** translate `Rigidbody.position`, not just `Transform.position`. **Velocities and angular velocities are frame-invariant under a pure translation and must be left untouched** — a rebase is a translation, not a boost, so linear/angular momentum carries over unchanged. (If you ever rebase into a *rotating* frame, à la KSP's co-rotating altitude trick, velocities *do* need transforming — we don't plan to.)
- **World-space particle systems:** pause, translate, resume — otherwise existing particles smear across the jump.
- **Trail/line renderers, decals, anything caching world positions:** must be offset or cleared, or they streak.
- **Non-networked / non-tracked objects:** there is no automatic hook; whatever system owns them must subscribe to the shift event and apply the delta.

Sources: [Marcos Pereira — Floating origin in Unity](https://marcospereira.me/2021/05/18/floating-origin-in-unity/), [brihernandez — Floating origin Gist](https://gist.github.com/brihernandez/9ebbaf35070181fa1ee56f9e702cc7a5), [coherence — World Origin Shifting](https://docs.coherence.io/manual/advanced-topics/big-worlds/world-origin-shifting), [Manuel Rauber — Floating Origin in Unity](https://manuel-rauber.com/2022/04/06/floating-origin-in-unity/), [Salty Hash — Infinite play space](https://saltyhash.org/2019/12/26/making-an-infinitely-large-play-space-in-unity3d-floating-origin/).

---

## 3. How KSP handles it (floating origin + Krakensbane + scaled space)

**The kraken.** A loosely-related family of KSP physics bugs that "gained its name because of its seemingly random occurrences when journeying through space," all rooted in float rounding that accumulates as a vessel's coordinate magnitude grows. With Kerbin's centre as origin, a distant ship's position components were large, so the dropped low bit represented metres instead of millimetres — parts wobbled apart, vessels tore themselves up, especially at high velocity.

**Krakensbane = KSP's floating origin.** The fix was to stop pinning the origin to Kerbin and instead **keep the origin on the active vessel**. Original behaviour: *when the craft moves more than ~2 km from origin, shift the entire universe so the craft's coordinates stay small.* Modern KSP refinements:
- The floating origin moves effectively **every frame** rather than only at a 2 km threshold.
- At low altitude the world is simulated in a **co-rotating reference frame** (the universe rotates around the craft) to handle the planet's surface velocity.
- **Double-precision** is used for the authoritative/orbital layer (e.g. maneuver-node math) to prevent interplanetary trajectory drift — floats are confined to the local physics frame.

**Scaled space (rendering trick).** KSP renders distant bodies and orbit lines in a separate 1/10-scale duplicate "scaled space" subscene so far-away geometry doesn't need full-precision world coordinates. Adequate for KSP's compact system; it strains under mods that add distant systems and is being rethought for interstellar scope.

**What we inherit / diverge on:**
- We inherit floating-origin-per-active-region and double-precision authoritative state.
- We *generalise* Krakensbane's single origin to **K simultaneous origins** (one per bubble) — see §4.
- Our orbital layer is patched conics with closed-form Kepler in doubles (ADR-0004), which already lives outside the float32 physics frame, so we get the "doubles for orbits" property by construction.

Sources: [HN — KSP's approach to precision](https://news.ycombinator.com/item?id=26938812), [KSP Wiki (Fandom) — Kraken / Version History](https://kerbalspaceprogram.fandom.com/wiki/Version_History), [KSP rounding & floating point writeup](http://ffden-2.phys.uaf.edu/webproj/211_fall_2014/Bryce_Melegari/ksp_rounding.html).

---

## 4. Multiple simultaneous origins — K bubbles far apart at once

This is our defining departure from KSP (single origin) and from typical single-player floating-origin tutorials. ADR-0003 runs **K independent physics worlds concurrently** (K small at 4 players) so crews can run parallel missions far apart. Each needs its own valid float32 frame *at the same time*.

**The model that fits us (and is independently validated):**

- **Global truth in doubles.** Every bubble has a `GlobalOrigin` stored as a double-precision vector (its true position in the solar-system frame). Every vessel's authoritative orbital/physics state is kept in doubles in the sim core.
- **Local simulation in floats.** Within a bubble, vessels are simulated by Unity/PhysX in float32 *relative to that bubble's GlobalOrigin*. Because every vessel in a bubble is within physics range of every other (they're a cluster), their local coordinates are always small — float32 stays in its high-precision envelope.
- **Per-frame conversion at the seam.** `local_float = (double_global_position − bubble.GlobalOrigin)` going into Unity; `double_global_position = bubble.GlobalOrigin + local_float` coming back out into authoritative state. The subtraction happens in doubles and *produces* a small float — this is the exact trick used for camera-relative rendering and in coherence's server-absolute / client-relative split.

**This is precisely the coherence architecture, reused for bubbles instead of clients:** *"all positions on the Replication Server are stored in absolute coordinates, while each Client has its own floating origin position, to which all of their game object positions are relative."* Swap "Client" → "physics bubble" and "Replication Server" → "our authoritative sim core" and it's our design.

**Unity-engine reality of running K worlds:** Unity has one default 3D `PhysicsScene`. To get K *independent* float32 frames that don't collide with each other, run each bubble in its own physics scene (`Physics.defaultPhysicsScene` plus additional scenes created with `PhysicsScene`/multi-scene physics, simulated independently via `PhysicsScene.Simulate`). Each scene has its own origin at (0,0,0); the bubble's `GlobalOrigin` lives in the sim core, not in the scene. This keeps two bubbles 10^9 m apart both sitting near their *own* (0,0,0) simultaneously, with no shared float32 frame to overflow.

**Merge / split** is the hard part and is P-4 — see §7.

Sources: [coherence — World Origin Shifting](https://docs.coherence.io/manual/advanced-topics/big-worlds/world-origin-shifting), [Ogre forums — precision far from origin](https://forums.ogre3d.org/viewtopic.php?f=5&t=34154), [Brprb08 — orbital-control-simulator (double-precision physics in Unity)](https://github.com/Brprb08/orbital-control-simulator).

---

## 5. The double-global / float-local split (recommendation)

**Recommendation: doubles are authoritative; floats are a per-bubble scratchpad.**

- **Doubles (sim core, engine-agnostic C#):** orbital elements / Kepler state for on-rails vessels; each active vessel's global position & velocity; each bubble's `GlobalOrigin`. This is the canonical state that gets persisted (Postgres, ADR-0007) and replicated. It never touches Unity float32.
- **Floats (Unity, per bubble):** transient local transforms and PhysX rigid-body state for the current tick. Derived from doubles each tick, discarded/overwritten each tick. Never persisted as truth.
- **The seam is a pure subtract/add in doubles** (§4). Do the subtraction *before* narrowing to float; never subtract two large floats (catastrophic cancellation — you'd lose the very precision you're protecting).
- **Aggregate in the wider type.** When integrating velocity over many ticks or summing many contributions, accumulate in doubles; narrowing only at the Unity boundary. (General float-precision hygiene: "if you sum a lot of data, use a higher-precision accumulator.")
- **Rendering** also rides the float-local frame: the camera sits near the bubble's local origin, so camera-relative positions are already small — no separate scaled-space scheme needed at our (single-system, first-cut) scope. Revisit if/when we add distant systems.

This split is the consensus across KSP, coherence, Star Citizen, Unigine, and the DOTS large-world discussions: **keep one double-precision source of truth; build small float local transforms from it in a system every frame, because the GPU and PhysX are single-precision and that will not change.**

Sources: [ilikebigbits — Float or double?](https://www.ilikebigbits.com/2017_06_01_float_or_double.html), [Unity DOTS for floating origin thread](https://discussions.unity.com/threads/dots-for-floating-origin.807117/), [coherence — World Origin Shifting](https://docs.coherence.io/manual/advanced-topics/big-worlds/world-origin-shifting).

---

## 6. Unity-specific notes (transforms, large-world packages, DOTS/ECS)

- **Transform precision limit.** `Vector3` is float32. Unity itself recommends staying within roughly a few km of origin; you hit ~1 m precision at ~10^6 m. There is no built-in double-precision transform.
- **No first-party "large world" transform package.** Unity has no shipped double-precision world solution for GameObjects; floating origin remains the standard pattern. Third-party/asset solutions exist but all reduce to "shift everything past a threshold" plus a double-precision tracker.
- **Multi-scene physics is the lever for K bubbles.** Use separate physics scenes per bubble and drive them with explicit `PhysicsScene.Simulate(dt)` from our fixed 60 Hz step (ADR-0006) rather than relying on Unity's automatic simulation. This also matches our render-decoupled fixed-step decision.
- **DOTS/ECS angle (informational; we are not committed to ECS).**
  - ECS makes mass origin-shifting cheap: one job over all entities. A reported benchmark shifted **200,000 translations in ~0.03 ms/frame** — origin rebasing is effectively free at our entity counts, so rebase frequency/threshold is not a perf concern for us.
  - Two patterns surface: (a) store world position as a chunk/sector id + local float3 (`GlobalPoint`-style struct), or (b) store float64 component data and *build* float local transforms in a system each frame. Both still narrow to float for rendering/PhysX. Pattern (b) is conceptually identical to our double-global/float-local split.
  - As of the referenced discussion, DOTS had **no turnkey floating-origin support** — you wire the hooks yourself. Not a blocker for our hand-rolled bubble manager.

Sources: [Unity DOTS for floating origin thread](https://discussions.unity.com/threads/dots-for-floating-origin.807117/), [coherence — World Origin Shifting](https://docs.coherence.io/manual/advanced-topics/big-worlds/world-origin-shifting), [Marcos Pereira — Floating origin in Unity](https://marcospereira.me/2021/05/18/floating-origin-in-unity/).

---

## Gotchas (consolidated checklist)

- **Subtract in doubles, then narrow to float.** Subtracting two large float32s first destroys precision (catastrophic cancellation).
- **A rebase is a translation, not a boost.** Move positions; leave linear/angular velocities alone. (Only a *rotating*-frame rebase touches velocities — we avoid those.)
- **Move `Rigidbody.position`, not just `Transform.position`,** or PhysX and the transform desync.
- **Pause world-space particles across a shift;** reset trail/line renderers and any cached world positions, or they streak across the jump.
- **Every system owning world objects must subscribe to the shift event** — there is no global automatic hook for non-tracked objects.
- **Pick a power-of-two threshold;** float precision steps at powers of two.
- **K bubbles = K independent float frames.** Don't let two bubbles share one Unity physics scene, or the far one overflows float32.
- **Determinism / replication:** the wire format and authoritative snapshots must carry the **double global** state (or global = origin + local recomputed server-side), never the raw float-local, so clients and persistence agree. (Ties into open item P-1.)
- **A merge changes a vessel's local frame.** Anything caching local coordinates across a merge/split (trails, predicted client transforms, in-flight reconciliation) must be re-based or reset on the event.

---

## Annotated link list

**KSP / the kraken**
- [HN discussion — "KSP's approach to the precision problem"](https://news.ycombinator.com/item?id=26938812) — clearest plain-English description of Krakensbane: shift the universe to keep the craft near origin; modern per-frame rebasing; doubles for maneuver nodes.
- [KSP Wiki (Fandom) — Version History / Kraken](https://kerbalspaceprogram.fandom.com/wiki/Version_History) — origin moved from Kerbin-centre to vessel-relative; floating origin added to scaled space. *(Canonical wiki.kerbalspaceprogram.com/wiki/Kraken returned Access Denied to automated fetch — read in a browser.)*
- [KSP rounding & floating point writeup (UAF)](http://ffden-2.phys.uaf.edu/webproj/211_fall_2014/Bryce_Melegari/ksp_rounding.html) — why magnitude growth turns a dropped bit from mm into metres.

**Float vs double / precision numbers**
- [ilikebigbits — Float or double?](https://www.ilikebigbits.com/2017_06_01_float_or_double.html) — the precision-vs-scale table used in §1; guidance on accumulating in the wider type.
- [Aapeli Vuorinen — Floating-point numbers for geographic coordinates](https://www.aapelivuorinen.com/blog/2023/06/30/floats-for-coordinates/) — float32 ~1.7 m vs float64 ~3 nm at planetary scale.

**Floating origin in Unity (implementation)**
- [Marcos Pereira — Floating origin in Unity](https://marcospereira.me/2021/05/18/floating-origin-in-unity/) — threshold choice, power-of-two advice, shifting roots + particles, multiplayer offset tracking.
- [brihernandez — Floating origin Gist](https://gist.github.com/brihernandez/9ebbaf35070181fa1ee56f9e702cc7a5) — compact drop-in script; camera-distance threshold, mass translation.
- [Manuel Rauber — Floating Origin in Unity](https://manuel-rauber.com/2022/04/06/floating-origin-in-unity/) — walkthrough of the technique and its rationale.
- [Salty Hash — Infinitely large play space](https://saltyhash.org/2019/12/26/making-an-infinitely-large-play-space-in-unity3d-floating-origin/) — threshold/recentre intuition.

**Multiple origins / large multiplayer worlds**
- [coherence — World Origin Shifting](https://docs.coherence.io/manual/advanced-topics/big-worlds/world-origin-shifting) — **closest match to our design:** server-absolute (double) + per-client (read: per-bubble) floating origin; shift event with delta; move-with vs don't-move-with modes.
- [Ogre forums — precision far from origin in space sims](https://forums.ogre3d.org/viewtopic.php?f=5&t=34154) — community treatment of camera-relative + double-then-narrow.

**Double-precision physics in Unity / DOTS**
- [Brprb08 — orbital-control-simulator](https://github.com/Brprb08/orbital-control-simulator) — Unity orbital sim running double-precision physics (native side) with GPU-drawn trajectories; example of doubles-authoritative / float-render.
- [Unity Discussions — DOTS for floating origin](https://discussions.unity.com/threads/dots-for-floating-origin.807117/) — ECS batch-shift benchmark (~0.03 ms / 200k entities); chunk+local vs float64-component patterns; no turnkey support.

**Reference implementation to read**
- [Sebastian Lague — Solar-System (Unity)](https://github.com/SebLague/Solar-System) — explorable solar system inspired by Outer Wilds; technique lives in the C# source (README is sparse) and the accompanying Coding Adventure videos — read the source for the precision/origin handling.

*Inaccessible to automated fetch:* the canonical `wiki.kerbalspaceprogram.com/wiki/Kraken` (Access Denied), `gamedev.net` large-multiplayer-worlds thread (HTTP 403), and `wiki.unity3d.com` Unify Community Floating Origin page (DNS unresolvable). Equivalent content was sourced from the alternatives above; open the originals in a browser if you want the primary text.

---

## Recommended implementation path

1. **Sim core owns doubles.** Add `GlobalOrigin` (double3) to the bubble model and keep every active vessel's global position/velocity in doubles. Orbital state already lives in doubles via patched conics (ADR-0004). *(plan.md: bubble manager + rigid-body integrator.)*
2. **One Unity physics scene per bubble.** Create/destroy a `PhysicsScene` per bubble; drive each with explicit `PhysicsScene.Simulate(fixedDt)` from the 60 Hz fixed step (ADR-0006). Each scene's origin is its bubble's floating origin.
3. **Seam conversion each tick.** Before stepping: `local = (vesselGlobalDouble − bubble.GlobalOrigin)` narrowed to float, written to PhysX. After stepping: `vesselGlobalDouble = bubble.GlobalOrigin + (double)local`. Subtraction always in doubles.
4. **Per-bubble rebase.** When the bubble's focus (centroid of its vessels, or the controlled vessel) exceeds a power-of-two threshold (~1024–2048 m) from its scene origin: translate all bodies in that scene by `−delta`, add `+delta` to `GlobalOrigin`, leave velocities untouched, pause/resume world particles, reset trails. Emit a `FloatingOriginShifted(bubbleId, delta)` event for presentation/replication consumers.
5. **Replication carries global doubles.** Snapshots encode global (double) state, not float-local, so clients and Postgres agree regardless of any server-side rebase. *(Coordinate with P-1 wire format.)*
6. **Then resolve P-4** (merge/split) per §7.

---

## P-4 proposal — floating-origin rebasing on bubble merge/split

*Open item, decide before Slice 1.1. The double-global / float-local split above makes this almost mechanical, because every vessel always has an authoritative global double-position independent of any bubble's local frame.*

**Trigger (from PHYS-2 / promotion):** a merge fires when two bubbles' vessels close within physics range (or a promoting vessel enters an existing bubble's range); a split fires when a bubble's vessels separate into clusters beyond physics range, or a vessel demotes to on-rails (PHYS-3).

**Merge (two bubbles → one):**
1. **Choose the surviving origin.** Keep the larger/more-populated bubble's `GlobalOrigin` (or the controlled-vessel's bubble's), to minimise the number of bodies that move. Call it `O_keep`.
2. **Re-base the incoming vessels into the kept frame.** For each vessel coming from the other bubble: `new_local = (vesselGlobalDouble − O_keep)` narrowed to float. Because both bubbles are about to be within physics range, `new_local` is guaranteed small — float32 stays precise. Velocities are unchanged (translation only).
3. **Move the bodies into the kept physics scene** at `new_local`, with their existing velocities, then destroy the now-empty scene.
4. **Emit `FloatingOriginShifted` for the merged-in vessels** (their local delta = `new_local − old_local`) so trails, client prediction, and reconciliation rebase rather than streak. PHYS-5 (docking without authority handoff) is satisfied automatically: both vessels were already in *one* server-side scene before contact.

**Split (one bubble → two):**
1. **Partition** the vessels into the new clusters (connected-components by physics range).
2. **One cluster keeps the existing scene & `GlobalOrigin`;** for each other cluster, create a new physics scene and set its `GlobalOrigin` to that cluster's centroid (in doubles, recomputed from authoritative global positions, *not* from float-local — avoids carrying any accumulated float error across the split).
3. **Re-base moved vessels** into their new scene: `new_local = (vesselGlobalDouble − newOrigin)`, velocities unchanged, emit shift events.

**Why this is safe and cheap:**
- Authoritative truth is always the global double-position; bubble frames are disposable scratch. A merge/split is just "recompute small float locals from doubles against a (possibly new) origin" — never a precision-losing transform between two float frames.
- All re-basing is pure translation, so momentum is conserved without velocity math.
- Centroids/origins are always recomputed from doubles, so accumulated float drift never propagates across a merge or split.
- Origin choice (keep the bigger frame) minimises body moves; ECS-style or simple-loop translation is sub-millisecond at our K≈few, small-vessel-count scale.

**Decision to lock for Slice 1.1:** *merge keeps the larger bubble's origin and re-bases incomers from authoritative doubles; split recomputes each new origin from the cluster centroid in doubles; all re-basing is translation-only (velocities preserved) and emits a `FloatingOriginShifted(bubbleId, vesselId, delta)` event; snapshots always carry global doubles.* This needs no cross-machine handoff (ADR-0003 / PHYS-5) and introduces no new precision-loss path.
