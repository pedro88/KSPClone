# The design-time/flight-time split is an assembly boundary

Constitution Art. 7 says construction and flight are separate systems. M3 makes that a *machine-checkable* boundary rather than a convention, and pins the few load-bearing choices in the VAB slice.

## Context

The VAB (M3) edits *Designs* — part trees with no world state — while flight (M0–M2) simulates *Vessels* with position, velocity, resources, and crew. Art. 7 forbids construction from touching the vessel replication/physics path. A comment can't enforce that; a wrong `using` would. The compiler can.

## Decisions

### 1. `KSPClone.Construction` references nothing

The Construction assembly's asmdef has `"references": []` and `noEngineReferences: true`. It cannot see SimCore (flight/physics), Net, Server, Client, or UnityEngine — the compiler rejects any attempt. That is the enforceable form of Art. 7: design-time code physically cannot reach into flight-time code. The part-tree model, edit ops, op-log, sessions, replica, and locks all live here as pure C#.

Consequence: Construction can't use SimCore's `Vector3d`/`Quaterniond`, so a part's local transform is its own plain-data `PartPose` (7 doubles, no rotation math). The launch boundary converts it to flight types. Duplicating a handful of fields is the price of a crisp boundary; we chose it deliberately over a shared-primitives assembly refactor.

### 2. The op-log's source player is a raw `Guid`, not `PlayerId`

`PlayerId` lives in SimCore. If `SequencedEditOp` used it, Construction would reference SimCore and the boundary would fall. So the op-log stores `Guid` and the Net layer maps `PlayerId ↔ Guid` at the wire boundary (M3-T04). Same reasoning for `DesignEditorSessions` membership.

### 3. Launch is its own assembly — the sole meeting point

`KSPClone.Launch` references *both* SimCore and Construction and contains only `LaunchInstantiator`. It reads an immutable snapshot of a Design and produces a Vessel. Neither SimCore nor Construction references the other; they meet exactly here (BUILD-4). Putting this in SimCore would force SimCore→Construction; putting it in Construction would force Construction→SimCore. A dedicated tiny assembly keeps both clean and keeps the conversion testable in EditMode without Unity.

### 4. The server assigns node ids; edits serialize in arrival order

Node ids are monotonic per Design and allocated by the server on an accepted add (`Design.AllocateNodeId`), never by the client — the client's proposed id is a temp handle mapped back in the ack. So two concurrent adds can never collide, with no CRDT (Art. 1). The canonical `PartTree` is the fold of the append-only `EditOpLog` in seq order; that fold-equals-tree invariant is the correctness contract for replication and persistence.

### 5. Designs persist relationally, folded-tree-only

`DesignStore` writes `design` + `design_node` rows (no JSON dependency), replacing a Design's whole node set in one transaction so a crash never tears the (tree, seq, next-node-id) triple. Restore rebuilds the tree by walking parent→child from the root — id order isn't safe after a move re-parents a low-id node under a high-id one; the parent link is. An op-log audit table is deferred; the folded tree plus seq restores the identical state.

## What this is not

- **Not** a runtime-wired feature end to end. The engine-agnostic mechanisms (op-log, ordered replica, locks, launch) are unit-tested; the transport dispatch (ServerNetHost/ClientNetPeer handling the new message tags) and DB write-through wiring are runtime/DB-pending, validated in-editor next — the same staging M1 used.
- **Not** a propulsion model. Launch sums part masses; engines/resources and real inertia come with the propulsion-parts slice.

## Satisfies

BUILD-1 (Design as a part tree distinct from a Vessel), BUILD-2 (server-serialized op-log broadcast to editors), BUILD-3 (subtree locks), BUILD-4 (launch instantiates a Vessel). Makes Constitution Art. 7 machine-checkable and keeps Art. 1 (server authority, no CRDT) in the construction path.
