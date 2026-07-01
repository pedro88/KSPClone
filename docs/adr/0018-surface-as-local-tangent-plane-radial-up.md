# Surface is a local tangent plane; a landed vessel is active-physics on a static collider

The demo has never had ground: the seed craft is parked ~50 Earth radii out where gravity is negligible, precisely so nothing needs collision (`ServerBootstrap._demoStartAtRest`, comment at ServerBootstrap.cs:22-27). "Start on Earth and feel weight" changes that. This ADR pins the smallest surface model that satisfies PHYS-7 without opening the terrain/rotating-body/surface-frame cans of worms.

## Context

A vessel resting on a planet is **not** on a Kepler orbit — its weight is held by a normal force, not centripetal balance. So the on-rails path (closed-form `position(t)`) cannot represent it. Under the constitution that leaves exactly one option: a landed, crewed vessel is an **active-physics** vessel (Art. 3 — on-rails is default, physics is the exception; a surface launch is an exception). Its weight and ground contact are simulated by PhysX in the vessel's *physics bubble*, server-authoritative like every other active-physics force (Art. 1).

Three things were missing to make that real:

1. **No ground collider anywhere.** The bubble `PhysicsScene`s contain only vessel rigid bodies; there is no static surface for them to rest on (`ServerVesselBodies` even sets the inertia tensor by hand *because* "the body has no collider", ServerVesselBodies.cs:80).
2. **The vessel rigid body has no collider**, so even with a ground it could not generate contact.
3. **Orientation.** Real "up" on a surface is *radial* — from the body centre to the vessel. But the whole client presentation is **untumbled**: it hardcodes world +Y as up (ground plane normal, exhaust plume −Y, orbit camera on world-up — ClientWorldRenderer.cs). Earth now sits at ~1 AU from the world origin (M2-T12), so the radial direction at an arbitrary surface point is *not* world +Y.

## Decisions

### 1. Surface = a flat, static tangent plane at the launch/landing site

Not curved terrain, not a mesh, not a heightfield. A single large static collider tangent to the body at the site, with its normal along the local *radial-up*. At 4 players around one pad this is indistinguishable from a curved surface at the scales a launch cares about, and it defers the entire terrain subsystem (streaming, LOD, quadtrees) that we are nowhere near needing. Documented as a *ground plane* in CONTEXT.md.

The plane is created in the vessel's bubble `PhysicsScene` (so bubbles stay isolated, ADR-0003) as a static collider — no `Rigidbody`, so PhysX treats it as immovable.

### 2. A landed vessel is active-physics resting on that plane (no new "landed rail")

We do **not** add a landed-on-rails analytic state in this slice. The craft is promoted to active-physics (as the demo already does on presence), gravity pulls it onto the plane (real `μ/r²` ≈ 9.82 m/s² at Earth's surface — the existing point-mass `ApplyGravity` already does this once the craft is at surface radius), and the plane's normal force holds it. Thrust and attitude act against that contact unchanged (PHYS-7's second clause).

A proper *warp-safe landed* state (so a parked craft can demote to on-rails and survive warp / empty-universe on the ground) is a **later slice** — it needs a landed rail keyed to body rotation, which we don't have (no sidereal spin yet). Flagged, not built.

### 3. The launch site is the body's +Y pole, so radial-up = world +Y

Rather than rotate the client into a per-site surface frame (touching ground, plume, dust, camera — a bigger change than the whole rest of this slice), we choose the launch point so radial-up is already world +Y: the point on the body's surface displaced from its centre by `+EarthRadius·ŷ`. Then "down" (gravity, toward the body centre) is −Y, the ground normal is +Y, and every untumbled +Y-up assumption in the client holds verbatim. No client orientation change ships in this slice.

This is expressible in Kepler elements for the pre-promotion seed: with inclination 0 and Ω 0 the orbital plane is the world XY plane, and a circular element (`e=0`) with `argp + M0 = π/2` places the craft at `(0, r, 0)` in Earth's frame at epoch. Set `r = EarthRadius + halfHeight` so it spawns in contact. On promotion the demo zeroes velocity (existing `OnDemoPromoted`), so it starts genuinely at rest on the plane.

**Approximation, stated honestly:** Earth itself orbits the Sun (~2e-7 rad/s), so over a long session the true radial at the fixed pad tilts away from +Y and Earth's centre translates away from the frozen plane. Both are negligible over a demo (minutes) and the ground plane is large and flat, so a sub-millidegree tilt is invisible. When we add body rotation and real landing, the +Y-pole trick retires in favour of an actual surface frame — a separate ADR.

### 4. The ground plane does not follow floating-origin rebases

The bubble rebases its float origin only when its centroid drifts past 1024 m (ADR-0012). A craft resting on the pad *is* the centroid, so it never drifts and no rebase fires while landed. Once the craft launches and climbs past the threshold a rebase does fire and the static plane is left behind in world terms — but by then the craft is >1 km above it and the pad is irrelevant. So the plane is created once in the bubble-local frame at spawn and never rebased. Cheap and correct for the landed/launch case; documented so nobody "fixes" it by adding rebase coupling.

## What this is not

- **Not** terrain. One flat tangent plane per site; no curvature, no mesh, no collision with anything but the pad.
- **Not** a landed on-rails state. A parked craft cannot yet demote-to-rails on the ground or survive warp landed; that needs body rotation first.
- **Not** a surface frame. Orientation is hard-aligned to world +Y via the +Y-pole launch site; a real per-site frame is deferred.
- **Not** a client rendering change. The existing untumbled +Y-up presentation is reused unchanged, which is the whole point of the +Y-pole choice.

## Satisfies

PHYS-7 (a landed active vessel holds its weight on server-authoritative ground contact and keeps normal control). Consistent with Art. 1 (contact simulated server-side, authoritative), Art. 2 (physics on the fixed tick, not render), Art. 3 (surface launch is the physics exception; on-rails remains the default elsewhere).
