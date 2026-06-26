# KSP multiplayer — prior art & lessons

> Why this matters for us: every prior attempt at KSP multiplayer hit the same three walls — time/warp, vessel replication at scale, and docking authority. Our ADRs are deliberate answers to those walls. This doc records what DMP, Luna MP, and KSP2 did, what we reuse, and what we do differently.
>
> **Sourcing note:** written from established community knowledge; the official KSP wiki blocks automated fetches, so verify formula/behaviour links in a browser. Repo links are primary sources — read the code, not just READMEs.

---

## 1. DarkMultiPlayer (DMP)

**What they did.** The first widely-used KSP1 multiplayer mod. Client-server, but the server is mostly a **relay + state store**, not an authoritative physics simulator — each client simulates its own vessels and pushes updates. Its defining choice is the **subspace** time model: every player lives in their own time bubble, each with its own game-time offset. When player A warps ahead, A's subspace advances; B stays behind. Players in different subspaces **cannot physically interact**; the UI shows others as "in the future/past." Resync merges subspaces by warping the laggard forward.

**What we reuse.** The subspace concept is the proof that *time is THE hard problem*, and that "each client authoritative for its own vessels" is what forces a relay rather than a sim server. Their vessel-update message taxonomy (position/orbit/rotation/resources at different rates) is a useful checklist for our snapshot contents.

**What we do differently.** We reject subspaces entirely → **single master clock + unanimous warp vote** (ADR-0002, TIME-1/3). We reject client-authoritative vessels → **dedicated authoritative server** simulates active vessels (ADR-0001). This trades DMP's "everyone can warp independently" freedom for a coherent shared world, which is acceptable at 4 coordinated players.

**Links:**
- https://github.com/godarklight/DarkMultiPlayer — source (study `VesselWorker`, the subspace/time sync, the update message types).
- https://forum.kerbalspaceprogram.com/ (search "DarkMultiPlayer") — design discussion & limitations threads.

---

## 2. Luna Multiplayer (LMP)

**What they did.** A later, more sophisticated KSP1 mod (spiritual successor to DMP), C#. Still fundamentally **client-authoritative with a relay server**, but with much better engineering: an interpolation system for remote vessels, a structured system-per-concern architecture (`VesselPositionSystem`, `VesselFlightStateSystem`, warp system, etc.), and a master-clock/time-sync via NTP-like server time. It kept a **subspace-like warp model** but tried harder to keep players together.

**What we reuse.** LMP's **remote-vessel interpolation** is directly relevant to our NET-4 (interpolate non-controlled vessels) — see netcode.md. Its system decomposition is a good template for our replication-layer module boundaries. Its server-time sync approach informs our client↔server clock sync.

**What we do differently.** Same core divergence as DMP: their authority lives on clients; ours on the server (ADR-0001). Because our server is authoritative, our docking has no two-host handoff (see §6) — LMP still suffers the handoff because two clients each own a vessel at contact.

**Links:**
- https://github.com/LunaMultiplayer/LunaMultiplayer — source (study the `*System` classes, interpolation, warp subsystem).
- https://lmpicg.github.io/ / project site (search) — feature docs.

---

## 3. KSP2 multiplayer — the cautionary tale

**What was promised.** KSP2 announced native multiplayer as a flagship feature. It never shipped in a working state before the studio (Intercept Games) was effectively shut down in 2024 and the project stalled.

**Why it struggled (lessons, not gossip).** Multiplayer was promised on top of an engine/physics stack that was already fighting performance and stability problems (the same wobble/kraken lineage), and retrofitting netcode onto a single-player-shaped simulation is exactly the "bolt the network on afterwards" trap. The takeaway repeated across postmortems: **multiplayer must shape the architecture from day one**, not be added later.

**What we do differently.** This is the entire reason our spine (M0) is server-authoritative fixed-step sim + on-rails default *before any feature* (constitution Art. 1, 2, 10). We design for the server-headless, render-decoupled case first (ADR-0006), which is the thing KSP2 could not retrofit.

**Links:**
- General coverage: search "KSP2 multiplayer cancelled / Intercept Games shutdown 2024" (Ars Technica, PC Gamer, Eurogamer).

---

## 4. KSP time-warp design (inherited rules)

**What KSP does.** Two distinct mechanisms:
- **Physics warp** (x1–x4): physics keeps running, just faster. Available in atmosphere / under thrust. Imprecise but lets you skip dull powered phases.
- **On-rails warp** (x5 … x100,000): physics OFF, vessels follow analytic orbits. Requires being **out of atmosphere and not under thrust** (warp-safe). Warp auto-drops near maneuver nodes and SOI changes so you don't skip them; warp ceiling is limited by altitude near a body.

**What we reuse.** Both warp kinds (TIME-7) and — crucially — the **auto-limit to the next point of interest** (TIME-4), which we *globalize* across all vessels. The warp-safe predicate (orbit stable, no thrust, not in atmo) is exactly our promotion/demotion + suspension gate (CONTEXT: warp-safe state).

**What we do differently.** In KSP (solo) the auto-limit only considers the active vessel. We compute the **earliest POI across all vessels in the world** (TIME-4) and gate warp behind a **unanimous vote** (TIME-3), because the clock is shared. KSP physics-warp needs no consent; ours does, because one master clock means x4 hits everyone (the R-09 hole we closed).

**Links:**
- https://wiki.kerbalspaceprogram.com/wiki/Time_warp (verify in browser — wiki blocks bots).

---

## 5. KSP on-rails vs loaded/physics vessel model

**What KSP does.** A vessel is either **"on rails"** (packed: analytic orbit, no physics, no part forces) or **"loaded/unpacked"** (full PhysX simulation). The transition is governed by **physics range / load distance** (~2.25 km unpack range historically, configurable) around the active vessel. Only vessels near the active one are unpacked; everything else is on rails. There is exactly **one** physics locus (the active vessel) in stock KSP.

**What we reuse.** The on-rails/loaded split is our on-rails vessel vs active-physics vessel (ADR-0003/0004) and the range-based promotion trigger (PHYS-2). Packing/unpacking maps to our promotion/demotion.

**What we do differently.** Stock KSP has **one** physics locus; we run **K concurrent physics bubbles** (ADR-0003) so four players on four missions all get real physics simultaneously — the thing stock KSP and the relay mods could not do for separated players. Each bubble has its own floating origin (see floating-origin.md).

**Links:**
- https://wiki.kerbalspaceprogram.com/wiki/Physics_range (verify in browser).
- KSP modding: search "PackedVessel / GoOnRails / GoOffRails Unity KSP" for the stock pack/unpack hooks.

---

## 6. The docking problem (what killed prior attempts)

**The wall.** Two vessels, each **authoritative on a different client**, approach for docking. At the moment they enter physics range of each other, *some single machine must simulate both* — there is no consistent shared physics otherwise. DMP/LMP must do a cross-client **authority handoff** at the worst possible moment (close proximity, relative motion, latency), and getting it seamless is extremely hard → jitter, kraken, failed docks. This is the single most-cited reason native KSP MP "never worked properly."

**How we avoid it.** Authority **never lives on a client** (ADR-0001). Two converging vessels are both promoted into the **same server-side physics bubble before contact** (PHYS-2/5). There is no handoff because the authority never moves — the server simulated both all along. The scenario that broke everyone else is a non-event for us. This is the strongest single argument for the dedicated-server choice.

**Links:**
- DMP/LMP source (the vessel-proximity / dock-detect paths) — see §1, §2 links.

---

## Top lessons for our build

1. **Time is the hardest problem, not orbits or rendering.** Everyone who shipped chose subspaces and lived with an incoherent world. We chose a single clock + vote and must make the *vote/auto-limit UX* excellent — that's where our risk moved.
2. **Client authority forces a relay and forces the docking handoff.** Server authority dissolves both. Worth the CPU (ADR-0001).
3. **Design for headless server + render-decoupled sim from day one** (KSP2's failure). This is M0, before features.
4. **Reuse remote-vessel interpolation patterns from LMP** for NET-4; don't reinvent.
5. **Warp-safe predicate = pack/unpack predicate = suspension gate.** One concept, reuse it everywhere (CONTEXT: warp-safe state).
6. **One physics locus is a stock-KSP limitation, not a law.** K bubbles is our differentiator and our cost center — keep K small (4 players).
7. **Globalize KSP's single-vessel auto-limit to all vessels** (TIME-4) — this is the subtle rule that keeps a single clock survivable.

## Things to study further in source

- DMP `VesselWorker` + subspace/time code — confirm exactly how resync warps a laggard (informs our connect-during-warp halt, TIME-6).
- LMP `VesselPositionSystem` interpolation buffers — exact buffer sizing vs our 20–30 Hz snapshots.
- Stock KSP pack/unpack hooks (`GoOnRails`/`GoOffRails`) — the precise state captured at the transition (informs our snapshot contents for promotion/demotion and suspension, SUSP-3).
- KSP `Krakensbane` / floating origin — cross-referenced in floating-origin.md.
