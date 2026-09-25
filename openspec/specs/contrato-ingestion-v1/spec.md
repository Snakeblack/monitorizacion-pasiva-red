# contrato-ingestion-v1 Specification

## Purpose

Definir el contrato JSON v1 para que una sonda entregue lotes de eventos sintéticos a la ingestión con ámbito verificable.

## Requirements

### Requirement: Lote JSON v1 delimitado {#REQ-contrato-ingestion-v1-001}

El sistema MUST aceptar un lote que incluya `schemaVersion` con valor 1, `batchId`, `siteId`, `sensorId` y `events`. Los identificadores `batchId`, `siteId`, `sensorId` y `eventId` MUST ser cadenas no vacías de hasta 128 caracteres. Cada elemento de `events` MUST incluir `eventId`, `occurredAt` y `data`; `occurredAt` MUST usar el formato RFC 3339 en UTC terminado en `Z`, con una precisión máxima de milisegundos, y `data` MUST ser un objeto JSON. `events` MUST contener al menos un elemento y como máximo 500. El cuerpo de solicitud MUST medir como máximo 1 MiB. El sistema MUST rechazar con HTTP 400 una versión no admitida, campos desconocidos en la envoltura del lote o del evento, tipos o formatos de campo no admitidos, un lote vacío o un lote que supere cualquiera de esos límites.

#### Scenario: Lote v1 dentro de límites

- GIVEN un lote JSON v1 con los campos requeridos, identificadores no vacíos de hasta 128 caracteres, `occurredAt` en RFC 3339 UTC terminado en `Z` y con precisión máxima de milisegundos, `data` como objeto JSON y al menos un evento
- WHEN se envía a la operación de ingestión
- THEN el contrato lo reconoce como candidato válido para procesamiento

#### Scenario: Versión, campos o límites no admitidos

- GIVEN una solicitud con versión distinta de 1, campo desconocido, valor de campo con tipo o formato no admitido, identificador vacío o superior a 128 caracteres, `occurredAt` no admitido, `data` que no sea un objeto JSON, cero eventos, más de 500 eventos o más de 1 MiB
- WHEN se valida la solicitud
- THEN la ingestión responde HTTP 400
- AND no persiste eventos del lote

### Requirement: Ámbito ligado a identidad confiable {#REQ-contrato-ingestion-v1-002}

El sistema MUST obtener la identidad de sede y sonda de un contexto confiable del host. MUST comprobar que esa identidad coincide con `siteId` y `sensorId` del lote antes de aceptar eventos. Los valores declarados por el cliente MUST NOT autorizar por sí solos un ámbito. La solicitud sin contexto confiable MUST rechazarse con HTTP 401; la solicitud cuyo `siteId` o `sensorId` no coincida con el contexto MUST rechazarse con HTTP 403. Ninguna de esas solicitudes MUST persistirse.

#### Scenario: Identidad y lote coinciden

- GIVEN un contexto confiable para una sede y sonda
- AND un lote cuyos `siteId` y `sensorId` coinciden con el contexto
- WHEN se valida el ámbito
- THEN los eventos solo pueden procesarse dentro de esa identidad

#### Scenario: Lote intenta cruzar el ámbito

- GIVEN un contexto confiable para una sede o sonda
- AND un lote que declara una sede o sonda distinta
- WHEN se valida el ámbito
- THEN se rechaza la solicitud
- AND la ingestión responde HTTP 403
- AND no se persiste ningún evento

## Clarifications

### Session 2026-09-24

- Q: ¿Qué reglas exactas aplicamos a los formatos y restricciones de `batchId`, `siteId`, `sensorId` y `eventId`, a la serialización y precisión de `occurredAt`, al tipo permitido para `data` y a si `events` puede estar vacío? → A: Los cuatro identificadores son cadenas no vacías de hasta 128 caracteres; `occurredAt` usa RFC 3339 UTC terminado en `Z` y precisión máxima de milisegundos; `data` es un objeto JSON; `events` contiene al menos un elemento.
- Q: ¿Qué códigos HTTP deben responderse cuando falta el contexto confiable o cuando no coincide con el ámbito del lote? → A: HTTP 401 cuando falta el contexto confiable; HTTP 403 cuando el ámbito del lote no coincide.
