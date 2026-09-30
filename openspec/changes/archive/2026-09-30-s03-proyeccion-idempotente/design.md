# Diseño: S03 — proyección idempotente y lectura mínima

## Enfoque técnico

Modo `design-after-spec`; contrato delimitado por las cuatro specs y `s03-contract-001`. Domain valida, Persistence transacciona/consulta y Host coordina HTTP/background. `InboxWriter` y `BatchContract` conservan aceptación, cuotas, conflictos y ACK S02. Sin dependencias nuevas.

Evidencia: `InboxWriter.cs` ya usa SQL parametrizado/transaccional, PK compuesta y `occurred_at_text`; `Program.cs` separa migraciones del arranque. Tests: PostgreSQL 18/Testcontainers y `WebApplicationFactory`. Capacidad pendiente según ADR-014/S17.

## Decisiones arquitectónicas

### Decision: Sesión y marcado en una transacción por evento

**Elección:** `processed_at timestamptz NULL` en bandeja; `monitoring.session_projection` con PK/FK compuesta, `occurred_at_text text` y `data jsonb` obligatorios. Sin columnas IP/puertos: S03 consulta por identidad.

| Opción | Coste / consecuencia | Decisión |
|---|---|---|
| Transacción local por evento | Un commit por sesión; rollback acotado | Elegida: satisface REQ-proyeccion-sesiones-idempotente-002 |
| Insertar y marcar por separado | Puede dejar medio procesamiento confirmado | Rechazada |
| Transacción del lote entero | Un inválido/fallo amplía bloqueos y rollback | Rechazada |

`SessionProjector` bloquea el pendiente exacto (`FOR UPDATE SKIP LOCKED`), valida, inserta y marca antes del commit. PK impide duplicados; `ON CONFLICT DO NOTHING` exige igualdad JSONB/`occurred_at_text` antes de marcar. Conflicto desigual: error de invariantes, sin sobrescritura. FK restringe borrados; no protege contra corrupción manual. ADR-001. [Bloqueos](https://www.postgresql.org/docs/18/sql-select.html#SQL-FOR-UPDATE-SHARE), [conflictos](https://www.postgresql.org/docs/18/sql-insert.html#SQL-ON-CONFLICT).

### Decision: Barrido paginado que avanza sobre no proyectables

**Elección:** pasada con cota superior, páginas de 100 claves por `(accepted_at, site_id, sensor_id, event_id)`, cursor exclusivo y límite inclusivo. Índice parcial `WHERE processed_at IS NULL`; conservar índice de cuota. Cursor/orden comparten comparación/collation PostgreSQL.

| Opción | Coste / consecuencia | Decisión |
|---|---|---|
| Cursor por pasada y cota superior | Relee inválidos en pasadas posteriores | Elegida: válidos posteriores progresan |
| Repetir siempre los primeros 100 pendientes | Inválidos pueden ocupar toda la página | Rechazada |
| Estado durable de cuarentena | Exige política y contrato S08 | Diferida |

Cursor avanza sobre inválidos/bloqueados; se reinicia para recuperar bloqueos y commits tardíos. Memoria por página; sin SLA de drenaje. ADR-002.

### Decision: Contexto de lectura exclusivo del servidor y del entorno de prueba

**Elección:** `GET /api/v1/sessions/{eventId}` usa `TrustedSessionReadContext(siteId, sensorId)` desde feature separado de `ITrustedSensorIdentityFeature`. Ignora headers/query/body/claims arbitrarios. Endpoint exige `Development`/`Testing` incluso con proveedor DI sustituido. Sin contexto: 401 antes de consultar; con contexto: 200/404.

| Opción | Coste / consecuencia | Decisión |
|---|---|---|
| Feature confiable separado | Requiere fixture interno; no acceso humano completo | Elegida: límite aprobado de S03 |
| Reutilizar identidad de sonda o headers de ámbito | Confunde máquina y lector; permite suplantación | Rechazada |
| OIDC/RBAC ahora | Amplía dependencias y alcance | Diferida a S12 |

`SessionReader` parametriza triple identidad y devuelve DTO con cinco campos acordados, texto `occurredAt` y valor `data`; JSONB no conserva formato. ADR-003.

## Flujo y operación

```mermaid
sequenceDiagram
    participant I as Ingestión S02
    participant DB as PostgreSQL
    participant W as Worker S03
    I->>DB: Commit de lote aceptado pendiente
    I-->>I: HTTP 200 vacío
    W->>DB: Leer página de claves pendientes
    loop Cada clave seleccionada
        W->>DB: BEGIN; pendiente exacto FOR UPDATE SKIP LOCKED
        W->>W: Validar data sintético
        alt Válido
            W->>DB: INSERT sesión; comprobar replay; UPDATE processed_at; COMMIT
        else Inválido, desconocido o no disponible
            W->>DB: ROLLBACK / terminar sin efectos
        end
    end
```

Fallo tras INSERT/durante UPDATE revierte ambos. COMMIT incierto: releer pendiente, sin reutilizar contexto fallido. `SessionProjectionWorker : BackgroundService` crea scope por pasada, transacciones independientes y readers cerrados antes de escribir. Sin `DbContext` compartido.

Worker solo con conexión; sin ella conserva liveness. `--migrate` no crea workers. Espera cancelable de 1 segundo; fallos transitorios liberan scope y reintentan exponencialmente hasta 30 segundos, restableciendo espera al recuperar. Cancelación revierte/termina. Errores no transitorios/invariantes detienen host; log categoría/tipo sin payload, IP, secretos ni texto PostgreSQL. Esperas internas reversibles; sin cuarentena/reconciliación/alarma S08.

## Archivos e interfaces

| Ruta | Acción y responsabilidad |
|---|---|
| `src/Monitoring.Domain/Sessions/SyntheticSessionContract.cs` | Crear parser puro `TryParse(JsonElement, out SyntheticSessionData?)`; exactamente campos aprobados, IP válida, enteros y fechas UTC estrictas como S02; conservar valores originales |
| `src/Monitoring.Domain/Sessions/SessionDetail.cs` | Crear DTO con `EventId`, `SiteId`, `SensorId`, `OccurredAt`, `Data` independiente de EF |
| `src/Monitoring.Persistence/Ingestion/IngestionInboxEntity{,Configuration}.cs` | Añadir marca nullable e índice parcial |
| `src/Monitoring.Persistence/Sessions/SessionProjectionEntity{,Configuration}.cs` | Crear modelo interno y clave/FK compuestas |
| `src/Monitoring.Persistence/Sessions/SessionProjector.cs` | Crear `RunPassAsync(CancellationToken)`; selección y transacciones; SQL Npgsql existente |
| `src/Monitoring.Persistence/Sessions/SessionReader.cs` | Crear `FindAsync(siteId, sensorId, eventId, CancellationToken)` → `SessionDetail?` |
| `src/Monitoring.Persistence/MonitoringDbContext.cs` | Registrar modelo de sesiones |
| `src/Monitoring.Persistence/Migrations/202609290003_SessionProjection.cs` | Crear migración aditiva secuencial |
| `src/Monitoring.Host/Sessions/TrustedSessionReadContext.cs` | Crear feature/proveedor de ámbito y puerto de lectura; fallback sin persistencia devuelve ausencia |
| `src/Monitoring.Host/Sessions/SessionEndpoint.cs` | Crear endpoint y adapter al lector; guard de entorno, 401/404/200 |
| `src/Monitoring.Host/Sessions/SessionProjectionWorker.cs`, `src/Monitoring.Host/Program.cs` | Crear worker y wiring explícito scoped, separado de migración |
| `tests/Monitoring.Tests/SyntheticSessionContractTests.cs`, `SessionProjectionTests.cs`, `SessionHostTests.cs`, `SessionWorkerTests.cs`, `SessionSchemaTests.cs` | Crear pruebas de los escenarios siguientes |
| `tests/Monitoring.Tests/{MigrationTests,InboxSchemaTests}.cs` | Actualizar expectativas aditivas sin eliminar unicidad/cuota S02 |
| `README.md` | Documentar ejecución, migración y límite dev/test |

## Estrategia de pruebas

Strict TDD con `dotnet test Monitoring.slnx`; registrar RED/GREEN antes de implementación. Reutilizar `PostgresFixture` sin reemplazar DB por mocks para atomicidad.

| Requisito y escenarios | Respuesta observable y verificación |
|---|---|
| contrato-001: válido, límites, no reconocido/ inválido | Unitarias con ambos protocolos, IPv4/IPv6, puertos extremos y cada campo ausente/adicional/tipo incorrecto; fechas 0–3 decimales, offset, exceso de precisión y fin anterior |
| contrato-002 y bandeja-004: arbitrarios/aceptación antes de proyección | HTTP S02 sigue 200 vacío; DB pendiente, sin sesión; controlar worker en fixture para observar estado previo |
| proyección-001: inicial, replay/concurrencia, ID entre orígenes | PostgreSQL real, contexts independientes e intentos superpuestos; una fila por triple y valores originales; mismo ID entre sedes y entre sondas |
| proyección-002 y bandeja-004: fallo entre pasos/marcado fallido | Trigger de prueba BEFORE UPDATE de processed_at lanza error tras INSERT; conexión independiente verifica 0 sesiones y marca NULL; retirar trigger y recrear worker/context produce una sesión |
| proyección-002: recuperación/reinicio | Host nuevo sobre la misma DB tras fallo/cancelación; espera acotada por condición, no sleeps fijos; verifica marcado+sesión y replay posterior |
| proyección-003: inválidos preceden a válidos | Más de una página completa de inválidos y desconocidos antes de válidos; válidos procesados, anteriores intactos; bloqueo concurrente se recupera en siguiente pasada |
| detalle-001: encontrado, ausente/fuera, ID compartido | HTTP con DB real y contexts separados; exactamente cinco campos, 404 ajeno y contenidos distintos por ámbito; headers/query no amplían acceso |
| detalle-002: falta contexto/límite de entorno | Identidad de sonda sola → 401; Production y entorno desconocido → 401 incluso con proveedor reemplazado; Development/Testing con feature autorizado → 200 |
| Operación y compatibilidad | Pruebas host: sin conexión conserva liveness; migración no inicia worker; indisponibilidad transitoria/reinicio recupera; cancelación libera transacción; ejecutar toda suite S02 |

## Migración y despliegue

Aplicar tercera migración por `--migrate`; probar actualización S02 con eventos existentes pendientes y repetición idempotente. Código después del esquema; solo worker proyecta. Reversión conserva tablas/marcas: sin `Down` ni reset automático. Capacidad/continuidad sujetas a S17.

## Preguntas abiertas

Ninguna bloqueante. El tratamiento durable de pendientes inválidos pertenece a S08; acceso humano a S12 y capacidad a S17.
