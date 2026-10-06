# consulta-sesiones Specification

## Purpose

Consultar sesiones iniciadas con filtros del caso, autorización en servidor y paginación acotada (F-05/S09).

## Requirements

### Requirement: Validación y límites F-05 {#REQ-consulta-sesiones-001}

La API MUST exigir `from`/`to` UTC `Z` con precisión ≤milisegundos y `from < to`. MUST limitar a 30 días de intervalo dentro de los30 días consultables; `to` MUST NOT ser futuro. Hasta24 h MUST permitir el intervalo y ámbito efectivo autorizado sin filtros opcionales adicionales. Para >24 h y ≤30 días MUST exigir sitio **y** sonda **y** IP de origen/destino. MUST aceptar filtros exactos `siteId`, `sensorId`, `sourceIp`, `destinationIp`, `protocol`, `sourcePort`, `destinationPort`, combinados por AND; IP MUST ser válida, protocolo TCP/UDP, puertos enteros0–65535. MUST rechazar campos desconocidos, búsqueda libre, intervalos/filtros inválidos o página fuera de1–100 con HTTP 400 antes de consultar backend; página predeterminada MUST ser50.

#### Scenario: Ventana selectiva

- GIVEN ventana 30 días, sitio/sonda permitidos y una IP de extremo
- WHEN se consulta con protocolo/puerto opcionales
- THEN usa todos los filtros y semántica `startedAt` de sesiones canónicas

#### Scenario: Límites rechazados

- GIVEN ausencia de tiempo, >30 días, >24 h sin ámbito/IP, puerto inválido o página 101
- WHEN se valida la solicitud
- THEN responde400 sin ejecutar búsqueda

### Requirement: Selección autorizada por operación {#REQ-consulta-sesiones-002}

Cada búsqueda/detalle MUST aplicar permisos actuales y ámbitos autorizados de `identidad-y-acceso` en servidor. Selectores del cliente MUST solo restringir ese conjunto; MUST NOT conceder permisos. Sin selectores de sitio/sonda la ventana corta MUST restringirse al conjunto autorizado. Selector ajeno MUST producir403 sin datos; ausencia de identidad MUST producir401. Los filtros de ámbito MUST preceder a la búsqueda; MUST NOT filtrar solo después en Angular.

#### Scenario: Ámbito adverso

- GIVEN analista autorizado solo a A y petición con selector B
- WHEN se consulta
- THEN responde403 sin revelar sesiones de B

### Requirement: Cursor de snapshot ligado y finito {#REQ-consulta-sesiones-003}

La paginación MUST usar snapshot PIT y `search_after`, orden total estable por `startedAt` y clave completa; MUST NOT usar offset ilimitado. Cursor opaco protegido contra alteración MUST ligar sujeto, permisos/ámbitos efectivos, filtros normalizados, tamaño de página, snapshot, posición y vencimiento absoluto10 min desde primera página. Cada página MUST revalidar identidad/permisos; cambio de ámbito/permisos MUST rechazar403, alteración/filtros distintos400, caducidad/PIT perdido410. MUST liberar snapshots al terminar/caducar y acotar su número por sujeto. Retención/supresión vigente MUST aplicarse incluso al snapshot.

#### Scenario: Empates y nuevas sesiones

- GIVEN sesiones con igual inicio y nuevas inserciones tras primera página
- WHEN se recorren páginas del mismo snapshot
- THEN las sesiones vigentes del snapshot aparecen una vez, sin omisiones por empates ni inserciones posteriores

#### Scenario: Cursor reutilizado indebidamente

- GIVEN cursor alterado, de otro usuario, caducado o con permisos reducidos
- WHEN se solicita otra página
- THEN rechaza con el código aplicable sin resultados

### Requirement: Respuesta y coste acotados {#REQ-consulta-sesiones-004}

La respuesta200 MUST contener `items` ≤página, `nextCursor` cuando quedan resultados y frescura de `pipeline-busqueda`; cada ítem MUST identificar ámbito/evento, extremos, protocolo e inicio/fin/procedencia. MUST NOT exigir conteo total/exportación. Ejecución backend MUST tener timeout ≤10s y cancelación propagada; timeout MUST responder504 sin página parcial, backend indisponible503, resultado vacío real200. Deben acotarse solicitudes/snapshots concurrentes; saturación MUST responder429. MUST NOT servir caducados/suprimidos desde índice atrasado.

#### Scenario: Vacío, lag y fallo

- GIVEN búsqueda sin coincidencias, proyección retrasada, caída o timeout
- WHEN termina la operación
- THEN distingue vacío200/frescura de indisponibilidad503/timeout504 sin resultados parciales falsos
