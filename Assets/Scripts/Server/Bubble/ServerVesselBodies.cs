#nullable enable annotations

using System;
using System.Collections.Generic;
using UnityEngine;
using KSPClone.SimCore;

namespace KSPClone.Server
{
    /// <summary>
    /// Owns the Unity rigid-body lifecycle for active-physics vessels
    /// (M1-T21). Listens to the engine-agnostic lifecycle events on
    /// <see cref="ServerSimulation"/> and instantiates / destroys a
    /// <see cref="RigidVesselBody"/> in the right bubble scene in lockstep:
    ///
    ///   VesselPromoted / VesselResumed → spawn a body in the bubble's scene
    ///   VesselDemoted  / VesselSuspended → destroy the body
    ///
    /// The body is seeded in the bubble's local frame (world − GlobalOrigin in
    /// doubles, then narrowed — ADR-0012 §6) and given its initial mass from
    /// the simulation's <see cref="ServerSimulation.Masses"/> registry. After
    /// that the <see cref="BubbleIntegrator"/> drives it each tick.
    /// </summary>
    public sealed class ServerVesselBodies : IDisposable
    {
        private readonly ServerSimulation _sim;
        private readonly UnityBubbleHost _host;
        private readonly Dictionary<VesselId, RigidVesselBody> _bodies = new();
        private bool _disposed;

        public ServerVesselBodies(ServerSimulation sim, UnityBubbleHost host)
        {
            _sim = sim ?? throw new ArgumentNullException(nameof(sim));
            _host = host ?? throw new ArgumentNullException(nameof(host));

            _sim.Promotion.VesselPromoted += OnPromoted;
            _sim.Suspension.VesselResumed += OnResumed;
            _sim.Demotion.VesselDemoted += OnDemoted;
            _sim.Suspension.VesselSuspended += OnSuspended;
        }

        public int ActiveBodyCount => _bodies.Count;

        public bool TryGetBody(VesselId id, out RigidVesselBody body) => _bodies.TryGetValue(id, out body!);

        private void OnPromoted(PromotionEvent e)
        {
            if (!_sim.Bubbles.TryGet(e.BubbleId, out var bubble)) return;
            var local = ToUnity(e.WorldPosition - bubble.GlobalOrigin);
            var localVel = ToUnity(e.WorldVelocity);
            Spawn(e.VesselId, e.BubbleId, local, localVel);
        }

        private void OnResumed(SuspendedVesselState snap)
        {
            // Resume restores the vessel into a bubble (existing or freshly
            // created by the suspension controller) and writes its local frame
            // back onto the vessel. Spawn from the vessel's current bubble.
            if (!_sim.World.Vessels.TryGetValue(snap.VesselId, out var v)) return;
            if (v.BubbleId is not { } bid) return;
            Spawn(snap.VesselId, bid, ToUnity(snap.LocalPosition), ToUnity(snap.LocalVelocity));
        }

        private void OnDemoted(DemotionEvent e) => Despawn(e.VesselId);
        private void OnSuspended(SuspendedVesselState snap) => Despawn(snap.VesselId);

        private void Spawn(VesselId vesselId, BubbleId bubbleId, Vector3 localPos, Vector3 localVel)
        {
            var sceneOpt = _host.TryGetScene(bubbleId);
            if (!sceneOpt.HasValue) return;

            // Defensive: a stale body for this vessel must not linger.
            Despawn(vesselId);

            var body = RigidVesselFactory.Create(sceneOpt.Value, vesselId, bubbleId, localPos, localVel);
            var mass = _sim.Masses.Get(vesselId);
            if (mass is not null) body.Body.mass = (float)Math.Max(mass.MassKg, 1.0);
            _bodies[vesselId] = body;
        }

        private void Despawn(VesselId vesselId)
        {
            if (!_bodies.TryGetValue(vesselId, out var body)) return;
            _bodies.Remove(vesselId);
            if (body != null) UnityEngine.Object.Destroy(body.gameObject);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _sim.Promotion.VesselPromoted -= OnPromoted;
            _sim.Suspension.VesselResumed -= OnResumed;
            _sim.Demotion.VesselDemoted -= OnDemoted;
            _sim.Suspension.VesselSuspended -= OnSuspended;
            foreach (var body in _bodies.Values)
                if (body != null) UnityEngine.Object.Destroy(body.gameObject);
            _bodies.Clear();
        }

        private static Vector3 ToUnity(Vector3d v) => new((float)v.X, (float)v.Y, (float)v.Z);
    }
}
