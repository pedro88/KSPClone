#nullable enable annotations

using System.Collections.Generic;
using NUnit.Framework;
using KSPClone.Construction;

namespace KSPClone.Construction.Tests
{
    /// <summary>
    /// The Design part-tree data model (M3-T01, BUILD-1): a Design is a tree of
    /// parts with no world state, queryable by stable node id. This fixture links
    /// only KSPClone.Construction — the machine-checkable form of Art. 7 (the
    /// Construction asmdef references nothing, so it cannot pull in flight/physics).
    /// </summary>
    public sealed class ConstructionModelTests
    {
        private static readonly PartTypeId Pod = new("command-pod");
        private static readonly PartTypeId Tank = new("fuel-tank");
        private static readonly PartTypeId Engine = new("engine");

        // Build: root(pod) → a(tank) → b(engine), and root → c(tank).
        private static (Design design, NodeId a, NodeId b, NodeId c) BuildSample()
        {
            var d = Design.Create(DesignId.New(), "test", Pod);
            var a = d.AllocateNodeId();
            d.Tree.Add(new PartNode(a, Tank, d.RootNodeId, "bottom", PartPose.Identity));
            var b = d.AllocateNodeId();
            d.Tree.Add(new PartNode(b, Engine, a, "bottom", PartPose.Identity));
            var c = d.AllocateNodeId();
            d.Tree.Add(new PartNode(c, Tank, d.RootNodeId, "radial-1", PartPose.Identity));
            return (d, a, b, c);
        }

        [Test]
        public void Create_SeedsSingleRoot_WithNoWorldState()
        {
            var d = Design.Create(DesignId.New(), "rocket", Pod);
            Assert.AreEqual(1, d.Tree.Count);
            Assert.AreEqual(new NodeId(1), d.RootNodeId);
            Assert.IsTrue(d.Tree.TryGet(d.RootNodeId, out var root));
            Assert.IsTrue(root.IsRoot);
            Assert.IsTrue(root.PartType.Equals(Pod));
            Assert.AreEqual(0L, d.AppliedSeq);
        }

        [Test]
        public void AllocateNodeId_IsMonotonicPerDesign()
        {
            var d = Design.Create(DesignId.New(), "x", Pod);
            Assert.AreEqual(new NodeId(2), d.AllocateNodeId());
            Assert.AreEqual(new NodeId(3), d.AllocateNodeId());
            Assert.AreEqual(4L, d.PeekNextNodeId);
        }

        [Test]
        public void Children_AreReturnedInInsertionOrder()
        {
            var (d, a, _, c) = BuildSample();
            var kids = d.Tree.Children(d.RootNodeId);
            Assert.AreEqual(2, kids.Count);
            Assert.AreEqual(a, kids[0]);
            Assert.AreEqual(c, kids[1]);
        }

        [Test]
        public void Subtree_EnumeratesSelfThenDescendants_DepthFirst()
        {
            var (d, a, b, c) = BuildSample();
            var sub = new List<NodeId>(d.Tree.Subtree(d.RootNodeId));
            CollectionAssert.AreEqual(new[] { d.RootNodeId, a, b, c }, sub);

            var subA = new List<NodeId>(d.Tree.Subtree(a));
            CollectionAssert.AreEqual(new[] { a, b }, subA);
        }

        [Test]
        public void Ancestors_WalkParentToRoot()
        {
            var (d, a, b, _) = BuildSample();
            var anc = new List<NodeId>(d.Tree.Ancestors(b));
            CollectionAssert.AreEqual(new[] { a, d.RootNodeId }, anc);
            Assert.IsTrue(d.Tree.IsSelfOrAncestor(a, b));
            Assert.IsTrue(d.Tree.IsSelfOrAncestor(d.RootNodeId, b));
            Assert.IsFalse(d.Tree.IsSelfOrAncestor(b, a));
        }

        [Test]
        public void RemoveSubtree_DropsNodeAndDescendants_KeepsSiblings()
        {
            var (d, a, b, c) = BuildSample();
            d.Tree.RemoveSubtree(a); // removes a and its child b
            Assert.IsFalse(d.Tree.Contains(a));
            Assert.IsFalse(d.Tree.Contains(b));
            Assert.IsTrue(d.Tree.Contains(c), "sibling survives");
            CollectionAssert.AreEqual(new[] { c }, new List<NodeId>(d.Tree.Children(d.RootNodeId)));
        }

        [Test]
        public void Reparent_MovesNodeAndItsSubtree()
        {
            var (d, a, b, c) = BuildSample();
            // Move a (with child b) under c.
            if (!d.Tree.TryGet(a, out var an)) Assert.Fail();
            d.Tree.Reparent(an.WithParent(c, "top", PartPose.Identity));

            CollectionAssert.AreEqual(new[] { c }, new List<NodeId>(d.Tree.Children(d.RootNodeId)));
            CollectionAssert.AreEqual(new[] { a }, new List<NodeId>(d.Tree.Children(c)));
            // b still hangs off a → subtree of a unchanged.
            CollectionAssert.AreEqual(new[] { a, b }, new List<NodeId>(d.Tree.Subtree(a)));
        }

        [Test]
        public void Clone_IsIndependentSnapshot()
        {
            var (d, a, _, _) = BuildSample();
            var snapshot = d.Tree.Clone();
            d.Tree.RemoveSubtree(a); // mutate the original after snapshotting

            Assert.IsTrue(snapshot.Contains(a), "clone is unaffected by later edits (launch snapshot, M3-T08)");
            Assert.IsFalse(d.Tree.Contains(a));
        }
    }
}
