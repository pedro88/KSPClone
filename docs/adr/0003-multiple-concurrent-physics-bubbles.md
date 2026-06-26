# Multiple concurrent physics bubbles

The server maintains several physics bubbles at once — one per cluster of nearby players/vessels — each with its own floating origin to preserve float precision at large world distances (the "kraken"). A vessel promotes from on-rails to active-physics when a player loads/approaches it or when two vessels close within physics range; it demotes when warp-safe and unattended.

We rejected a single global physics bubble: it would force all four players to stay physically together and kill the parallel-missions premise (separate crews on separate missions simultaneously). Multiple bubbles are what make that premise possible.

A direct payoff: docking needs no cross-machine authority handoff. Two converging vessels join the *same* bubble before they make contact, so the scenario that broke prior KSP multiplayer attempts (two ships simulated on two machines at the moment of contact) never arises here. Cost: the server runs K independent physics worlds concurrently (K is small at 4 players).
