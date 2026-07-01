#nullable enable annotations

namespace KSPClone.Construction
{
    /// <summary>
    /// A small stock part library modelled on KSP's early parts (mass/thrust/Isp
    /// rounded to plausible values). Enough to assemble a flyable rocket in the
    /// VAB and launch it. Not a balance pass — reference data for the demo. Masses
    /// in kg, thrust in N, Isp in s.
    /// </summary>
    public static class StockParts
    {
        public static readonly PartTypeId Mk1Pod        = new("mk1-command-pod");
        public static readonly PartTypeId FlT400        = new("fl-t400-tank");
        public static readonly PartTypeId FlT800        = new("fl-t800-tank");
        public static readonly PartTypeId Swivel        = new("lv-t45-swivel");
        public static readonly PartTypeId Terrier       = new("lv-909-terrier");
        public static readonly PartTypeId Decoupler     = new("tr-18a-decoupler");
        public static readonly PartTypeId NoseCone      = new("aerodynamic-nose-cone");

        private static AttachPoint Ap(string key, double y) => new(key, new PartPose(0, y, 0, 0, 0, 0, 1));

        /// <summary>The default stock catalog used by the demo VAB.</summary>
        public static PartCatalog Catalog() => new(new[]
        {
            new PartType(Mk1Pod, dryMassKg: 840, displayName: "Mk1 Command Pod",
                attachPoints: new[] { Ap("bottom", -0.6), Ap("top", 0.6) }),

            new PartType(FlT400, dryMassKg: 250, propellantKg: 2000, displayName: "FL-T400 Fuel Tank",
                attachPoints: new[] { Ap("top", 1.0), Ap("bottom", -1.0) }),

            new PartType(FlT800, dryMassKg: 500, propellantKg: 4000, displayName: "FL-T800 Fuel Tank",
                attachPoints: new[] { Ap("top", 1.9), Ap("bottom", -1.9) }),

            new PartType(Swivel, dryMassKg: 1500, engineThrustN: 215_000, engineIspS: 320,
                displayName: "LV-T45 'Swivel' Engine", attachPoints: new[] { Ap("top", 0.8) }),

            new PartType(Terrier, dryMassKg: 500, engineThrustN: 60_000, engineIspS: 345,
                displayName: "LV-909 'Terrier' Engine", attachPoints: new[] { Ap("top", 0.5) }),

            new PartType(Decoupler, dryMassKg: 50, displayName: "TR-18A Stack Decoupler",
                attachPoints: new[] { Ap("top", 0.2), Ap("bottom", -0.2) }),

            new PartType(NoseCone, dryMassKg: 30, displayName: "Aerodynamic Nose Cone",
                attachPoints: new[] { Ap("bottom", -0.5) }),
        });
    }
}
