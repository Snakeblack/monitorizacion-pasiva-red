# Design: Contrato v1 e ingestión durable S02

## Technical Approach

S02 añade una operación POST de ingestión al host Minimal API de S01. El host limita el cuerpo, resuelve la identidad confiable de sede y sonda, valida el JSON v1 y delega la admisión de un lote completo a un servicio de persistencia. PostgreSQL, provisional conforme a ADR-014, conserva la bandeja; una transacción serializada por origen decide duplicados, conflicto y cuota antes de emitir HTTP 200. No se proyectan sesiones ni se implementa transporte mTLS en este slice. Requisitos: `REQ-contrato-ingestion-v1-001/002` y `REQ-bandeja-ingestion-durable-001/002/003`.

## Architecture Decisions

### Decision: Bandeja por evento con comparación JSON estructural

| Opción | Trade-off | Decisión |
|---|---|---|
| Fila por `(site_id, sensor_id, event_id)`, evento completo `jsonb` | Un índice único y comparación por valor; el formato binario queda ligado a PostgreSQL mientras sea el candidato | Elegida. `jsonb` ignora el orden de propiedades, conserva el orden de arrays y permite comparar sin confiar en hashes. |
| Bytes originales o hash | Menos trabajo de parseo o comparación, pero considera distintos objetos reordenados o exige resolver colisiones y canonicalización | Descartada para S02. |

`batchId` y `schemaVersion` se guardan como procedencia de la primera aceptación, pero la identidad y comparación del evento abarcan `eventId`, `occurredAt` y `data`; cambiar solo `batchId` no crea conflicto. La fila inicial nunca se sobrescribe. La restricción primaria compuesta garantiza unicidad incluso si una futura ruta omite el servicio. Véase ADR-001.

### Decision: Cuota móvil en la misma transacción de admisión

| Opción | Trade-off | Decisión |
|---|---|---|
| Fila de coordinación por origen bloqueada `FOR UPDATE`, consulta de aceptaciones de los últimos 60 s, inserciones y commit | Serializa lotes de una misma sonda; ofrece un límite exacto entre instancias del host | Elegida por la atomicidad y la cuota aprobada. |
| Contador en memoria o caché externo | Menor espera local, pero puede admitir más de 500 tras concurrencia/reinicio o requiere otro servicio | Descartada. |

La fila de coordinación se crea dentro de la transacción si falta. Tras tomar el bloqueo se lee cada ID existente, se compara el valor JSON y se cuenta solo la parte nueva del lote. Un conflicto devuelve 409; si `count(accepted_at > now - 60 s) + nuevos > 500`, se devuelve 429; ambos terminan sin inserts. El instante de admisión se toma del reloj de PostgreSQL después de adquirir el bloqueo. La consulta usa índice `(site_id, sensor_id, accepted_at)`; el índice único cubre la búsqueda por ID. Un fallo de SQL/commit se propaga como error interno y nunca como ACK. Véase ADR-002.

### Decision: Identidad del host separada del contrato enviado

El endpoint obtiene `(siteId, sensorId)` mediante un proveedor de identidad confiable del host, no de cabeceras o campos del cliente. El adaptador de S02 no autentica sondas reales: en el entorno aislado, las pruebas inyectan contexto confiable en el host; sin él la operación devuelve 401. S13 conectará mTLS a este límite, con revisión de seguridad antes de usar sondas reales. Una identidad presente pero distinta del lote devuelve 403 antes de acceder a la bandeja. Mantener la frontera explícita evita que `siteId`/`sensorId` autodeclarados concedan escritura. Véase ADR-003.

## Data Flow

```text
Sonda sintética -> Host: POST JSON v1
Host -> identidad confiable: resolver origen (ausente -> 401)
Host -> validador: tamaño, esquema, tipos, fecha y límites (inválido -> 400)
Host -> validador: comparar ámbito (distinto -> 403)
Host -> bandeja: transacción, bloqueo por origen, duplicados/conflictos, cuota
bandeja -> PostgreSQL: insertar eventos nuevos; commit
PostgreSQL -> Host: commit confirmado -> 200, cuerpo vacío
```

El lote es la unidad de decisión. Si contiene IDs repetidos con distinto contenido, se rechaza entero con 409; si se repiten con igual contenido, cuentan una sola vez como nuevos. Los eventos ya aceptados se comparan antes de consumir cuota. Un lote mixto de reenvíos y nuevos usa solo estos últimos para la ventana móvil. La transacción se revierte ante conflicto, cuota o fallo antes de commit. Si se pierde la respuesta después del commit, el reenvío idéntico lee filas existentes y obtiene 200 sin nuevas inserciones.

El contador de rechazo se incrementa exactamente una vez al decidir una respuesta 400, 403, 409 o 429. Si existe identidad confiable usa las etiquetas de sede y sonda de esa identidad, nunca las del cuerpo; sin identidad usa solo la serie agregada, incluido 401. No se incrementa por fallos internos. Se expone mediante `System.Diagnostics.Metrics`; el receptor y política operativa quedan para S14. La cardinalidad de origen precisa inventario operativo antes de activar emisores reales.

## File Changes

| File | Action | Description |
|---|---|---|
| `src/Monitoring.Host/Program.cs` | Modify | Registrar servicio, proveedor de identidad, límite de tamaño y operación Minimal API; conservar `/health/live` y `--migrate`. |
| `src/Monitoring.Host/Ingestion/BatchEndpoint.cs` | Create | Parseo estricto, traducción de resultados a HTTP y contador de rechazos. |
| `src/Monitoring.Host/Ingestion/TrustedSensorIdentity.cs` | Create | Contexto confiable inyectable por el host; ninguna cabecera del cliente crea identidad. |
| `src/Monitoring.Domain/Ingestion/BatchContract.cs` | Create | Modelos y validación de JSON v1 sin dependencia de EF/Npgsql. |
| `src/Monitoring.Persistence/MonitoringDbContext.cs` | Modify | Mapeo de bandeja y fila de coordinación por origen. |
| `src/Monitoring.Persistence/Ingestion/InboxWriter.cs` | Create | Admisión transaccional y clasificación de duplicado, conflicto y cuota. |
| `src/Monitoring.Persistence/Migrations/202609240002_DurableInbox.cs` | Create | Tablas, clave única e índice de ventana móvil en esquema `monitoring`. |
| `tests/Monitoring.Tests/IngestionContractTests.cs` | Create | Casos de contrato y ámbito con host aislado. |
| `tests/Monitoring.Tests/IngestionPersistenceTests.cs` | Create | Integración con PostgreSQL desechable, concurrencia, cuota, rollback y ACK. |

## Interfaces / Contracts

El cuerpo es un objeto JSON v1 con solo `schemaVersion`, `batchId`, `siteId`, `sensorId`, `events`; cada evento tiene solo `eventId`, `occurredAt`, `data`. `events` contiene 1–500 entradas; los cuatro IDs son cadenas no vacías de hasta 128 caracteres; `occurredAt` es RFC 3339 UTC con `Z` y como máximo milisegundos; `data` es objeto JSON. El cuerpo tiene máximo 1 MiB. También se rechazan campos desconocidos en envoltura y evento. El objeto `data` mantiene su estructura JSON libre. El validador compara valores JSON estructurales: propiedades sin orden, arrays con orden. La validación ocurre antes de abrir la transacción; se comprueba el tamaño mientras se lee para no cargar cuerpos superiores al límite.

Resultado HTTP: 200 sin cuerpo solo tras commit para nuevo o reenvío idéntico; 400 contrato inválido; 401 sin identidad confiable; 403 ámbito distinto; 409 ID reutilizado con contenido distinto; 429 cuota excedida. Un error de almacenamiento no se traduce a éxito ni a rechazo contractual. La dirección concreta de la ruta es configuración interna del host en S02; su publicación externa queda vinculada a S13.

## Testing Strategy

Strict TDD con `dotnet test Monitoring.slnx`: para cada comportamiento escribir primero una prueba fallida, implementar lo mínimo y repetir. Las pruebas de contrato cubren límite exacto de bytes/eventos/IDs, tipos, fecha UTC y precisión, campos desconocidos, ámbito 401/403 y cuerpo vacío. `WebApplicationFactory` aporta una identidad de prueba desde el servidor, nunca desde el payload. Las pruebas con `PostgresFixture` migran una base vacía y verifican filas y respuestas reales.

| Requirement / quality concern | Trigger and conditions | Expected response | Verification |
|---|---|---|---|
| ACK durable y rollback (`...001`) | Fallo inyectado antes de commit; respuesta perdida después | Sin 200 ni filas nuevas antes de commit; reenvío posterior 200 sin duplicado | Integración HTTP + PostgreSQL, recuento y contenido de filas. |
| Idempotencia y conflicto (`...002`) | Reordenar propiedades, conservar/cambiar orden de arrays, dos envíos concurrentes | Una fila por identidad; ambos idénticos 200; distinto 409 sin reemplazo | Integración concurrente con dos peticiones y lectura directa. |
| Cuota (`...003`) | 500 nuevos en ventana, lote mixto, concurrencia en mismo origen y otra sonda | 429 sin inserción parcial; reenvíos no gastan cuota; otra sonda independiente | Reloj de base controlado para bordes de 60 s y pruebas concurrentes. |
| Contrato y ámbito (`contrato...001/002`) | JSON inválido, identidad ausente o distinta | 400/401/403, cero filas, contador correcto | Pruebas HTTP y contador mediante listener de métricas. |

No se atribuye capacidad de producción a estas pruebas pequeñas: S17 mide los objetivos de ADR-013 con carga representativa.

La cuota aprobada de 500 eventos/minuto por cada una de ocho sondas permitiría como máximo 5,76 M eventos/día a ritmo uniforme, mientras ADR-013 propone 10 M sesiones/día. La relación eventos/sesión y el reparto real aún no están medidos; S17 debe revisar esta cuota y el perfil antes de declarar aptitud de producción.

## Migration / Rollout

La migración incremental añade `monitoring.ingestion_origin` y `monitoring.ingestion_inbox` a la base vacía de S01; no transforma datos. Ejecutar `--migrate` antes de habilitar el endpoint. En reversión, deshabilitar la operación y conservar la bandeja para futura proyección o exportación; eliminar tablas solo tras comprobar que no hay eventos pendientes. PostgreSQL sigue siendo candidato provisional hasta S17.

## Open Questions

Ninguna bloquea S02. La ruta externa, el adaptador mTLS, los plazos de retención, el receptor de métricas y la capacidad quedan en sus slices posteriores.
