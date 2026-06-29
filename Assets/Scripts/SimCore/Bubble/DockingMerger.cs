#nullable enable annotations

using System;

namespace KSPClone.SimCore
{
    /// <summary>
    /// Emitted when two latched vessels are merged into a single rigid
    /// vessel (PHYS-5, ADR-0013 §5). The original vessel ids are
    /// preserved in <see cref="AbsorbedVesselId"/> so clients can
    /// reconcile their replicated state.
    /// </summary>
    public readonly struct DockMergedEvent
    {
        public VesselId SurvivingVesselId { get; }
        public VesselId AbsorbedVesselId { get; }
        public double CombinedMassKg { get; }
        public Vector3d CombinedVelocity { get; }
        public Vector3d CombinedAngularVelocity { get; }

        public DockMergedEvent(VesselId survivingVesselId, VesselId absorbedVesselId, double combinedMassKg, Vector3d combinedVelocity, Vector3d combinedAngularVelocity)
        {
            SurvivingVesselId = survivingVesselId;
            AbsorbedVesselId = absorbedVesselId;
            CombinedMassKg = combinedMassKg;
            CombinedVelocity = combinedVelocity;
            CombinedAngularVelocity = combinedAngularVelocity;
        }
    }

    /// <summary>
    /// Consumes a <see cref="DockLatchedEvent"/> and merges the two
    /// vessels into one rigid vessel within the same bubble (M1-T18).
    ///
    /// Conservation laws:
    ///  - Total linear momentum: m_a·v_a + m_b·v_b = (m_a + m_b)·v_combined
    ///  - Total mass: m_a + m_b
    ///  - Inertia tensor: principal-moment sum (M1 ships diagonal
    ///    principal moments; future slices use the full 3×3 tensor
    ///    with parallel-axis adjustments)
    ///  - Vessel id: the heavier vessel survives; the lighter is
    ///    absorbed (its id is removed from the bubble and the world).
    /// </summary>
    public sealed class DockingMerger
    {
        public event Action<DockMergedEvent>? VesselsMerged;

        private readonly SimWorld _world;
        private readonly BubbleRegistry _registry;
        private readonly VesselMassRegistry _masses;

        public DockingMerger(SimWorld world, BubbleRegistry registry, VesselMassRegistry masses)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _masses = masses ?? throw new ArgumentNullException(nameof(masses));
        }

        /// <summary>
        /// Merge the two vessels from the latch event into one rigid
        /// vessel. Returns false if either vessel id is unknown or if
        /// either lacks a mass entry.
        /// </summary>
        public bool MergeFromLatch(DockLatchedEvent latch)
        {
            return Merge(latch.VesselA, latch.VesselB);
        }

        public bool Merge(VesselId aId, VesselId bId)
        {
            if (!_world.Vessels.TryGetValue(aId, out var a)) return false;
            if (!_world.Vessels.TryGetValue(bId, out var b)) return false;
            if (a.State != VesselState.ActivePhysics || b.State != VesselState.ActivePhysics) return false;
            if (a.BubbleId is null || !a.BubbleId.Equals(b.BubbleId)) return false;

            var massA = _masses.Get(aId);
            var massB = _masses.Get(bId);
            if (massA is null || massB is null) return false;

            // Heavier vessel survives.
            Vessel survivor = massA.MassKg >= massB.MassKg ? a : b;
            Vessel absorbed = survivor == a ? b : a;
            var survivorMassEntry = survivor == a ? massA : massB;
            var absorbedMassEntry = absorbed == a ? massA : massB;

            var combinedMass = massA.MassKg + massB.MassKg;

            var vA = a.CachedWorldVelocity!.Value;
            var vB = b.CachedWorldVelocity!.Value;

            // p_total = m_a·v_a + m_b·v_b
            var momentum = vA * massA.MassKg + vB * massB.MassKg;
            var combinedVelocity = momentum / combinedMass;

            // Inertia: principal moments sum (M1 simplification). Capture the
            // combined moments in locals BEFORE writing them back — the
            // survivor mass entry aliases massA or massB, so mutating it first
            // would corrupt the angular-momentum sum below.
            var combinedInertiaX = massA.InertiaPrincipalX + massB.InertiaPrincipalX;
            var combinedInertiaY = massA.InertiaPrincipalY + massB.InertiaPrincipalY;
            var combinedInertiaZ = massA.InertiaPrincipalZ + massB.InertiaPrincipalZ;

            // Conserve total angular momentum about the combined COM (PHYS-5,
            // M1-T18 step 2). About C, the orbital term of the combined body
            // vanishes, so L_total = I_combined·ω_combined. Each input
            // contributes its own spin (I_i·ω_i) plus the orbital term of its
            // COM relative to C: m_i·(r_i − r_C)×v_i. With diagonal principal
            // moments, I·ω and I⁻¹·L are component-wise (M1 simplification,
            // matching the inertia-sum above).
            var posA = a.CachedWorldPosition!.Value;
            var posB = b.CachedWorldPosition!.Value;
            var com = (posA * massA.MassKg + posB * massB.MassKg) / combinedMass;
            var wA = a.CachedAngularVelocity ?? Vector3d.Zero;
            var wB = b.CachedAngularVelocity ?? Vector3d.Zero;
            var spin = new Vector3d(
                massA.InertiaPrincipalX * wA.X + massB.InertiaPrincipalX * wB.X,
                massA.InertiaPrincipalY * wA.Y + massB.InertiaPrincipalY * wB.Y,
                massA.InertiaPrincipalZ * wA.Z + massB.InertiaPrincipalZ * wB.Z);
            var orbital = Vector3d.Cross(posA - com, vA) * massA.MassKg
                        + Vector3d.Cross(posB - com, vB) * massB.MassKg;
            var totalL = spin + orbital;
            var combinedAngularVelocity = new Vector3d(
                totalL.X / combinedInertiaX,
                totalL.Y / combinedInertiaY,
                totalL.Z / combinedInertiaZ);

            survivorMassEntry.MassKg = combinedMass;
            survivorMassEntry.InertiaPrincipalX = combinedInertiaX;
            survivorMassEntry.InertiaPrincipalY = combinedInertiaY;
            survivorMassEntry.InertiaPrincipalZ = combinedInertiaZ;

            // Remove the absorbed vessel from the bubble and the world.
            if (survivor.BubbleId is { } bid && _registry.TryGet(bid, out var bubble))
                bubble.Remove(absorbed.Id);
            _masses.Clear(absorbed.Id);
            _world.UnregisterVessel(absorbed.Id);

            // The merged body carries the conserved linear + angular state;
            // its position is the survivor's (no jump — M1-T19).
            survivor.CachedWorldVelocity = combinedVelocity;
            survivor.CachedAngularVelocity = combinedAngularVelocity;

            VesselsMerged?.Invoke(new DockMergedEvent(
                survivor.Id, absorbed.Id, combinedMass, combinedVelocity, combinedAngularVelocity));
            return true;
        }
    }
}