# Verification Report: s02-contrato-ingestion-durable

**Route:** standard fallback (no `routing:` in `openspec/config.yaml`).
**Mode:** Strict TDD, targeted recheck of frozen finding C-01.
**Verdict:** **PASS WITH WARNINGS**.

## Bounded lineage and candidate

| Check | Result |
|---|---|
| Lineage | Generation 2, `sha256:2fb7b5f9a50b72ca9be8eafda625e691ad4082e990a27562f037123c3049e3d9` |
| Frozen finding | C-01 only; `code-bug`; remediation attempt 1 of 2 |
| Current Candidate | `sha256:eba539f0167a64544598589d8d30a5800494fa5bae936ee6bc5769d00b377e0c` |
| Snapshot | Recovered with `verifyLiveWorkspace: true`; candidate identity and Git tree match |
| Contract digest | `sha256:6ddb9ab7fa36eb4c6a7722d4849eaa899c1cdb7cc37c45ce9deed53fc9909185`, unchanged |
| Router | `run-targeted-recheck`; no full discovery |

The isolated recheck worktree used base commit `204a499` and the exact functional files from correction commit `45ecaad`. The active worktree's later commit changes the Git base tree, which prevents `verifyLiveWorkspace: true` there; the isolated worktree restores the frozen base and verifies the same Candidate bytes. The already approved scope expansion `s02-new-scope-001` covers the schema test.

## Frozen validation recipe

| Command | Exit | Result |
|---|---:|---|
| `dotnet test Monitoring.slnx --filter FullyQualifiedName~IngestionPersistenceTests` | 0 | 12 passed, 0 failed, 0 skipped; includes C-01 HTTP/PostgreSQL regression |
| `dotnet test Monitoring.slnx` | 0 | 54 passed, 0 failed, 0 skipped; includes inbox migration/schema test |

Both commands were executed once in this recheck. No new causal regression or unrelated late observation was found.

## C-01 result

| Frozen requirement | Evidence level | Result |
|---|---|---|
| `REQ-bandeja-ingestion-durable-002`: a reused event ID with distinct `occurredAt` JSON text returns 409 and preserves the first event | runtime-test | PASS |

`DifferentOccurredAtTextForSameInstantConflictsAndPreservesOriginalValue` sends `2026-09-24T12:30:00Z` and then `2026-09-24T12:30:00.000Z` for one event. It asserts HTTP 409 for the second request, one persisted row, and retention of the first raw text. `InboxWriter` compares the original string ordinally and stores it in `occurred_at_text`; `occurred_at` remains a timestamp for temporal queries. `InboxSchemaTests` confirms the new column and the retained timestamp type. The earlier 10 passing scenarios in the full discovery are unchanged by this targeted recheck; the full suite passes.

## Strict TDD evidence in scope

| Check | Result | Evidence |
|---|---|---|
| C-01 persistence regression RED | Confirmed from apply progress | Before the fix, the test expected 409 and received 200. |
| C-01 persistence regression GREEN | Confirmed now | Included in 12 passing persistence tests. |
| Schema test RED/GREEN | Confirmed from apply progress and current run | The insert initially failed on the new required column; schema test now passes in the 54-test suite. |
| Assertion quality | PASS | The C-01 test asserts HTTP status, row count and stored raw value; the schema test asserts column names/types and insertion behavior. |
| Changed-file coverage | Not available | `testing.coverage.available: false` in project config. |

Historical RED runner output is described in `apply-progress.md`; a current passing run does not recreate historical RED evidence.

## Findings and verdict

**CRITICAL:** None unresolved. Frozen C-01 is resolved.
**WARNING:** W-01 (`tasks-gap`) remains from the original discovery: task 3.4 recorded no observed RED because its new response-loss test passed on its first run. This process provenance warning does not indicate a failing runtime behavior and was not reopened or expanded during the targeted recheck. Follow-up: https://github.com/Snakeblack/monitorizacion-pasiva-red/issues/18; the historical RED will not be fabricated.
**SUGGESTION:** None added.

`evaluateRecheck` returned `action: close`, `status: closed`, `terminal_reason: all-findings-verified` and `verified_candidate_id: sha256:eba539f0167a64544598589d8d30a5800494fa5bae936ee6bc5769d00b377e0c`; the runtime/orchestrator persisted that state transition in state.yaml. **PASS WITH WARNINGS** follows from C-01 passing both frozen commands with W-01 retained.
