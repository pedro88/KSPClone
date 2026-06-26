# Unity Dedicated Server, Fixed-Timestep Sim & Networking — Curated Reference

> **Why this matters for us:** ADR-0008 commits us to Unity with a *headless dedicated-server build* driving a *custom* simulation (the engine is renderer + physics-in-bubbles + assets, not our netcode). ADR-0006 fixes the sim at **60 Hz server physics**, snapshots at **20–30 Hz**, decoupled from any render framerate so the server and clients run the identical loop. ADR-0001 makes that server the single authoritative clock (NET-1) — so we need a headless Unity build that runs deterministic, render-independent physics on a no-GPU Linux VPS.

**Unity version assumed:** **Unity 6 LTS (6000.x)**, specifically the `6000.x` manual line and `com.unity.dedicated-server` 1.x. Most of this also works back to Unity 2022 LTS, but several APIs (`StandaloneBuildSubtarget.Server`, `Physics.simulationMode`, the `com.unity.dedicated-server` package, Multiplayer Roles) are version-sensitive — each is flagged inline. **Verify against your exact editor version before relying on any single API name.**

---

## 1. Unity Dedicated Server build target

The **Dedicated Server** is a *sub-target* of the three desktop platforms (Linux / macOS / Windows), not a separate platform. It produces a headless executable with no graphical interface and strips server-irrelevant assets/code.

**Key facts**
- **Build subtarget API:** `BuildPlayerOptions.subtarget = (int)StandaloneBuildSubtarget.Server;` with `target = BuildTarget.StandaloneLinux64`. Defines the scripting symbol **`UNITY_SERVER`** for `#if`-gating server-only code.
- **CLI build:** `-buildTarget Linux64 -standaloneBuildSubtarget Server` (run the editor in `-batchmode -quit` with an `-executeMethod` build entry point).
- **Module required:** "Linux Dedicated Server Build Support" installed via Unity Hub (separate from "Linux Build Support (Mono/IL2CPP)").
- **Package:** `com.unity.dedicated-server` (1.x) adds **Multiplayer Roles** (tag GameObjects/components as Client/Server/Both so server builds physically strip client-only content), **Content Selection / Automatic Selection** (auto-remove component types), and **CLI Arguments Defaults**. Runtime arg access via the **`DedicatedServer.Arguments`** API.
- **Minimum versions:** the build *sub-target* exists from **2021.3 LTS**; the `com.unity.dedicated-server` *package* (Multiplayer Roles etc.) **requires Unity 6+**.

**What the build auto-strips / disables** (so the server is cheap on a headless box):
- Audio subsystem deactivated; lighting threads removed.
- PlayerLoop callbacks removed: `UpdateInputManager`, `UpdateAllRenderers`, `UIElementsUpdatePanels`, `UpdateAudio`.
- GPU-only asset data stripped (texture pixel data, mesh vertex data without CPU Read/Write).
- Optional **"Enable Dedicated Server Optimizations"** Player Setting additionally strips Shaders and Fonts.

**Dedicated Server vs. plain headless (`-batchmode -nographics`):** A normal Standalone build run with `-batchmode -nographics` also skips graphics device init and works on a GPU-less box — but it keeps client assets and client-side PlayerLoop work. Per Unity, "the only difference is that the Dedicated Server build target is optimized to increase memory and CPU performance when running as a networked application." **Use the Server sub-target for production; `-nographics` on a Standalone build is the cheap fallback / CI path.** You still pass `-batchmode -nographics` when *running* a server build to be safe on a headless host.

**Gotchas / version drift**
- The package was at `0.x` (preview) in early Unity 6 betas and is now `1.x` — Multiplayer Roles UI moved around between versions; check the package version, not just the editor version.
- `-nographics` historically still created a null graphics device on some platforms; on Unity 6 Linux server it does not initialize a device. Don't assume any `Graphics`/`Camera`/render-texture API works on the server — gate all of it behind `#if !UNITY_SERVER`.

**Links**
- [Manual: Dedicated Server (6000.0)](https://docs.unity3d.com/6000.0/Documentation/Manual/dedicated-server.html) — landing/index for the whole topic.
- [Manual: Introduction to Dedicated Server (6000.2)](https://docs.unity3d.com/6000.2/Documentation/Manual/dedicated-server-introduction.html) — what the sub-target strips and why.
- [Manual: Build for Dedicated Server (6000.4)](https://docs.unity3d.com/6000.4/Documentation/Manual/dedicated-server-build.html) — `StandaloneBuildSubtarget.Server`, `-standaloneBuildSubtarget Server`, `UNITY_SERVER`, `DedicatedServer.Arguments`.
- [Manual: Dedicated Server requirements (6000.3)](https://docs.unity3d.com/6000.3/Documentation/Manual/dedicated-server-requirements.html) — editor/module versions (2021.3 LTS+).
- [Manual: Dedicated Server optimizations (6000.1)](https://docs.unity3d.com/6000.1/Documentation/Manual/dedicated-server-optimizations.html) — auto-disabled PlayerLoop items, "Enable Dedicated Server Optimizations".
- [Package: Dedicated Server 1.4](https://docs.unity3d.com/Packages/com.unity.dedicated-server@1.4/manual/index.html) — Multiplayer Roles, Content Selection, CLI Arguments Defaults.
- [Manual: Desktop headless mode](https://docs.unity3d.com/Manual/desktop-headless-mode.html) — `-batchmode -nographics`, headless vs. server sub-target.

---

## 2. Fixed-timestep simulation, decoupled from render

Unity's `FixedUpdate` already decouples physics from render: it runs `Time.fixedDeltaTime`-spaced (default **0.02 s = 50 Hz**), and may fire zero, one, or many times per rendered frame to catch up. On a headless server there *is* no render frame, so the loop is essentially "run fixed ticks as fast as the wall clock requires."

**Key facts**
- **`Time.fixedDeltaTime`** — the fixed tick interval. For NET-5 set it to **`1f/60f` (60 Hz)**. Set it once at startup.
- **`Time.maximumDeltaTime`** ("Maximum Allowed Timestep" in Time Manager) — caps how much accumulated time one frame may spend on FixedUpdate+physics. If a frame overruns, Unity "stops time" and lets processing catch up, so a hitch slows the sim instead of triggering a death-spiral of FixedUpdates. Unity suggests `0.1` for physics-heavy games; lower (e.g. `0.0333`) bounds the catch-up iteration count at the cost of momentary slow-down.
- `FixedUpdate` runs *before* internal physics for that step; render-frame logic stays in `Update`.

**Server-loop pattern.** Two valid approaches:
1. **Let Unity drive `FixedUpdate`** at `fixedDeltaTime = 1/60` and do sim work there. Simplest; relies on Unity's player loop pacing.
2. **Own the clock explicitly** (recommended for an authoritative single-clock server, ADR-0001): drive your own accumulator from real time and step the sim + `Physics.Simulate(1f/60f)` manually (see §4). This gives you a single master tick counter you fully control, decoupled from Unity's FixedUpdate scheduling, and makes the identical loop trivially reusable on the client for prediction (ADR-0006).

**Gotchas / version drift**
- On a headless server, don't let the loop spin a CPU core at thousands of ticks/s — cap with `Application.targetFrameRate` (e.g. 60) or `Thread.Sleep`/your own pacing, or you'll burn the VPS. (The optimizations manual does *not* set this for you.)
- `Time.fixedDeltaTime` and `Time.maximumDeltaTime` are global and stable across recent versions; the *Time Manager inspector label* "Maximum Allowed Timestep" maps to `Time.maximumDeltaTime`.
- Determinism across machines is **not** guaranteed by fixed timestep alone — float results differ across CPUs/builds. Our model sidesteps this by being server-authoritative (clients predict then reconcile, NET-2/NET-3), not by lockstep determinism.

**Links**
- [ScriptRef: MonoBehaviour.FixedUpdate](https://docs.unity3d.com/ScriptReference/MonoBehaviour.FixedUpdate.html) — fires 0..n times/frame, decoupled from render.
- [Manual: Fixed updates (6000.3)](https://docs.unity3d.com/6000.3/Documentation/Manual/fixed-updates.html) — the fixed-timestep model in Unity 6.
- [Manual: Time Manager](https://docs.unity3d.com/Manual/class-TimeManager.html) — Fixed Timestep + Maximum Allowed Timestep (`Time.maximumDeltaTime`).

---

## 3. Networking: NGO vs. Netcode for Entities vs. custom + transport

Per ADR-0008 we are **not** using Unity's high-level networking for the sim core; replication is custom. This section is to confirm that choice and pick the transport.

**Unity's own decision guidance** (Unity blog "How to choose the right netcode"):
- **Netcode for GameObjects (NGO, `com.unity.netcode.gameobjects`)** — easy GameObject/scene sync, client- *or* server-authoritative. **Does not ship client-side prediction or lag compensation.** Recommended for casual/co-op that doesn't need perfect cross-client sync.
- **Netcode for Entities (`com.unity.netcode`, DOTS)** — purpose-built **server-authoritative with client prediction**, interpolation, and lag compensation. Recommended for fast/competitive, large player counts, complex logic. **Requires the full DOTS/ECS stack** (Entities, and for deterministic physics, Unity Physics / Havok).
- **Custom on Unity Transport (`com.unity.transport`, UTP)** — UTP is the netcode-agnostic backbone *under both* of the above and is explicitly supported for custom solutions. Connection-based abstraction over UDP/WebSockets with **Pipelines** for optional reliability, ordering, and fragmentation (e.g. ReliableSequenced pipeline).

**Mapping to our requirements (NET-1..NET-6):** We need exactly server-authority + client prediction + reconciliation + snapshot interpolation at custom rates (60 Hz sim / 20–30 Hz snapshots). NGO lacks prediction. Netcode for Entities *has* all of it (its server even defaults to **60 ticks/s**, matching NET-5) — but it forces DOTS on the entire sim and is a heavy commitment for 4 players. A **custom replication layer over UTP** matches ADR-0008 ("replication is custom"), keeps us on classic MonoBehaviour/RigidBody physics in bubbles (ADR-0005), and lets us set our own 60 Hz tick + 20–30 Hz snapshot cadence directly.

**Transport candidates for the custom layer**
| Transport | Notes | Fit for us |
|---|---|---|
| **Unity Transport (UTP)** `com.unity.transport` 2.x | Official, maintained, reliable/unreliable Pipelines, UDP+WebSocket, integrates with Unity Relay; Burst/jobs-friendly but usable from plain C#. | **Default pick** — official, future-proof, no extra native deps. |
| **LiteNetLib** | Pure-C# reliable UDP, lightweight, low CPU/alloc, Linux/.NET Core fine. Very popular for custom Unity netcode. | Strong alternative; simplest API, easy on headless Linux. |
| **ENet / ENet-CSharp (e.g. Ignorance)** | Native ENet via C# wrapper, battle-tested reliable UDP, channels, fragmentation. | Fine, but native lib to ship per-platform; only if you need ENet specifics. |

**Gotchas / version drift**
- NGO and Netcode for Entities are **different packages and not interchangeable** (`com.unity.netcode.gameobjects` vs `com.unity.netcode`) — a frequent source of confusion.
- UTP major versions matter: **2.x** is current for Unity 6; 1.x APIs differ. Check the package version.
- LiteNetLib/ENet run fine headless on Linux, but you own NAT/keepalive/security yourself; UTP gives you Relay/encryption hooks out of the box.

**Links**
- [Unity blog: How to choose the right netcode](https://unity.com/blog/games/how-to-choose-the-right-netcode-for-your-game) — *(WebFetch returns HTTP 403; readable in a browser. Content above is from Unity's own summary surfaced in search.)*
- [Unity netcode packages overview](https://docs.unity.com/en-us/multiplayer/netcode/netcode) — NGO vs Netcode for Entities.
- [Package: Unity Transport 2.4](https://docs.unity3d.com/Packages/com.unity.transport@2.4/manual/index.html) — Pipelines, UDP/WebSocket, custom-netcode support.
- [Feature: Networking & Netcode](https://unity.com/features/netcode) — Unity's product framing.

---

## 4. Controlled server-side physics (manual stepping)

This is the core of running an authoritative physics tick on a headless server.

**Key facts**
- **`Physics.simulationMode`** (enum **`SimulationMode`**) controls when physics steps: `FixedUpdate` (default), `Update`, or **`Script`** (manual). **`Physics.autoSimulation` is deprecated** — use `simulationMode` (2D: `Physics2D.simulationMode` with `SimulationMode2D.Script`).
- **Disable auto-sim:** `Physics.simulationMode = SimulationMode.Script;` at startup (or Project Settings > Physics > Simulation Mode = Script).
- **Step manually:** `Physics.Simulate(1f/60f);` once per server tick with a **fixed** step (NET-5). Unity's best practice: pass a fixed value (e.g. 0.02), **not** `Time.deltaTime`; large steps (>~0.03 s) cause tunneling/jitter.
- **Multi-scene physics** (relevant to our concurrent physics bubbles, ADR-0003): each physics scene steps independently via `physicsScene.Simulate(step)` on a `PhysicsScene` handle (create scenes with `LocalPhysicsMode`). This lets each active bubble be its own isolated `PhysicsScene` you step explicitly.

**Server tick skeleton (illustrative):**
```csharp
void Awake() {
    Physics.simulationMode = SimulationMode.Script;   // Unity 6; was Physics.autoSimulation=false pre-2022
    Time.fixedDeltaTime = 1f / 60f;
}
// In your owned fixed-step loop:
void ServerTick() {
    StepGameplayBeforePhysics();
    Physics.Simulate(1f / 60f);          // or perBubbleScene.Simulate(1f/60f) per ADR-0003
    StepGameplayAfterPhysics();
    if (tick % snapshotInterval == 0) EmitSnapshot();   // 20–30 Hz, NET-5
}
```

**Gotchas / version drift**
- **API rename:** pre-2022 code used `Physics.autoSimulation = false`. On Unity 6 that property is deprecated → use `Physics.simulationMode = SimulationMode.Script`. If you copy older KSP-adjacent samples, expect this rename.
- With manual stepping you take over ownership: nothing steps physics unless *you* call `Simulate`. Triggers/collision callbacks fire during your `Simulate` call.
- Per-scene `PhysicsScene.Simulate` requires creating scenes with the local-physics mode; objects only collide within their own scene — exactly what we want for independent bubbles, but easy to get wrong if a bubble's objects land in the default scene.

**Links**
- [ScriptRef: Physics.simulationMode / SimulationMode](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/SimulationMode.html) — `Script` mode.
- [ScriptRef: Physics.Simulate](https://docs.unity3d.com/ScriptReference/Physics.Simulate.html) — manual step, fixed-step guidance.
- [ScriptRef: Physics.autoSimulation (deprecated)](https://docs.unity3d.com/ScriptReference/Physics-autoSimulation.html) — note the deprecation → `simulationMode`.
- [Manual: Manually set physics simulation (6000.3)](https://docs.unity3d.com/6000.3/Documentation/Manual/physics-optimization-cpu-manual-simulation.html) — `simulationMode = Script`, fixed step, `PhysicsScene.Simulate`.

---

## 5. DOTS / ECS relevance for many vessels/parts

**The case for DOTS:** ECS scales to huge entity counts deterministically, and Netcode for Entities gives server-authoritative prediction with the client prediction loop running at the *exact same `SimulationTickRate` as the server* (60 ticks/s default via the `ClientServerTickRate` singleton). Deterministic physics via Havok Physics for Unity. That is genuinely the "right" stack for a competitive game with thousands of networked entities.

**The case against, for us:**
- **Scale is tiny.** ADR-0001 explicitly notes 4 players = "a handful of active vessels at once," everything else cheap on-rails analytic propagation (ADR-0004 patched conics, ADR-0002 suspension). The thing DOTS optimizes (raw entity throughput) is not our bottleneck.
- **DOTS is all-or-nothing for the sim core.** Netcode for Entities requires the Entities stack; you can't bolt it onto MonoBehaviour/RigidBody bubbles. Adopting it would re-architect the entire sim, contradicting ADR-0005 (rigid RigidBody vessels) and ADR-0008 (custom replication, engine = renderer+rigidbody-physics).
- **Cost/iteration.** Higher complexity, longer iteration, and the team's existing skills (a stated ADR-0008 reason for choosing Unity) are classic C#/MonoBehaviour.

**Recommendation: do NOT adopt DOTS/ECS or Netcode for Entities.** Stay on classic GameObject + RigidBody physics in per-bubble `PhysicsScene`s, manually stepped, with a custom replication layer over UTP. Revisit *only* if a single bubble's part count makes per-tick CPU the bottleneck — and even then, prefer optimizing the hot path (jobs/Burst on isolated systems) over a full ECS rewrite.

**Gotchas / version drift**
- Entities/Netcode for Entities versioning is fast-moving (1.x). The "60 ticks/s default" and `ClientServerTickRate` are current-version facts; confirm if you ever reconsider.

**Links**
- [Package: Netcode for Entities (latest)](https://docs.unity3d.com/Packages/com.unity.netcode@latest/) — server-authoritative + client prediction framework.
- [Netcode for Entities: Client/Server Worlds (1.0)](https://docs.unity3d.com/Packages/com.unity.netcode@1.0/manual/client-server-worlds.html) — prediction loop at server `SimulationTickRate`, 60-tick default, `ClientServerTickRate`.
- [ECS for Unity](https://unity.com/ecs) and [DOTS](https://unity.com/dots) — determinism/scale claims, Havok Physics for Unity.

---

## 6. Deploying a Unity Linux headless server on a VPS (Hetzner, no GPU)

**Build pipeline**
- Build with **Linux Dedicated Server Build Support** module installed; CLI: editor in `-batchmode -nographics -quit -executeMethod YourBuilder.Build -buildTarget Linux64 -standaloneBuildSubtarget Server`.
- **Licensing in CI/headless:** activate via manual/float license — `-createManualActivationFile` to generate the request, then a returned `.ulf` applied with `-manualLicenseFile`. (Unity Build Automation / a Personal-license headless build is the common path.)
- Output is a Linux x64 ELF + `_Data` folder. No GPU, no X server required.

**Running on the VPS**
- Start with `./Server.x86_64 -batchmode -nographics -logFile /var/log/ksp/server.log -port 7777` (custom args readable via `DedicatedServer.Arguments`).
- Wrap in a **systemd unit** (`Restart=on-failure`, `WorkingDirectory`, dedicated unprivileged user) for supervision and restart-on-crash. Community guides (SergeantBiggs) show a working Drone pipeline + systemd unit pattern.
- A Hetzner CX/CPX/CCX instance (shared or dedicated vCPU) is fine; ADR-0001 scaling is vertical (more cores), and an EU VPS gives the 20–60 ms RTT ADR-0006 assumes. Pick an EU region near the players.
- No GPU is needed — the Server sub-target and `-nographics` skip the graphics device entirely (§1).

**Gotchas / version drift**
- Ensure the **glibc** on the VPS matches what the IL2CPP/Mono build expects; very old/new distros can mismatch. Ubuntu LTS is the safe default.
- Open the UDP port in Hetzner's firewall/Cloud Firewall and any OS `ufw`.
- Cap CPU usage with a tick-rate limit (§2) so an idle server doesn't peg a vCPU.
- Unity license activation flags have shifted across versions; verify the exact `-createManualActivationFile` / Build Automation flow for your editor version.

**Links**
- [Manual: Build for Dedicated Server (6000.4)](https://docs.unity3d.com/6000.4/Documentation/Manual/dedicated-server-build.html) — CLI build, `DedicatedServer.Arguments`.
- [Build Automation: configure Dedicated Server target](https://docs.unity.com/en-us/build-automation/advanced-build-configuration/configure-dedicated-server-build-target) — official CI path.
- [SergeantBiggs: Automated Headless Unity Builds](https://blog.sergeantbiggs.net/posts/automated-headless-unity-builds/) — *(community)* example pipeline + systemd service + `-batchmode -nographics`.
- [Unity Discussions: Headless Linux install for building on a server](https://discussions.unity.com/t/headless-linux-install-of-unity-version-for-building-on-server/899208) — *(community)* license activation on headless.

---

## Recommended setup (for KSPClone)

| Decision | Choice | Why / ADR |
|---|---|---|
| **Unity version** | **Unity 6 LTS (6000.x)**, IL2CPP | Current LTS, has `com.unity.dedicated-server` 1.x, `simulationMode`, current UTP. |
| **Server build** | Dedicated Server **sub-target**, `StandaloneBuildSubtarget.Server`, `BuildTarget.StandaloneLinux64`; run with `-batchmode -nographics -logFile …` | §1; auto-strips render/audio/GPU assets; ADR-0008 headless. |
| **Sim loop** | Own the clock: fixed **60 Hz** tick (`Time.fixedDeltaTime = 1/60`), cap CPU with `Application.targetFrameRate`/pacing | ADR-0006, NET-5. |
| **Physics** | `Physics.simulationMode = SimulationMode.Script`; manual `Physics.Simulate(1/60)` (or per-bubble `PhysicsScene.Simulate`) | §4; ADR-0003 bubbles, ADR-0005 rigid bodies. |
| **Snapshots** | Emit at **20–30 Hz**, interpolated on clients | NET-4, NET-5. |
| **Netcode** | **Custom replication over Unity Transport (UTP 2.x)**; LiteNetLib as fallback. **No** NGO, **no** Netcode for Entities/DOTS. | ADR-0008 (custom replication), §3/§5. |
| **Hosting** | EU Hetzner VPS (vCPU, no GPU), systemd unit, vertical scaling | §6, ADR-0001. |

---

## Open questions for our case

1. **Own-clock vs. Unity FixedUpdate:** do we drive ticks from our own accumulator (cleaner single master clock, ADR-0001) or rely on Unity's FixedUpdate at 60 Hz? Decide before sim code assumes one model (ADR-0006 warns retrofitting is painful).
2. **Per-bubble `PhysicsScene` topology (ADR-0003):** one `PhysicsScene` per active bubble, stepped independently — confirm cost of N scenes vs. one shared scene with spatial separation, and how docking (two bubbles merging) is handled across scenes.
3. **UTP vs. LiteNetLib for our custom layer:** UTP is more future-proof and Relay-ready; LiteNetLib is simpler. Prototype both for our 20–30 Hz snapshot + reliable-event mix before committing.
4. **Determinism scope:** we rely on server authority + reconciliation, not lockstep determinism. Confirm float non-determinism across client/server CPUs is fully absorbed by NET-3 reconciliation (it should be) and never assumed away.
5. **CPU pacing on the VPS:** exact mechanism to cap an idle/active server tick to ~60 Hz without busy-spinning a vCPU.
6. **Licensing for CI builds:** which Unity license tier and which activation flow (Build Automation vs. manual `.ulf`) for an automated Linux server build?
7. **Snapshot rate inside 20–30 Hz:** pick a fixed snapshot interval relative to the 60 Hz tick (every 2nd vs. 3rd tick) and how it interacts with client interpolation buffering.
