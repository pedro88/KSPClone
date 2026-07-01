#nullable enable annotations

using System;
using System.Collections.Generic;
using KSPClone.Construction;
using KSPClone.Net;

namespace KSPClone.Client
{
    /// <summary>
    /// Client-side VAB state (M3 wiring): joins the shared demo Design, holds the
    /// server-driven <see cref="DesignReplica"/> and lock view, and turns UI
    /// intents into edit-op / lock / launch messages. Edits are not applied
    /// optimistically — the server's broadcast (which the submitter also receives)
    /// is what mutates the replica, so the client always shows authoritative
    /// state and can't diverge.
    /// </summary>
    public sealed class ClientVabModel
    {
        private readonly ClientNetPeer _peer;
        private readonly Dictionary<NodeId, Guid> _locks = new();
        private long _tempCounter = -1; // client temp ids are negative; the server assigns the real id

        public PartCatalog Catalog { get; } = StockParts.Catalog();
        public DesignId DesignId { get; } = StockParts.DemoDesignId;
        public DesignReplica? Replica { get; private set; }
        public IReadOnlyDictionary<NodeId, Guid> Locks => _locks;
        public string LastAck { get; private set; } = "";

        public ClientVabModel(ClientNetPeer peer)
        {
            _peer = peer;
            peer.DesignSnapshotReceived += OnSnapshot;
            peer.EditOpBroadcastReceived += OnBroadcast;
            peer.EditOpAckReceived += OnAck;
            peer.LockBroadcastReceived += OnLock;
        }

        public bool Ready => Replica != null;
        public void Join() => _peer.JoinDesign(DesignId);

        public void AddPart(PartTypeId type, NodeId parent, string attach) =>
            _peer.SubmitEditOp(DesignId, _tempCounter--,
                new AddPartOp(new NodeId(0), type, parent, attach, PartPose.Identity));

        public void RemovePart(NodeId node) =>
            _peer.SubmitEditOp(DesignId, _tempCounter--, new RemovePartOp(node));

        public void MovePart(NodeId node, NodeId newParent, string attach) =>
            _peer.SubmitEditOp(DesignId, _tempCounter--,
                new MovePartOp(node, newParent, attach, PartPose.Identity));

        public void ClaimLock(NodeId node) => _peer.ClaimLock(DesignId, node);
        public void ReleaseLock(NodeId node) => _peer.ReleaseLock(DesignId, node);
        public void Launch() => _peer.LaunchDesign(DesignId);

        /// <summary>Attach-point keys of a node's part type that no child occupies yet.</summary>
        public IEnumerable<string> FreeAttachPoints(NodeId node)
        {
            if (Replica is null || !Replica.Tree.TryGet(node, out var n) || !Catalog.TryGet(n.PartType, out var type))
                yield break;
            var used = new HashSet<string>();
            foreach (var childId in Replica.Tree.Children(node))
                if (Replica.Tree.TryGet(childId, out var c)) used.Add(c.AttachPoint);
            foreach (var ap in type.AttachPoints)
                if (!used.Contains(ap.Key)) yield return ap.Key;
        }

        private void OnSnapshot(DesignSnapshotMessage s)
        {
            var tree = DesignWireCodec.BuildTree(s);
            var design = Design.Restore(s.DesignId, "", tree, nextNodeId: 1, appliedSeq: s.Seq);
            Replica = new DesignReplica(design, s.RootPartType, s.Seq);
        }

        private void OnBroadcast(EditOpBroadcastMessage b)
        {
            if (b.DesignId.Equals(DesignId)) Replica?.ApplyBroadcast(b.Op);
        }

        private void OnAck(EditOpAckMessage a) =>
            LastAck = a.Applied ? $"seq {a.Seq}, node {a.AssignedNodeId.Value}" : $"rejected: {a.Reason}";

        private void OnLock(LockBroadcastMessage l)
        {
            if (l.Locked) _locks[l.NodeId] = l.Holder;
            else _locks.Remove(l.NodeId);
        }
    }
}
