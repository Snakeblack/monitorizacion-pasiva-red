# Verification Report: s04-vista-angular

Verification outcome: **PASS WITH WARNINGS**.

Targeted recheck (Pipeline A) on the existing lineage. Not a new discovery. Strict TDD stays active; this pass did not re-run steps 3–10.

## Targeted recheck

| Field | Value |
|---|---|
| `verify_lineage.status` at start | `recheck-pending` |
| lineage_id | `sha256:97f355e647f19225370ad940df187f3bf237329afd3dff8178190c0bc23c759d` |
| generation | 1 |
| current_candidate_id | `sha256:505f241bdbc459f69bb70506377179ce8aa302cd8218c5a92939da24d606930a` |
| Snapshot recovery | `recoverCandidateSnapshot` with `verifyLiveWorkspace: true` returned `ok: true` |
| Router | `getLineageNextAction` → `run-targeted-recheck` / `active-recheck-pending` |

| Command | Exit | Counts | Wall |
|---|---:|---|---:|
| `dotnet test Monitoring.slnx --filter FullyQualifiedName~CiWorkflowPinsNodeAndRunsAngularTests` | 0 | Con error: 0, Superado: 1, Omitido: 0, Total: 1, Duración: 8 ms | 3094 ms |
| `dotnet test Monitoring.slnx --filter FullyQualifiedName~CiVerifyJobStopsBeforePublishWhenAngularViewCheckFails` | 0 | Con error: 0, Superado: 1, Omitido: 0, Total: 1, Duración: 1 s | 4229 ms |

The frozen file-text test still exits 0. Keeping that test was required. Its exit 0 is not the proof that closes `S04-C001`.

`CiVerifyJobStopsBeforePublishWhenAngularViewCheckFails` in `tests/Monitoring.Tests/VistaAngularDeliveryTests.cs` starts, from `src/monitoring-web`, `npx ng test --watch=false --include=src/app/this-file-does-not-exist.spec.ts`, then chains `&&` to a publish sentinel. The method asserts the runner output contains `No tests found matching`, `process.ExitCode != 0`, and that the sentinel file does not exist. The run was not skipped (`Omitido: 0`, `Superado: 1`). That is `runtime-test` for Integración continua: a non-zero view check was observed, and the next publish sentinel did not run.

`recheck_results.S04-C001`: PASS. `new_findings`: none. `evaluateRecheck` returned `action: close`, `reason: All frozen findings verified fixed`, `status: closed`, `terminal_reason: all-findings-verified`, `verified_candidate_id` equal to the current candidate. `S04-W001` and `S04-W002` stay advisory and were not added to the lineage.

The declared `tests` quality gate was not re-run. Pipeline A does not include Step 9a. The two commands above are the recheck evidence. `gates.quality-gates` was left as the prior `pass` audit.

The sections below are the prior full discovery. Rows updated in this recheck are marked. Unedited discovery text remains the record of that pass.

Full discovery on the persisted `standard` route. Strict TDD is active. No `verify_lineage` block existed at start. `getLineageNextAction(undefined)` returned `run-discovery` / `no-active-lineage`. This run did not reuse earlier pass counts.

## Completeness and recovery

| Check | Result | Evidence |
|---|---|---|
| Required standard artifacts | Complete | `proposal.md`, two change-local specs, `design.md`, `tasks.md`, `apply-progress.md` |
| Tasks | 30/30 complete | Tasks 1.1–5.5 are `[x]`; each has a TDD Cycle Evidence row |
| Assumption reconciliation | Left unresolved | Six `leave-unresolved` actions; every entry stays `unresolved` / `high` |
| Route | standard | `state.yaml` `route.actual_route` |

## Executed verification

Coverage, linter, and type-checker commands are empty in `openspec/config.yaml`, so those metrics were not run. The fixture-to-view spec completed inside the npm command, so Docker-backed PostgreSQL was usable for that spec.

| Command | Exit | Counts | Duration | Role |
|---|---:|---|---|---|
| `dotnet test Monitoring.slnx` | 0 | Con error: 0, Superado: 122, Omitido: 0, Total: 122 | test duration 19 s; wall 23099 ms; budget 180000 ms; `timedOut: false` | Exact `tests` gate |
| `npm --prefix src/monitoring-web test` | 0 | 4 files, 15 passed, 0 failed | Vitest 21.74 s; wall 39886 ms; budget 300000 ms; `timedOut: false` | Exact UI command, started through `cmd.exe` because the Windows shim is `npm.cmd` |

The Angular logger did not print individual names. The files on disk are `app.component.spec.ts` (2), `session-detail-api.spec.ts` (2), `session-detail-page.component.spec.ts` (8 `it` + 2 `it.each`), and `session-detail-view-chain.spec.ts` (1). That is 15 tests. The chain spec is in the passing set.

## Behavioral compliance matrix

Every row is a MUST scenario. `runtime-test` means this phase executed a test that drives the production path and observes the specified output.

| Requirement / scenario | Implementation | This run | Evidence | Verdict |
|---|---|---|---|---|
| vista-001 / Sesión encontrada | Success branch renders five `dt`/`dd` rows and «Sesión en ámbito» | Page success specs inside npm 15/15 | runtime-test | PASS |
| vista-002 / Identificador ausente | HTTP 404 maps to `kind: 'empty'` | Page 404 spec inside npm 15/15 | runtime-test | PASS |
| vista-002 / Identificador de otro ámbito | Page treats 404 as empty; host 404 omits the foreign body | Page spec in npm 15/15; `EventOutsideConfiguredScopeReturnsNotFoundWithoutForeignBody` inside dotnet 122/122 | runtime-test | PASS |
| vista-003 / Sin contexto o entorno no autorizado | Page error on 401; Production does not register the feature | Page 401 spec in npm 15/15; `ProductionLeavesFeatureUnsetAndReturnsUnauthorized` inside dotnet 122/122 | runtime-test | PASS |
| vista-003 / Fallo de red | Non-404, including status 0, is `kind: 'error'` | Page status-0 spec inside npm 15/15 | runtime-test | PASS |
| vista-004 / Petición sin ámbito | Client adds no query or scope headers; middleware reads configuration only | API spec in npm 15/15; `ClientHeadersAndQueryDoNotReplaceConfiguredScope` inside dotnet 122/122 | runtime-test | PASS |
| vista-004 / Control operable por teclado | Retry `<button type="button">` handles click, Enter, and Space | `it.each` Enter/Space inside npm 15/15 | runtime-test | PASS |
| vista-005 / Tres estados | Same page harness walks 200, 404, and 401 | Chain-of-states spec inside npm 15/15 | runtime-test | PASS |
| vista-005 / Fixture hasta la vista | `SessionViewChainHost` emits `VIEW_CHAIN_READY`; the page GETs that origin | Chain spec inside npm 15/15 | runtime-test | PASS |
| vista-005 / Integración continua | `.github/workflows/ci.yml` job `verify` runs `dotnet test` and `npm test` as ordinary `run` steps | `CiVerifyJobStopsBeforePublishWhenAngularViewCheckFails` exit 0; observes non-zero `npx ng test` and an absent publish sentinel | runtime-test | PASS |
| vista-005 / Sin publicación | `PublishedHostDoesNotIncludeWwwrootOrMonitoringWebArtifacts` runs `dotnet publish` and asserts the tree | Inside dotnet 122/122 | runtime-test | PASS |
| ambito-003 / Ámbito de servidor en Development o Testing | Middleware `Set` from `TrustedSessionRead:SiteId` / `SensorId` | `ConfiguredScopeReturnsFivePersistedFieldsWithoutClientScopeHeaders` inside dotnet 122/122 | runtime-test | PASS |
| ambito-003 / Petición que declara otro ámbito | Middleware does not read the request | `ClientHeadersAndQueryDoNotReplaceConfiguredScope` inside dotnet 122/122 | runtime-test | PASS |
| ambito-003 / Production ignora la configuración | Middleware not registered outside Development and Testing | `ProductionLeavesFeatureUnsetAndReturnsUnauthorized` inside dotnet 122/122 | runtime-test | PASS |
| ambito-003 / Development o Testing sin configuración | Blank keys do not `Set` the feature | `MissingConfigurationReturnsUnauthorizedWithoutReading` inside dotnet 122/122 | runtime-test | PASS |
| ambito-001 / Sesión encontrada | `SessionHostTests` returns the five persisted fields for the substituted scope | `DetailReturnsExactlyFiveFieldsAndSharedIdStaysInTrustedScope` inside dotnet 122/122 | runtime-test | PASS |
| ambito-001 / Sesión ausente o fuera de ámbito | Same theory: missing id and `other-only` return 404 | Same method, inside dotnet 122/122 | runtime-test | PASS |
| ambito-001 / Mismo ID en ámbitos distintos | Same theory returns three scope pairs | Same method, inside dotnet 122/122 | runtime-test | PASS |

**Compliance summary**: 18/18 scenarios satisfied at the required evidence level. Updated by the targeted recheck.

`CiWorkflowPinsNodeAndRunsAngularTests` still asserts Node 24.16.0, `npm test`, `dotnet test Monitoring.slnx --no-build`, and the absence of `--watch=false`. That file-text fact remains `static-lint` and still exits 0. The targeted recheck does not treat that exit as the fix. `CiVerifyJobStopsBeforePublishWhenAngularViewCheckFails` runs the CI view runner forced onto a missing spec and asserts a non-zero exit plus an absent publish sentinel. That observation is the `runtime-test` for Integración continua.

The publish scenario is covered by the real `dotnet publish` tree check, not by the companion file-text fact `CiWorkflowPublishesHostAndRejectsSpaTree`.

## Correctness (Static Evidence)

| Requirement | Status | Notes |
|---|---|---|
| Vista success, empty, error, retry, keyboard | Implemented and executed | Page and API specs passed inside npm 15/15 |
| Fixture to view | Implemented and executed | Chain spec is one of the four passing files |
| Client omits scope headers | Implemented and executed | API spec asserts no `X-Site-Id`, `X-Sensor-Id`, or query |
| Server scope ignores the request | Implemented and executed | `TrustedSessionReadScopeMiddleware` reads configuration only; the host theory passed |
| Production and missing config return 401 | Implemented and executed | Registration is gated in `Program.cs`; both facts passed |
| ambito-001 | Implemented and executed | `SessionHostTests` passed inside the full suite |
| CI fails the job | Present as ordinary `run` steps; pipeline failure not executed | File-text fact passed and does not meet the MUST bar |
| Publish excludes the SPA | Implemented and executed | Publish test passed; `UseStaticFiles` is absent |

## Coherence (Design)

| Decision | Followed? | Notes |
|---|---|---|
| Angular 22.2.0, npm, `@angular/build:unit-test`, SPA outside the host | Yes | This npm run used Vitest 5.0.3 via `ng test --watch=false`. `Program.cs` does not call `UseStaticFiles`. |
| Scope only from server configuration in Development or Testing | Yes | `Program.cs` registers the middleware only under `IsDevelopment()` or `IsEnvironment("Testing")`. |
| Both values present and not blank, or no feature | Yes | `IsNullOrWhiteSpace` on both ids. Internal spaces are not rejected. Unresolved assumption `sdd-apply-001`. |
| Dense ficha, low chrome, `DESIGN.md` tokens | Yes | Body 13px, title 18px, row 36px. Status copy is text. |
| Chain: fixture, empty ACK, worker, `VIEW_CHAIN_READY` | Yes | The passing chain spec consumed that line. |
| `computed()` view; only 404 is empty; retry repeats GET | Yes | `SessionDetailPage.view` is a `computed()`. `retryTick` repeats the GET. |
| `SESSION_DETAIL_API_ORIGIN` empty except in the chain | Yes | Factory returns `''`. The chain spec overrides it. |
| CI: Node 24.16.0, npm cache, `dotnet test`, `npm ci`, `npm test`, publish tree check, 15 min job | Yes | `ci.yml` matches the design. The evidence for a failing pipeline does not. |
| `CreateHost` still substitutes the read provider | Yes | `SessionHostTests` still drives the substituted provider and passed. |

Class name is CLI `App`, while design prose says `AppComponent`. The file and the shell behavior match the design. Not recorded as a finding.

## Assumption Reconciliation

The launch contained `assumption_resolutions` with `leave-unresolved` for all six ids. Statuses were not changed. No entry was confirmed. Every entry is `reversibility: high`, so none is escalated.

| id | statement | reversibility | outcome |
|---|---|---|---|
| sdd-propose-001 | Chain renders the real GET with `@angular/build:unit-test`, without an E2E tool | high | unresolved (no escalation). The passing chain spec matches it. |
| sdd-apply-001 | Both `TrustedSessionRead` values are absent when null or whitespace; internal spaces are not rejected | high | unresolved (no escalation). Spec does not require rejecting internal spaces. |
| sdd-apply-002 | Phase 2 page was an empty placeholder | high | unresolved (no escalation). The page now loads states. |
| sdd-apply-003 | `SESSION_DETAIL_API_ORIGIN` factory is empty unless the chain overrides it | high | unresolved (no escalation). |
| sdd-apply-004 | `SessionViewChainHost` sets content root by walking to `Monitoring.slnx` | high | unresolved (no escalation). |
| sdd-apply-005 | Chain spec uses local Node shims instead of `@types/node` | high | unresolved (no escalation). |

## Traceability Matrix

| REQ | Tasks | Commits | Tests | Status |
|---|---|---|---|---|
| REQ-vista-detalle-sesion-001 | 3.3, 3.4 | working tree, uncommitted | Page success specs inside npm 15/15 | OK |
| REQ-vista-detalle-sesion-002 | 3.5, 3.6 | working tree, uncommitted | Page 404 specs and host foreign-body fact | OK |
| REQ-vista-detalle-sesion-003 | 3.7, 3.8 | working tree, uncommitted | Page 401 and status 0; Production 401 fact | OK |
| REQ-vista-detalle-sesion-004 | 3.1, 3.2, 3.9, 3.10 | working tree, uncommitted | API spec, keyboard `it.each`, host scope theory | OK |
| REQ-vista-detalle-sesion-005 | 3.11, 3.12, 4.1–4.4, 5.1–5.4 | working tree, uncommitted | Three-state, chain, and publish tests passed. CI fail-fast observed by `CiVerifyJobStopsBeforePublishWhenAngularViewCheckFails` | OK |
| REQ-detalle-sesion-ambito-003 | 1.1–1.4 | working tree, uncommitted | `TrustedSessionReadScopeTests` inside dotnet 122/122 | OK |
| REQ-detalle-sesion-ambito-001 | 5.5 | working tree, uncommitted | `SessionHostTests` inside dotnet 122/122 | OK |

## TDD compliance

`apply-progress.md` has TDD Cycle Evidence tables for tasks 1.1–5.5 and one `json:strict-tdd-evidence` block. `validateEvidenceRecord` from `C:\Users\sn4ke\.cursor\scripts\lib\strict-tdd-evidence-remediation.js`, with this repository as `rootDir`, returned `valid: false`. First reason: `snapshot-digest-mismatch`. 136 errors. Distinct codes: `snapshot-digest-mismatch` (7), `evidence-mode-cycle-mismatch` (30), `cycle-red-enum-invalid` (18), `cycle-green-enum-invalid` (22), `cycle-triangulate-enum-invalid` (14), `cycle-triangulate-missing` (2), `cycle-refactor-enum-invalid` (24), `cycle-test_file-missing` (3), `provenance-missing-or-mismatch` (12), `unsafe-evidence-path` (3), `runtime-receipt-unverified` (1).

Of the seven pinned functional-snapshot files, six digests equal the raw SHA-256 of the current bytes. `tests/Monitoring.Tests/VistaAngularDeliveryTests.cs` matches neither the raw digest nor the LF-normalized digest. That file changed after the pin. The validator hashes with LF normalization, so the six raw pins also fail its digest check. `evidence_mode` is `live` while provenance `source` is `working-tree`. No runtime receipts exist. Historical RED was not re-observed and is not backfilled. This run's passing suites confirm current GREEN behavior; they do not authenticate the historical record.

| Check | Result | Details |
|---|---|---|
| TDD Evidence reported | PASS | Table present for 30/30 tasks. Machine record is not a valid live receipt. |
| All tasks have tests | PASS | Coding tasks name existing test files. Scaffold rows 2.1–2.5 are `N/A` / ➖. |
| RED confirmed (tests exist) | PASS | Named behavioral test files exist. Historical RED failure was not re-run. |
| GREEN confirmed (tests pass) | PASS | npm 15/15 and `dotnet test Monitoring.slnx` 122/122 on this run. |
| Triangulation adequate | PASS | Success, empty, error, keyboard, and scope theories use different values. The fixture-to-view spec is one scenario and one test. |
| Safety Net for modified files | PASS | `Program.cs` records `SessionHostTests` before the edit. New files are marked `N/A (new)`. |

**TDD Compliance**: 6/6 checks passed.

### Test layer distribution

Changed or new test files for this change. `it.each` counts as two tests. `VistaAngularDeliveryTests.cs` contributes to both unit and integration. Counts are from the source. The full dotnet assembly and the npm suite both passed.

| Layer | Tests | Files | Tools |
|---|---:|---:|---|
| Unit | 6 | 2 | xUnit file assertions (4); Angular `HttpTestingController` on `SessionDetailApi` (2) |
| Integration | 22 | 5 | TestBed / `RouterTestingHarness` (app 2, page 10); real GET chain (1); `WebApplicationFactory` + PostgreSQL (8); `dotnet publish` subprocess (1) |
| E2E | 0 | 0 | Not available (`testing.e2e.available: false`). No Playwright claim. |
| **Total** | **28** | **6** | |

`testing` capabilities list xUnit, Testcontainers PostgreSQL, and Docker. The Angular runner is `@angular/build:unit-test` (Vitest 5.0.3 in this npm run). No E2E tool was used.

### Changed file coverage

Coverage analysis skipped — no coverage tool detected. The normalized `tests` gate declares no `coverage.minimum`.

### Assertion quality

| File | Line | Assertion | Issue | Severity |
|---|---:|---|---|---|
| `src/monitoring-web/src/app/session-detail/session-detail-view-chain.spec.ts` | 131 | `expect(shown).toContain(eventId)` | Harness `eventId` is `event`, already contained in the `eventId` label | WARNING |
| `src/monitoring-web/src/app/session-detail/session-detail-view-chain.spec.ts` | 132 | `expect(shown).toContain('site')` | Satisfied by the `siteId` label without proving the value `site` | WARNING |
| `src/monitoring-web/src/app/session-detail/session-detail-view-chain.spec.ts` | 133 | `expect(shown).toContain('sensor')` | Satisfied by the `sensorId` label without proving the value `sensor` | WARNING |
| `src/monitoring-web/src/app/session-detail/session-detail-page.component.spec.ts` | 125 | `expect(shown).not.toContain('site-b')` | 404 body flushed by that spec is empty, so the token cannot come from the response | WARNING |

The chain spec still calls production code: it renders `SessionDetailPage` against the real GET and asserts «Sesión en ámbito», the persisted `occurredAt`, `synthetic-session`, `192.0.2.1`, `TCP`, `65535`, and five `dt` rows. Those assertions ran inside the passing npm suite. Page specs assert distinctive values (`site-a`, `sensor-a`, `shared`, `other-event`) that are not implied by the labels. No tautology, zero-assertion test, unguarded ghost loop, or test that skips the production path was found. `retryButton` fails if the button is missing. `dt` length uses `toHaveLength`, not a loop.

`VistaAngularDeliveryTests` file-content facts assert concrete workflow, config, and README strings. They ran inside dotnet 122/122. They are `static-lint`. They are not tautologies. The publish fact runs `dotnet publish` and checks the output tree. The later `CiVerifyJobStopsBeforePublishWhenAngularViewCheckFails` fact is not file text: the targeted recheck executed it and it observes a failing `ng test` and an absent sentinel.

**Assertion quality**: 0 CRITICAL, 4 WARNING

### Quality metrics

**Linter**: Not available (`quality_tools.linter.available: false`).
**Type checker**: Not available (`quality_tools.type_checker.available: false`).
The Angular test build completed (`Application bundle generation complete`) and the suite exited 0. The .NET projects compiled inside `dotnet test Monitoring.slnx`, which then reported 122 passed.

## Quality Gates

`parseQualityGates` and `validateQualityGates` from `C:\Users\sn4ke\.cursor\scripts\lib\quality-gates.js`. Validation: valid, no errors. `KNOWN_GATES` is `tests`, `lint`, `architecture`, `security`. The declared `ui` gate is dropped. Normalized policy contains only `tests`.

The `ui` command was still executed. It is not part of `aggregateStatus` or `buildAuditBlock`.

| gate | status | required | on_fail | detail |
|---|---|---|---|---|
| tests | pass | true | halt | `dotnet test Monitoring.slnx` exit 0; 122 passed, 0 failed, 0 skipped; wall 23099 ms |
| ui | not in normalized policy | true in raw config | halt in raw config | Executed separately: `npm --prefix src/monitoring-web test` exit 0; 4 files; 15 passed; 0 failed; Vitest 21.74 s; wall 39886 ms |

`classifyGate('tests', policy.tests, { exitCode: 0, timedOut: false })` returned `{ status: 'pass' }`. `enforceGate` returned `{ finding: null, blocksArchive: false }`. `aggregateStatus` returned `pass`.

Audit block from `buildAuditBlock`, evaluated at `2026-09-30T19:15:23.201Z`:

```yaml
gates:
  quality-gates:
    status: pass
    evaluated_at: "2026-09-30T19:15:23.201Z"
    gates:
      tests:
        status: pass
        required: true
        on_fail: halt
```

Read-back of `state.yaml` confirmed `gates.quality-gates.status` is `pass` and `gates.quality-gates.gates.tests.status` is `pass`. `gates.quality-review-gate` was not edited. Approvals and assumptions were kept.

The targeted recheck did not re-execute `dotnet test Monitoring.slnx` and did not rewrite `gates.quality-gates`. Pipeline A stops before Step 9a. The frozen commands in `## Targeted recheck` are the recheck evidence.

## Verify lineage

Discovery opened this lineage with `startVerifyLineageFromWorkspace` at `status: remediation-pending`, `current_candidate_id` `sha256:6914dcde1c890d315b9c72848b34f08ff2105f0f8fe280919f82cc18ebe183b2`, and `remediation_attempts` 0. The frozen command was `dotnet test Monitoring.slnx --filter FullyQualifiedName~CiWorkflowPinsNodeAndRunsAngularTests` with `expected_exit: 0`. That command exits 0 because the test only reads `ci.yml`. The remediation kept that test and added the fail-fast observation.

Targeted recheck result, merged into `state.yaml` `verify_lineage`:

| Field | Value |
|---|---|
| lineage_id | `sha256:97f355e647f19225370ad940df187f3bf237329afd3dff8178190c0bc23c759d` |
| status | `closed` |
| current_candidate_id | `sha256:505f241bdbc459f69bb70506377179ce8aa302cd8218c5a92939da24d606930a` |
| verified_candidate_id | `sha256:505f241bdbc459f69bb70506377179ce8aa302cd8218c5a92939da24d606930a` |
| contract_digest | `sha256:3c62f8a9a03e82349ff99822137de7a8b21e3a0e53654ea30eca22df95efd0ec` |
| remediation_attempts | 1 of 2 |
| terminal_reason | `all-findings-verified` |
| finding | `S04-C001` resolved |

## Findings

### CRITICAL

None open.

#### S04-C001 — resolved — CI failure scenario is file text, not a failing pipeline

- Severity: CRITICAL, now resolved.
- Origin: `design-gap`.
- Area: `REQ-vista-detalle-sesion-005` / Integración continua; `tests/Monitoring.Tests/VistaAngularDeliveryTests.cs`.
- Discovery evidence: `CiWorkflowPinsNodeAndRunsAngularTests` only reads `ci.yml`. That test still exits 0 and was kept.
- Recheck evidence: `CiVerifyJobStopsBeforePublishWhenAngularViewCheckFails` exited 0, was not skipped, and asserts a non-zero `npx ng test --watch=false --include=src/app/this-file-does-not-exist.spec.ts` plus an absent publish sentinel.
- Status: `resolved` by `evaluateRecheck` (`recheck_results.S04-C001: PASS`).

### WARNING

#### S04-W001 — Chain spec does not distinguish site and sensor values from labels

- Severity: WARNING.
- Origin: `code-bug` (test assertion, not the page binding).
- Area: `session-detail-view-chain.spec.ts` lines 131–133; companion check at `session-detail-page.component.spec.ts` line 125.
- Evidence: harness ids are `event`, `site`, and `sensor`. The template always renders `eventId`, `siteId`, and `sensorId`. `toContain` on those short strings passes even if the values were wrong. `occurredAt` and `data` tokens are asserted for real, and that spec passed in this npm run. The host foreign-body fact passed in the dotnet suite.
- Workaround: none required to explain the npm pass. A stricter assertion would expect the values beside the labels.

#### S04-W002 — Live Strict TDD record is not runtime-authenticated

- Severity: WARNING.
- Origin: `tasks-gap` (apply evidence shape).
- Area: `openspec/changes/s04-vista-angular/apply-progress.md` `json:strict-tdd-evidence`.
- Evidence: `validateEvidenceRecord` invalid, 136 errors, codes listed above. Six pins match raw SHA-256. `VistaAngularDeliveryTests.cs` matches neither raw nor LF. No runtime receipts. Current suites passed and are not a backfill of historical RED.
- Workaround: retain the record; do not fabricate receipts.

### SUGGESTION

- `tasks.md` still says `Decision needed before apply: Yes` and `Chain strategy: pending`, while approval `s04-size-exception-001` already accepted `size:exception`.
- Task 3.12 text still shows `npm test -- --watch=false`. This run used `npm --prefix src/monitoring-web test`, which exited 0.

## Operative-memory handoff

This targeted recheck did not run Step 10b and did not edit `openspec/memory/known-issues.md`. The existing CI, chain-assertion, and Strict TDD entries for this change stay as written. `S04-C001` is resolved in `verify_lineage`; the memory file still carries the older CI heading at `WARNING`.

## Next action

`verify_lineage.status` is `closed`. `S04-C001` is resolved. `S04-W001` and `S04-W002` stay advisory. Archive waits until those warnings are accepted or turned into follow-up work. This recheck did not archive and did not change application code. The prior `tests` gate remains `pass`; it was not re-run.

## Verdict

PASS WITH WARNINGS

18/18 MUST scenarios meet the required evidence level. Integración continua is `runtime-test` via `CiVerifyJobStopsBeforePublishWhenAngularViewCheckFails`. `S04-W001` and `S04-W002` remain advisory, so the outcome is not a clean PASS.
