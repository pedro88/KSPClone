#nullable enable annotations

using System;
using System.Collections.Generic;
using NUnit.Framework;
using KSPClone.Construction;

namespace KSPClone.Construction.Tests
{
    /// <summary>
    /// Server-authoritative op-log (M3-T03, BUILD-2): arrival-order sequencing,
    /// server-assigned node ids, and the log-fold == canonical-tree invariant.
    /// </summary>
    public sealed class DesignEditServiceTests
    {
        private static readonly PartTypeId Pod = new("pod");
        private static readonly PartTypeId Tank = new("tank");

        [Test]
        public void Add_AssignsServerNodeId_AndSequences()
        {
            var reg = new DesignRegistry();
            var d = reg.Create("r", Pod);
            var svc = new DesignEditService(reg);

            // Client proposes a bogus temp id; the server ignores it.
            var res = svc.Submit(d.Id, Guid.NewGuid(),
                new AddPartOp(new NodeId(-999), Tank, d.RootNodeId, "bottom", PartPose.Identity));

            Assert.IsTrue(res.IsApplied);
            Assert.AreEqual(1L, res.Seq);
            Assert.AreEqual(new NodeId(2), res.AssignedNodeId, "server assigns the id, not the client");
            Assert.IsTrue(d.Tree.Contains(new NodeId(2)));
            Assert.AreEqual(1L, d.AppliedSeq);
        }

        [Test]
        public void CollidingClientAddIds_BothHonored_WithDistinctServerIds()
        {
            var reg = new DesignRegistry();
            var d = reg.Create("r", Pod);
            var svc = new DesignEditService(reg);
            var a = Guid.NewGuid();
            var b = Guid.NewGuid();

            // Both clients propose the SAME temp id.
            var r1 = svc.Submit(d.Id, a, new AddPartOp(new NodeId(1000), Tank, d.RootNodeId, "s1", PartPose.Identity));
            var r2 = svc.Submit(d.Id, b, new AddPartOp(new NodeId(1000), Tank, d.RootNodeId, "s2", PartPose.Identity));

            Assert.IsTrue(r1.IsApplied && r2.IsApplied);
            Assert.AreNotEqual(r1.AssignedNodeId, r2.AssignedNodeId, "distinct server ids despite colliding temp ids");
            Assert.AreEqual(3, d.Tree.Count);
        }

        [Test]
        public void RejectedOp_NotAppended_SeqUnchanged()
        {
            var reg = new DesignRegistry();
            var d = reg.Create("r", Pod);
            var svc = new DesignEditService(reg);

            svc.Submit(d.Id, Guid.NewGuid(), new AddPartOp(default, Tank, d.RootNodeId, "bottom", PartPose.Identity)); // seq 1
            var before = d.AppliedSeq;
            var bad = svc.Submit(d.Id, Guid.NewGuid(), new RemovePartOp(new NodeId(9999))); // unknown → reject

            Assert.IsFalse(bad.IsApplied);
            Assert.AreEqual(RejectionReason.UnknownNode, bad.Reason);
            Assert.AreEqual(before, d.AppliedSeq, "rejected ops don't advance the seq");
            Assert.IsTrue(reg.TryGetLog(d.Id, out var log));
            Assert.AreEqual(1, log.Count);
        }

        [Test]
        public void InterleavedStreams_ContiguousSeq_AndFoldEqualsCanonicalTree()
        {
            var reg = new DesignRegistry();
            var d = reg.Create("r", Pod);
            var svc = new DesignEditService(reg);
            var a = Guid.NewGuid();
            var b = Guid.NewGuid();

            // Two players interleave adds and a move.
            var s1 = svc.Submit(d.Id, a, new AddPartOp(default, Tank, d.RootNodeId, "a1", PartPose.Identity));
            var s2 = svc.Submit(d.Id, b, new AddPartOp(default, Tank, d.RootNodeId, "b1", PartPose.Identity));
            var s3 = svc.Submit(d.Id, a, new AddPartOp(default, Tank, s1.AssignedNodeId, "sub", PartPose.Identity));
            var s4 = svc.Submit(d.Id, b, new MovePartOp(s3.AssignedNodeId, s2.AssignedNodeId, "moved", PartPose.Identity));
            Assert.IsTrue(s1.IsApplied && s2.IsApplied && s3.IsApplied && s4.IsApplied);

            Assert.IsTrue(reg.TryGetLog(d.Id, out var log));
            // Contiguous 1..4.
            for (int i = 0; i < log.Count; i++) Assert.AreEqual(i + 1L, log.Ops[i].Seq);

            // Folding the log from a fresh root reproduces the canonical tree.
            var folded = DesignEditService.Fold(Pod, log);
            AssertSameStructure(d.Tree, folded);
        }

        private static void AssertSameStructure(PartTree expected, PartTree actual)
        {
            Assert.AreEqual(expected.Count, actual.Count, "node count");
            foreach (var id in Subtree(expected, expected.Root))
            {
                Assert.IsTrue(actual.TryGet(id, out var an), $"missing {id}");
                expected.TryGet(id, out var en);
                Assert.AreEqual(en.Parent, an.Parent, $"parent of {id}");
                Assert.AreEqual(en.AttachPoint, an.AttachPoint, $"attach of {id}");
            }
        }

        private static IEnumerable<NodeId> Subtree(PartTree t, NodeId root)
        {
            foreach (var n in t.Subtree(root)) yield return n;
        }
    }
}
