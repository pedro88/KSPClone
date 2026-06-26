# Single Postgres as the authoritative durable store

One Postgres database holds *all* durable state: progression (tech tree, science, funds) and world/vessel state (orbital elements, crew assignments, suspended-vessel snapshots as JSONB). The server keeps a hot in-memory copy of the live simulation and writes through to Postgres on meaningful events (SOI change, demotion, suspension, committed warp endpoints).

We rejected splitting world state into a separate store (Redis/KV/snapshot files). At 4 players the entire universe is kilobytes to low megabytes: an on-rails vessel is a small row (orbital elements + vessel clock + crew refs), and an active vessel suspended is a JSONB snapshot. A single store gives one backup/restore story and atomic consistency between coupled facts ("science was awarded" and "the vessel that earned it reached orbit"). A second datastore would be premature complexity at this scale; it can be introduced later if write volume ever justifies it.
