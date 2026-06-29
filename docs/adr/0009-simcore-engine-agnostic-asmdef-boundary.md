# SimCore is engine-agnostic; transport lives in Server / Client assemblies

The simulation core (`KSPClone.SimCore` Unity assembly) is pure C# with `noEngineReferences: true`. Nothing inside it imports `UnityEngine.*`. The headless server host (`KSPClone.Server`) and the (future) client host are separate Unity assemblies that can import `UnityEngine`, hold MonoBehaviours, and talk to the wire transport — they call into SimCore, not the other way around.

This is enforced at compile time by the asmdef: `Assets/Scripts/SimCore/KSPClone.SimCore.asmdef` has `noEngineReferences: true`; Unity's compiler refuses any `using UnityEngine;` inside SimCore. The Assembly-CSharp-Editor test runner cannot run SimCore tests without Unity because the tests live in `Assets/Tests/EditMode/` — but the production code is engine-free and unit-testable in principle from a plain `dotnet test`.

Why this matters: the hard part of the project is **time, replication, and authority**. None of that depends on UnityEngine. Folding Unity in early would mean every Kepler solve, every warp state-machine transition, every connection-registry event is gated by a Unity Editor being open. The C# `dotnet` test runner can not exercise any of it. Slow feedback loop, harder to refactor.

The seam also keeps the persistence layer (`KSPClone.Persistence`) engine-free: Npgsql is plain .NET, no UnityEngine, no MonoBehaviour. Postgres-backed restore is testable from a Python script that issues the same SQL the C# repo runs — that is exactly how `tests/persistence/test_*.py` validates the schema.

**Rejected:** the conventional Unity layout that puts gameplay scripts in `Assets/Scripts/` with `using UnityEngine;` everywhere. It is simpler to start with, and a nightmare to disentangle later once half the codebase touches `MonoBehaviour` and `Transform`.

**Rule of thumb:** if a class does not need `MonoBehaviour`, `Vector3`, `Quaternion`, `Debug.Log`, or any Unity asset reference, it belongs in SimCore. If you find yourself adding `using UnityEngine;` to a SimCore file, the file is in the wrong assembly.