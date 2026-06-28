#nullable enable annotations

using System;

namespace KSPClone.SimCore
{
    /// <summary>
    /// Stable identifier for a <see cref="PhysicsBubble"/>. Generated once
    /// at bubble creation; never reused, never recycled. Referenced by
    /// vessels (<see cref="Vessel.BubbleId"/>), snapshots, and events so
    /// consumers can correlate without walking the registry.
    /// </summary>
    public readonly struct BubbleId : IEquatable<BubbleId>
    {
        public Guid Value { get; }

        public BubbleId(Guid value) => Value = value;

        public static BubbleId New() => new(Guid.NewGuid());

        public bool Equals(BubbleId other) => Value.Equals(other.Value);
        public override bool Equals(object? obj) => obj is BubbleId b && Equals(b);
        public override int GetHashCode() => Value.GetHashCode();
        public override string ToString() => Value.ToString();

        public static bool operator ==(BubbleId a, BubbleId b) => a.Equals(b);
        public static bool operator !=(BubbleId a, BubbleId b) => !a.Equals(b);
    }
}