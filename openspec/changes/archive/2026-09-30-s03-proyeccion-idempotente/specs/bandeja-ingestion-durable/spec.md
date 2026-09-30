# Delta for bandeja-ingestion-durable

## ADDED Requirements

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
