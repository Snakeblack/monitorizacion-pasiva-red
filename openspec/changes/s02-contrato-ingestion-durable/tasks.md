# Tasks: Contrato v1 e ingestión durable S02

## Spec/Design Reconciliation

| Requirement / Scenario | Priority | Design allocation | Status | Notes |
|---|---|---|---|---|
| `REQ-contrato-ingestion-v1-001`: contrato JSON v1, límites y errores 400 | MUST | `BatchContract.cs`, `BatchEndpoint.cs`; `IngestionContractTests.cs` | covered-by-design | Incluye máximo 1 MiB/500 eventos, IDs, UTC milisegundos y campos desconocidos. |
| `REQ-contrato-ingestion-v1-002`: origen confiable, 401/403 y aislamiento | MUST | `TrustedSensorIdentity.cs`, `BatchEndpoint.cs`; pruebas HTTP | covered-by-design | El cliente no establece su propia identidad. |
| `REQ-bandeja-ingestion-durable-001`: atomicidad, commit previo al ACK y 400/409/429 | MUST | `InboxWriter.cs`, migración y endpoint; pruebas PostgreSQL | covered-by-design | El error de almacenamiento no se traduce a 200. |
| `REQ-bandeja-ingestion-durable-002`: igualdad JSON, idempotencia, concurrencia y conflicto | MUST | `InboxWriter.cs`, clave única y `jsonb`; pruebas de integración | covered-by-design | Propiedades sin orden, arrays con orden; conservar primera aceptación. |
| `REQ-bandeja-ingestion-durable-003`: cuota móvil y contadores por origen/agregado | MUST | fila de origen bloqueada, índice temporal y métrica en `BatchEndpoint.cs` | covered-by-design | Solo eventos nuevos confirmados consumen cuota; excluir fallos internos. |

### Reconciliation Verdict
- MUST coverage: complete.
- SHOULD/MAY gaps: none.
- Ambiguities to track: none; contrato, códigos HTTP, semántica de cuota y métricas están precisados por specs y diseño.

## Review Workload Forecast

Estimated changed lines: 650–900 líneas cambiadas, incluyendo migración, implementación por capas y pruebas de integración.
Delivery strategy: auto-chain; dividir en tres slices autónomos, cada uno con sus pruebas y commit de unidad de trabajo.
Suggested split: PR 1 contrato/identidad/endpoint y pruebas HTTP; PR 2 esquema y admisión transaccional; PR 3 cuotas, métricas y pruebas de integración extremo a extremo.

Decision needed before apply: No
Chained PRs recommended: Yes
Chain strategy: feature-branch-chain
400-line budget risk: High

## Suggested Work Units

| Unit | Goal | Likely PR | Notes |
|---|---|---|---|
| 1 | Contrato v1 estricto, resolución de identidad confiable y traducción HTTP básica | PR #1 | Base: rama tracker S02; validar 400/401/403/200 vacío con host de prueba. |
| 2 | Migración, mapeo EF y aceptación atómica/idempotente con unicidad PostgreSQL | PR #2 | Base: rama PR #1; probar commit, rollback, duplicados, conflicto y concurrencia. |
| 3 | Ventana móvil por origen, contadores de rechazo y escenarios completos | PR #3 | Base: rama PR #2; probar cuota, métricas y suite completa desde base vacía. |

Cada PR debe quedar por debajo de 400 líneas cambiadas; si el slice excede el límite durante implementación, subdividirlo manteniendo su orden y evidencia antes de empezar el slice siguiente. El error de almacenamiento y la pérdida de respuesta tras commit deben verificarse sin simular ACK exitoso antes de confirmar persistencia.

## Phase 1: Contrato y frontera del host (PR #1)

- [x] 1.1 RED: añadir pruebas de contrato en `tests/Monitoring.Tests/IngestionContractTests.cs` para versión/campos/tipos, IDs (1–128), UTC `Z` hasta milisegundos, `data` objeto, 1–500 eventos y cuerpo de 1 MiB (`REQ-contrato-ingestion-v1-001`).
- [x] 1.2 GREEN: implementar modelos y validación JSON estricta en `src/Monitoring.Domain/Ingestion/BatchContract.cs`; probar rechazo 400 sin acceso a persistencia (`REQ-contrato-ingestion-v1-001`).
- [x] 1.3 RED/GREEN: cubrir identidad ausente, coincidencia y discrepancia; implementar `src/Monitoring.Host/Ingestion/TrustedSensorIdentity.cs` y comprobar ámbito antes de tocar la bandeja (401/403) (`REQ-contrato-ingestion-v1-002`).
- [x] 1.4 RED/GREEN: probar registro de Minimal API, límite de lectura y respuesta 200 vacía; implementar `src/Monitoring.Host/Ingestion/BatchEndpoint.cs` y registrar ruta, dependencias y límite de cuerpo en `src/Monitoring.Host/Program.cs` (`REQ-contrato-ingestion-v1-001`, `REQ-contrato-ingestion-v1-002`).
- [x] 1.5 REFACTOR: consolidar validación y mapeo de errores sin alterar contratos; ejecutar `dotnet test Monitoring.slnx --filter FullyQualifiedName~IngestionContractTests`.

## Phase 2: Bandeja durable e idempotencia (PR #2)

- [x] 2.1 RED: ampliar `tests/Monitoring.Tests/IngestionPersistenceTests.cs` para ACK después de commit, fallo anterior al commit sin filas/200, reenvío idéntico sin duplicar, contenido distinto 409 y conservación del evento original (`REQ-bandeja-ingestion-durable-001`, `REQ-bandeja-ingestion-durable-002`).
- [x] 2.2 GREEN: agregar tabla de origen y bandeja por evento, clave única `(site_id, sensor_id, event_id)` e índice `(site_id, sensor_id, accepted_at)` en `src/Monitoring.Persistence/Migrations/202609240002_DurableInbox.cs`; mapear entidades en `MonitoringDbContext.cs` y verificar el esquema incremental desde S01.
- [x] 2.3 GREEN: implementar `src/Monitoring.Persistence/Ingestion/InboxWriter.cs` con transacción de lote, bloqueo por origen, comparación estructural `jsonb`, deduplicación local del lote, inserción solo de nuevos y rollback en conflicto/fallo (`REQ-bandeja-ingestion-durable-001`, `REQ-bandeja-ingestion-durable-002`).
- [x] 2.4 RED/GREEN: añadir prueba con envíos concurrentes del mismo evento y objetos JSON reordenados/arrays reordenados; asegurar una fila, equivalencia solo para orden de propiedades y respuesta conforme a contrato (`REQ-bandeja-ingestion-durable-002`).
- [x] 2.5 REFACTOR: verificar rollback y unicidad a nivel de base; ejecutar `dotnet test Monitoring.slnx --filter FullyQualifiedName~IngestionPersistenceTests` contra PostgreSQL desechable.

## Phase 3: Cuota, señales y aceptación integral (PR #3)

- [x] 3.1 RED: probar 500 eventos nuevos por ventana móvil, lote que excede remanente sin inserción parcial, reenvíos sin consumo y aislamiento entre sondas (`REQ-bandeja-ingestion-durable-003`).
- [x] 3.2 GREEN: en `InboxWriter.cs`, adquirir bloqueo transaccional por origen, usar instante PostgreSQL tras bloqueo y rechazar con 429 si aceptados en 60 s más nuevos exceden 500 (`REQ-bandeja-ingestion-durable-003`).
- [ ] 3.3 RED/GREEN: probar un incremento por solicitud rechazada 400/403/409/429 usando identidad confiable, serie agregada cuando falta y ausencia de incremento en errores internos; emitir métricas en `BatchEndpoint.cs` con etiquetas de origen confiable solamente (`REQ-bandeja-ingestion-durable-003`).
- [ ] 3.4 RED/GREEN: añadir escenario integral de lote mixto, respuesta perdida tras commit y reintento idéntico; comprobar estado PostgreSQL y cuerpo vacío en `tests/Monitoring.Tests/IngestionPersistenceTests.cs` (`REQ-bandeja-ingestion-durable-001`, `REQ-bandeja-ingestion-durable-002`).
- [ ] 3.5 REFACTOR/VERIFICACIÓN: revisar límites, filtros de identidad y atomicidad; ejecutar `dotnet test Monitoring.slnx` con migración aplicada desde base vacía. No declarar aptitud/capacidad de producción: queda pendiente S17.

## Strict TDD Evidence Plan

En cada comportamiento, registrar primero la prueba focal fallida (RED), hacer el cambio mínimo que la pasa (GREEN) y limpiar duplicación sin cambiar el resultado (REFACTOR). Evidencia enfocada por slice: filtro `IngestionContractTests` en PR #1; `IngestionPersistenceTests` en PR #2; ambos filtros durante PR #3 y `dotnet test Monitoring.slnx` al cierre. Las pruebas de persistencia usan PostgreSQL desechable y migran desde base vacía; las pruebas HTTP suministran identidad desde el servidor de prueba, nunca desde el cuerpo.
