# Delta for vista-detalle-sesion

## MODIFIED Requirements

### Requirement: Detalle de la sesión en ámbito {#REQ-vista-detalle-sesion-001}

Cuando solicita detalle de identidad presente en ámbito autorizado, vista MUST mostrar exactamente `eventId`, `siteId`, `sensorId`, `occurredAt`, `data` del200. MUST distinguir éxito con texto, presentar datos como superficie interna y MUST NOT mostrar datos ajenos ni portada comercial. MUST representar sesión sintética/capturada conforme a contrato, incluidos inferencia/parcialidad cuando corresponden.
(Previously: detalle únicamente sintético en ámbito de lectura configurado.)

#### Scenario: Sesión encontrada

- GIVEN sesión proyectada en ámbito autorizado
- WHEN solicita su identidad
- THEN muestra cinco campos persistidos y estado de éxito por texto

### Requirement: Estado de error {#REQ-vista-detalle-sesion-003}

401/403, falta de identidad, fallo de red,503 o504 MUST mostrar error legible, sin tratarlo como vacío ni conservar datos ajenos/anteriores.401 MUST requerir sesión válida;403 MUST indicar denegación; fallo de proyección MUST distinguirse de ausencia de coincidencias. Peticiones tardías de una selección/identidad anterior MUST NOT reemplazar la selección vigente.
(Previously: fuera de Development/Testing siempre se mostraba error.)

#### Scenario: Sin contexto o entorno no autorizado

- GIVEN falta de contexto válido o permiso denegado
- WHEN solicita `eventId`
- THEN muestra error por texto sin campos de sesión

#### Scenario: Fallo de red

- GIVEN petición de detalle que falla por red
- WHEN termina el intento
- THEN muestra error por texto sin campos de sesión

### Requirement: Cliente HTTP sin contexto de lectura {#REQ-vista-detalle-sesion-004}

Vista MUST obtener detalle mediante API y autenticación OIDC; MUST NOT enviar cabeceras/valores que pretendan fijar identidad/roles de confianza. Selectores de sede/sonda MUST solo escoger ámbitos concedidos. En modo de prueba MUST conservar petición histórica sin contexto declarado. Controles MUST tener rol/nombre accesibles y ser operables por teclado; MUST NOT usar contenedores clicables sin rol.
(Previously: toda petición omitía sede/sonda porque el servidor instalaba único ámbito de prueba.)

#### Scenario: Petición sin ámbito en la solicitud

- GIVEN modo de detalle histórico de prueba
- WHEN solicita `eventId`
- THEN no envía contexto de confianza y usa ámbito del servidor

#### Scenario: Control operable por teclado

- GIVEN control interactivo de detalle
- WHEN se opera con teclado
- THEN puede activarse y expone rol/nombre

### Requirement: Cadena vertical y verificación continua {#REQ-vista-detalle-sesion-005}

MUST comprobar automáticamente detalle, vacío y error, y fixture aceptado→autoridad/outbox→Kafka→índice→listado→detalle Angular contra API real. CI MUST fallar si falla cualquiera. Publicación privada de UI productiva MUST exigir puertas de identidad/seguridad/liberación; MUST NOT habilitar modo de desarrollo en ese artefacto.
(Previously: vertical terminaba en detalle sin Kafka/búsqueda y prohibía incluir vista en producción.)

#### Scenario: Tres estados de la vista

- GIVEN encontrado, ausente y no autorizado/inalcanzable
- WHEN ejecuta comprobaciones
- THEN cubre cinco campos, vacío sin ajenos y error con texto

#### Scenario: Fixture hasta la vista

- GIVEN fixture aceptado/proyectado/indexado en ámbito de prueba o humano autorizado
- WHEN recorre listado y detalle contra API real
- THEN muestra identidad/filtros correctos y cinco campos persistidos

#### Scenario: Integración continua

- GIVEN comprobación de vista/vertical fallida
- WHEN ejecuta CI
- THEN falla pipeline

#### Scenario: Publicación en producción autorizada

- GIVEN empaquetado privado con puertas de producción resueltas
- WHEN inspecciona artefacto
- THEN incluye UI autenticada sin contexto de confianza de desarrollo

## ADDED Requirements

### Requirement: Listado y navegación de sesiones iniciadas {#REQ-vista-detalle-sesion-006}

Angular MUST proponer últimas 24 h, ofrecer filtros exactos de tiempo/IP/protocolo/puertos/sede/sonda de `consulta-sesiones`, validar límites visibles y resetear cursor al cambiar filtros. MUST navegar página≤100 y detalle con identidad completa; MUST mostrar carga, vacío, error, frescura/lag y410 con reinicio de consulta por texto. MUST NOT habilitar exportación ni búsqueda libre. Cierre/cambio de sesión MUST limpiar datos/cursor. Búsqueda por defecto24 h MUST funcionar sin filtros opcionales.

#### Scenario: Filtros y páginas

- GIVEN usuario con ámbitos concedidos y últimas 24 h
- WHEN busca, avanza página y cambia IP
- THEN aplica filtros, conserva identidad al abrir detalle y reinicia cursor para nueva IP
