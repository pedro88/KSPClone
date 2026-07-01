#nullable enable annotations

using System;
using NUnit.Framework;
using KSPClone.Construction;

namespace KSPClone.Construction.Tests
{
    /// <summary>
    /// Subtree locks (M3-T06/T07, BUILD-3): claim/deny/release/auto-release, and
    /// enforcement of a holder's lock against foreign edit ops (while the holder's
    /// own ops still apply).
    /// </summary>
    public sealed class SubtreeLockTests
    {
        private static readonly PartTypeId Pod = new("pod");
        private static readonly PartTypeId Tank = new("tank");

        private static (DesignRegistry reg, Design d, DesignEditService svc, NodeId upper, NodeId inner) Setup()
        {
            var reg = new DesignRegistry();
            var d = reg.Create("r", Pod);
            var svc = new DesignEditService(reg);
            var upper = svc.Submit(d.Id, Guid.NewGuid(),
                new AddPartOp(default, Tank, d.RootNodeId, "top", PartPose.Identity)).AssignedNodeId;
            var inner = svc.Submit(d.Id, Guid.NewGuid(),
                new AddPartOp(default, Tank, upper, "bottom", PartPose.Identity)).AssignedNodeId;
            return (reg, d, svc, upper, inner);
        }

        [Test]
        public void Claim_Grants_ThenOverlappingClaimDenied()
        {
            var (_, d, _, upper, inner) = Setup();
            var mgr = new SubtreeLockManager();
            var A = Guid.NewGuid();
            var B = Guid.NewGuid();

            Assert.IsTrue(mgr.Claim(d.Id, d.Tree, upper, A).Granted);

            var onNode = mgr.Claim(d.Id, d.Tree, upper, B);
            Assert.IsFalse(onNode.Granted);
            Assert.AreEqual(A, onNode.HeldBy);

            var onDescendant = mgr.Claim(d.Id, d.Tree, inner, B); // inside A's subtree
            Assert.IsFalse(onDescendant.Granted, "a node inside a locked subtree is denied");
            Assert.AreEqual(A, onDescendant.HeldBy);

            var onAncestor = mgr.Claim(d.Id, d.Tree, d.RootNodeId, B); // ancestor of A's lock
            Assert.IsFalse(onAncestor.Granted, "an ancestor of a locked subtree is denied");
        }

        [Test]
        public void Release_ByHolderOnly_ThenReclaimGranted()
        {
            var (_, d, _, upper, _) = Setup();
            var mgr = new SubtreeLockManager();
            var A = Guid.NewGuid();
            var B = Guid.NewGuid();
            mgr.Claim(d.Id, d.Tree, upper, A);

            Assert.IsFalse(mgr.Release(d.Id, upper, B), "non-holder cannot release");
            Assert.IsTrue(mgr.Release(d.Id, upper, A));
            Assert.IsTrue(mgr.Claim(d.Id, d.Tree, upper, B).Granted, "released lock is re-claimable");
        }

        [Test]
        public void AutoRelease_OnPlayerLeave()
        {
            var (_, d, _, upper, _) = Setup();
            var mgr = new SubtreeLockManager();
            var A = Guid.NewGuid();
            var B = Guid.NewGuid();
            mgr.Claim(d.Id, d.Tree, upper, A);

            mgr.ReleaseAllForPlayer(A); // A disconnects
            Assert.IsTrue(mgr.Claim(d.Id, d.Tree, upper, B).Granted);
        }

        [Test]
        public void ForeignEditOp_InLockedSubtree_Rejected_HoldersOwnApplies()
        {
            var (_, d, svc, upper, inner) = Setup();
            var mgr = new SubtreeLockManager();
            svc.PreApplyGate = mgr.CheckOp; // wire T07 enforcement
            var A = Guid.NewGuid();
            var B = Guid.NewGuid();
            mgr.Claim(d.Id, d.Tree, upper, A);

            // B tries to remove a node inside A's locked subtree → rejected.
            var bRemove = svc.Submit(d.Id, B, new RemovePartOp(inner));
            Assert.IsFalse(bRemove.IsApplied);
            Assert.AreEqual(RejectionReason.SubtreeLocked, bRemove.Reason);
            Assert.AreEqual(A, (Guid)bRemove.RejectedBy!);
            Assert.IsTrue(d.Tree.Contains(inner), "tree unchanged by the rejected foreign op");

            // A's own op inside the subtree still applies.
            var aAdd = svc.Submit(d.Id, A, new AddPartOp(default, Tank, inner, "bottom", PartPose.Identity));
            Assert.IsTrue(aAdd.IsApplied);

            // After A releases, B's op applies.
            mgr.Release(d.Id, upper, A);
            var bRemove2 = svc.Submit(d.Id, B, new RemovePartOp(inner));
            Assert.IsTrue(bRemove2.IsApplied);
        }
    }
}
