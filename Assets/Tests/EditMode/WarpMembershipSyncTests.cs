#nullable enable annotations

using System.Linq;
using NUnit.Framework;
using KSPClone.SimCore;

namespace KSPClone.SimCore.Tests
{
    public sealed class WarpMembershipSyncTests
    {
        [Test]
        public void ConnectWhileIdle_DoesNothing()
        {
            var conns = new ConnectionRegistry();
            var fsm = new WarpStateMachine(new MasterClock(), conns);
            new WarpMembershipSync(conns, fsm);
            conns.AddNew();
            Assert.AreEqual(WarpState.Idle, fsm.State);
        }

        [Test]
        public void ConnectWhileVoting_AddsToRequiredSet()
        {
            var conns = new ConnectionRegistry();
            var fsm = new WarpStateMachine(new MasterClock(), conns);
            new WarpMembershipSync(conns, fsm);

            var a = conns.AddNew();
            var b = conns.AddNew();
            fsm.RequestWarp(new WarpRequest(a.Id, 100.0, WarpKind.OnRails));
            Assert.AreEqual(WarpState.Voting, fsm.State);

            var latecomer = conns.AddNew();
            Assert.IsTrue(fsm.Vote.Required.Contains(latecomer.Id));
        }

        [Test]
        public void DisconnectWhileVoting_DropsFromRequired_AllowsUnanimity()
        {
            var conns = new ConnectionRegistry();
            var clock = new MasterClock();
            var fsm = new WarpStateMachine(clock, conns);
            new WarpMembershipSync(conns, fsm);

            var a = conns.AddNew();
            var b = conns.AddNew();
            var c = conns.AddNew();
            fsm.RequestWarp(new WarpRequest(a.Id, 100.0, WarpKind.OnRails));
            // a is approved; b, c must still approve
            Assert.AreEqual(2, fsm.Vote.Pending.Count);

            // b disconnects; a is already approved; c remains → still 2 required (a + c)
            conns.Remove(b.Id);
            Assert.AreEqual(2, fsm.Vote.Required.Count);
        }

        [Test]
        public void DisconnectWhileVoting_LastBlocker_CompletesUnanimity_AndActivates()
        {
            var conns = new ConnectionRegistry();
            var clock = new MasterClock();
            var fsm = new WarpStateMachine(clock, conns);
            new WarpMembershipSync(conns, fsm);

            var a = conns.AddNew();
            var b = conns.AddNew();
            var c = conns.AddNew();
            fsm.RequestWarp(new WarpRequest(a.Id, 1000.0, WarpKind.OnRails)); // a approved
            fsm.Approve(b.Id);                                                // a, b approved; c pending
            Assert.AreEqual(WarpState.Voting, fsm.State);

            // The only remaining blocker disconnects → the rest are unanimous,
            // so the warp must start (TIME-5).
            conns.Remove(c.Id);
            Assert.AreEqual(WarpState.Active, fsm.State,
                "Removing the last non-approver must complete unanimity and activate.");
            Assert.AreEqual(1000.0, clock.Rate);
        }

        [Test]
        public void DisconnectWhileActive_DropsVoter_KeepsWarpActive()
        {
            var conns = new ConnectionRegistry();
            var clock = new MasterClock();
            var fsm = new WarpStateMachine(clock, conns);
            new WarpMembershipSync(conns, fsm);

            var a = conns.AddNew();
            var b = conns.AddNew();
            fsm.RequestWarp(new WarpRequest(a.Id, 100.0, WarpKind.OnRails));
            fsm.Approve(b.Id);
            Assert.AreEqual(WarpState.Active, fsm.State);
            Assert.AreEqual(100.0, clock.Rate);

            conns.Remove(b.Id);
            Assert.AreEqual(WarpState.Active, fsm.State,
                "TIME-5: disconnect during active warp must NOT halt it.");
            Assert.AreEqual(100.0, clock.Rate);
        }

        [Test]
        public void ConnectWhileActive_HaltsToBaseline()
        {
            var conns = new ConnectionRegistry();
            var clock = new MasterClock();
            var fsm = new WarpStateMachine(clock, conns);
            new WarpMembershipSync(conns, fsm);

            var a = conns.AddNew();
            fsm.RequestWarp(new WarpRequest(a.Id, 100.0, WarpKind.OnRails));
            Assert.AreEqual(WarpState.Active, fsm.State);

            WarpStateMachine.WarpEndReason? seenReason = null;
            fsm.WarpEnded += (_, why) => seenReason = why;

            conns.AddNew(); // new player connects mid-warp

            Assert.AreEqual(WarpState.Idle, fsm.State,
                "TIME-6: connect during active warp must halt to baseline.");
            Assert.AreEqual(1.0, clock.Rate);
            Assert.AreEqual(WarpStateMachine.WarpEndReason.HaltedByConnection, seenReason);
        }

        [Test]
        public void DisconnectWhileIdle_DoesNothing()
        {
            var conns = new ConnectionRegistry();
            var clock = new MasterClock();
            var fsm = new WarpStateMachine(clock, conns);
            new WarpMembershipSync(conns, fsm);

            var a = conns.AddNew();
            conns.Remove(a.Id);
            Assert.AreEqual(WarpState.Idle, fsm.State);
        }
    }
}