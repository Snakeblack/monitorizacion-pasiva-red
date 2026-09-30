# Verification Report: S03 — proyección idempotente

Verification outcome: **PASS WITH WARNINGS**.

Full discovery on persisted `standard` route. Strict TDD active. All 19 MUST scenarios have executed runtime evidence. No CRITICAL or BLOCKER findings; one WARNING concerns historical TDD authenticity. No active remediation lineage is opened for an advisory finding.

## Completeness and recovery

| Check | Result | Evidence |
|---|---|---|
| Required standard artifacts | Complete | Proposal, four change-local specs, design, tasks and apply progress exist |
| Tasks | 14/14 complete | Tasks 1.1–4.4 checked; matching authoritative cycle entries exist |
| Assumption reconciliation | Complete | Both assumptions confirmed with resolution timestamps and approval `s03-assumptions-001` |
| Previous preflight | Resolved | The earlier FAIL represented an unresolved checklist only, not a functional finding |
| Functional candidate | Unchanged | `sha256:b858d6aeb45c3e6317475d4339e834ae090ec87f7416d377c7c79a8561e70cd2` |

All 24 functional file digests were rehashed from disk with the declared LF policy and match the functional manifest and authoritative evidence snapshot. No source/test drift was found. The candidate here is the strict-TDD functional projection identity, not an invented bounded-lineage Candidate snapshot.

## Executed verification

`dotnet test Monitoring.slnx` was executed once in this verify phase: exit 0, 109 passed, 0 failed, 0 skipped; test duration 19 seconds. It compiled Domain, Persistence, Host and Tests and exercised PostgreSQL through Testcontainers. The same execution satisfies scenario cross-reference and the declared tests quality gate; no duplicate test run was performed.

Apply's recorded standalone build had exit 0, zero warnings and zero errors. Verify did not repeat that build: the current suite compiled all projects successfully. `git diff --check` passed; Git emitted line-ending notices only. Coverage and separate linter/type-checker metrics are unavailable according to cached config; no coverage percentage is claimed.

## Behavioral compliance matrix

Each row below is a MUST scenario, has `runtime-test` evidence from the successful suite, and passes. Test names are abbreviated to their owning class and method; production paths are relative to `src/`.

| Requirement / scenario | Implementation | Executed tests | Verdict |
|---|---|---|---|
| contrato-001 / Sesión válida | Domain/Sessions/SyntheticSessionContract.cs | SyntheticSessionContractTests.ValidSessionsPreserveOriginalValues | PASS |
| contrato-001 / Límites válidos | Same parser, protocol/port/date checks | Same theory: TCP/UDP, IPv4/IPv6, 0/65535, equal instants and 0–3 decimals | PASS |
| contrato-001 / Objeto no reconocido o inválido | Exact nine fields, duplicate/type/IP/port/date validation | SyntheticSessionContractTests.UnknownOrInvalidSessionsRemainUnrecognized: 34 cases | PASS |
| contrato-002 / Datos arbitrarios aceptados | S02 ingestion retained; projector validates accepted data | SessionHostTests.AckPrecedesControlledProjectionAndResendDoesNotConsumeQuotaAgain | PASS |
| proyección-001 / Proyección inicial | Persistence/Sessions/SessionProjector.cs | SessionProjectionTests.InitialProjectionAndEqualReplayPreserveOneSessionPerOrigin | PASS |
| proyección-001 / Replay y concurrencia | Composite key, row lock, equal-content comparison | InitialProjectionAndEqualReplayPreserveOneSessionPerOrigin; ConcurrentWorkersAndSkippedLockedEventRecoverOnNextPass | PASS |
| proyección-001 / Identificador compartido entre orígenes | Composite site/sensor/event identity | InitialProjectionAndEqualReplayPreserveOneSessionPerOrigin; SessionHostTests.DetailReturnsExactlyFiveFieldsAndSharedIdStaysInTrustedScope | PASS |
| proyección-002 / Fallo entre proyección y marcado | Single transaction, disposed on failure | SessionProjectionTests.MarkingFailureRollsBackInsertAndRestartRecoversPending | PASS |
| proyección-002 / Recuperación tras reinicio | New worker scope/context and pending selection | MarkingFailureRollsBackInsertAndRestartRecoversPending; SessionWorkerTests.CancellationDuringMarkingReleasesTransactionAndRestartRecovers; TransientFailureDisposesScopeAndRetryRecoversWithNewContextAndSafeLogs | PASS |
| proyección-003 / Inválidos preceden a válidos | Bounded pass, exclusive composite cursor, 100-key pages | SessionProjectionTests.MoreThanOnePageOfInvalidUnknownAndValidEventsDoesNotStarveLaterValidEvents: 105 invalid/unknown followed by 103 valid | PASS |
| detalle-001 / Sesión encontrada | Persistence/Sessions/SessionReader.cs; Host/Sessions/SessionEndpoint.cs | SessionHostTests.DetailReturnsExactlyFiveFieldsAndSharedIdStaysInTrustedScope: five fields and persisted values | PASS |
| detalle-001 / Sesión ausente o fuera de ámbito | Parameterized triple identity and 404 | Same HTTP theory: missing and foreign-site session; headers/query cannot widen scope | PASS |
| detalle-001 / Mismo ID en ámbitos distintos | Trusted context site/sensor predicate | Same HTTP theory: three origins, distinct protocol/IP contents | PASS |
| detalle-002 / Falta contexto de lectura | Separate read feature; guard before query | SensorIdentityHeadersAndQueryDoNotAuthorizeReading; ProviderReadsOnlySeparateServerFeature | PASS |
| detalle-002 / Límite de entorno | Endpoint allows Development/Testing only | EnvironmentGuardRejectsEvenSubstitutedProviderBeforeReading: Production/Unknown; positive Development/Testing theory | PASS |
| bandeja-004 / Aceptación antes de proyección | Ingestion commit/ACK independent of worker | AckPrecedesControlledProjectionAndResendDoesNotConsumeQuotaAgain: controlled worker absence, empty HTTP 200 and pending state | PASS |
| bandeja-004 / Procesado confirmado | Projection+processed_at same commit | InitialProjectionAndEqualReplayPreserveOneSessionPerOrigin | PASS |
| bandeja-004 / Marcado fallido | Transaction rollback after inserted projection | MarkingFailureRollsBackInsertAndRestartRecoversPending: independent connections observe no projection and NULL mark | PASS |
| bandeja-004 / Reenvío después de procesar | Existing S02 idempotency and quotas | AckPrecedesControlledProjectionAndResendDoesNotConsumeQuotaAgain: identical HTTP 200 at 500-event quota, one session, no new acceptance | PASS |

Original S01/S02 tests also passed, covering acceptance, quota, atomicity, 409 conflicts and rejection counters. Arbitrary data remains accepted by S02; invalid/unknown events remain pending without starving valid events.

## Design and quality scenario coherence

The three accepted ADR decisions are implemented: composite PK/FK plus JSONB, one transaction per event; cursor with inclusive upper bound and PostgreSQL ordering; separate trusted read context and explicit environment guard. SQL values are parameterized. Cursor SQL interpolates only an internal fixed fragment. Reader JSON values are cloned after document disposal.

Additive migration is exercised from an existing S02 database with pending data and repeated execution. Composite uniqueness, foreign-key/delete restriction and the partial pending index are asserted on real PostgreSQL. Existing quota index remains present. Destructive Down is rejected as designed.

Operation tests exercise transient SQLSTATE 40001 recovery using a fresh disposed scope, cancellation during a database marking trigger, released locks, restarted worker, and fatal SQLSTATE P0001 stopping the host without payload leakage. Host tests prove the worker is registered only with persistence, migration does not consume pending events, and liveness works without persistence. Assertions on context disposal and zero reader calls verify the stated lifecycle/security constraints.

No load, latency, continuity or production readiness measurement is claimed. Pending invalid-event treatment belongs to S08, human OIDC/RBAC to S12, and capacity validation to S17, as contracted. These scope boundaries are not new defects.

## TDD compliance

The authoritative schema-v1 record and its final derived Markdown table are the machine source of truth. The installed `validateEvidenceRecord` returned `valid: true`, `authenticity: legacy-unverifiable`. Rendering equivalence is true; 14 cycle records cover all 14 task IDs, with real existing test files and current matching hashes.

| Check | Result | Details |
|---|---|---|
| TDD evidence reported | PASS | Exactly one authoritative schema-v1 block; derived table equivalent |
| All coding tasks have tests | PASS | 14/14 cycle entries; documentation/final verification share task 4.4 |
| RED tests and historical outcomes | PASS with authenticity limitation | Behavioral failures recorded in genuine TRX; historical implementation/test ordering cannot be fully authenticated |
| GREEN cross-reference | PASS | Current execution 109/109; all referenced test files included |
| Triangulation | PASS | Input variants, positive/negative results, replay/conflict/rollback/concurrency/scope cases |
| Safety net | PASS with authenticity limitation | Genuine baseline TRX: 54/54 before recorded cycle timestamps; earlier pre-write snapshots absent |

| Tasks | Unit | Observed RED / GREEN |
|---|---|---|
| 1.1, 1.2 | U1 parser | 4 behavioral failures / 38 passing |
| 1.3, 1.4 | U2 schema | Migration count 2 rather than 3 / 3 passing; separate schema RED also exists |
| 2.1, 2.2 | U3 transaction | 3 behavioral failures / 3 passing |
| 2.3, 2.4 | U4 cursor/concurrency | Starvation: 0 rather than 103 / 5 passing |
| 3.1, 3.2 | U5 scoped detail | 6 behavioral failures / 6 passing |
| 4.1, 4.2 | U6 worker | 3 behavioral failures / 3 passing |
| 4.3, 4.4 | U6 wiring/final regression | Missing worker / 12 focal and 109 final passing |

All 17 raw TRX files exist and their SHA-256 hashes match `evidence/test-runs.json`. The U2 initial report also includes a fixture relation-exists failure; that infrastructure/setup failure is not counted as behavioral RED. The later schema report supplies the real migration-count RED. Initial Docker failures are likewise excluded.

TRX files remain ignored local test outputs. The genuine pre-wiring hash snapshot exists, but does not substitute for the missing earlier snapshots. No runtime receipts or absent historical output were fabricated. A current pass proves current behavior, not the entire historical test-first sequence.

## Test layer distribution

The changed test files execute 58 cases: 55 new cases plus three updated pre-existing cases. This is a subset of the complete 109-test suite.

| Layer | Cases | Files | Tools |
|---|---:|---:|---|
| Unit | 39 | 2 | xUnit: 38 parser cases and one trusted-feature provider case |
| Integration | 19 | 7 | PostgreSQL/Testcontainers, WebApplicationFactory and real migration subprocesses |
| E2E | 0 | 0 | Unavailable; no browser/end-to-end production claim |
| Total changed-test cases | 58 | 8 distinct | SessionHostTests spans unit and integration |

## Changed file coverage and quality metrics

Coverage analysis skipped: no coverage tool detected/configured. No minimum is configured in the tests quality gate. Separate linter/type-checker metrics skipped: no tools declared. The current dotnet suite compiled the solution successfully.

## Assertion quality

All created/modified test files were inspected. No tautologies, assertion-free cases, unguarded empty ghost loops or mock-heavy cases were found. Database row counts have controlled setup and positive/negative companions. Logger assertions are guarded by a non-empty condition; foreign-scope HTTP assertions are paired with successful in-scope responses. Concrete values, status codes, transaction effects and exception classes are asserted. No additional blocking or warning assertion findings.

## Quality Gates

Policy validation: valid, no errors. All declared gates were classified and enforced; only tests is declared. The 180000-ms budget was not exceeded.

| gate | status | required | on_fail | detail |
|---|---|---|---|---|
| tests | pass | true | halt | `dotnet test Monitoring.slnx`: exit 0, 109/109, no skips |

Aggregate status: `pass`. The coordinator persisted the audit built by `quality-gates.js` at `2026-09-29T22:53:56.202Z`; verify read back `state.yaml.gates.quality-gates.status: pass` and the required/halt tests row. Executor writes were limited to this report and matching assumption resolutions. No CRITICAL findings require `verify_lineage` allocation.

## Findings

### S03-W001 — Historical Strict TDD evidence is not runtime-authenticated

- Severity: WARNING.
- Origin: `tasks-gap` (historical execution provenance completeness; not a code bug).
- Affected paths: `apply-progress.md`, `evidence/test-runs.json`, `evidence/wiring-red-snapshot.json` under this change.
- Evidence: valid schema-v1 historical working-tree record; 17 matching genuine TRX digests; current functional manifest unchanged; no earlier authenticated receipts or all pre-write snapshots.
- Implication: current behavior and observed RED/GREEN outcomes are proven, but the complete test-first ordering of every earlier functional edit cannot be authenticated. The validator explicitly supports historical legacy records; no declared project policy requires `requireHistoricalAuth`, so this limitation is advisory rather than an invented CRITICAL contract failure.
- Workaround: retain honest historical evidence; capture runtime-authenticated RED/GREEN receipts and pre-write snapshots for future slices; never reconstruct missing history.
- Archive disposition: requires explicit risk acceptance or follow-up conversion before archive, after the declared quality-review gate.

No further production access or pending-invalid warning is raised because those limits were already explicitly approved in the behavioral contract.

## Operative-memory handoff

The executor's permitted write targets exclude `openspec/memory/known-issues.md`. The qualifying WARNING and exact normalized memory entry are returned to the coordinator for prepend/dedup persistence; no additional memory file was written or falsely listed as an artifact.

## Next action

Coordinator: persist the warning memory entry, project this phase result, run the declared quality-review gate, and resolve S03-W001 for archive. No implementation remediation or bounded verify lineage is warranted by this report.
