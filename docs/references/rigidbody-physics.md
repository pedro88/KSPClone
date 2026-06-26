# Rigid-Body Vessel Physics in Unity — Implementation Reference

> **Why this matters for us.** [ADR-0005](../adr/0005-rigid-vessels.md) rejects KSP's per-part soft-joint model: a vessel is **one rigid body**, with motion only at deliberate *articulation points* (robotic hinges/rotors, docking ports), and structural failure is a **discrete event**, not soft-body flex.
> This doc gathers the authoritative Unity APIs to satisfy **PHYS-4** (rigid vessel, motion only at articulation points), **PHYS-5** (dock two vessels into one without authority transfer), and **PHYS-6** (discrete structural failure at a load threshold).
> Active physics runs **server-side in bubbles** (PHYS-1/2) at a fixed 60 Hz (NET-5), so we drive PhysX manually and treat its non-determinism as a non-issue (server is authoritative — NET-1).

**Unity version assumed:** **Unity 6** (6000.x LTS). All APIs below exist back to ~2020 LTS (when `ArticulationBody` shipped); the manual-simulation API name changed (see §1.5: `Physics.autoSimulation` → `Physics.simulationMode`).

---

## 1. Rigidbody: thrust, forces, mass, inertia, ForceMode

### 1.1 Applying thrust
- `Rigidbody.AddForce(Vector3 force, ForceMode mode)` — apply at center of mass (no torque).
- `Rigidbody.AddForceAtPosition(Vector3 force, Vector3 position, ForceMode mode)` — **use this for engines**: applying thrust at the nozzle's world position automatically produces the correct torque if the thrust line is off the center of mass (gimbal, asymmetric engine-out). This is exactly the behaviour we want for a rigid vessel.
- `Rigidbody.AddTorque(Vector3 torque, ForceMode mode)` — pure rotation (RCS/reaction wheels can be modelled as direct torque).
- `Rigidbody.AddRelativeForce` / `AddRelativeForceAtPosition` — same in body-local space (convenient: engine thrust is "down the local axis").

### 1.2 ForceMode (exact semantics)
`UnityEngine.ForceMode` has four values:

| ForceMode | Meaning | Uses mass? | Continuous/instant | Effect on velocity |
|---|---|---|---|---|
| `Force` | Continuous force (Newtons) | yes | continuous (per step) | `v += force * dt / mass` |
| `Acceleration` | Continuous accel, ignores mass | no | continuous (per step) | `v += accel * dt` |
| `Impulse` | Instant impulse (N·s) | yes | instant | `v += impulse / mass` |
| `VelocityChange` | Instant velocity delta (m/s) | no | instant | `v += delta` |

For sustained engine thrust call `AddForceAtPosition(..., ForceMode.Force)` **every physics step**. For one-shot separation kicks (decouplers), use `ForceMode.Impulse` (see §6).

### 1.3 Mass, center of mass, inertia tensor
- `Rigidbody.mass` — total vessel mass (kg). Sum of part masses; recompute on staging/docking (§5, §4).
- `Rigidbody.centerOfMass` — **local-space** COM. By default Unity computes it from attached colliders' shapes (assuming uniform density), which is *not* what we want for parts of differing density. Set it explicitly from a mass-weighted sum of part COMs. Setting it overrides the auto value; assign `Vector3` to override, and it stays overridden until you reset it.
- `Rigidbody.ResetCenterOfMass()` — restore the auto-computed COM.
- `Rigidbody.worldCenterOfMass` — read-only world COM (handy for thrust/torque math and HUD).
- `Rigidbody.inertiaTensor` + `Rigidbody.inertiaTensorRotation` — the diagonalised inertia in a frame at the COM. *"Inertia tensor is a rotational analog of mass."* If unset, Unity computes it automatically from all colliders attached to the Rigidbody. For our compound hull this auto-value is usually adequate; override only if we model density precisely. **`ResetInertiaTensor()`** restores the auto value.
- **Important for staging/docking:** after you change which colliders are attached (or change mass), call `ResetCenterOfMass()` and `ResetInertiaTensor()` (or set them explicitly) so PhysX recomputes the mass distribution for the new compound shape.

### 1.4 Center of mass and `AddForceAtPosition`
Torque from `AddForceAtPosition` is computed about the **current `centerOfMass`**, so a correct COM is load-bearing for realistic rotation under off-axis thrust. Get the COM right first, then thrust math is free.

### 1.5 Server-side manual `Physics.Simulate()`
We do **not** want PhysX stepping itself on the server's render loop; we step it deterministically per server tick.

- **Disable auto-step:** `Physics.simulationMode = SimulationMode.Script;` (Unity 6). *(Pre-2022 name: `Physics.autoSimulation = false;` — superseded by `SimulationMode`.)* Set it once at startup before any physics runs. Other modes are `SimulationMode.FixedUpdate` (default) and `SimulationMode.Update`.
- **Step manually:** `Physics.Simulate(float step)` — runs *all* stages: collision detection, rigidbody + joint integration, and physics callbacks (contacts, triggers, `OnJointBreak`). Pass a **fixed step** (e.g. `1f/60f` for our 60 Hz tick, NET-5) — never `Time.deltaTime`. Calling `Physics.Simulate` does **not** call `FixedUpdate`, so any per-step force application (engine thrust) must be driven by our own tick code, not `FixedUpdate`.
- **Per-bubble isolation:** each `PhysicsScene` has its own `PhysicsScene.Simulate(step)`. This maps cleanly to PHYS-1 *physics bubbles*: create one `PhysicsScene` per bubble (via `SceneManager.CreateScene` with `LocalPhysicsMode.Physics3D`) and step them independently, each with its own floating origin.

---

## 2. One vessel = one Rigidbody (compound colliders) vs many bodies

This is the core ADR-0005 decision, and Unity's design strongly favours it.

- **Compound collider = many colliders, one Rigidbody.** Put a `Rigidbody` on the vessel root; each part contributes one or more child `Collider` components on child GameObjects. PhysX treats them as **a single rigid body** with one combined mass/COM/inertia. This is exactly our "rigid vessel."
- **Performance:** compound colliders of primitives (box/capsule/sphere) are far cheaper than convex mesh colliders and avoid the per-part joint solving that dominates KSP's cost. Guidance: *"use as few primitive colliders as possible."* Note collisions are reported **per individual collider** in the compound, so arrange shapes to avoid spurious internal collision pairs.
- **Why not many bodies + joints (the KSP model):** each `Rigidbody`+`Joint` pair must be solved iteratively in *maximal coordinate space*; constraints are only satisfied "if the solver converges after a set of iterations" → the wobble/"kraken" instability, and cost + replicated state scale with part count. A single compound Rigidbody replicates as **one transform + one velocity** (matches ADR-0005's replication argument and NET-4/NET-5 snapshot budget).
- **Non-uniform mass:** because we collapse many parts into one body, set `mass`, `centerOfMass`, and (optionally) `inertiaTensor` from the part list rather than trusting the collider-derived defaults (§1.3).

**Concrete:** the vessel hull is **one `Rigidbody` with a tree of child primitive colliders** (one or a few per part). Only articulation points and decouple points break this single-body rule.

---

## 3. Joints & ArticulationBody — robotic hinges/rotors and docking

### 3.1 The three options
- **`HingeJoint`** — a single rotational DOF with optional motor/spring/limits. Connects two Rigidbodies. Simple, but maximal-coordinate (can drift/stretch under load).
- **`ConfigurableJoint`** — the superset joint: per-axis Locked/Limited/Free linear and angular motion, drives, limits, projection, and `breakForce`/`breakTorque`. Connects two Rigidbodies.
- **`ArticulationBody`** — a body **and** its joint to its parent in one component, solved in **reduced coordinate space**.

### 3.2 ArticulationBody is the right tool for robotic hinges/rotors
*"An Articulation Body allows you to define in one single component the properties that you would similarly define through a Rigidbody and a regular Joint."* Build the robotic limb as a parent→child chain of `ArticulationBody` components (no `Rigidbody` on the moving parts; colliders still required). The root is an `ArticulationBody` (set **`immovable`** if it is the static base; for us the root is the vessel hull, which is mobile).

Joint types (`ArticulationJointType`):
- `FixedJoint` — rigid, unbreakable, unstretchable.
- `PrismaticJoint` — slide along one axis (linear actuators, telescoping).
- `RevoluteJoint` — rotate about one axis = **our robotic hinge / rotor**.
- `SphericalJoint` — two swings + one twist.

Per-DOF motion is `ArticulationDofLock`: `LockedMotion` / `LimitedMotion` / `FreeMotion`. Drives (`ArticulationDrive` via `xDrive`/`yDrive`/`zDrive`) move the joint to a target:

```
appliedForce = stiffness * (target - position) + damping * (targetVelocity - velocity)   // clamped to forceLimit
```

So a robotic hinge = `RevoluteJoint` + `LimitedMotion` on its DOF + an `ArticulationDrive` with `stiffness`, `damping`, `target` (degrees), and `forceLimit`. A continuously-spinning rotor = `FreeMotion` + drive on `targetVelocity`.

### 3.3 Why ArticulationBody over Rigidbody+Joint for articulation
Reduced-coordinate solving means **locked DOFs are unbreakable and unstretchable by design** — no joint drift, no stretch, no solver-convergence wobble. *"Errors are propagated up the kinematic chain"* with iterative joints; articulations avoid this. This is the rigid-but-articulated behaviour ADR-0005 wants: the limb moves *only* at its commanded DOF and never sags or jitters.

**Trade-off to note:** an `ArticulationBody` chain is a single solver island rooted at one root body. That makes articulations a natural fit *within* one vessel hull (hull = root `ArticulationBody`, robotic parts = child articulations). It complicates runtime merge/split (docking), because re-parenting across articulation chains is heavier than swapping a `FixedJoint` — see §4 and Gotchas.

### 3.4 Docking ports
Two designs, pick per the merge strategy in §4:
- **`ArticulationBody` everywhere:** dock = re-parent the incoming vessel's articulation chain under the host's root. Cleanest "one rigid body" result; heaviest to mutate at runtime.
- **`FixedJoint` latch:** keep each vessel a compound `Rigidbody`; on dock create a `FixedJoint` (or `ConfigurableJoint` fully Locked) between them. Simpler runtime mutation, but reintroduces one solver joint per dock (acceptable — it's *one* joint per docking interface, not per part).

---

## 4. Docking: detect alignment, latch into one rigid body, undock

### 4.1 Detect & align
- Put a trigger `Collider` (`isTrigger = true`) on each docking port; use `OnTriggerEnter` / `OnTriggerStay` (these fire under `Physics.Simulate` too).
- Confirm capture conditions before latching: ports within distance, axes anti-parallel (`Vector3.Dot(portA.forward, -portB.forward) > cosTolerance`), roll aligned, and closing speed under a limit.

### 4.2 Latch (merge two vessels → one rigid body)
**Key Unity fact:** *non-kinematic Rigidbodies ignore the transform hierarchy* — parenting one under another does **not** make them move together. You must either join with a joint or genuinely merge into one body. Two viable strategies:

1. **Joint latch (recommended first cut):** add a `FixedJoint` on vessel A with `connectedBody = B.rigidbody` (or a fully-Locked `ConfigurableJoint`). Both keep their own `Rigidbody`. Pro: trivial to create/destroy, and undock = `Destroy(joint)`. Con: it's a solver constraint (tiny flex possible) — mitigate with `ConfigurableJoint.projectionMode = PositionAndRotation` and a high/`Infinity` break force at the joint (so the *port* doesn't break; structural failure is modelled elsewhere per PHYS-6).
2. **True merge (matches ADR-0005 literally — one body):** re-parent B's colliders (and any `ArticulationBody` sub-chains) under A's root GameObject, destroy B's `Rigidbody`, then `A.ResetCenterOfMass()` + `A.ResetInertiaTensor()` (or set explicitly) and `A.mass += Bmass`. Result is a single compound Rigidbody — the cleanest rigid vessel, but more bookkeeping (transform offsets, restoring sub-articulations, velocity continuity: set the merged body's velocity to the COM-weighted average of the two pre-dock velocities).

**PHYS-5 ("without transferring authority between machines"):** because all active physics runs **server-side** (PHYS-1), both vessels are already simulated by the same server bubble before they dock. The merge is a purely server-side body mutation; clients only see the resulting snapshot (NET-4). No cross-machine authority handoff is needed — this requirement is satisfied by the bubble model, not by the joint API.

### 4.3 Undock (split)
- Joint-latch design: `Destroy(fixedJoint)`, then apply a small separation impulse along the port axis (§6).
- True-merge design: this is the same operation as **staging** (§5) — partition the compound back into two bodies.

---

## 5. Staging / decoupling: split one rigid body into two at runtime

There is no PhysX "split body" call — you reconstruct two bodies:

1. Decide the partition: which child colliders/parts go to the jettisoned stage.
2. Create a new GameObject for the separated stage, add a `Rigidbody`, and **re-parent** that subset of colliders onto it.
3. Set the new body's `velocity` and `angularVelocity` to match the parent at separation (so it inherits motion — compute the linear velocity at the new body's COM: `v_part = v + ω × (comPart − comParent)`).
4. On **both** bodies recompute mass distribution: `ResetCenterOfMass()` + `ResetInertiaTensor()` (or explicit), and fix `mass`.
5. Apply **separation forces** (§6).
6. Optionally add brief collision ignore between the two fresh bodies (`Physics.IgnoreCollision`) for a few steps so they don't immediately re-collide at the seam.

This is the inverse of the §4.2 true-merge and shares its helper code. Decouple points are pre-authored in the part tree (BUILD-1), so the partition is known ahead of time.

---

## 6. Discrete structural failure & separation forces (PHYS-6)

### 6.1 Joint break at a load threshold
For *intentional articulation joints* and *docking ports* that should be able to fail:
- `Joint.breakForce` (float) and `Joint.breakTorque` (float) — set to the part's load threshold; `Mathf.Infinity` = unbreakable.
- When exceeded, PhysX **breaks the joint, raises `OnJointBreak(float breakForce)`** on the joint's GameObject, then **auto-removes and destroys the Joint component**. The float is the magnitude at failure.
- **Caveat:** a `ConfigurableJoint` can only break on an axis that is **Locked or Limited** (a `Free` axis carries no constraint force, so it never breaks). Lock/limit the DOF you want to be able to snap.
- `OnJointBreak` is our discrete-failure event hook: emit a `StructuralFailure` domain event, then run the §5 split to physically separate the now-detached substructure. This gives PHYS-6's "discrete event, no soft-body flex": the structure is intact until the threshold, then snaps cleanly.

### 6.2 Failure for the compound hull (no joint there)
Our hull is one rigid body with **no internal joints**, so there's nothing for PhysX to break inside it. Discrete structural failure of the monolithic hull is therefore a **game-logic decision**, not a PhysX event: track the load on critical members (peak contact impulse from `OnCollisionEnter`/`collision.impulse`, or computed stress from thrust/acceleration) and, when it crosses a part threshold, **trigger the §5 split yourself** (and destroy the part as an event). PhysX `breakForce` only helps where an actual joint exists (articulation points, docking ports).

### 6.3 Separation forces
- Decoupler ejection / undock kick: `rb.AddForce(axis * ejectImpulse, ForceMode.Impulse)` on each resulting body (opposite directions), applied the step after the split.
- For an off-axis kick (spin-stabilised separation) use `AddForceAtPosition(..., ForceMode.Impulse)`.

---

## 7. PhysX determinism caveats (and why they don't bite us)

- **Unity/PhysX is not cross-platform deterministic.** Floating-point results differ across CPU vendors/architectures and SIMD/compiler settings: a replay captured on Intel can desync on AMD even with identical build, timestep, and inputs. Determinism needs *both* identical timestep *and* identical FP precision across machines — PhysX guarantees neither across hardware.
- **Same machine + fixed step ≈ reproducible.** On one binary/CPU with a fixed `Physics.Simulate` step it is *largely* repeatable, but Unity does not contractually guarantee even this.
- **Why this is fine for us (authoritative server, NET-1):** PhysX runs **only on the server**. There is exactly one simulator; clients never run authoritative physics — they predict their own vessel (NET-2) and **reconcile** to server snapshots (NET-3), and render others by interpolation (NET-4). We never compare two independent PhysX runs, so cross-platform non-determinism is irrelevant. The contract that matters is "server snapshot wins," not "physics is bit-identical everywhere."
- **Consequence:** do not design any mechanic that assumes a client can re-derive the server's exact physics state. Always treat the server snapshot as ground truth.

---

## 8. Gotchas (read before implementing)

1. **Non-kinematic Rigidbodies ignore parenting.** Re-parenting alone never couples two dynamic bodies — you must merge into one body or add a joint (§4.2). Easy to get silently wrong.
2. **Always reset mass distribution after a merge/split.** Forgetting `ResetCenterOfMass()` / `ResetInertiaTensor()` (or explicit values) leaves the body with stale COM/inertia → wrong rotation under thrust.
3. **Velocity continuity on split/merge.** A freshly created body defaults to zero velocity; explicitly transfer linear+angular velocity (with the `ω × r` term for off-COM parts) or the stage will appear to "stop" at separation.
4. **ConfigurableJoint only breaks on Locked/Limited axes** (§6.1). A Free axis never reports `OnJointBreak`.
5. **`Physics.Simulate` does not call `FixedUpdate`.** All per-step force application (thrust) must be driven by your own server tick, not `FixedUpdate`, once in `SimulationMode.Script`.
6. **Pass a fixed step to `Physics.Simulate`, never `Time.deltaTime`** — variable steps cause inconsistent/unstable integration.
7. **ArticulationBody chains are single solver islands.** Re-parenting articulations at runtime (docking) is heavier than swapping a `FixedJoint`; prefer compound-Rigidbody + FixedJoint latch for the first cut, reserve full articulation re-parenting for later if needed.
8. **Compound colliders report collisions per child collider.** Expect multiple contact callbacks per vessel-vessel touch; design contact/stress logic accordingly.
9. **PhysicsScene per bubble.** Don't let two bubbles share the default scene, or their bodies will interact; create a `PhysicsScene` per bubble and step each separately (§1.5).

---

## 9. Annotated links (verified)

**Unity Scripting API — Rigidbody & forces**
- [Rigidbody](https://docs.unity3d.com/6000.4/Documentation/ScriptReference/Rigidbody.html) — the body component; mass, drag, velocity, COM, inertia.
- [Rigidbody.AddForce](https://docs.unity3d.com/ScriptReference/Rigidbody.AddForce.html) — apply force at COM with a `ForceMode`.
- [Rigidbody.AddTorque](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Rigidbody.AddTorque.html) — apply rotational force.
- [ForceMode](https://docs.unity3d.com/ScriptReference/ForceMode.html) — Force/Acceleration/Impulse/VelocityChange semantics (our §1.2 table).
- [Rigidbody.inertiaTensor](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Rigidbody-inertiaTensor.html) — diagonal inertia at COM; auto-computed from colliders if unset.

**Manual simulation (server-side stepping)**
- [Physics.Simulate](https://docs.unity3d.com/ScriptReference/Physics.Simulate.html) — manually advance the simulation by a fixed step.
- [Manual: Manually set physics simulation](https://docs.unity3d.com/6000.3/Documentation/Manual/physics-optimization-cpu-manual-simulation.html) — `SimulationMode.Script` setup + best practices (fixed step, no FixedUpdate).
- [Physics.autoSimulation (legacy)](https://docs.unity3d.com/2017.1/Documentation/ScriptReference/Physics-autoSimulation.html) — pre-Unity-2022 way to disable auto-step (now `simulationMode`).

**Compound colliders / single body**
- [Manual: Introduction to compound colliders](https://docs.unity3d.com/6000.3/Documentation/Manual/compound-colliders-introduction.html) — many colliders, one Rigidbody = one rigid body.
- [Manual: Collider types and performance](https://docs.unity3d.com/2022.3/Documentation/Manual/physics-optimization-cpu-collider-types.html) — primitives vs mesh colliders; keep collider count low.

**ArticulationBody (robotic hinges/rotors)**
- [Manual: Articulation Body component reference](https://docs.unity3d.com/Manual//class-ArticulationBody.html) — joint types, drives, immovable root, reduced coordinates.
- [Manual: Introduction to physics articulations](https://docs.unity.cn/Manual/physics-articulations.html) — reduced vs maximal coordinate space (why articulations don't drift/stretch).
- [Unity-Technologies/articulations-robot-demo — Building With Articulations](https://github.com/Unity-Technologies/articulations-robot-demo/blob/master/docs/Building-With-Articulations.md) — official worked example: chain construction, revolute joints, drives.
- [Unity blog: Use articulation bodies for realistic motion](https://unity.com/blog/industry/use-articulation-bodies-to-easily-prototype-industrial-designs-with-realistic-motion-and) — rationale and accuracy claims for industrial/robotic chains.

**Joints & structural failure**
- [ConfigurableJoint](https://docs.unity3d.com/ScriptReference/ConfigurableJoint.html) — superset joint (per-axis lock/limit/free, drives, projection, break force).
- [Hinge Joint component reference](https://docs.unity3d.com/6000.4/Documentation/Manual/class-HingeJoint.html) — simple single-DOF hinge with motor/spring/limit.
- [Joint.breakForce](https://docs.unity3d.com/ScriptReference/Joint-breakForce.html) / [Joint.breakTorque](https://docs.unity3d.com/ScriptReference/Joint-breakTorque.html) — failure thresholds (`Mathf.Infinity` = unbreakable).
- [Joint.OnJointBreak](https://docs.unity3d.com/ScriptReference/Joint.OnJointBreak.html) — `void OnJointBreak(float breakForce)`; joint is auto-destroyed after the callback.
- [Issue: ConfigurableJoint break needs Locked/Limited axis](https://issuetracker.unity3d.com/issues/configurable-joint-does-not-break-when-break-torque-is-small-and-a-high-torque-is-applied) — the §6.1 caveat.

**Determinism**
- [Discussion: Why Unity Physics Is Not Deterministic](https://discussions.unity.com/t/why-unity-physics-is-not-deterministic/1667389) — needs identical timestep + FP precision.
- [GameDev.net: FP determinism in Unity/PhysX/.NET, Intel vs AMD](https://www.gamedev.net/forums/topic/708301-floating-point-determinism-in-unity-physx-and-net-intel-vs-amd/) — concrete cross-vendor desync example.
- [Unity Multiplayer: Physics](https://mp-docs.dl.it.unity3d.com/netcode/current/advanced-topics/physics/) — networked/authoritative physics guidance.

**KSP references (docking/staging behaviour)**
- [KSP Wiki: Docking](https://wiki.kerbalspaceprogram.com/wiki/Docking) — docking-port capture/coupling semantics to mirror.
- [Kerbal Joint Reinforcement (Continued)](https://github.com/KSP-RO/Kerbal-Joint-Reinforcement-Continued) — illustrates *why* KSP's per-part soft joints needed stiffening (the problem ADR-0005 sidesteps).
- [kOS: Stage structure](https://ksp-kos.github.io/KOS/structures/vessels/stage.html) — staging/decoupler activation model for reference.

---

## 10. Recommended implementation path

1. **Server stepping spine.** Set `Physics.simulationMode = SimulationMode.Script` at boot; create one `PhysicsScene` per bubble; drive each with `PhysicsScene.Simulate(1f/60f)` from the server tick (NET-5). Apply thrust inside that tick, not `FixedUpdate`.
2. **Rigid hull.** One `Rigidbody` per vessel; child primitive colliders per part. Set `mass`, `centerOfMass`, `inertiaTensor` from the part tree; call `ResetCenterOfMass`/`ResetInertiaTensor` after any change.
3. **Thrust/RCS.** Engines = `AddForceAtPosition(thrust, nozzlePos, ForceMode.Force)` each step; RCS/reaction wheels = `AddTorque`. One-shot kicks = `Impulse`.
4. **Articulation.** Model robotic hinges/rotors as `ArticulationBody` sub-chains under the hull (RevoluteJoint + drive). Start here only where motion is actually needed; everything else stays in the rigid compound.
5. **Docking (first cut).** Trigger colliders on ports → alignment check → `FixedJoint` latch (Locked `ConfigurableJoint` with projection if you want zero flex). Undock = destroy joint + separation impulse. Upgrade to true single-body merge later if replication/wobble demands it.
6. **Staging.** Pre-authored decouple points; on stage, re-parent the jettisoned colliders to a new `Rigidbody`, transfer velocity (`v + ω×r`), reset mass distribution on both, apply `Impulse` separation, brief `Physics.IgnoreCollision`.
7. **Structural failure.** Where a real joint exists (articulation/dock), set `breakForce`/`breakTorque` on Locked/Limited axes and handle `OnJointBreak` as a domain event → split. For the monolithic hull, track load in game logic and trigger the same split at threshold.
8. **Trust the server.** Snapshots are ground truth (NET-1/3/4); never assume a client reproduces server physics.

---

## 11. Open questions for our case

1. **Latch vs true-merge for docking.** Is a `FixedJoint` latch's tiny residual flex acceptable under ADR-0005's "rigid" promise, or must every dock collapse to a single compound Rigidbody? (Replication is one transform either way only in the true-merge case — a joint-latched pair is *two* bodies on the wire.)
2. **Articulation re-parenting on dock.** If both vessels carry `ArticulationBody` sub-chains, how do we cheaply re-root the incoming chain into the host on merge — or do we forbid docking while a robotic part is mid-motion?
3. **Hull failure model.** What's the authoritative "load" signal for the jointless compound hull (peak contact impulse? computed thrust/accel stress? per-member analytic model?) that PHYS-6 thresholds compare against?
4. **Mass distribution source of truth.** Do we set `inertiaTensor` explicitly from part data, or trust Unity's collider-derived auto-inertia? (Affects rotation realism vs authoring cost.)
5. **Bubble step budget.** With K concurrent `PhysicsScene.Simulate` calls at 60 Hz on one server, what is the per-bubble part/collider budget before the tick blows past 16.6 ms?
6. **Floating origin per bubble.** How does each bubble's floating-origin rebasing interact with `Physics.Simulate` (rebase between steps, never mid-step) to avoid precision artifacts at large coordinates?
