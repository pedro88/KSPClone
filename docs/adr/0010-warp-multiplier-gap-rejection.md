# Warp multipliers in the (4, 1000) gap are rejected outright

`WarpPolicy.ClassifyMultiplier` accepts `≤ 4` as `WarpKind.Physics` and `≥ 1000` as `WarpKind.OnRails`. Multipliers in `(4, 1000)` throw `ArgumentOutOfRangeException`. There is no third warp mode, and there is no "use the closest kind" fallback.

Why the gap exists: physics warp keeps active physics stepping (so it's bound by what the 60 Hz integrator can do in one tick — somewhere between x1 and x4 before per-frame delta-v exceeds simulation tolerances). On-rails warp advances only closed-form conics (so it's bound only by what the player's patience accepts — anywhere from x100 upward is fine). The intermediate multipliers have no use case: if a vessel needs active physics it needs it at the slowest rate (≤ x4); if it doesn't, the analytic-only OnRails path works at any rate ≥ x1000 with no simulation cost difference. We refuse to invent semantics for a range no one needs.

The cost of rejecting is one error message instead of a silent mode switch — exactly the trade we want, because silent mode switches are how time-acceleration bugs hide.

Rejected: silently rounding to the closest kind, or adding a `WarpKind.Hybrid` that runs physics at reduced fidelity. Both would surprise the player (the vessel does something other than what was requested) and confuse the on-rails replication path (which assumes on-rails means analytic-only).

To revisit: only if M1's active-physics bubble needs a sub-on-rails high-multiplier warp — unlikely given that physics bubbles are local and bounded, but the threshold values are constants in `WarpPolicy` and easy to retune.