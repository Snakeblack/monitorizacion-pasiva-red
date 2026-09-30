# proyeccion-sesiones-idempotente Specification

## Purpose

Convertir eventos sintéticos pendientes en sesiones durables con idempotencia, recuperación y aislamiento de origen.

## Requirements

### Requirement: Una sesión por origen y evento {#REQ-proyeccion-sesiones-idempotente-001}

El worker MUST proyectar cada evento reconocido bajo su identidad persistida `(siteId, sensorId, eventId)`. MUST conservar `occurredAt` y el `data` validado del evento. Los reintentos, replay y procesamiento concurrente MUST NOT generar más de una sesión para esa identidad ni alterar el contenido aceptado. Un mismo `eventId` de sedes o sondas distintas MUST identificar sesiones independientes.

#### Scenario: Proyección inicial

- GIVEN un evento sintético válido pendiente en la bandeja
- WHEN su procesamiento termina con éxito
- THEN existe una sesión con su identidad, `occurredAt` y `data` validado
- AND el evento queda procesado

#### Scenario: Replay y concurrencia

- GIVEN un evento válido reprocesado o intentado concurrentemente
- WHEN terminan los intentos exitosos
- THEN existe exactamente una sesión para su identidad de origen y evento
- AND se conserva el contenido aceptado

#### Scenario: Identificador compartido entre orígenes

- GIVEN eventos válidos con igual `eventId` y distinta sede o sonda
- WHEN se proyectan
- THEN cada origen conserva su sesión independiente

### Requirement: Commit conjunto y recuperación {#REQ-proyeccion-sesiones-idempotente-002}

La proyección PostgreSQL y el marcado procesado MUST confirmarse en la misma transacción por evento. Un fallo antes del commit MUST revertir ambos efectos; MUST NOT quedar una sesión confirmada sin su marcado ni un marcado sin su sesión. Tras reiniciar, el worker MUST poder recuperar los eventos pendientes y procesarlos sin duplicados.

#### Scenario: Fallo entre proyección y marcado

- GIVEN un evento válido pendiente
- WHEN ocurre un fallo tras crear la proyección y antes de confirmar el marcado
- THEN no queda proyección confirmada y el evento sigue pendiente

#### Scenario: Recuperación tras reinicio

- GIVEN un evento pendiente después de un fallo antes del commit
- WHEN el worker reinicia y procesa con éxito ese evento
- THEN queda exactamente una sesión y el evento queda procesado

### Requirement: Pendientes no proyectables sin bloqueo de válidos {#REQ-proyeccion-sesiones-idempotente-003}

Los eventos desconocidos o inválidos MUST permanecer pendientes, sin proyección ni marcado. Su presencia MUST NOT impedir que el worker avance sobre eventos válidos pendientes, incluidos los posteriores a esos eventos en el orden de selección. S03 MUST NOT incorporar cuarentena ni reconciliación completa de S08.

#### Scenario: Inválidos preceden a válidos

- GIVEN eventos desconocidos o inválidos pendientes seguidos de eventos válidos pendientes
- WHEN el worker continúa procesando la bandeja
- THEN los válidos terminan proyectados y procesados
- AND los desconocidos o inválidos siguen pendientes sin sesión
