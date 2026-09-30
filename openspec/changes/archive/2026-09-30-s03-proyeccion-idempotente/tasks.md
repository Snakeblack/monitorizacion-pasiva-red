# Tareas: S03 — proyección idempotente y lectura mínima

## Review Workload Forecast

Decision needed before apply: Yes
Chained PRs recommended: Yes
Chain strategy: pending
400-line budget risk: High

| Área | Líneas cambiadas estimadas (adiciones + eliminaciones) |
|---|---:|
| Domain, esquema y migración | 180–260 |
| Proyector y lector Persistence | 240–340 |
| Worker, contexto, endpoint y wiring Host | 180–260 |
| Pruebas nuevas y adaptación S02 | 570–800 |
| README y roadmap | 30–40 |
| Total código, pruebas y documentación | 1.200–1.700 |

Los artefactos OpenSpec/ADR también cuentan si entran en la PR; el total de revisión será superior. `delivery_strategy: single-pr` expresa preferencia, no aprobación de `size:exception`. El coordinador debe resolver la entrega antes de apply. No se ejecuta código en esta fase.

### Unidades sugeridas

| Unidad | Entregable verificable | Límite y reversión |
|---|---|---|
| U1 | Contrato Domain y pruebas | Parser aislado; revertir código |
| U2 | Esquema aditivo y pruebas | Sin consumidor; conservar migración/datos al revertir |
| U3 | Proyección inicial, replay y atomicidad | Invocación directa probada; sin wiring automático |
| U4 | Barrido paginado, concurrencia y recuperación | Ampliar U3; revertir algoritmo sin resetear marcas |
| U5 | Lector y detalle protegido | Depende U2/U3; revertir endpoint |
| U6 | Worker, wiring y documentación | Integración final; revertir worker conservando datos |

Cada unidad incluye sus pruebas y evidencia; subdividir si su diff supera 400 líneas. Alternativas: stacked-to-main integra en orden; feature-branch-chain conserva integración final en tracker (PR1 base tracker, siguientes base rama anterior); excepción conserva una PR con aprobación explícita.

## Spec/Design Reconciliation

| Requisito / escenarios | Prioridad | Asignación de diseño | Estado |
|---|---|---|---|
| contrato-sesion-sintetica-001: válido, límites, inválidos | MUST | Parser Domain estricto | covered-by-design |
| contrato-sesion-sintetica-002: data arbitrario aceptado | MUST | Validación posterior; ingestión intacta | covered-by-design |
| proyeccion-sesiones-idempotente-001: inicial, replay/concurrencia, orígenes | MUST | PK compuesta, bloqueo y comparación | covered-by-design |
| proyeccion-sesiones-idempotente-002: rollback, reinicio | MUST | Transacción por evento y scopes nuevos | covered-by-design |
| proyeccion-sesiones-idempotente-003: inválidos antes de válidos | MUST | Cursor, cota y siguiente pasada | covered-by-design |
| detalle-sesion-ambito-001: encontrado, ausente/ajeno, ID compartido | MUST | Lector parametrizado y DTO | covered-by-design |
| detalle-sesion-ambito-002: sin contexto, producción | MUST | Feature separado y guard del endpoint | covered-by-design |
| bandeja-ingestion-durable-004: aceptación, marcado, fallo, reenvío | MUST | processed_at y commit conjunto | covered-by-design |

Cobertura MUST completa; sin gaps SHOULD/MAY ni ambigüedades. ADR-001/002/003 y `design.md` fijan arquitectura. Sin cuarentena S08, acceso humano S12 ni capacidad acreditada S17.

## Evidencia Strict TDD

Para cada unidad: RED conductual antes del código → GREEN mínimo → refactor con pruebas verdes. Registrar en `apply-progress.md` requisito, prueba, comando, salida/estado y diff anterior/posterior; errores de compilación o infraestructura no sustituyen RED. Runner: `dotnet test Monitoring.slnx`; filtros focales por clase y suite completa al cerrar integración. PostgreSQL real mediante `tests/Monitoring.Tests/PostgresFixture.cs`, sincronización por condición acotada y contexts independientes. `[ ]` pendiente; `[~]` implementado sin verificar; `[x]` verificado localmente.

## Fase 1: Contrato y esquema (U1/U2)

- [x] 1.1 RED en `tests/Monitoring.Tests/SyntheticSessionContractTests.cs`: TCP/UDP, IPv4/IPv6, puertos extremos, fechas 0–3 decimales y cada campo ausente/adicional/tipo incorrecto, offset, exceso de precisión y fin anterior. [REQ-contrato-sesion-sintetica-001]
- [x] 1.2 GREEN en `src/Monitoring.Domain/Sessions/{SyntheticSessionContract,SessionDetail}.cs`: parser puro y DTO, conservar valores originales; refactor sin dependencia EF. [REQ-contrato-sesion-sintetica-001, REQ-detalle-sesion-ambito-001]
- [x] 1.3 RED en `tests/Monitoring.Tests/{SessionSchemaTests,MigrationTests,InboxSchemaTests}.cs`: migrar S02 con pendientes, repetir migración, PK/FK compuestas e índice parcial sin perder cuota/unicidad. [REQ-bandeja-ingestion-durable-004, REQ-proyeccion-sesiones-idempotente-001]
- [x] 1.4 GREEN: añadir `src/Monitoring.Persistence/Migrations/202609290003_SessionProjection.cs`, actualizar `MonitoringDbContext.cs`, `Ingestion/IngestionInboxEntity{,Configuration}.cs` y crear `Sessions/SessionProjectionEntity{,Configuration}.cs`; sin Down destructivo. [REQ-bandeja-ingestion-durable-004, REQ-proyeccion-sesiones-idempotente-001]

## Fase 2: Proyección durable (U3/U4)

- [x] 2.1 RED en `tests/Monitoring.Tests/SessionProjectionTests.cs`: inicial, replay igual, conflicto de contenido, ID entre sedes/sondas; trigger BEFORE UPDATE provoca rollback verificable desde otra conexión. [REQ-proyeccion-sesiones-idempotente-001, REQ-proyeccion-sesiones-idempotente-002, REQ-bandeja-ingestion-durable-004]
- [x] 2.2 GREEN en `src/Monitoring.Persistence/Sessions/SessionProjector.cs`: bloquear pendiente exacto, validar, insertar/comparar y marcar en una transacción; cerrar readers y no sobrescribir contenido. [REQ-proyeccion-sesiones-idempotente-001, REQ-proyeccion-sesiones-idempotente-002, REQ-bandeja-ingestion-durable-004]
- [x] 2.3 RED en `tests/Monitoring.Tests/SessionProjectionTests.cs`: más de 100 inválidos/desconocidos antes de válidos, workers concurrentes, bloqueados recuperados y reinicio tras retirar trigger. [REQ-proyeccion-sesiones-idempotente-001, REQ-proyeccion-sesiones-idempotente-002, REQ-proyeccion-sesiones-idempotente-003]
- [x] 2.4 GREEN/refactor en `src/Monitoring.Persistence/Sessions/SessionProjector.cs`: páginas de 100, cursor exclusivo/cota inclusiva PostgreSQL; avanzar incluso al omitir y reiniciar pasada. [REQ-proyeccion-sesiones-idempotente-003]

## Fase 3: Detalle por ámbito (U5)

- [x] 3.1 RED en `tests/Monitoring.Tests/SessionHostTests.cs`: cinco campos exactos, 404 ajeno, ID compartido, headers/query sin autoridad, sonda sola 401, Production/entorno desconocido 401 aun sustituyendo proveedor. [REQ-detalle-sesion-ambito-001, REQ-detalle-sesion-ambito-002]
- [x] 3.2 GREEN: crear `src/Monitoring.Persistence/Sessions/SessionReader.cs` y `src/Monitoring.Host/Sessions/{TrustedSessionReadContext,SessionEndpoint}.cs`; consulta triple parametrizada, feature separado, fallback y guard Development/Testing. [REQ-detalle-sesion-ambito-001, REQ-detalle-sesion-ambito-002]

## Fase 4: Operación y cierre (U6)

- [x] 4.1 RED en `tests/Monitoring.Tests/SessionWorkerTests.cs`: scope nuevo tras fallo transitorio, recuperación/reinicio, cancelación libera transacción, error no transitorio detiene host; logs sin datos sensibles. [REQ-proyeccion-sesiones-idempotente-002]
- [x] 4.2 GREEN: crear `src/Monitoring.Host/Sessions/SessionProjectionWorker.cs` y wiring scoped en `src/Monitoring.Host/Program.cs`; espera cancelable 1s/backoff máximo 30s, sin worker en --migrate ni sin conexión. [REQ-proyeccion-sesiones-idempotente-002]
- [x] 4.3 En `tests/Monitoring.Tests/{SessionHostTests,HostStartupTests,IngestionHostTests}.cs`, demostrar ACK previo a proyección controlando worker, data arbitrario pendiente, reenvío sin cuota extra, liveness sin conexión y migración sin worker; suite S02 verde. [REQ-contrato-sesion-sintetica-002, REQ-bandeja-ingestion-durable-004]
- [x] 4.4 Actualizar `README.md` y frase obsoleta de `docs/roadmap.md` con estado probado S01/S02/S03 y límite dev/test, conservando slices posteriores; cerrar refactor y suite completa, registrar diff real y evidencias en `apply-progress.md`. [REQ-detalle-sesion-ambito-002]
