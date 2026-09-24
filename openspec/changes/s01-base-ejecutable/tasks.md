# Tasks: S01 — base ejecutable

## Spec/Design Reconciliation

| Requirement / Scenario | Priority | Design Allocation | Status | Notes |
|------------------------|----------|-------------------|--------|-------|
| REQ-base-ejecutable-001: checkout limpio arranca y dominio aislado | MUST | `Monitoring.slnx`, `src/Monitoring.Domain/**`, `src/Monitoring.Host/**`, `tests/Monitoring.Tests/**` | covered-by-design | Liveness sin funciones posteriores; prueba de dependencias. |
| REQ-base-ejecutable-002: migración inicial, repetición e indisponibilidad | MUST | `src/Monitoring.Persistence/**`, comando `--migrate` en `src/Monitoring.Host/**`, pruebas Testcontainers | covered-by-design | Esquema/historial mínimo; PostgreSQL provisional, sin reclamo de capacidad. |
| REQ-base-ejecutable-003: límite del slice S01 | MUST | Host, dominio, migración y pruebas; revisión de esquema y rutas | covered-by-design | Excluye eventos, ingestión, worker, API funcional y UI. |
| REQ-verificacion-base-001: runner .NET y Postgres efímero desde cero | MUST | `tests/Monitoring.Tests/**`, `global.json`, `Monitoring.slnx`, `README.md` | covered-by-design | `dotnet test Monitoring.slnx`; requiere Docker. |
| REQ-verificacion-base-002: CI limpia y falla ante error | MUST | `.github/workflows/ci.yml` | covered-by-design | Restore, build y test en GitHub Actions con Docker. |
| REQ-verificacion-base-003: prerequisitos y comandos reproducibles | MUST | `README.md`, `openspec/config.yaml` | covered-by-design | Documenta provisionalidad de PostgreSQL y alcance de verificación. |

### Reconciliation Verdict
- MUST coverage: complete
- SHOULD/MAY gaps: none
- Ambiguities to track: none; SDK 10, xUnit, Testcontainers PostgreSQL, GitHub Actions y comando de migración están fijados en el diseño.

## Review Workload Forecast

| Field | Value |
|-------|-------|
| Estimated changed lines | 650–900 total; each planned PR slice targets ≤400 changed lines |
| 400-line budget risk | High |
| Chained PRs recommended | Yes |
| Suggested split | PR 1 runner/solution; PR 2 host y frontera de dominio; PR 3 persistencia/migración; PR 4 integración, CI y documentación |
| Delivery strategy | auto-chain |
| Chain strategy | feature-branch-chain |

Decision needed before apply: No
Chained PRs recommended: Yes
Chain strategy: feature-branch-chain
400-line budget risk: High

## Suggested Work Units

| Unit | Goal | Likely PR | Notes |
|------|------|-----------|-------|
| 1 | Instalar estructura de solución, SDK y runner xUnit; dejar una prueba de arquitectura roja antes de producción | PR 1 | Base: rama feature/tracker S01; dotnet restore/build/test disponibles. |
| 2 | Añadir dominio independiente y host mínimo con liveness, guiados por pruebas rojas | PR 2 | Base: rama PR 1; arranque limpio y referencias comprobadas. |
| 3 | Añadir persistencia EF/Npgsql y migración explícita idempotente, guiadas por pruebas Testcontainers rojas | PR 3 | Base: rama PR 2; PostgreSQL efímero y prueba de indisponibilidad. |
| 4 | Completar CI limpia y README/config; ejecutar suite completa | PR 4 | Base: rama PR 3; restore/build/test en CI y comandos reproducibles. |

### Chained PR Verification

Cada slice se revisa con diff limitado a su unidad y comandos focales; las PR 2–4 apuntan a la rama inmediata anterior y se retargetean/rebasan si el diff incluye cambios ya presentados por la PR padre. Auto-chain permite iniciar la primera unidad sin decisión pendiente; continuar cada slice depende de la verificación local de su anterior.

## Checklist Status Legend

- `[ ]` Not implemented yet
- `[~]` Implemented but not yet verified locally
- `[x]` Implemented and verified locally

## Phase 1: Runner y solución

- [x] 1.1 Crear `global.json`, `Monitoring.slnx` y `Directory.Build.props` para SDK .NET 10 y convenciones reproducibles; confirmar `dotnet --version` y `dotnet restore Monitoring.slnx`. [REQ-verificacion-base-001]
- [x] 1.2 Crear `tests/Monitoring.Tests/Monitoring.Tests.csproj` con xUnit, `Microsoft.NET.Test.Sdk` y referencias de arquitectura; escribir primero prueba roja que exija la frontera de dominio y ejecutar `dotnet test Monitoring.slnx --filter FullyQualifiedName~DomainDependency`. [REQ-base-ejecutable-001]
- [x] 1.3 Registrar `dotnet test Monitoring.slnx` como runner/capacidad detectada en `openspec/config.yaml` solo cuando el runner esté instalado y ejecutable; verificar `dotnet test Monitoring.slnx`. [REQ-verificacion-base-001]

## Phase 2: Dominio y host mínimo

- [x] 2.1 Crear `src/Monitoring.Domain/Monitoring.Domain.csproj` sin referencias de persistencia; hacer pasar la prueba de frontera con `dotnet test Monitoring.slnx --filter FullyQualifiedName~DomainDependency`. [REQ-base-ejecutable-001]
- [ ] 2.2 Crear pruebas rojas de arranque/liveness en `tests/Monitoring.Tests/HostStartupTests.cs`; ejecutar `dotnet test Monitoring.slnx --filter FullyQualifiedName~HostStartup` antes de implementar el host. [REQ-base-ejecutable-001]
- [ ] 2.3 Crear `src/Monitoring.Host/Monitoring.Host.csproj` y `Program.cs` con composición y `GET /health/live`; pasar la prueba de arranque con el filtro focal y después la suite. [REQ-base-ejecutable-001, REQ-base-ejecutable-003]

## Phase 3: Persistencia y migración

- [ ] 3.1 Configurar xUnit/Testcontainers PostgreSQL en `tests/Monitoring.Tests/Monitoring.Tests.csproj` y fixture aislada desechable en `tests/Monitoring.Tests/PostgresFixture.cs`; demostrar prueba roja de esquema ausente. [REQ-verificacion-base-001]
- [ ] 3.2 Añadir prueba roja de primera migración y repetición en `tests/Monitoring.Tests/MigrationTests.cs`; verificar esquema `monitoring`, historial y ausencia de tablas funcionales contra base vacía. [REQ-base-ejecutable-002, REQ-base-ejecutable-003]
- [ ] 3.3 Añadir prueba roja de indisponibilidad y fallo visible sin filtrar credenciales en `tests/Monitoring.Tests/MigrationFailureTests.cs`; usar endpoint cerrado y timeout acotado. [REQ-base-ejecutable-002]
- [ ] 3.4 Crear `src/Monitoring.Persistence/Monitoring.Persistence.csproj`, `MonitoringDbContext.cs` y migración mínima; implementar `--migrate` en `src/Monitoring.Host/Program.cs` usando `ConnectionStrings:Monitoring`, sin migrar en arranque normal. Pasar pruebas focales y suite con `dotnet test Monitoring.slnx`. [REQ-base-ejecutable-002]

## Phase 4: CI y reproducción limpia

- [ ] 4.1 Crear `.github/workflows/ci.yml` con setup-dotnet 10 y pasos `dotnet restore Monitoring.slnx`, `dotnet build Monitoring.slnx --no-restore` y `dotnet test Monitoring.slnx --no-build`; comprobar que fallo de test hace fallar el job. [REQ-verificacion-base-002]
- [ ] 4.2 Documentar SDK, Docker, configuración local, migración explícita, liveness y comandos en `README.md`; declarar `build`/`test` en `openspec/config.yaml` tras comprobarlos. [REQ-verificacion-base-003]
- [ ] 4.3 Ejecutar desde checkout limpio `dotnet restore Monitoring.slnx`, `dotnet build Monitoring.slnx --no-restore` y `dotnet test Monitoring.slnx --no-build`; comprobar workflow y que la documentación identifica PostgreSQL como provisional, no validación de producción. [REQ-verificacion-base-001, REQ-verificacion-base-002, REQ-verificacion-base-003]
