#nullable enable annotations

using System;
using NUnit.Framework;
using KSPClone.Construction;
using KSPClone.Net;

namespace KSPClone.SimCore.Tests
{
    /// <summary>
    /// Design replication wire round-trips (M3-T04): submit/ack/broadcast/snapshot
    /// and every edit-op kind survive encode→decode intact.
    /// </summary>
    public sealed class DesignWireCodecTests
    {
        private static readonly PartTypeId Pod = new("command-pod");
        private static readonly PartTypeId Tank = new("fuel-tank");

        [Test]
        public void Submit_RoundTrips_EveryOpKind()
        {
            var d = DesignId.New();
            var pose = new PartPose(1, 2, 3, 0.1, 0.2, 0.3, 0.9);

            var add = new EditOpSubmitMessage(d, 7, new AddPartOp(new NodeId(5), Tank, new NodeId(1), "bottom", pose));
            var back = DesignWireCodec.DecodeSubmit(DesignWireCodec.EncodeSubmit(add));
            Assert.AreEqual(MessageType.EditOpSubmit, WireCodec.PeekType(DesignWireCodec.EncodeSubmit(add)));
            Assert.AreEqual(7L, back.ClientTempId);
            var a = (AddPartOp)back.Op;
            Assert.AreEqual(new NodeId(5), a.NewNodeId);
            Assert.IsTrue(a.PartType.Equals(Tank));
            Assert.AreEqual(new NodeId(1), a.ParentNodeId);
            Assert.AreEqual("bottom", a.AttachPoint);
            Assert.AreEqual(1.0, a.LocalPose.Px, 1e-12);
            Assert.AreEqual(0.9, a.LocalPose.Qw, 1e-12);

            var rem = new EditOpSubmitMessage(d, 8, new RemovePartOp(new NodeId(5)));
            var remBack = (RemovePartOp)DesignWireCodec.DecodeSubmit(DesignWireCodec.EncodeSubmit(rem)).Op;
            Assert.AreEqual(new NodeId(5), remBack.NodeId);

            var mov = new EditOpSubmitMessage(d, 9, new MovePartOp(new NodeId(5), new NodeId(2), "radial", pose));
            var movBack = (MovePartOp)DesignWireCodec.DecodeSubmit(DesignWireCodec.EncodeSubmit(mov)).Op;
            Assert.AreEqual(new NodeId(2), movBack.NewParentNodeId);
            Assert.AreEqual("radial", movBack.NewAttachPoint);
        }

        [Test]
        public void Ack_RoundTrips()
        {
            var d = DesignId.New();
            var m = new EditOpAckMessage(d, 42, true, 7, new NodeId(11), RejectionReason.UnknownNode);
            var back = DesignWireCodec.DecodeAck(DesignWireCodec.EncodeAck(m));
            Assert.AreEqual(42L, back.ClientTempId);
            Assert.IsTrue(back.Applied);
            Assert.AreEqual(7L, back.Seq);
            Assert.AreEqual(new NodeId(11), back.AssignedNodeId);
        }

        [Test]
        public void Broadcast_RoundTrips_WithSourcePlayer()
        {
            var d = DesignId.New();
            var src = Guid.NewGuid();
            var m = new EditOpBroadcastMessage(d, new SequencedEditOp(3, src,
                new AddPartOp(new NodeId(4), Tank, new NodeId(1), "top", PartPose.Identity)));
            var back = DesignWireCodec.DecodeBroadcast(DesignWireCodec.EncodeBroadcast(m));
            Assert.AreEqual(3L, back.Op.Seq);
            Assert.AreEqual(src, back.Op.SourcePlayer);
            Assert.IsInstanceOf<AddPartOp>(back.Op.Op);
        }

        [Test]
        public void Snapshot_RoundTrips_AndRebuildsTree()
        {
            // Build a small tree, snapshot it, decode, rebuild, compare.
            var d = Design.Create(DesignId.New(), "r", Pod);
            var a = d.AllocateNodeId(); d.Tree.Add(new PartNode(a, Tank, d.RootNodeId, "bottom", PartPose.Identity));
            var b = d.AllocateNodeId(); d.Tree.Add(new PartNode(b, Tank, a, "bottom", PartPose.Identity));

            var nodes = new System.Collections.Generic.List<PartNode>();
            foreach (var id in d.Tree.Subtree(d.RootNodeId)) { d.Tree.TryGet(id, out var n); nodes.Add(n); }
            var msg = new DesignSnapshotMessage(d.Id, 2, Pod, nodes);

            var back = DesignWireCodec.DecodeSnapshot(DesignWireCodec.EncodeSnapshot(msg));
            Assert.AreEqual(2L, back.Seq);
            Assert.IsTrue(back.RootPartType.Equals(Pod));
            var tree = DesignWireCodec.BuildTree(back);
            Assert.AreEqual(3, tree.Count);
            Assert.IsTrue(tree.Contains(a));
            Assert.IsTrue(tree.Contains(b));
        }
    }
}
