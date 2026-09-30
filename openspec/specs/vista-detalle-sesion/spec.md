# vista-detalle-sesion Specification

## Purpose

Vista interna de una sesión sintética proyectada: detalle, vacío y error. La UI es cliente HTTP del detalle existente. El acceso permanece limitado a desarrollo y pruebas hasta S12.

## Requirements

### Requirement: Detalle de la sesión en ámbito {#REQ-vista-detalle-sesion-001}

Cuando el operador solicita el detalle de un `eventId` presente en el ámbito de lectura del servidor, la vista MUST mostrar exactamente `eventId`, `siteId`, `sensorId`, `occurredAt` y `data` del cuerpo HTTP 200. MUST distinguir el éxito con texto, no solo con color. MUST presentar esos campos como contenido principal de una superficie interna de datos. MUST NOT mostrar datos de otra sede o sonda ni una portada comercial.

#### Scenario: Sesión encontrada

- GIVEN una sesión proyectada en el ámbito de lectura del servidor
- WHEN el operador solicita su `eventId`
- THEN la vista muestra los cinco campos con los valores persistidos
- AND el estado de éxito es legible por texto

### Requirement: Estado vacío {#REQ-vista-detalle-sesion-002}

Cuando el `eventId` no existe en el ámbito o existe solo en otra sede o sonda, la vista MUST mostrar un estado vacío. MUST identificar la ausencia con texto, no solo con color. MUST NOT mostrar datos de la sesión ajena.

#### Scenario: Identificador ausente

- GIVEN un ámbito de lectura y un `eventId` que no existe en él
- WHEN el operador solicita su detalle
- THEN la vista muestra un estado vacío legible por texto
- AND no muestra campos de sesión

#### Scenario: Identificador de otro ámbito

- GIVEN un `eventId` existente solo en otra sede o sonda
- WHEN el operador solicita su detalle
- THEN la vista muestra un estado vacío legible por texto
- AND no muestra datos de la sesión ajena

### Requirement: Estado de error {#REQ-vista-detalle-sesion-003}

Cuando la API responde HTTP 401, no hay contexto de lectura, el entorno no es Development ni Testing, o falla la red, la vista MUST mostrar un estado de error. MUST identificar el error con texto, no solo con color. MUST NOT tratar ese fallo como estado vacío ni mostrar datos de sesión.

#### Scenario: Sin contexto o entorno no autorizado

- GIVEN una consulta de detalle sin contexto de lectura o en un entorno distinto de Development y Testing
- WHEN el operador solicita un `eventId`
- THEN la vista muestra un estado de error legible por texto
- AND no muestra campos de sesión

#### Scenario: Fallo de red

- GIVEN que la petición de detalle no llega a completarse por un fallo de red
- WHEN el operador solicita un `eventId`
- THEN la vista muestra un estado de error legible por texto
- AND no muestra campos de sesión

### Requirement: Cliente HTTP sin contexto de lectura {#REQ-vista-detalle-sesion-004}

La vista MUST obtener el detalle mediante la petición HTTP ya expuesta. MUST NOT enviar el contexto de lectura de confianza en cabeceras ni parámetros de consulta. Todo control interactivo MUST exponer un rol y MUST ser operable con teclado. MUST NOT presentar una acción solo como un contenedor clicable sin rol.

#### Scenario: Petición sin ámbito en la solicitud

- GIVEN la vista de detalle
- WHEN solicita un `eventId` al API
- THEN la petición no incluye el contexto de lectura en cabeceras ni en la consulta
- AND el ámbito efectivo sigue siendo el del servidor

#### Scenario: Control operable por teclado

- GIVEN un control interactivo en la vista de detalle
- WHEN el operador lo usa solo con teclado
- THEN puede activarlo
- AND el control expone un rol

### Requirement: Cadena vertical y verificación continua {#REQ-vista-detalle-sesion-005}

El sistema MUST comprobar de forma automática los estados detalle, vacío y error de la vista. MUST comprobar que un fixture sintético aceptado, confirmado y proyectado aparece en la vista cuando el ámbito de lectura coincide. La integración continua MUST fallar si falla cualquiera de esas comprobaciones. La vista MUST NOT publicarse como artefacto de producción.

#### Scenario: Tres estados de la vista

- GIVEN respuestas de detalle encontrado, ausente y no autorizado o inalcanzable
- WHEN se ejecutan las comprobaciones automáticas de la vista
- THEN cubren éxito con los cinco campos, vacío sin datos ajenos y error con texto

#### Scenario: Fixture hasta la vista

- GIVEN un fixture sintético aceptado, confirmado y proyectado en el ámbito de lectura del servidor de pruebas
- WHEN se consulta su `eventId` a través de la vista contra el API real
- THEN la vista muestra los cinco campos persistidos

#### Scenario: Integración continua

- GIVEN una comprobación de vista o de la cadena fixture-hasta-vista que falla
- WHEN se ejecuta la integración continua
- THEN el pipeline falla

#### Scenario: Sin publicación en producción

- GIVEN un empaquetado o despliegue de producción
- WHEN se inspecciona el artefacto publicado
- THEN no incluye la vista interna
