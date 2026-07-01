#nullable enable annotations

using UnityEngine;
using KSPClone.SimCore;

namespace KSPClone.Server
{
    /// <summary>
    /// Server-side MonoBehaviour that represents one active vessel as a
    /// single rigid body inside a bubble's PhysicsScene. Created by the
    /// <see cref="RigidVesselFactory"/> when a vessel is promoted or
    /// joins an existing bubble; destroyed on demotion / suspension.
    ///
    /// The transform stores the vessel's position and velocity in the
    /// bubble's *local* frame (i.e. relative to the bubble's floating
    /// origin). The host reads the global position by adding the
    /// bubble's <see cref="PhysicsBubble.GlobalOrigin"/> to the local
    /// transform each tick before publishing it back to the sim core.
    ///
    /// Per ADR-0005 and the M1 spec, a vessel is a single rigid body:
    /// no inter-part flex, no soft joints. Structural failures split
    /// the part tree into separate <see cref="RigidVesselBody"/>
    /// instances within the same bubble.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class RigidVesselBody : MonoBehaviour
    {
        public VesselId VesselId { get; private set; }
        public BubbleId BubbleId { get; private set; }

        public Rigidbody Body { get; private set; } = null!;

        private void Awake()
        {
            Body = gameObject.GetComponent<Rigidbody>() ?? gameObject.AddComponent<Rigidbody>();
            Body.useGravity = false; // gravity is applied manually by the BubbleIntegrator (M1-T06)
            Body.interpolation = RigidbodyInterpolation.None;
            Body.collisionDetectionMode = CollisionDetectionMode.Discrete;

            // Contact hull for surface/ground collision (M2.5-T02, PHYS-7). One
            // capsule approximating the single-rigid-body craft (ADR-0005). Mass
            // and inertia are set explicitly by ServerVesselBodies after spawn,
            // so this collider's presence must not be relied on to derive them.
            var capsule = gameObject.AddComponent<CapsuleCollider>();
            capsule.direction = 1; // Y axis — matches the +Y-up / +Y-thrust convention
            capsule.radius = 1.0f;
            capsule.height = (float)(WorldSeed.PadHalfHeight * 2.0); // 4 m tall
            capsule.center = Vector3.zero;
        }

        /// <summary>
        /// Initialise the rigidbody with the vessel's seed state (local
        /// frame position and velocity). Called by the factory right
        /// after instantiation.
        /// </summary>
        public void Seed(VesselId vesselId, BubbleId bubbleId, Vector3 localPosition, Vector3 localVelocity)
        {
            VesselId = vesselId;
            BubbleId = bubbleId;
            Body.position = localPosition;
            Body.linearVelocity = localVelocity;
            transform.position = localPosition;
        }

        /// <summary>
        /// Apply a translation in the bubble's local frame (used during
        /// floating-origin rebases and merge/split re-basing). Velocities
        /// are untouched — a rebase is a translation, not a boost
        /// (ADR-0012 §6).
        /// </summary>
        public void TranslateLocal(Vector3 deltaLocal)
        {
            Body.position += deltaLocal;
            transform.position += deltaLocal;
        }
    }

    /// <summary>
    /// Factory that instantiates <see cref="RigidVesselBody"/> inside
    /// the bubble's Unity scene. The host calls this on
    /// <see cref="BubbleManager.VesselJoinedBubble"/> or
    /// <see cref="PromotionController.VesselPromoted"/>.
    /// </summary>
    public static class RigidVesselFactory
    {
        public static RigidVesselBody Create(UnityEngine.SceneManagement.Scene scene, VesselId vesselId, BubbleId bubbleId, Vector3 localPosition, Vector3 localVelocity)
        {
            var go = new GameObject($"Vessel_{vesselId}");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, scene);
            var body = go.AddComponent<RigidVesselBody>();
            body.Seed(vesselId, bubbleId, localPosition, localVelocity);
            return body;
        }
    }
}