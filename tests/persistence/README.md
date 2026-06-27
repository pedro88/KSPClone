# Persistence tests

Schema round-trip test for the M0 persistence layer, runnable from the
command line without Unity. Connects to the local Postgres dev container
(greenu_test database, port 5433).

```sh
pip install psycopg2-binary
python3 tests/persistence/test_schema_roundtrip.py
```

The EditMode tests under `Assets/Tests/EditMode/WorldRepositoryTests.cs`
exercise the same schema through the C# `WorldRepository` from inside
Unity Editor.