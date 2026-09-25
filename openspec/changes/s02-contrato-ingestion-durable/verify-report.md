# Verification Report: s02-contrato-ingestion-durable

**Route:** standard fallback (no `routing:` in `openspec/config.yaml`).
**Mode:** Strict TDD.
**Verdict:** **FAIL**.

## Completeness

| Metric | Result |
|---|---:|
| Tasks in `tasks.md` | 15 |
| Marked complete | 15 |
| Incomplete | 0 |
| Required standard artifacts | Proposal, two specs, design, tasks and apply progress present |

## Build, tests and quality gate

| Command / gate | Result | Evidence |
|---|---|---|
| `dotnet test Monitoring.slnx` | PASS | 53 passed, 0 failed, 0 skipped; PostgreSQL Testcontainers tests executed. |
| `dotnet build Monitoring.slnx --no-restore` | PASS | 0 warnings, 0 errors. |
| Required `quality_gates.tests` (`dotnet test Monitoring.slnx`, 180 s, halt) | PASS | Same full-suite execution completed in 21 s; no separate repeat needed. |
| Manual verification | Not performed | Automated HTTP/PostgreSQL tests provide runtime evidence. |
| Coverage | Unavailable | `testing.coverage.available: false`; no threshold declared. |

## Spec compliance matrix

| Requirement | Scenario | Strongest evidence | Result | Evidence and limit |
|---|---|---|---|---|
| `REQ-contrato-ingestion-v1-001` | Valid v1 batch | runtime-test | PASS | `IngestionContractTests.ContractAcceptsV1AndMaximumIdentifierAndEventBoundaries`; HTTP acceptance in `IngestionHostTests`. |
| `REQ-contrato-ingestion-v1-001` | Unsupported version, fields, types and limits: 400, no rows | runtime-test | PASS | Contract boundary tests plus HTTP invalid/oversized tests reject before writer invocation. |
| `REQ-contrato-ingestion-v1-002` | Trusted identity matches; only that origin processes | runtime-test | PASS | Host passes resolved identity to writer; matching HTTP and persisted row tests. |
| `REQ-contrato-ingestion-v1-002` | Wrong origin: 403, no rows; absent identity: 401 | runtime-test | PASS | `IngestionHostTests` rejects before writer; headers and bearer claims do not establish identity. |
| `REQ-bandeja-ingestion-durable-001` | New batch: commit, then empty 200 and atomic acceptance | runtime-test | PASS | `AckFollowsCommitAndReorderedObjectResendIsIdempotent` reads PostgreSQL after HTTP 200; writer commits before returning `Accepted`. |
| `REQ-bandeja-ingestion-durable-001` | Failure before commit: no 200, no new rows | runtime-test | PASS | `FailureBeforeCommitNeverAcknowledgesOrPersistsBatch` injects an insert trigger failure. |
| `REQ-bandeja-ingestion-durable-002` | Identical resend: empty 200, one acceptance | runtime-test | PASS | Reordered object and lost-response retry tests inspect HTTP and row count. |
| `REQ-bandeja-ingestion-durable-002` | Concurrent identical resend | runtime-test | PASS | `ConcurrentIdenticalRequestsCreateOneAcceptance`: both 200, one row. |
| `REQ-bandeja-ingestion-durable-002` | Same ID, different JSON value: 409, original preserved | inspection-proof with contradictory source evidence | **FAIL** | `InboxWriter` compares parsed `DateTimeOffset` values, so distinct allowed `occurredAt` JSON strings for the same instant are incorrectly treated as identical. See C-01. Existing tests cover changed `data` and array order but omit this case. |
| `REQ-bandeja-ingestion-durable-003` | Per-origin rolling quota | runtime-test | PASS | 500 boundary, 61-second expiry, origin separation and concurrent admissions tested against PostgreSQL. |
| `REQ-bandeja-ingestion-durable-003` | Over-limit batch: 429, no partial rows, scoped counter | runtime-test | PASS | Persistence test checks 429 and row count; host metric test checks one trusted-origin measurement. |

**Compliance:** 10/11 specified scenarios meet their required evidence and behavior. The failed MUST scenario prevents a pass.

## Correctness and design coherence

| Decision or invariant | Result | Evidence |
|---|---|---|
| Trusted host identity, no client-selected origin | Followed | Feature-based provider; 401/403 before the writer. |
| One transaction and origin row lock for quota and idempotency | Followed | `InboxWriter` locks with `FOR UPDATE`, reads recent rows, inserts and commits in one transaction. |
| Composite uniqueness and temporal index | Followed | Migration and `InboxSchemaTests` verify both on PostgreSQL. |
| Structured JSON equality, including `occurredAt` as JSON content | **Deviates** | `data` uses PostgreSQL `jsonb`, but `occurredAt` is stored as `timestamptz` and compared by instant, losing the original JSON string representation. |
| ACK only after durable commit; lost response retry | Followed | Writer commits before returning accepted; retry test discards first response after a committed row exists. |

## Strict TDD compliance

| Check | Result | Details |
|---|---|---|
| TDD evidence present | PASS | `TDD Cycle Evidence` tables and `json:strict-tdd-evidence` block exist in `apply-progress.md`. |
| Task coverage | PASS | All 15 checked tasks are represented by grouped TDD cycles or verification/refactor rows. |
| Test files exist and run | PASS | Four change test files; 49 associated cases pass in the 53-case suite. |
| RED provenance | WARNING | Historic RED results are recorded for contract, host, schema, writer, quota and metrics. Task 3.4 explicitly records no new RED: its added tests passed initially because prior implementation already supplied the behavior. A present-day pass cannot prove historic RED. |
| Functional snapshot | PASS | All 17 SHA-256 entries in the persisted snapshot match disk exactly. |
| GREEN | PASS | Current suite passes; recorded historical GREEN is consistent with present execution. |
| Triangulation and safety net | PASS | Boundary, negative, concurrent and integration cases exist; safety net was recorded for modified behavior. |
| Assertion quality | PASS | Reviewed all four change test files; assertions inspect HTTP status/body, PostgreSQL rows/content, schema, metrics or parser values. No tautology, empty ghost loop or assertion-free test found. |

The historical RED commands are documented in apply progress but their earlier runner output is not independently recoverable from this checkout. This limits process provenance, not the current runtime result.

### Test layer distribution

| Layer | Tests | Files | Tool |
|---|---:|---:|---|
| Unit | 29 | 1 | xUnit |
| Integration | 20 | 3 | `WebApplicationFactory`, PostgreSQL/Testcontainers |
| E2E | 0 | 0 | Not configured |
| **Change total** | **49** | **4** | |

### Changed-file coverage and quality metrics

Coverage analysis skipped: no coverage tool is configured. No separate linter or type checker is configured. `dotnet build` completed without warnings or errors.

## Traceability

| Requirement | Tasks | Implementation / tests | Status |
|---|---|---|---|
| `REQ-contrato-ingestion-v1-001` | 1.1, 1.2, 1.4 | `BatchContract`, `BatchEndpoint`; contract and host tests | Covered |
| `REQ-contrato-ingestion-v1-002` | 1.3, 1.4 | Trusted identity provider and host tests | Covered |
| `REQ-bandeja-ingestion-durable-001` | 2.1, 2.3, 3.4 | `InboxWriter`; persistence and retry tests | Covered |
| `REQ-bandeja-ingestion-durable-002` | 2.1, 2.3, 2.4, 3.4 | `InboxWriter`; duplicate/concurrent/conflict tests | **Defect C-01** |
| `REQ-bandeja-ingestion-durable-003` | 3.1, 3.2, 3.3 | Writer quota and host metrics tests | Covered |

## Findings

### CRITICAL

- **C-01 — `code-bug` — distinct `occurredAt` JSON values are accepted as identical.** `REQ-bandeja-ingestion-durable-002` requires equality of the event's JSON value, and the design includes `occurredAt` in that comparison. Both `2026-09-24T12:30:00Z` and `2026-09-24T12:30:00.000Z` are valid under `REQ-contrato-ingestion-v1-001`; the strings differ, but parsing them yields equal `DateTimeOffset` values (confirmed with the runtime's parser). `InboxWriter.SameTimestamp` and `FindExistingAsync` compare only those parsed instants, while the table retains only `timestamptz`. A resend with the same origin, ID and `data` but the other timestamp representation returns 200 instead of 409. This also means the first event's full original content cannot be reconstructed from the inbox row. The regression recipe is an HTTP/PostgreSQL test that first accepts one representation, then sends the other and asserts 409, one row and unchanged first content.

### WARNING

- **W-01 — `tasks-gap` — task 3.4 has no observed RED.** Apply progress explicitly records that the new response-loss regression test passed on first execution. The behavior had been introduced and tested earlier, so this is a process evidence limitation rather than proof of a runtime failure. Do not retroactively claim a RED run.

### SUGGESTION

None. S17 capacity, mTLS and projection are outside S02.

## Verify lineage handoff

The frozen remediation scope for this discovery is **C-01** only (`code-bug`): `src/Monitoring.Persistence/Ingestion/InboxWriter.cs`, the inbox storage mapping/migration if needed to preserve JSON timestamp identity, and `tests/Monitoring.Tests/IngestionPersistenceTests.cs`. Validate with a new focal HTTP/PostgreSQL regression test and `dotnet test Monitoring.slnx`. The runtime/orchestrator owns canonical `verify_lineage` state projection; this executor did not mutate `state.yaml` or fabricate a candidate snapshot.

## Verdict

**FAIL** — the current suite and build pass, but one MUST idempotency scenario conflicts with the implemented timestamp comparison. Route C-01 to `sdd-apply` in bounded remediation mode; keep W-01 as an advisory provenance finding.
