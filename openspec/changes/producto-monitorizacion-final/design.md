# Diseño: producto de monitorización pasiva completo

Modo `design-after-spec`. Autoridad funcional: `specs/*/spec.md` de este cambio y aprobaciones de `state.yaml`. El diseño adopta PostgreSQL, Kafka/Connect/Elasticsearch, Keycloak y EJBCA; los objetivos de ADR-013 siguen siendo criterios de aceptación por ejecutar.

## Enfoque técnico y evidencia existente

Se amplía la solución .NET 10/Angular 22 existente, con dominio independiente, persistencia y host Minimal APIs. `SessionProjector.cs` ya bloquea eventos y confirma sesión/marcado conjuntamente; `InboxWriter.cs` serializa admisión por origen, compara JSONB y aplica hoy 500/min. `SessionReader.cs` lee autoridad; el endpoint y contexto de lectura restringen actualmente acceso a Development/Testing. Angular usa signals/computed/rxResource y prueba una cadena HTTP real. Se mantienen estos límites estructurales y se sustituyen explícitamente las restricciones funcionales previas mediante los deltas, sin modificar archivos archivados.

## Architecture Decisions

### Decision: Autoridad PostgreSQL y publicación mediante conectores

**Elección:** transacción sesión + metadatos canónicos + marcado + outbox; Debezium EventRouter publica el outbox a Kafka y el sink Elasticsearch realiza la escritura. **Alternativas:** dual write API→Kafka pierde atomicidad; productor/consumidor casero reproduce recuperación ya provista por Connect. **Consecuencias:** replicación lógica, WAL, offsets, ACL y recuperación pasan a ser responsabilidades operativas. `decisions/adr-001.md` sucede las decisiones condicionadas de ADR-010/014; no cambia las cifras de ADR-013.

### Decision: Revisiones y barreras de supresión persistentes

**Elección:** clave inequívoca, revisión de autoridad como external version y borrado como documento mínimo de barrera. **Alternativas:** versión basada en offset permite que un replay nuevo sobrescriba una supresión; DELETE físico depende de la retención temporal de versiones del motor. **Consecuencias:** barreras ocupan almacenamiento mínimo y se purgan únicamente cuando todos los horizontes autorizados vencen. Detalle y listado revalidan vigencia contra autoridad. `decisions/adr-002.md`.

### Decision: Consulta temporal autorizada y snapshot finito

**Elección:** PIT/search_after, filtro de permisos antes del motor y verificación autoritativa acotada antes de entregar. **Alternativas:** offset crece con página y cambia bajo escritura; filtrar en Angular revela datos ajenos; fallback global PostgreSQL carece de coste acotado. **Consecuencias:** registro de cursores, clave de protección compartida y cancelación de ambas lecturas. `decisions/adr-003.md`.

### Decision: Identidades humanas y máquinas separadas

**Elección:** Keycloak PKCE/OIDC y permisos por pares sede/sonda; EJBCA con certificados cliente registrados y revocación fresca. **Alternativas:** cabeceras cliente no acreditan identidad; CN solo no acredita registro; certificados estáticos no prueban emisión/renovación/revocación. **Consecuencias:** clientes, raíces, secretos, privilegios y ciclo de vida independientes. `decisions/adr-004.md`.

### Decision: Sonda aislada y continuidad operada

**Elección:** proceso .NET Linux propio, extractor tshark, spool SQLite transaccional, correlación acotada; PostgreSQL primaria/réplica con fencing operado y WAL/base backup. **Alternativas:** compartir DLL backend-sonda acopla despliegues; spool en memoria pierde eventos; promoción automática sin coordinador puede producir dos escritores. **Consecuencias:** captura requiere privilegios mínimos Linux; local demuestra fallos, S17 mide aptitud en infraestructura representativa. `decisions/adr-005.md`.

## Data Flow

```mermaid
sequenceDiagram
    participant S as Sonda + spool
    participant A as API mTLS
    participant P as PostgreSQL
    participant W as Worker
    participant C as Debezium/Connect
    participant K as Kafka
    participant E as Elasticsearch sink
    participant U as Angular + OIDC
    S->>A: Lote v1, IDs/contenido durables
    A->>P: Admisión atómica, cuota/origen
    P-->>A: COMMIT
    A-->>S: 200 cuerpo vacío
    W->>P: Bloquear pendiente; sesión/marcado/outbox COMMIT
    P->>C: WAL confirmado, pgoutput
    C->>K: Clave + payload + header revision
    K->>E: INSERT con external version
    U->>A: Consulta + token humano
    A->>E: Ámbitos + filtros + PIT/search_after
    A->>P: Verificar vigencia de identidades obtenidas
    A-->>U: Página + cursor + frescura
```

Caída Kafka/sink conserva autoridad y aceptación; la búsqueda devuelve 503/504 o frescura retrasada. Presión WAL/outbox activa alertas y admisión controlada antes de agotamiento, nunca falso ACK. Permanente inválido queda en cuarentena y no impide claves válidas; transitorio conserva pendiente con `next_attempt_at`. DLQ es almacenamiento restringido recuperable, nunca publicación aplicada.

## Interfaces / Contracts

`contrato-ingestion-v1`, `SyntheticSessionContract` y respuesta de detalle de cinco campos permanecen intactos. Añadir `CapturedSessionContract` y `DeviceObservationContract` con campos/validación exactos de sus specs. `CanonicalSession` normaliza IP/tiempo, mantiene JSON original y procedencia; sintético no recibe contadores inventados. Puertos de aplicación en `Monitoring.Domain`: `ISessionSearch`, `ISessionVisibility`, `IInventoryRepository`, `IAuditWriter`, `IProjectionStatus`; adaptadores se registran explícitamente en Host, sin SDK/ORM/web en dominio. Sonda tiene contratos propios compatibles y fixtures compartidos por archivo, sin referencia a ensamblados backend.

`GET /api/v1/sessions` acepta exclusivamente `from,to,siteId,sensorId,sourceIp,destinationIp,protocol,sourcePort,destinationPort,pageSize,cursor`. UTC Z/milisegundos; `[from,to)` por startedAt; ventana ≤30 días dentro de retención, sin futuro. >24 h exige siteId **y** sensorId **y** una IP; ≤24 h permite todos los ámbitos concedidos sin filtros opcionales. Página 1–100, defecto50. Respuesta `{items,nextCursor?,freshness:{state,measuredAt,lagSeconds?}}`; ítems contienen identidad completa, extremos, protocolo, inicio/fin/procedencia e inferencia/parcialidad cuando corresponda. 400 validación/alteración/filtros diferentes, 401 identidad, 403 permiso/ámbito/sujeto distinto, 410 PIT/cursor expirado, 429 saturación, 503 dependencia, 504 plazo10s. Detalle mantiene ruta histórica con `siteId,sensorId` explícitos en producción multiámbito; pruebas sin selectores usan únicamente el contexto servidor existente.

Cursor protegido con ASP.NET Data Protection incluye versión, subject, hash de permisos/pares actuales ordenados, consulta normalizada, página, PIT, posición total `(startedAt,documentKey)` y caducidad absoluta10min. Registro PostgreSQL de leases impone inicialmente dos snapshots por sujeto/20 búsquedas simultáneas globales configurables, sin prometer rendimiento. Cerrar al agotar, caducar o logout; limpieza periódica y cambios de PIT ID quedan registrados. Cada página lee permisos actuales, filtra retención y comprueba en PostgreSQL hasta100 identidades por bloque. Si hay suprimidos, avanza sobre todos los hits examinados, continúa bajo el mismo plazo; al agotarlo responde504 sin página parcial. No hay límite artificial que declare fin y omita hits vigentes.

Inventario: `GET /api/v1/inventory/devices|candidates|observations`; `POST /devices`, `PATCH /devices/{id}`, `POST /candidates/{id}/confirm|reject|merge`, revisión esperada en cuerpo. Validación, permiso de todos los ámbitos, bloqueo/revisión y auditoría en una transacción; conflicto409. Listados con keyset firmado, página≤100 y10s. Cuarentena se resuelve mediante comando operativo auditado, sin panel ni mutación del original.

## Modelo, migraciones y proyección

Migraciones nuevas secuenciales desde `202610050004_CanonicalOutbox` (sin editar las tres anteriores), seguidas de `202610060005_SearchDocumentIdentity` (identidad compacta de búsqueda) y de `...QuarantineReceipts`, `...InventoryIdentity`, `...OperationLeases` con los siguientes identificadores libres, además de las tablas de frescura/checkpoint de K03. EF/Npgsql usan parámetros, nullable y migración bajo lock; CLI `--migrate` permanece. Mantener las cinco columnas actuales de `session_projection` y agregar `session_metadata` con PK triple/FK a sesión, started/ended timestamptz, IP inet, puertos/protocolo/VLAN/procedencia y revisión. Evita romper inserts explícitos de pruebas existentes. Backfill validado por páginas genera metadatos/outbox una vez para sintéticos ya procesados.

`session_identity` conserva clave triple, documentKey, revisión, estado y tiempos de supresión sin metadatos de tráfico. `session_identity` añade `search_document_id` uuid NOT NULL UNIQUE, asignado por PostgreSQL (`gen_random_uuid()`) la primera vez que existe la identidad y permanente: un trigger rechaza cambiarlo y la barrera de supresión conserva el mismo valor. `projection_outbox`: UUID id, aggregateid/documentKey, aggregatetype=`sessions`, target_topic, revision bigint>0, schema_version 1 o 2, payload jsonb, created_at; unicidad identidad/revisión/target_topic. Es append-only; ack de frescura/checkpoint vive en tabla separada. Clave documental `base64url(UTF8(siteId)).base64url(UTF8(sensorId)).base64url(UTF8(eventId))`, sin padding: segmentos codificados no contienen separador. Identificadores permitidos siguen su longitud128; ninguna concatenación literal de identificadores. Tres identificadores de128 bytes producen una clave de515 bytes, por encima del límite de512 bytes del `_id` de Elasticsearch ([documentación](https://www.elastic.co/docs/reference/elasticsearch/mapping-reference/mapping-id-field)); por eso la clave documental sigue siendo la identidad canónica y la clave Kafka, pero el `_id` del índice es `search_document_id` (36 bytes). No se reducen los identificadores admitidos ni se descartan sesiones. Se descartó un hash SHA-256 de la clave porque exigiría un registro de colisiones; el UUID persistente conserva unicidad por restricción, reutilización determinista por identidad y rechazo de colisión en la autoridad.

Compatibilidad y reprocesamiento: el contrato con `searchDocumentId` es `schemaVersion`2 y se publica en `monitoring.sessions.v2` hacia la familia de índices `sessions-v2-NNNNNN`; la versión de contrato, el topic y el índice avanzan juntos. El historial `schema_version`1 en `monitoring.sessions.v1` es append-only: no se reescribe ni se borra, el sink ya no lo consume y su índice `sessions-v1-000001` queda como generación heredada fuera del alias `sessions-read`. La migración asigna un UUID distinto a cada identidad existente (volátil por fila) y `--migrate` reproyecta cada identidad activa y cada barrera de supresión a `monitoring.sessions.v2` con su revisión actual, una sola vez (la unicidad identidad/revisión/topic hace idempotente la repetición). Las sesiones activas se reconstruyen desde `session_projection` y `ingestion_inbox`; no se inventan metadatos. Revertir el binario exige recuperar el sink v1 y su índice heredado, pues un binario anterior publica en v1.

Payload upsert incluye schemaVersion2, operation, documentKey, searchDocumentId, revisión, identidad y campos normalizados. Delete reemplaza completamente el documento con schemaVersion/operation=`delete`/clave/searchDocumentId/identidad/revisión, sin extremos ni datos de sesión. Header `revision` numérico nace de columna bigint mediante EventRouter `table.fields.additional.placement=revision:header:revision`; `table.expand.json.payload=true`, `route.by.field=target_topic`, `route.topic.replacement=${routedByValue}`, target inicial `monitoring.sessions.v1`, StringConverter para clave y JsonConverter sin schemas para valor. Predicado limita transformación al outbox; heartbeat no pasa por EventRouter. Sink: `write.method=INSERT`, `key.ignore=false`, `schema.ignore=true`, `external.version.header=revision`, `max.in.flight.requests=1`, índice regular precreado, `behavior.on.null.values=FAIL`, mapping strict, DLQ restringida y logs de mensajes desactivados. No UPSERT ni data stream. Validación de schemaVersion2/operation, searchDocumentId (UUID canónico en minúsculas) y revisión/header ocurre antes de indexar mediante SMT puro de contrato en `deploy/connect/transforms/`, sin llamadas externas; tras validar, el mismo SMT sustituye la clave Kafka (clave documental completa) por `searchDocumentId`, que el sink usa como `_id` con `key.ignore=false`, conservando topic, partición, offset y headers; rechazos van a DLQ durable. Errores mapping recuperables usan DLQ antes de avanzar offset, no contador applied; errores no recuperables detienen tarea. Estas opciones existen en el [código16.0.0](https://raw.githubusercontent.com/confluentinc/kafka-connect-elasticsearch/v16.0.0/src/main/java/io/confluent/connect/elasticsearch/ElasticsearchSinkConnectorConfig.java); [EventRouter](https://debezium.io/documentation/reference/stable/transformations/outbox-event-router.html) transporta columnas como headers.

Mapping `sessions-v1-000001`: IP `ip`, fechas `date` con epoch_millis/strict_date_time, puertos integer, revisiones long, ámbito/protocolo/procedencia/clave keyword, sin dynamic ni payload original. Alias de lectura `sessions-read`, índice físico de escritura configurable por generación. Consulta exige operation=upsert. Retención bloquea identidad, incrementa revisión y confirma supresión/outbox junto al borrado de sesión/metadatos. Desvincular FK sesión→inbox permite purga de procesados48h; conservar recibo compacto de identidad/occurredAt exacto/digest para comparar reenvíos y evitar nueva cuota tras purga. El digest debe implementar igualdad JSONB (orden de objetos, última clave repetida y números decimales equivalentes), probado contra PostgreSQL; no hash del JSON bruto ni conversión float. Retención de recibos/barreras supera replay/backups autorizados y se ratifica antes de datos reales.

Rebuild crea índice separado y sink adicional; un barrido por keyset sobre la última fila de outbox de cada identidad captura revisiones vigentes y barreras. (Implementado sin transacción REPEATABLE READ de larga duración, que retendría el vacuum: la corrección la da el anti-join por revisión —solo se copia una identidad cuando la generación carece de ella a esa revisión o a una posterior— y el plazo del barrido.) Inserta outbox de reconstrucción en target_topic de generación, preservando clave/revisión. Un registro de generaciones (`search_generation`; la generación1 es el índice/topic actuales y solo una puede estar activa) y un anti-join por identidad y revisión copian cambios concurrentes del outbox ordinario al de generación, enlazando cada copia con su fila de origen (`source_outbox_id`); no produce directamente a Kafka. Los escritores publican siempre al topic de la generación activa, leído bajo el lock compartido de publicación. Retención de outbox se suspende para el horizonte de esa generación. El snapshot tiene plazo y presión WAL monitorizada. No deducir orden de commits de sequence/max(id). Una fase final toma lock exclusivo de publicación, copiado final y espera acotada del sink, valida conteos/identidades/revisiones por ámbito y cambia alias atómicamente; todos los escritores de sesión/retención toman lock compartido. Si la fase excede30s aborta switch, libera escritores y conserva índice anterior; reintenta en ventana operativa. Versiones externas resuelven solapamiento. Restore reaplica supresiones externas vigentes/corte antes de habilitar lectura; backup no es autoridad de barreras más recientes.

`ProjectionStatus` compara por bloques las revisiones de outbox confirmado con documentos realmente visibles tras refresh y registra comprobación aparte; estado actual requiere barrido completo sin huecos/DLQ, no basta estado RUNNING del conector. Lag mide desde `accepted_at` del ACK hasta visibilidad; ausencia de checkpoint verificable devuelve recovering/lag desconocido. Los contadores de trabajo aplicado distinguen barreras y sesiones; un offset adelantado sobre DLQ nunca acredita aplicación.

## Seguridad, sonda y operación

Keycloak: realm monitoring, cliente público monitoring-web PKCE S256, audience monitoring-api, roles de spec y claim validado `monitoring_scopes` de pares. API JwtBearer valida issuer/aud/firma/vigencia y consulta asignaciones actuales mediante adaptador Keycloak con credencial de solo lectura acotada por realm; cache vigente breve, fallo de comprobación cierra acceso. Así una reducción de permisos no espera a que caduque el token/cursor. Angular usa cliente OIDC fijado, tokens solo memoria, logout/401 limpia recursos y cursores; validación state/nonce/redirect URI exactos.

Kestrel listener de ingestión privado con certificado cliente; listener humano separado. EJBCA emite clientAuth y SAN de sonda, registro vincula issuer+serial/fingerprint a par activo. Verificar cadena, SAN registrado, vigencia, EKU y CRL firmada actualizada≤5min/nextUpdate; ausencia fresca rechaza ingestión y alerta. No confiar en cabeceras fuera de un proxy explícitamente confiable. Bootstrap EJBCA administra perfiles/registro mediante CLI local protegida y enrollment REST disponible en Community; no depender de EST/gestión Enterprise. TLS1.2+ con hostname en todos los enlaces; secretos fuera de git y servicios de datos sin puertos públicos.

Sonda Linux: tshark stdout de campos necesarios (`frame.time_epoch`, longitud, eth/IP/IPv6, tcp/udp, VLAN y flags), sin `-w`; fixture PCAP autorizado solo en pruebas. Correlador cinco-tupla bidireccional+ámbito/VLAN conserva dirección primera, TCP5min/UDP1min/duración1h. Persistir checkpoint de flujos/IDs; reinicio cierra parcial con reason restart. SQLite WAL/transactions conserva bytes exactos y batch IDs, ACK elimina únicamente tras200 vacío; un emisor inicial, batch≤500/1MiB, backoff/jitter≤60s, 400/409 aisla,401/403 suspende. Cuota física incluye WAL/journal y estadísticas, descarte más antiguo y contador durable en misma transacción. Fórmula4h+ráfaga y25% margen según spec; tamaño medido antes de fijar bytes. Límite de flujos/memoria configura descarte visible. Sin Npcap en Windows; captura de producción Linux SPAN/TAP, capabilities NET_RAW/NET_ADMIN únicamente extractor.

S14 integra OpenTelemetry y alertas a receptor HTTPS real de laboratorio con almacenamiento/verificación de recepción; exportador compatible privado. S15 usa plazos exactos de specs y limpieza por lotes/checkpoint. S16 ofrece perfil Compose primaria/réplica, WAL/base backup a volumen separado y restore aislado; script de failover primero fencea primaria y valida único escritor, después promueve y repone slot/CDC. Fuera del laboratorio copias y supresiones se replican a dominio de fallo independiente. Alarmas WAL/disco/spool80%, antigüedad pendiente>60s, CRL próxima a5min y ausencia de copia/WAL incluyen destinatario y runbook; umbrales configurables. Readiness separa ingestión durable/búsqueda degradada. No etiquetas IP/MAC/eventId ni payload/secretos.

## File Changes y asignación de escenarios

La tabla asigna **todos los escenarios bajo cada requisito de las15 specs** al componente/archivo y prueba indicados; los sufijos son IDs REQ del dominio de la fila. No existen escenarios delegados a documentación como sustituto de software.

| Dominio y requisitos | Archivos implementadores (crear salvo los existentes señalados) | Evidencia asignada a todos sus escenarios |
|---|---|---|
| base-ejecutable002–003 | modificar `MonitoringDbContext.cs`, `Program.cs`, migraciones nuevas; `Domain/Ports/` | `MigrationTests`, `MigrationFailureTests`, `DomainDependencyTests`: vacío/repetición/fallo/backfill/límites |
| sesiones-canonicas001–003 | `Domain/Sessions/{CapturedSessionContract,CanonicalSession,SessionIdentity}.cs` | `CanonicalSessionTests`: válidos/invalidación/IPv6/intervalo/identidades/fixture histórico |
| bandeja-ingestion-durable003–005 | modificar `Persistence/Ingestion/InboxWriter.cs`; `QuarantineStore.cs`, `EventReceipt.cs`; modificar worker | `IngestionPersistenceTests`, `QuarantineTests`: cuotas/rollback/reenvío/error permanente/transitorio/resolución |
| proyeccion-sesiones-idempotente001–003 | modificar `Persistence/Sessions/SessionProjector.cs`; `OutboxStore.cs` | `SessionProjectionTests`, `OutboxTests`: inicial/replay/concurrencia/ámbitos/fallo/marcado/reinicio/no starvation |
| pipeline-busqueda001–004 | `deploy/connect/{source,sink}.json`, `deploy/elasticsearch/sessions-v1.json`; `Persistence/Search/{ProjectionStatus,RebuildCoordinator}.cs` | `SearchPipelineTests`: commit/rollback/duplicado/revisión/DLQ/caída/frescura/delete/replay/rebuild |
| consulta-sesiones001–004 | `Domain/Search/SessionQuery.cs`, `Host/Search/{SessionSearchEndpoint,ProtectedCursor}.cs`, `Persistence/Search/ElasticsearchSessionSearch.cs` | `SessionSearchTests`: selectividad/límites403/empates/cursor/retención/PIT410/vacío/503/504 |
| detalle-sesion-ambito001–003 | modificar `SessionEndpoint.cs`, `SessionReader.cs`, `TrustedSessionReadScope.cs` | `SessionHostTests`, `TrustedSessionReadScopeTests`: encontrados/ausencia/ámbitos/401/modos/configuración |
| vista-detalle-sesion001,003–006 | modificar Angular `session-detail/`, `app.routes.ts`, `app.config.ts`; crear `session-search/` | Vitest HTTP/componentes y `tests/e2e/session-search.spec.ts`: estados/filtros/páginas/accesibilidad/vertical/CI/artefacto |
| captura-y-entrega-sonda001–004 | `src/Monitoring.Probe/{Capture,Correlation,Spool,Delivery}/`, Dockerfile | `tests/Monitoring.Probe.Tests/`: tshark real/ruido/ámbitos/inversos/reloj/reinicio/ACK perdido/lleno/disco |
| inventario-dispositivos001–004 | `Domain/Inventory/`, `Persistence/Inventory/`, `Host/Inventory/`, Angular `inventory/` | `InventoryTests`: observación/ausenteMAC/IP/VLAN/rechazo/fusión409/auditoría rollback |
| identidad-y-acceso001–004 | `Host/Security/{OidcIdentity,ScopeAuthorization,KeycloakPermissions}.cs`, Angular `auth/`, `deploy/keycloak/realm.json` | `IdentityTests` con Keycloak real: inválidos/matriz/cruce funciones/logout/denegación segura |
| transporte-y-pki001–004 | `Host/Security/{SensorCertificateIdentity,RevocationStatus}.cs`, `deploy/pki/`, `scripts/lab/pki.sh` | `PkiTests` con EJBCA: TLS/name/CA/EKU/revocado/sinregistro/emisión/renovación/backlog/ACL |
| operacion-y-ciclo-datos001–004 | `Host/Operations/`, `Persistence/Operations/`, `deploy/postgres/`, `scripts/lab/{failover,restore,rebuild}.sh` | `OperationTests`: alerta receptada/reconciliación/caducidad/pendiente/fencing/failover/PITR |
| aptitud-y-liberacion001–004 | `compose.yaml`, `compose.continuity.yaml`, `deploy/versions.env`, Dockerfiles; `scripts/lab/`, `tests/Monitoring.Acceptance/`, `scripts/release/` | vertical limpio/dependencia fallida/S17 completo/gate S18 verificable; fallos son fallo global |
| verificacion-base001–003 | modificar `.github/workflows/ci.yml`, README, `openspec/config.yaml`; crear workflow S17; docs abajo | .NET/Angular/vertical limpio, fallos CI y reproducción de comandos |

Los prefijos de backend son `src/Monitoring.{Domain,Persistence,Host}/`; los tests .NET de backend son `tests/Monitoring.Tests/`. Añadir Probe/proyecto tests a `Monitoring.slnx`; UI sigue artefacto separado con reverse proxy TLS privado. La prueba CI existente que excluye SPA del publish-host sigue válida: publicar Angular en imagen propia.

Actualizar documentos vivos `docs/architecture/{technical-baseline,flujo-completo}.md`, `docs/product/{brief,functional-scope}.md`, `docs/{roadmap,roadmap-gaps}.md`, `docs/development/slices.md`, `docs/architecture/decisions/README.md` y crear sucesores ADR-015–019 referenciando decisiones change-local; conservar históricos. Documentar arquitectura adoptada y evidencia pendiente específica, sin llamarla candidata. Crear `docs/operations/{stack,search-rebuild,quarantine,pki,retention,failover,pitr,alerts,release}.md` y `docs/evidence/` con manifiestos ejecutado/fallido/no-ejecutado. Config actual aún declara ausencia de código y PostgreSQL provisional: corregirlo en K01 junto a capacidades reales, no inventar pruebas ejecutadas.

## Stack fijado y arranque entregable

`deploy/versions.env` fija .NET10.0.303, paquetes existentes EF10.0.12/Npgsql10.0.3, Node24.16/npm12.0.2/Angular22.2.0; `postgres:18.6-bookworm`, `apache/kafka:4.3.1`, Debezium PostgreSQL3.6.3.Final, plugin sink16.0.0, `docker.elastic.co/elasticsearch/elasticsearch:9.5.4`, `quay.io/keycloak/keycloak:26.7.5`, `keyfactor/ejbca-ce:9.6.3`. El apply fija digest de **cada** imagen descargada, hashes de plugins y lockfiles; no `latest` ni instalaciones dinámicas en arranque. Connect se construye con distribución Apache Kafka4.3.1/Java21, plugins en directorios separados; no presupone licencia de cp-server-connect. Sink tiene Confluent Community License, distribución Elasticsearch ELv2 y EJBCA LGPL: declarar en paquete/SBOM. [Kafka](https://kafka.apache.org/downloads/), [Keycloak26.7.5](https://www.keycloak.org/2026/09/keycloak-2675-released), [sink16](https://docs.confluent.io/kafka-connectors/elasticsearch/current/index.html) y [Debezium3.6](https://debezium.io/releases/3.6/) sustentan la selección; instalación conjunta todavía debe probarse. PostgreSQL18 está soportado por Debezium desde3.4; no hay matriz oficial EJBCA9.6.3/PG18 encontrada, por lo que persistencia/arranque/renovación EJBCA contra PG18 es prueba obligatoria y cualquier incompatibilidad se informa antes de declarar K04 terminado.

Servicios Compose: postgres, kafka, connect, elasticsearch, keycloak, ejbca, bootstrap, migrate, api, web, probe-fixture, alert-receiver. Bases/roles separadas para monitoring/Keycloak/EJBCA; PostgreSQL18 monta `/var/lib/postgresql`, activa wal_level logical, slots acotados y TLS. Migrador crea publicación restringida al outbox; usuario CDC tiene REPLICATION/LOGIN/CONNECT/USAGE/SELECT únicamente sobre tablas requeridas, nunca superuser. `pgoutput`, slot estable/failover cuando configurado y publicación explícita sin autocreación all-tables. ACL Kafka separa source producer, sink consumer, administración y DLQ; sink ES solo índices autorizados, API solo búsqueda. Bootstrap de EJBCA usa `TLS_SETUP_ENABLED=true`, crea ManagementCA y protege administrador antes de exponer cualquier puerto; genera CA/certificados de laboratorio y perfiles con EJBCA real. Solo web/IdP HTTPS y endpoint mTLS se publican en127.0.0.1; datos/admin solo red interna. `lab-secrets/` ignorado y fuera de artefactos; claves servidor/sonda creadas localmente, CSR se firma en EJBCA. Resolución de nombres SAN/issuer debe coincidir dentro y fuera de Docker, sin desactivar hostname/JWT issuer. Healthchecks de todos los servicios y bootstrap/migración ordenados con plazos finitos y errores sanitizados. Versiones/imágenes contrastadas con [tags EJBCA](https://hub.docker.com/r/keyfactor/ejbca-ce/tags) y [releases Elasticsearch](https://www.elastic.co/downloads/past-releases#elasticsearch); confirmar pull/digests es parte de K04, no evidencia de esta fase.

Contratos de comandos **a implementar** en K04 (scripts idempotentes, no acreditados por este diseño):

```powershell
# Runtime local Windows ya disponible; cambios solo en este proceso.
$env:DOCKER_HOST='tcp://127.0.0.1:2375'
$env:TESTCONTAINERS_HOST_OVERRIDE='127.0.0.1'
Remove-Item Env:DOCKER_CONTEXT -ErrorAction SilentlyContinue
dotnet restore Monitoring.slnx
dotnet build Monitoring.slnx --no-restore
dotnet test Monitoring.slnx --no-build
npm --prefix src/monitoring-web ci
npm --prefix src/monitoring-web test
npm --prefix src/monitoring-web run build
docker compose run --rm bootstrap
docker compose up --build --wait --wait-timeout 600
docker compose run --rm probe-fixture emit
docker compose run --rm verifier vertical
docker compose run --rm verifier faults
docker compose down
# Reset separado y explícitamente destructivo:
docker compose down --volumes
```

En Linux se omiten variables WSL y se usa socket de Docker local; las imágenes/servicios/guías de producción conservan topología Linux. `verifier vertical` autentica humano en Keycloak, emite fixture con certificado EJBCA, acredita ACK/PG/outbox/topic/ES/API/Angular mediante navegador real Playwright; plazo5min. Scripts operativos se ejecutan por contenedores/toolbox, sin exigir tshark host. Dockerfile probe fija paquete tshark/imagen Linux y registra versión; parser prueba exactamente esa salida.

## Testing Strategy y rollout finito

Strict TDD: cada tarea funcional identifica REQ/escenario, ejecuta prueba nueva RED antes de código, luego GREEN y registra comandos, salida/fecha/dataset/reloj/semilla en apply-progress. Ampliar pruebas existentes bajo deltas: la prueba antigua de cuota500 recibe cuota500 explícita; laboratorio producto usa6000. Desconocidos pasan a cuarentena, por lo que cambian solo expectativas supersedidas y mantienen no-proyección/no-starvation. Falta Docker/tshark/conector/CA nunca se convierte en skip aprobatorio. No se ejecutaron builds/tests ni servicios durante diseño.

| Condición real | Respuesta verificable | Prueba/evidencia |
|---|---|---|
| Fallo antes commit y ACK perdido | cero efectos parciales; retry una identidad/publicación | triggers PostgreSQL, caída/reinicio, lectura por conexión independiente |
| Replay antiguo con offset nuevo después delete | revisión vigente/barrera, nunca exposición incluso PIT abierto | Connect16/ES real, re-publicación conservando header, comparación autoridad |
| Cambio permisos durante cursor |403 y datos/cursor limpiados | Keycloak real, modificar asignación y repetir página |
| Error individual y sink interrumpido | DLQ durable, válidos recuperados, lag visible | detener contenedor, insertar incompatible, límites espera y reconciliación |
| TLS/CRL expirados o sin CA/EKU | rechazo cerrado y alerta real | EJBCA emisión/revocación, reloj/caché controlados sin bypass |
| Retención/restore/failover | sin expirados/dos primarias; ACKs reconciliados | instancia aislada, manifiesto recuperación, receptor |
| 30d/72h/10M por día/ráfagas/20consultas | objetivos ADR013 cumplidos o resultado fallido explícito | job S17 finito con hardware, métricas/coste completos; smoke no sustituye |

Secuencia de implementación dentro del mismo change: **K01/K04 → K02 → K03 → S09** entrega primero demostración buscable desde sesiones ingresadas, con pruebas por bloque; contexto Development/Testing solo hasta integrar identidad. Después S05–S08 incorpora captura real, S10–S13 inventario/Keycloak/EJBCA, S14–S16 operación/continuidad, S17 ensayo completo, S18 paquete/decisión. K04 puede arrancar servicios/identidad/PKI para la demo; S12/S13 completa la matriz y ciclo productivo. Cada bloque termina con recorrido ejecutado y evidencia; el paquete final requiere todas las puertas. Rollback detiene productores/conectores, mantiene PG/outbox/offsets/recibos/barreras y vuelve a binario compatible; migraciones destructivas Down prohibidas. Alias vuelve solo a índice reconciliado. S17/S18 sin evidencias/políticas/guardia devuelve bloqueo de liberación, con software y resultados revisables conservados.

## Open Questions

Ninguna decisión de diseño pendiente. Datos representativos, políticas finales, guardia y resultados completos S17/S18 son las puertas de liberación explícitas de las specs, no decisiones técnicas delegadas al implementador.
