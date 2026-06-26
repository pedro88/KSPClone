# Living universe with per-vessel suspension

Game-time always advances, even when the server is empty (baseline 1:1 real-time; warp requires players + a unanimous vote). The universe is "living," not merely "saved." On-rails vessels stay synced to the master clock at all times via analytic propagation — cheap, so the empty server keeps the universe evolving.

The hard case is a vessel that cannot be propagated analytically (in atmosphere, under thrust, mid-docking) when the last player leaves. We decided such a vessel is *suspended*: snapshotted at disconnect, its vessel clock pauses, and it resumes from the snapshot when a player next loads it. Its vessel clock may therefore lag the master clock.

Rejected alternatives: (a) 24/7 server-side physics for unattended active vessels — burns CPU continuously and lets pilotless craft crash unattended; (b) forced rail-snap — fabricates a fake orbit for a vessel that was in atmosphere, corrupting its state. Suspension keeps "living universe" true for the ~99% on rails while refusing to fake or destroy the fragile minority under active physics.
