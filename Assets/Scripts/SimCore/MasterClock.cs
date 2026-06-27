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
    }
}