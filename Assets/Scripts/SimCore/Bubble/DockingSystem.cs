#nullable enable annotations

using System;
using System.Collections.Generic;

namespace KSPClone.SimCore
{
    /// <summary>
    /// One docking port on a vessel (PHYS-5). A port is an articulation
    /// point with a capture frame: position and axis in the vessel's
    /// local frame. Two ports on two distinct vessels in the same
    /// bubble are within capture tolerance iff relative distance,
    /// axis misalignment, and closing speed all stay under their
    /// thresholds (<see cref="DockingSystem.CaptureDistanceMeters"/>,
    /// <see cref="DockingSystem.CaptureAxisDotMin"/>,
    /// <see cref="DockingSystem.CaptureClosingSpeedMps"/>).
    /// </summary>
    public sealed class DockingPort
    {
        public string Name { get; }
        public Vector3d LocalPosition { get; }
        public Vector3d LocalAxis { get; } // unit vector

        public DockingPort(string name, Vector3d localPosition, Vector3d localAxis)
        {
            Name = name;
            LocalPosition = localPosition;
            LocalAxis = localAxis.Normalized();
        }
    }

    /// <summary>
    /// Per-vessel list of <see cref="DockingPort"/>. Populated by the
    /// design layer (BUILD-4) when a Design is instantiated; the M1
    /// demo seeds a port on each seeded vessel so docking can be
    /// exercised without the part tree.
    /// </summary>
    public sealed class VesselPortRegistry
    {
        private readonly Dictionary<VesselId, List<DockingPort>> _byVessel = new();

        public IReadOnlyList<DockingPort>? PortsFor(VesselId id)
            => _byVessel.TryGetValue(id, out var list) ? list : null;

        public void Set(VesselId id, IEnumerable<DockingPort> ports)
            => _byVessel[id] = new List<DockingPort>(ports);
    }

    /// <summary>
    /// Discrete event: two docking ports have latched inside the
    /// capture tolerance (PHYS-5, ADR-0013 §5). One event per latch,
    /// reliable-ordered.
    /// </summary>
    public readonly struct DockLatchedEvent
    {
        public VesselId VesselA { get; }
        public VesselId VesselB { get; }
        public string PortA { get; }
        public string PortB { get; }

        public DockLatchedEvent(VesselId vesselA, VesselId vesselB, string portA, string portB)
        {
            VesselA = vesselA;
            VesselB = vesselB;
            PortA = portA;
            PortB = portB;
        }
    }

    /// <summary>
    /// Docking detector (M1-T17). Each tick scans every pair of distinct
    /// vessels in the same bubble and every pair of their docking ports.
    /// When the relative distance, axis dot product (alignment), and
    /// closing speed all clear their thresholds, emits one
    /// <see cref="DockLatchedEvent"/> for the pair and marks the ports
    /// consumed so the event fires exactly once.
    ///
    /// Both vessels must already share a bubble before the latch (this
    /// is guaranteed by M1-T02: bubbles merge at R_phys = 2.5 km, the
    /// capture distance is much smaller, ~10 cm). That means docking
    /// needs no authority handoff (M1-T19) — both vessels were already
    /// in one server-side scene before contact.
    /// </summary>
    public sealed class DockingSystem
    {
        public double CaptureDistanceMeters { get; }
        public double CaptureAxisDotMin { get; }
        public double CaptureClosingSpeedMps { get; }

        public event Action<DockLatchedEvent>? DockLatched;

        private readonly SimWorld _world;
        private readonly VesselPortRegistry _ports;
        private readonly HashSet<(VesselId, VesselId)> _latchedPairs = new();

        public DockingSystem(
            SimWorld world,
            VesselPortRegistry ports,
            double captureDistanceMeters = 0.10,
            double captureAxisDotMin = 0.95,
            double captureClosingSpeedMps = 0.5)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _ports = ports ?? throw new ArgumentNullException(nameof(ports));
            CaptureDistanceMeters = captureDistanceMeters;
            CaptureAxisDotMin = captureAxisDotMin;
            CaptureClosingSpeedMps = captureClosingSpeedMps;
        }

        /// <summary>
        /// Scan every vessel pair that shares a bubble. Returns the
        /// number of new latches this tick.
        /// </summary>
        public int RunLatchPass()
        {
            int latched = 0;
            // Group vessels by bubble id; pairs only considered within a group.
            var byBubble = new Dictionary<BubbleId, List<Vessel>>();
            foreach (var v in _world.Vessels.Values)
            {
                if (v.State != VesselState.ActivePhysics) continue;
                if (v.BubbleId is not { } bid) continue;
                if (!byBubble.TryGetValue(bid, out var bucket))
                {
                    bucket = new List<Vessel>();
                    byBubble[bid] = bucket;
                }
                bucket.Add(v);
            }

            foreach (var bucket in byBubble.Values)
            {
                for (int i = 0; i < bucket.Count; i++)
                {
                    for (int j = i + 1; j < bucket.Count; j++)
                    {
                        if (TryLatchPair(bucket[i], bucket[j])) latched++;
                    }
                }
            }
            return latched;
        }

        private bool TryLatchPair(Vessel a, Vessel b)
        {
            // Normalise the latched-pair key so (A,B) and (B,A) refer to the same pair.
            var key = a.Id.Value.CompareTo(b.Id.Value) < 0
                ? (a.Id, b.Id)
                : (b.Id, a.Id);
            if (_latchedPairs.Contains(key)) return false;

            var portsA = _ports.PortsFor(a.Id);
            var portsB = _ports.PortsFor(b.Id);
            if (portsA is null || portsB is null || portsA.Count == 0 || portsB.Count == 0)
                return false;

            foreach (var pa in portsA)
            {
                foreach (var pb in portsB)
                {
                    if (PortsInTolerance(a, pa, b, pb))
                    {
                        _latchedPairs.Add(key);
                        DockLatched?.Invoke(new DockLatchedEvent(a.Id, b.Id, pa.Name, pb.Name));
                        return true;
                    }
                }
            }
            return false;
        }

        private bool PortsInTolerance(Vessel a, DockingPort pa, Vessel b, DockingPort pb)
        {
            if (!a.CachedLocalPosition.HasValue || !b.CachedLocalPosition.HasValue) return false;
            // Note: in M1 we compare ports in world frame (the world positions are
            // available via CachedWorldPosition on the host side; here we fall back
            // to local assuming the bubble origin is similar — a future slice
            // unifies via CachedWorldPosition).
            var aWorld = a.CachedWorldPosition ?? a.CachedLocalPosition!.Value;
            var bWorld = b.CachedWorldPosition ?? b.CachedLocalPosition!.Value;
            var relPos = bWorld - aWorld;
            if (relPos.Length > CaptureDistanceMeters) return false;

            // Axis alignment: pa.LocalAxis must point toward pb.LocalAxis
            // (their dot product should be ≤ -CaptureAxisDotMin).
            var axisDot = Vector3d.Dot(pa.LocalAxis, pb.LocalAxis);
            if (axisDot > -CaptureAxisDotMin) return false;

            // Closing speed: relative velocity along the contact normal.
            if (!a.CachedWorldVelocity.HasValue || !b.CachedWorldVelocity.HasValue) return false;
            var relVel = b.CachedWorldVelocity.Value - a.CachedWorldVelocity.Value;
            var normalUnit = relPos.Normalized();
            var closing = -Vector3d.Dot(relVel, normalUnit); // positive when approaching
            if (closing > CaptureClosingSpeedMps) return false;

            return true;
        }
    }
}