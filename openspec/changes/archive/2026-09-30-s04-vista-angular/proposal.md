# Proposal: Vista Angular del detalle de sesión

## Intent

Vista interna del detalle sintético, vacío y error: fixture → ACK → worker → API → UI.

## Scope

### In Scope
- SPA standalone en `src/monitoring-web/`.
- Detalle 200 (`eventId`, `siteId`, `sensorId`, `occurredAt`, `data`), vacío 404 y error (401 o red), con texto además del color.
- Perfil `linear-attio-ui` y semilla `DESIGN.md`.
- Puente de ámbito en Development/Testing, leído de configuración del servidor.
- Runner `@angular/build:unit-test`: componente e integración hasta la UI.
- CI con Node; la UI no se publica.

### Out of Scope
- S09 listado, S05 captura y S12 OIDC/RBAC.
- SPA en `Monitoring.Host` o en la imagen.
- OpenAPI, BFF, Playwright. Versión de Angular en diseño.

## Capabilities

### New Capabilities
- `vista-detalle-sesion`: detalle, vacío, error, runner, componente, integración hasta la UI y CI.

### Modified Capabilities
- `detalle-sesion-ambito`: Development/Testing instala el contexto desde configuración de servidor. La petición no fija el ámbito. Otros entornos responden 401.

## Approach

SPA en `src/monitoring-web/` con proxy a la API. Standalone, signals y `computed`. `HttpClient` espeja `SessionDetail`; el dominio no referencia la UI.

`Program.cs` instala `ITrustedSessionReadContextFeature` solo en Development o Testing, con sede y sonda de configuración. El guard de `SessionEndpoint` sigue igual.

Componente: 200, 404 y error. Integración en host Testing: fixture, ACK, worker y render del GET real. Sin Playwright: `testing.e2e` ausente y un solo PR.

## Affected Areas

| Area | Impact | Description |
|------|--------|-------------|
| `src/monitoring-web/` | New | SPA y pruebas |
| `DESIGN.md` | New | Semilla visual |
| `src/Monitoring.Host/Program.cs` | Modified | Puente dev/test |
| `src/Monitoring.Host/Sessions/` | Modified | Contexto de servidor |
| `.github/workflows/ci.yml` | Modified | Paso Node |
| `openspec/config.yaml` | Modified | Comandos UI |
| `README.md` | Modified | Uso local |

## Risks

| Risk | Likelihood | Mitigation |
|------|------------|------------|
| Puente fuera de dev/test | Med | Guard y prueba 401 |
| Proxy oculta 401/404 | Med | Prueba de tres estados |
| CI 15 min o diff > 400 | Med | Sin E2E de navegador |

## Rollback Plan

Quitar la SPA, `DESIGN.md`, el paso Node y el puente. API y 401 de S03 quedan. Sin migración.

## Dependencies

- S03: API, fixture y host.
- Node LTS; versión Angular en diseño.
- PostgreSQL de pruebas.
- Skill `linear-attio-ui`.

## Success Criteria

- [ ] Development muestra los cinco campos del ámbito de servidor.
- [ ] `eventId` ausente o ajeno muestra vacío, sin datos ajenos.
- [ ] Sin contexto, Production o fallo de red: error en vista y 401 en API.
- [ ] CI falla si fallan componente o cadena hasta la UI.
