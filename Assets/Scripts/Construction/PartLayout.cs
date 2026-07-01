#nullable enable annotations

using System.Collections.Generic;

namespace KSPClone.Construction
{
    /// <summary>A part placed in the vessel body frame: its centre offset along the stack axis (+Y) and size.</summary>
    public readonly struct PlacedPart
    {
        public NodeId Node { get; }
        public PartTypeId PartType { get; }
        public double CenterY { get; }
        public double HeightM { get; }
        public double RadiusM { get; }

        public PlacedPart(NodeId node, PartTypeId partType, double centerY, double heightM, double radiusM)
        {
            Node = node; PartType = partType; CenterY = centerY; HeightM = heightM; RadiusM = radiusM;
        }
    }

    /// <summary>
    /// Minimal stacking layout of a part tree (M3 modelling): each part's centre
    /// along the vessel +Y axis, derived from its parent's attach-point offset and
    /// the parts' heights so they sit flush. Pure + engine-agnostic — the VAB
    /// preview and the launched craft's presentation both consume it, and the sim
    /// treats the whole thing as one rigid body (ADR-0005).
    /// </summary>
    public static class PartLayout
    {
        public static IReadOnlyList<PlacedPart> Compute(PartTree tree, PartCatalog catalog)
        {
            var list = new List<PlacedPart>();
            Place(tree, catalog, tree.Root, 0.0, list);
            return list;
        }

        private static void Place(PartTree tree, PartCatalog catalog, NodeId node, double centerY, List<PlacedPart> list)
        {
            if (!tree.TryGet(node, out var n)) return;
            double h = 1.0, r = 0.6;
            catalog.TryGet(n.PartType, out var type);
            if (type != null) { h = type.HeightM; r = type.RadiusM; }
            list.Add(new PlacedPart(node, n.PartType, centerY, h, r));

            foreach (var childId in tree.Children(node))
            {
                if (!tree.TryGet(childId, out var child)) continue;
                double faceY = 0.0;
                if (type != null && type.TryAttachPoint(child.AttachPoint, out var ap)) faceY = ap.LocalPose.Py;
                double childH = catalog.TryGet(child.PartType, out var ct) ? ct.HeightM : 1.0;
                int dir = faceY >= 0 ? 1 : -1;
                double childCenter = centerY + faceY + dir * (childH / 2.0);
                Place(tree, catalog, childId, childCenter, list);
            }
        }
    }
}
