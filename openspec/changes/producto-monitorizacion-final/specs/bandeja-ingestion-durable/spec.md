# Delta for bandeja-ingestion-durable

## MODIFIED Requirements

### Requirement: Cuota y contador por origen {#REQ-bandeja-ingestion-durable-003}

El sistema MUST limitar cada origen confiable mediante cuota configurable positiva de eventos nuevos en cualquier ventana móvil60 s. Laboratorio MUST iniciar con 6000 eventos/min por sonda; dimensionamiento MUST calcularla por tasa/ráfaga/eventos por sesión/observaciones, verificándola en S17 sin afirmar capacidad por configuración. Solo cuentan nuevos confirmados por commit; reenvíos idénticos MUST NOT consumir cuota. Un lote que exceda cuota MUST rechazarse completo con429, sin persistencia parcial. MUST incrementar una vez el contador por solicitud rechazada con identidad confiable (400/403/409/429), sin incrementar por fallos internos; sin identidad MUST usar contador agregado sin etiquetas de sede/sonda. Contadores MUST distinguir sede/sonda.
(Previously: cuota fija500 eventos nuevos por ventana móvil60 s; ahora configurable y dimensionada.)

#### Scenario: Cuotas aisladas por sonda

- GIVEN dos sondas confiables con cuotas independientes
- WHEN una alcanza su cuota en60 s
- THEN el exceso recibe429 y la otra conserva su cuota

#### Scenario: Lote excede la cuota

- GIVEN cuota restante menor que nuevos eventos del lote
- WHEN se envía el lote
- THEN responde429 sin persistencia parcial y el contador conserva sede/sonda

### Requirement: Transición procesada ligada a la proyección {#REQ-bandeja-ingestion-durable-004}

Evento nuevo MUST permanecer pendiente hasta procesamiento exitoso o cuarentena por causa permanente. Para sesiones, procesado MUST confirmarse con sesión y outbox en misma transacción; para observaciones, con sus efectos de inventario definidos. Desconocidos/invalidación permanente MUST quedar en cuarentena no resuelta, sin falso procesado; fallos transitorios MUST permanecer pendientes. MUST conservar aceptación atómica,200 vacío tras commit, reenvío idéntico,409, cuota/contadores y contenido/identidad inmutables.
(Previously: desconocidos/inválidos quedaban pendientes; procesado solo ligado a sesión, sin outbox/cuarentena.)

#### Scenario: Aceptación antes de la proyección

- GIVEN lote válido aceptado durablemente
- WHEN ingestión responde200 vacío
- THEN sus no procesados permanecen pendientes sin exigir índice terminado

#### Scenario: Procesado confirmado

- GIVEN evento de sesión válido pendiente
- WHEN confirma transacción de sesión/marcado/outbox
- THEN los tres efectos quedan confirmados conjuntamente

#### Scenario: Marcado fallido

- GIVEN proyección en curso
- WHEN falla marcado antes del commit conjunto
- THEN permanece pendiente sin sesión/outbox confirmados

#### Scenario: Reenvío después de procesar

- GIVEN evento procesado reenviado idéntico
- WHEN ingestión responde200 vacío
- THEN no crea aceptación/sesión/outbox nuevos ni consume cuota

## ADDED Requirements

### Requirement: Cuarentena y reintento conciliables {#REQ-bandeja-ingestion-durable-005}

Worker MUST distinguir causas permanentes/transitorias, reintentar transitorias con espera y concurrencia acotadas, recuperar pendientes tras reinicio y avanzar sobre válidos. Cuarentena MUST conservar identidad/contenido original protegido, código de causa mínimo, tiempo/intentonas y estado de resolución, sin payload en logs. Acceso/resolución MUST estar restringidos a operación auditada; MUST NOT purgar no resueltos ni editar el original. Corrección MUST emitir evento nuevo vinculado, o descarte explícito auditado; replay compatible MUST preservar idempotencia. Cambios de estado MUST contabilizarse una vez y reconciliarse conforme a operación.

#### Scenario: Permanente entre válidos

- GIVEN evento incompatible entre dos válidos
- WHEN el worker procesa
- THEN aísla solo el incompatible, proyecta los válidos y publica saldo conciliable

#### Scenario: Fallo temporal y resolución

- GIVEN caída de PostgreSQL y cuarentena pendiente
- WHEN recupera conexión o se resuelve manualmente
- THEN reintenta sin duplicar y conserva evidencia auditada sin modificar el evento original
