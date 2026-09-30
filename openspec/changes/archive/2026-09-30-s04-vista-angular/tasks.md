# Tasks: Vista Angular del detalle de sesión

## Spec/Design Reconciliation

| Requirement / Scenario | Priority | Design Allocation | Status | Notes |
|------------------------|----------|-------------------|--------|-------|
| REQ-vista-detalle-sesion-001 / Sesión encontrada | MUST | `session-detail-page.component.ts`, specs HttpTestingController | covered-by-design | Texto de éxito además de color |
| REQ-vista-detalle-sesion-002 / Identificador ausente | MUST | Página: 404 → vacío | covered-by-design | Sin campos de sesión |
| REQ-vista-detalle-sesion-002 / Identificador de otro ámbito | MUST | API 404 (`TrustedSessionReadScopeTests` + página) | covered-by-design | Scope valida cuerpo ajeno |
| REQ-vista-detalle-sesion-003 / Sin contexto o entorno | MUST | Página error; host 401 | covered-by-design | No confundir con vacío |
| REQ-vista-detalle-sesion-003 / Fallo de red | MUST | Página status 0 u otro ≠404 | covered-by-design | |
| REQ-vista-detalle-sesion-004 / Petición sin ámbito | MUST | `session-detail-api.ts`, tests scope | covered-by-design | Servidor fija ámbito por config |
| REQ-vista-detalle-sesion-004 / Teclado | MUST | `<button type="button">`, enter/space | covered-by-design | jsdom no simula click por tecla |
| REQ-vista-detalle-sesion-005 / Tres estados | MUST | Spec agregado página | covered-by-design | 200, 404, 401/red |
| REQ-vista-detalle-sesion-005 / Fixture hasta la vista | MUST | `SessionViewChainHost.cs`, spec cadena | covered-by-design | GET real, sin mock HTTP |
| REQ-vista-detalle-sesion-005 / CI | MUST | `ci.yml`, puerta `ui` en config | covered-by-design | on_fail halt |
| REQ-vista-detalle-sesion-005 / Sin publicación | MUST | publish host + aserción árbol | covered-by-design | Sin wwwroot SPA |
| REQ-detalle-sesion-ambito-003 / Dev o Testing con config | MUST | `TrustedSessionReadScope.cs`, tests Theory | covered-by-design | Proveedor real |
| REQ-detalle-sesion-ambito-003 / Petición otro ámbito | MUST | `TrustedSessionReadScopeTests` cabeceras/query | covered-by-design | Config gana |
| REQ-detalle-sesion-ambito-003 / Production | MUST | Tests 401; middleware no registrado | covered-by-design | Feature nulo tras next |
| REQ-detalle-sesion-ambito-003 / Sin config Dev/Testing | MUST | Tests 401 | covered-by-design | |
| REQ-detalle-sesion-ambito-001 / encontrada, ausente, mismo id | MUST | `SessionHostTests` existente | covered-by-design | Apply: no romper; verify re-ejecuta |

### Reconciliation Verdict
- MUST coverage: complete
- SHOULD/MAY gaps: none
- Ambiguities to track: none

## Review Workload Forecast

| Field | Value |
|-------|-------|
| Estimated changed lines | 1100–1700 |
| 400-line budget risk | High |
| Chained PRs recommended | Yes |
| Suggested split | Un solo PR con excepción de tamaño (entrega acordada) |
| Delivery strategy | single-pr |
| Chain strategy | pending |

Decision needed before apply: Yes
Chained PRs recommended: Yes
Chain strategy: pending
400-line budget risk: High

### Suggested Work Units

| Unit | Goal | Likely PR | Notes |
|------|------|-----------|-------|
| 1 | Host ámbito por configuración + tests | PR único (size-exception) | Base para cadena real |
| 2 | SPA, TDD estados, cadena `--view-chain`, CI | mismo PR | Depende de unit 1 para integración |

### Checklist Status Legend

- `[ ]` Not implemented yet
- `[~]` Implemented but not yet verified locally
- `[x]` Implemented and verified locally

## Phase 1: Ámbito de lectura en el host (TDD)

- [x] 1.1 RED: crear `tests/Monitoring.Tests/TrustedSessionReadScopeTests.cs` con Theory Dev/Testing 200 y cinco campos, cabeceras/query ignoradas, Production 401, Dev/Testing sin claves 401, aserción de petición sin `X-Site-Id`/`X-Sensor-Id` [REQ-detalle-sesion-ambito-003]
- [x] 1.2 GREEN: implementar `src/Monitoring.Host/Sessions/TrustedSessionReadScope.cs` (opciones `TrustedSessionRead:SiteId`/`SensorId`, middleware `Set` feature, sin leer petición) [REQ-detalle-sesion-ambito-003]
- [x] 1.3 GREEN: registrar middleware en `src/Monitoring.Host/Program.cs` solo en Development y Testing [REQ-detalle-sesion-ambito-003]
- [x] 1.4 VERIFY: `dotnet test Monitoring.slnx --filter FullyQualifiedName~TrustedSessionReadScopeTests` en verde [REQ-detalle-sesion-ambito-003]

## Phase 2: Scaffold SPA y semilla visual

- [x] 2.1 Crear `src/monitoring-web/` con `npx --yes @angular/cli@22.2.0` (standalone, routing), proxy `ng serve` `/api` → `http://127.0.0.1:5080`, runner `@angular/build:unit-test` en `angular.json`
- [x] 2.2 Actualizar `.gitignore` (dependencias y `dist`); commitear lockfile npm
- [x] 2.3 Generar `DESIGN.md` en raíz desde plantilla `linear-attio-ui` (`assets/DESIGN.template.md`)
- [x] 2.4 Crear shell `src/monitoring-web/src/app/app.component.ts` (cabecera compacta, nav plegable «Detalle», `router-outlet`) y ruta `/sessions/:eventId` → `SessionDetailPage`
- [x] 2.5 Aplicar tokens tipográficos y neutros en `src/monitoring-web/src/styles.css` según `DESIGN.md`

## Phase 3: Cliente HTTP y estados de la ficha (TDD)

- [x] 3.1 RED: `session-detail-api.spec.ts` — GET a `/api/v1/sessions/{id}` sin cabeceras ni query de ámbito; falla sin implementación [REQ-vista-detalle-sesion-004]
- [x] 3.2 GREEN: `session-detail-api.ts` con `SessionDetail` y GET `${origin}/api/v1/sessions/${encodeURIComponent(eventId)}` [REQ-vista-detalle-sesion-004]
- [x] 3.3 RED: `session-detail-page.component.spec.ts` — HttpTestingController 200 muestra cinco campos y texto de éxito legible [REQ-vista-detalle-sesion-001]
- [x] 3.4 GREEN: `session-detail-page.component.ts` — `computed()` para vista éxito, loading «Consultando detalle», éxito «Sesión en ámbito» [REQ-vista-detalle-sesion-001]
- [x] 3.5 RED: specs 404 → vacío «No hay sesión en este ámbito», sin filas de ficha (ausente) [REQ-vista-detalle-sesion-002]
- [x] 3.6 GREEN: rama vacío en componente; otro ámbito cubierto por mismo 404 en spec adicional [REQ-vista-detalle-sesion-002]
- [x] 3.7 RED: specs 401 y status 0 → error «No se pudo consultar el detalle», sin campos [REQ-vista-detalle-sesion-003]
- [x] 3.8 GREEN: rama error + botón `Reintentar consulta` que repite GET [REQ-vista-detalle-sesion-003]
- [x] 3.9 RED: cada `button` activable con `keydown.enter` y `keydown.space` (sin depender de click simulado por tecla) [REQ-vista-detalle-sesion-004]
- [x] 3.10 GREEN: `type="button"`, handlers click/enter/space en controles interactivos [REQ-vista-detalle-sesion-004]
- [x] 3.11 RED: spec que encadena respuestas 200, 404 y 401 o red en la misma suite de página [REQ-vista-detalle-sesion-005]
- [x] 3.12 VERIFY: `npm --prefix src/monitoring-web test -- --watch=false` con filtro de specs de página en verde [REQ-vista-detalle-sesion-005]

## Phase 4: Cadena fixture → vista (integración real)

- [x] 4.1 RED: ajustar `tests/Monitoring.Tests/Monitoring.Tests.csproj` (`GenerateProgramFile` false, `StartupObject`) y spec de cadena que falla hasta existir host [REQ-vista-detalle-sesion-005]
- [x] 4.2 GREEN: `tests/Monitoring.Tests/SessionViewChainHost.cs` — `Main` para `--view-chain`, `PostgresFixture`, POST lote, ACK 200 vacío, worker, poll GET 10 s, emite `VIEW_CHAIN_READY` [REQ-vista-detalle-sesion-005]
- [x] 4.3 GREEN: spec cadena en `src/monitoring-web` — `beforeAll` 120 s, `provideHttpClient()` fetch, `SESSION_DETAIL_API_ORIGIN`, datos `SyntheticSessionContractTests.ValidData`, render GET real y cinco campos [REQ-vista-detalle-sesion-005]
- [x] 4.4 VERIFY: desde raíz con `Monitoring.slnx`, `dotnet run --project tests/Monitoring.Tests --no-build -- --view-chain` + suite npm cadena (documentado en diseño) [REQ-vista-detalle-sesion-005]

## Phase 5: CI, configuración y no publicación

- [x] 5.1 Actualizar `openspec/config.yaml`: capacidad `angular` 22.2.0, `npm` en `package_managers`, puerta `ui` (`npm --prefix src/monitoring-web test`, required, halt, 300000 ms) [REQ-vista-detalle-sesion-005]
- [x] 5.2 Actualizar `.github/workflows/ci.yml`: Node 24.16.0, caché, `dotnet test`, `npm ci`, `npm test` en `src/monitoring-web` [REQ-vista-detalle-sesion-005]
- [x] 5.3 Actualizar `README.md`: proxy local, variables de ámbito servidor, SPA no publicada en producción
- [x] 5.4 VERIFY: paso CI o test dotnet que `dotnet publish` del host no incluye `wwwroot` ni artefactos `monitoring-web` [REQ-vista-detalle-sesion-005]
- [x] 5.5 VERIFY: regresión `dotnet test Monitoring.slnx` completa; confirmar `SessionHostTests` sigue verde para REQ-detalle-sesion-ambito-001 [REQ-detalle-sesion-ambito-001]
