#nullable enable annotations

using System.Collections.Generic;
using UnityEngine;
using KSPClone.SimCore;

namespace KSPClone.Client
{
    /// <summary>
    /// Renders the Sun, Earth, and Moon as billboards inside an inverted-sphere
    /// skybox shell parented to the *controlled vessel* (M2.5-T01). The shell
    /// rides the same float-local frame as <see cref="ClientWorldRenderer"/>,
    /// so the bodies' true world positions can be ~1.5e11 m (Sun) without
    /// breaking float32 — only their *direction* is material.
    ///
    /// Apparent angular size policy (Slice 2.5.2): real atan(R/D), with a
    /// configurable minimum (default ~2°) so distant bodies never collapse to
    /// a single pixel. The sim core never reads visual apparent size.
    /// </summary>
    public sealed class CelestialSkyboxRenderer
    {
        // Radius of the inverted-sphere shell in render-local metres. Bodies
        // sit on this shell; their apparent size is then a function of their
        // mesh radius vs. this shell radius. 100 m is comfortable for the
        // vessel-anchored render frame and keeps the shell well inside the
        // far clip while the camera orbits at ~3–400 m.
        private const float ShellRadius = 100f;

        // Apparent angular floor (radians). ~2° = 35 mrad.
        private const float MinAngularRadiusRad = 0.035f;

        // Apparent angular ceiling. The skybox is a *sky* layer: bodies live on
        // a shell at ShellRadius and their mesh radius is ShellRadius·tan(θ). If
        // θ approaches 90° (which it does the instant the camera nears a body's
        // surface — distance → radius → atan(1) = 45°, and worse below) the mesh
        // grows to engulf the camera and you end up *inside* a giant sphere. Cap
        // θ so tan(θ) ≤ 0.4, i.e. the mesh radius never exceeds 0.4·ShellRadius.
        private const float MaxAngularRadiusRad = 0.3805f; // atan(0.4)

        // Skip the body you're standing on only for the first ~15 km of altitude
        // (distance ≤ radius · this factor): near the pad the ground grid is the
        // surface, and the ZTest-Always billboard would paint a "marble" over it.
        // Past ~15 km the grid has receded, so the body reappears as the globe —
        // handing off with no empty gap. (R+15 km)/R ≈ 1.00235 for Earth. A real
        // curved horizon/terrain needs scaled space (kept out of the flat render
        // frame by ADR-0015) and is a future slice.
        private const float SurfaceSkipFactor = 1.00235f;

        // Glow shell sits behind the body disk at a larger radius. Drawn with
        // alpha so it fades into the sky.
        private const float GlowRadiusFactor = 2.4f;

        private Transform? _camera; // resolved lazily — Camera.main can be null at Start
        private GameObject? _shell;
        private readonly Dictionary<CelestialBodyId, BodyVisual> _bodies = new();

        public CelestialSkyboxRenderer(Transform? camera = null)
        {
            _camera = camera;
        }

        /// <summary>
        /// Place each body on the skybox shell, sized to its apparent angular
        /// radius from <paramref name="cameraWorldPos"/>. Sun direction is
        /// passed to the Moon shader so its unlit hemisphere dims. The shell
        /// is re-centred on the camera each frame so it rides the float-local
        /// render frame (ADR-0015).
        /// </summary>
        public void Render(
            BodyRegistry registry,
            Vector3d cameraWorldPos,
            double gameTime,
            Vector3d sunDirectionWorld)
        {
            // Camera.main may not have existed when this renderer was built;
            // resolve it here so the shell comes alive once it does (matches
            // ClientWorldRenderer). Without this the skybox stays dark forever.
            _camera ??= Camera.main != null ? Camera.main.transform : null;
            if (_camera == null) return;
            EnsureShell();
            EnsureBodies();

            // Re-centre the shell on the camera every frame — the camera rides
            // the vessel's float-local frame (ADR-0015), so the shell follows.
            _shell!.transform.position = _camera.position;

            foreach (var kv in _bodies)
            {
                var bodyWorldPos = registry.WorldPositionOf(kv.Key, gameTime);
                var toBody = bodyWorldPos - cameraWorldPos;
                double distance = toBody.Length;

                // Skip the body you're standing on: at/near the surface it is
                // ground, not sky, and a shell billboard would engulf the camera.
                double physicalRadius = PhysicalRadiusOf(kv.Key);
                if (distance <= physicalRadius * SurfaceSkipFactor || distance < 1.0)
                {
                    kv.Value.Disk.SetActive(false);
                    kv.Value.Glow?.SetActive(false);
                    continue;
                }
                kv.Value.Disk.SetActive(true);
                kv.Value.Glow?.SetActive(true);

                var dir = new Vector3((float)(toBody.X / distance), (float)(toBody.Y / distance), (float)(toBody.Z / distance));
                double realAngularRadius = ComputeRealAngularRadius(kv.Key, distance);
                float apparentAngularRadius = Mathf.Clamp(
                    (float)realAngularRadius, MinAngularRadiusRad, MaxAngularRadiusRad);

                // Mesh radius (metres on the shell) from apparent angular size:
                //   meshRadius = ShellRadius * tan(apparentAngularRadius)
                float meshRadius = ShellRadius * Mathf.Tan(apparentAngularRadius);
                kv.Value.Disk.transform.localPosition = dir * ShellRadius;
                kv.Value.Disk.transform.localScale = Vector3.one * (meshRadius * 2f);
                kv.Value.Disk.transform.rotation = Quaternion.LookRotation(dir);

                // Glow halo (sun only) at a larger radius behind the disk.
                if (kv.Value.Glow != null)
                {
                    float glowRadius = meshRadius * GlowRadiusFactor;
                    kv.Value.Glow.transform.localPosition = dir * (ShellRadius - 0.01f);
                    kv.Value.Glow.transform.localScale = Vector3.one * (glowRadius * 2f);
                    kv.Value.Glow.transform.rotation = Quaternion.LookRotation(dir);
                }
            }

            // Pass sun direction (world, normalised) into the Moon shader for
            // day/night shading. The shader does the lit-vs-dark dot product in
            // world space, so we hand it the world-space direction directly.
            if (_bodies.TryGetValue(CelestialBodyId.Moon, out var moon))
            {
                var mr = moon.Disk.GetComponent<Renderer>();
                if (mr != null && mr.sharedMaterial != null)
                {
                    // Each body owns a unique material (MakeMaterial news one per
                    // body), so writing sharedMaterial is safe and avoids the
                    // per-renderer copy that reading `.material` would allocate.
                    var sun = new Vector3((float)sunDirectionWorld.X, (float)sunDirectionWorld.Y, (float)sunDirectionWorld.Z);
                    mr.sharedMaterial.SetVector("_SunDir",
                        new Vector4(sun.x, sun.y, sun.z, 0));
                }
            }
        }

        // Real angular radius from camera to body centre, given its physical
        // radius and straight-line distance. Falls back to a generous cap when
        // the body has no finite radius (Sun has +∞ SOI in the seed).
        private static double ComputeRealAngularRadius(CelestialBodyId id, double distance)
        {
            double physicalRadius = PhysicalRadiusOf(id);
            if (physicalRadius <= 0.0 || distance <= 0.0) return 0.0;
            return System.Math.Atan(physicalRadius / distance);
        }

        // Physical body radii (metres). Presentation-only sizing; the sim core
        // never reads these (bodies carry SOI radii, not draw radii).
        private static double PhysicalRadiusOf(CelestialBodyId id) => id switch
        {
            CelestialBodyId.Sun    => 6.957e8, // 695.7 Mm
            CelestialBodyId.Planet => 6.371e6, // 6.371 Mm
            CelestialBodyId.Moon   => 1.737e6, // 1.737 Mm
            _ => 0.0,
        };

        private void EnsureShell()
        {
            if (_shell != null) return;
            // Pure anchor transform (scale 1, no mesh). It only carries the
            // float-local frame so the bodies ride the camera. A *scaled*
            // primitive here would multiply every child's localPosition and
            // localScale by ShellRadius*2 — bodies flew to ~20 km and clipped
            // out of the frustum, so the sky read as empty. ShellRadius is
            // applied per-body in unscaled metres instead.
            _shell = new GameObject("CelestialSkyboxShell");
            _shell.transform.position = Vector3.zero;
        }

        private void EnsureBodies()
        {
            EnsureBody(CelestialBodyId.Sun,   new Color(1.0f, 0.85f, 0.35f, 1f), glow: true);
            EnsureBody(CelestialBodyId.Planet, new Color(0.20f, 0.45f, 0.80f, 1f), glow: false);
            EnsureBody(CelestialBodyId.Moon,  new Color(0.65f, 0.65f, 0.65f, 1f), glow: false);
        }

        private void EnsureBody(CelestialBodyId id, Color color, bool glow)
        {
            if (_bodies.ContainsKey(id)) return;

            var disk = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            disk.name = $"CelestialBody_{id}";
            Object.Destroy(disk.GetComponent<Collider>());
            var rend = disk.GetComponent<Renderer>();
            rend.sharedMaterial = MakeMaterial(color, emission: glow);
            disk.transform.SetParent(_shell!.transform, worldPositionStays: false);
            _bodies[id] = new BodyVisual { Disk = disk, Glow = null };

            if (glow)
            {
                var halo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                halo.name = $"CelestialGlow_{id}";
                Object.Destroy(halo.GetComponent<Collider>());
                var hr = halo.GetComponent<Renderer>();
                hr.sharedMaterial = MakeMaterial(new Color(1f, 0.6f, 0.15f, 0.18f), emission: false);
                halo.transform.SetParent(_shell.transform, worldPositionStays: false);
                _bodies[id].Glow = halo;
            }
        }

        private static Material MakeMaterial(Color color, bool emission)
        {
            var sh = Shader.Find("KSPClone/CelestialBody");
            var mat = new Material(sh);
            mat.color = color;
            if (emission)
                mat.SetColor("_Emission", color * 0.5f);
            return mat;
        }

        public void Clear()
        {
            foreach (var b in _bodies.Values)
            {
                if (b.Disk != null) Object.Destroy(b.Disk);
                if (b.Glow != null) Object.Destroy(b.Glow);
            }
            _bodies.Clear();
            if (_shell != null) { Object.Destroy(_shell); _shell = null; }
        }

        private sealed class BodyVisual
        {
            public GameObject Disk = null!;
            public GameObject? Glow;
        }
    }
}