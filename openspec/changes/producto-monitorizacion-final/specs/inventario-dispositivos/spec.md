# inventario-dispositivos Specification

## Purpose

Conservar observaciones y candidatos separados del inventario confirmado, con edición/fusión manual auditada (F-06/S10–S11).

## Requirements

### Requirement: Observación versionada e idempotente {#REQ-inventario-dispositivos-001}

El evento MUST usar `contrato-ingestion-v1` con `data` delimitado: `kind: "device-observation"`, `version: 1`, `observedAt`, `ip`, `mac` y `vlanId`. Tiempo MUST cumplir UTC/milisegundos; IP MUST ser válida; MAC MUST ser dirección unicast de 48 bits o `null`; VLAN MUST ser0–4094 o `null`. El worker MUST conservar observación por identidad de evento, normalizar MAC/IP para comparación y registrar la asociación IP como temporal (`firstSeen`/`lastSeen`), sin inferir propietario. MAC ausente MUST conservar observación sin candidato. Duplicación/replay MUST NOT multiplicar observaciones/asociaciones ni modificar el inventario. Contrato inválido MUST ir a cuarentena.

#### Scenario: Observación repetida y sin MAC

- GIVEN evento válido reenviado y otro válido sin MAC
- WHEN el worker los procesa
- THEN conserva una observación por identidad y solo el primero puede generar candidato

### Requirement: Candidatos por identidad observada {#REQ-inventario-dispositivos-002}

El candidato MUST identificarse por MAC normalizada+sede+sonda+VLAN, incluida «sin etiqueta». Cambios de IP MUST actualizar asociaciones temporales del mismo candidato; misma MAC en otro ámbito MUST ser otro candidato. NAT, MAC aleatoria, IP/nombre iguales MUST NOT fusionar ni confirmar automáticamente. API/UI MUST listar candidatos/observaciones solo para analista y administrador autorizados, con paginación1–100 y filtro de ámbito; auditor MUST recibir403.

#### Scenario: IP cambia y ámbito cambia

- GIVEN MAC con dos IP sucesivas y la misma MAC en otra VLAN
- WHEN llegan observaciones
- THEN el primer candidato conserva ambos periodos y la otra VLAN permanece separada

### Requirement: Inventario confirmado manualmente {#REQ-inventario-dispositivos-003}

Solo `administrador-inventario` con ámbito autorizado MUST poder crear/corregir dispositivos, confirmar/rechazar candidatos y fusionarlos explícitamente con un dispositivo destino. MUST existir identificador estable de dispositivo, nombre no vacío ≤128 caracteres, descripción ≤1024 y revisión para concurrencia. Confirmación/fusión MUST validar todos los ámbitos afectados; MUST NOT ampliar permisos ni destruir observaciones. Rechazado MUST permanecer rechazado ante nuevas observaciones hasta decisión manual posterior. Asociación/fusión MUST ser transaccional; candidato ya vinculado a otro dispositivo o revisión obsoleta MUST producir409 sin cambios parciales. Reglas MUST ser compartidas por API/worker.

#### Scenario: Corrección manual y observaciones posteriores

- GIVEN dispositivo corregido y candidato rechazado
- WHEN llegan observaciones adicionales
- THEN no sobrescriben la corrección ni revierten el rechazo

#### Scenario: Conflicto concurrente

- GIVEN dos decisiones sobre la misma revisión de candidato/dispositivo
- WHEN ambas intentan confirmar cambios distintos
- THEN una confirma y la obsoleta recibe409 sin fusión parcial

### Requirement: Auditoría inseparable y lectura permitida {#REQ-inventario-dispositivos-004}

Cada cambio manual MUST confirmar junto con auditoría de actor OIDC, acción, instante UTC, IDs afectados, revisiones y cambios mínimos necesarios. Fallo de auditoría MUST revertir la operación. Registro MUST quedar restringido a operación sin un panel nuevo ni payload/secretos. Los tres roles MUST consultar inventario confirmado dentro de sus ámbitos; listados MUST tener página≤100/timeout≤10s. UI MUST distinguir éxito, vacío, conflicto y denegación por texto.

#### Scenario: Registro obligatorio

- GIVEN cambio autorizado y fallo del almacenamiento de auditoría
- WHEN se intenta confirmar
- THEN no queda cambio de inventario sin auditoría
