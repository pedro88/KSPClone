# Constitution

Non-negotiable principles. Every spec, plan, slice, and task is checked against these. Changing one is a deliberate amendment, not a drift.

## Articles

1. **One authoritative server, one master clock.** All simulation authority and the single game-time clock live on the dedicated server. No client is ever the authority for shared state. (ADR-0001)

2. **The simulation is fixed-timestep and render-independent.** The same fixed-step loop runs on the headless server and on clients. Nothing in gameplay logic may depend on render framerate. (ADR-0006)

3. **On-rails is the default; physics is the exception.** Every vessel is analytically propagated (patched conics) unless it is inside an active physics bubble. Closed-form `position(t)` must always exist. (ADR-0003, ADR-0004)

4. **The universe lives even when empty, but nothing is faked.** Game-time always advances; on-rails state stays synced; an active-physics vessel that loses all presence is suspended at a snapshot, never retro-simulated or rail-snapped. (ADR-0002)

5. **Local crew always holds local authority.** A human in a station can always hand-fly their vessel. The network grants extra capability; it never removes a seated human's basic control. (CONTEXT: Blackout)

6. **Control is partitioned, never contended.** Stations own disjoint systems; concurrent player inputs cannot conflict by construction. (CONTEXT: Station)

7. **Design-time and flight-time are separate systems.** Construction edits never touch the vessel replication/physics path. (CONTEXT: Design, Vessel)

8. **One shared program.** Progression (tech, science, funds) is shared by all players and durable in a single Postgres store, transactional with world state. (ADR-0007, CONTEXT: Space program)

9. **Spec before code.** A behaviour is specified in EARS in `spec.md` before it is implemented. Each task cites the requirement it satisfies. The spec is the source of truth; code follows it.

10. **Thin vertical slices.** Work ships as end-to-end slices (server → wire → client), smallest first. The spine is proven before features are layered.
