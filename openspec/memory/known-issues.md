---
title: Known Issues
last_updated: 2026-09-30
---

## tests gate timed out before any result
- severity: resolved
- area: quality_gates.tests; dotnet test Monitoring.slnx; MUST scenarios whose only proof is that suite
- workaround: the publish fact now reads both process streams concurrently; the suite completed 122 passed
- change: s04-vista-angular
- date: 2026-09-30

## Chain spec does not distinguish site and sensor values from labels
- severity: WARNING
- area: src/monitoring-web/src/app/session-detail/session-detail-view-chain.spec.ts
- workaround: none required to explain the npm pass; assert values that are not substrings of the field labels
- change: s04-vista-angular
- date: 2026-09-30
- follow-up: openspec/changes/s04-vista-angular/follow-up.md
- disposition: converted-to-follow-up

## Live Strict TDD record is not runtime-authenticated
- severity: WARNING
- area: openspec/changes/s04-vista-angular/apply-progress.md json:strict-tdd-evidence
- workaround: retain the record; do not fabricate runtime receipts
- change: s04-vista-angular
- date: 2026-09-30
- follow-up: openspec/changes/s04-vista-angular/follow-up.md
- disposition: converted-to-follow-up

## CI failure scenario is file text, not a failing pipeline
- severity: resolved
- area: REQ-vista-detalle-sesion-005 Integración continua; .github/workflows/ci.yml
- workaround: CiVerifyJobStopsBeforePublishWhenAngularViewCheckFails observes the failing view runner and the absent publish sentinel
- change: s04-vista-angular
- date: 2026-09-30

## Historical Strict TDD evidence is not runtime-authenticated
- severity: WARNING
- area: S03 Strict TDD provenance; openspec/changes/archive/2026-09-30-s03-proyeccion-idempotente/apply-progress.md and evidence/
- workaround: retain honest historical evidence; capture runtime-authenticated RED/GREEN receipts and pre-write snapshots for future slices; never reconstruct missing history
- change: s03-proyeccion-idempotente
- date: 2026-09-30
