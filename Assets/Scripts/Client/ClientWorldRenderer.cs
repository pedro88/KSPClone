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
        private Transform? _camera; // resolved lazily — Camera.main can be null at Start

        // Orbit camera: hold right mouse to swing around the controlled vessel,
        // scroll to zoom. Always looks at the craft. Seeded to a slight
        // top-down-behind framing.
        private float _camYaw;
        // Seed a stronger downward tilt so the ground pad (a horizontal +Y plane,
        // invisible edge-on) and the body directly below the craft on a surface
        // launch are both in frame from the start. Right-drag still overrides.
        private float _camPitch = 30f;
        private float _camDistance = 18f;
        private const float OrbitSpeed = 6f;
        private const float ZoomSpeed = 4f;

        // Attitude (RCS) jets: small blue cones that fire along the commanded
        // pitch/yaw/roll axis while the pilot steers. Distinct from the orange
        // main plume. Axis mapping follows the same untumbled render convention
        // as the rest (pitch→X, yaw→Y, roll→Z in the render frame).
        private readonly GameObject?[] _rcs = new GameObject?[3];

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

        // Ground grid extent + the far clip needed to keep it visible high into
        // the climb. The plane is ~30 km across and drawn world-relative, so it
        // recedes as a shrinking grid up to tens of km of altitude — bridging to
        // where the skybox Earth globe takes over (no empty gap below).
        private const float GroundHalfSpanScale = 40000f; // Unity plane base 10 m → ~400 km wide
        private const float GroundFarClip = 500000f;       // metres — keep the grid visible into the hundreds of km

        public ClientWorldRenderer(Transform? camera = null)
        {
            _camera = camera;
        }

        public void Render(ClientFlightModel flight, double throttle = 0.0, Vector3 attitude = default)
        {
            if (flight.ControlledVesselId is not { } controlled) return;

            // Controlled vessel: predicted state, anchors the origin → ≈(0,0,0).
            Place(controlled, flight.ToRenderLocal(flight.ControlledState.Position), isControlled: true);

            // Every other vessel: interpolated world position in the same frame.
            foreach (var id in flight.InterpolatedVesselIds)
                if (flight.TrySampleOther(id, out var world))
                    Place(id, flight.ToRenderLocal(world), isControlled: false);

            OrientControlled(controlled, flight.ControlledState.Orientation);
            RenderGround(flight);
            RenderReferenceField(flight);
            RenderThrust(flight, throttle);
            RenderRcs(flight, attitude);

            // Camera.main may not have existed when this renderer was built;
            // resolve it here so the orbit rig comes alive once it does.
            _camera ??= Camera.main != null ? Camera.main.transform : null;
            if (_camera != null && _objects.TryGetValue(controlled, out var ctrlGo))
                UpdateCamera(ctrlGo.transform.position);
        }

        private void RenderRcs(ClientFlightModel flight, Vector3 attitude)
        {
            EnsureRcs();
            if (flight.ControlledVesselId is not { } cid || !_objects.TryGetValue(cid, out var cap))
            {
                foreach (var j in _rcs) j?.SetActive(false);
                return;
            }

            var center = cap.transform.position;
            var bodyRot = cap.transform.rotation; // capsule is velocity-aligned
            float[] cmd = { attitude.x, attitude.y, attitude.z };
            Vector3[] ax = { Vector3.right, Vector3.up, Vector3.forward }; // pitch→X, yaw→Y, roll→Z

            for (int i = 0; i < 3; i++)
            {
                var jet = _rcs[i]!;
                bool on = Mathf.Abs(cmd[i]) > 0.001f;
                jet.SetActive(on);
                if (!on) continue;

                // Jet direction is in the capsule's body frame, so the jets ride
                // its (velocity) orientation instead of sticking out in world axes.
                var dir = bodyRot * (ax[i] * Mathf.Sign(cmd[i]));
                float len = 0.4f + 1.6f * Mathf.Clamp01(Mathf.Abs(cmd[i]) / 0.5f);
                jet.transform.position = center + dir * (1.1f + len * 0.5f);
                jet.transform.rotation = Quaternion.FromToRotation(Vector3.up, dir);
                jet.transform.localScale = new Vector3(0.12f, len * 0.5f, 0.12f);
            }
        }

        private void EnsureRcs()
        {
            for (int i = 0; i < _rcs.Length; i++)
            {
                if (_rcs[i] != null) continue;
                var jet = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                jet.name = "RcsJet";
                Object.Destroy(jet.GetComponent<Collider>());
                jet.GetComponent<Renderer>().material.color = new Color(0.25f, 0.6f, 1f);
                jet.SetActive(false);
                _rcs[i] = jet;
            }
        }

        // Mouse-orbit rig: right-drag rotates, wheel zooms, camera always
        // frames the controlled vessel. Gives the missing sense of 3D motion
        // even though the vessel itself can't yet show its attitude.
        private void UpdateCamera(Vector3 target)
        {
            // Push the far clip out so the large receding ground grid stays
            // visible for tens of km of ascent — long enough to hand off to the
            // skybox Earth globe with no gap of empty space below.
            var cam = _camera!.GetComponent<Camera>();
            if (cam != null && cam.farClipPlane < GroundFarClip) cam.farClipPlane = GroundFarClip;

            if (Input.GetMouseButton(1))
            {
                _camYaw += Input.GetAxis("Mouse X") * OrbitSpeed;
                // Allow looking essentially straight down: on a vertical launch
                // the body you came from sits directly below (−Y), so you must be
                // able to pitch to ~90° to see the Earth globe under the craft.
                _camPitch = Mathf.Clamp(_camPitch - Input.GetAxis("Mouse Y") * OrbitSpeed, -89f, 89f);
            }
            _camDistance = Mathf.Clamp(_camDistance - Input.mouseScrollDelta.y * ZoomSpeed, 3f, 400f);

            var rot = Quaternion.Euler(_camPitch, _camYaw, 0f);
            _camera!.position = target + rot * new Vector3(0f, 0f, -_camDistance);
            _camera.LookAt(target);
        }

        private void RenderGround(ClientFlightModel flight)
        {
            // Pad = the spawn point — but only once the predicted state has been
            // reconciled to the real world position. Before the first snapshot
            // ControlledState is Identity (origin), which would pin the pad to
            // the planet centre ~3e8 m away and push it past the far clip.
            if (_padWorld is null)
            {
                var p0 = flight.ControlledState.Position;
                if (p0.LengthSquared < 1.0) return;
                _padWorld = p0;
            }
            EnsureGround();

            // Horizontal in the render frame (normal = world +Y). Everything
            // client-side treats +Y as up — the flame fires −Y, the orbit camera
            // sits above on world-up — so the pad must match, or it shows
            // edge-on and vanishes. Dropped ~1 m so the capsule rests on it.
            var local = flight.ToRenderLocal(_padWorld.Value);
            _ground!.transform.position = new Vector3((float)local.X, (float)local.Y - 1f, (float)local.Z);
            _ground.transform.rotation = Quaternion.identity;
        }

        private void EnsureGround()
        {
            if (_ground != null) return;
            _ground = GameObject.CreatePrimitive(PrimitiveType.Plane); // 10 m base, normal +Y
            _ground.name = "LaunchPadGround";
            Object.Destroy(_ground.GetComponent<Collider>()); // presentation only (no contact)
            // ~400 km square: it's world-pinned and rendered relative to the craft,
            // so it slides downward as you climb — a big grid makes that motion
            // (and thus altitude) legible, and with the pushed-out far clip it
            // stays on-screen into the hundreds of km, handing off to the skybox
            // Earth globe with no gap. Mipmaps blur the far cells (no moiré).
            _ground.transform.localScale = new Vector3(GroundHalfSpanScale, 1f, GroundHalfSpanScale);
            var mat = _ground.GetComponent<Renderer>().material;
            mat.color = Color.white;                       // let the texture's colours show
            mat.mainTexture = GroundGridTexture();
            mat.mainTextureScale = new Vector2(400f, 400f); // ~1 km grid cells across the 400 km plane
        }

        private Texture2D? _gridTex;

        // Earthy ground tile with bright grid lines on two edges; tiled 40× it
        // reads as a ~50 m survey grid. Gives the receding ground parallax so
        // liftoff altitude is visible without any HUD.
        private Texture2D GroundGridTexture()
        {
            if (_gridTex != null) return _gridTex;
            const int s = 64;
            var baseCol = new Color(0.16f, 0.34f, 0.24f);  // land green
            var lineCol = new Color(0.55f, 0.75f, 0.60f);  // grid line
            var t = new Texture2D(s, s) { wrapMode = TextureWrapMode.Repeat };
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    bool line = x < 2 || y < 2; // two edges → continuous grid when tiled
                    t.SetPixel(x, y, line ? lineCol : baseCol);
                }
            t.Apply();
            _gridTex = t;
            return t;
        }

        private void RenderThrust(ClientFlightModel flight, double throttle)
        {
            EnsureFlame();
            bool firing = throttle > 0.001;
            _flame!.SetActive(firing);
            if (!firing) return;

            var c = flight.ToRenderLocal(flight.ControlledState.Position);
            var center = new Vector3((float)c.X, (float)c.Y, (float)c.Z);
            float len = (float)(0.6 + 3.0 * throttle);     // plume length grows with throttle

            // Exhaust leaves the engine down the vessel's local −Y (opposite the
            // +Y thrust axis), rotated by the true attitude — so the plume tracks
            // where the craft actually points, not the velocity heading.
            Vector3 tail = ToUnity(flight.ControlledState.Orientation) * Vector3.down;

            _flame.transform.position = center + tail * (1f + len * 0.5f);
            _flame.transform.rotation = Quaternion.FromToRotation(Vector3.up, tail);
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
                var r = go.GetComponent<Renderer>();
                r.material.color = isControlled ? Color.cyan : Color.gray;
                // A banded texture gives the otherwise-featureless capsule a
                // surface, so its orientation (and any roll) is actually legible.
                r.material.mainTexture = BandedTexture();
                _objects[id] = go;
            }
            go.transform.position = new Vector3((float)local.X, (float)local.Y, (float)local.Z);
        }

        // Orient the controlled capsule to its true, server-authoritative
        // attitude (ADR-0019): the capsule's long axis (+Y) is the vessel's
        // thrust axis, so the nose points where you're actually steering — no
        // more velocity-heading guess. The quaternion is the predicted state's
        // (reconciled against snapshots), so steering is zero-lag.
        private void OrientControlled(VesselId id, Quaterniond orientation)
        {
            if (!_objects.TryGetValue(id, out var go)) return;
            go.transform.rotation = ToUnity(orientation);
        }

        private static Quaternion ToUnity(Quaterniond q) =>
            new((float)q.X, (float)q.Y, (float)q.Z, (float)q.W);

        private Texture2D? _bandTex;

        private Texture2D BandedTexture()
        {
            if (_bandTex != null) return _bandTex;
            const int s = 64;
            var t = new Texture2D(s, s);
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    // Horizontal bands + a checker so both pitch and roll read.
                    bool band = (y / 8) % 2 == 0;
                    bool check = ((x / 8) + (y / 8)) % 2 == 0;
                    t.SetPixel(x, y, band
                        ? new Color(0.95f, 0.95f, 1f)
                        : (check ? new Color(0.2f, 0.5f, 0.7f) : new Color(0.1f, 0.3f, 0.5f)));
                }
            t.Apply();
            _bandTex = t;
            return t;
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
            for (int i = 0; i < _rcs.Length; i++)
                if (_rcs[i] != null) { Object.Destroy(_rcs[i]); _rcs[i] = null; }
            _padWorld = null;
        }
    }
}
