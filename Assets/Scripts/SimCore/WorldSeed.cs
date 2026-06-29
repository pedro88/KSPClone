using System;

namespace KSPClone.SimCore
{
    /// <summary>
    /// Deterministic M0 world seed: an Earth–Moon system plus one vessel on a
    /// transfer orbit whose apoapsis meets the Moon's SOI, so the warp
    /// auto-limit has a real POI to stop on. Deterministic (fixed vessel id,
    /// fixed elements) so a restored world matches the seeded one (PERSIST-3).
    /// </summary>
    public static class WorldSeed
    {
        public const double EarthMu         = 3.986004418e14;
        public const double MoonMu          = 4.9048695e12;
        public const double EarthRadius     = 6_371_000.0;
        public const double EarthSoiRadius  = 924_000_000.0;
        public const double MoonOrbitRadius = 384_400_000.0;
        public const double MoonSoiRadius   = 66_100_000.0;

        public static readonly VesselId SeedVesselId =
            new(new Guid("00000000-0000-0000-0000-000000000001"));

        public static BodyRegistry CreateBodies() => new(new[]
        {
            new CelestialBody(CelestialBodyId.Planet, "Earth", EarthMu, EarthSoiRadius, CelestialBodyId.Root),
            new CelestialBody(CelestialBodyId.Moon, "Moon", MoonMu, MoonSoiRadius, CelestialBodyId.Planet,
                new Orbit(MoonOrbitRadius, 0.0, 0, 0, 0, 0, 0, CelestialBodyId.Planet)),
        });

        public static Vessel CreateVessel()
        {
            const double encounterTime = 200_000.0;
            var moonMeanMotion = Math.Sqrt(EarthMu / (MoonOrbitRadius * MoonOrbitRadius * MoonOrbitRadius));
            var moonAngle = moonMeanMotion * encounterTime;
            var peri = EarthRadius + 300_000.0;
            var apo  = MoonOrbitRadius - MoonSoiRadius * 0.5;
            var a = 0.5 * (peri + apo);
            var e = (apo - peri) / (apo + peri);
            var meanMotion = Math.Sqrt(EarthMu / (a * a * a));
            var argp = KeplerPropagator.WrapTwoPi(moonAngle - Math.PI);
            var m0   = KeplerPropagator.WrapTwoPi(Math.PI - meanMotion * encounterTime);
            return new Vessel(SeedVesselId, new Orbit(a, e, 0.0, 0.0, argp, m0, 0.0, CelestialBodyId.Planet));
        }

        /// <summary>Registers the seed vessel into a world (bootstrap callback for WorldRestorer).</summary>
        public static void Seed(SimWorld world) => world.RegisterVessel(CreateVessel());

        // --- Demo-craft propulsion + mass (M1 Slice 1.6, ADR-0016) ---
        // Flat spec until the M3 build system emits part trees. A single
        // upper-stage-ish craft: ~5 t wet, one 60 kN / 300 s engine.

        public const double SeedWetMassKg = 5_000.0;
        public const double SeedPropellantKg = 3_000.0;
        public const double SeedEngineThrustN = 60_000.0;
        public const double SeedEngineIspS = 300.0;

        /// <summary>Total mass + a diagonal inertia tensor for the seed vessel.</summary>
        public static RigidVesselMass CreateMass() =>
            new(SeedWetMassKg, ix: 8_000.0, iy: 8_000.0, iz: 8_000.0);

        /// <summary>
        /// The seed vessel's engine set: one main engine thrusting along
        /// the vessel's local +Y, mounted below the centre of mass.
        /// </summary>
        public static EngineModule[] CreateEngines() => new[]
        {
            new EngineModule(
                name: "main",
                thrustNewtons: SeedEngineThrustN,
                ispSeconds: SeedEngineIspS,
                mountLocal: new Vector3d(0.0, -2.0, 0.0),
                thrustDirLocal: new Vector3d(0.0, 1.0, 0.0),
                propellantKg: SeedPropellantKg),
        };
    }
}
