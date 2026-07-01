#nullable enable annotations

using NUnit.Framework;
using KSPClone.Construction;

namespace KSPClone.Construction.Tests
{
    /// <summary>
    /// Pure edit-op application (M3-T02, BUILD-1/2): each op kind mutates the tree
    /// as specified; each rejection fires on its exact trigger and leaves the tree
    /// unchanged.
    /// </summary>
    public sealed class PartTreeMutatorTests
    {
        private static readonly PartTypeId Pod = new("pod");
        private static readonly PartTypeId Tank = new("tank");

        private static Design Fresh() => Design.Create(DesignId.New(), "t", Pod);

        private static NodeId Add(Design d, NodeId parent, string attach)
        {
            var id = d.AllocateNodeId();
            var r = PartTreeMutator.Apply(d.Tree, new AddPartOp(id, Tank, parent, attach, PartPose.Identity));
            Assert.IsTrue(r.IsApplied);
            return id;
        }

        [Test]
        public void Add_AttachesChild()
        {
            var d = Fresh();
            var id = Add(d, d.RootNodeId, "bottom");
            Assert.IsTrue(d.Tree.Contains(id));
            Assert.AreEqual(2, d.Tree.Count);
        }

        [Test]
        public void Add_UnknownParent_Rejected_TreeUnchanged()
        {
            var d = Fresh();
            var r = PartTreeMutator.Apply(d.Tree,
                new AddPartOp(new NodeId(99), Tank, new NodeId(42), "x", PartPose.Identity));
            Assert.IsFalse(r.IsApplied);
            Assert.AreEqual(RejectionReason.UnknownNode, r.Reason);
            Assert.AreEqual(1, d.Tree.Count);
        }

        [Test]
        public void Add_DuplicateNodeId_Rejected()
        {
            var d = Fresh();
            var id = Add(d, d.RootNodeId, "bottom");
            var r = PartTreeMutator.Apply(d.Tree, new AddPartOp(id, Tank, d.RootNodeId, "radial", PartPose.Identity));
            Assert.IsFalse(r.IsApplied);
            Assert.AreEqual(RejectionReason.DuplicateNode, r.Reason);
        }

        [Test]
        public void Add_OccupiedAttachPoint_Rejected_TreeUnchanged()
        {
            var d = Fresh();
            Add(d, d.RootNodeId, "bottom");
            var before = d.Tree.Count;
            var r = PartTreeMutator.Apply(d.Tree,
                new AddPartOp(d.AllocateNodeId(), Tank, d.RootNodeId, "bottom", PartPose.Identity));
            Assert.IsFalse(r.IsApplied);
            Assert.AreEqual(RejectionReason.AttachPointOccupied, r.Reason);
            Assert.AreEqual(before, d.Tree.Count);
        }

        [Test]
        public void Remove_DropsSubtree()
        {
            var d = Fresh();
            var a = Add(d, d.RootNodeId, "bottom");
            var b = Add(d, a, "bottom");
            var r = PartTreeMutator.Apply(d.Tree, new RemovePartOp(a));
            Assert.IsTrue(r.IsApplied);
            Assert.IsFalse(d.Tree.Contains(a));
            Assert.IsFalse(d.Tree.Contains(b));
        }

        [Test]
        public void Remove_Root_Rejected()
        {
            var d = Fresh();
            var r = PartTreeMutator.Apply(d.Tree, new RemovePartOp(d.RootNodeId));
            Assert.IsFalse(r.IsApplied);
            Assert.AreEqual(RejectionReason.RemoveRoot, r.Reason);
            Assert.AreEqual(1, d.Tree.Count);
        }

        [Test]
        public void Remove_UnknownNode_Rejected()
        {
            var d = Fresh();
            var r = PartTreeMutator.Apply(d.Tree, new RemovePartOp(new NodeId(123)));
            Assert.IsFalse(r.IsApplied);
            Assert.AreEqual(RejectionReason.UnknownNode, r.Reason);
        }

        [Test]
        public void Move_Reparents()
        {
            var d = Fresh();
            var a = Add(d, d.RootNodeId, "bottom");
            var c = Add(d, d.RootNodeId, "radial");
            var r = PartTreeMutator.Apply(d.Tree, new MovePartOp(a, c, "bottom", PartPose.Identity));
            Assert.IsTrue(r.IsApplied);
            CollectionAssert.Contains(System.Linq.Enumerable.ToArray(d.Tree.Children(c)), a);
            CollectionAssert.DoesNotContain(System.Linq.Enumerable.ToArray(d.Tree.Children(d.RootNodeId)), a);
        }

        [Test]
        public void Move_UnderOwnDescendant_Rejected_Cycle_TreeUnchanged()
        {
            var d = Fresh();
            var a = Add(d, d.RootNodeId, "bottom");
            var b = Add(d, a, "bottom");
            var r = PartTreeMutator.Apply(d.Tree, new MovePartOp(a, b, "x", PartPose.Identity));
            Assert.IsFalse(r.IsApplied);
            Assert.AreEqual(RejectionReason.WouldCreateCycle, r.Reason);
            // Unchanged: a still under root, b still under a.
            CollectionAssert.Contains(System.Linq.Enumerable.ToArray(d.Tree.Children(d.RootNodeId)), a);
            CollectionAssert.Contains(System.Linq.Enumerable.ToArray(d.Tree.Children(a)), b);
        }

        [Test]
        public void Move_Root_Rejected()
        {
            var d = Fresh();
            var a = Add(d, d.RootNodeId, "bottom");
            var r = PartTreeMutator.Apply(d.Tree, new MovePartOp(d.RootNodeId, a, "x", PartPose.Identity));
            Assert.IsFalse(r.IsApplied);
            Assert.AreEqual(RejectionReason.CannotMoveRoot, r.Reason);
        }

        [Test]
        public void Move_OntoOccupiedAttachPoint_Rejected()
        {
            var d = Fresh();
            var a = Add(d, d.RootNodeId, "bottom");
            var c = Add(d, d.RootNodeId, "radial");
            Add(d, c, "bottom"); // occupy c/bottom
            var r = PartTreeMutator.Apply(d.Tree, new MovePartOp(a, c, "bottom", PartPose.Identity));
            Assert.IsFalse(r.IsApplied);
            Assert.AreEqual(RejectionReason.AttachPointOccupied, r.Reason);
        }
    }
}
