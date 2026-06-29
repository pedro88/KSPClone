# Persistence uses Npgsql 8 netstandard2.1, vendored as a DLL in Assets/Plugins

The persistence layer targets **Npgsql 8.x netstandard2.1**, copied as a single `Npgsql.dll` into `Assets/Plugins/Npgsql/`. Not a NuGet package, not NuGetForUnity, not a Unity Package Manager dependency. Just a vendored DLL referenced from the `KSPClone.Persistence` asmdef's `precompiledReferences`.

Why vendored: Unity 6 LTS supports the netstandard2.1 API surface, and the Npgsql 8 netstandard2.1 build is **self-contained** — all of its transitive BCL dependencies (System.Diagnostics.DiagnosticSource, System.Threading.Channels, System.Text.Json, System.Collections.Immutable, etc.) live in the .NET Standard 2.1 profile that the Unity Mono runtime ships with. One DLL, no dependency chase.

We evaluated Npgsql 6.x netstandard2.0 first. It pulls six BCL packages as hard dependencies, several of which fight with Mono's existing BCL (System.Text.Json in particular has Mono compatibility issues). 8.x netstandard2.1 is the path of least resistance.

The cost: when we update Unity or Npgsql we re-vendor the DLL manually. At the cadence this project moves, that is a quarterly chore at worst — cheap, and the alternative (a NuGetForUnity dependency) means a third-party package manager in the build pipeline, which has its own failure modes (lock file drift, CI restore failures).

Rejected: System.Data.SqlClient-style ODBC drivers — wrong database. Microsoft.Data.Sqlite — wrong database. Raw libpq via P/Invoke — six months of yak-shaving for no payoff. Hand-rolled Postgres wire protocol — same.

The EditMode tests in `Assets/Tests/EditMode/WorldRepositoryTests.cs` and `WorldRestorerTests.cs` exercise the same `Npgsql.dll`; they probe the local Postgres container (greenu_test, port 5433) and skip gracefully if unreachable, so CI on a machine without Postgres is not blocked.

To revisit: if we ever need Postgres LISTEN/NOTIFY, JSONB indexing on snapshot content, or COPY-based bulk loads at high throughput, we will revisit the choice — but those are M2+ concerns, and 8.x supports all of them.