using System.Collections.Generic;
using NUnit.Framework;
using KSPClone.SimCore;

namespace KSPClone.SimCore.Tests
{
    public sealed class WarpStateMachineTests
    {
        private MasterClock _clock = null!;
        private ConnectionRegistry _conns = null!;
        private WarpStateMachine _fsm = null!;
        private List<PlayerSession> _sessions = null!;

        [SetUp]
        public void SetUp()
        {
            _clock = new MasterClock();
            _conns = new ConnectionRegistry();
            _fsm = new WarpStateMachine(_clock, _conns);
            _sessions = new List<PlayerSession>
            {
                _conns.AddNew(),
                _conns.AddNew(),
                _conns.AddNew(),
            };
        }

        [Test]
        public void Idle_StartsAtBaselineRate()
        {
            Assert.AreEqual(WarpState.Idle, _fsm.State);
            Assert.AreEqual(1.0, _clock.Rate);
        }

        [Test]
        public void RequestWarp_FromIdle_TransitionsToVoting_AndCountsRequesterApproval()
        {
            var req = new WarpRequest(_sessions[0].Id, 100.0, WarpKind.OnRails);
            Assert.IsTrue(_fsm.RequestWarp(req));
            Assert.AreEqual(WarpState.Voting, _fsm.State);
            Assert.AreEqual(2, _fsm.Vote.Pending.Count,
                "Requester's approval is pre-counted; 2 of 3 still pending.");
        }

        [Test]
        public void TwoOfThreeApprovals_KeepsVoting_NoRateChange()
        {
            _fsm.RequestWarp(new WarpRequest(_sessions[0].Id, 50.0, WarpKind.OnRails));
            _fsm.Approve(_sessions[1].Id);
            Assert.AreEqual(WarpState.Voting, _fsm.State);
            Assert.AreEqual(1.0, _clock.Rate,
                "MasterClock.Rate must not change until unanimity (Constitution Art. 1).");
        }

        [Test]
        public void UnanimousThreeApprovals_TransitionsToActive_SetsRate()
        {
            var req = new WarpRequest(_sessions[0].Id, 1000.0, WarpKind.OnRails);
            _fsm.RequestWarp(req);
            _fsm.Approve(_sessions[1].Id);
            _fsm.Approve(_sessions[2].Id);

            Assert.AreEqual(WarpState.Active, _fsm.State);
            Assert.AreEqual(1000.0, _clock.Rate);
            Assert.AreEqual(req.Multiplier, _clock.Rate);
        }

        [Test]
        public void WithheldApproval_KeepsVoting_Indefinitely()
        {
            _fsm.RequestWarp(new WarpRequest(_sessions[0].Id, 100.0, WarpKind.OnRails));
            _fsm.Approve(_sessions[1].Id);
            // _sessions[2] never approves
            Assert.AreEqual(WarpState.Voting, _fsm.State);
            Assert.AreEqual(1.0, _clock.Rate);
        }

        [Test]
        public void SoloPlayer_RequestWarp_GoesImmediatelyActive()
        {
            // Drop two of the three so we are solo.
            _conns.Remove(_sessions[1].Id);
            _conns.Remove(_sessions[2].Id);
            var req = new WarpRequest(_sessions[0].Id, 10.0, WarpKind.Physics);
            Assert.IsTrue(_fsm.RequestWarp(req));
            Assert.AreEqual(WarpState.Active, _fsm.State);
            Assert.AreEqual(10.0, _clock.Rate);
        }

        [Test]
        public void Cancel_ByRequester_BeforeUnanimous_ReturnsToIdle()
        {
            _fsm.RequestWarp(new WarpRequest(_sessions[0].Id, 100.0, WarpKind.OnRails));
            Assert.IsTrue(_fsm.Cancel(_sessions[0].Id));
            Assert.AreEqual(WarpState.Idle, _fsm.State);
            Assert.AreEqual(1.0, _clock.Rate);
        }

        [Test]
        public void Cancel_ByNonRequester_IsRejected()
        {
            _fsm.RequestWarp(new WarpRequest(_sessions[0].Id, 100.0, WarpKind.OnRails));
            Assert.IsFalse(_fsm.Cancel(_sessions[1].Id));
            Assert.AreEqual(WarpState.Voting, _fsm.State);
        }

        [Test]
        public void RequestWarp_WhileActive_IsRejected()
        {
            _fsm.RequestWarp(new WarpRequest(_sessions[0].Id, 5.0, WarpKind.Physics));
            Assert.IsFalse(_fsm.RequestWarp(new WarpRequest(_sessions[0].Id, 10.0, WarpKind.Physics)),
                "Warp cannot be re-requested while one is already Active.");
        }

        [Test]
        public void Halt_FromActive_ResetsRate_AndFiresWarpEnded()
        {
            _fsm.RequestWarp(new WarpRequest(_sessions[0].Id, 100.0, WarpKind.OnRails));
            WarpRequest? seenReq = null;
            WarpStateMachine.WarpEndReason? seenReason = null;
            _fsm.WarpEnded += (r, why) => { seenReq = r; seenReason = why; };

            _fsm.Halt(WarpStateMachine.WarpEndReason.AutoLimit);

            Assert.AreEqual(WarpState.Idle, _fsm.State);
            Assert.AreEqual(1.0, _clock.Rate);
            Assert.IsTrue(seenReq.HasValue);
            Assert.AreEqual(WarpStateMachine.WarpEndReason.AutoLimit, seenReason);
        }

        [Test]
        public void Halt_NotActive_IsNoOp()
        {
            _fsm.Halt(WarpStateMachine.WarpEndReason.AutoLimit);
            Assert.AreEqual(WarpState.Idle, _fsm.State);
        }

        [Test]
        public void WarpStarted_FiresExactlyOnce_OnUnanimity()
        {
            int started = 0;
            _fsm.WarpStarted += _ => started++;
            _fsm.RequestWarp(new WarpRequest(_sessions[0].Id, 50.0, WarpKind.OnRails));
            _fsm.Approve(_sessions[1].Id);
            _fsm.Approve(_sessions[2].Id);
            Assert.AreEqual(1, started);
        }

        [Test]
        public void StateChanged_FiresForEachTransition()
        {
            var seen = new List<WarpState>();
            _fsm.StateChanged += s => seen.Add(s);
            _fsm.RequestWarp(new WarpRequest(_sessions[0].Id, 50.0, WarpKind.OnRails));
            _fsm.Approve(_sessions[1].Id);
            _fsm.Approve(_sessions[2].Id);
            _fsm.Halt(WarpStateMachine.WarpEndReason.AutoLimit);
            CollectionAssert.AreEqual(
                new[] { WarpState.Voting, WarpState.Active, WarpState.Idle },
                seen);
        }
    }
}