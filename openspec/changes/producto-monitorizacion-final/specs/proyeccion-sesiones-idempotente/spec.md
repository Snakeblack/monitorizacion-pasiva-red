# Delta for proyeccion-sesiones-idempotente

## MODIFIED Requirements

### Requirement: Una sesión por origen y evento {#REQ-proyeccion-sesiones-idempotente-001}

Worker MUST proyectar contratos sintético y capturado reconocidos por `sesiones-canonicas` bajo identidad persistida `(siteId,sensorId,eventId)`, conservando `occurredAt` y `data` validado. Reintentos, replay y concurrencia MUST NOT crear más de una sesión ni alterar contenido aceptado. Mismo `eventId` en sedes/sondas distintas MUST identificar sesiones independientes. Normalización consultable MUST respetar contrato canónico sin ampliar sintético v1.
(Previously: solo proyección de eventos sintéticos reconocidos.)

#### Scenario: Proyección inicial

- GIVEN evento sintético o capturado válido pendiente
- WHEN termina procesamiento
- THEN conserva sesión/identidad/`occurredAt`/`data` y evento procesado

#### Scenario: Replay y concurrencia

- GIVEN evento válido reprocesado o intentado concurrentemente
- WHEN terminan intentos exitosos
- THEN existe exactamente una sesión con el contenido aceptado

#### Scenario: Identificador compartido entre orígenes

- GIVEN eventos con igual `eventId` y distinta sede/sonda
- WHEN se proyectan
- THEN cada origen conserva sesión independiente

### Requirement: Commit conjunto y recuperación {#REQ-proyeccion-sesiones-idempotente-002}

Sesión PostgreSQL, marcado procesado y outbox de publicación MUST confirmarse en una transacción por evento. Fallo antes del commit MUST revertir los tres; MUST NOT quedar sesión sin marcado/outbox ni publicación sin sesión confirmada. Reinicio MUST recuperar pendientes sin duplicar sesión/publicación lógica. No se exige entrega Kafka/Elasticsearch dentro de esa transacción; MUST seguirse `pipeline-busqueda`.
(Previously: commit conjunto solo de sesión y marcado, sin publicación transaccional.)

#### Scenario: Fallo entre proyección y marcado

- GIVEN evento pendiente y sesión creada dentro de la transacción
- WHEN falla antes de confirmar marcado/outbox
- THEN no queda ninguno de los tres efectos y el evento sigue pendiente

#### Scenario: Recuperación tras reinicio

- GIVEN evento pendiente tras fallo antes del commit
- WHEN reinicia y procesa con éxito
- THEN queda una sesión, un marcado y una publicación lógica outbox

### Requirement: Pendientes no proyectables sin bloqueo de válidos {#REQ-proyeccion-sesiones-idempotente-003}

Evento desconocido/inválido permanente MUST quedar en cuarentena conforme a `bandeja-ingestion-durable`, sin sesión ni marcado procesado. Observaciones válidas MUST seguir su procesamiento específico, sin inventar sesiones. Su presencia MUST NOT impedir eventos válidos posteriores en el orden de selección. Fallos transitorios MUST mantenerse pendientes y reintentarse sin girar continuamente sobre una misma fila.
(Previously: desconocidos/inválidos permanecían pendientes y S03 prohibía cuarentena/reconciliación.)

#### Scenario: Inválidos preceden a válidos

- GIVEN desconocidos/inválidos seguidos de sesiones válidas
- WHEN el worker continúa
- THEN los válidos se proyectan/procesan y los permanentes quedan en cuarentena sin sesión
