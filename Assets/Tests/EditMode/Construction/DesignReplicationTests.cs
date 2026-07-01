#nullable enable annotations

using System;
using System.Collections.Generic;
using NUnit.Framework;
using KSPClone.Construction;

namespace KSPClone.Construction.Tests
{
    /// <summary>
    /// Ordered broadcast + convergence (M3-T04, BUILD-2): the client replica
    /// applies ops strictly in seq order (buffering gaps), and two editors driven
    /// by the coordinator converge on the server's canonical tree.
    /// </summary>
    public sealed class DesignReplicationTests
    {
        private static readonly PartTypeId Pod = new("pod");
        private static readonly PartTypeId Tank = new("tank");

        [Test]
        public void Replica_AppliesInSeqOrder_BufferingGaps()
        {
            var seed = Design.Create(DesignId.New(), "x", Pod);
            var root = seed.RootNodeId;
            var rep = new DesignReplica(seed, Pod, baselineSeq: 0);
            var g = Guid.NewGuid();

            var op1 = new SequencedEditOp(1, g, new AddPartOp(new NodeId(2), Tank, root, "s1", PartPose.Identity));
            var op2 = new SequencedEditOp(2, g, new AddPartOp(new NodeId(3), Tank, root, "s2", PartPose.Identity));
            var op3 = new SequencedEditOp(3, g, new AddPartOp(new NodeId(4), Tank, root, "s3", PartPose.Identity));

            Assert.AreEqual(BroadcastApply.Applied, rep.ApplyBroadcast(op1));
            Assert.AreEqual(BroadcastApply.Buffered, rep.ApplyBroadcast(op3)); // gap: 2 missing
            Assert.IsTrue(rep.HasBufferedGap);
            Assert.AreEqual(2, rep.Tree.Count);

            Assert.AreEqual(BroadcastApply.Applied, rep.ApplyBroadcast(op2)); // fills gap, drains 3
            Assert.AreEqual(3L, rep.AppliedSeq);
            Assert.AreEqual(4, rep.Tree.Count);
            Assert.IsFalse(rep.HasBufferedGap);

            Assert.AreEqual(BroadcastApply.Duplicate, rep.ApplyBroadcast(op1));
        }

        [Test]
        public void TwoEditors_ConvergeOnServerTree()
        {
            var reg = new DesignRegistry();
            var d = reg.Create("rocket", Pod);
            var svc = new DesignEditService(reg);
            var sessions = new DesignEditorSessions();
            var sink = new FakeSink(Pod);
            var coord = new DesignEditCoordinator(svc, reg, sessions, sink);

            var A = Guid.NewGuid();
            var B = Guid.NewGuid();
            coord.Join(d.Id, A);
            coord.Join(d.Id, B);

            // Concurrent-ish edits from both, interleaved through the one server.
            var r1 = coord.Submit(d.Id, A, 10, new AddPartOp(default, Tank, d.RootNodeId, "a1", PartPose.Identity));
            var r2 = coord.Submit(d.Id, B, 20, new AddPartOp(default, Tank, d.RootNodeId, "b1", PartPose.Identity));
            coord.Submit(d.Id, A, 11, new AddPartOp(default, Tank, r1.AssignedNodeId, "sub", PartPose.Identity));
            coord.Submit(d.Id, B, 21, new MovePartOp(r1.AssignedNodeId, r2.AssignedNodeId, "moved", PartPose.Identity));

            AssertSameStructure(d.Tree, sink.Replica(A).Tree);
            AssertSameStructure(d.Tree, sink.Replica(B).Tree);
            Assert.AreEqual(d.AppliedSeq, sink.Replica(A).AppliedSeq);
        }

        private sealed class FakeSink : IEditOpSink
        {
            private readonly PartTypeId _root;
            private readonly Dictionary<Guid, DesignReplica> _replicas = new();
            public FakeSink(PartTypeId root) => _root = root;

            public DesignReplica Replica(Guid player) => _replicas[player];

            public void Snapshot(Guid player, Design design)
            {
                // Real transport serializes the full tree; in this test players
                // join before any edits, so a fresh root + baseline seq suffices.
                var seed = Design.Create(design.Id, "replica", _root);
                _replicas[player] = new DesignReplica(seed, _root, design.AppliedSeq);
            }

            public void Broadcast(IReadOnlyCollection<Guid> members, DesignId designId, SequencedEditOp op)
            {
                foreach (var m in members)
                    if (_replicas.TryGetValue(m, out var rep)) rep.ApplyBroadcast(op);
            }

            public void Ack(Guid player, DesignId designId, long clientTempId, SubmitResult result) { }
        }

        private static void AssertSameStructure(PartTree expected, PartTree actual)
        {
            Assert.AreEqual(expected.Count, actual.Count, "node count");
            foreach (var id in expected.Subtree(expected.Root))
            {
                Assert.IsTrue(actual.TryGet(id, out var an), $"missing {id}");
                expected.TryGet(id, out var en);
                Assert.AreEqual(en.Parent, an.Parent, $"parent of {id}");
                Assert.AreEqual(en.AttachPoint, an.AttachPoint, $"attach of {id}");
            }
        }
    }
}
