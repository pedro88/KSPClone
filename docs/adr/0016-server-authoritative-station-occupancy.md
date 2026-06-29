# Server-authoritative station occupancy and pilot-input authority

How a connected player comes to control a vessel, and where the "is this player allowed to command this vessel?" check lives. A dev stub for the still-open identity/join/auth decision.

## Context

M1 has no notion of a player controlling a vessel: `ServerSimulation.Connect` adds a `PlayerSession`, and `PromotionController.RequestPlayerLoad` is never called. The full M1 loop needs a trigger that promotes a vessel and a rule that decides whose pilot input is authoritative (NET-1: server is sole authority; Art. 6: control partitioned, never contended). Identity/join/auth is an explicitly open roadmap item, so this is a **stub** sized for M0–M3, not the final design.

The glossary already defines **Station** (Pilot/Engineer/Navigator, disjoint system sets) and **Occupying** (a player occupies at most one station; disconnect vacates; last-leave demotes-or-suspends). This ADR wires those concepts; it does not invent new vocabulary.

## Decisions

### 1. `ControlRegistry` in SimCore, keyed (VesselId, Station) → PlayerId

A SimCore registry (mirroring `ConnectionRegistry`) records station occupancy. `ServerSimulation` owns it and exposes:

- `OccupyStation(playerId, vesselId, station)` — records occupancy and, for `Pilot`, calls `PromotionController.RequestPlayerLoad(vesselId)`.
- `IsOccupied(vesselId)` — any station occupied; this is the `occupancyLookup` the demotion/suspension passes consume (so leave → demote-or-suspend falls out for free).
- `Disconnect(playerId)` also vacates that player's stations.

A new wire command `OccupyStation(vesselId, station)` carries the request; the client auto-occupies `Pilot` on the first handshake vessel for the slice. ("Claim" is an informal synonym; the canonical verb is **occupy**.)

### 2. The pilot-input authority gate lives in SimCore, not the transport

`ServerSimulation.SubmitPilotInput(playerId, msg)` checks `ControlRegistry.Owner(msg.VesselId, Pilot) == playerId`; if not, the input is rejected and counted. Only on success does it reach `InputChannel.Submit`. `ServerNetHost` merely maps peer→player, decodes, and forwards — it holds no authority logic.

This keeps authority as domain logic covered by headless SimCore tests, consistent with ADR-0009. Field-level seat authority (Pilot owns throttle+attitude, rejects engineer/staging fields) stays where it already is, inside `InputChannel.Submit`.

## What this is not

- **Not** authentication. There is no identity verification, no reserved seats, no anti-spoofing — a peer is trusted to be whoever the server assigned on connect. That is the open identity/join item, deliberately deferred.
- **Not** the crew/automation layer (M2). Automation fallback for an empty station is out of scope here; this ADR only routes a *present* pilot's input and detects the unattended case for the leave path.

## Satisfies

NET-1 (server sole authority over applied input), Art. 6 (control partitioned by station, contention impossible by construction), and supplies the occupancy signal SUSP-2/3 need for the last-leave demote-or-suspend branch.
