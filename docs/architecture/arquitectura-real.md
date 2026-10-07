# Arquitectura real (observada)

Diagrama de lo que **existe y corre de verdad**, levantado a partir del sistema en ejecución y del código. No se ha consultado ninguna documentación del repositorio (ni ADR, ni guías, ni `stack.md`): si algo aquí contradice un documento, manda esto, porque se midió. Medición del 2026-10-07 sobre el proyecto Compose `monitoring-local`.

**Cómo se obtuvo** (reproducible):

| Dato | Fuente |
|---|---|
| Contenedores, imágenes, puertos, estado | `docker ps`, `docker network inspect monitoring-local_default` |
| Dependencias, perfiles, variables, secretos | `docker compose … config --format json` (todos los solapamientos y perfiles activos) |
| Tablas, publicación y slot de replicación, roles y conexiones | `psql` contra `monitoring`: `information_schema`, `pg_publication`, `pg_replication_slots`, `pg_stat_activity` |
| Conectores | `GET :8083/connectors/<n>/config` |
| Tópicos y grupos de consumo | `kafka-topics.sh --describe`, `kafka-consumer-groups.sh --list` |
| Índices y alias | `GET :9200/_cat/indices`, `/_alias` |
| Métricas y objetivos | API de Prometheus (`/targets`, `/label/job/values`, `/label/__name__/values`) |
| Procesos internos, endpoints, proyectos | código: `*.csproj`, `Program.cs`, `AddHostedService`, `Map*` |
| Keycloak: realm y cliente | `deploy/keycloak/realm-monitoring.json` y el descubrimiento en vivo |

---

## 1. Vista de contenedores

Nueve contenedores sanos en una única red (`monitoring-local_default`, 172.21.0.0/16). Solo cinco publican un puerto al equipo, todos ligados a `127.0.0.1`. La consola **no es un contenedor**: es `ng serve` en el equipo anfitrión.

```mermaid
flowchart LR
  subgraph HOST["Equipo anfitrión (127.0.0.1)"]
    BR["Navegador"]
    NG["ng serve · :4200<br/>(configuración lab)<br/>proxy /api → :5080"]
  end

  subgraph NET["Red Compose · monitoring-local_default"]
    API["api · ASP.NET Core 10<br/>:8080 (publicado :5080)<br/>Development · Oidc"]
    KC["keycloak 26.7.5 · start-dev<br/>:8080 HTTP (publicado :8081)<br/>:8443 HTTPS · :9000 gestión"]
    PG[("postgres 18.6<br/>:5432 · wal_level=logical")]
    CN["connect · Kafka Connect<br/>:8083 (publicado) · JMX :9404<br/>Debezium + sink ES + transform"]
    KF["kafka 4.3.1 (KRaft)<br/>:9092 PLAINTEXT"]
    ES[("elasticsearch 9.5.4<br/>:9200 (publicado)<br/>security desactivada")]
    PR["prometheus 3.15<br/>:9090 (publicado)<br/>receptor OTLP + reglas"]
    KX["kafka-exporter :9308"]
    EX["elasticsearch-exporter :9114"]
  end

  BR -->|"HTTP"| NG
  BR -->|"login OIDC · HTTP loopback"| KC
  NG -->|"/api · HTTP"| API
  API -->|"SQL · rol monitoring_app"| PG
  API -->|"HTTP · consulta"| ES
  API -->|"HTTPS :8443 · CA de laboratorio<br/>descubrimiento + claves"| KC
  API -->|"OTLP push"| PR
  PG -->|"slot pgoutput · rol monitoring_cdc"| CN
  CN -->|"produce / consume"| KF
  CN -->|"bulk INSERT"| ES
  PR -->|"scrape :9404"| CN
  PR -->|"scrape"| KX
  PR -->|"scrape"| EX
  KX -.->|"lee"| KF
  EX -.->|"lee"| ES
```

### Inventario

| Contenedor | Imagen | Expuesto al equipo | Arranca tras | Notas |
|---|---|---|---|---|
| `api` | `monitoring/api` (aspnet 10.0.12) | `127.0.0.1:5080→8080` | `bootstrap`, `elasticsearch`, `keycloak` | Secretos: `postgres_app`, `lab_identity_ca` |
| `migrate` · `bootstrap` | misma imagen / `postgres` | — | `postgres` / `migrate` | Se ejecutan una vez y terminan con código 0 |
| `postgres` | `postgres:18.6` | — | — | Autoridad de datos. Tres roles (ver §3) |
| `kafka` | `apache/kafka:4.3.1` | — | — | Un nodo, replicación 1, retención 168 h |
| `connect` | `monitoring/connect` (Kafka + Debezium + sink + transform propio) | `127.0.0.1:8083` | `kafka` | Agente JMX de Prometheus en `:9404` |
| `elasticsearch` | `9.5.4` | `127.0.0.1:9200` | — | Un nodo, `xpack.security.enabled=false` |
| `keycloak` | `26.7.5` | `127.0.0.1:8081→8080` | — | HTTP y HTTPS activos a la vez |
| `prometheus` | `v3.15.0` | `127.0.0.1:9090` | — | Perfil `observability-lab` |
| `kafka-exporter` · `elasticsearch-exporter` | `v1.10.0` · `v1.11.0` | — | `kafka` · `elasticsearch` | Perfil `observability` |

---

## 2. Camino de un dato: de la bandeja al navegador

Es el flujo que se ejerce con los datos de demo. La entrada de datos es la **bandeja de ingestión en PostgreSQL**; esa bandeja la escribe `InboxWriter` cuando llega un lote por `POST /api/v1/ingestion/batches` (exige identidad de sonda: responde 401 sin credencial). En esta máquina **no hay ninguna sonda corriendo**, así que los datos entran por el sembrado, que escribe en la misma tabla.

```mermaid
sequenceDiagram
  autonumber
  participant P as Sonda / sembrado
  participant API as API (host .NET)
  participant PG as PostgreSQL
  participant CDC as Debezium (en connect)
  participant K as Kafka
  participant SK as Sink ES (en connect)
  participant ES as Elasticsearch
  participant UI as Consola

  P->>API: lote de eventos (POST /ingestion/batches)
  API->>PG: INSERT ingestion_inbox (+ ingestion_origin)
  Note over API,PG: SessionProjectionWorker lee la bandeja
  API->>PG: UPSERT session_projection / session_identity / session_metadata<br/>+ INSERT projection_outbox (misma transacción)
  PG-->>CDC: WAL · slot monitoring_outbox · publicación monitoring_outbox
  CDC->>K: tópico monitoring.sessions.v2 (EventRouter por target_topic, cabecera revision)
  K->>SK: grupo connect-monitoring-sink
  SK->>ES: INSERT con versión externa = revision → sessions-v2-000001
  SK-->>K: fallos al tópico monitoring.sessions.dlq
  Note over API,ES: ProjectionReconciliationWorker compara outbox vs índice<br/>y guarda el resultado en search_projection_check
  UI->>API: GET /api/v1/sessions (Bearer)
  API->>ES: búsqueda por alias sessions-read (PIT + cursor)
  API->>PG: re-verifica cada resultado y los permisos · lease en search_snapshot_lease
  API-->>UI: página acotada + frescura del índice
```

Detalles observados:

- **Una sola escritura transaccional** hacia la autoridad y la cola de salida (`projection_outbox`): no hay doble escritura a Kafka.
- La publicación `monitoring_outbox` incluye **solo** la tabla `projection_outbox`; el slot usa `pgoutput` y está activo.
- El sink usa `write.method=INSERT` con versión externa (cabecera `revision`), `errors.tolerance=all` y tópico de cartas muertas; un `RegexRouter` lleva el tópico al índice `sessions-v2-000001`. El alias de lectura `sessions-read` apunta a ese índice.
- Estado medido: 175 documentos en el índice, 1 partición por tópico, grupo `connect-monitoring-sink` presente.

---

## 3. Autoridad de datos (PostgreSQL)

Un solo esquema `monitoring` con 20 tablas, agrupadas por responsabilidad:

```mermaid
flowchart TB
  subgraph ING["Ingestión"]
    ib[ingestion_inbox]
    io[ingestion_origin]
    iq[ingestion_quarantine]
    iqa[ingestion_quarantine_audit]
  end
  subgraph SES["Sesiones"]
    sp[session_projection]
    si[session_identity]
    sm[session_metadata]
    po[projection_outbox]
  end
  subgraph BUS["Búsqueda"]
    sg[search_generation]
    spc[search_projection_check]
    ssl[search_snapshot_lease]
  end
  subgraph INV["Inventario"]
    d[device]
    dc[device_candidate]
    dia[device_ip_association]
    dobs[device_observation]
    ds[device_scope]
    ia[inventory_audit]
  end
  subgraph SEG["Seguridad"]
    ad[access_denial_audit]
    pr[probe_registry]
    pra[probe_registry_audit]
  end
  ib --> sp
  ib --> dobs
  dobs --> dc
  dc --> d
  sp --> po
  po -.->|"WAL"| EXT(("Debezium"))
```

| Rol de base de datos | Uso observado | Privilegios |
|---|---|---|
| `monitoring` | administración y arranque | superusuario y replicación (solo `bootstrap`, `psql`) |
| `monitoring_app` | **el API** (3 conexiones vivas) | sin superusuario ni replicación; permisos sobre las 20 tablas |
| `monitoring_cdc` | **Debezium** (2 conexiones «General» + 1 «Streaming») | replicación y **solo** `SELECT` sobre `projection_outbox` |

---

## 4. Identidad y confianza

```mermaid
sequenceDiagram
  autonumber
  participant B as Navegador
  participant KC as Keycloak
  participant UI as Consola (:4200)
  participant API as API

  B->>UI: abrir /sessions
  UI->>KC: descubrimiento (HTTP, loopback)
  UI-->>B: redirección con PKCE S256
  B->>KC: usuario y contraseña (realm monitoring)
  KC-->>B: código
  B->>UI: /auth/callback
  UI->>KC: cambio de código por token (CORS permitido)
  UI->>API: GET /api/v1/... + Authorization: Bearer
  Note over API,KC: Canal propio del API hacia Keycloak
  API->>KC: HTTPS :8443 — descubrimiento + JWKS<br/>confía solo en la CA de laboratorio
  API-->>UI: 200 / 401 / 403 según firma, emisor, audiencia, roles y ámbitos
```

- **Dos caminos distintos hacia Keycloak.** El navegador usa el HTTP de loopback (`127.0.0.1:8081`); el API usa HTTPS interno (`keycloak:8443`) validando la cadena contra la CA del laboratorio. El emisor que ve el token es el de loopback; los endpoints de canal trasero siguen el esquema y el host de quien pregunta.
- **Cliente `monitoring-web`**: público, PKCE S256, sin concesión directa. Tres mapeadores: `roles`, `monitoring_scopes` y la audiencia `monitoring-api`.
- **Roles** (`analista`, `auditor`, `administrador-inventario`) y cuatro usuarios sintéticos; el ámbito (`monitoring_scopes`) lo lleva cada token y lo aplica el API.
- **Claves**: el API las relee periódicamente y ante un identificador de clave desconocido, con espera acotada; una clave retirada deja de valer en minutos.
- **mTLS de sondas** está implementado en el código (`ProbeCertificateMiddleware`, `ProbeCertificateValidator`, `RevocationRefreshWorker`, tabla `probe_registry`) pero **no está activo en este laboratorio**: el API escucha solo HTTP en `:8080` y el endpoint de ingestión responde 401 sin identidad.

---

## 5. Por dentro del API (código)

Cuatro proyectos de producción. `Monitoring.Probe` es independiente: no referencia a los demás ni al revés.

```mermaid
flowchart LR
  subgraph PROD["Producción"]
    HOST["Monitoring.Host<br/>(ejecutable ASP.NET Core)"]
    PERS["Monitoring.Persistence<br/>(EF Core · Npgsql · cliente ES)"]
    DOM["Monitoring.Domain<br/>(reglas puras)"]
    PROBE["Monitoring.Probe<br/>(proceso aparte)"]
  end
  subgraph TEST["Pruebas"]
    T1["Monitoring.Tests"]
    T2["Monitoring.Probe.Tests"]
  end
  HOST --> PERS
  HOST --> DOM
  PERS --> DOM
  T1 --> HOST
  T1 --> DOM
  T2 --> PROBE
```

**Hilos de fondo en el host** (`AddHostedService`):

| Servicio | Qué hace |
|---|---|
| `SessionProjectionWorker` | Convierte la bandeja en sesiones, identidad, metadatos y salida CDC |
| `ProjectionReconciliationWorker` | Compara la autoridad con el índice y registra faltantes/obsoletos |
| `RetentionWorker` | Retención de datos |
| `PipelineMetrics` · `QuarantineMetrics` | Publican contadores y medidores |
| `RevocationRefreshWorker` | Refresca la revocación de certificados de sonda (solo con mTLS configurado) |

**Modos de línea de comandos del mismo ejecutable:** `--migrate`, `--project-pending`, `--rebuild-search`, `--quarantine …`, `--probes …`, `--restore-finalize`.

**Superficie HTTP real:**

| Ruta | Método | Acceso |
|---|---|---|
| `/health/live` | GET | abierto |
| `/api/v1/sessions` · `/api/v1/sessions/{eventId}` | GET | token + rol + ámbito |
| `/api/v1/inventory/devices` · `/candidates` · `/observations` | GET | token + rol (los candidatos, no el auditor) |
| `/api/v1/inventory/devices` | POST · PATCH `/{id}` | administrador de inventario |
| `/api/v1/inventory/candidates/{id}/confirm` · `/reject` · `/merge` | POST | administrador de inventario |
| `/api/v1/ingestion/batches` | POST | identidad de sonda (401 sin ella) |

**Consola Angular:** rutas `/sessions`, `/sessions/:eventId`, `/inventory`, `/auth/callback`, `/signed-out`; todas las de trabajo con guardia de autenticación. Habla **solo** con `/api/...` (proxy del servidor de desarrollo); nunca con Elasticsearch, Kafka ni PostgreSQL.

**Sonda (código):** `TsharkCapture` (tshark/dumpcap) → `TsharkParser` → `FlowCorrelator` → `SqliteSpool` (cola local en disco) → `ProbeDelivery` (HTTP hacia la ingestión, con certificado de cliente PEM opcional), orquestado por `ProbeEngine`, más `ProbeTelemetry`. Se ejecuta con `Monitoring.Probe <configuración.json> [--status]`.

---

## 6. Observabilidad

```mermaid
flowchart LR
  API["api"] -->|"OTLP push<br/>9 medidores monitoring_*"| PR["prometheus"]
  CN["connect :9404<br/>agente JMX"] -->|"scrape"| PR
  EX["elasticsearch-exporter :9114"] -->|"scrape"| PR
  KX["kafka-exporter :9308"] -->|"scrape"| PR
  PR --> R["28 reglas de alerta<br/>(alerts.rules.yml)"]
```

- Trabajos activos en Prometheus: `monitoring-api` (empujado por OTLP), `connect`, `elasticsearch`, `kafka`; los tres últimos en `up`.
- Medidores del API que llegan de verdad: `monitoring_ingestion_{accepted,pending,processed,quarantined}_events`, `monitoring_pipeline_wal_slot_{active,confirm_lag_bytes,retained_bytes}`, `monitoring_search_freshness_{lag_seconds,state}`.
- **No hay** Alertmanager ni Grafana: las reglas se evalúan, pero nada notifica ni dibuja paneles.

---

## 7. Lo que hay y lo que no

| Componente | Estado real hoy |
|---|---|
| Ingestión durable → PostgreSQL → Debezium → Kafka → Elasticsearch | **Presente y funcionando** (175 documentos indexados, conector fuente y sink en marcha) |
| API con validación OIDC, matriz de roles y ámbitos | **Presente** (Keycloak real) |
| TLS API → Keycloak con CA propia | **Presente**; el navegador usa HTTP en loopback |
| Consola Angular | **Presente** (servidor de desarrollo, no empaquetada en el host) |
| Prometheus con reglas | **Presente**, solo de laboratorio |
| Sonda de captura real | **Código y pruebas sí; ningún proceso corriendo** |
| mTLS de sondas y registro de sondas | **Código y tablas sí; sin activar** en el laboratorio |
| PKI real (EJBCA o equivalente) | **Ausente** |
| Alertmanager · Grafana · notificaciones | **Ausentes** |
| Cifrado en Kafka y autenticación en Elasticsearch/Connect | **Ausentes** (red interna de laboratorio; los puertos publicados son solo `127.0.0.1`) |
| Alta disponibilidad (varios nodos) | **Ausente**: un nodo de PostgreSQL, Kafka y Elasticsearch |
| Keycloak en modo producción | **No** (`start-dev`, base embebida) |
