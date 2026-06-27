namespace KSPClone.SimCore
{
    public sealed class Vessel
    {
        public VesselId Id { get; }
        public Orbit Orbit { get; set; }
        public bool OnRails { get; set; } = true;

        public Vessel(VesselId id, Orbit orbit)
        {
            Id = id;
            Orbit = orbit;
        }
    }
}