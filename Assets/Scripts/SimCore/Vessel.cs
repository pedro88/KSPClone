namespace KSPClone.SimCore
{
    public sealed class Vessel
    {
        public VesselId Id { get; }
        public bool OnRails { get; set; }

        public Vessel(VesselId id)
        {
            Id = id;
            OnRails = true;
        }
    }
}