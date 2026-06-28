#nullable enable annotations

using System;
using System.Collections.Generic;

namespace KSPClone.SimCore
{
    /// <summary>
    /// Applies SOI crossings to vessels at the master-clock boundary:
    /// re-parents the vessel's orbit to the new SOI body by re-deriving
    /// classical elements from the (position, velocity) at the crossing
    /// time, expressed in the new parent's inertial frame.
    /// </summary>
    public sealed class SoiTransition
    {
        public IReadOnlyCollection<(VesselId vessel, Poi poi)> Applied => _applied;

        private readonly SimWorld _world;
        private readonly BodyRegistry _bodies;
        private readonly PoiRegistry _pois;
        private readonly List<(VesselId, Poi)> _applied = new();

        public event Action<VesselId, Poi>? VesselReParented;

        public SoiTransition(SimWorld world, BodyRegistry bodies, PoiRegistry pois)
        {
            _world = world;
            _bodies = bodies;
            _pois = pois;
        }

        public void ApplyDue(double gameTime)
        {
            // Collect due POIs first to avoid mutating the registry mid-iter.
            var due = new List<Poi>();
            foreach (var p in _pois.All)
                if (p.GameTime <= gameTime) due.Add(p);
                else break; // sorted

            foreach (var poi in due)
            {
                if (!_world.Vessels.TryGetValue(poi.VesselId, out var vessel)) { _pois.Remove(poi); continue; }
                if (!vessel.OnRails) { _pois.Remove(poi); continue; }

                var newParent = _bodies.Get(poi.ToBody);

                // State at the exact crossing time, in old parent frame.
                var (oldRelPos, oldRelVel, _, _) = KeplerPropagator.WorldFrameStateAt(vessel.Orbit, poi.GameTime, _bodies);
                var oldParentWorld = _bodies.WorldPositionOf(vessel.Orbit.ParentBody, poi.GameTime);
                var newParentWorld = _bodies.WorldPositionOf(newParent.Id, poi.GameTime);

                // Re-express in new parent's frame.
                var newRelPos = oldRelPos + (oldParentWorld - newParentWorld);
                // Drop parent-body velocity for M0 (static tree in T06; bodies at origin).
                var newRelVel = oldRelVel;

                // Convert to new elements about the new parent.
                var newOrbit = StateVectorToOrbit.Convert(
                    newRelPos, newRelVel, newParent.GravParameterMu,
                    gameTime: poi.GameTime, parentBody: newParent.Id);

                vessel.Orbit = newOrbit;
                _pois.Remove(poi);
                _applied.Add((vessel.Id, poi));
                VesselReParented?.Invoke(vessel.Id, poi);
            }
        }
    }
}