# transporte-y-pki Specification

## Purpose

Proteger enlaces privados y operar certificados de sonda con EJBCA (F-08/S13).

## Requirements

### Requirement: TLS validado sin degradación {#REQ-transporte-y-pki-001}

Enlaces Angular/API/Keycloak, sonda/ingestión y enlaces de servicios PostgreSQL/Kafka/Connect/Elasticsearch/EJBCA MUST usar TLS con cadena y nombre de servidor validados. API/datos/administración MUST desplegarse en redes privadas, sin exposición pública directa. MUST NOT existir fallback en claro, aceptación universal de certificados ni configuración productiva que omita validación. TLS mínimo MUST ser1.2; material privado y credenciales MUST suministrarse por entorno/secretos fuera del repositorio, con permisos mínimos y rotación.

#### Scenario: Servidor inválido y downgrade

- GIVEN CA/nombre de servidor inválidos o intento de enlace en claro
- WHEN un componente conecta
- THEN rechaza el enlace y no reintenta omitiendo validación

### Requirement: Certificado ligado a la sonda {#REQ-transporte-y-pki-002}

EJBCA MUST emitir certificados cliente de máquina con propósito de autenticación y vínculo verificable al registro sede/sonda. Ingestión MUST comprobar cadena, vigencia, propósito y estado de revocación antes de aceptar lotes; MUST NOT confiar solo en CN o cabeceras reenviadas sin proxy confiable. Certificado ausente/desconocido/vencido/revocado MUST impedir aceptación. Estado de revocación MUST tener frescura máxima5 min; si no puede acreditarla MUST cerrar ingestión afectada y alertar, sin ACK falso. Autorización de origen MUST seguir `identidad-y-acceso`.

#### Scenario: Certificados rechazados

- GIVEN certificado ausente, de otra CA, vencido, revocado o sin registro confiable
- WHEN intenta ingestión
- THEN no acepta eventos ni emite ACK durable

### Requirement: Ciclo de emisión y renovación probado {#REQ-transporte-y-pki-003}

MUST ofrecer procedimientos reproducibles con EJBCA para registrar/empezar sonda, emitir, renovar antes de caducar, revocar y sustituir credenciales comprometidas. Renovación MUST conservar identidad estable de eventos/spool; cambio de sede/sonda MUST exigir reprovisión explícita. Revocación/rotación de CA MUST actualizar confianza y alertas con responsable; MUST NOT recuperar certificados deshabilitados al reiniciar. Laboratorio MUST ensayar emisión y revocación contra EJBCA real; un certificado estático autofirmado MUST NOT sustituir esa integración.

#### Scenario: Renovación con backlog

- GIVEN sonda con spool pendiente y certificado próximo a caducar
- WHEN renueva y reenvía
- THEN conserva identidad/eventos y entrega mediante certificado nuevo válido

#### Scenario: Revocación operada

- GIVEN certificado emitido por EJBCA y posteriormente revocado
- WHEN se actualiza estado de revocación dentro de5 min
- THEN nuevos intentos con ese certificado se rechazan y la alerta llega al receptor

### Requirement: Privilegio mínimo de infraestructura {#REQ-transporte-y-pki-004}

Credenciales de CDC, topics, sink, consulta, administración y CA MUST ser independientes con operaciones mínimas. API MUST NOT distribuir credenciales de Elasticsearch/Kafka al navegador. Puertos de administración MUST restringirse a operación; copia/rotación MUST proteger claves y auditar acciones sin secretos. Configuración faltante de confianza/permisos MUST ser error explícito, sin credenciales comunes de producción.

#### Scenario: Cliente humano directo al backend

- GIVEN navegador o credencial de solo consulta API
- WHEN intenta escribir topic/índice o administrar CA
- THEN no dispone de credenciales/permisos para esa acción
