#nullable enable annotations

using System;

namespace KSPClone.Construction
{
    /// <summary>Stable identifier of a <see cref="Design"/> (UUID, server-assigned).</summary>
    public readonly struct DesignId : IEquatable<DesignId>
    {
        public readonly Guid Value;
        public DesignId(Guid value) => Value = value;
        public static DesignId New() => new(Guid.NewGuid());
        public bool Equals(DesignId other) => Value.Equals(other.Value);
        public override bool Equals(object? obj) => obj is DesignId d && Equals(d);
        public override int GetHashCode() => Value.GetHashCode();
        public override string ToString() => Value.ToString("N")[..8];
    }

    /// <summary>
    /// Stable identifier of a <see cref="PartNode"/> within one Design. Monotonic
    /// per Design and assigned by the server (M3-T03) — never an index or object
    /// ref, so edit ops and subtree locks address nodes durably across edits.
    /// </summary>
    public readonly struct NodeId : IEquatable<NodeId>
    {
        public readonly long Value;
        public NodeId(long value) => Value = value;
        public static readonly NodeId None = new(0);
        public bool IsNone => Value == 0;
        public bool Equals(NodeId other) => Value == other.Value;
        public override bool Equals(object? obj) => obj is NodeId n && Equals(n);
        public override int GetHashCode() => Value.GetHashCode();
        public override string ToString() => $"node#{Value}";
    }

    /// <summary>Identifier of a part *type* in the <see cref="PartCatalog"/> (e.g. "command-pod").</summary>
    public readonly struct PartTypeId : IEquatable<PartTypeId>
    {
        public readonly string Value;
        public PartTypeId(string value) => Value = value ?? throw new ArgumentNullException(nameof(value));
        public bool Equals(PartTypeId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object? obj) => obj is PartTypeId p && Equals(p);
        public override int GetHashCode() => Value is null ? 0 : StringComparer.Ordinal.GetHashCode(Value);
        public override string ToString() => Value ?? "<null>";
    }
}
