-- Runs once on first container init (against the default `greenu` database).
-- The `greenu` database is created by POSTGRES_DB; add the test database.
-- Tables are created by the application (WorldRepository.Migrate, idempotent),
-- so we only create the databases here.
CREATE DATABASE greenu_test;
