# aptitud-y-liberacion Specification

## Purpose

Reproducir el producto integrado en local, ejecutar aceptación completa y condicionar la liberación a evidencia real (K04/S17–S18).

## Requirements

### Requirement: Entorno integrado reproducible {#REQ-aptitud-y-liberacion-001}

Checkout limpio MUST ofrecer configuración declarativa con versiones fijadas y healthchecks de PostgreSQL, Kafka, Connect/Debezium, Elasticsearch, Keycloak y EJBCA reales, más API/worker/Angular y sonda/generador. MUST aprovisionar migraciones, topics, conectores, mappings/alias, realm/roles/ámbitos y certificados de laboratorio; MUST documentar requisitos, arranque, espera, emisión fixture, inspección y parada. Reset destructivo de volúmenes MUST ser explícito separado del arranque ordinario. Dependencia no disponible MUST producir fallo identificable, sin éxito simulado. Entorno local MUST aplicar TLS/mTLS y acceso privado de `transporte-y-pki`; credenciales de laboratorio MUST identificarse y MUST NOT convertirse en valores productivos.

#### Scenario: Vertical real desde cero

- GIVEN Docker/dependencias funcionales y checkout limpio
- WHEN se arranca y envía fixture por ingestión
- THEN verifica ACK, autoridad/outbox, Kafka, documento y listado/detalle Angular con identidad Keycloak

### Requirement: Pruebas funcionales y fallos finitos {#REQ-aptitud-y-liberacion-002}

MUST existir suite reproducible Strict TDD/CI de .NET/Angular e integración real para filtros/límites/cursor/aislamiento, captura/correlación, duplicados/reinicio/ACK perdido, cuarentena, permisos, certificados, lag/replay/borrado/rebuild, inventario y recuperación. Cada prueba MUST declarar dataset, reloj/semilla cuando aplique, dependencia y resultado; espera/reintento MUST tener plazo finito. Mocks MUST NOT sustituir la evidencia integrada de servicios adoptados. Ausencia de Docker/tshark/servicio MUST informarse como no ejecutado/bloqueado, nunca aprobado.

#### Scenario: Fallo de dependencia

- GIVEN Kafka/EJBCA o `tshark` indisponible para una prueba necesaria
- WHEN se ejecuta la suite
- THEN reporta causa/plazo y no acredita esa integración como superada

### Requirement: Ensayo representativo S17 {#REQ-aptitud-y-liberacion-003}

S17 MUST cargar30 días completos con distribución declarada, cuatro sedes/ocho sondas y muestras autorizadas; después MUST mantener10 M sesiones/día durante72 h, ráfagas5× de 15 min y20 consultas simultáneas (detalle/24 h/30 días selectivos, IP frecuentes/escasas), con retención, failover y PITR activos. MUST medir p95 consulta≤2s/5s respectivamente, timeout≤10s, frescura p95≤60 s desde ACK y drenaje≤15 min tras ráfaga, pérdida reconciliada y continuidad definida. MUST publicar hardware/red, eventos/paquetes por sesión, tamaños/planes, CPU/RAM/I/O/disco/WAL/copias/índice/Kafka, crecimiento, coste y esfuerzo operativo. Cuotas/spool MUST ajustarse al perfil;500 eventos/min×8=5,76M/día MUST NOT presentarse como soporte para10 M sesiones/día. Ensayo parcial MUST NOT extrapolar aptitud; objetivo fallido MUST exigir ajuste y repetición.

#### Scenario: Perfil incompleto o fallo

- GIVEN menos de30 días/72 h/volumen o p95/pérdida/recuperación fuera de objetivo
- WHEN se evalúa S17
- THEN publica alcance medido y bloquea declaración de aptitud hasta reensayo completo

### Requirement: Paquete privado y decisión S18 {#REQ-aptitud-y-liberacion-004}

Entrega MUST incluir binarios/configuración por entorno, secretos externos, migraciones/reversión, runbooks, evidencias F-01–F-09/G-01–G-09 y responsables. ADR sucesores/configuración/documentación MUST reflejar PostgreSQL autoritativo, Kafka/Connect/Elasticsearch, Keycloak y EJBCA adoptados sin reescribir historia. Datos reales MUST habilitarse solo tras matriz/plazos/seguridad/redes/guardia validados y aceptación de producto/operación; S17/continuidad MUST estar aprobados para declarar producción apta. MUST registrar bloqueo cuando faltan evidencias/aceptaciones. Reversión MUST preservar autoridad/outbox/offsets/supresiones y nunca habilitar credenciales de desarrollo.

#### Scenario: Puerta de liberación

- GIVEN paquete funcional pero políticas, guardia o S17 pendientes
- WHEN se intenta liberar
- THEN conserva paquete/evidencias revisables y registra bloqueo sin declarar aptitud ni conectar datos reales
