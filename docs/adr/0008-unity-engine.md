# Unity as the game engine

We will build on Unity. The architecture is heavily custom (patched-conics propagator, fixed-timestep authoritative simulation, custom replication with client-side prediction, rigid bodies only), so the engine is effectively renderer + input + scene graph + rigid-body physics in bubbles + asset pipeline. Both Unity and Godot could serve that role; we chose Unity for the team's existing C# skills, the larger ecosystem and reference material (including KSP-adjacent code), and stronger AI-assist coverage.

Consequence to not get wrong: the dedicated server runs as a Unity headless/dedicated-server build with no rendering, driving the fixed-timestep simulation independently of any render framerate (see ADR-0006). We are deliberately *not* using Unity's high-level networking or its soft-joint physics — replication and structure are custom — so Unity lock-in is mostly the editor/asset pipeline and C#, not the simulation core.
