#nullable enable annotations

using System;
using System.Collections.Generic;
using System.Linq;

namespace KSPClone.SimCore
{
    public enum WarpState
    {
        Idle = 0,
        Voting = 1,
        Active = 2,
    }

    /// <summary>
    /// The kind of warp requested. Physics = low multiplier with active
    /// physics still stepping; OnRails = high multiplier, analytic conic
    /// propagation only (TIME-7).
    /// </summary>
    public enum WarpKind
    {
        Physics = 0,
        OnRails = 1,
    }

    /// <summary>
    /// A warp request, parameterized by the multiplier the requester
    /// wants and the kind of warp. Both are decided at request time —
    /// the FSM does not mutate them after the vote opens.
    /// </summary>
    public readonly struct WarpRequest
    {
        public PlayerId Requester { get; }
        public double Multiplier { get; }
        public WarpKind Kind { get; }

        public WarpRequest(PlayerId requester, double multiplier, WarpKind kind)
        {
            Requester = requester;
            Multiplier = multiplier;
            Kind = kind;
        }
    }

    /// <summary>
    /// Tracks approvals for an open warp vote. Non-approval is "not
    /// yet," not a permanent veto (per the warp vote contract in
    /// CONTEXT.md). The required voter set is closed at vote-open time
    /// but T20 mutates it on disconnect/connect.
    /// </summary>
    public sealed class WarpVote
    {
        public IReadOnlyCollection<PlayerId> Required => _required;
        public IReadOnlyCollection<PlayerId> Approved => _approved;
        public IReadOnlyCollection<PlayerId> Pending => ComputePending();
        public bool IsUnanimous => _approved.Count == _required.Count && _required.Count > 0;

        private readonly HashSet<PlayerId> _required = new();
        private readonly HashSet<PlayerId> _approved = new();

        public void Open(IEnumerable<PlayerId> voters)
        {
            _required.Clear();
            _approved.Clear();
            foreach (var v in voters) _required.Add(v);
        }

        public void Approve(PlayerId id)
        {
            if (!_required.Contains(id)) return;
            _approved.Add(id);
        }

        public void WithdrawApproval(PlayerId id) => _approved.Remove(id);

        public void AddRequired(PlayerId id) => _required.Add(id);

        public bool RemoveRequired(PlayerId id)
        {
            _approved.Remove(id);
            return _required.Remove(id);
        }

        private HashSet<PlayerId> ComputePending()
        {
            var p = new HashSet<PlayerId>(_required);
            p.ExceptWith(_approved);
            return p;
        }
    }

    /// <summary>
    /// Three-state FSM that gates every warp behind a unanimous player
    /// vote (TIME-3). Idle → Voting (on RequestWarp) → Active (on
    /// unanimity) → Idle (on cancel or auto-limit, latter in T19).
    /// The FSM is the SOLE writer of <see cref="MasterClock.Rate"/>.
    ///
    /// T20 hooks connect/disconnect events from <see cref="ConnectionRegistry"/>
    /// into this FSM so warp membership stays correct during play.
    /// </summary>
    public sealed class WarpStateMachine
    {
        public WarpState State { get; private set; } = WarpState.Idle;
        public WarpRequest? CurrentRequest { get; private set; }
        public WarpVote Vote { get; } = new();

        public enum WarpEndReason
        {
            Cancelled = 0,
            AutoLimit = 1,
            HaltedByConnection = 2,
        }

        /// <summary>Fired when the FSM transitions to a new state.</summary>
        public event Action<WarpState>? StateChanged;

        /// <summary>Fired when the FSM becomes Active; carries the chosen request.</summary>
        public event Action<WarpRequest>? WarpStarted;

        /// <summary>Fired when the FSM returns to Idle after having been Active.</summary>
        public event Action<WarpRequest, WarpEndReason>? WarpEnded;

        private readonly MasterClock _clock;
        private readonly ConnectionRegistry _connections;

        public WarpStateMachine(MasterClock clock, ConnectionRegistry connections)
        {
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _connections = connections ?? throw new ArgumentNullException(nameof(connections));
        }

        /// <summary>
        /// Client opens a warp request. Idle → Voting, vote opened over
        /// the current connection set with the requester's approval
        /// already counted (so a solo player can warp).
        /// </summary>
        public bool RequestWarp(WarpRequest request)
        {
            if (State != WarpState.Idle) return false;
            if (request.Multiplier <= 1.0) return false;
            if (!_connections.Contains(request.Requester)) return false;

            Vote.Open(_connections.All.Select(s => s.Id));
            Vote.Approve(request.Requester);
            CurrentRequest = request;
            TransitionTo(WarpState.Voting);

            if (Vote.IsUnanimous) Activate();
            return true;
        }

        public void Approve(PlayerId id)
        {
            if (State != WarpState.Voting) return;
            Vote.Approve(id);
            if (Vote.IsUnanimous) Activate();
        }

        /// <summary>
        /// Re-check unanimity while Voting and activate if every remaining
        /// required voter has approved. Called after the required set shrinks
        /// (e.g. a non-approving voter disconnects mid-vote, TIME-5).
        /// </summary>
        public void ReevaluateUnanimity()
        {
            if (State == WarpState.Voting && Vote.IsUnanimous) Activate();
        }

        public bool Cancel(PlayerId requester)
        {
            if (State != WarpState.Voting) return false;
            if (CurrentRequest is not WarpRequest req || req.Requester != requester) return false;
            TransitionTo(WarpState.Idle);
            CurrentRequest = null;
            Vote.Open(Array.Empty<PlayerId>());
            return true;
        }

        public void Halt(WarpEndReason reason)
        {
            if (State != WarpState.Active) return;
            var req = CurrentRequest!.Value;
            _clock.Rate = 1.0;
            TransitionTo(WarpState.Idle);
            CurrentRequest = null;
            Vote.Open(Array.Empty<PlayerId>());
            WarpEnded?.Invoke(req, reason);
        }

        private void Activate()
        {
            var req = CurrentRequest!.Value;
            _clock.Rate = req.Multiplier;
            TransitionTo(WarpState.Active);
            WarpStarted?.Invoke(req);
        }

        private void TransitionTo(WarpState next)
        {
            if (State == next) return;
            State = next;
            StateChanged?.Invoke(next);
        }
    }
}