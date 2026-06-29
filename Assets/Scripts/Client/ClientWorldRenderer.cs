#nullable enable annotations

using System.Collections.Generic;
using UnityEngine;
using KSPClone.SimCore;

namespace KSPClone.Client
{
    /// <summary>
    /// Renders vessels into a single float-local frame anchored on the
    /// controlled vessel (M1-T25, ADR-0015). Each frame the render origin is
    /// re-set to the controlled vessel's predicted world position; every
    /// vessel's Unity transform is (worldDouble − origin) narrowed to float —
    /// so the controlled vessel sits at ≈0 and the camera rides its frame.
    /// No scaled space, no per-bubble origins (those are server-side).
    /// </summary>
    public sealed class ClientWorldRenderer
    {
        private readonly Dictionary<VesselId, GameObject> _objects = new();
        private readonly Transform? _camera;

        // Orbit camera: hold right mouse to swing around the controlled vessel,
        // scroll to zoom. Always looks at the craft. Seeded to a slight
        // top-down-behind framing.
        private float _camYaw;
        private float _camPitch = 15f;
        private float _camDistance = 18f;
        private const float OrbitSpeed = 3f;
        private const float ZoomSpeed = 3f;

        // Static-world reference field ("space dust"): a lattice of markers
        // pinned to fixed world coordinates. The vessel anchors the render
        // origin and the camera rides it, so the controlled craft never moves
        // on screen — but these world-fixed markers stream past as it
        // translates, making speed and direction visible without scaled space.
        // The lattice is grid-snapped to the vessel each frame: a marker that
        // leaves one side reappears on the other, so coverage is seamless and
        // the count stays fixed.
        private readonly List<GameObject> _dust = new();
        private const double DustSpacing = 80.0;            // metres between markers
        private const int DustRadius = 3;                   // (2·R+1)^3 = 343 markers
        private const float DustScale = 0.4f;

        // Exhaust plume under the controlled craft, length ∝ throttle. The seed
        // engine thrusts along the vessel's local +Y, so the plume points −Y
        // ("down" in the render frame — same untumbled assumption as the dust;
        // the client has no authoritative orientation to rotate by yet).
        private GameObject? _flame;

        // Launch-pad ground: a flat plane pinned to the vessel's spawn point in
        // world space, with its normal along the local "up" (away from the
        // planet centre at the world origin). Rendered world-relative, so it
        // sinks away beneath the craft as it climbs — the only ground cue
        // available without terrain or body data on the client.
        private GameObject? _ground;
        private Vector3d? _padWorld;

        public ClientWorldRenderer(Transform? camera = null)
        {
            _camera = camera;
        }

        public void Render(ClientFlightModel flight, double throttle = 0.0)
        {
            if (flight.ControlledVesselId is not { } controlled) return;

            // Controlled vessel: predicted state, anchors the origin → ≈(0,0,0).
            Place(controlled, flight.ToRenderLocal(flight.ControlledState.Position), isControlled: true);

            // Every other vessel: interpolated world position in the same frame.
            foreach (var id in flight.InterpolatedVesselIds)
                if (flight.TrySampleOther(id, out var world))
                    Place(id, flight.ToRenderLocal(world), isControlled: false);

            RenderGround(flight);
            RenderReferenceField(flight);
            RenderThrust(flight, throttle);

            if (_camera != null && _objects.TryGetValue(controlled, out var ctrlGo))
                UpdateCamera(ctrlGo.transform.position);
        }

        // Mouse-orbit rig: right-drag rotates, wheel zooms, camera always
        // frames the controlled vessel. Gives the missing sense of 3D motion
        // even though the vessel itself can't yet show its attitude.
        private void UpdateCamera(Vector3 target)
        {
            if (Input.GetMouseButton(1))
            {
                _camYaw += Input.GetAxis("Mouse X") * OrbitSpeed;
                _camPitch = Mathf.Clamp(_camPitch - Input.GetAxis("Mouse Y") * OrbitSpeed, -85f, 85f);
            }
            _camDistance = Mathf.Clamp(_camDistance - Input.mouseScrollDelta.y * ZoomSpeed, 3f, 400f);

            var rot = Quaternion.Euler(_camPitch, _camYaw, 0f);
            _camera!.position = target + rot * new Vector3(0f, 0f, -_camDistance);
            _camera.LookAt(target);
        }

        private void RenderGround(ClientFlightModel flight)
        {
            // Pad = wherever the craft first spawned.
            _padWorld ??= flight.ControlledState.Position;
            EnsureGround();

            var p = _padWorld.Value;
            var local = flight.ToRenderLocal(p);
            _ground!.transform.position = new Vector3((float)local.X, (float)local.Y, (float)local.Z);

            // "Up" = radial away from the planet centre (world origin). The
            // render frame is a pure translation, so world directions hold.
            double m = p.Length;
            var up = m > 1e-6
                ? new Vector3((float)(p.X / m), (float)(p.Y / m), (float)(p.Z / m))
                : Vector3.up;
            _ground.transform.rotation = Quaternion.FromToRotation(Vector3.up, up);
        }

        private void EnsureGround()
        {
            if (_ground != null) return;
            _ground = GameObject.CreatePrimitive(PrimitiveType.Plane); // 10×10 m, normal +Y
            _ground.name = "LaunchPadGround";
            Object.Destroy(_ground.GetComponent<Collider>()); // presentation only (no contact)
            _ground.transform.localScale = new Vector3(60f, 1f, 60f); // ~600 m square
            _ground.GetComponent<Renderer>().material.color = new Color(0.22f, 0.24f, 0.28f);
        }

        private void RenderThrust(ClientFlightModel flight, double throttle)
        {
            EnsureFlame();
            bool firing = throttle > 0.001;
            _flame!.SetActive(firing);
            if (!firing) return;

            var c = flight.ToRenderLocal(flight.ControlledState.Position);
            float len = (float)(0.6 + 3.0 * throttle);     // plume length grows with throttle
            // Capsule is ~2 m tall (±1 in Y); hang the plume just below it.
            _flame.transform.position = new Vector3((float)c.X, (float)c.Y - 1f - len * 0.5f, (float)c.Z);
            // Default cylinder is 2 m tall → scale Y by len/2 to get total length `len`.
            _flame.transform.localScale = new Vector3(0.35f, len * 0.5f, 0.35f);
        }

        private void EnsureFlame()
        {
            if (_flame != null) return;
            _flame = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            _flame.name = "ThrustPlume";
            Object.Destroy(_flame.GetComponent<Collider>()); // presentation only
            _flame.GetComponent<Renderer>().material.color = new Color(1f, 0.55f, 0.1f);
            _flame.SetActive(false);
        }

        // Re-pin the lattice to the grid cell the vessel sits in. Because the
        // base is grid-snapped, each marker's local position glides smoothly
        // with motion and only the far boundary cell wraps (invisible, like fog).
        private void RenderReferenceField(ClientFlightModel flight)
        {
            EnsureDustPool();
            var w = flight.ControlledState.Position;
            double bx = System.Math.Round(w.X / DustSpacing) * DustSpacing;
            double by = System.Math.Round(w.Y / DustSpacing) * DustSpacing;
            double bz = System.Math.Round(w.Z / DustSpacing) * DustSpacing;

            int idx = 0;
            for (int i = -DustRadius; i <= DustRadius; i++)
                for (int j = -DustRadius; j <= DustRadius; j++)
                    for (int k = -DustRadius; k <= DustRadius; k++)
                    {
                        var local = flight.ToRenderLocal(new Vector3d(
                            bx + i * DustSpacing, by + j * DustSpacing, bz + k * DustSpacing));
                        _dust[idx++].transform.position =
                            new Vector3((float)local.X, (float)local.Y, (float)local.Z);
                    }
        }

        private void EnsureDustPool()
        {
            int span = 2 * DustRadius + 1;
            int target = span * span * span;
            while (_dust.Count < target)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = "RefMarker";
                Object.Destroy(go.GetComponent<Collider>()); // presentation only
                go.transform.localScale = new Vector3(DustScale, DustScale, DustScale);
                go.GetComponent<Renderer>().material.color = new Color(0.55f, 0.6f, 0.7f);
                _dust.Add(go);
            }
        }

        private void Place(VesselId id, Vector3d local, bool isControlled)
        {
            if (!_objects.TryGetValue(id, out var go))
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                go.name = $"ClientVessel_{id}";
                Object.Destroy(go.GetComponent<Collider>()); // presentation only
                go.GetComponent<Renderer>().material.color = isControlled ? Color.cyan : Color.gray;
                _objects[id] = go;
            }
            go.transform.position = new Vector3((float)local.X, (float)local.Y, (float)local.Z);
        }

        public void Clear()
        {
            foreach (var go in _objects.Values)
                if (go != null) Object.Destroy(go);
            _objects.Clear();
            foreach (var go in _dust)
                if (go != null) Object.Destroy(go);
            _dust.Clear();
            if (_flame != null) { Object.Destroy(_flame); _flame = null; }
            if (_ground != null) { Object.Destroy(_ground); _ground = null; }
            _padWorld = null;
        }
    }
}
