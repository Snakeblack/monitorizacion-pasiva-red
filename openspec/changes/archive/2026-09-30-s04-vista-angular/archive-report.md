# Archive plan: S04 — Vista Angular

Planned date: 2026-09-30 (Europe/Madrid). Intended destination: `openspec/changes/archive/2026-09-30-s04-vista-angular/`.

## Outcome and review focus

S04 delivers an internal Angular SPA for synthetic session detail (success, empty, error), a Development/Testing server scope bridge from configuration, fixture-to-view chain tests, and CI gates without publishing the SPA in the host artifact. S03 detail HTTP contract remains; OIDC/RBAC (S12), real capture (S05), and 30-day listing (S09) stay out of scope.

This report prepares closure. Live writes to `openspec/specs/**` and `docs/adr/**`, the archive move, and the final receipt belong to the archive transaction runtime. The active change folder still exists at `openspec/changes/s04-vista-angular/`.

## Verification and gates

- Standard route: proposal, two change-local specs, design, apply progress, and 30/30 tasks complete.
- Verdict: **PASS WITH WARNINGS**; 18/18 MUST scenarios satisfied; no open CRITICAL or BLOCKER in verify findings.
- `verify_lineage.status`: **closed**; `S04-C001` resolved via runtime observation in `CiVerifyJobStopsBeforePublishWhenAngularViewCheckFails`.
- Quality gates: `tests` **pass** (required, on_fail halt); audit block unchanged from full verify.
- Quality review (successor lineage, generation 2): **done**; `terminal_reason`: **all-remediation-slices-passed**; archive identity candidate **`sha256:e9aa1d75b5bcf7174f3dc2b360735d0384c76bde970087e31692f109e81806e1`** (matches `archive-plan.json` `source_fingerprint`). Trust, runtime, and evolution lenses completed; blocking runtime finding remediated and validated.
- Delivery: single PR with approved `size:exception`. Six apply assumptions remain explicitly unresolved (high reversibility).

## Warnings accepted as follow-up

Per user decision on 2026-09-30 (chat), both advisory verify warnings are converted to scheduled follow-up work in [follow-up.md](follow-up.md). Recorded in `archive-plan.json` `accepted_warnings` with disposition `converted-to-follow-up`.

| ID | Summary | Disposition |
|---|---|---|
| S04-W001 | Chain spec `toContain` on labels may pass without proving site/sensor values | converted-to-follow-up → follow-up.md |
| S04-W002 | Strict TDD machine record invalid; no runtime receipts for historical RED | converted-to-follow-up → follow-up.md |

This satisfies PASS WITH WARNINGS archive closure; no further warning acceptance is required.

## Prepared specifications

| Domain | Action | Requirements |
|---|---|---|
| vista-detalle-sesion | New baseline | 5 requirements (detail, empty, error, HTTP client, chain/CI) |
| detalle-sesion-ambito | Merge delta into live baseline | REQ-001 modified; REQ-002 preserved; REQ-003 added |

Prepared bytes live under `archive-prepared/specs/`. Genesis deltas under `specs/` are unchanged. The plan records `content_sha256`, `target_before_sha256` for `detalle-sesion-ambito` (matches `state.yaml` `baseline_fingerprints.detalle-sesion-ambito`), and `null` for the new vista domain.

## Proposed ADR promotions

Runtime applies `docs/adr/` (existing S01–S03 promotions). Change-local ADRs remain in the archived folder as audit trail.

- [decisions/adr-001.md](decisions/adr-001.md) → [docs/adr/adr-20260930-004-spa-angular-fuera-del-host.md](../../../docs/adr/adr-20260930-004-spa-angular-fuera-del-host.md)
- [decisions/adr-002.md](decisions/adr-002.md) → [docs/adr/adr-20260930-005-ambito-lectura-solo-desde-configuracion-servidor.md](../../../docs/adr/adr-20260930-005-ambito-lectura-solo-desde-configuracion-servidor.md)

## Inventory and rollback

`archive_inventory` lists every origin artifact to preserve except `archive-plan.json` (excluded from identity hash to avoid self-reference; runtime still copies it). Includes verify-lineage recovery JSON, follow-up, prepared specs, decisions, and phase artifacts.

Rollback strategy: **staging-rename** (runtime-owned). Functional rollback removes the SPA tree, scope middleware, and CI steps; no fabricated TDD receipts.

Move completion is **not** claimed here. Orchestrator invokes `node scripts/archive-transaction-run.js s04-vista-angular` and treats the runtime success receipt as sole close authority.

## Cost

No per-phase cost data was recorded for this change
(`.ospec/session/s04-vista-angular/phase-costs.jsonl` missing or empty).

**Total user questions asked**: 0 (`gates.*.questions_asked` absent in `state.yaml` → contractual 0).
