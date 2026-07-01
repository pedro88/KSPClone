#nullable enable annotations

using System;
using System.Collections.Generic;
using System.IO;
using KSPClone.Construction;

namespace KSPClone.Net
{
    // --- Design/VAB replication messages (M3-T04). Player identity is taken from
    // the connection server-side, so client→server messages don't carry it. ---

    public readonly struct JoinDesignMessage { public readonly DesignId DesignId; public JoinDesignMessage(DesignId d) => DesignId = d; }
    public readonly struct LeaveDesignMessage { public readonly DesignId DesignId; public LeaveDesignMessage(DesignId d) => DesignId = d; }

    /// <summary>client→server submit: a temp id maps the coming ack back to a client-local add.</summary>
    public readonly struct EditOpSubmitMessage
    {
        public readonly DesignId DesignId;
        public readonly long ClientTempId;
        public readonly EditOp Op;
        public EditOpSubmitMessage(DesignId d, long tempId, EditOp op) { DesignId = d; ClientTempId = tempId; Op = op; }
    }

    /// <summary>server→submitter: outcome of a submit (temp id → seq + assigned node id, or rejection).</summary>
    public readonly struct EditOpAckMessage
    {
        public readonly DesignId DesignId;
        public readonly long ClientTempId;
        public readonly bool Applied;
        public readonly long Seq;
        public readonly NodeId AssignedNodeId;
        public readonly RejectionReason Reason;
        public EditOpAckMessage(DesignId d, long tempId, bool applied, long seq, NodeId assigned, RejectionReason reason)
        { DesignId = d; ClientTempId = tempId; Applied = applied; Seq = seq; AssignedNodeId = assigned; Reason = reason; }
    }

    /// <summary>server→all editors: one accepted, sequenced op.</summary>
    public readonly struct EditOpBroadcastMessage
    {
        public readonly DesignId DesignId;
        public readonly SequencedEditOp Op;
        public EditOpBroadcastMessage(DesignId d, SequencedEditOp op) { DesignId = d; Op = op; }
    }

    /// <summary>client→server: claim/release a subtree lock on a node (M3-T06).</summary>
    public readonly struct LockRequestMessage
    {
        public readonly DesignId DesignId;
        public readonly NodeId NodeId;
        public LockRequestMessage(DesignId d, NodeId n) { DesignId = d; NodeId = n; }
    }

    /// <summary>server→all editors: a lock changed (claimed or released).</summary>
    public readonly struct LockBroadcastMessage
    {
        public readonly DesignId DesignId;
        public readonly NodeId NodeId;
        public readonly Guid Holder;   // default when released
        public readonly bool Locked;   // true = claimed, false = released
        public LockBroadcastMessage(DesignId d, NodeId n, Guid holder, bool locked)
        { DesignId = d; NodeId = n; Holder = holder; Locked = locked; }
    }

    /// <summary>client→server: launch a Design onto the pad (M3-T08).</summary>
    public readonly struct LaunchDesignMessage
    {
        public readonly DesignId DesignId;
        public LaunchDesignMessage(DesignId d) { DesignId = d; }
    }

    /// <summary>server→launcher: the Design was launched as this new Vessel (take control of it).</summary>
    public readonly struct DesignLaunchedMessage
    {
        public readonly DesignId DesignId;
        public readonly KSPClone.SimCore.VesselId VesselId;
        public DesignLaunchedMessage(DesignId d, KSPClone.SimCore.VesselId v) { DesignId = d; VesselId = v; }
    }

    /// <summary>server→joining editor: full-tree resync baseline (nodes + current seq).</summary>
    public readonly struct DesignSnapshotMessage
    {
        public readonly DesignId DesignId;
        public readonly long Seq;
        public readonly PartTypeId RootPartType;
        public readonly IReadOnlyList<PartNode> Nodes; // root first, parent-before-child
        public DesignSnapshotMessage(DesignId d, long seq, PartTypeId rootType, IReadOnlyList<PartNode> nodes)
        { DesignId = d; Seq = seq; RootPartType = rootType; Nodes = nodes; }
    }

    /// <summary>
    /// Wire codec for the Design replication channel (M3-T04). Kept apart from
    /// <see cref="WireCodec"/> but shares its <see cref="MessageType"/> tag space.
    /// </summary>
    public static class DesignWireCodec
    {
        public static byte[] EncodeJoin(JoinDesignMessage m) => TagAndId(MessageType.JoinDesign, m.DesignId);
        public static byte[] EncodeLeave(LeaveDesignMessage m) => TagAndId(MessageType.LeaveDesign, m.DesignId);

        public static DesignId DecodeJoin(byte[] p) => ReadTaggedId(p);
        public static DesignId DecodeLeave(byte[] p) => ReadTaggedId(p);

        public static byte[] EncodeLaunch(LaunchDesignMessage m) => TagAndId(MessageType.LaunchDesign, m.DesignId);
        public static DesignId DecodeLaunch(byte[] p) => ReadTaggedId(p);

        public static byte[] EncodeLaunched(DesignLaunchedMessage m)
        {
            using var ms = new MemoryStream(); using var w = new BinaryWriter(ms);
            w.Write((byte)MessageType.DesignLaunched);
            WriteDesignId(w, m.DesignId);
            w.Write(m.VesselId.Value.ToByteArray());
            return ms.ToArray();
        }

        public static DesignLaunchedMessage DecodeLaunched(byte[] p)
        {
            using var ms = new MemoryStream(p); using var r = new BinaryReader(ms);
            r.ReadByte();
            var d = ReadDesignId(r);
            var v = new KSPClone.SimCore.VesselId(new Guid(r.ReadBytes(16)));
            return new DesignLaunchedMessage(d, v);
        }

        public static byte[] EncodeClaimLock(LockRequestMessage m) => TagLockReq(MessageType.ClaimLock, m);
        public static byte[] EncodeReleaseLock(LockRequestMessage m) => TagLockReq(MessageType.ReleaseLock, m);

        public static LockRequestMessage DecodeLockRequest(byte[] p)
        {
            using var ms = new MemoryStream(p); using var r = new BinaryReader(ms);
            r.ReadByte();
            var d = ReadDesignId(r);
            var n = new NodeId(r.ReadInt64());
            return new LockRequestMessage(d, n);
        }

        public static byte[] EncodeLockBroadcast(LockBroadcastMessage m)
        {
            using var ms = new MemoryStream(); using var w = new BinaryWriter(ms);
            w.Write((byte)MessageType.LockBroadcast);
            WriteDesignId(w, m.DesignId);
            w.Write(m.NodeId.Value);
            w.Write(m.Holder.ToByteArray());
            w.Write(m.Locked);
            return ms.ToArray();
        }

        public static LockBroadcastMessage DecodeLockBroadcast(byte[] p)
        {
            using var ms = new MemoryStream(p); using var r = new BinaryReader(ms);
            r.ReadByte();
            var d = ReadDesignId(r);
            var n = new NodeId(r.ReadInt64());
            var holder = new Guid(r.ReadBytes(16));
            var locked = r.ReadBoolean();
            return new LockBroadcastMessage(d, n, holder, locked);
        }

        private static byte[] TagLockReq(MessageType t, LockRequestMessage m)
        {
            using var ms = new MemoryStream(); using var w = new BinaryWriter(ms);
            w.Write((byte)t); WriteDesignId(w, m.DesignId); w.Write(m.NodeId.Value);
            return ms.ToArray();
        }

        public static byte[] EncodeSubmit(EditOpSubmitMessage m)
        {
            using var ms = new MemoryStream(); using var w = new BinaryWriter(ms);
            w.Write((byte)MessageType.EditOpSubmit);
            WriteDesignId(w, m.DesignId);
            w.Write(m.ClientTempId);
            WriteEditOp(w, m.Op);
            return ms.ToArray();
        }

        public static EditOpSubmitMessage DecodeSubmit(byte[] p)
        {
            using var ms = new MemoryStream(p); using var r = new BinaryReader(ms);
            r.ReadByte();
            var d = ReadDesignId(r);
            var tempId = r.ReadInt64();
            var op = ReadEditOp(r);
            return new EditOpSubmitMessage(d, tempId, op);
        }

        public static byte[] EncodeAck(EditOpAckMessage m)
        {
            using var ms = new MemoryStream(); using var w = new BinaryWriter(ms);
            w.Write((byte)MessageType.EditOpAck);
            WriteDesignId(w, m.DesignId);
            w.Write(m.ClientTempId);
            w.Write(m.Applied);
            w.Write(m.Seq);
            w.Write(m.AssignedNodeId.Value);
            w.Write((int)m.Reason);
            return ms.ToArray();
        }

        public static EditOpAckMessage DecodeAck(byte[] p)
        {
            using var ms = new MemoryStream(p); using var r = new BinaryReader(ms);
            r.ReadByte();
            var d = ReadDesignId(r);
            var tempId = r.ReadInt64();
            var applied = r.ReadBoolean();
            var seq = r.ReadInt64();
            var assigned = new NodeId(r.ReadInt64());
            var reason = (RejectionReason)r.ReadInt32();
            return new EditOpAckMessage(d, tempId, applied, seq, assigned, reason);
        }

        public static byte[] EncodeBroadcast(EditOpBroadcastMessage m)
        {
            using var ms = new MemoryStream(); using var w = new BinaryWriter(ms);
            w.Write((byte)MessageType.EditOpBroadcast);
            WriteDesignId(w, m.DesignId);
            w.Write(m.Op.Seq);
            w.Write(m.Op.SourcePlayer.ToByteArray());
            WriteEditOp(w, m.Op.Op);
            return ms.ToArray();
        }

        public static EditOpBroadcastMessage DecodeBroadcast(byte[] p)
        {
            using var ms = new MemoryStream(p); using var r = new BinaryReader(ms);
            r.ReadByte();
            var d = ReadDesignId(r);
            var seq = r.ReadInt64();
            var src = new Guid(r.ReadBytes(16));
            var op = ReadEditOp(r);
            return new EditOpBroadcastMessage(d, new SequencedEditOp(seq, src, op));
        }

        public static byte[] EncodeSnapshot(DesignSnapshotMessage m)
        {
            using var ms = new MemoryStream(); using var w = new BinaryWriter(ms);
            w.Write((byte)MessageType.DesignSnapshot);
            WriteDesignId(w, m.DesignId);
            w.Write(m.Seq);
            w.Write(m.RootPartType.Value);
            w.Write(m.Nodes.Count);
            foreach (var n in m.Nodes)
            {
                w.Write(n.Id.Value);
                w.Write(n.PartType.Value);
                w.Write(n.Parent.Value);
                w.Write(n.AttachPoint);
                WritePose(w, n.LocalPose);
            }
            return ms.ToArray();
        }

        public static DesignSnapshotMessage DecodeSnapshot(byte[] p)
        {
            using var ms = new MemoryStream(p); using var r = new BinaryReader(ms);
            r.ReadByte();
            var d = ReadDesignId(r);
            var seq = r.ReadInt64();
            var rootType = new PartTypeId(r.ReadString());
            var count = r.ReadInt32();
            var nodes = new List<PartNode>(count);
            for (int i = 0; i < count; i++)
            {
                var id = new NodeId(r.ReadInt64());
                var pt = new PartTypeId(r.ReadString());
                var parent = new NodeId(r.ReadInt64());
                var attach = r.ReadString();
                var pose = ReadPose(r);
                nodes.Add(new PartNode(id, pt, parent, attach, pose));
            }
            return new DesignSnapshotMessage(d, seq, rootType, nodes);
        }

        /// <summary>Rebuild a PartTree from a snapshot's node list (root first, parent-before-child).</summary>
        public static PartTree BuildTree(DesignSnapshotMessage snap)
        {
            if (snap.Nodes.Count == 0) throw new ArgumentException("Empty snapshot.");
            var tree = new PartTree(snap.Nodes[0]); // root
            for (int i = 1; i < snap.Nodes.Count; i++) tree.Add(snap.Nodes[i]);
            return tree;
        }

        // --- primitives ---

        private static void WriteEditOp(BinaryWriter w, EditOp op)
        {
            switch (op)
            {
                case AddPartOp a:
                    w.Write((byte)1);
                    w.Write(a.NewNodeId.Value);
                    w.Write(a.PartType.Value);
                    w.Write(a.ParentNodeId.Value);
                    w.Write(a.AttachPoint);
                    WritePose(w, a.LocalPose);
                    break;
                case RemovePartOp rm:
                    w.Write((byte)2);
                    w.Write(rm.NodeId.Value);
                    break;
                case MovePartOp mv:
                    w.Write((byte)3);
                    w.Write(mv.NodeId.Value);
                    w.Write(mv.NewParentNodeId.Value);
                    w.Write(mv.NewAttachPoint);
                    WritePose(w, mv.NewLocalPose);
                    break;
                default:
                    throw new ArgumentException($"Unknown edit op {op.GetType().Name}.");
            }
        }

        private static EditOp ReadEditOp(BinaryReader r)
        {
            var tag = r.ReadByte();
            switch (tag)
            {
                case 1:
                    return new AddPartOp(new NodeId(r.ReadInt64()), new PartTypeId(r.ReadString()),
                        new NodeId(r.ReadInt64()), r.ReadString(), ReadPose(r));
                case 2:
                    return new RemovePartOp(new NodeId(r.ReadInt64()));
                case 3:
                    return new MovePartOp(new NodeId(r.ReadInt64()), new NodeId(r.ReadInt64()),
                        r.ReadString(), ReadPose(r));
                default:
                    throw new InvalidDataException($"Unknown edit-op tag {tag}.");
            }
        }

        private static void WritePose(BinaryWriter w, PartPose p)
        {
            w.Write(p.Px); w.Write(p.Py); w.Write(p.Pz);
            w.Write(p.Qx); w.Write(p.Qy); w.Write(p.Qz); w.Write(p.Qw);
        }

        private static PartPose ReadPose(BinaryReader r) =>
            new(r.ReadDouble(), r.ReadDouble(), r.ReadDouble(),
                r.ReadDouble(), r.ReadDouble(), r.ReadDouble(), r.ReadDouble());

        private static void WriteDesignId(BinaryWriter w, DesignId id) => w.Write(id.Value.ToByteArray());
        private static DesignId ReadDesignId(BinaryReader r) => new(new Guid(r.ReadBytes(16)));

        private static byte[] TagAndId(MessageType t, DesignId id)
        {
            using var ms = new MemoryStream(); using var w = new BinaryWriter(ms);
            w.Write((byte)t); WriteDesignId(w, id);
            return ms.ToArray();
        }

        private static DesignId ReadTaggedId(byte[] p)
        {
            using var ms = new MemoryStream(p); using var r = new BinaryReader(ms);
            r.ReadByte();
            return ReadDesignId(r);
        }
    }
}
