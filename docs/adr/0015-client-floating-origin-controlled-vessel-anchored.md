# Client floating origin: a single controlled-vessel-anchored frame

How the client places vessels in Unity float32 from authoritative world doubles, without the server's K-origin bubble machinery.

## Context

The server runs K concurrent bubbles, each with its own `GlobalOrigin` double anchor and its own `PhysicsScene` (ADR-0003, ADR-0012). The client has none of that: it receives per-vessel world-frame state as doubles in snapshots (ADR-0013), predicts its own controlled vessel locally (NET-2), and interpolates the rest (NET-4). It still has to subtract a large double world position down to a float Unity transform — and that subtraction must not destroy precision (the kraken; ADR-0012 §6).

ADR-0012 already states the intended client model under "What this is not": *"Camera rides the controlled vessel in the same float-local frame — no separate render-scale scene at our single-system scope"*, and *not* a scaled-space scheme. This ADR makes that concrete.

## Decisions

### 1. One client origin, re-anchored on the controlled vessel every frame

The client holds a single `RenderOrigin : double3`. Each rendered frame:

```
RenderOrigin = controlledVessel.PredictedWorldPosition           // doubles
foreach rendered vessel v:
    v.transform.position = (float)(v.WorldPositionDouble - RenderOrigin)   // subtract in doubles, then narrow (ADR-0012 §6)
```

The controlled vessel sits at ≈(0,0,0) in float every frame — full precision exactly where the player looks and where prediction/reconciliation operate. Vessels within physics range (≤ a few km) are small floats; far vessels produce large floats but are not nearby and are culled, never rendered with precision demands.

Because the origin tracks the controlled vessel continuously, there is **no rebase threshold and no `FloatingOriginShifted` event on the client** — the per-frame re-anchor *is* the rebase, and it is free (one double subtraction per vessel). This is deliberately unlike the server, which rebases in discrete 1024 m steps (ADR-0012 §1) because its origin is shared by a whole cluster and must stay stable across a tick for PhysX.

### 2. No-controlled-vessel fallback

Before the player occupies a station (pre-`OccupyStation`, see ADR-0016) or after they leave, there is no predicted controlled vessel. The origin then anchors on the handshake/last-known world position of the vessel in focus. Rendering always has a frame; the player simply isn't predicting anything yet.

## What this is not

- **Not** the server's floating origin. The server origin is per-bubble, double-anchored, rebased in fixed steps, and authoritative. The client origin is single, ephemeral, re-derived each frame, and presentation-only.
- **Not** scaled space. Celestial bodies are not rendered to scale in a separate render scene at our single-system scope; the camera lives in the controlled vessel's float-local frame.
- **Not** K simultaneous client origins. The client renders one neighbourhood (the controlled cluster) at full precision; it does not need a precise frame for every parallel mission at once.

## Satisfies

PHYS-1 / ADR-0012 (float precision at interplanetary distance) on the client without duplicating server bubble state. NET-2/NET-4 by giving prediction (controlled) and interpolation (others) a common, precise local frame to render into.
