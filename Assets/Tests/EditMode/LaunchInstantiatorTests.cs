#nullable enable annotations

using NUnit.Framework;
using KSPClone.Construction;
using KSPClone.Launch;

namespace KSPClone.SimCore.Tests
{
    /// <summary>
    /// Launch instantiation (M3-T08, BUILD-4): a Design becomes a Vessel in the
    /// world with the expected part count/mass and unoccupied stations, while the
    /// source Design is left untouched. Construction and flight meet only here.
    /// </summary>
    public sealed class LaunchInstantiatorTests
    {
        private static readonly PartTypeId Pod = new("command-pod");
        private static readonly PartTypeId Tank = new("fuel-tank");
        private static readonly PartTypeId Engine = new("engine");

        private static PartCatalog Catalog() => new(new[]
        {
            new PartType(Pod, 800.0),
            new PartType(Tank, 1250.0),
            new PartType(Engine, 300.0),
        });

        // root(pod) → tank → engine
        private static Design ThreePartDesign()
        {
            var d = Design.Create(DesignId.New(), "rocket", Pod);
            var tank = d.AllocateNodeId();
            d.Tree.Add(new PartNode(tank, Tank, d.RootNodeId, "bottom", PartPose.Identity));
            var engine = d.AllocateNodeId();
            d.Tree.Add(new PartNode(engine, Engine, tank, "bottom", PartPose.Identity));
            return d;
        }

        [Test]
        public void Launch_CreatesVesselInWorld_WithAggregateMass()
        {
            var d = ThreePartDesign();
            var world = new SimWorld();
            var masses = new VesselMassRegistry();
            var orbit = new Orbit(7_000_000.0, 0, 0, 0, 0, 0, 0, CelestialBodyId.Planet);

            var res = LaunchInstantiator.Launch(d, Catalog(), orbit, world, masses);

            Assert.IsTrue(world.Vessels.ContainsKey(res.Vessel.Id), "vessel appears in the world");
            Assert.AreEqual(3, res.PartCount);
            Assert.AreEqual(2350.0, res.TotalMassKg, 1e-9); // 800 + 1250 + 300
            Assert.AreEqual(2350.0, masses.Get(res.Vessel.Id)!.MassKg, 1e-9);
            Assert.AreEqual(VesselState.OnRails, res.Vessel.State, "starts on-rails at the pad");
            Assert.AreEqual(CelestialBodyId.Planet, res.Vessel.Orbit.ParentBody);
        }

        [Test]
        public void Launch_LeavesSourceDesignUnchanged()
        {
            var d = ThreePartDesign();
            var seqBefore = d.AppliedSeq;
            var countBefore = d.Tree.Count;
            var nextIdBefore = d.PeekNextNodeId;

            LaunchInstantiator.Launch(d, Catalog(),
                new Orbit(7_000_000.0, 0, 0, 0, 0, 0, 0, CelestialBodyId.Planet),
                new SimWorld(), new VesselMassRegistry());

            Assert.AreEqual(seqBefore, d.AppliedSeq, "op seq unchanged");
            Assert.AreEqual(countBefore, d.Tree.Count, "design tree unchanged");
            Assert.AreEqual(nextIdBefore, d.PeekNextNodeId, "no node ids allocated by launch");
        }

        [Test]
        public void Launch_FromStockParts_BuildsEngines_AndWetMass()
        {
            // pod → FL-T400 (2000 kg prop) → LV-909 Terrier engine.
            var d = Design.Create(DesignId.New(), "stock", StockParts.Mk1Pod);
            var tank = d.AllocateNodeId();
            d.Tree.Add(new PartNode(tank, StockParts.FlT400, d.RootNodeId, "bottom", PartPose.Identity));
            var eng = d.AllocateNodeId();
            d.Tree.Add(new PartNode(eng, StockParts.Terrier, tank, "bottom", PartPose.Identity));

            var world = new SimWorld();
            var masses = new VesselMassRegistry();
            var engines = new VesselEngineRegistry();
            var res = LaunchInstantiator.Launch(d, StockParts.Catalog(),
                new Orbit(7_000_000.0, 0, 0, 0, 0, 0, 0, CelestialBodyId.Planet), world, masses, engines);

            // Wet mass = 840 + (250 + 2000) + 500 = 3590 kg.
            Assert.AreEqual(3590.0, res.TotalMassKg, 1e-9);
            Assert.AreEqual(1, res.EngineCount);
            Assert.AreEqual(3590.0, masses.Get(res.Vessel.Id)!.MassKg, 1e-9);
            var em = engines.EnginesFor(res.Vessel.Id);
            Assert.IsNotNull(em);
        }

        [Test]
        public void Launch_TwoVessels_FromSameDesign_AreDistinct()
        {
            var d = ThreePartDesign();
            var world = new SimWorld();
            var masses = new VesselMassRegistry();
            var orbit = new Orbit(7_000_000.0, 0, 0, 0, 0, 0, 0, CelestialBodyId.Planet);

            var a = LaunchInstantiator.Launch(d, Catalog(), orbit, world, masses);
            var b = LaunchInstantiator.Launch(d, Catalog(), orbit, world, masses);

            Assert.AreNotEqual(a.Vessel.Id, b.Vessel.Id);
            Assert.AreEqual(2, world.Vessels.Count);
        }
    }
}
