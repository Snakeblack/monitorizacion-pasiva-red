# Archive Report: S01 — base ejecutable

**Change**: `s01-base-ejecutable`  
**Route**: standard fallback (no routing table; no `route.actual_route` state)  
**Archive date (planned)**: 2026-09-24  
**Planned destination**: `openspec/changes/archive/2026-09-24-s01-base-ejecutable/`  
**Verify verdict**: PASS (no CRITICAL or WARNING findings)  
**Candidate**: `f88702ca911a9006f6e26ff2ab6d0b5da4312ff4`

## Close Gate

- Verification report records PASS for all 11 scenarios, with no CRITICAL, WARNING, or SUGGESTION findings.
- All 13 tasks are complete.
- The required tests quality gate is PASS in both `verify-report.md` and `state.yaml`; clean hosted CI passed for the candidate.
- The `net10.0` assumption is confirmed and its resolution is persisted in `state.yaml`.
- Accepted warnings: none.

## Summary

S01 delivers a modular .NET 10 host, an infrastructure-independent domain project, an explicit repeatable initial PostgreSQL migration, disposable PostgreSQL integration tests, and clean-checkout CI. PostgreSQL remains provisional per ADR-014; no production capacity claim is made. The two new capability specs are prepared as full specs because no live baseline specs exist.

## Specs Prepared (change-local)

| Domain | Action | Details |
|--------|--------|---------|
| base-ejecutable | ADD | New capability; full prepared spec with 3 requirements and 6 scenarios. |
| verificacion-base | ADD | New capability; full prepared spec with 3 requirements and 5 scenarios. |

Prepared content is in `specs/<domain>/spec-prepared.md`; runtime-owned writes are pending for `openspec/specs/{base-ejecutable,verificacion-base}/spec.md` with an absent-target precondition.

## ADR Promotions (proposed; runtime-owned)

| Source | Target |
|--------|--------|
| `decisions/adr-001.md` | `docs/adr/adr-20260924-001-frontera-de-dominio-y-migracion-postgresql-inicial.md` |
| `decisions/adr-002.md` | `docs/adr/adr-20260924-002-verificacion-con-postgresql-desechable-en-pruebas-net.md` |

Both decisions remain consistent with the verified implementation. Their change-local copies remain in the audit trail; live ADR writes are pending the archive transaction runtime.

## Verification Evidence Summary

- `dotnet test Monitoring.slnx --no-build`: 4/4 passed locally, including disposable PostgreSQL migration tests.
- GitHub Actions PR and push runs passed for the verified candidate, including checkout, SDK setup, restore, build, and Docker-backed tests.
- Liveness returned HTTP 200; all six requirements and 11 scenarios passed the compliance review.
- Strict TDD evidence covers all 8 coding tasks; the required quality gate is recorded as pass.

## Cost

Estimated token figures below are heuristic estimates from `.ospec/session/s01-base-ejecutable/phase-costs.jsonl` (~4 bytes/token), not exact metering. Duration was unavailable in the records and is shown as 0 ms.

| Phase | Invocations | Re-launches | Duration | Model Tiers | Statuses | Estimated Prompt Tokens | Estimated Artifact Tokens | Estimated Tool Output Tokens | Estimated Output Tokens |
|-------|-------------|-------------|----------|-------------|----------|-------------------------|---------------------------|------------------------------|-------------------------|
| explore | 1 | 0 | 0ms | unknown | unknown | 58076 (estimated) | 0 (estimated) | 0 (estimated) | 109 (estimated) |
| propose | 1 | 0 | 0ms | unknown | success | 59361 (estimated) | 0 (estimated) | 0 (estimated) | 32 (estimated) |
| spec | 1 | 0 | 0ms | unknown | blocked | 60262 (estimated) | 0 (estimated) | 0 (estimated) | 21 (estimated) |
| design | 1 | 0 | 0ms | unknown | success | 61762 (estimated) | 0 (estimated) | 0 (estimated) | 21 (estimated) |
| tasks | 1 | 0 | 0ms | unknown | success | 64139 (estimated) | 0 (estimated) | 0 (estimated) | 21 (estimated) |
| apply | 5 | 4 | 0ms | unknown | partial, success | 425186 (estimated) | 0 (estimated) | 0 (estimated) | 311 (estimated) |
| verify | 4 | 3 | 0ms | unknown | blocked, success | 438799 (estimated) | 0 (estimated) | 0 (estimated) | 338 (estimated) |

**Total user questions asked**: 0 (no `gates.*.questions_asked` fields are recorded in `state.yaml`)

## Archive Inventory

The plan inventories the complete active change folder as of plan emission, excluding `archive-plan.json` from the self-referential fingerprint. It includes the proposal, both original and prepared specs, design, tasks, apply and verify evidence, state, ADR decisions, exploration, and this report. The plan itself is copied by the runtime.

## Runtime-Owned Completion

`archive-report.md` and `archive-plan.json` are prepared in the active change folder. Live spec/ADR writes and the move to the dated archive destination remain pending the deterministic archive transaction runtime; this report does not claim that the archive is complete.
