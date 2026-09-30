# Delta for detalle-sesion-ambito

## ADDED Requirements

### Requirement: Contexto instalado desde configuración del servidor {#REQ-detalle-sesion-ambito-003}

En Development y Testing el anfitrión MUST instalar el contexto de lectura de confianza desde configuración del servidor, con sede y sonda. La petición HTTP MUST NOT establecer, sustituir ni ampliar ese contexto mediante cabeceras, consulta o cuerpo. En cualquier otro entorno el detalle MUST responder HTTP 401 y MUST NOT instalar ese contexto desde configuración.

#### Scenario: Ámbito de servidor en Development o Testing

- GIVEN Development o Testing con sede y sonda en configuración del servidor y una sesión proyectada en ese ámbito
- WHEN se consulta su `eventId` sin declarar ámbito en la petición
- THEN se obtiene HTTP 200 con los cinco campos persistidos

#### Scenario: Petición que declara otro ámbito

- GIVEN el contexto instalado desde configuración del servidor
- WHEN la petición declara otra sede o sonda en cabeceras o consulta
- THEN el detalle usa el ámbito de la configuración
- AND no usa los valores de la petición

#### Scenario: Production ignora la configuración

- GIVEN un entorno de producción con la misma configuración de sede y sonda
- WHEN se consulta el detalle
- THEN se obtiene HTTP 401

#### Scenario: Development o Testing sin configuración de ámbito

- GIVEN Development o Testing sin contexto de lectura en la configuración del servidor
- WHEN se consulta el detalle
- THEN se obtiene HTTP 401

## MODIFIED Requirements

### Requirement: Detalle dentro del ámbito confiable {#REQ-detalle-sesion-ambito-001}

El detalle MUST identificar la sesión por `eventId` dentro de la sede y sonda del contexto confiable de lectura. MUST responder HTTP 200 cuando existe en ese ámbito, con exactamente `eventId`, `siteId`, `sensorId`, `occurredAt` y el `data` validado. MUST responder HTTP 404 cuando no existe en ese ámbito, incluida una sesión existente en otra sede o sonda. La petición MUST NOT fijar el ámbito de lectura mediante cabeceras, consulta o cuerpo. Los valores declarados por el solicitante MUST NOT autorizar un ámbito.
(Previously: los valores del solicitante no autorizaban otro ámbito; la petición ahora no fija el ámbito.)

#### Scenario: Sesión encontrada

- GIVEN una sesión proyectada dentro de la sede y sonda del contexto de lectura
- WHEN se consulta su `eventId`
- THEN se obtiene HTTP 200 con los cinco campos establecidos y sus valores persistidos

#### Scenario: Sesión ausente o fuera de ámbito

- GIVEN un contexto de lectura y un `eventId` ausente en su ámbito o existente solo en otra sede o sonda
- WHEN se consulta el detalle
- THEN se obtiene HTTP 404 sin datos de la sesión ajena

#### Scenario: Mismo ID en ámbitos distintos

- GIVEN sesiones con igual `eventId` en dos ámbitos distintos
- WHEN cada contexto consulta ese identificador
- THEN cada uno recibe únicamente la sesión de su propia sede y sonda
