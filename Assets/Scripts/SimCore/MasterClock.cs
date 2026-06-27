namespace KSPClone.SimCore
{
    public sealed class MasterClock
    {
        public double GameTimeSeconds { get; private set; }
        public double Rate { get; set; } = 1.0;

        public void Advance(double dtSeconds)
        {
            GameTimeSeconds += dtSeconds * Rate;
        }
    }
}