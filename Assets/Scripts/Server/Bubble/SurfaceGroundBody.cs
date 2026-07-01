#nullable enable annotations

using UnityEngine;
using UnityEngine.SceneManagement;
using KSPClone.SimCore;

namespace KSPClone.Server
{
    /// <summary>
    /// A static ground-plane collider inside a bubble's PhysicsScene — the
    /// server-authoritative surface a landed vessel rests on (M2.5-T02, PHYS-7,
    /// ADR-0018 §1). Modelled as a wide, thick <see cref="BoxCollider"/> whose
    /// top face sits at the surface, normal along the local +Y (radial-up at the
    /// +Y-pole launch site, ADR-0018 §3).
    ///
    /// No <see cref="Rigidbody"/>: PhysX treats it as immovable, so the vessel's
    /// weight is held by the resulting contact. It is created once in the
    /// bubble-local frame at spawn and is NOT re-based on floating-origin shifts
    /// (ADR-0018 §4): a landed craft is the bubble centroid and never drifts, and
    /// once it climbs off the pad the ground is far below and irrelevant.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SurfaceGroundBody : MonoBehaviour
    {
        // Half-extents: a 1 km square pad, 5 m thick. Large enough that the
        // sub-millidegree tilt from Earth's own orbital motion (ADR-0018 §3) is
        // invisible over a demo, thick enough that no plausible touchdown speed
        // tunnels through in one 60 Hz Discrete step.
        private const float HalfWidth = 500f;
        private const float HalfThickness = 5f;

        public BoxCollider Collider { get; private set; } = null!;

        private void Awake()
        {
            Collider = gameObject.AddComponent<BoxCollider>();
            Collider.size = new Vector3(HalfWidth * 2f, HalfThickness * 2f, HalfWidth * 2f);
        }

        /// <summary>
        /// Position the pad so its top face lies at <paramref name="surfaceLocalY"/>
        /// in the bubble-local frame, centred under (<paramref name="localX"/>,
        /// <paramref name="localZ"/>).
        /// </summary>
        public void Place(float localX, float surfaceLocalY, float localZ)
        {
            transform.position = new Vector3(localX, surfaceLocalY - HalfThickness, localZ);
            transform.rotation = Quaternion.identity;
        }
    }

    /// <summary>
    /// Instantiates a <see cref="SurfaceGroundBody"/> in a bubble's Unity scene,
    /// mirroring <see cref="RigidVesselFactory"/>.
    /// </summary>
    public static class SurfaceGroundFactory
    {
        /// <summary>
        /// Create a ground pad in <paramref name="scene"/> whose top face is at
        /// the surface directly beneath a craft resting at
        /// <paramref name="craftLocalPos"/> (bubble-local). The surface is one
        /// <see cref="WorldSeed.PadHalfHeight"/> below the craft's centre.
        /// </summary>
        public static SurfaceGroundBody Create(Scene scene, Vector3 craftLocalPos)
        {
            var go = new GameObject("SurfaceGround");
            SceneManager.MoveGameObjectToScene(go, scene);
            var ground = go.AddComponent<SurfaceGroundBody>();
            ground.Place(craftLocalPos.x, craftLocalPos.y - (float)WorldSeed.PadHalfHeight, craftLocalPos.z);
            return ground;
        }
    }
}
