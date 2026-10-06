# Exploración: cierre de arquitectura y slices de producto

## Estado actual

S01–S04 están archivados y aportan una base ASP.NET Core modular, dominio separado, PostgreSQL con bandeja durable y proyección idempotente, endpoint de detalle limitado a Development/Testing y una SPA Angular de detalle. La proyección guarda `occurred_at_text` y el contrato sintético completo como JSONB; no tiene columnas tipadas ni una consulta de lista. Angular solo registra `sessions/:eventId`. La búsqueda requerida por el caso (PDF, p. 3; índice con Kafka Connect a Elasticsearch, pp. 8–10; OIDC/Keycloak, pp. 11–13) aún no existe.

ADR-010/014 y G-08 difieren Kafka y Elasticsearch, pero el alcance aceptado ahora adopta esa integración y Keycloak/EJBCA. Se conserva PostgreSQL como autoridad de sesión e inventario y Elasticsearch como proyección reemplazable para búsqueda. La escritura de sesión y evento de publicación se confirma mediante outbox en la misma transacción; Debezium/Kafka Connect publica el outbox en Kafka y el sink de Kafka Connect proyecta Elasticsearch. Así no se intenta una doble escritura HTTP no atómica. La clave del documento se deriva de site/sensor/event (identidad completa), con borrado y reconstrucción explícitos. Debezium añade una dependencia de infraestructura al flujo outbox, pero encaja con PostgreSQL autoritativo y elimina la brecha de publicación; un publicador propio con checkpoint crea trabajo de recuperación y concurrencia que no reduce el coste total.

## Áreas afectadas

- `docs/development/slices.md`, `docs/roadmap.md`, `docs/roadmap-gaps.md` — secuencia, responsabilidades y brechas deben adoptar las nuevas piezas sin renumerar S05–S18.
- `docs/architecture/decisions/` — añadir ADR sucesores a ADR-010/014 para búsqueda/proyección, broker/conectores, proveedor OIDC y CA; los ADR archivados permanecen históricos.
- `src/Monitoring.Domain/Sessions/SyntheticSessionContract.cs` — hoy define el contrato de datos sintéticos; S06 deberá convertir captura a sesión canónica sin ampliar implícitamente este contrato versionado.
- `src/Monitoring.Persistence/Sessions/SessionProjectionEntity.cs`, `SessionProjectionEntityConfiguration.cs`, `SessionProjector.cs`, `SessionReader.cs` y migraciones — modelo JSONB actual carece de timestamps tipados y campos de búsqueda; la autoridad y su outbox requieren migraciones aditivas y consultas acotadas.
- `src/Monitoring.Host/Sessions/SessionEndpoint.cs`, `Program.cs` — endpoint solo detalle y contexto de prueba; faltan búsqueda, límites en servidor, OIDC/RBAC y wiring de proyección.
- `src/monitoring-web/src/app/app.routes.ts`, `session-detail/` — falta listado con filtros, estados y cursor.
- `compose*.yaml` (no existe actualmente), configuración de Connect y pruebas en `tests/Monitoring.Tests/` — topología reproducible de dependencias y verificación de reintentos, offsets, rebuild y aislamiento.

## Slices implementables

Conservar S05–S18 y su intención. Insertar K01–K04 como dependencias explícitas del camino de consulta, e integrar sus prerequisitos en las fichas existentes:

| Slice | Entrega y dependencia propuesta |
|---|---|
| S05–S08 | Mantener IDs y alcance. S05 extrae metadatos con `tshark`; S06 crea sesiones inferidas; S07 conserva/reintenta lotes; S08 reconcilia errores. S06 depende además del contrato canónico decidido en S02. Los eventos proyectables se escriben en PostgreSQL y outbox en la transacción existente. |
| K01 | ADR sucesores adoptan PostgreSQL autoritativo, outbox CDC, Kafka, Debezium/Kafka Connect y Elasticsearch de consulta; definen clave documental completa, tombstones/borrado, reindexado, retención duplicada, ACL/TLS y operación. Cierra primero la arquitectura y precede a implementación K02. |
| K02 | Esquema/migración outbox y captura CDC desde PostgreSQL a topic Kafka; idempotencia, orden por clave y recuperación del offset. Depende de K01 y S03/S08; se publica solo tras commit autoritativo. |
| K03 | Kafka Connect sink a índice Elasticsearch versionado, mappings explícitos, clave tenant/site/sensor/event, actualización idempotente, borrado, DLQ/retry y procedimiento de bootstrap/rebuild. Depende de K02. Elasticsearch nunca autoriza ni reemplaza la autoridad PostgreSQL. |
| S09 | Consulta HTTP paginada con filtros temporales/IP/protocolo/puerto y límites del roadmap; backend consulta Elasticsearch y conserva el detalle/ámbito confiable. Depende de S03/S04 y K03; definir discrepancia de frescura y fallback/estado de proyección. |
| K04 | Despliegue local reproducible de PostgreSQL, Kafka, Connect/Debezium y Elasticsearch más generador sintético; healthchecks, configuración declarativa y reset de volúmenes documentado. Infraestructura/CI disponible para el resto de slices. |
| S10–S11 | Candidatos e inventario manual/auditado; PostgreSQL autoritativo. Dependen de S05/S08 y K01 solo para los eventos/proyecciones que realmente se busquen. |
| S12–S13 | Keycloak OIDC y roles en API (S12), TLS y mTLS de sondas con EJBCA (S13); conservar IdP/CA como dependencias adoptadas según el alcance y validar ciclo de certificados. UI nunca decide permisos. |
| S14–S18 | Métricas, retención, recuperación, aptitud y liberación conservan IDs; ampliar pruebas para lag/offset/replay, borrado/rebuild y costes de la proyección. S17 mide el perfil del sistema; ninguna aptitud se presume por incorporar más componentes. |

Para cumplir «primero arquitectura y dependencias», el orden release comienza K01 (decisiones y contratos), K04 como habilitador de entorno, luego S05–S08 y K02/K03 hasta cerrar la ruta de proyección, S09 y UI, y finalmente S10–S18 respetando prerequisitos funcionales. K04 puede prepararse junto con K01, pero K02 no precede a la decisión documentada. Las consultas con cursor de Elasticsearch deben usar PIT + `search_after`, orden determinista y tope de página; filtros de más de 24 h requieren sitio/sonda e IP de extremo, límites de timeout y ventana conforme a S09. Elasticsearch no ofrece transacción con PostgreSQL: el diseño acepta consistencia eventual y necesita medir/documentar lag, replay y resultados temporalmente incompletos.

## Alternativas consideradas

1. **PostgreSQL autoritativo + outbox CDC → Kafka → Connect → Elasticsearch (recomendado).** Conserva la semántica de commit actual y habilita replay, índice buscable y consumers desacoplados; añade cuatro servicios conceptuales, duplicación de datos y consistencia eventual que requieren operación y reconciliación.
2. **Doble escritura directa desde el worker a PostgreSQL y Kafka/Elasticsearch.** Menos infraestructura de CDC, pero cada fallo entre commits deja una divergencia difícil de reparar; rechazada por pérdida parcial y acoplamiento al sink.
3. **Publicador de outbox propio con checkpoints, PostgreSQL → Kafka → Connect → Elasticsearch.** Mantiene atomicidad y evita Debezium, pero transfiere al producto locking, checkpoints, snapshots, retención/WAL y recuperación CDC; mayor código específico sin ventaja operativa demostrada.
4. **PostgreSQL como única búsqueda, sin broker ni Elasticsearch.** Es la ruta más simple y consistente, pero contradice el alcance funcional actualizado de adoptar Kafka/Connect/Elasticsearch y la ampliación concreta del caso; no es la ruta seleccionada.

Para identidad, el alcance acepta Keycloak y EJBCA: Keycloak sirve OIDC de personas y roles validados por API; EJBCA emite y revoca certificados de máquina para mTLS según S13. Reutilización corporativa puede ser adaptador/configuración si satisface estas decisiones, pero no se difiere la capacidad. No se comparte DLL de dominio con la sonda: los contratos versionados y mapeo en fronteras mantienen independientes los despliegues.

## Recomendación

Proponer primero decisiones ADR sucesoras firmes y contratos de sesión, outbox, clave de documento, permisos y consistencia. Después implementar el camino vertical más pequeño: evento sintético aceptado → proyección transaccional PostgreSQL + outbox → Kafka CDC → Elasticsearch sink → API de búsqueda con tenant/site/sensor y filtros acotados → lista Angular y detalle existente. Este camino valida identidad, mapeo, replay, filtros y paginación antes de sumar captura real, dispositivos y operación completa. A continuación se implementan S05–S08 usando el contrato ya probado y S10–S18 conservando aceptación y dependencias actuales.

Verificación E2E reproducible en una máquina preparada: `docker compose up -d` para Postgres/Kafka/Connect/Elasticsearch/Keycloak/EJBCA de laboratorio; migrar API; emitir fixture estable por lote; comprobar ACK y fila autoritativa, evento/topic, documento indexado y misma clave tras reenvío; consultar API con cada filtro/cursor y cargar la SPA; probar ámbito cruzado 401/403, caída/reinicio de Connect, lag, replay, tombstone y rebuild; ejecutar integración .NET y tests Angular. Las pruebas de captura añaden `tshark` y fixture PCAP autorizado/sintético con salida fijada. El host actual tiene .NET 10.0.303, Node 24.16.0 y npm 12.0.2; Docker está instalado pero su daemon no responde, y `tshark` falta. Por ello solo se confirmó disponibilidad de ejecutables, no se ejecutaron builds, pruebas ni E2E; el stack de servicios y el flujo de captura requieren preparación antes de reproducirlo.

## Riesgos

- Kafka Connect/Debezium y Elasticsearch crean lag eventual, retención duplicada, offsets/WAL, esquemas/mappings y procedimientos adicionales; K01/K03/S14–S17 deben cerrar operación, seguridad, costes y evidencia.
- Las sesiones actuales solo guardan JSONB/texto y el endpoint permanece limitado a Development/Testing; una migración aditiva y controles OIDC/RBAC de S12 son necesarios antes de consulta de personas.
- Borrado de 30 días debe propagarse a Elasticsearch y sobrevivir replay/rebuild; el outbox no puede retener indefinidamente IP/MAC ni resucitar sesiones caducadas.
- Keycloak/EJBCA se adoptan como capacidades, pero topología, HA, certificados, responsables y políticas siguen sin evidencia; no afirmar aptitud productiva.
- El ambiente actual no permite validar Docker ni captura `tshark`; los objetivos de ADR-013 siguen siendo criterios pendientes, no resultados.

## Listo para propuesta

Sí. La propuesta debe codificar K01–K04 como habilitadores nuevos, preservar S05–S18, empezar con decisiones/contratos y entregar primero el vertical sintético de búsqueda. Los objetivos de producción y la preparación de infraestructura permanecen como verificación pendiente.
