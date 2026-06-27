namespace KSPClone.SimCore
{
    public readonly record struct VesselId(System.Guid Value)
    {
        public static VesselId New() => new(System.Guid.NewGuid());
        public override string ToString() => Value.ToString();
    }
}