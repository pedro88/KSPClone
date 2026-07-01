#nullable enable annotations

using System;
using NUnit.Framework;
using KSPClone.Construction;

namespace KSPClone.Construction.Tests
{
    /// <summary>Live VAB stats: mass/propellant/thrust/TWR/Δv from the part tree.</summary>
    public sealed class DesignStatsTests
    {
        [Test]
        public void Compute_PodTankTerrier_AggregatesMassThrustTwrDeltaV()
        {
            // pod → FL-T400 (250 dry + 2000 prop) → LV-909 Terrier (500 dry, 60 kN, Isp 345)
            var d = Design.Create(DesignId.New(), "r", StockParts.Mk1Pod);
            var tank = d.AllocateNodeId();
            d.Tree.Add(new PartNode(tank, StockParts.FlT400, d.RootNodeId, "bottom", PartPose.Identity));
            var eng = d.AllocateNodeId();
            d.Tree.Add(new PartNode(eng, StockParts.Terrier, tank, "bottom", PartPose.Identity));

            var s = DesignStats.Compute(d.Tree, StockParts.Catalog());

            Assert.AreEqual(3, s.PartCount);
            Assert.AreEqual(1590.0, s.DryMassKg, 1e-9);       // 840 + 250 + 500
            Assert.AreEqual(2000.0, s.PropellantKg, 1e-9);
            Assert.AreEqual(3590.0, s.WetMassKg, 1e-9);
            Assert.AreEqual(60_000.0, s.ThrustN, 1e-9);
            Assert.AreEqual(345.0, s.EffectiveIspS, 1e-9);

            // TWR = 60000 / (3590 * 9.80665) ≈ 1.704
            Assert.AreEqual(60_000.0 / (3590.0 * DesignStats.G0), s.TwrEarthSurface, 1e-6);
            Assert.Greater(s.TwrEarthSurface, 1.0, "can lift off");

            // Δv = 345 * g0 * ln(3590/1590) ≈ 2757 m/s
            Assert.AreEqual(345.0 * DesignStats.G0 * Math.Log(3590.0 / 1590.0), s.DeltaVMps, 1e-6);
        }

        [Test]
        public void Compute_NoEngine_ZeroThrustTwrDeltaV()
        {
            var d = Design.Create(DesignId.New(), "r", StockParts.Mk1Pod);
            var s = DesignStats.Compute(d.Tree, StockParts.Catalog());
            Assert.AreEqual(0.0, s.ThrustN, 1e-9);
            Assert.AreEqual(0.0, s.TwrEarthSurface, 1e-9);
            Assert.AreEqual(0.0, s.DeltaVMps, 1e-9);
        }

        [Test]
        public void TwoEngines_EffectiveIsp_IsMassFlowWeighted()
        {
            // Two different engines → effective Isp = ΣF / Σ(F/Isp).
            var d = Design.Create(DesignId.New(), "r", StockParts.Mk1Pod);
            var t = d.AllocateNodeId();
            d.Tree.Add(new PartNode(t, StockParts.FlT800, d.RootNodeId, "bottom", PartPose.Identity));
            var e1 = d.AllocateNodeId();
            d.Tree.Add(new PartNode(e1, StockParts.Swivel, t, "top", PartPose.Identity));   // 215 kN, 320 s
            var e2 = d.AllocateNodeId();
            d.Tree.Add(new PartNode(e2, StockParts.Terrier, t, "bottom", PartPose.Identity)); // 60 kN, 345 s

            var s = DesignStats.Compute(d.Tree, StockParts.Catalog());
            double expected = (215_000.0 + 60_000.0) / (215_000.0 / 320.0 + 60_000.0 / 345.0);
            Assert.AreEqual(expected, s.EffectiveIspS, 1e-6);
            Assert.AreEqual(275_000.0, s.ThrustN, 1e-9);
        }
    }
}
