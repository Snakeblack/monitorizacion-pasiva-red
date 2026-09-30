# contrato-sesion-sintetica Specification

## Purpose

Definir qué objetos `data` aceptados por S02 reconoce S03 para proyectar una sesión sintética, sin restringir la ingestión.

## Requirements

### Requirement: Sesión sintética v1 delimitada {#REQ-contrato-sesion-sintetica-001}

S03 MUST reconocer exclusivamente objetos `data` con todos estos campos obligatorios y ningún campo adicional: `kind` igual a `"synthetic-session"`, `version` igual a 1, `sourceIp` y `destinationIp` como direcciones IP válidas, `sourcePort` y `destinationPort` como enteros entre 0 y 65535, `protocol` igual a `"TCP"` o `"UDP"`, y `startedAt` y `endedAt` como instantes RFC 3339 UTC terminados en `Z`, con precisión máxima de milisegundos y fin mayor o igual al inicio. MUST NOT añadir correlación ni VLAN al contrato.

#### Scenario: Sesión válida

- GIVEN `data` con exactamente los campos requeridos, valores válidos y fin igual o posterior al inicio
- WHEN S03 evalúa el evento aceptado
- THEN reconoce una sesión sintética v1 proyectable

#### Scenario: Límites válidos

- GIVEN sesiones con puertos 0 o 65535, protocolo TCP o UDP, y fin igual al inicio
- WHEN S03 valida sus campos
- THEN esos valores son admitidos

#### Scenario: Objeto no reconocido o inválido

- GIVEN un discriminador o versión desconocidos, campo ausente o adicional, IP inválida, puerto no entero o fuera de rango, protocolo distinto, instante sin UTC `Z`, precisión superior a milisegundos o fin anterior al inicio
- WHEN S03 evalúa `data`
- THEN no lo reconoce como sesión proyectable

### Requirement: Validación posterior a la aceptación {#REQ-contrato-sesion-sintetica-002}

La validación S03 MUST realizarse sobre eventos ya aceptados. La ingestión S02 MUST seguir aceptando cualquier objeto JSON `data` conforme a `contrato-ingestion-v1`; MUST NOT rechazar un lote por no cumplir el contrato de sesión sintética.

#### Scenario: Datos arbitrarios aceptados

- GIVEN un lote S02 válido con `data` arbitrario que no cumple el contrato sintético
- WHEN la ingestión confirma durablemente el lote
- THEN conserva su HTTP 200 de cuerpo vacío y el evento aceptado
- AND S03 no proyecta ese evento
