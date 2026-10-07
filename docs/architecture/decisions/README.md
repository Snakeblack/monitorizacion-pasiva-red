# Registro de decisiones

[ADR-013](ADR-013.md) define los objetivos de la primera entrega de producción. [ADR-014](ADR-014.md) conserva la elección histórica provisional S00. [ADR-015](ADR-015.md)–[ADR-019](ADR-019.md) adoptan autoridad PostgreSQL, conectores de búsqueda, revisiones/barreras, consulta finita, Keycloak/EJBCA y sonda/continuidad; S17/S18 siguen pendientes. [ADR-010](ADR-010.md) conserva la selección histórica; [ADR-011](ADR-011.md) define observabilidad y [ADR-020](ADR-020.md) añade los exportadores de la tubería de búsqueda. [ADR-012](ADR-012.md) conserva el historial anterior, cuyas cifras de escala y continuidad fueron sustituidas. Las confirmaciones restantes están en [brechas](../../roadmap-gaps.md).

| ADR | Estado y alcance |
|---|---|
| [001](ADR-001.md) | .NET modular; revisado para limitar procesos iniciales |
| [002](ADR-002.md) | Red privada; revisado por ADR-010, sin mTLS universal por defecto |
| [003](ADR-003.md) | Kafka; sustituido como elección inicial, evolución condicional |
| [004](ADR-004.md) | Avro/Registry; sustituido como elección inicial |
| [005](ADR-005.md) | ClickHouse/PostgreSQL; sustituido como elección inicial; revisar con evidencia S17 |
| [006](ADR-006.md) | Workers separados; revisado por ADR-010 |
| [007](ADR-007.md) | Esquema ClickHouse; no vigente como diseño inicial |
| [008](ADR-008.md) | EJBCA/mTLS universal; revisado por ADR-010 |
| [009](ADR-009.md) | Reutilización de plataforma; revisado por ADR-010 y ADR-011 |
| [010](ADR-010.md) | Arquitectura modular y elección provisional de almacén; alternativas y coste operativo |
| [011](ADR-011.md) | Aceptado: observabilidad compartida; Prometheus/Grafana solo si cubren un hueco demostrado |
| [012](ADR-012.md) | Historial del contrato de piloto; cifras de escala y continuidad sustituidas por ADR-013 |
| [013](ADR-013.md) | Vigente: objetivos de producción y criterios de validación empírica S17 |
| [014](ADR-014.md) | Histórico: PostgreSQL provisional S00; decisión sustituida por ADR-015 |
| [015](ADR-015.md) | Aceptado: autoridad PostgreSQL y Debezium/Kafka/Connect/Elasticsearch |
| [016](ADR-016.md) | Aceptado: revisiones semánticas y barreras persistentes de supresión |
| [017](ADR-017.md) | Aceptado: consulta autorizada, PIT y cursor finito |
| [018](ADR-018.md) | Aceptado: identidades humanas Keycloak y máquinas EJBCA |
| [019](ADR-019.md) | Aceptado: sonda aislada, spool y continuidad operada |
| [020](ADR-020.md) | Aceptado: exportadores de Kafka/Connect/Elasticsearch y Prometheus solo de laboratorio; complementa ADR-011 |

Cada decisión futura registrará evidencia, coste recurrente, responsable, consecuencias y plan de reversión. El índice separa la propuesta histórica del contrato vigente.
