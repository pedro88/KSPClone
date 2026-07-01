#nullable enable annotations

using System.Collections.Generic;
using UnityEngine;
using KSPClone.Construction;

namespace KSPClone.Client
{
    /// <summary>
    /// Live 3D preview of the shared Design while the VAB is open (M3): builds the
    /// stacked parts in front of the camera, shows green attach markers at free
    /// attach points, and does mouse placement — click a part to select it, click
    /// a marker to attach the armed part there. Presentation-only; the authoritative
    /// tree still lives on the server (the click just sends an edit op).
    /// </summary>
    public sealed class VabPreview
    {
        private const float Distance = 7f;     // metres in front of the camera
        private const float RotSpeedDeg = 25f; // idle auto-rotate

        private GameObject? _anchor;
        private readonly List<GameObject> _rig = new();
        private long _builtSeq = long.MinValue;
        private int _builtLocks = -1;
        private long _builtSelected = long.MinValue;
        private float _spin;

        public void Tick(Camera? cam, ClientVabModel vab, bool open)
        {
            if (!open || cam == null || !vab.Ready)
            {
                if (_anchor != null) _anchor.SetActive(false);
                return;
            }
            EnsureAnchor();
            _anchor!.SetActive(true);

            // Float in front of the camera, offset to the right so the centre VAB
            // panel doesn't overlap it; tilt + spin so the stack reads as 3D.
            // Hold still while a part is armed so its marker is easy to click.
            _anchor.transform.position = cam.transform.position
                + cam.transform.forward * Distance
                + cam.transform.right * 3.5f;
            if (vab.ArmedPart is null) _spin += RotSpeedDeg * Time.unscaledDeltaTime;
            _anchor.transform.rotation = Quaternion.Euler(15f, _spin, 0f);

            if (vab.Replica!.AppliedSeq != _builtSeq || vab.Locks.Count != _builtLocks || vab.Selected.Value != _builtSelected)
            {
                Rebuild(vab);
                _builtSeq = vab.Replica.AppliedSeq;
                _builtLocks = vab.Locks.Count;
                _builtSelected = vab.Selected.Value;
            }

            HandleClick(cam, vab);
        }

        private void HandleClick(Camera cam, ClientVabModel vab)
        {
            if (!Input.GetMouseButtonDown(0)) return;
            // The VAB IMGUI panel sits on the left/centre; only treat clicks in the
            // right portion (where the preview floats) as 3D picks, so panel clicks
            // don't also raycast-place behind the UI.
            if (Input.mousePosition.x < Screen.width * 0.42f) return;
            var ray = cam.ScreenPointToRay(Input.mousePosition);
            // Only the VAB preview objects have colliders in the default scene
            // (flight visuals strip theirs), so an unmasked raycast hits preview only.
            if (!Physics.Raycast(ray, out var hit, 100f)) return;
            var tag = hit.collider.GetComponent<VabPartTag>();
            if (tag == null) return;
            if (tag.IsMarker)
            {
                if (vab.ArmedPart is { } ap) vab.AddPart(ap, tag.Node, tag.AttachKey);
            }
            else
            {
                vab.Selected = tag.Node;
            }
        }

        private void Rebuild(ClientVabModel vab)
        {
            Clear();
            var tree = vab.Replica!.Tree;
            var catalog = vab.Catalog;
            foreach (var p in PartLayout.Compute(tree, catalog))
            {
                // Part body.
                var body = GameObject.CreatePrimitive(PartVisuals.Shape(p.PartType));
                body.name = $"VabPart_{p.PartType}";
                var col = PartVisuals.Color(p.PartType);
                if (vab.Selected.Equals(p.Node)) col = Color.Lerp(col, Color.yellow, 0.55f);
                body.GetComponent<Renderer>().material.color = col;
                body.transform.SetParent(_anchor!.transform, false);
                body.transform.localPosition = new Vector3(0f, (float)p.CenterY, 0f);
                float dia = (float)(p.RadiusM * 2.0);
                body.transform.localScale = new Vector3(dia, (float)(p.HeightM / 2.0), dia);
                body.AddComponent<VabPartTag>().Node = p.Node;
                _rig.Add(body);

                // Green markers at this part's free attach points (beside the stack).
                if (!catalog.TryGet(p.PartType, out var type)) continue;
                foreach (var key in FreeAttach(tree, type, p.Node))
                {
                    if (!type.TryAttachPoint(key, out var ap)) continue;
                    var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    marker.name = $"VabMarker_{p.Node}_{key}";
                    marker.GetComponent<Renderer>().material.color = new Color(0.3f, 1f, 0.45f);
                    marker.transform.SetParent(_anchor!.transform, false);
                    marker.transform.localPosition = new Vector3((float)(p.RadiusM + 0.4), (float)(p.CenterY + ap.LocalPose.Py), 0f);
                    marker.transform.localScale = Vector3.one * 0.35f;
                    var t = marker.AddComponent<VabPartTag>();
                    t.Node = p.Node; t.AttachKey = key; t.IsMarker = true;
                    _rig.Add(marker);
                }
            }
        }

        private static IEnumerable<string> FreeAttach(PartTree tree, PartType type, NodeId node)
        {
            var used = new HashSet<string>();
            foreach (var c in tree.Children(node)) if (tree.TryGet(c, out var cn)) used.Add(cn.AttachPoint);
            foreach (var ap in type.AttachPoints) if (!used.Contains(ap.Key)) yield return ap.Key;
        }

        private void EnsureAnchor() { if (_anchor == null) _anchor = new GameObject("VabPreview"); }

        private void Clear()
        {
            foreach (var g in _rig) if (g != null) Object.Destroy(g);
            _rig.Clear();
        }

        public void Dispose()
        {
            Clear();
            if (_anchor != null) Object.Destroy(_anchor);
            _anchor = null;
        }
    }
}
