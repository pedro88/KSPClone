#nullable enable annotations

using System;
using System.Collections.Generic;

namespace KSPClone.SimCore
{
    /// <summary>
    /// One structural joint on a vessel (PHYS-6, ADR-0005). Holds the
    /// vessel together until the load across it exceeds the configured
    /// break thresholds. M1 ships a scalar break-force + break-torque;
    /// full 6-DOF load tensors land with the part tree (M3).
    ///
    /// A joint is "broken" exactly once: the
    /// <see cref="StructuralFailureSystem"/> emits one
    /// <see cref="StructuralFailureEvent"/> per joint crossing the
    /// threshold, then the joint is removed.
    /// </summary>
    public sealed class StructuralJoint
    {
        public string Name { get; }
        public double BreakForceNewtons { get; }
        public double BreakTorqueNm { get; }

        public StructuralJoint(string name, double breakForceNewtons, double breakTorqueNm)
        {
            Name = name;
            BreakForceNewtons = breakForceNewtons;
            BreakTorqueNm = breakTorqueNm;
        }

        public bool ExceedsForce(double currentLoadNewtons) =>
            BreakForceNewtons > 0.0 && currentLoadNewtons > BreakForceNewtons;

        public bool ExceedsTorque(double currentTorqueNm) =>
            BreakTorqueNm > 0.0 && currentTorqueNm > BreakTorqueNm;
    }

    /// <summary>
    /// Per-vessel list of <see cref="StructuralJoint"/>. Populated by
    /// the design layer (BUILD-4) once a part tree is available; for
    /// M1 the server seeds a single mid-stage decoupler by default.
    /// </summary>
    public sealed class VesselJointRegistry
    {
        private readonly Dictionary<VesselId, List<StructuralJoint>> _byVessel = new();

        public IReadOnlyList<StructuralJoint>? JointsFor(VesselId id)
            => _byVessel.TryGetValue(id, out var list) ? list : null;

        public void Set(VesselId id, IEnumerable<StructuralJoint> joints)
            => _byVessel[id] = new List<StructuralJoint>(joints);

        public void Clear(VesselId id) => _byVessel.Remove(id);

        /// <summary>
        /// Drop a single joint (the caller identified which by name).
        /// Returns true if a joint was removed.
        /// </summary>
        public bool RemoveJoint(VesselId id, string jointName)
        {
            if (!_byVessel.TryGetValue(id, out var list)) return false;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].Name == jointName)
                {
                    list.RemoveAt(i);
                    return true;
                }
            }
            return false;
        }
    }

    /// <summary>
    /// Surface event when a joint breaks (PHYS-6, ADR-0013 §5). Emitted
    /// exactly once per joint crossing its threshold; the failure is
    /// discrete — no soft flex, no intermediate frames (acceptance
    /// criterion in M1-T09).
    /// </summary>
    public readonly struct StructuralFailureEvent
    {
        public VesselId VesselId { get; }
        public string JointName { get; }
        public double BreakingForceNewtons { get; }
        public double BreakingTorqueNm { get; }
        public VesselId NewVesselId { get; }

        public StructuralFailureEvent(VesselId vesselId, string jointName, double breakingForce, double breakingTorque, VesselId newVesselId)
        {
            VesselId = vesselId;
            JointName = jointName;
            BreakingForceNewtons = breakingForce;
            BreakingTorqueNm = breakingTorque;
            NewVesselId = newVesselId;
        }
    }

    /// <summary>
    /// Per-tick scan that checks the load across every joint on every
    /// active vessel and emits a <see cref="StructuralFailureEvent"/>
    /// when a threshold is exceeded.
    ///
    /// M1 simplification: the load on each joint is whatever value the
    /// caller passes via <see cref="ReportLoad"/>. The full load tensor
    /// (force + torque at the joint, summed across attached parts)
    /// is computed by the rigidbody integrator with the part tree.
    /// Today the caller reports a single scalar force and a single
    /// torque; the system only breaks joints whose thresholds are
    /// exceeded, exactly once per joint per vessel lifetime.
    /// </summary>
    public sealed class StructuralFailureSystem
    {
        public event Action<StructuralFailureEvent>? JointBroken;

        private readonly SimWorld _world;
        private readonly VesselJointRegistry _joints;

        public StructuralFailureSystem(SimWorld world, VesselJointRegistry joints)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _joints = joints ?? throw new ArgumentNullException(nameof(joints));
        }

        /// <summary>
        /// Report a load (force, torque) across a specific joint on a
        /// vessel. Returns true if the joint broke this call (i.e. the
        /// joint is removed and an event is emitted).
        /// </summary>
        public bool ReportLoad(VesselId vesselId, string jointName, double forceNewtons, double torqueNm)
        {
            var list = _joints.JointsFor(vesselId);
            if (list is null) return false;
            StructuralJoint? joint = null;
            foreach (var j in list)
                if (j.Name == jointName) { joint = j; break; }
            if (joint is null) return false;

            if (!joint.ExceedsForce(forceNewtons) && !joint.ExceedsTorque(torqueNm)) return false;

            var newVesselId = VesselId.New();
            _joints.RemoveJoint(vesselId, jointName);
            JointBroken?.Invoke(new StructuralFailureEvent(vesselId, jointName, forceNewtons, torqueNm, newVesselId));
            return true;
        }
    }
}