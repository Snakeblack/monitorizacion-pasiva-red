# sesiones-canonicas Specification

## Purpose

Definir metadatos de sesión capturada y una representación consultable compatible con las sesiones sintéticas existentes, sin modificar su contrato v1.

## Requirements

### Requirement: Contrato capturado versionado {#REQ-sesiones-canonicas-001}

El evento capturado MUST usar la envoltura `contrato-ingestion-v1` sin ampliarla. Su `data` MUST contener exactamente `kind: "captured-session"`, `version: 1`, `sourceIp`, `destinationIp`, `sourcePort`, `destinationPort`, `protocol`, `startedAt`, `endedAt`, `vlanId`, `correlationVersion: 1`, `inferred: true`, `partial`, `closeReason`, `packetCount` y `byteCount`. IP MUST ser IPv4/IPv6 válida; puertos MUST ser enteros 0–65535; protocolo MUST ser TCP/UDP; tiempos MUST ser RFC3339 UTC `Z`, precisión máxima milisegundos y fin ≥ inicio; VLAN MUST ser entero 0–4094 o `null` («sin etiqueta»); contadores MUST ser enteros no negativos. `partial` MUST ser booleano; `closeReason` MUST ser `inactivity`, `max-duration`, `shutdown` o `restart`. Versiones/campos inválidos MUST seguir la cuarentena de `bandeja-ingestion-durable`, sin reinterpretación silenciosa.

#### Scenario: Sesión capturada válida

- GIVEN un evento con todos los campos válidos y VLAN sin etiqueta
- WHEN el worker valida `data`
- THEN reconoce una sesión inferida versionada y conserva VLAN `null`

#### Scenario: Contrato inválido

- GIVEN versión desconocida, contador negativo, IP inválida, campo adicional o fin anterior al inicio
- WHEN el worker valida el evento ya aceptado
- THEN no crea sesión y registra su causa permanente

### Requirement: Identidad y adaptación compatibles {#REQ-sesiones-canonicas-002}

La identidad canónica MUST ser `(siteId,sensorId,eventId)`; MUST conservar `occurredAt` y `data` aceptados. El modelo consultable MUST normalizar tiempos e IP para comparación, conservando protocolo y extremos del contrato; MUST distinguir procedencia `synthetic`/`capture`. La adaptación de `synthetic-session` v1 MUST respetar todos sus campos/límites actuales, tratar VLAN desconocida como `null` y MUST NOT inventar captura, contadores o inferencia para un fixture. Ampliaciones futuras MUST usar una versión/discriminador distinto.

#### Scenario: Fixture histórico compatible

- GIVEN una sesión sintética v1 válida ya persistida
- WHEN se migra/adapta al modelo consultable
- THEN conserva identidad, `occurredAt` y `data`, y permite filtrar sus extremos/tiempos

#### Scenario: Misma identidad textual en fuentes distintas

- GIVEN igual `eventId` en dos sedes/sondas
- WHEN ambas se normalizan
- THEN siguen siendo dos sesiones independientes

### Requirement: Semántica temporal explícita {#REQ-sesiones-canonicas-003}

La búsqueda temporal MUST seleccionar sesiones **iniciadas**: `startedAt >= from AND startedAt < to`, comparando instantes UTC. MUST NOT sustituirlo por `occurredAt`, `endedAt` o solapamiento. Detalle y listado MUST conservar los tiempos originales; la UI MUST rotular «sesiones iniciadas» y distinguir sesión inferida/parcial cuando corresponde.

#### Scenario: Fronteras del intervalo

- GIVEN sesiones iniciadas exactamente en `from`, antes de `from` pero terminadas dentro, y exactamente en `to`
- WHEN se consulta `[from,to)`
- THEN solo entra la iniciada en `from`

#### Scenario: Direcciones equivalentes

- GIVEN dos representaciones textuales válidas de la misma IPv6
- WHEN se filtra esa IP
- THEN la comparación normalizada encuentra ambas sin cambiar el `data` guardado
