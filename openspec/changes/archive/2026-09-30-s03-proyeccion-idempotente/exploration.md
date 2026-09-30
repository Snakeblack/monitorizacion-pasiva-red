## Exploration: S03 — proyección idempotente y lectura mínima

### Current State

S02 stores one row per event in `monitoring.ingestion_inbox`, keyed by `(site_id, sensor_id, event_id)`. The row also contains `batch_id`, `schema_version`, `occurred_at`, its original UTC text, arbitrary object-valued `data` as `jsonb`, and `accepted_at`. The trusted site/sensor identity is resolved by the host and checked against the submitted batch before persistence; S02's `InboxWriter` then uses PostgreSQL transactions and the origin key. There is no worker, session projection, detail endpoint, or user authorization implementation yet.

The project keeps ASP.NET Core composition in `Monitoring.Host`, domain types free of EF/Npgsql, and PostgreSQL mappings/migrations in `Monitoring.Persistence`. `Program.cs` currently builds the host, registers ingestion services and maps health plus ingestion routes. `PostgresFixture` and Testcontainers provide disposable PostgreSQL integration tests; existing persistence tests cover S02 atomicity and replay patterns. ADR-014 keeps PostgreSQL provisional pending S17.

The existing input contract is intentionally generic: `data` may be any JSON object and has no event kind, session schema, or version field beyond the outer `schemaVersion`. The sources say S03 projects a synthetic session, but do not define the session fields, how session events are distinguished from other event data, or which values detail returns. Those public behavior decisions need clarification before writing normative S03 requirements.

### Affected Areas

- `src/Monitoring.Host/Program.cs` — composition root for a hosted worker and a minimal detail route.
- `src/Monitoring.Host/Ingestion/` — trusted identity is currently an ingestion-only host feature; detail access will need its own trusted caller/site scope boundary.
- `src/Monitoring.Domain/Ingestion/BatchContract.cs` — confirms `data` is arbitrary JSON; avoid silently imposing a session shape on the general S02 contract.
- `src/Monitoring.Persistence/Ingestion/InboxWriter.cs` and `IngestionInboxEntity.cs` — established inbox transaction and source identity; worker should consume persisted scope, never re-trust payload-declared scope.
- `src/Monitoring.Persistence/MonitoringDbContext.cs` and `src/Monitoring.Persistence/Migrations/` — add projection storage, processing state, keys, and indexes in an additive versioned migration.
- `tests/Monitoring.Tests/IngestionPersistenceTests.cs`, `IngestionHostTests.cs`, `PostgresFixture.cs` — reuse real PostgreSQL and host-level test patterns for replay, crash recovery, scoping, and API responses.
- `openspec/specs/contrato-ingestion-v1/spec.md`, `openspec/specs/bandeja-ingestion-durable/spec.md` — baseline contracts to preserve; neither defines session data or projection semantics.
- `docs/development/slices.md`, `docs/roadmap.md`, `docs/architecture/technical-baseline.md`, `docs/product/functional-scope.md`, `docs/architecture/decisions/ADR-014.md` — S03 scope, exclusions, data flow, access direction, and PostgreSQL caveat.

### Approaches

1. **Typed synthetic session envelope within `data`** — Add a discriminator and a narrowly specified session payload for S03; leave the outer S02 event contract generic. Persist a session row and processed marker atomically per inbox event, with a unique source/event key on the projection.
   - Pros: clear dispatch and validation contract; unknown/non-session events can remain pending or follow an explicitly agreed policy; uniqueness is a second idempotency barrier.
   - Cons: requires deciding field names/types and malformed/unknown-event handling; adds a contract versioning choice.
   - Effort: Medium.

2. **Treat every S03 inbox row as a session fixture** — Interpret all `data` objects delivered in the isolated S02→S03 path as session payloads and project them directly.
   - Pros: minimal new event envelope and small vertical slice.
   - Cons: ambiguous once multiple event kinds exist; makes malformed payload policy and API data shape implicit; risks changing the meaning of the generic S02 contract.
   - Effort: Low initially, with higher follow-on migration risk.

For processing, prefer a transaction per event: select one pending inbox row, insert/upsert its projection under a unique `(site_id, sensor_id, event_id)` key, then mark that inbox row processed and commit. A failure before commit rolls back both effects; a crash after commit leaves both durable, and a later replay sees the processed row or existing unique projection. PostgreSQL row locking/`FOR UPDATE SKIP LOCKED` is a viable concurrent-worker mechanism, but should be selected in design based on the desired worker concurrency. A batch-wide transaction is another option, but increases lock duration and rollback scope.

### Recommendation

Keep the existing S02 JSON v1 envelope and generic `data` semantics intact. Specify an S03 synthetic-session payload discriminator and fields in a separate change-local spec, with explicit behavior for unknown or malformed events; the current sources do not support choosing those public details. Store projection state and a processed timestamp on the inbox row (or an equivalent explicit state), and keep the session projection plus processed transition in one PostgreSQL transaction. Add a unique source/event identity to the projection so retries remain safe even if processing is invoked again. Process one event per transaction for bounded rollback and recovery, and consider `SKIP LOCKED` if concurrent worker instances are in scope.

For detail, use a parameterized lookup by session ID that also requires the caller's trusted site/sensor scope in the predicate; return not-found for rows outside that scope to avoid disclosing their existence. However, the current repo defines no trusted human/API identity or RBAC implementation, so the S03 fixture authorization boundary and exact response contract need to be agreed. Do not derive read authority from an arbitrary client-supplied site or sensor ID.

Strict TDD should add a failing PostgreSQL integration test for replay producing one projection, a failure injected between insert and marking proving both roll back, and a subsequent worker run recovering the pending event. Add host/API tests for detail found, absent, and out-of-scope behavior. These are test recommendations, not production code changes in this phase.

### Material Questions for Specification

- What exact synthetic session fields and JSON types belong in `data`, and what discriminator/version identifies it? Are TCP/UDP endpoints, ports, protocol, VLAN, start/end time, and partial/inferred status required now?
- What should the worker do with malformed or unknown event kinds: leave pending and retry, mark rejected/quarantined, or mark processed with no projection? Automatic retry of a permanently invalid item could block useful progress.
- What is the stable public session ID and detail response shape? Reuse `(siteId, sensorId, eventId)` as ID, expose `eventId`, or generate a database ID? This affects callers and uniqueness, so it is costly to change later.
- What trusted identity is available to the detail endpoint in S03? Current trusted identity represents a sensor and grants ingestion only; the product baseline reserves human OIDC/RBAC for S12. A test-only trusted site/sensor context can prove isolation, but must not be mistaken for production user authorization.
- Should detail return 404 for an out-of-scope ID (recommended to conceal existence) or 403 (clearer authorization result)? The choice is observable API behavior.

### Risks

- `data` is arbitrary JSON today. Projecting it as a session without a discriminator/schema creates implicit behavior and may make future event kinds unsafe or ambiguous.
- Marking processed separately from writing the session creates loss/duplication windows; both operations must share one database transaction, including for crash recovery.
- The inbox primary key already scopes by site and sensor. Any projection key or API query that omits that scope risks collisions or cross-scope disclosure.
- Production human authorization is explicitly later (S12); S03 must not present a test identity seam as production RBAC.
- Existing tests use real disposable PostgreSQL, which is the right evidence for transaction semantics; a fake repository cannot prove PostgreSQL rollback/locking behavior.
- PostgreSQL remains provisional. This slice can verify correctness and small-scale behavior only, not ADR-013 capacity targets.

### Ready for Proposal

Yes, for a bounded proposal that records the above contract decisions as questions to resolve before spec completion. Keep scope to synthetic event projection, atomic processed marking, scoped detail, and replay/crash-recovery tests; exclude real capture, event correlation, listing/search, Angular, production OIDC/RBAC, and capacity claims. If the owner cannot confirm the event and detail contracts during clarification, keep the change blocked before implementation rather than inventing them.
