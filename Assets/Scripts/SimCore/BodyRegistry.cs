using System.Collections.Generic;

namespace KSPClone.SimCore
{
    /// <summary>
    /// Holds the fixed system of celestial bodies. Lookup by id; provides
    /// world-frame position of any body at a given game-time.
    /// For M0 (T06) bodies are static w.r.t. their parent; in T07 a body
    /// may carry its own <see cref="Orbit"/> around its parent and this
    /// registry propagates that orbit to give the world position.
    /// </summary>
    public sealed class BodyRegistry
    {
        public IReadOnlyDictionary<CelestialBodyId, CelestialBody> Bodies => _bodies;

        private readonly Dictionary<CelestialBodyId, CelestialBody> _bodies;

        public BodyRegistry(IEnumerable<CelestialBody> bodies)
        {
            _bodies = new Dictionary<CelestialBodyId, CelestialBody>();
            foreach (var b in bodies)
                _bodies[b.Id] = b;
        }

        public CelestialBody Get(CelestialBodyId id) => _bodies[id];

        public bool TryGet(CelestialBodyId id, out CelestialBody body) => _bodies.TryGetValue(id, out body!);

        /// <summary>
        /// World-frame position of the body's centre at <paramref name="gameTime"/>.
        /// Root body is fixed at the origin. A child body without an
        /// <see cref="CelestialBody.OrbitAroundParent"/> sits at its parent's
        /// world position. A child with an orbit has it propagated via
        /// <see cref="KeplerPropagator.WorldFrameStateAt"/> and added to the
        /// parent's world position.
        /// </summary>
        public Vector3d WorldPositionOf(CelestialBodyId id, double gameTime)
        {
            var body = _bodies[id];
            if (body.ParentId is not CelestialBodyId parentId)
                return Vector3d.Zero;
            var parentPos = WorldPositionOf(parentId, gameTime);
            if (body.OrbitAroundParent is null)
                return parentPos;
            var (relPos, _, _, _) = KeplerPropagator.WorldFrameStateAt(body.OrbitAroundParent, gameTime, this);
            return parentPos + relPos;
        }

        /// <summary>
        /// Returns the parent chain of <paramref name="id"/>, root first.
        /// </summary>
        public IEnumerable<CelestialBodyId> AncestorsOf(CelestialBodyId id)
        {
            var body = _bodies[id];
            while (body.ParentId is CelestialBodyId pid)
            {
                yield return pid;
                body = _bodies[pid];
            }
        }
    }
}