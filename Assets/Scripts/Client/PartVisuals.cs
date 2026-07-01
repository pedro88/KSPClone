#nullable enable annotations

using UnityEngine;
using KSPClone.Construction;

namespace KSPClone.Client
{
    /// <summary>
    /// Presentation-only mapping from a part type to a primitive shape + colour
    /// for the assembled-craft view (M3). Sizes come from the part's HeightM/RadiusM;
    /// this only chooses the mesh + tint. Purely client-side — the sim treats the
    /// whole craft as one rigid body (ADR-0005).
    /// </summary>
    public static class PartVisuals
    {
        public static PrimitiveType Shape(PartTypeId id)
        {
            if (id.Equals(StockParts.Mk1Pod)) return PrimitiveType.Capsule;
            if (id.Equals(StockParts.NoseCone)) return PrimitiveType.Capsule;
            return PrimitiveType.Cylinder; // tanks, engines, decoupler
        }

        public static Color Color(PartTypeId id)
        {
            if (id.Equals(StockParts.Mk1Pod)) return new Color(0.80f, 0.82f, 0.85f);   // pod: light grey
            if (id.Equals(StockParts.FlT400)) return new Color(0.90f, 0.90f, 0.92f);   // tank: white
            if (id.Equals(StockParts.FlT800)) return new Color(0.88f, 0.88f, 0.90f);
            if (id.Equals(StockParts.Swivel)) return new Color(0.35f, 0.35f, 0.38f);   // engine: dark
            if (id.Equals(StockParts.Terrier)) return new Color(0.40f, 0.38f, 0.35f);
            if (id.Equals(StockParts.Decoupler)) return new Color(0.55f, 0.45f, 0.25f); // decoupler: bronze
            if (id.Equals(StockParts.NoseCone)) return new Color(0.75f, 0.20f, 0.20f);  // nose: red
            return new Color(0.7f, 0.7f, 0.7f);
        }
    }
}
