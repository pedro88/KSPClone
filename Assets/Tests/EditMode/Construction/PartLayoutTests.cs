#nullable enable annotations

using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using KSPClone.Construction;

namespace KSPClone.Construction.Tests
{
    /// <summary>Stacking layout: parts sit flush along +Y by attach offset + heights.</summary>
    public sealed class PartLayoutTests
    {
        [Test]
        public void Stack_Pod_Tank_Engine_CentersAreFlush()
        {
            // pod(H1.1) → FL-T400(H1.9) on pod "bottom" → Terrier(H0.8) on tank "bottom".
            var d = Design.Create(DesignId.New(), "r", StockParts.Mk1Pod);
            var tank = d.AllocateNodeId();
            d.Tree.Add(new PartNode(tank, StockParts.FlT400, d.RootNodeId, "bottom", PartPose.Identity));
            var eng = d.AllocateNodeId();
            d.Tree.Add(new PartNode(eng, StockParts.Terrier, tank, "bottom", PartPose.Identity));

            var placed = PartLayout.Compute(d.Tree, StockParts.Catalog());
            var byNode = placed.ToDictionary(p => p.Node, p => p);

            Assert.AreEqual(0.0, byNode[d.RootNodeId].CenterY, 1e-9);
            // pod bottom face -0.55, tank half 0.95 → -1.5
            Assert.AreEqual(-1.5, byNode[tank].CenterY, 1e-9);
            // tank bottom face -0.95 (rel tank), tank center -1.5, engine half 0.4 → -2.85
            Assert.AreEqual(-2.85, byNode[eng].CenterY, 1e-9);
            Assert.AreEqual(3, placed.Count);
        }
    }
}
