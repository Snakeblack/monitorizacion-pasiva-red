# Guía de demo

Cómo levantar el laboratorio, entrar con cada rol y enseñar todo lo que hoy se puede mostrar. Pensada para leerse de arriba abajo la primera vez y para saltar a un apartado después.

> **Qué es y qué no es.** Es el producto real corriendo en un laboratorio local: ingestión durable, proyección idempotente, PostgreSQL como autoridad, Debezium → Kafka → Elasticsearch, API con identidad real (Keycloak), consola Angular y métricas con alertas. Los **datos son sintéticos** (no hay sonda capturando tráfico real) y varios componentes siguen en modo laboratorio. La lista honesta está en [Límites](#límites-que-conviene-decir-en-voz-alta).

## 1. Qué vas a ver

```
 Sonda (sintética) ─► bandeja durable ─► proyector ─► PostgreSQL (autoridad)
                                                         │ outbox
                                                 Debezium ─► Kafka ─► Connect (sink) ─► Elasticsearch
                                                                                              │
 Navegador ─► Consola Angular ─► API ASP.NET Core ◄── valida tokens (Keycloak) ──────────────┘
                                   │
                                   └─ métricas OTLP ─► Prometheus (reglas de alerta)
```

## 2. Arranque

Requisitos y su verificación están en la [guía de instalación para un LLM o un equipo nuevo](../development/guia-instalacion-para-llm.md). Con todo instalado, desde la raíz del repositorio (PowerShell 7; en Linux/macOS usa `pwsh` igual):

```powershell
$env:MONITORING_COMPOSE_PROJECT = 'monitoring-local'

# 1. Núcleo (PostgreSQL, Kafka, Connect, Elasticsearch, API): genera secretos, publica la API y construye las imágenes. Tarda varios minutos.
pwsh scripts/lab/core.ps1 -Action up -Project monitoring-local

# 2. Índice, alias y conectores Debezium/Elasticsearch
node scripts/lab/pipeline.mjs

# 3. Keycloak real + API en modo OIDC + Prometheus + puertos de demo + exportadores
docker compose --env-file deploy/versions.env -p monitoring-local `
  -f compose.yaml -f deploy/compose.e2e.yaml -f deploy/compose.identity.yaml -f deploy/compose.demo.yaml `
  --profile observability --profile observability-lab up -d --wait --wait-timeout 300

# 4. Datos de demo: 80 sesiones en dos sitios y 10 candidatos de inventario (repetible, no duplica)
node scripts/lab/seed-demo.mjs

# 5. Consola web (déjala en una terminal aparte)
npm --prefix src/monitoring-web ci          # solo la primera vez
npm --prefix src/monitoring-web run start:lab
```

Al terminar, `node scripts/lab/demo-credentials.mjs` escribe y muestra los usuarios y la contraseña (ver [§4](#4-usuarios-y-roles)).

**Apagar:** `pwsh scripts/lab/core.ps1 -Action down -Project monitoring-local` (conserva los datos). Para empezar de cero, usa otro nombre de proyecto `monitoring-*` o `docker compose -p monitoring-local down --volumes`.

## 3. Enlaces

| Qué | Dirección | Notas |
|---|---|---|
| **Consola web** | http://localhost:4200 | Redirige a Keycloak para iniciar sesión. También vale `http://127.0.0.1:4200`. |
| Keycloak (inicio de sesión) | http://127.0.0.1:8081/realms/monitoring/account | Se abre solo desde la consola. |
| Keycloak (administración) | http://127.0.0.1:8081/admin | Usuario `lab-admin`, misma contraseña que los demás. Realm `monitoring`. |
| API: liveness | http://127.0.0.1:5080/health/live | Responde 200 sin consultar la base. El resto exige token. |
| **Prometheus** | http://127.0.0.1:9090 | Alertas: `/alerts` · Reglas: `/rules` · Objetivos: `/targets` · Consultas: `/graph` |
| Elasticsearch | http://127.0.0.1:9200 | `/_cluster/health`, `/_cat/indices?v`, `/sessions-read/_search` |
| Kafka Connect | http://127.0.0.1:8083/connectors | Dos conectores: `monitoring-source` y `monitoring-sink`. |

Todos están ligados a `127.0.0.1`: solo se ven desde este equipo. Elasticsearch y Connect del laboratorio **no tienen autenticación**; no los publiques en otra interfaz.

## 4. Usuarios y roles

La contraseña es aleatoria por equipo y vive en `lab-secrets/keycloak-lab.txt` (ignorado por git). Para verla junto a los usuarios:

```powershell
node scripts/lab/demo-credentials.mjs     # escribe y muestra lab-secrets/credenciales-demo.txt
```

En el formulario de Keycloak escribe el **usuario** (no un correo). La contraseña es la misma para los cuatro.

| Usuario | Rol | Ámbito | Qué demuestra |
|---|---|---|---|
| `admin-inventario` | Administrador de inventario | Sitio A y sitio B | Ve ambos sitios; confirma, rechaza, fusiona y edita en inventario |
| `analista` | Analista | Solo sitio A (`pipeline-site`) | Aislamiento: no ve nada del sitio B |
| `auditor` | Auditor | Solo sitio B (`lab-site-b`) | Lectura; el inventario es de solo lectura y los candidatos le están vedados |
| `sin-ambito` | Analista | Ninguno | Rol válido sin ámbito: el API responde **403** y no devuelve datos |

La autorización la decide siempre el API a partir del token; la consola solo oculta lo que no puedes usar.

## 5. Guion sugerido (≈ 15 min)

### 5.1 Entrar y orientarse (analista)
1. Abre http://localhost:4200 → te lleva a Keycloak → entra como **`analista`**.
2. Abajo a la izquierda ves tu usuario y rol. Arriba a la derecha, la **frescura del índice** (verde = al día).
3. La lista muestra las últimas 24 h del **sitio A** solamente. Cada fila: cuándo empezó (relativo y UTC exacto), origen → destino con puertos, protocolo, duración, sede/sonda y procedencia (`Sintética` / `Capturada`).

### 5.2 Filtros (lo que más se ha cuidado)
- **Rango de tiempo**: 15 min · 1 h · 6 h · 24 h · 7 d · 30 d. Un clic y busca. El rango siempre termina «ahora», aunque tardes en pulsar Buscar.
- **`+ Filtro`** (o pulsa **`/`** desde cualquier sitio): sede, sonda, IP origen/destino, puertos y protocolo. Cada filtro es una **etiqueta editable**; se quita con la ×. Todos son **exactos** y se combinan; no hay búsqueda libre por diseño.
- **IP mal escrita**: la etiqueta se pone en rojo y el mensaje dice qué falta, sin llegar al servidor.
- **Personalizado**: escribe fechas como `2026-10-05 12:00` (UTC); se normalizan solas.
- **Más de 24 h**: la regla exige sede, sonda y una IP. Elige `7 d` y aparece un aviso con botones **+ Sede / + Sonda / + IP origen** para añadirlos de un clic.
- **Filas** (25/50/100) y **Página siguiente** usan un cursor sellado ligado a tu usuario y a tus ámbitos.
- Prueba: filtra por **Protocolo = UDP** y pulsa **Buscar sesiones** (o Enter).

### 5.3 Detalle
Pulsa cualquier fila (o el identificador). El detalle muestra los cinco campos de la sesión y el contenido (`data`) en JSON; **Copiar identificador** lo lleva al portapapeles. Si alguien intenta abrir una sesión de un sitio fuera de su ámbito (por ejemplo, el analista con el identificador del sitio B), el API responde **403** sin devolver ningún dato de la sesión.

### 5.4 Aislamiento y denegación
1. **Cerrar sesión** (abajo a la izquierda). Keycloak cierra también su sesión.
2. Entra como **`auditor`**: solo sitio B. Abre *Inventario → Candidatos*: el API lo deniega por rol.
3. Entra como **`sin-ambito`**: «Acceso denegado» y ninguna fila.
4. Entra como **`admin-inventario`**: ves los dos sitios mezclados. En el listado cada fila indica su sede.

### 5.5 Inventario (administrador)
En *Inventario → Candidatos* aparecen los equipos observados que aún no están confirmados (MAC, VLAN, IP, cuándo se vieron).
- **Confirmar** → pide nombre (y descripción) → el candidato pasa a *Dispositivos*.
- **Rechazar** → pide un motivo. **Fusionar** → lo une a un dispositivo existente.
- *Dispositivos → Editar* cambia nombre o descripción. Cada cambio usa la **revisión esperada**: si otra persona lo tocó antes, ves «Otro usuario ha cambiado este registro» y la lista se refresca.
- Todas las decisiones quedan auditadas; no se pueden confirmar automáticamente.

### 5.6 La tubería por dentro
```powershell
# La autoridad: sesiones aceptadas y proyectadas
docker exec monitoring-local-postgres-1 psql -U monitoring -d monitoring -c "select site_id, count(*) from monitoring.session_identity where event_id like 'demo-%' group by 1"

# Debezium + sink: ambos RUNNING
curl http://127.0.0.1:8083/connectors/monitoring-source/status
curl http://127.0.0.1:8083/connectors/monitoring-sink/status

# El índice de búsqueda
curl "http://127.0.0.1:9200/_cat/indices?v"
curl -s "http://127.0.0.1:9200/sessions-read/_search?q=eventId:demo-a-001&pretty"

# El tópico de Kafka entre ambos (Git Bash en Windows: antepón MSYS_NO_PATHCONV=1)
docker exec monitoring-local-kafka-1 sh -c "/opt/kafka/bin/kafka-topics.sh --bootstrap-server localhost:9092 --list"
```
La consola **nunca** habla con Elasticsearch ni con Kafka: solo con el API, que re-verifica cada resultado contra la autoridad.

### 5.7 Observabilidad (Prometheus)
- http://127.0.0.1:9090/targets → tres exportadores en `up` (Kafka, Elasticsearch, Connect). El API empuja sus métricas por OTLP.
- http://127.0.0.1:9090/rules → **28 reglas de alerta** de la tubería (sondas, ingestión, proyección, CDC, frescura de búsqueda, retención).
- Consultas para `/graph`:
  - `monitoring_search_freshness_lag_seconds` — retraso de la búsqueda frente a la autoridad.
  - `monitoring_ingestion_pending_events` — eventos aceptados aún sin proyectar.
  - `monitoring_pipeline_projection_oldest_pending_seconds` — antigüedad del más viejo.
  - `monitoring_pipeline_wal_slot_retained_bytes` — WAL retenido por el slot de replicación.
- Cada alerta tiene causa y acción en el [runbook](../runbooks/pipeline-alerts.md).
- **Provocar fallos de verdad** (cada uno dispara su alerta y la resuelve al deshacerlo; tarda varios minutos):
  ```powershell
  $env:TELEMETRY_OTLP_ENDPOINT = 'http://prometheus:9090/api/v1/otlp/v1/metrics'
  node scripts/lab/alert-rehearsal.mjs
  ```

### 5.8 Identidad y seguridad (para quien lo pregunte)
- **Keycloak real** (26.7.5). El API valida emisor, audiencia, firma, vigencia y algoritmo (solo asimétricos); los roles y ámbitos salen del token, nunca de cabeceras.
- **TLS API → Keycloak**: el API lee el descubrimiento y las claves por **HTTPS** y solo confía en la CA del laboratorio ([ADR-025](../architecture/decisions/ADR-025.md)). El navegador usa `http://127.0.0.1:8081` (loopback) para no obligarte a instalar ningún certificado.
- **Rotación de claves sin reiniciar**: en *Keycloak admin → Realm settings → Keys → Add provider (rsa-generated)* con prioridad mayor; el siguiente inicio de sesión ya firma con la clave nueva y el API la acepta a la primera. Una clave retirada deja de valer en ≈5 min ([ADR-026](../architecture/decisions/ADR-026.md)).
- **Caída del proveedor**: `docker stop monitoring-local-keycloak-1`. Quien ya está dentro sigue trabajando (el API conserva las claves); no se puede iniciar sesión nueva hasta `docker start monitoring-local-keycloak-1`.

### 5.9 Resiliencia en vivo
- `docker stop monitoring-local-elasticsearch-1` → la consola muestra un error claro («Búsqueda no disponible», 503, o «tiempo de espera», 504) en lugar de datos viejos; al arrancarlo vuelve sola con «Reintentar consulta».
- Para el estado general: `pwsh scripts/lab/core.ps1 -Action health -Project monitoring-local`.

### 5.10 Pruebas automáticas
```powershell
dotnet test Monitoring.slnx                       # ≈ 500 pruebas (necesita Docker: levanta su propio PostgreSQL)
npm --prefix src/monitoring-web test              # 178 pruebas de la consola
cd tests/e2e; npm ci; npx playwright install chromium
$env:E2E_IDENTITY='1'; $env:MONITORING_COMPOSE_PROJECT='monitoring-local'; npx playwright test identity-keycloak identity-rotation
```
Los E2E de identidad recorren el inicio de sesión real, el aislamiento entre sitios, la matriz de roles, la rotación de claves y la caída de Keycloak. Los specs de identidad pueden correr con la consola `start:lab` encendida (Playwright la reutiliza); el spec sin identidad (`session-vertical`) necesita el API en modo desarrollo.

## 6. Límites que conviene decir en voz alta

- **Datos sintéticos.** La sonda real (captura con `tshark` sobre SPAN/TAP) está implementada y probada con PCAP sintético, pero no hay captura en vivo en esta demo.
- **Laboratorio, no producción.** Kafka sin cifrar, Elasticsearch sin seguridad y Keycloak en `start-dev` con base embebida. Los perfiles de seguridad de producción, mTLS de sondas con una PKI real (EJBCA) y alta disponibilidad no están cerrados ([roadmap](../roadmap.md)).
- **PostgreSQL es la elección provisional** de almacén de sesiones hasta el ensayo de capacidad (S17).
- **Consultas retenidas:** una búsqueda con más de una página retiene un «snapshot» 10 min. En producción cada usuario puede tener 2; el laboratorio lo sube a 8 para que recargar la página no te bloquee (`Search__Leases__MaxPerSubject`).
- **«Índice retrasado» en un entorno ya usado para pruebas.** La consola compara cada minuto la autoridad con el índice. Si el entorno guarda restos de fixtures de pruebas que están en PostgreSQL pero no en Elasticsearch, el aviso naranja es **correcto**, no un fallo de la consola. Para una demo limpia empieza con volúmenes nuevos (ver *Apagar* en el §2). Comprobación: `docker exec monitoring-local-postgres-1 psql -U monitoring -d monitoring -c "select examined, missing, stale from monitoring.search_projection_check order by finished_at desc limit 1"` (`missing` y `stale` a 0 = al día).

## 7. Solución de problemas

| Síntoma | Causa | Qué hacer |
|---|---|---|
| «Inicia sesión…» o «sin proveedor de identidad» | La consola se arrancó sin la configuración de laboratorio | Para y arranca con `npm --prefix src/monitoring-web run start:lab` (no `start`) |
| Keycloak: `Invalid parameter: redirect_uri` | Abriste la consola desde un origen que no es `localhost:4200` ni `127.0.0.1:4200` | Usa uno de esos dos |
| Keycloak: «Invalid username or password» | Usuario o contraseña mal; o bloqueo temporal tras varios fallos | Escribe el usuario (no el correo); repasa `lab-secrets/credenciales-demo.txt`; espera unos minutos |
| «No se pudo contactar con el proveedor de identidad» | Keycloak parado o aún arrancando | `docker ps` y espera a `healthy`; `docker start monitoring-local-keycloak-1` |
| Todo da 401 durante ≈1 min tras recrear Keycloak | Claves de firma nuevas aún no leídas | Espera un minuto (el API las relee solo) |
| 429 «Demasiadas consultas simultáneas» | Límite de consultas con varias páginas abiertas | Acota el rango o añade filtros; caduca a los 10 min |
| El listado muestra «Índice retrasado» | Proyección o conector parado, **o restos de pruebas antiguas** en la autoridad que no están indexados | Comprueba `missing`/`stale` (consulta del §6), `/connectors/*/status` y el [runbook](../runbooks/pipeline-alerts.md); para una demo limpia, volúmenes nuevos |
| Docker se queda sin memoria / error 500 | Poca RAM para WSL/Docker Desktop | 8 GB para WSL (`memory=8GB` en `~/.wslconfig`, luego `wsl --shutdown`) |
| `address already in use` | Otro proceso usa 4200, 5080, 8081, 9090, 9200 u 8083 | Libera el puerto o para el otro proceso |
| Cambiaste código del API y no se nota | La imagen del API la construye el servicio `migrate` | `pwsh scripts/lab/prepare.ps1`, `docker compose … build migrate` y recrea `api` |
