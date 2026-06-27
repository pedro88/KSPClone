using System.Collections.Generic;

namespace KSPClone.SimCore
{
    /// <summary>
    /// Builds a <see cref="WorldHandshakeMessage"/> from the live
    /// server state. Called on connect for the connecting client.
    /// </summary>
    public sealed class WorldHandshakeBuilder
    {
        private readonly SimWorld _world;

        public WorldHandshakeBuilder(SimWorld world)
        {
            _world = world;
        }

        public WorldHandshakeMessage Build()
        {
            var vessels = new List<HandshakeVessel>(_world.Vessels.Count);
            foreach (var v in _world.Vessels.Values)
                vessels.Add(new HandshakeVessel(v.Id, v.Orbit, v.OnRails));
            return new WorldHandshakeMessage(_world.Clock.GameTimeSeconds, vessels);
        }
    }
}