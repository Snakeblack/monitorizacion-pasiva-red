# operacion-y-ciclo-datos Specification

## Purpose

Operar señales, retención y recuperación de autoridad/proyección con resultados verificables (S14–S16).

## Requirements

### Requirement: Reconciliación y alertas accionables {#REQ-operacion-y-ciclo-datos-001}

MUST exponer captura/parseo/descarte, emisión/spool/ACK, aceptación/rechazo/duplicado, pendiente/cuarentena/procesado, CDC/Kafka/sink, consultas, borrado, WAL/réplica/failover, backups/PITR y certificados. Contadores MUST tener unidades explícitas: paquetes, eventos y sesiones MUST NOT equipararse; transiciones MUST reconciliarse sin contar reintentos como trabajo nuevo. MUST permitir comprobar saldos por ámbito y periodo, incluyendo pendientes/fallidos/descartados. Cada alerta MUST declarar umbral, receptor, destinatario y runbook; fallos inducidos MUST alcanzar un receptor real de laboratorio. Logs/métricas MUST excluir payload/secretos/IP/MAC y etiquetas sin cota; ámbitos MUST limitarse al registro de sondas. Readiness MUST distinguir degradación de búsqueda de durabilidad de ingestión.

#### Scenario: Fallo en una etapa

- GIVEN sink caído o pérdida de spool/captura inducida
- WHEN se revisan señales y receptor
- THEN identifica etapa/unidad/saldo pendiente o perdido y entrega alerta con acción

### Requirement: Retención y supresión autoritativas {#REQ-operacion-y-ciclo-datos-002}

Sesiones MUST ser consultables30 días desde `startedAt`; con reloj UTC controlado MUST excluir/suprimir las anteriores a `now-30 días`. Borrado de sesión y registro outbox de supresión MUST ser atómicos y propagarse según `pipeline-busqueda`. MUST existir plazos explícitos de bandeja procesada, cuarentena resuelta, observaciones, auditoría, topics/outbox y backups por entorno; pendientes no resueltos MUST NOT purgarse automáticamente. Laboratorio MUST iniciar con procesados48 h, cuarentena resuelta/observaciones/copias7 días y auditoría 30 días; topics/outbox MUST limitarse por tiempo/bytes y checkpoint seguro, alertando presión antes de pérdida de recuperación. Antes de datos reales, producto/seguridad/cumplimiento MUST ratificar todos los plazos y barreras de replay. Fallo de borrado MUST reintentar y alertar, sin volver a exponer caducados.

#### Scenario: Caducidad y pendientes

- GIVEN sesión anterior al corte, otra exactamente en él y evento pendiente antiguo
- WHEN se ejecuta retención
- THEN suprime la anterior, conserva la del corte y mantiene/alerta el pendiente

### Requirement: Continuidad de autoridad y dependencias {#REQ-operacion-y-ciclo-datos-003}

PostgreSQL MUST disponer de HA gestionada apta o primaria/réplica con conmutación operada y prevención de dos escritores. Failover ordinario MUST ensayarse con objetivo≤15 min; reingesta/reinicio MUST conservar ACK/idempotencia. Caída de Kafka/Connect/Elasticsearch MUST conservar autoridad/outbox y hacer visible degradación hasta recuperación; cuotas/WAL MUST acotar crecimiento sin ocultar fallos. Configuración/offsets/ACLs de servicios MUST respaldarse; índices MUST poder reconstruirse. El objetivo99,9% mensual ordinario MUST medirse separado de desastres, sin atribuir SLA contractual.

#### Scenario: Fallo de nodo

- GIVEN carga activa y fallo de primaria
- WHEN se conmuta conforme al runbook
- THEN recupera servicio dentro del objetivo ensayado, sin dos primarias ni duplicación por reenvío

### Requirement: Copias y PITR sin resurrección {#REQ-operacion-y-ciclo-datos-004}

MUST existir copia base y WAL continuo fuera del dominio de fallo, restauración aislada y verificación de integridad/ACKs. Ensayo de desastre MUST medir RPO≤15 min de datos confirmados y RTO≤2h, incluida reaplicación de supresiones y recuperación/rebuild de proyección antes de lectura. Copia faltante/inválida/WAL discontinuo MUST alertar e impedir declarar recuperación apta. Runbook MUST identificar responsables, punto recuperado, pérdida efectiva y decisión de habilitación; un backup existente MUST NOT contarse como restauración probada.

#### Scenario: Restauración de histórico vencido

- GIVEN backup con sesiones ahora caducadas y supresiones posteriores
- WHEN se restaura y reconstruye consulta
- THEN reaplica corte/supresiones antes de exponer y publica pérdida/tiempos medidos
