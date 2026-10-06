# identidad-y-acceso Specification

## Purpose

Aplicar Keycloak/OIDC y la matriz F-07 por operación y ámbito, separando personas de sondas (S12).

## Requirements

### Requirement: Identidad humana validada {#REQ-identidad-y-acceso-001}

Keycloak MUST suministrar OIDC para personas. La API MUST validar emisor/audiencia configurados, firma, vigencia y sujeto; MUST NOT confiar en cabeceras de roles/ámbitos del cliente. Token ausente/inválido/caducado MUST responder401; validación indisponible sin claves confiables vigentes MUST cerrar acceso. Claves confiables cacheadas MUST respetar su vigencia y rotación. El modo de lectura configurado de Development/Testing MUST ser explícito y MUST NOT habilitarse en producción. Configuración inválida/mezcla insegura MUST impedir arranque productivo.

#### Scenario: Token inválido o IdP no verificable

- GIVEN firma/emisor/audiencia inválidos, expiración o ausencia de claves vigentes
- WHEN se solicita una operación humana
- THEN no se concede identidad y responde401

### Requirement: Matriz de roles y ámbitos {#REQ-identidad-y-acceso-002}

Roles MUST ser `analista`, `auditor`, `administrador-inventario`; permisos y lista de pares sede/sonda MUST proceder de configuración/claims validados de Keycloak. La API MUST denegar por defecto y comprobar permiso/ámbito en cada operación, incluido cursor y fusión. Sin permiso MUST responder403; varios roles MUST unir solo permisos declarados y ámbitos concedidos. MUST NOT ofrecer gestión de roles, exportación masiva ni endpoints humanos para certificados de sonda.

| Operación | Analista | Auditor | Administrador |
|---|---|---|---|
| Sesiones/detalle e inventario confirmado | Sí | Sí | Sí |
| Candidatos/observaciones | Sí | No | Sí |
| Crear/corregir/confirmar/rechazar/fusionar | No | No | Sí |

#### Scenario: Matriz completa

- GIVEN token válido de cada rol y ámbito A
- WHEN prueba operaciones de la matriz y ámbito B
- THEN solo concede las celdas permitidas dentro de A y deniega B

### Requirement: Identidad de sonda separada {#REQ-identidad-y-acceso-003}

Certificado mTLS validado y registro de sonda MUST establecer sede/sonda confiables para ingestión. MUST cotejar la envoltura con esa identidad; falta/discordancia MUST conservar401/403 sin persistencia. Sonda MUST poder únicamente entregar lotes; certificado de sonda MUST NOT autorizar consulta/inventario. Token humano MUST NOT sustituir certificado de ingestión. Desarrollo MUST usar identidades de prueba delimitadas al entorno.

#### Scenario: Cruce de funciones

- GIVEN certificado de sonda consultando sesiones o token humano enviando ingestión sin certificado
- WHEN se evalúa autorización
- THEN se rechaza sin datos ni aceptación durable

### Requirement: Inicio de sesión y denegaciones seguras {#REQ-identidad-y-acceso-004}

Angular MUST usar Authorization Code con PKCE, sin secreto de cliente, validar retorno/state y ofrecer cierre de sesión. MUST NOT persistir tokens en almacenamiento local permanente ni registrar tokens. UI MUST mostrar funciones según permisos sin sustituir servidor; caducidad/cambio de identidad MUST limpiar datos/cursor y requerir sesión válida. Denegaciones MUST auditar actor cuando conocido, operación, tiempo y causa/correlación mínima; MUST NOT incluir tokens, IP/MAC de sesiones, payload o secretos.

#### Scenario: Sesión termina con datos visibles

- GIVEN lista/detalle cargados y credencial expirada o cierre de sesión
- WHEN se renueva el estado de autenticación
- THEN retira datos/cursor anteriores y exige identidad válida para volver a leer
