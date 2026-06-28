#nullable enable annotations

using System;
using KSPClone.SimCore;

namespace KSPClone.SimCore
{
    /// <summary>
    /// Hosts the auto-limit behaviour for an active warp (TIME-4):
    /// when the warp reaches the earliest global POI it halts the
    /// FSM, resets Rate to 1.0, and fires the warp-commit hook so
    /// persistence (T22) can durably store the endpoint.
    /// </summary>
    public sealed class WarpAutoLimit
    {
        public double? EndGameTime { get; private set; }
        public bool HasFired { get; private set; }

        public event Action<double>? WarpCommitted;

        private readonly SimWorld _world;
        private readonly PoiRegistry _pois;
        private readonly WarpStateMachine _fsm;

        public WarpAutoLimit(SimWorld world, PoiRegistry pois, WarpStateMachine fsm)
        {
            _world = world;
            _pois = pois;
            _fsm = fsm;
        }

        /// <summary>
        /// Called once when the warp transitions to Active, after
        /// T18 has refreshed the POI registry. Picks the earliest
        /// future POI as the auto-limit target.
        /// </summary>
        public void Arm()
        {
            HasFired = false;
            var now = _world.Clock.GameTimeSeconds;
            var earliest = _pois.EarliestAfter(now);
            EndGameTime = earliest?.GameTime;
        }

        /// <summary>
        /// Called every tick while the FSM is Active. If the clock has
        /// reached the end time, halt the FSM exactly at that time
        /// (never past it) and fire WarpCommitted.
        /// </summary>
        public void Tick()
        {
            if (HasFired) return;
            if (_fsm.State != WarpState.Active) return;
            if (EndGameTime is not double end) return;

            if (_world.Clock.GameTimeSeconds >= end)
            {
                // The clock may have advanced past the POI during the
                // final tick; clamp to the POI time exactly.
                // We do this by reducing MasterClock.GameTimeSeconds by
                // the overshoot in real-seconds. Since Rate is the
                // multiplier, the overshoot in wall-seconds is
                // (gameTime - end) / Rate.
                var overshootGame = _world.Clock.GameTimeSeconds - end;
                var rate = _world.Clock.Rate;
                if (rate > 0.0 && overshootGame > 0.0)
                {
                    var overshootWall = overshootGame / rate;
                    _world.Clock.Advance(-overshootWall);
                }

                _fsm.Halt(WarpStateMachine.WarpEndReason.AutoLimit);
                HasFired = true;
                WarpCommitted?.Invoke(end);
            }
        }
    }
}