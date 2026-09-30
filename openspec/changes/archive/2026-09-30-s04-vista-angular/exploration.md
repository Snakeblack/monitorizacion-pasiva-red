## Exploration: S04 — vista Angular de extremo a extremo

### Current State

El repositorio cierra S01–S03: solución .NET (`Monitoring.slnx`), host en `src/Monitoring.Host`, pruebas en `tests/Monitoring.Tests` con PostgreSQL desechable (Testcontainers) y CI en `.github/workflows/ci.yml` limitado a `dotnet restore/build/test`. No hay carpeta Angular, `package.json` ni pasos Node en CI. `openspec/config.yaml` declara TypeScript/Angular en stack propuesto pero sin comandos de install/build/test frontend.

La API de detalle ya existe: `GET /api/v1/sessions/{eventId}` en `SessionEndpoint.cs` devuelve `SessionDetail` (cinco campos) con 200/404 según ámbito y 401 sin contexto. Solo responde en `Development` o `Testing`; en `Production` u otros entornos devuelve 401 antes de leer. El ámbito proviene de `ITrustedSessionReadContextFeature` en `HttpContext`, resuelto por `HostContextTrustedSessionReadContextProvider`; headers, query e identidad de ingestión no autorizan lectura (cubierto en `SessionHostTests`). El host público en `Program.cs` no registra middleware de contexto ni sirve estáticos/CORS: las pruebas inyectan contexto vía `WebApplicationFactory` o feature en servidor de prueba.

El fixture sintético está documentado en `README.md` y materializado en `SyntheticSessionContractTests.ValidData`; flujos ACK → proyección → detalle ya se ejercitan en .NET (`SessionHostTests`, `AckPrecedesControlledProjectionAndResendDoesNotConsumeQuotaAgain`). La UI debe mostrar detalle, vacío (404) y error (401/red), con perfil visual híbrido `linear-attio-ui` (chrome sobrio + superficies de datos densas; tokens semánticos, sin hero marketing; conviene sembrar `DESIGN.md` desde la plantilla de la skill porque no existe en el repo).

### Affected Areas

- `docs/development/slices.md` — criterios S04 (fixture, runner frontend, CI, integración vertical, acceso solo desarrollo).
- `openspec/specs/detalle-sesion-ambito/spec.md` — contrato HTTP y prohibición de usar parámetros del cliente como ámbito.
- `README.md` — contrato de ingestión/sesión sintética y límites del endpoint de detalle.
- `src/Monitoring.Host/Program.cs` y `Sessions/*` — posible extensión dev-only para contexto de lectura navegable y/o estáticos; sin tocar guardias de entorno S03.
- `Monitoring.slnx` — opcional referencia documental; Angular no encaja como proyecto MSBuild clásico.
- `.github/workflows/ci.yml` — nuevos pasos Node/Angular y prueba de integración.
- Área nueva (p. ej. `src/monitoring-web/`) — SPA, servicios HTTP, componentes detalle/vacío/error, pruebas de componente.

### Approaches

1. **SPA en `src/monitoring-web/` + API en host existente (desacoplada en dev)**
   - Descripción: Aplicación Angular standalone en carpeta hermana de `Monitoring.Host`; en local `ng serve` con `proxy.conf.json` hacia `http://localhost:5080`; build de producción como artefacto estático (sin acoplar aún al host).
   - Pros: Respeta hexagonal (UI solo HTTP); alinea con doc “Angular conectada exclusivamente a la API”; revisión y CI frontend independientes del compile .NET.
   - Cons: Cross-origin en dev exige CORS en API o proxy; el contexto de lectura S03 no llega al navegador sin un mecanismo servidor adicional (p. ej. middleware Development que fija ámbito desde configuración, no desde petición).
   - Effort: Medium.

2. **SPA servida por `Monitoring.Host` solo en Development**
   - Descripción: Publicar `dist/` bajo `wwwroot` con `UseStaticFiles`/`MapFallbackToFile` condicionado a `IsDevelopment()`.
   - Pros: Same-origin simplifica cookies futuras y evita CORS en dev; un solo puerto para demo vertical.
   - Cons: Mezcla despliegue UI en el host antes de S12; riesgo de filtrar estáticos si el condicional de entorno falla; no reduce la necesidad de contexto de lectura confiable en servidor.
   - Effort: Medium–High (más superficie en host).

3. **Monorepo Nx o workspace npm en raíz**
   - Pros: Escalable si llegan más frontends.
   - Cons: Sin segundo frontend ni convención previa; sobredimensiona S04 y el presupuesto 400 líneas.
   - Effort: High.

**Llamada a `GET /api/v1/sessions/{eventId}` desde Angular**

| Enfoque | Pros | Cons |
|--------|------|------|
| Servicio `HttpClient` + tipos TS espejo de JSON API | Simple, testeable con `HttpTestingController` | Duplicación controlada de forma de respuesta (aceptable en borde UI) |
| OpenAPI codegen | Contrato único | No hay OpenAPI publicado hoy; coste extra S04 |
| BFF Angular | Podría inyectar contexto | Viola separación y duplica API |

**Acceso solo desarrollo**

| Enfoque | Pros | Cons |
|--------|------|------|
| Confiar en guard S03 (401 fuera Dev/Testing) + no empaquetar UI en imagen prod | Mínimo cambio backend | Navegador manual aún necesita contexto de lectura en Dev |
| Middleware/config Development-only que establece `ITrustedSessionReadContextFeature` desde `appsettings.Development.json` | Compatible con spec (no headers cliente); habilita UI real | Cambio acotado en host; debe quedar explícitamente fuera de Production |
| Simular ámbito en UI (query/localStorage) | Rápido para demo | **Inviable**: contradice `REQ-detalle-sesion-ambito-001/002` |

**Runner de pruebas frontend**

| Enfoque | Pros | Cons |
|--------|------|------|
| `@angular/build:unit-test` (Vitest integrado en CLI reciente) | Alineado con skill `angular-vitest-testing`; sin tercer Vitest | Requiere fijar versión Angular/CLI en propuesta |
| Karma/Jasmine legacy | Conocido | Desalineado con stack moderno del proyecto |
| Jest + jest-preset-angular | Alternativa | Segunda pila de test innecesaria si el builder oficial basta |

**Integración fixture → ACK → worker → API → UI en CI**

| Enfoque | Pros | Cons |
|--------|------|------|
| A) Mantener cadena .NET existente + pruebas Angular de componente/servicio con mocks para estados UI | Bajo riesgo; reutiliza `PostgresFixture` y `WebApplicationFactory` | La palabra “UI” en aceptación exige al menos un test que ejecute código Angular contra respuestas reales o E2E breve |
| B) Playwright (o similar) contra host Testing + build Angular servido por test host | Demuestra vertical incluyendo render | Más flaky/tiempo; Docker + browser en CI |
| C) Test .NET que levanta host con contexto + script npm “integration” que llama API y aserta contrato consumido por servicio Angular exportado | Puente híbrido | Menos estándar; documentar bien |

### Recommendation

Adoptar **Approach 1** (`src/monitoring-web/`) con servicio Angular `HttpClient` hacia la API, estados detalle/vacío/error en componentes standalone (OnPush, signals/`computed`, control flow moderno) y shell inicial tipo híbrido linear-attio (sidebar/view-header + panel de detalle denso). Para desarrollo manual y para integración real del navegador, planificar en diseño un **puente de contexto de lectura solo Development/Testing** en el host (p. ej. middleware que lee `TrustedSessionReadScope` de configuración y setea `ITrustedSessionReadContextFeature`, nunca desde headers/query del cliente), respetando el ADR/spec S03.

Pruebas: **`ng test` con `@angular/build:unit-test`** para componente(s) de detalle (200 con fixture, 404 vacío, 401/5xx error accesible); integración en CI ampliando el job actual con setup Node LTS, `npm ci`, test headless Angular, y **una prueba vertical .NET existente ampliada o hermana** que ya hace fixture→ACK→worker→GET detalle, más **un E2E mínimo (Playwright)** que cargue la ruta de detalle contra `WebApplicationFactory` en entorno `Testing` con contexto inyectado—solo si el presupuesto lo permite; si no, component test + integración API documentada como parcial hasta tasks.

CI: secuencia `dotnet test` + paso frontend; job opcional con `ASPNETCORE_ENVIRONMENT=Production` assert 401 en sesiones; no publicar artefactos UI en pipeline de “prod”. Actualizar `openspec/config.yaml` en apply con comandos frontend acordados (Strict TDD exige runner antes de código productivo).

### Risks

- Sin mecanismo servidor de contexto de lectura en Development, la UI en navegador real siempre verá 401 aunque la API funcione en tests—bloqueante para demo vertical si no se diseña en S04.
- CORS/proxy mal configurados pueden ocultar 401/404 reales o mezclar ámbitos en depuración.
- Añadir Node/Angular + posible Playwright incrementa tiempo de CI y riesgo de superar 400 líneas; conviene encadenar tareas en `sdd-tasks`.
- Servir estáticos desde el host sin guard estricto de entorno podría exponer UI antes de S12/S13.
- `DESIGN.md` ausente: riesgo de deriva visual respecto al perfil híbrido reutilizable en S09–S12.

### Ready for Proposal

**Sí.** El alcance S04, dependencias S03 y contratos baseline están claros. La propuesta debe fijar ubicación `src/monitoring-web/`, versión Angular/CLI, estrategia del puente de contexto dev-only, runner `@angular/build:unit-test`, forma concreta de la prueba integrada UI (E2E mínimo vs. división componente + .NET), semilla de `DESIGN.md` híbrido y actualización de CI/`openspec/config.yaml`. OIDC, listado 30 días y captura real permanecen explícitamente fuera.
