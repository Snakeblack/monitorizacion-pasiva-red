# Glosario

| Término | Uso en el proyecto |
|---|---|
| Sonda / probe | Proceso cercano al segmento que observa una copia de tráfico y emite metadatos. |
| SPAN / TAP | Puerto espejo / dispositivo que entrega una copia sin interponer la sonda en el tráfico. |
| Sesión | Flujo TCP/UDP bidireccional inferido por 5-tupla canonizada y ámbito sitio/sonda/VLAN; cierre por inactividad o al llegar a 1 h. No equivale a reconstrucción exacta de TCP. |
| Observación de dispositivo | Evidencia pasiva de MAC/IP y ámbito; no equivale siempre a un dispositivo único. Sin MAC no crea candidato. |
| Candidato de dispositivo | Agrupación provisional por MAC + sitio + sonda + VLAN; solo un administrador confirma o rechaza su incorporación al inventario. IP es asociación temporal. |
| Bandeja de entrada durable | Almacén que confirma lotes con identificador estable antes de proyectarlos; PostgreSQL es el candidato provisional de S00, sujeto a validación integrada en S17. |
| Spool local | Cola persistente por sonda hasta ACK; capacidad calculada para al menos 4 h a tasa sostenida más ráfaga 5× de 15 min, con pérdida visible al agotarse. |
| Kafka topic / partición / offset | Conceptos del broker Kafka; opción condicionada por un cuello demostrado de amortiguación o replay. |
| Avro / Schema Registry | Formato binario con esquema y servicio de contratos; diferidos junto con Kafka. |
| ClickHouse / MergeTree | Motor analítico columnar y familia de tablas; opción si la carga medida en S17 supera la solución inicial. |
| PostgreSQL | Primer candidato para bandeja, sesiones e inventario; S00 modela provisionalmente 30 días y unas 300 M sesiones, y S17 comprueba el comportamiento del sistema integrado. |
| OIDC / RBAC | Inicio de sesión basado en tokens / autorización de acciones por roles. |
| PKI / EJBCA / X.509 | Infraestructura de certificados / CA candidata si la corporativa no sirve / formato de certificado. |
| mTLS / SAN / EKU | TLS mutuo en fronteras acordadas / identidad nombrada y usos permitidos del certificado. |
| Cuarentena | Registro durable de eventos inválidos y causa, con acceso restringido; no presupone un topic DLQ. |
| RPO / RTO | Objetivos propuestos para desastre: pérdida ≤ 15 min de datos confirmados por ACK y recuperación ≤ 2 h, pendientes de ensayo y aceptación. No incluyen paquetes que la sonda no capturó. |
| SLO / función de aptitud | Objetivo medible de servicio / comprobación automatizable de una propiedad arquitectónica. |

Fuente: caso pp. 2–13 y análisis pp. 10–11, ambos orientativos; [ADR-010](../architecture/decisions/ADR-010.md) decide las piezas candidatas y [ADR-013](../architecture/decisions/ADR-013.md) fija objetivos de producción propuestos. [ADR-012](../architecture/decisions/ADR-012.md) conserva las reglas no sustituidas. El [modelo S00](../architecture/modelo-capacidad-s00.md) sustenta la [decisión provisional](../architecture/decisions/ADR-014.md); las [validaciones de producción](../roadmap-gaps.md#validaciones-para-liberar-la-primera-entrega-de-producción), incluida S17, determinarán si se cumplen los objetivos.
