using System;
using KSPClone.SimCore;

namespace KSPClone.SimCore
{
    /// <summary>
    /// Wires the <see cref="ConnectionRegistry"/> connect/disconnect
    /// events into the <see cref="WarpStateMachine"/> so warp
    /// membership stays correct during play (TIME-5 / TIME-6).
    ///
    /// Rules per the M0 ticket:
    /// - Disconnect while Voting: drop the player from the required
    ///   set; re-evaluate unanimity (may now pass).
    /// - Disconnect while Active: drop them as a voter; warp continues
    ///   unchanged.
    /// - Connect while Active: halt to Idle (Rate = 1.0) immediately
    ///   and include the new player in the next vote.
    /// - Connect while Voting: add them to the required set.
    /// </summary>
    public sealed class WarpMembershipSync
    {
        private readonly ConnectionRegistry _connections;
        private readonly WarpStateMachine _fsm;

        public WarpMembershipSync(ConnectionRegistry connections, WarpStateMachine fsm)
        {
            _connections = connections;
            _fsm = fsm;
            _connections.PlayerConnected += OnConnected;
            _connections.PlayerDisconnected += OnDisconnected;
        }

        private void OnConnected(PlayerSession session)
        {
            switch (_fsm.State)
            {
                case WarpState.Active:
                    // TIME-6: a player joining mid-warp never consented;
                    // halt to baseline and include them in the next vote.
                    _fsm.Halt(WarpStateMachine.WarpEndReason.HaltedByConnection);
                    break;
                case WarpState.Voting:
                    // The new player must approve the open vote too.
                    _fsm.Vote.AddRequired(session.Id);
                    break;
                // Idle: nothing to do.
            }
        }

        private void OnDisconnected(PlayerSession session)
        {
            switch (_fsm.State)
            {
                case WarpState.Voting:
                    if (_fsm.Vote.RemoveRequired(session.Id) && _fsm.Vote.IsUnanimous)
                        _fsm.Approve(session.Id); // never happens, but keeps the contract
                    // If the disconnector was the requester, we leave
                    // the vote open — they were already approved, the
                    // remaining voters can still approve.
                    break;
                case WarpState.Active:
                    // TIME-5: warp continues, drop the disconnector as a voter.
                    _fsm.Vote.RemoveRequired(session.Id);
                    break;
                // Idle: nothing to do.
            }
        }
    }
}