# Delta for detalle-sesion-ambito

## MODIFIED Requirements

### Requirement: Detalle dentro del ámbito confiable {#REQ-detalle-sesion-ambito-001}

Detalle MUST leer sesión canónica autoritativa identificada por `(siteId,sensorId,eventId)` dentro de ámbitos autorizados actuales. MUST responder200 con exactamente `eventId`, `siteId`, `sensorId`, `occurredAt`, `data` validado, y404 para inexistente/caducada/suprimida en ámbito autorizado. Selector ajeno MUST responder403 sin revelar existencia. Valores cliente MUST solo seleccionar entre permisos concedidos, nunca autorizar sede/sonda. Endpoint histórico sin selectores MUST conservar ámbito único configurado en Development/Testing; selección productiva MUST ser explícita si hay varios ámbitos.
(Previously: `eventId` se resolvía exclusivamente en ámbito configurado; solicitudes no seleccionaban ámbito.)

#### Scenario: Sesión encontrada

- GIVEN sesión vigente dentro de ámbito autorizado
- WHEN consulta su identidad
- THEN obtiene200 y los cinco campos persistidos, incluso si índice está atrasado

#### Scenario: Sesión ausente o fuera de ámbito

- GIVEN evento ausente/caducado en ámbito autorizado o selector ajeno
- WHEN consulta detalle
- THEN responde404 para el primero o403 para el selector ajeno, sin datos ajenos

#### Scenario: Mismo ID en ámbitos distintos

- GIVEN igual `eventId` en dos ámbitos
- WHEN cada identidad consulta ámbito autorizado
- THEN recibe solo la sesión correspondiente

### Requirement: Contexto de lectura separado y limitado {#REQ-detalle-sesion-ambito-002}

Lectura MUST exigir identidad humana OIDC y permiso según `identidad-y-acceso`, separados de sonda. Sin contexto válido MUST responder401, sin permiso403. Contexto configurado de pruebas MUST limitarse a Development/Testing explícitos; MUST NOT permitir acceso productivo. Producción MUST habilitar únicamente OIDC/RBAC validado.
(Previously: OIDC/RBAC reservado a S12 y lectura habilitada solo en desarrollo/pruebas.)

#### Scenario: Falta contexto de lectura

- GIVEN petición sin identidad humana válida aunque tenga certificado de sonda
- WHEN consulta detalle
- THEN recibe401

#### Scenario: Límite de entorno

- GIVEN entorno productivo
- WHEN intenta contexto de desarrollo en lugar de OIDC
- THEN no habilita acceso

### Requirement: Contexto instalado desde configuración del servidor {#REQ-detalle-sesion-ambito-003}

En Development/Testing, modo de lectura de prueba explícito MUST instalar sede/sonda desde configuración. Petición MUST NOT sustituir/ampliar ese contexto. En otros entornos MUST NOT instalarlo; MUST resolver identidad/ámbitos mediante OIDC. Configuración de prueba sola MUST NOT conceder acceso productivo; falta de identidad/contexto válido MUST producir401.
(Previously: todo entorno fuera de Development/Testing respondía401, incluso con identidad humana.)

#### Scenario: Ámbito de servidor en Development o Testing

- GIVEN modo de prueba con sede/sonda y sesión coincidente
- WHEN consulta endpoint histórico sin selector
- THEN obtiene200 con los cinco campos persistidos

#### Scenario: Petición que declara otro ámbito

- GIVEN modo de prueba con contexto configurado
- WHEN declara otra sede/sonda en cabecera/consulta
- THEN no sustituye el ámbito configurado ni accede al ajeno

#### Scenario: Production ignora la configuración

- GIVEN producción con configuración de sede/sonda de prueba y sin OIDC válido
- WHEN consulta detalle
- THEN obtiene401

#### Scenario: Development o Testing sin configuración de ámbito

- GIVEN desarrollo/pruebas sin contexto configurado ni OIDC válido
- WHEN consulta detalle
- THEN obtiene401
