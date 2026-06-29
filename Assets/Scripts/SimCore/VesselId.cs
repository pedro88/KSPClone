#nullable enable annotations

using System;

namespace KSPClone.SimCore
{
    public readonly struct VesselId : IEquatable<VesselId>
    {
        public Guid Value { get; }

        public VesselId(Guid value) => Value = value;

        public static VesselId New() => new(Guid.NewGuid());

        public bool Equals(VesselId other) => Value.Equals(other.Value);
        public override bool Equals(object? obj) => obj is VesselId v && Equals(v);
        public override int GetHashCode() => Value.GetHashCode();
        public override string ToString() => Value.ToString();

        public static bool operator ==(VesselId a, VesselId b) => a.Equals(b);
        public static bool operator !=(VesselId a, VesselId b) => !a.Equals(b);
    }
}
