# Dedicated authoritative server with a single master clock

The world is hosted on a dedicated server that holds all simulation authority and owns the one master clock (game-time). We rejected the client-host model: a persistent, living shared universe needs a fixed home that survives any disconnect, and a single timeline needs exactly one authoritative clock. Client-host would force a full-world authority migration every time the host left, and would reintroduce cross-machine authority handoffs (e.g. docking) that the server model makes disappear — both vessels are always simulated in the same place.

Cost accepted: the server runs real physics for active vessels (CPU, not just packet relay). At 4 players this is a handful of active vessels at once; everything else is cheap on-rails propagation. Scaling is vertical (more cores) — a hosting problem, not an architecture problem.
