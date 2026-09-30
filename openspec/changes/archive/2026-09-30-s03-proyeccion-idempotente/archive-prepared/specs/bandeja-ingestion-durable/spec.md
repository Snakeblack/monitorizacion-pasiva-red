# bandeja-ingestion-durable Specification

## Purpose

Definir la aceptación durable de eventos por origen, su ACK, idempotencia, conflictos y señales de rechazo, antes de su futura proyección.

## Requirements

### Requirement: Persistencia atómica antes del ACK {#REQ-bandeja-ingestion-durable-001}

El sistema MUST aceptar o rechazar cada lote como unidad atómica: si un evento no puede aceptarse, MUST NOT persistir parcialmente los eventos nuevos de ese lote. Debe persistir durablemente cada evento aceptado bajo la identidad confiable `(siteId, sensorId)` y su `eventId`. MUST responder HTTP 200 con cuerpo vacío a un lote nuevo o a un reenvío idéntico solo después del commit durable. Un fallo antes del commit MUST NOT producir HTTP 200 ni dejar eventos nuevos del lote persistidos. Una solicitud inválida MUST recibir HTTP 400; un `eventId` reutilizado con contenido distinto MUST recibir HTTP 409; una cuota excedida MUST recibir HTTP 429.

#### Scenario: Lote nuevo confirmado

- GIVEN un lote válido que no excede la cuota
- WHEN se completa su commit durable
- THEN la ingestión responde HTTP 200 después del commit
- AND el cuerpo de la respuesta está vacío
- AND todos sus eventos quedan aceptados como una unidad

#### Scenario: Fallo antes del commit

- GIVEN un lote válido cuya operación durable falla antes del commit
- WHEN la ingestión termina la operación
- THEN no responde HTTP 200
- AND ningún evento nuevo del lote queda persistido

### Requirement: Reenvío idempotente y conflicto de contenido {#REQ-bandeja-ingestion-durable-002}

La identidad `(siteId, sensorId, eventId)` MUST identificar un único contenido de evento. Un reenvío con el mismo valor JSON MUST responder HTTP 200 sin crear otra aceptación. La comparación MUST ignorar el orden de propiedades de objetos JSON y MUST conservar el orden de los arrays. Si la misma identidad trae otro contenido, el sistema MUST responder HTTP 409 y MUST conservar intacto el evento ya aceptado.

#### Scenario: Reenvío idéntico

- GIVEN un evento ya confirmado para una identidad de origen e `eventId`
- WHEN vuelve a enviarse con el mismo valor JSON
- THEN la ingestión responde HTTP 200
- AND el cuerpo de la respuesta está vacío
- AND no crea una segunda aceptación durable

#### Scenario: Reenvío concurrente del mismo evento

- GIVEN dos solicitudes concurrentes del mismo evento para la misma identidad
- WHEN ambas solicitudes intentan ser aceptadas
- THEN queda una sola aceptación durable
- AND ambas pueden recibir HTTP 200 una vez resuelta la aceptación

#### Scenario: Reutilización del ID con contenido distinto

- GIVEN un evento ya confirmado para una identidad de origen e `eventId`
- WHEN llega el mismo `eventId` con contenido distinto
- THEN la ingestión responde HTTP 409
- AND conserva el contenido previamente aceptado

### Requirement: Cuota y contador por origen {#REQ-bandeja-ingestion-durable-003}

El sistema MUST limitar cada origen de sonda confiable a 500 eventos nuevos confirmados en cualquier ventana móvil de 60 segundos. Solo cuentan los eventos nuevos una vez confirmados por commit durable; los reenvíos idénticos MUST NOT consumir cuota. Cuando la aceptación de los eventos nuevos de una solicitud exceda la cuota aplicable, MUST rechazar el lote completo con HTTP 429 y MUST NOT persistir parcialmente eventos nuevos. El sistema MUST exponer un contador que incremente una vez por solicitud rechazada con identidad confiable, incluidas las respuestas HTTP 400, 403, 409 y 429, sin incrementar por fallos internos. Las solicitudes rechazadas sin identidad confiable MUST incrementar un contador agregado sin etiquetas de sede ni sonda. El contador por origen MUST permitir distinguir cada sede y sonda.

#### Scenario: Cuotas aisladas por sonda

- GIVEN dos sondas con identidades confiables distintas
- WHEN una sonda alcanza su cuota en una ventana móvil de 60 segundos
- THEN sus solicitudes que excedan la cuota reciben HTTP 429
- AND la otra sonda conserva su cuota independiente

#### Scenario: Lote excede la cuota

- GIVEN una sonda cuya cuota restante en la ventana móvil de 60 segundos es menor que los eventos nuevos del lote
- WHEN la sonda envía ese lote
- THEN la ingestión responde HTTP 429
- AND no persiste parcialmente sus eventos nuevos
- AND el contador de rechazos conserva el ámbito de sede y sonda

### Requirement: Transición procesada ligada a la proyección {#REQ-bandeja-ingestion-durable-004}

Un evento recién aceptado MUST permanecer pendiente hasta su procesamiento exitoso. La transición a procesado MUST confirmarse exclusivamente junto con su proyección de sesión en la misma transacción. Los eventos no reconocidos o inválidos para S03 MUST permanecer pendientes. El procesamiento MUST conservar los contratos existentes de aceptación atómica, HTTP 200 con cuerpo vacío tras commit, reenvío idéntico, conflicto HTTP 409, cuota y contadores; MUST NOT modificar el contenido ni la identidad del evento aceptado.

#### Scenario: Aceptación antes de la proyección

- GIVEN un lote S02 válido aceptado durablemente
- WHEN la ingestión responde HTTP 200 con cuerpo vacío
- THEN los eventos aún no procesados permanecen pendientes
- AND el ACK no exige que la proyección haya terminado

#### Scenario: Procesado confirmado

- GIVEN un evento sintético válido pendiente
- WHEN se confirma la transacción de proyección y marcado
- THEN el evento queda procesado y su sesión queda confirmada conjuntamente

#### Scenario: Marcado fallido

- GIVEN un evento pendiente con proyección en curso
- WHEN falla el marcado antes del commit conjunto
- THEN el evento conserva su estado pendiente sin proyección confirmada

#### Scenario: Reenvío después de procesar

- GIVEN un evento ya proyectado y procesado
- WHEN la sonda reenvía idéntico contenido bajo la misma identidad
- THEN se conserva HTTP 200 con cuerpo vacío sin nueva aceptación ni consumo de cuota
- AND se conserva una sola sesión

## Clarifications

### Session 2026-09-24

- Q: ¿Cómo se computan los 500 eventos por minuto? → A: Se aplica una ventana móvil de 60 segundos y solo cuentan los eventos nuevos confirmados; los reenvíos idénticos no consumen cuota.
- Q: ¿Qué debe contar el contador de rechazos por sede y sonda? → A: Incrementa una vez por solicitud rechazada con identidad confiable, incluyendo HTTP 400, 403, 409 y 429; excluye fallos internos. Las solicitudes sin identidad confiable incrementan un contador agregado.
- Q: ¿Qué cuerpo debe llevar la respuesta HTTP 200 para un lote nuevo y para un reenvío idéntico? → A: El cuerpo HTTP 200 está vacío en ambos casos.
