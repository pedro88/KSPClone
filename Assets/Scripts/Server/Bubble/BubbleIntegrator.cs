#nullable enable annotations

using System.Collections.Generic;
using UnityEngine;
using KSPClone.SimCore;

namespace KSPClone.Server
{
    /// <summary>
    /// Server-side driver that steps every bubble's PhysicsScene at
    /// the fixed 60 Hz tick (ADR-0006, ADR-0012 §1) and reconciles the
    /// resulting transforms back into the sim core's authoritative
    /// doubles.
    ///
    /// Per tick, for each live bubble:
    ///   1. Pre-step: apply per-vessel forces (thrust, gravity) — wired
    ///      by Slice 1.2 (M1-T06 / T07); today the rigidbody coasts
    ///      inertially because no forces are applied.
    ///   2. Step: <c>physicsScene.Simulate(dt)</c>.
    ///   3. Post-step: read every <see cref="RigidVesselBody"/>'s local
    ///      position+velocity, convert to global doubles
    ///      (world = origin + local; in doubles, ADR-0012 §6), and
    ///      write back to <see cref="Vessel.CachedWorldPosition"/>
    ///      / <see cref="Vessel.CachedWorldVelocity"/>. Also write
    ///      <see cref="Vessel.CachedLocalPosition"/> /
    ///      <see cref="Vessel.CachedLocalVelocity"/>.
    ///   4. Rebase: ask <see cref="FloatingOriginManager"/> if the
    ///      bubble's local centroid drifted past the threshold;
    ///      if so, translate every rigidbody by the inverse delta and
    ///      apply the shift to the sim core bubble's GlobalOrigin
    ///      (already done by the manager — the rigidbodies must follow).
    ///
    /// Lives in <see cref="KSPClone.Server"/> (UnityEngine-coupled).
    /// </summary>
    public sealed class BubbleIntegrator
    {
        public double FixedDt { get; }
        private readonly BubbleRegistry _registry;
        private readonly UnityBubbleHost _host;
        private readonly FloatingOriginManager _floatingOrigin;
        private readonly SimWorld _world;
        private readonly VesselEngineRegistry _engines;
        private readonly VesselMassRegistry _masses;
        private readonly List<RigidVesselBody> _scratch = new();

        public BubbleIntegrator(
            SimWorld world,
            BubbleRegistry registry,
            UnityBubbleHost host,
            FloatingOriginManager floatingOrigin,
            VesselEngineRegistry engines,
            VesselMassRegistry masses,
            double fixedDt = 1.0 / 60.0)
        {
            _world = world;
            _registry = registry;
            _host = host;
            _floatingOrigin = floatingOrigin;
            _engines = engines;
            _masses = masses;
            FixedDt = fixedDt;
        }

        /// <summary>Step every live bubble once.</summary>
        public void Step()
        {
            // Snapshot bubble ids so we can mutate the registry during iteration
            // (destroying empty bubbles, etc.) without invalidating the enumerator.
            var bubbleIds = new List<BubbleId>(_registry.Count);
            foreach (var b in _registry.All) bubbleIds.Add(b.Id);

            foreach (var id in bubbleIds)
            {
                if (!_registry.TryGet(id, out var bubble)) continue;
                if (bubble.Lifecycle == BubbleLifecycle.Empty) continue;
                var sceneOpt = _host.TryGetPhysicsScene(id);
                if (!sceneOpt.HasValue) continue;

                StepBubble(bubble, sceneOpt.Value);
            }
        }

        private void StepBubble(PhysicsBubble bubble, PhysicsScene scene)
        {
            // (1) Pre-step forces. Apply gravity from each vessel's SOI body
            //     (PHYS-4: single body per vessel, ORBIT-1). The body's world
            //     position is subtracted by the bubble's GlobalOrigin in
            //     doubles — never in floats (ADR-0012 §6).
            CollectRigidBodies(bubble, _scratch);
            ApplyGravity(bubble, _scratch);
            ApplyThrust(bubble, _scratch);

            // (2) Step the bubble's physics scene independently of Unity's
            // automatic simulation (SimulationMode.Script + Physics.Simulate).
            scene.Simulate(FixedDt);

            // (3) Post-step: read transforms back into authoritative doubles.
            foreach (var rb in _scratch)
            {
                if (!_world.Vessels.TryGetValue(rb.VesselId, out var vessel)) continue;
                var local = rb.Body.position;
                var localVel = rb.Body.velocity;
                var worldPos = bubble.GlobalOrigin + ToVector3d(local);
                var worldVel = ToVector3d(localVel);
                vessel.CachedLocalPosition = ToVector3d(local);
                vessel.CachedLocalVelocity = ToVector3d(localVel);
                vessel.CachedWorldPosition = worldPos;
                vessel.CachedWorldVelocity = worldVel;
            }

            // (4) Floating-origin rebase. The manager updates the bubble's
            // GlobalOrigin; we apply the inverse delta to every rigidbody
            // in the bubble so the move is invisible to anything that
            // reads local coords.
            if (_floatingOrigin.RebaseIfDrifted(bubble, _world.Vessels.Values))
            {
                // The shift event tells us the delta. We need to move every
                // rigidbody by -delta in local coords. Re-collect since
                // membership could in principle change between ticks (M1: it
                // doesn't, but defensive).
                CollectRigidBodies(bubble, _scratch);
                var newOrigin = bubble.GlobalOrigin; // updated by the manager
                foreach (var rb in _scratch)
                {
                    if (!_world.Vessels.TryGetValue(rb.VesselId, out var vessel)) continue;
                    if (!vessel.CachedWorldPosition.HasValue) continue;
                    var newLocal = ToUnity(vessel.CachedWorldPosition.Value - newOrigin);
                    rb.TranslateLocal(newLocal - rb.Body.position);
                    vessel.CachedLocalPosition = ToVector3d(rb.Body.position);
                }
            }
        }

        private void ApplyGravity(PhysicsBubble bubble, List<RigidVesselBody> bodies)
        {
            if (_world.Bodies is null) return;
            foreach (var rb in bodies)
            {
                if (!_world.Vessels.TryGetValue(rb.VesselId, out var vessel)) continue;
                var parentId = vessel.Orbit.ParentBody;
                if (!_world.Bodies.TryGet(parentId, out var parentBody)) continue;
                var parentWorldPos = _world.Bodies.WorldPositionOf(parentId, _world.Clock.GameTimeSeconds);
                var parentLocalPos = parentWorldPos - bubble.GlobalOrigin; // doubles
                var vesselLocalPos = ToVector3d(rb.Body.position);
                var a = GravityModel.Acceleration(vesselLocalPos, parentLocalPos, parentBody.GravParameterMu);
                var f = ToUnity(a) * (float)rb.Body.mass;
                rb.Body.AddForce(f, ForceMode.Force);
            }
        }

        private void ApplyThrust(PhysicsBubble bubble, List<RigidVesselBody> bodies)
        {
            // Thrust is applied in vessel-local space (the engine's
            // ThrustDirectionLocal is a unit vector in the vessel's
            // body frame). For M1 we approximate the vessel frame by
            // the rigidbody's transform.up; this is correct for
            // untumbled craft and degrades gracefully under rotation.
            foreach (var rb in bodies)
            {
                if (!_world.Vessels.TryGetValue(rb.VesselId, out var vessel)) continue;
                var engines = _engines.EnginesFor(vessel.Id);
                if (engines is null || engines.Count == 0)
                {
                    vessel.ThrustActive = false;
                    continue;
                }
                var throttle = vessel.ThrottleCommand;
                if (throttle <= 0.0)
                {
                    vessel.ThrustActive = false;
                    continue;
                }

                bool anyFiring = false;
                foreach (var e in engines)
                {
                    var fMag = e.EffectiveThrust(throttle);
                    if (fMag <= 0.0) continue;
                    anyFiring = true;
                    // Convert engine-local thrust direction to world
                    // (the rigidbody transform defines the vessel frame).
                    var dirWorld = rb.transform.TransformDirection(ToUnity(e.ThrustDirectionLocal));
                    var force = dirWorld * (float)fMag;
                    var mountWorld = rb.Body.position + rb.transform.TransformDirection(ToUnity(e.MountLocalPosition));
                    rb.Body.AddForceAtPosition(force, mountWorld, ForceMode.Force);
                }
                vessel.ThrustActive = anyFiring;

                // Propellant accounting + Δv bookkeeping.
                _engines.ConsumePropellant(vessel.Id, throttle, FixedDt, _masses);
                var mass = _masses.Get(vessel.Id);
                if (mass is not null) rb.Body.mass = (float)Math.Max(mass.MassKg, 1.0);
            }
        }

        private void CollectRigidBodies(PhysicsBubble bubble, List<RigidVesselBody> output)
        {
            output.Clear();
            var sceneOpt = _host.TryGetPhysicsScene(bubble.Id);
            if (!sceneOpt.HasValue) return;
            foreach (var root in sceneOpt.Value.GetRootGameObjects())
            {
                foreach (var body in root.GetComponentsInChildren<RigidVesselBody>(includeInactive: true))
                {
                    if (bubble.Contains(body.VesselId)) output.Add(body);
                }
            }
        }

        private static Vector3d ToVector3d(Vector3 v) => new(v.x, v.y, v.z);
        private static Vector3 ToUnity(Vector3d v) => new((float)v.X, (float)v.Y, (float)v.Z);
    }
}