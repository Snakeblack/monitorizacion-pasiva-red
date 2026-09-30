# Apply progress — s04-vista-angular

Strict TDD. Delivery `single-pr` with maintainer-accepted `size:exception` (approval `s04-size-exception-001` in `state.yaml`). This batch is only the host read-scope slice (Phase 1, tasks 1.1–1.4), not the whole change. No commits or branch changes.

## Batch 1 — Host trusted read scope from server configuration

### Completed tasks

- [x] 1.1 RED: `TrustedSessionReadScopeTests` — Theory Development/Testing 200 with five fields, request without `X-Site-Id`/`X-Sensor-Id`, headers/query ignored, Production 401 with null feature after `next`, Dev/Testing without keys 401, foreign-scope 404 without alien body.
- [x] 1.2 GREEN: `TrustedSessionReadScope.cs` options + middleware `Set` of `ITrustedSessionReadContextFeature` from `TrustedSessionRead:SiteId`/`SensorId`; does not read the HTTP request.
- [x] 1.3 GREEN: middleware registered in `Program.cs` only when the host environment is Development or Testing.
- [x] 1.4 VERIFY: `dotnet test Monitoring.slnx --filter FullyQualifiedName~TrustedSessionReadScopeTests` — 8 passed, 0 failed.

### TDD Cycle Evidence

| Task | Test File | Layer | Safety Net | RED | GREEN | TRIANGULATE | REFACTOR | Notes / Rationale |
|------|-----------|-------|------------|-----|-------|-------------|----------|-------------------|
| 1.1 | `tests/Monitoring.Tests/TrustedSessionReadScopeTests.cs` | Integration | ✅ SessionHostTests 8/8 before host edits | ✅ Written; 5 failed (401 vs 200/404), 3 passed (existing 401 paths) | ➖ RED-only task | ✅ Dev/Testing, headers/query, Production, missing config, foreign 404 | ➖ None needed | First URI-query assertion threw on a relative URI; fixed in the test before production code so RED became HTTP 401 vs 200/404. |
| 1.2 | `tests/Monitoring.Tests/TrustedSessionReadScopeTests.cs` | Integration | N/A (new host file) | ✅ Written (1.1) | ✅ Passed with 1.3 | ✅ Same eight cases force config-backed feature, not Fake It | ➖ None needed | Middleware binds `TrustedSessionRead` and `Set`s the existing feature; never reads headers/query/body. |
| 1.3 | `tests/Monitoring.Tests/TrustedSessionReadScopeTests.cs` | Integration | ✅ SessionHostTests 8/8 before `Program.cs` | ✅ Written (1.1) | ✅ Passed with 1.2 | ✅ Production probe sees feature null after `next`; Dev/Testing without keys stay 401 | ➖ None needed | Registration gated with `IsDevelopment()` / `IsEnvironment("Testing")`. |
| 1.4 | `tests/Monitoring.Tests/TrustedSessionReadScopeTests.cs` | Integration | ✅ HostStartupTests + DomainDependencyTests 2/2 after host edits | ✅ Written (1.1) | ✅ `dotnet test Monitoring.slnx --filter FullyQualifiedName~TrustedSessionReadScopeTests` exit 0; 8 passed | ➖ Verify task | ➖ None needed | Same command as GREEN; no extra production edit. |

### Test Summary

- **Total tests written**: 8 (`TrustedSessionReadScopeTests`)
- **Total tests passing**: 8
- **Layers used**: Unit (0), Integration (8), E2E (0)
- **Approval tests** (refactoring): None — no refactoring tasks
- **Pure functions created**: 0 (host adapter middleware)

### Files Changed

| File | Action | What Was Done |
|------|--------|---------------|
| `tests/Monitoring.Tests/TrustedSessionReadScopeTests.cs` | Created | Real-provider host tests for config scope, ignored client headers/query, Production 401 + null feature, missing config 401, foreign 404 |
| `src/Monitoring.Host/Sessions/TrustedSessionReadScope.cs` | Created | Options + middleware that sets the trusted read feature from configuration only |
| `src/Monitoring.Host/Program.cs` | Modified | `UseMiddleware<TrustedSessionReadScopeMiddleware>()` only in Development and Testing |
| `openspec/changes/s04-vista-angular/tasks.md` | Modified | Marked 1.1–1.4 `[x]` |

### Verification

- Safety net: `dotnet test Monitoring.slnx --filter FullyQualifiedName~SessionHostTests` — exit 0, 8 passed (before `Program.cs` edit).
- RED: `dotnet test Monitoring.slnx --filter FullyQualifiedName~TrustedSessionReadScopeTests --no-restore` — exit 1, 5 failed (401 vs OK/NotFound), 3 passed (Production and missing-config 401).
- GREEN / 1.4: `dotnet test Monitoring.slnx --filter FullyQualifiedName~TrustedSessionReadScopeTests` — exit 0, 8 passed, 0 failed, ~5 s.
- Post-change: `dotnet test Monitoring.slnx --filter "FullyQualifiedName~HostStartupTests|FullyQualifiedName~DomainDependencyTests"` — exit 0, 2 passed.

### Deviations from Design

None — implementation matches design. `CreateHost` in `SessionHostTests` still substitutes `ITrustedSessionReadContextProvider`. Domain and `SessionEndpoint` were not changed. `TrustedSessionRead__SiteId` / `TrustedSessionRead__SensorId` are IConfiguration's existing environment mapping of the colon keys.

### Issues Found

None.

### Remaining Tasks

Phase 2–5 (tasks 2.1–5.5): SPA scaffold, session-detail UI TDD, fixture→view chain, CI/config/non-publish.

### Workload / PR Boundary

- Mode: `size:exception` (approval `s04-size-exception-001`); delivery strategy `single-pr`
- Current work unit: host read-scope slice (Phase 1)
- Boundary: starts at `TrustedSessionReadScopeTests` RED; ends when that filter is green. Does not include `src/monitoring-web/` or later phases.
- Estimated review budget impact: this slice is well under 400 lines; the accepted exception covers the whole change forecast (1100–1700), not this batch alone.

### Status

4/30 tasks complete. Ready for next apply batch (Phase 2).

## Batch 2 — SPA scaffold (Phase 2, tasks 2.1–2.5)

Strict TDD remains active. This batch is scaffolding only: no behavior RED/GREEN cycle for `ng new` or design tokens. Behavior tests start in Phase 3. Delivery `single-pr` with maintainer-accepted `size:exception` (approval `s04-size-exception-001`). No commits or branch changes; stayed on `feature/diagramas_generales`.

### Completed tasks

- [x] 2.1 `src/monitoring-web/` created with `npx --yes @angular/cli@22.2.0` (standalone, routing, skip-git, npm, style css, no SSR, vitest). Proxy `/api` → `http://127.0.0.1:5080`. Runner `@angular/build:unit-test` in `angular.json`.
- [x] 2.2 Root `.gitignore` ignores `src/monitoring-web/node_modules/`, `dist/`, `.angular/`. `package-lock.json` exists and is not ignored. Commit of the lockfile is deferred (apply batch must not commit).
- [x] 2.3 Root `DESIGN.md` seeded from `linear-attio-ui` `assets/DESIGN.template.md`, adapted to hybrid session-detail chrome (no inspector, no proprietary assets).
- [x] 2.4 Shell: compact header, collapsible nav toggle (`<button type="button">`), nav item «Detalle» as a native button, `router-outlet`. Route `/sessions/:eventId` → empty `SessionDetailPage` placeholder (states are Phase 3).
- [x] 2.5 Semantic type and neutral tokens in `src/monitoring-web/src/styles.css` matching `DESIGN.md`.

### TDD Cycle Evidence

| Task | Test File | Layer | Safety Net | RED | GREEN | TRIANGULATE | REFACTOR | Notes / Rationale |
|------|-----------|-------|------------|-----|-------|-------------|----------|-------------------|
| 2.1 | N/A | Scaffold | N/A (new SPA) | ➖ Scaffold / no behavior yet | ✅ `npx ng build` exit 0 | ➖ N/A | ➖ None needed | CLI scaffold is not a behavior cycle. Do not invent a fake RED for `ng new`. |
| 2.2 | N/A | Scaffold | N/A | ➖ Scaffold / no behavior yet | ✅ `git check-ignore` lockfile exit 1; node_modules ignored | ➖ N/A | ➖ None needed | Lockfile versionable; commit out of scope for this apply batch. |
| 2.3 | N/A | Scaffold | N/A | ➖ Scaffold / no behavior yet | ✅ `DESIGN.md` present, hybrid, no brand assets | ➖ N/A | ➖ None needed | Design-language seed; no runtime behavior. |
| 2.4 | `src/monitoring-web/src/app/app.component.spec.ts` | Scaffold smoke | ✅ CLI spec 2/2 before shell replace | ➖ Scaffold / no behavior yet | ✅ `npm test` 2 passed (toggle + Detalle are `<button type="button">`) | ➖ N/A | ➖ None needed | Smoke asserts shell controls, not session-detail states. Phase 3 owns behavior TDD. |
| 2.5 | `src/monitoring-web/src/app/app.component.spec.ts` | Scaffold | N/A (new tokens) | ➖ Scaffold / no behavior yet | ✅ `npx ng build` includes `styles-*.css` 2.04 kB; `npm test` exit 0 | ➖ N/A | ➖ None needed | Tokens are CSS variables; no behavior assertions on class names. |

### Test Summary

- **Total tests written this batch**: 2 scaffold smokes in `app.component.spec.ts` (CLI welcome spec replaced; not Phase 3 behavior tests)
- **Total tests passing this batch**: 2
- **Layers used**: Scaffold smoke (2). Behavior unit tests: 0 (start Phase 3)
- **Approval tests** (refactoring): None
- **Pure functions created**: 1 (`navToggleLabel` via `computed()`)

### Files Changed

| File | Action | What Was Done |
|------|--------|---------------|
| `src/monitoring-web/` | Created | Angular 22.2.0 workspace (standalone, routing, vitest, npm lockfile) |
| `src/monitoring-web/proxy.conf.json` | Created | `/api` → `http://127.0.0.1:5080` |
| `src/monitoring-web/angular.json` | Modified | `proxyConfig`; test builder remains `@angular/build:unit-test` |
| `src/monitoring-web/package.json` | Modified | `npm test` → `ng test --watch=false`; `allowScripts` for esbuild/@parcel/watcher (npm 12) |
| `.gitignore` | Modified | SPA `node_modules/`, `dist/`, `.angular/`; lockfile not ignored |
| `DESIGN.md` | Created | Hybrid tokens and shell contract from linear-attio template |
| `src/monitoring-web/src/app/app.component.ts` | Modified | Compact header, collapsible nav, `computed()` label |
| `src/monitoring-web/src/app/app.component.html` | Modified | Replaced CLI marketing welcome; native buttons + `router-outlet` |
| `src/monitoring-web/src/app/app.component.css` | Modified | Quiet chrome using semantic tokens |
| `src/monitoring-web/src/app/app.component.spec.ts` | Modified | Scaffold smoke for toggle and «Detalle» buttons |
| `src/monitoring-web/src/app/app.routes.ts` | Modified | `/sessions/:eventId` → `SessionDetailPage` |
| `src/monitoring-web/src/app/session-detail/session-detail-page.component.ts` | Created | Empty OnPush placeholder (no GET/states) |
| `src/monitoring-web/src/styles.css` | Modified | Semantic type/neutral tokens |
| `src/monitoring-web/src/index.html` | Modified | `lang="es"`, title «Monitorización» |
| `openspec/changes/s04-vista-angular/tasks.md` | Modified | Marked 2.1–2.5 `[x]` |

### Verification

- CLI generate: `npx --yes @angular/cli@22.2.0 new monitoring-web --directory=src/monitoring-web --routing --style=css --ssr=false --skip-git --package-manager=npm --standalone --defaults --interactive=false --file-name-style-guide=2016 --test-runner=vitest --ai-config=none --skip-tests=false`. Schematic files written; nested `npm install --allow-scripts` failed (npm 12 project-scoped). Follow-up `npm install` in `src/monitoring-web` succeeded (267 packages). `npm install-scripts approve esbuild @parcel/watcher` then `npm install` so esbuild postinstall could run.
- Safety net (before replacing CLI welcome): `npx ng test --watch=false` — exit 0, 2 passed.
- Build: `npx ng build` — exit 0, `Application bundle generation complete`, styles 2.04 kB.
- Smoke: `npm test` (`ng test --watch=false`) — exit 0, 1 file, 2 passed, vitest 5.0.3, builder `@angular/build:unit-test`.
- Gitignore: `git check-ignore -q src/monitoring-web/package-lock.json` exit 1 (not ignored); `node_modules` ignored.

### Deviations from Design

None material. `--file-name-style-guide=2016` so files match `app.component.ts` / `session-detail-page.component.ts`. Class name remains CLI `App` (not `AppComponent`). `SessionDetailPage` is an empty placeholder; HTTP and three states are Phase 3. `allowScripts` in `package.json` is an npm 12 install requirement so esbuild can postinstall; not a UI contract change. Lockfile not committed in this batch (orchestrator: no commits).

### Issues Found

Angular CLI's bundled `npm install --allow-scripts` failed under npm 12 (`EALLOWSCRIPTS`). Workaround: `npm install` plus `allowScripts` for `esbuild@0.28.2` and `@parcel/watcher@2.6.0`. `lmdb` and `msgpackr-extract` install scripts remain blocked (optional native addons); build and tests succeeded without them.

### Remaining Tasks

Phase 3–5 (tasks 3.1–5.5): session-detail HTTP client TDD, fixture→view chain, CI/config/non-publish.

### Workload / PR Boundary

- Mode: `size:exception` (approval `s04-size-exception-001`); delivery strategy `single-pr`
- Current work unit: SPA scaffold only (Phase 2)
- Boundary: starts at `ng new` into `src/monitoring-web/`; ends when build and `npm test` smoke are green. Does not include Phase 3 client/states, Phase 4 chain, or Phase 5 CI.
- Estimated review budget impact: scaffold + tokens + shell; accepted exception still covers the whole change forecast (1100–1700).

### Status

9/30 tasks complete. Ready for next apply batch (Phase 3).

## Batch 3 — Session detail page behavior (Phase 3, tasks 3.1–3.12)

Strict TDD remains active. Delivery `single-pr` with maintainer-accepted `size:exception` (approval `s04-size-exception-001`). This batch is the detail page behavior only: HTTP client, loading/success/empty/error, retry, keyboard. No commits or branch changes; stayed on `feature/diagramas_generales`. Phase 4 chain and Phase 5 CI were not implemented.

### Completed tasks

- [x] 3.1 RED: `session-detail-api.spec.ts` — GET `/api/v1/sessions/{id}` without scope headers/query; compile then assertion failure without HTTP.
- [x] 3.2 GREEN: `session-detail-api.ts` — `SessionDetail`, empty-origin `SESSION_DETAIL_API_ORIGIN`, `GET ${origin}/api/v1/sessions/${encodeURIComponent(eventId)}`.
- [x] 3.3 RED: page spec 200 — empty placeholder had no «Consultando detalle».
- [x] 3.4 GREEN: `SessionDetailPage` — `rxResource` + `computed()` view, loading «Consultando detalle», success «Sesión en ámbito» and five fields.
- [x] 3.5 RED: 404 — `value()` threw; no empty copy.
- [x] 3.6 GREEN: 404 → empty «No hay sesión en este ámbito», no `dt` rows; extra spec for other-scope 404.
- [x] 3.7 RED: 401 and status 0 still showed loading.
- [x] 3.8 GREEN: error «No se pudo consultar el detalle»; `Reintentar consulta` (`type="button"`) repeats GET via `retryTick`.
- [x] 3.9 RED: keydown Enter/Space did not issue a second GET (jsdom does not click on those keys).
- [x] 3.10 GREEN: `(click)`, `(keydown.enter)`, `(keydown.space)` call retry.
- [x] 3.11: same suite chains 200, 404, 401 (first run passed; prior cycles already implemented the three states).
- [x] 3.12 VERIFY: page include 10 passed; session-detail glob 12 passed; full `npm --prefix src/monitoring-web test` 14 passed, 0 failed.

### TDD Cycle Evidence

| Task | Test File | Layer | Safety Net | RED | GREEN | TRIANGULATE | REFACTOR | Notes / Rationale |
|------|-----------|-------|------------|-----|-------|-------------|----------|-------------------|
| 3.1 | `src/monitoring-web/src/app/session-detail/session-detail-api.spec.ts` | Unit | ✅ npm test 2/2 App specs | ✅ Written; compile miss then expectOne found none (1 failed) | ➖ RED-only task | ✅ encoded `evt/1` | ➖ None needed | Stub `Observable` so RED is assertion, not only TS2307. |
| 3.2 | `src/monitoring-web/src/app/session-detail/session-detail-api.spec.ts` | Unit | N/A (new API) | ✅ Written (3.1) | ✅ 1 then 2 passed | ✅ `shared` empty origin, no `X-Site-Id`/`X-Sensor-Id` | ✅ `sessionDetailUrl` | Origin factory `''`; token for Phase 4 chain. |
| 3.3 | `src/monitoring-web/src/app/session-detail/session-detail-page.component.spec.ts` | Unit | ✅ API specs 2/2 | ✅ Written; `''` vs «Consultando detalle» | ➖ RED-only task | ➖ Single 200 until 3.4 | ➖ None needed | Placeholder from Phase 2. |
| 3.4 | `src/monitoring-web/src/app/session-detail/session-detail-page.component.spec.ts` | Unit | N/A (page behavior) | ✅ Written (3.3) | ✅ 1 passed | ✅ second 200 payload (`other-event` / UDP) | ✅ template+css | `computed()` view; `provideHttpClient()` in `app.config.ts`. |
| 3.5 | `src/monitoring-web/src/app/session-detail/session-detail-page.component.spec.ts` | Unit | ✅ 2/2 page specs | ✅ Written; resource `value()` throws on 404 | ➖ RED-only task | ➖ Single missing 404 | ➖ None needed | Assert 0 `dt`, not CSS classes. |
| 3.6 | `src/monitoring-web/src/app/session-detail/session-detail-page.component.spec.ts` | Unit | N/A | ✅ Written (3.5) | ✅ 3 then 4 passed | ✅ `other-only` 404 empty | ➖ None needed | Read `error()` before `value()`. |
| 3.7 | `src/monitoring-web/src/app/session-detail/session-detail-page.component.spec.ts` | Unit | ✅ 4/4 page | ✅ Written; 401 and status 0 still loading (2 failed) | ➖ RED-only task | ✅ 401 and ProgressEvent status 0 | ➖ None needed | Must not match empty copy. |
| 3.8 | `src/monitoring-web/src/app/session-detail/session-detail-page.component.spec.ts` | Unit | N/A | ✅ Error text GREEN 6/6; retry button missing then click retries | ✅ 7 passed | ✅ retry 401→200 `shared` | ✅ `retryTick` params (zoneless `reload()` issued no GET) | Native `button type="button"`. |
| 3.9 | `src/monitoring-web/src/app/session-detail/session-detail-page.component.spec.ts` | Unit | ✅ 7/7 page | ✅ Written; Enter/Space expectOne found none (2 failed) | ➖ RED-only task | ✅ Enter and Space | ➖ None needed | Dispatch `keydown`, not click. |
| 3.10 | `src/monitoring-web/src/app/session-detail/session-detail-page.component.spec.ts` | Unit | N/A | ✅ Written (3.9) | ✅ 9 passed | ✅ both keys | ✅ `retryFromKeyboard` | preventDefault on key handlers. |
| 3.11 | `src/monitoring-web/src/app/session-detail/session-detail-page.component.spec.ts` | Unit | ✅ 9/9 page | ✅ Chain spec written | ✅ 10 passed first run | ✅ 200 then 404 then 401 same harness | ✅ retryButton helper | Aggregation; no extra production. |
| 3.12 | `src/monitoring-web/src/app/session-detail/session-detail-page.component.spec.ts` | Unit | ✅ full suite 14/14 | ✅ Written (3.3–3.11) | ✅ page include 10/10; glob 12/12; npm test 14/14 | ➖ Verify task | ➖ None needed | npm 12 rejects extra `-- --watch=false`; script already has `--watch=false`. |

### Test Summary

- **Total tests written this batch**: 12 (2 API + 10 page)
- **Total tests passing this batch**: 12 session-detail + 2 shell = 14
- **Layers used**: Unit (12 behavior). Integration/E2E: 0 (Phase 4 owns the real GET chain)
- **Approval tests** (refactoring): None
- **Pure functions created**: 2 (`sessionDetailUrl`, `httpStatus`)

### Files Changed

| File | Action | What Was Done |
|------|--------|---------------|
| `src/monitoring-web/src/app/session-detail/session-detail-api.spec.ts` | Created | GET URL, encodeURIComponent, no scope headers/query |
| `src/monitoring-web/src/app/session-detail/session-detail-api.ts` | Created | `SessionDetail`, origin token, `sessionDetailUrl`, HttpClient GET |
| `src/monitoring-web/src/app/session-detail/session-detail-page.component.spec.ts` | Created | 200, 404, 401, status 0, retry click/keyboard, chained three states |
| `src/monitoring-web/src/app/session-detail/session-detail-page.component.ts` | Modified | Replaced empty placeholder: `rxResource`, `computed()` view, retry |
| `src/monitoring-web/src/app/session-detail/session-detail-page.component.html` | Created | Loading/empty/error/success; native retry button |
| `src/monitoring-web/src/app/session-detail/session-detail-page.component.css` | Created | Dense ficha using DESIGN.md tokens |
| `src/monitoring-web/src/app/app.config.ts` | Modified | `provideHttpClient()` |
| `openspec/changes/s04-vista-angular/tasks.md` | Modified | Marked 3.1–3.12 `[x]` |

### Verification

- Safety net: `npm --prefix src/monitoring-web test` — exit 0, 2 passed (App shell).
- 3.1 RED: `npx ng test --watch=false --include=src/app/session-detail/session-detail-api.spec.ts` — exit 1, 1 failed (expectOne found none).
- 3.2 GREEN: same include — exit 0, 2 passed.
- 3.3 RED: page include — exit 1, expected «Consultando detalle», received `''`.
- 3.12 page filter: `npx ng test --watch=false --include=src/app/session-detail/session-detail-page.component.spec.ts` — exit 0, 10 passed, 0 failed.
- Session-detail glob: `--include=src/app/session-detail/**/*.spec.ts` — exit 0, 2 files, 12 passed.
- Full SPA: `npm --prefix src/monitoring-web test` — exit 0, 3 files, 14 passed, 0 failed.
- `npx ng build` — exit 0.
- Orchestrator extra `-- --watch=false` after `npm --prefix ... test` is `EUNKNOWNCONFIG` on npm 12; the `test` script already runs `ng test --watch=false`.

### Deviations from Design

None material. `resource.reload()` did not emit a second GET under zoneless TestBed; retry bumps `retryTick` in `rxResource` params instead, which is the same observable contract (repeat GET). `SESSION_DETAIL_API_ORIGIN` defaults to `''` (proxy); Phase 4 still owns setting it for the chain.

### Issues Found

npm 12 treats `npm --prefix src/monitoring-web test -- --watch=false` as an unknown config flag. Use `npm --prefix src/monitoring-web test` or `npx ng test --watch=false --include=...`.

### Remaining Tasks

Phase 4–5 (tasks 4.1–5.5): fixture→view chain, CI/config/non-publish.

### Workload / PR Boundary

- Mode: `size:exception` (approval `s04-size-exception-001`); delivery strategy `single-pr`
- Current work unit: detail page behavior only (Phase 3)
- Boundary: starts at `session-detail-api` RED; ends when page include and full `npm test` are green. Does not include Phase 4 `SessionViewChainHost` or Phase 5 CI.
- Estimated review budget impact: accepted exception still covers the whole change forecast (1100–1700).

### Status

21/30 tasks complete. Ready for next apply batch (Phase 4).

## Batch 4 — Fixture → view chain (Phase 4, tasks 4.1–4.4)

Strict TDD remains active. Delivery `single-pr` with maintainer-accepted `size:exception` (approval `s04-size-exception-001`). This batch is the fixture → ACK → worker → API → UI chain only. No commits or branch changes; stayed on `feature/diagramas_generales`. Phase 5 CI/config/non-publish was not implemented.

### Completed tasks

- [x] 4.1 RED: `GenerateProgramFile` false, `StartupObject=Monitoring.Tests.SessionViewChainHost`, chain spec spawn of `--view-chain` failed with exit 1 and no `VIEW_CHAIN_READY`.
- [x] 4.2 GREEN: `SessionViewChainHost.Main` — `PostgresFixture`, `CreateAsync`, substitute only `ITrustedSensorIdentityProvider`, POST batch, ACK 200 empty or exit 1, worker (no `ProjectAsync`), poll GET 10 s, `UseKestrel(0)` before `CreateClient`, emit `VIEW_CHAIN_READY {origin} {eventId}`, wait stdin.
- [x] 4.3 GREEN: `session-detail-view-chain.spec.ts` — `beforeAll` 120 s, `provideHttpClient(withFetch())`, `SESSION_DETAIL_API_ORIGIN` from READY, `ValidData` five fields, no `HttpTestingController`. Page factory origin stays empty.
- [x] 4.4 VERIFY: from repo root, `dotnet run --project tests/Monitoring.Tests --no-build -- --view-chain` emitted READY and exited 0 after stdin close; `npm --prefix src/monitoring-web test` 15 passed, 0 failed.

### TDD Cycle Evidence

| Task | Test File | Layer | Safety Net | RED | GREEN | TRIANGULATE | REFACTOR | Notes / Rationale |
|------|-----------|-------|------------|-----|-------|-------------|----------|-------------------|
| 4.1 | `src/monitoring-web/src/app/session-detail/session-detail-view-chain.spec.ts` | Integration | ✅ npm test 14/14; HostStartup+Domain 2/2; page include 10/10 | ✅ Written; stub Main exit 1, no READY (1 suite failed, 1 skipped) | ➖ RED-only task | ➖ Single fixture-to-view | ➖ None needed | Isolated file so page specs do not start Docker. Node builtin shims, no `@types/node`. |
| 4.2 | `src/monitoring-web/src/app/session-detail/session-detail-view-chain.spec.ts` | Integration | N/A (new host Main) | ✅ Written (4.1) | ✅ Chain include 1 passed after content-root fix | ➖ Single | ✅ `UseContentRoot` via `Monitoring.slnx` | `dotnet run` is not testhost; factory looked for `{cwd}/Monitoring.Host`. |
| 4.3 | `src/monitoring-web/src/app/session-detail/session-detail-view-chain.spec.ts` | Integration | ✅ page include 10/10 without Docker | ✅ Written (4.1) | ✅ 1 passed, real GET, five `dt` rows, ValidData | ➖ Single scenario | ➖ None needed | Origin injected only in this spec. |
| 4.4 | `src/monitoring-web/src/app/session-detail/session-detail-view-chain.spec.ts` | Integration | ✅ HostStartup+Domain 2/2 | ✅ Written (4.1) | ✅ `dotnet run --no-build -- --view-chain` READY+exit 0; `npm --prefix src/monitoring-web test` 15/15 | ➖ Verify task | ➖ None needed | npm 12: do not append `-- --watch=false`. |

### Test Summary

- **Total tests written this batch**: 1 (`SessionDetail fixture-to-view chain`)
- **Total tests passing this batch**: 1 chain + 14 existing SPA = 15
- **Layers used**: Integration (1 real GET). Unit: 0 new
- **Approval tests** (refactoring): None
- **Pure functions created**: 1 (`HostProjectRoot`)

### Files Changed

| File | Action | What Was Done |
|------|--------|---------------|
| `tests/Monitoring.Tests/Monitoring.Tests.csproj` | Modified | `GenerateProgramFile` false; `StartupObject` `Monitoring.Tests.SessionViewChainHost` |
| `tests/Monitoring.Tests/SessionViewChainHost.cs` | Created | `--view-chain` Main: fixture, ACK, worker poll, Kestrel, READY, stdin |
| `src/monitoring-web/src/app/session-detail/session-detail-view-chain.spec.ts` | Created | Isolated chain spec, 120 s `beforeAll`, real fetch GET |
| `src/monitoring-web/src/app/session-detail/view-chain-node.shims.d.ts` | Created | Ambient Node builtins for the chain spec (no extra runner) |
| `openspec/changes/s04-vista-angular/tasks.md` | Modified | Marked 4.1–4.4 `[x]` |

### Verification

- Safety net: `npm --prefix src/monitoring-web test` — exit 0, 14 passed (before chain spec).
- Safety net: `dotnet test Monitoring.slnx --filter "FullyQualifiedName~HostStartupTests|FullyQualifiedName~DomainDependencyTests"` — exit 0, 2 passed.
- Isolation: `npx ng test --watch=false --include=src/app/session-detail/session-detail-page.component.spec.ts` — exit 0, 10 passed (no Docker).
- 4.1 RED: `npx ng test --watch=false --include=src/app/session-detail/session-detail-view-chain.spec.ts` — exit 1, stub `--view-chain` exited 1 before `VIEW_CHAIN_READY`.
- 4.2/4.3 GREEN: same include — exit 0, 1 passed, 0 failed, ~9 s.
- 4.4 host: `dotnet run --project tests/Monitoring.Tests --no-build -- --view-chain` from repo root — `VIEW_CHAIN_READY http://localhost:5000 event`, stdin close, exit 0.
- 4.4 npm: `npm --prefix src/monitoring-web test` — exit 0, 4 files, 15 passed, 0 failed.
- `npx ng build` — exit 0.

### Deviations from Design

None material. `WebApplicationFactory.UseKestrel(int)` returns void (call after `WithWebHostBuilder`, before `CreateClient`). `dotnet run` does not apply the testhost content-root manifest, so the host sets `UseContentRoot` to `src/Monitoring.Host` by walking to `Monitoring.slnx`. Page `SESSION_DETAIL_API_ORIGIN` factory stays `''`.

### Issues Found

`dotnet run --project tests/Monitoring.Tests` initially threw `DirectoryNotFoundException` for `{repo}/Monitoring.Host/`. Fixed with `UseContentRoot`. npm 12 still rejects extra `-- --watch=false`.

### Remaining Tasks

Phase 5 (tasks 5.1–5.5): CI, `openspec/config.yaml` ui gate, README, publish tree, full `dotnet test` regression.

### Workload / PR Boundary

- Mode: `size:exception` (approval `s04-size-exception-001`); delivery strategy `single-pr`
- Current work unit: fixture → ACK → worker → API → UI chain only (Phase 4)
- Boundary: starts at chain spec RED; ends when `--view-chain` READY and npm chain/full suite are green. Does not include Phase 5.
- Estimated review budget impact: accepted exception still covers the whole change forecast (1100–1700).

### Status

25/30 tasks complete. Ready for next apply batch (Phase 5).

## Batch 5 — CI, config, README, and publish exclusion (Phase 5, tasks 5.1–5.5)

Strict TDD remains active. Delivery `single-pr` with maintainer-accepted `size:exception` (approval `s04-size-exception-001`). This batch is CI, `openspec/config.yaml`, README, and the publish exclusion check only. No commits or branch changes; stayed on `feature/diagramas_generales`. Did not redo tasks 1.1–4.4.

### Completed tasks

- [x] 5.1 RED/GREEN: `OpenspecConfigDeclaresAngularNpmAndUiGate` — `package_managers` includes npm, capability `angular` 22.2.0, puerta `ui` (`npm --prefix src/monitoring-web test`, required, halt, 300000 ms). `commands.test` stays `dotnet test Monitoring.slnx`.
- [x] 5.2 RED/GREEN: `CiWorkflowPinsNodeAndRunsAngularTests` — Node 24.16.0, `cache: npm` + lockfile path, `dotnet test`, `npm ci`, `npm test` in `src/monitoring-web`. Same job as .NET so Docker remains available for the chain spec. No extra `-- --watch=false`.
- [x] 5.3 RED/GREEN: `ReadmeDocumentsProxyTrustedScopeAndNonPublication` — proxy `/api` → `http://127.0.0.1:5080`, `TrustedSessionRead:SiteId`/`SensorId` and env form, SPA no se publica.
- [x] 5.4 RED/GREEN: CI publish + tree check; `PublishedHostDoesNotIncludeWwwrootOrMonitoringWebArtifacts` runs `dotnet publish` of the host and asserts no `wwwroot` and no `monitoring-web` paths. Host already excluded SPA (hexagonal); RED was the missing CI step.
- [x] 5.5 VERIFY: `dotnet test Monitoring.slnx` exit 0, 122 passed, 0 failed. `SessionHostTests` filter 8 passed, 0 failed.

### TDD Cycle Evidence

| Task | Test File | Layer | Safety Net | RED | GREEN | TRIANGULATE | REFACTOR | Notes / Rationale |
|------|-----------|-------|------------|-----|-------|-------------|----------|-------------------|
| 5.1 | `tests/Monitoring.Tests/VistaAngularDeliveryTests.cs` | Unit | ✅ HostStartup+Domain 2/2 | ✅ Written; 1 failed (`package_managers: [dotnet, npm]` missing) | ✅ 1 passed | ✅ npm, angular 22.2.0, ui gate, tests command unchanged | ➖ None needed | Config-only; no unrelated rewrite. |
| 5.2 | `tests/Monitoring.Tests/VistaAngularDeliveryTests.cs` | Unit | N/A (ci.yml) | ✅ Written; 1 failed (`node-version: "24.16.0"` missing), 1 passed | ✅ 2 passed | ✅ cache, npm ci, npm test, working-directory, no `--watch=false` | ➖ None needed | `actions/setup-node@v7` matches checkout@v7. Chain spec stays in `npm test`. |
| 5.3 | `tests/Monitoring.Tests/VistaAngularDeliveryTests.cs` | Unit | N/A (README) | ✅ Written; 1 failed (`src/monitoring-web` missing) | ✅ 3 passed | ✅ proxy, colon keys, env keys, `no se publica` | ➖ None needed | Intro updated S01–S04; later slices still out of scope. |
| 5.4 | `tests/Monitoring.Tests/VistaAngularDeliveryTests.cs` | Integration | ✅ publish tree already empty on first run | ✅ CI fact failed (no `dotnet publish`); publish tree 0 hits | ✅ 5 passed | ✅ CI yaml + real publish tree | ➖ None needed | No host csproj change; SPA never copied. |
| 5.5 | `tests/Monitoring.Tests/SessionHostTests.cs` | Integration | ✅ full slnx 122/122 | ➖ Verify task | ✅ `dotnet test Monitoring.slnx` 122 passed; SessionHostTests 8/8 | ➖ Verify task | ➖ None needed | Docker up; REQ-detalle-sesion-ambito-001 still green. |

### Test Summary

- **Total tests written this batch**: 5 (`VistaAngularDeliveryTests`)
- **Total tests passing this batch**: 5 delivery + 122 slnx (includes those 5 and SessionHostTests 8)
- **Layers used**: Unit (4 config/docs/CI), Integration (1 real `dotnet publish`)
- **Approval tests** (refactoring): 1 characterization (`PublishedHostDoesNotIncludeWwwrootOrMonitoringWebArtifacts` passed on first execution because the host never copied the SPA)
- **Pure functions created**: 2 (`RepoRoot`, `Normalize`)

### Files Changed

| File | Action | What Was Done |
|------|--------|---------------|
| `tests/Monitoring.Tests/VistaAngularDeliveryTests.cs` | Created | Config, CI, README, publish-tree, and CI publish-exclusion facts |
| `openspec/config.yaml` | Modified | npm in package_managers; angular 22.2.0; quality_gates.ui |
| `.github/workflows/ci.yml` | Modified | Node 24.16.0 + npm cache, npm ci/test, publish + tree check |
| `README.md` | Modified | SPA path, proxy, TrustedSessionRead, non-publication |
| `openspec/changes/s04-vista-angular/tasks.md` | Modified | Marked 5.1–5.5 `[x]` |

### Verification

- Safety net: `dotnet test Monitoring.slnx --filter "FullyQualifiedName~HostStartupTests|FullyQualifiedName~DomainDependencyTests"` — exit 0, 2 passed.
- 5.1 RED: `dotnet test Monitoring.slnx --filter FullyQualifiedName~VistaAngularDeliveryTests` — exit 1, 1 failed, 0 passed.
- 5.1 GREEN: same filter — exit 0, 1 passed.
- 5.2 RED: same filter — exit 1, 1 failed, 1 passed (`node-version` missing).
- 5.2 GREEN: same filter — exit 0, 2 passed.
- 5.3 RED: `--filter FullyQualifiedName~ReadmeDocumentsProxyTrustedScopeAndNonPublication` — exit 1, 1 failed.
- 5.3 GREEN: VistaAngularDeliveryTests — exit 0, 3 passed.
- 5.4 RED: VistaAngularDeliveryTests — exit 1, 1 failed (CI publish), 4 passed (local publish tree already clean).
- 5.4 GREEN: VistaAngularDeliveryTests — exit 0, 5 passed, 0 failed.
- 5.5: `dotnet test Monitoring.slnx` — exit 0, 122 passed, 0 failed, ~19 s.
- 5.5 SessionHostTests: `dotnet test Monitoring.slnx --no-build --filter FullyQualifiedName~SessionHostTests` — exit 0, 8 passed, 0 failed.

### Deviations from Design

None material. `dotnet publish` of the host needed no csproj/wwwroot change; exclusion is the existing hexagonal boundary. CI uses `actions/setup-node@v7` (same major family as `checkout@v7`) with Node `24.16.0` as designed.

### Issues Found

None. Docker was available; 5.5 is not deferred.

### Remaining Tasks

None in this change. Ready for `sdd-verify`.

### Workload / PR Boundary

- Mode: `size:exception` (approval `s04-size-exception-001`); delivery strategy `single-pr`
- Current work unit: CI, config, README, and publish exclusion (Phase 5)
- Boundary: starts at the ui-gate config RED; ends when full `dotnet test Monitoring.slnx` and SessionHostTests are green. Does not include sdd-verify.
- Estimated review budget impact: accepted exception still covers the whole change forecast (1100–1700).

### Status

30/30 tasks complete. Ready for sdd-verify.

```json:strict-tdd-evidence
{
  "schema_version": 1,
  "change": "s04-vista-angular",
  "evidence_mode": "live",
  "cycles": [
    {
      "task": "1.1",
      "test_file": "tests/Monitoring.Tests/TrustedSessionReadScopeTests.cs",
      "test_name": "Monitoring.Tests.TrustedSessionReadScopeTests",
      "layer": "integration",
      "safety_net": {
        "command": "dotnet test Monitoring.slnx --filter FullyQualifiedName~SessionHostTests",
        "exit_code": 0,
        "discovered": 8,
        "passed": 8,
        "failed": 0
      },
      "red": {
        "command": "dotnet test Monitoring.slnx --filter FullyQualifiedName~TrustedSessionReadScopeTests --no-restore",
        "exit_code": 1,
        "observed": "Five behavioral failures: configured Development/Testing GET returned 401 instead of 200, client headers/query still 401 instead of configured 200, foreign eventId returned 401 instead of 404. Three 401 characterization cases (Production, missing config) already passed.",
        "discovered": 8,
        "passed": 3,
        "failed": 5
      },
      "green": {
        "command": "dotnet test Monitoring.slnx --filter FullyQualifiedName~TrustedSessionReadScopeTests",
        "exit_code": 0,
        "discovered": 8,
        "passed": 8,
        "failed": 0
      },
      "triangulation": "Theory Development/Testing; headers and query of another scope; Production feature null after next; Dev/Testing without keys; 404 without foreign body.",
      "refactor": "not-needed",
      "provenance": {
        "source": "working-tree",
        "commit": "working-tree",
        "test_file": "tests/Monitoring.Tests/TrustedSessionReadScopeTests.cs",
        "test_digest": "sha256:dc26ea0ae7f769889044593cedb945338efa3293a955a22f34b295100d3f1d14",
        "command": "dotnet test Monitoring.slnx --filter FullyQualifiedName~TrustedSessionReadScopeTests"
      }
    },
    {
      "task": "1.2",
      "test_file": "tests/Monitoring.Tests/TrustedSessionReadScopeTests.cs",
      "test_name": "Monitoring.Tests.TrustedSessionReadScopeTests",
      "layer": "integration",
      "red": "written",
      "green": "passed",
      "triangulate": "passed",
      "refactor": "not-needed",
      "provenance": {
        "source": "working-tree",
        "commit": "working-tree",
        "test_file": "tests/Monitoring.Tests/TrustedSessionReadScopeTests.cs",
        "test_digest": "sha256:dc26ea0ae7f769889044593cedb945338efa3293a955a22f34b295100d3f1d14",
        "command": "dotnet test Monitoring.slnx --filter FullyQualifiedName~TrustedSessionReadScopeTests"
      }
    },
    {
      "task": "1.3",
      "test_file": "tests/Monitoring.Tests/TrustedSessionReadScopeTests.cs",
      "test_name": "Monitoring.Tests.TrustedSessionReadScopeTests",
      "layer": "integration",
      "safety_net": {
        "command": "dotnet test Monitoring.slnx --filter FullyQualifiedName~SessionHostTests",
        "exit_code": 0,
        "discovered": 8,
        "passed": 8,
        "failed": 0
      },
      "red": "written",
      "green": "passed",
      "triangulate": "passed",
      "refactor": "not-needed",
      "provenance": {
        "source": "working-tree",
        "commit": "working-tree",
        "test_file": "tests/Monitoring.Tests/TrustedSessionReadScopeTests.cs",
        "test_digest": "sha256:dc26ea0ae7f769889044593cedb945338efa3293a955a22f34b295100d3f1d14",
        "command": "dotnet test Monitoring.slnx --filter FullyQualifiedName~TrustedSessionReadScopeTests"
      }
    },
    {
      "task": "1.4",
      "test_file": "tests/Monitoring.Tests/TrustedSessionReadScopeTests.cs",
      "test_name": "Monitoring.Tests.TrustedSessionReadScopeTests",
      "layer": "integration",
      "red": "written",
      "green": {
        "command": "dotnet test Monitoring.slnx --filter FullyQualifiedName~TrustedSessionReadScopeTests",
        "exit_code": 0,
        "discovered": 8,
        "passed": 8,
        "failed": 0
      },
      "triangulate": "not-needed",
      "refactor": "not-needed",
      "provenance": {
        "source": "working-tree",
        "commit": "working-tree",
        "test_file": "tests/Monitoring.Tests/TrustedSessionReadScopeTests.cs",
        "test_digest": "sha256:dc26ea0ae7f769889044593cedb945338efa3293a955a22f34b295100d3f1d14",
        "command": "dotnet test Monitoring.slnx --filter FullyQualifiedName~TrustedSessionReadScopeTests"
      }
    },
    {
      "task": "2.1",
      "test_file": null,
      "test_name": null,
      "layer": "scaffold",
      "red": "not-applicable",
      "green": {
        "command": "npx ng build",
        "exit_code": 0,
        "observed": "Angular 22.2.0 application bundle complete at src/monitoring-web/dist/monitoring-web. Not a behavior GREEN.",
        "discovered": 0,
        "passed": 0,
        "failed": 0
      },
      "triangulate": "not-applicable",
      "refactor": "not-needed",
      "notes": "Scaffold / no behavior yet. ng new is not a RED/GREEN behavior cycle. Behavior tests start in Phase 3.",
      "provenance": {
        "source": "working-tree",
        "commit": "working-tree",
        "test_file": "src/monitoring-web/angular.json",
        "test_digest": "sha256:9821e2e21836f58d4eb02e5f64dce6e502aefa15f329de9ad19638167bf3d979",
        "command": "npx ng build"
      }
    },
    {
      "task": "2.2",
      "test_file": null,
      "test_name": null,
      "layer": "scaffold",
      "red": "not-applicable",
      "green": {
        "command": "git check-ignore -q src/monitoring-web/package-lock.json",
        "exit_code": 1,
        "observed": "package-lock.json is not ignored; node_modules and dist are ignored. Lockfile commit deferred (no commits in this batch).",
        "discovered": 0,
        "passed": 0,
        "failed": 0
      },
      "triangulate": "not-applicable",
      "refactor": "not-needed",
      "notes": "Scaffold / no behavior yet.",
      "provenance": {
        "source": "working-tree",
        "commit": "working-tree",
        "test_file": ".gitignore",
        "test_digest": "sha256:4fa8bd5cdbc0490afb5984427f7e7a49d5653347d30a02828eebd6259a68bec7",
        "command": "git check-ignore -q src/monitoring-web/package-lock.json"
      }
    },
    {
      "task": "2.3",
      "test_file": null,
      "test_name": null,
      "layer": "scaffold",
      "red": "not-applicable",
      "green": {
        "command": "static-check DESIGN.md",
        "exit_code": 0,
        "observed": "Root DESIGN.md exists, hybrid mode, quiet chrome, no inspector, no proprietary Linear/Attio assets.",
        "discovered": 0,
        "passed": 0,
        "failed": 0
      },
      "triangulate": "not-applicable",
      "refactor": "not-needed",
      "notes": "Scaffold / no behavior yet. Design-language seed only.",
      "provenance": {
        "source": "working-tree",
        "commit": "working-tree",
        "test_file": "DESIGN.md",
        "test_digest": "sha256:e0dd3694fd456c8132dc9830775b19e40b6745ee17594a089fa8d4e8d95a2f34",
        "command": "static-check DESIGN.md"
      }
    },
    {
      "task": "2.4",
      "test_file": "src/monitoring-web/src/app/app.component.spec.ts",
      "test_name": "App",
      "layer": "scaffold-smoke",
      "safety_net": {
        "command": "npx ng test --watch=false",
        "exit_code": 0,
        "discovered": 2,
        "passed": 2,
        "failed": 0
      },
      "red": "not-applicable",
      "green": {
        "command": "npm test",
        "exit_code": 0,
        "observed": "2 passed: nav toggle is button type=button and collapses; Detalle is button type=button. Not session-detail behavior.",
        "discovered": 2,
        "passed": 2,
        "failed": 0
      },
      "triangulate": "not-applicable",
      "refactor": "not-needed",
      "notes": "Scaffold / no behavior yet. Smoke only. Phase 3 owns session-detail RED/GREEN.",
      "provenance": {
        "source": "working-tree",
        "commit": "working-tree",
        "test_file": "src/monitoring-web/src/app/app.component.spec.ts",
        "test_digest": "sha256:bb91c33185a158af40a2e8f548b24cd389304f0d9a20d2221777f1d1dd86bf10",
        "command": "npm test"
      }
    },
    {
      "task": "2.5",
      "test_file": "src/monitoring-web/src/app/app.component.spec.ts",
      "test_name": "App",
      "layer": "scaffold",
      "red": "not-applicable",
      "green": {
        "command": "npx ng build",
        "exit_code": 0,
        "observed": "styles-N4XGY5NK.css 2.04 kB emitted; npm test still 2 passed. Tokens are CSS custom properties, not a behavior cycle.",
        "discovered": 2,
        "passed": 2,
        "failed": 0
      },
      "triangulate": "not-applicable",
      "refactor": "not-needed",
      "notes": "Scaffold / no behavior yet.",
      "provenance": {
        "source": "working-tree",
        "commit": "working-tree",
        "test_file": "src/monitoring-web/src/styles.css",
        "test_digest": "sha256:c454350481ab4d751a071d2c32ff1c6bace73f09847a3049000b111b1f294853",
        "command": "npx ng build"
      }
    },
    {
      "task": "3.1",
      "test_file": "src/monitoring-web/src/app/session-detail/session-detail-api.spec.ts",
      "test_name": "SessionDetailApi",
      "layer": "unit",
      "safety_net": {
        "command": "npm --prefix src/monitoring-web test",
        "exit_code": 0,
        "discovered": 2,
        "passed": 2,
        "failed": 0
      },
      "red": {
        "command": "npx ng test --watch=false --include=src/app/session-detail/session-detail-api.spec.ts",
        "exit_code": 1,
        "observed": "After a compile-only miss (TS2307), a stub Observable compiled and expectOne found no GET to /api/v1/sessions/evt%2F1.",
        "discovered": 1,
        "passed": 0,
        "failed": 1
      },
      "green": "written",
      "triangulate": "passed",
      "refactor": "not-needed",
      "provenance": {
        "source": "working-tree",
        "commit": "working-tree",
        "test_file": "src/monitoring-web/src/app/session-detail/session-detail-api.spec.ts",
        "test_digest": "sha256:d319263d3b463807b39dd709d923b7bce5013e30bab80385f7953109258754ef",
        "command": "npx ng test --watch=false --include=src/app/session-detail/session-detail-api.spec.ts"
      }
    },
    {
      "task": "3.2",
      "test_file": "src/monitoring-web/src/app/session-detail/session-detail-api.spec.ts",
      "test_name": "SessionDetailApi",
      "layer": "unit",
      "red": "written",
      "green": {
        "command": "npx ng test --watch=false --include=src/app/session-detail/session-detail-api.spec.ts",
        "exit_code": 0,
        "discovered": 2,
        "passed": 2,
        "failed": 0
      },
      "triangulate": "passed",
      "refactor": "passed",
      "notes": "sessionDetailUrl extracted after GREEN. SESSION_DETAIL_API_ORIGIN factory is empty string.",
      "provenance": {
        "source": "working-tree",
        "commit": "working-tree",
        "test_file": "src/monitoring-web/src/app/session-detail/session-detail-api.ts",
        "test_digest": "sha256:9d5aaf69b44cf91935ac793a3bf56058ef8eb41cc18e7e1b9b60d70e760afc44",
        "command": "npx ng test --watch=false --include=src/app/session-detail/session-detail-api.spec.ts"
      }
    },
    {
      "task": "3.3",
      "test_file": "src/monitoring-web/src/app/session-detail/session-detail-page.component.spec.ts",
      "test_name": "SessionDetailPage",
      "layer": "unit",
      "safety_net": {
        "command": "npx ng test --watch=false --include=src/app/session-detail/session-detail-api.spec.ts",
        "exit_code": 0,
        "discovered": 2,
        "passed": 2,
        "failed": 0
      },
      "red": {
        "command": "npx ng test --watch=false --include=src/app/session-detail/session-detail-page.component.spec.ts",
        "exit_code": 1,
        "observed": "Empty Phase 2 placeholder: expected Consultando detalle, received empty text.",
        "discovered": 1,
        "passed": 0,
        "failed": 1
      },
      "green": "written",
      "triangulate": "not-needed",
      "refactor": "not-needed",
      "provenance": {
        "source": "working-tree",
        "commit": "working-tree",
        "test_file": "src/monitoring-web/src/app/session-detail/session-detail-page.component.spec.ts",
        "test_digest": "sha256:e7df6c5c5876e55d552d5efaf820c6e41882f46bee06c77d0ebcad3ac0790403",
        "command": "npx ng test --watch=false --include=src/app/session-detail/session-detail-page.component.spec.ts"
      }
    },
    {
      "task": "3.4",
      "test_file": "src/monitoring-web/src/app/session-detail/session-detail-page.component.spec.ts",
      "test_name": "SessionDetailPage",
      "layer": "unit",
      "red": "written",
      "green": {
        "command": "npx ng test --watch=false --include=src/app/session-detail/session-detail-page.component.spec.ts",
        "exit_code": 0,
        "discovered": 2,
        "passed": 2,
        "failed": 0
      },
      "triangulate": "passed",
      "refactor": "passed",
      "notes": "Second 200 payload other-event/UDP. computed() view; provideHttpClient in app.config.",
      "provenance": {
        "source": "working-tree",
        "commit": "working-tree",
        "test_file": "src/monitoring-web/src/app/session-detail/session-detail-page.component.ts",
        "test_digest": "sha256:8033fc9f471c09b2db2945ef990138d9d79216179c40c89dcb15c05914e74c6a",
        "command": "npx ng test --watch=false --include=src/app/session-detail/session-detail-page.component.spec.ts"
      }
    },
    {
      "task": "3.5",
      "test_file": "src/monitoring-web/src/app/session-detail/session-detail-page.component.spec.ts",
      "test_name": "SessionDetailPage",
      "layer": "unit",
      "red": {
        "command": "npx ng test --watch=false --include=src/app/session-detail/session-detail-page.component.spec.ts",
        "exit_code": 1,
        "observed": "HTTP 404: resource value() threw ResourceValueError; 1 failed, 2 passed.",
        "discovered": 3,
        "passed": 2,
        "failed": 1
      },
      "green": "written",
      "triangulate": "not-needed",
      "refactor": "not-needed",
      "provenance": {
        "source": "working-tree",
        "commit": "working-tree",
        "test_file": "src/monitoring-web/src/app/session-detail/session-detail-page.component.spec.ts",
        "test_digest": "sha256:e7df6c5c5876e55d552d5efaf820c6e41882f46bee06c77d0ebcad3ac0790403",
        "command": "npx ng test --watch=false --include=src/app/session-detail/session-detail-page.component.spec.ts"
      }
    },
    {
      "task": "3.6",
      "test_file": "src/monitoring-web/src/app/session-detail/session-detail-page.component.spec.ts",
      "test_name": "SessionDetailPage",
      "layer": "unit",
      "red": "written",
      "green": {
        "command": "npx ng test --watch=false --include=src/app/session-detail/session-detail-page.component.spec.ts",
        "exit_code": 0,
        "discovered": 4,
        "passed": 4,
        "failed": 0
      },
      "triangulate": "passed",
      "refactor": "not-needed",
      "notes": "other-only 404 uses the same empty copy and no dt rows.",
      "provenance": {
        "source": "working-tree",
        "commit": "working-tree",
        "test_file": "src/monitoring-web/src/app/session-detail/session-detail-page.component.ts",
        "test_digest": "sha256:8033fc9f471c09b2db2945ef990138d9d79216179c40c89dcb15c05914e74c6a",
        "command": "npx ng test --watch=false --include=src/app/session-detail/session-detail-page.component.spec.ts"
      }
    },
    {
      "task": "3.7",
      "test_file": "src/monitoring-web/src/app/session-detail/session-detail-page.component.spec.ts",
      "test_name": "SessionDetailPage",
      "layer": "unit",
      "red": {
        "command": "npx ng test --watch=false --include=src/app/session-detail/session-detail-page.component.spec.ts",
        "exit_code": 1,
        "observed": "401 and status 0 still rendered Consultando detalle instead of error copy. 2 failed, 4 passed.",
        "discovered": 6,
        "passed": 4,
        "failed": 2
      },
      "green": "written",
      "triangulate": "passed",
      "refactor": "not-needed",
      "provenance": {
        "source": "working-tree",
        "commit": "working-tree",
        "test_file": "src/monitoring-web/src/app/session-detail/session-detail-page.component.spec.ts",
        "test_digest": "sha256:e7df6c5c5876e55d552d5efaf820c6e41882f46bee06c77d0ebcad3ac0790403",
        "command": "npx ng test --watch=false --include=src/app/session-detail/session-detail-page.component.spec.ts"
      }
    },
    {
      "task": "3.8",
      "test_file": "src/monitoring-web/src/app/session-detail/session-detail-page.component.spec.ts",
      "test_name": "SessionDetailPage",
      "layer": "unit",
      "red": {
        "command": "npx ng test --watch=false --include=src/app/session-detail/session-detail-page.component.spec.ts",
        "exit_code": 1,
        "observed": "Error copy passed (6/6) then retry button missing; after button, zoneless reload() issued no second GET until retryTick params.",
        "discovered": 7,
        "passed": 6,
        "failed": 1
      },
      "green": {
        "command": "npx ng test --watch=false --include=src/app/session-detail/session-detail-page.component.spec.ts",
        "exit_code": 0,
        "discovered": 7,
        "passed": 7,
        "failed": 0
      },
      "triangulate": "passed",
      "refactor": "passed",
      "provenance": {
        "source": "working-tree",
        "commit": "working-tree",
        "test_file": "src/monitoring-web/src/app/session-detail/session-detail-page.component.ts",
        "test_digest": "sha256:8033fc9f471c09b2db2945ef990138d9d79216179c40c89dcb15c05914e74c6a",
        "command": "npx ng test --watch=false --include=src/app/session-detail/session-detail-page.component.spec.ts"
      }
    },
    {
      "task": "3.9",
      "test_file": "src/monitoring-web/src/app/session-detail/session-detail-page.component.spec.ts",
      "test_name": "SessionDetailPage",
      "layer": "unit",
      "red": {
        "command": "npx ng test --watch=false --include=src/app/session-detail/session-detail-page.component.spec.ts",
        "exit_code": 1,
        "observed": "keydown Enter and Space did not issue a second GET (jsdom does not synthesize click). 2 failed, 7 passed.",
        "discovered": 9,
        "passed": 7,
        "failed": 2
      },
      "green": "written",
      "triangulate": "passed",
      "refactor": "not-needed",
      "provenance": {
        "source": "working-tree",
        "commit": "working-tree",
        "test_file": "src/monitoring-web/src/app/session-detail/session-detail-page.component.spec.ts",
        "test_digest": "sha256:e7df6c5c5876e55d552d5efaf820c6e41882f46bee06c77d0ebcad3ac0790403",
        "command": "npx ng test --watch=false --include=src/app/session-detail/session-detail-page.component.spec.ts"
      }
    },
    {
      "task": "3.10",
      "test_file": "src/monitoring-web/src/app/session-detail/session-detail-page.component.spec.ts",
      "test_name": "SessionDetailPage",
      "layer": "unit",
      "red": "written",
      "green": {
        "command": "npx ng test --watch=false --include=src/app/session-detail/session-detail-page.component.spec.ts",
        "exit_code": 0,
        "discovered": 9,
        "passed": 9,
        "failed": 0
      },
      "triangulate": "passed",
      "refactor": "passed",
      "notes": "type=button with click, keydown.enter, keydown.space.",
      "provenance": {
        "source": "working-tree",
        "commit": "working-tree",
        "test_file": "src/monitoring-web/src/app/session-detail/session-detail-page.component.html",
        "test_digest": "sha256:c39e92d6ff5643f5ec23d6c7c67d510f6627dcf77d0b8418342ed046782773dd",
        "command": "npx ng test --watch=false --include=src/app/session-detail/session-detail-page.component.spec.ts"
      }
    },
    {
      "task": "3.11",
      "test_file": "src/monitoring-web/src/app/session-detail/session-detail-page.component.spec.ts",
      "test_name": "SessionDetailPage",
      "layer": "unit",
      "red": "written",
      "green": {
        "command": "npx ng test --watch=false --include=src/app/session-detail/session-detail-page.component.spec.ts",
        "exit_code": 0,
        "observed": "Chain spec 200 then 404 then 401 passed on first execution; prior cycles already implemented the three states.",
        "discovered": 10,
        "passed": 10,
        "failed": 0
      },
      "triangulate": "passed",
      "refactor": "passed",
      "notes": "Aggregation spec in the same page suite. No additional production code.",
      "provenance": {
        "source": "working-tree",
        "commit": "working-tree",
        "test_file": "src/monitoring-web/src/app/session-detail/session-detail-page.component.spec.ts",
        "test_digest": "sha256:e7df6c5c5876e55d552d5efaf820c6e41882f46bee06c77d0ebcad3ac0790403",
        "command": "npx ng test --watch=false --include=src/app/session-detail/session-detail-page.component.spec.ts"
      }
    },
    {
      "task": "3.12",
      "test_file": "src/monitoring-web/src/app/session-detail/session-detail-page.component.spec.ts",
      "test_name": "SessionDetailPage",
      "layer": "unit",
      "red": "written",
      "green": {
        "command": "npx ng test --watch=false --include=src/app/session-detail/session-detail-page.component.spec.ts",
        "exit_code": 0,
        "observed": "Page include 10 passed. Glob session-detail 12 passed. npm --prefix src/monitoring-web test 14 passed 0 failed. npm 12 rejects extra -- --watch=false.",
        "discovered": 10,
        "passed": 10,
        "failed": 0
      },
      "triangulate": "not-needed",
      "refactor": "not-needed",
      "provenance": {
        "source": "working-tree",
        "commit": "working-tree",
        "test_file": "src/monitoring-web/src/app/session-detail/session-detail-page.component.spec.ts",
        "test_digest": "sha256:e7df6c5c5876e55d552d5efaf820c6e41882f46bee06c77d0ebcad3ac0790403",
        "command": "npm --prefix src/monitoring-web test"
      }
    },
    {
      "task": "4.1",
      "test_file": "src/monitoring-web/src/app/session-detail/session-detail-view-chain.spec.ts",
      "test_name": "SessionDetail fixture-to-view chain",
      "layer": "integration",
      "safety_net": {
        "command": "npm --prefix src/monitoring-web test",
        "exit_code": 0,
        "discovered": 14,
        "passed": 14,
        "failed": 0
      },
      "red": {
        "command": "npx ng test --watch=false --include=src/app/session-detail/session-detail-view-chain.spec.ts",
        "exit_code": 1,
        "observed": "Stub SessionViewChainHost Main returned 1 without VIEW_CHAIN_READY. 1 suite failed, 1 test skipped.",
        "discovered": 1,
        "passed": 0,
        "failed": 1
      },
      "green": "written",
      "triangulate": "not-needed",
      "refactor": "not-needed",
      "notes": "Isolated spec file. Page include remains 10/10 without Docker. GenerateProgramFile false and StartupObject set so dotnet run does not launch xunit.",
      "provenance": {
        "source": "working-tree",
        "commit": "working-tree",
        "test_file": "src/monitoring-web/src/app/session-detail/session-detail-view-chain.spec.ts",
        "test_digest": "sha256:e63f95bd65ce83d41505e4feb410ba87923e528d38a3f768a3049675580ddcfb",
        "command": "npx ng test --watch=false --include=src/app/session-detail/session-detail-view-chain.spec.ts"
      }
    },
    {
      "task": "4.2",
      "test_file": "src/monitoring-web/src/app/session-detail/session-detail-view-chain.spec.ts",
      "test_name": "SessionDetail fixture-to-view chain",
      "layer": "integration",
      "red": "written",
      "green": {
        "command": "npx ng test --watch=false --include=src/app/session-detail/session-detail-view-chain.spec.ts",
        "exit_code": 0,
        "observed": "SessionViewChainHost: PostgresFixture, POST ACK 200 empty, worker poll GET 10s, UseKestrel(0) before CreateClient, VIEW_CHAIN_READY. First run DirectoryNotFoundException for {cwd}/Monitoring.Host; UseContentRoot via Monitoring.slnx fixed it.",
        "discovered": 1,
        "passed": 1,
        "failed": 0
      },
      "triangulate": "not-needed",
      "refactor": "passed",
      "notes": "UseKestrel(int) returns void. Only ITrustedSensorIdentityProvider is substituted. No ProjectAsync.",
      "provenance": {
        "source": "working-tree",
        "commit": "working-tree",
        "test_file": "tests/Monitoring.Tests/SessionViewChainHost.cs",
        "test_digest": "sha256:f6eeca8602df6d445c9b8babeaa32a6de017ce59f427c99d2ae9676424c55c77",
        "command": "npx ng test --watch=false --include=src/app/session-detail/session-detail-view-chain.spec.ts"
      }
    },
    {
      "task": "4.3",
      "test_file": "src/monitoring-web/src/app/session-detail/session-detail-view-chain.spec.ts",
      "test_name": "SessionDetail fixture-to-view chain",
      "layer": "integration",
      "safety_net": {
        "command": "npx ng test --watch=false --include=src/app/session-detail/session-detail-page.component.spec.ts",
        "exit_code": 0,
        "discovered": 10,
        "passed": 10,
        "failed": 0
      },
      "red": "written",
      "green": {
        "command": "npx ng test --watch=false --include=src/app/session-detail/session-detail-view-chain.spec.ts",
        "exit_code": 0,
        "observed": "Real GET via provideHttpClient(withFetch()) and SESSION_DETAIL_API_ORIGIN. Five dt rows and ValidData fields. Page token factory remains empty string.",
        "discovered": 1,
        "passed": 1,
        "failed": 0
      },
      "triangulate": "not-needed",
      "refactor": "not-needed",
      "provenance": {
        "source": "working-tree",
        "commit": "working-tree",
        "test_file": "src/monitoring-web/src/app/session-detail/session-detail-view-chain.spec.ts",
        "test_digest": "sha256:e63f95bd65ce83d41505e4feb410ba87923e528d38a3f768a3049675580ddcfb",
        "command": "npx ng test --watch=false --include=src/app/session-detail/session-detail-view-chain.spec.ts"
      }
    },
    {
      "task": "4.4",
      "test_file": "src/monitoring-web/src/app/session-detail/session-detail-view-chain.spec.ts",
      "test_name": "SessionDetail fixture-to-view chain",
      "layer": "integration",
      "red": "written",
      "green": {
        "command": "npm --prefix src/monitoring-web test",
        "exit_code": 0,
        "observed": "dotnet run --project tests/Monitoring.Tests --no-build -- --view-chain from repo root: VIEW_CHAIN_READY http://localhost:5000 event, stdin close, exit 0. npm --prefix src/monitoring-web test: 4 files, 15 passed, 0 failed. npm 12 rejects extra -- --watch=false.",
        "discovered": 15,
        "passed": 15,
        "failed": 0
      },
      "triangulate": "not-needed",
      "refactor": "not-needed",
      "provenance": {
        "source": "working-tree",
        "commit": "working-tree",
        "test_file": "src/monitoring-web/src/app/session-detail/session-detail-view-chain.spec.ts",
        "test_digest": "sha256:e63f95bd65ce83d41505e4feb410ba87923e528d38a3f768a3049675580ddcfb",
        "command": "dotnet run --project tests/Monitoring.Tests --no-build -- --view-chain"
      }
    },
    {
      "task": "5.1",
      "test_file": "tests/Monitoring.Tests/VistaAngularDeliveryTests.cs",
      "test_name": "Monitoring.Tests.VistaAngularDeliveryTests.OpenspecConfigDeclaresAngularNpmAndUiGate",
      "layer": "unit",
      "safety_net": {
        "command": "dotnet test Monitoring.slnx --filter \"FullyQualifiedName~HostStartupTests|FullyQualifiedName~DomainDependencyTests\"",
        "exit_code": 0,
        "discovered": 2,
        "passed": 2,
        "failed": 0
      },
      "red": {
        "command": "dotnet test Monitoring.slnx --filter FullyQualifiedName~VistaAngularDeliveryTests",
        "exit_code": 1,
        "observed": "package_managers still [dotnet] only; npm, angular 22.2.0, and quality_gates.ui absent.",
        "discovered": 1,
        "passed": 0,
        "failed": 1
      },
      "green": {
        "command": "dotnet test Monitoring.slnx --filter FullyQualifiedName~VistaAngularDeliveryTests",
        "exit_code": 0,
        "discovered": 1,
        "passed": 1,
        "failed": 0
      },
      "triangulation": "npm in package_managers; angular 22.2.0 capability; ui gate required/halt/300000; commands.test remains dotnet test Monitoring.slnx.",
      "refactor": "not-needed",
      "provenance": {
        "source": "working-tree",
        "commit": "working-tree",
        "test_file": "tests/Monitoring.Tests/VistaAngularDeliveryTests.cs",
        "test_digest": "sha256:7a027e4dad524f70a318a4b55a7bfc2da5f67dd555c348f7752459415e60e45a",
        "command": "dotnet test Monitoring.slnx --filter FullyQualifiedName~VistaAngularDeliveryTests"
      }
    },
    {
      "task": "5.2",
      "test_file": "tests/Monitoring.Tests/VistaAngularDeliveryTests.cs",
      "test_name": "Monitoring.Tests.VistaAngularDeliveryTests.CiWorkflowPinsNodeAndRunsAngularTests",
      "layer": "unit",
      "red": {
        "command": "dotnet test Monitoring.slnx --filter FullyQualifiedName~VistaAngularDeliveryTests",
        "exit_code": 1,
        "observed": "ci.yml had no node-version 24.16.0. 1 failed, 1 passed (5.1).",
        "discovered": 2,
        "passed": 1,
        "failed": 1
      },
      "green": {
        "command": "dotnet test Monitoring.slnx --filter FullyQualifiedName~VistaAngularDeliveryTests",
        "exit_code": 0,
        "discovered": 2,
        "passed": 2,
        "failed": 0
      },
      "triangulate": "passed",
      "refactor": "not-needed",
      "notes": "Node 24.16.0, cache npm + lockfile, npm ci, npm test in src/monitoring-web, same job as Docker-backed dotnet test. No extra --watch=false.",
      "provenance": {
        "source": "working-tree",
        "commit": "working-tree",
        "test_file": ".github/workflows/ci.yml",
        "test_digest": "sha256:c2c1f600d2adeb4cc6d7a7b6000e973e64cab318ee3176f9c80c4bd87b641358",
        "command": "dotnet test Monitoring.slnx --filter FullyQualifiedName~VistaAngularDeliveryTests"
      }
    },
    {
      "task": "5.3",
      "test_file": "tests/Monitoring.Tests/VistaAngularDeliveryTests.cs",
      "test_name": "Monitoring.Tests.VistaAngularDeliveryTests.ReadmeDocumentsProxyTrustedScopeAndNonPublication",
      "layer": "unit",
      "red": {
        "command": "dotnet test Monitoring.slnx --filter FullyQualifiedName~ReadmeDocumentsProxyTrustedScopeAndNonPublication",
        "exit_code": 1,
        "observed": "README still described Angular S04 as a later slice; src/monitoring-web missing.",
        "discovered": 1,
        "passed": 0,
        "failed": 1
      },
      "green": {
        "command": "dotnet test Monitoring.slnx --filter FullyQualifiedName~VistaAngularDeliveryTests",
        "exit_code": 0,
        "discovered": 3,
        "passed": 3,
        "failed": 0
      },
      "triangulate": "passed",
      "refactor": "not-needed",
      "provenance": {
        "source": "working-tree",
        "commit": "working-tree",
        "test_file": "README.md",
        "test_digest": "sha256:439aafd995922783137ffa0bf778ca57888fbebba3b172db44308f9cc753f592",
        "command": "dotnet test Monitoring.slnx --filter FullyQualifiedName~VistaAngularDeliveryTests"
      }
    },
    {
      "task": "5.4",
      "test_file": "tests/Monitoring.Tests/VistaAngularDeliveryTests.cs",
      "test_name": "Monitoring.Tests.VistaAngularDeliveryTests.PublishedHostDoesNotIncludeWwwrootOrMonitoringWebArtifacts",
      "layer": "integration",
      "red": {
        "command": "dotnet test Monitoring.slnx --filter FullyQualifiedName~VistaAngularDeliveryTests",
        "exit_code": 1,
        "observed": "CiWorkflowPublishesHostAndRejectsSpaTree failed: no dotnet publish step. PublishedHostDoesNotIncludeWwwrootOrMonitoringWebArtifacts already passed (0 wwwroot/monitoring-web hits). 1 failed, 4 passed.",
        "discovered": 5,
        "passed": 4,
        "failed": 1
      },
      "green": {
        "command": "dotnet test Monitoring.slnx --filter FullyQualifiedName~VistaAngularDeliveryTests",
        "exit_code": 0,
        "discovered": 5,
        "passed": 5,
        "failed": 0
      },
      "triangulate": "passed",
      "refactor": "not-needed",
      "notes": "No host csproj change. CI publish + find wwwroot/monitoring-web; local publish tree empty.",
      "provenance": {
        "source": "working-tree",
        "commit": "working-tree",
        "test_file": "tests/Monitoring.Tests/VistaAngularDeliveryTests.cs",
        "test_digest": "sha256:7a027e4dad524f70a318a4b55a7bfc2da5f67dd555c348f7752459415e60e45a",
        "command": "dotnet test Monitoring.slnx --filter FullyQualifiedName~VistaAngularDeliveryTests"
      }
    },
    {
      "task": "5.5",
      "test_file": "tests/Monitoring.Tests/SessionHostTests.cs",
      "test_name": "Monitoring.Tests.SessionHostTests",
      "layer": "integration",
      "red": "not-needed",
      "green": {
        "command": "dotnet test Monitoring.slnx",
        "exit_code": 0,
        "observed": "Full slnx 122 passed 0 failed ~19s. SessionHostTests filter --no-build: 8 passed 0 failed. Docker available.",
        "discovered": 122,
        "passed": 122,
        "failed": 0
      },
      "triangulate": "not-needed",
      "refactor": "not-needed",
      "notes": "Verify task. REQ-detalle-sesion-ambito-001 still green.",
      "provenance": {
        "source": "working-tree",
        "commit": "working-tree",
        "test_file": "tests/Monitoring.Tests/SessionHostTests.cs",
        "test_digest": "sha256:c13472dc98dc242edb5d261365c30ed01f28694d8a368855e9d7925582f2eb03",
        "command": "dotnet test Monitoring.slnx"
      }
    }
  ],
  "functional_snapshot": {
    "projection": "strict-tdd-functional-v1",
    "base_tree": "1eae9e14f03c9050eb74657d99e03f791bb402d0",
    "genesis_paths": [
      "src/Monitoring.Host/Program.cs",
      "src/Monitoring.Host/Sessions/TrustedSessionReadScope.cs",
      "tests/Monitoring.Tests/TrustedSessionReadScopeTests.cs"
    ],
    "files": [
      {
        "path": "src/Monitoring.Host/Program.cs",
        "digest": "sha256:8e76e5c464a021c124fc96077cf58db6d0201604863a2560ce9ab843d70da5f4"
      },
      {
        "path": "src/Monitoring.Host/Sessions/TrustedSessionReadScope.cs",
        "digest": "sha256:fa3d1bc5ffb1e97037e68d0999b39858defa485e9ff3aa792160e3da2099509d"
      },
      {
        "path": "tests/Monitoring.Tests/TrustedSessionReadScopeTests.cs",
        "digest": "sha256:dc26ea0ae7f769889044593cedb945338efa3293a955a22f34b295100d3f1d14"
      },
      {
        "path": "tests/Monitoring.Tests/VistaAngularDeliveryTests.cs",
        "digest": "sha256:7a027e4dad524f70a318a4b55a7bfc2da5f67dd555c348f7752459415e60e45a"
      },
      {
        "path": "openspec/config.yaml",
        "digest": "sha256:a5b62ec8c94b6a4276b2faeb1f09c96043e6bb7776bde1510170306aaa8c224b"
      },
      {
        "path": ".github/workflows/ci.yml",
        "digest": "sha256:c2c1f600d2adeb4cc6d7a7b6000e973e64cab318ee3176f9c80c4bd87b641358"
      },
      {
        "path": "README.md",
        "digest": "sha256:439aafd995922783137ffa0bf778ca57888fbebba3b172db44308f9cc753f592"
      }
    ]
  }
}
```

## Remediation after verify FAIL — publish-test deadlock

The re-verify `tests` gate aborted at 180000 ms with no summary because `PublishedHostDoesNotIncludeWwwrootOrMonitoringWebArtifacts` blocked inside sequential `StandardOutput.ReadToEnd()` then `StandardError.ReadToEnd()`. Blame identified that test after 121 others had passed. The fact now reads both streams concurrently and cancels the publish at 120 s, matching `MigrationProcess`. Assertions are unchanged. Follow-up `dotnet test Monitoring.slnx` exited 0 with 122 passed in about 18 s. The functional snapshot digest above for `VistaAngularDeliveryTests.cs` predates this edit.

## Remediation S04-C001 — CI fail-fast observation

Added `CiVerifyJobStopsBeforePublishWhenAngularViewCheckFails`. It reads the verify-job span (`npm test` in `src/monitoring-web` before `Publish host`, no `continue-on-error`), runs `npx ng test --watch=false --include=src/app/this-file-does-not-exist.spec.ts` so the view-check runner fails, and uses shell fail-fast (`&&`) so the publish-position sentinel is not created. Stdout and stderr are read concurrently with `ReadToEndAsync` before wait; the process tree is killed after 120 s. `CiWorkflowPinsNodeAndRunsAngularTests` stayed green. `verify_lineage.status` is `recheck-pending` after attempt 1.
