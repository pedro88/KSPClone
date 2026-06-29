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
        private readonly Vector3 _cameraOffset = new(0f, 6f, -18f);

        public ClientWorldRenderer(Transform? camera = null)
        {
            _camera = camera;
        }

        public void Render(ClientFlightModel flight)
        {
            if (flight.ControlledVesselId is not { } controlled) return;

            // Controlled vessel: predicted state, anchors the origin → ≈(0,0,0).
            Place(controlled, flight.ToRenderLocal(flight.ControlledState.Position), isControlled: true);

            // Every other vessel: interpolated world position in the same frame.
            foreach (var id in flight.InterpolatedVesselIds)
                if (flight.TrySampleOther(id, out var world))
                    Place(id, flight.ToRenderLocal(world), isControlled: false);

            if (_camera != null && _objects.TryGetValue(controlled, out var ctrlGo))
                _camera.position = ctrlGo.transform.position + _cameraOffset;
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
        }
    }
}
