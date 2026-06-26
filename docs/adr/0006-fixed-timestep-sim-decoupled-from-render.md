# Fixed-timestep simulation decoupled from render

The simulation runs on a fixed timestep, fully decoupled from any render framerate, so the same loop runs identically on the headless dedicated server and on clients. This must be right from day one — it is painful to retrofit once gameplay code assumes frame-coupled updates.

Agreed budget (the R-11 acceptance numbers):

- **Server physics tick:** fixed 60 Hz for active physics bubbles. On-rails vessels are analytic and need no tick.
- **Snapshot / replication rate to clients:** 20–30 Hz, interpolated.
- **Pilot perceived input lag:** effectively 0, via client-side prediction; correctness held by server reconciliation.
- **Playable RTT target:** smooth up to 150 ms RTT, graceful degradation beyond. (4 friends → an EU VPS is realistically 20–60 ms.)
- **Reconciliation:** sub-threshold divergence smoothed over a few frames; hard snap only on large desync.

Rejected: a render-coupled update loop. It would make the headless server behave differently from clients, make physics framerate-dependent, and break determinism of the fixed-step authoritative simulation.
