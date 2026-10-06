# pipeline-busqueda Specification

## Purpose

Mantener Elasticsearch como proyección reemplazable de PostgreSQL mediante outbox, Debezium/Kafka Connect y Kafka (K02–K03).

## Requirements

### Requirement: Publicación tras autoridad {#REQ-pipeline-busqueda-001}

El flujo MUST ser PostgreSQL autoritativo → outbox transaccional → Debezium/Connect → Kafka → Connect sink → Elasticsearch. MUST publicar únicamente cambios confirmados. Cada registro MUST portar contrato versionado, operación `upsert`/`delete`, identidad completa y revisión monotónica de esa identidad. La clave Kafka/documental MUST derivarse inequívocamente de `(siteId,sensorId,eventId)`; concatenaciones ambiguas MUST NOT colisionar. Registros de una identidad MUST conservar orden; replay/duplicados MUST dejar un único documento de la revisión vigente. Una revisión antigua MUST NOT sobrescribir otra nueva ni resucitar una supresión.

#### Scenario: Commit y repetición

- GIVEN commit autoritativo y duplicación/replay de su registro
- WHEN CDC y sink terminan
- THEN Kafka conserva identidad y Elasticsearch tiene un único documento vigente

#### Scenario: Transacción revertida

- GIVEN fallo anterior al commit de sesión/outbox
- WHEN CDC avanza
- THEN no publica ese cambio ni crea documento

### Requirement: Contratos y errores de proyección {#REQ-pipeline-busqueda-002}

Los índices MUST ser versionados con mappings explícitos de IP, fechas UTC, puertos, protocolo, ámbito y clave ordenable. Una versión desconocida/documento inválido MUST aislarse con causa mínima en cola restringida, sin perderlo como éxito ni bloquear claves válidas. Fallos transitorios MUST reintentarse; offset/checkpoint MUST NOT avanzar como aplicado antes de la aceptación durable correspondiente. Reinicio MUST recuperar offsets; duplicados tras resultado ambiguo MUST ser inocuos. MUST observarse lag de CDC/sink, WAL retenido, reintentos, cola fallida y diferencias de conteos por ámbito.

#### Scenario: Sink caído y registro inválido

- GIVEN interrupción del sink y un registro incompatible entre válidos
- WHEN se restablece el servicio
- THEN recupera los válidos y mantiene el incompatible aislado y visible, sin contar falso éxito

### Requirement: Frescura y autoridad visible {#REQ-pipeline-busqueda-003}

MUST medir desde ACK durable hasta sesión consultable. La API MUST comunicar estado `current`, `lagging` o `recovering`, instante de medida y lag conocido o desconocido; MUST NOT inventar lag cero sin evidencia. `lagging` MUST identificar pendientes de antigüedad >60 s; recuperación/checkpoint desconocido MUST ser `recovering`. La indisponibilidad/timeout MUST seguir `consulta-sesiones`, sin fallback ilimitado a PostgreSQL. El detalle MUST leer autoridad y puede existir antes del índice.

#### Scenario: Autoridad adelantada al índice

- GIVEN sesión confirmada con publicación pendiente
- WHEN se consulta detalle y listado
- THEN detalle puede encontrarla y listado informa frescura sin presentar su ausencia como pérdida confirmada

### Requirement: Borrado y reconstrucción seguros {#REQ-pipeline-busqueda-004}

Borrado autoritativo MUST emitir supresión identificada/revisionada y excluir inmediatamente sesiones caducadas/suprimidas de API aunque índice/PIT esté atrasado. MUST conservar barrera mínima de supresión hasta superar horizontes de replay/backup autorizados. Rebuild MUST usar snapshot autoritativo acotado más cambios concurrentes, validar conteos/identidades por ámbito y solo cambiar alias tras ponerse al día. Fallo MUST conservar índice anterior consultable o indicar indisponibilidad; rollback/replay/restauración MUST aplicar supresiones vigentes antes de habilitar lectura. Elasticsearch MUST NOT convertirse en fuente de restauración autoritativa.

#### Scenario: Tombstone y replay antiguo

- GIVEN sesión suprimida y un upsert anterior reenviado
- WHEN se recupera o reconstruye el índice
- THEN la sesión no vuelve a exposición ni en cursores abiertos

#### Scenario: Rebuild interrumpido

- GIVEN índice nuevo incompleto y cambios concurrentes
- WHEN falla su construcción
- THEN no cambia el alias a datos incompletos y conserva evidencia para reanudar
