namespace KSPClone.SimCore
{
    public sealed class MasterClock
    {
        private double _gameTimeSeconds;

        // Read-only from outside: single writer is Advance (Constitution Art. 1).
        public double GameTimeSeconds => _gameTimeSeconds;
        public double Rate { get; set; } = 1.0;

        public void Advance(double dtSeconds)
        {
            _gameTimeSeconds += dtSeconds * Rate;
        }

        /// <summary>
        /// Hard-set the game-time to an exact value. The only legitimate
        /// caller is <see cref="WarpAutoLimit"/> when the warp's final
        /// tick would overshoot the POI target (TIME-4). It is *not* a
        /// generic time machine — every other writer must go through
        /// <see cref="Advance"/>.
        /// </summary>
        public void ClampTo(double gameTimeSeconds)
        {
            _gameTimeSeconds = gameTimeSeconds;
        }
    }
}