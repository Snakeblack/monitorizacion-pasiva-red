# Stack interno reproducible

K01/K02 adoptan la autoridad PostgreSQL y la base de publicación de ADR-015/016. K04 incorpora aquí **solo el core sintético aislado**: PostgreSQL18.6, Kafka4.3.1, Connect con Debezium3.6.3.Final y sink16.0.0, Elasticsearch9.5.4, migrador, bootstrap y API. Todos tienen espera finita; migrate/bootstrap deben terminar0. Datos, broker, búsqueda, Connect y API carecen de puertos publicados. No conectar sondas ni datos reales en este core.

## Preparación y comandos

Requiere Docker Linux con al menos4GiB disponibles para este smoke, .NET SDK10.0.303, Node24.16/npm12 y PowerShell7. En Windows se utiliza Ubuntu WSL; Compose corre dentro de WSL para montar el checkout con rutas Linux. El CLI Windows puede construir imágenes, pero los montajes remotos de secretos no admiten rutas `C:/...`. No se cambian opciones globales de Docker ni del sistema.

```powershell
$env:DOCKER_HOST='tcp://127.0.0.1:2375'
$env:TESTCONTAINERS_HOST_OVERRIDE='127.0.0.1'
Remove-Item Env:DOCKER_CONTEXT -ErrorAction SilentlyContinue
./scripts/lab/core.ps1 -Action up
./scripts/lab/core.ps1 -Action health
./scripts/lab/core.ps1 -Action test -Project monitoring-foundation-test
./scripts/lab/core.ps1 -Action down
```

`prepare.ps1` genera secretos locales aleatorios fuera de git en `lab-secrets/`, verifica hashes de plugins y publica la API con el SDK exacto. Se reutilizan secretos y volúmenes existentes; cambiarlos sin migrar las credenciales de PostgreSQL no rota contraseñas de una base inicializada. La imagen runtime ASP.NET10.0.12 está fijada por digest. El registro oficial no ofrece una imagen SDK10.0.303; el publish portable con SDK host evita cambiar el SDK aprobado.

En Linux se usa el socket local y se omiten las variables WSL; `pwsh scripts/lab/core.ps1` y `node --test tests/stack/foundation.test.mjs` usan Docker directamente. Los defaults no fuerzan el daemon TCP fuera de Windows.

`deploy/versions.env` fija digests de imágenes oficiales; `deploy/connect/Dockerfile` comprueba SHA256 de los dos plugins durante build. Orígenes: [Maven Debezium](https://repo.maven.apache.org/maven2/io/debezium/debezium-connector-postgres/3.6.3.Final/), [manifest oficial del sink16](https://api.hub.confluent.io/api/plugins/confluentinc/kafka-connect-elasticsearch/versions/16.0.0), [registro Microsoft](https://mcr.microsoft.com/en-us/product/dotnet/sdk/about). No se descargan ni instalan plugins durante startup. Kafka incluye Java21. El sink usa Confluent Community License; Elasticsearch ELv2; PostgreSQL PostgreSQL License; Kafka/Debezium Apache2.0. La distribución final/SBOM se completa en S18.

## Migración, parada y reinicio

`--migrate` serializa migración y backfill con lock PostgreSQL.0004 añade `session_identity`, `session_metadata` y `projection_outbox`; mantiene las cinco columnas de `session_projection`. El backfill valida contratos por páginas100, conserva originales y genera metadatos/outbox una vez. Una interrupción vuelve a intentarse sin duplicar publicación. Down destructivo está prohibido. 0005 añade `session_identity.search_document_id` (uuid único, permanente, asignado por PostgreSQL a las identidades existentes) y admite `schema_version`2 en el outbox. Tras migrar, `--migrate` reproyecta cada identidad activa y cada barrera a `monitoring.sessions.v2` con su revisión actual, una sola vez; el historial v1 permanece intacto. El sink consume solo v2 hacia `sessions-v2-000001` y `node scripts/lab/pipeline.mjs` retira el alias `sessions-read` de las generaciones `sessions-v1-*`. El transform se prueba sin contenedores con `node --test tests/stack/session-contract.test.mjs` tras `scripts/lab/build-transform.ps1`.

`core.ps1 -Action down` elimina solo contenedores/red del proyecto especificado y conserva volúmenes. Para comenzar una prueba realmente vacía, utilizar un proyecto `monitoring-*` nuevo. La limpieza de volúmenes es una acción separada explícita; verificar antes que pertenecen al proyecto elegido:

```powershell
wsl.exe -d Ubuntu --cd (Get-Location).Path env -u DOCKER_CONTEXT DOCKER_HOST=tcp://127.0.0.1:2375 docker compose --env-file deploy/versions.env -p monitoring-foundation-test -f compose.yaml down --volumes
```

En el laboratorio WSL de4GiB, ejecutar la suite Testcontainers con el core detenido, conservando volúmenes. Se observaron desapariciones intermitentes de Ryuk en ejecuciones paralelas, incluso una con el core parado; no hay evidencia de OOM que permita atribuir una causa. No se contabilizan como RED funcional ni como GREEN. La alternativa acotada ejecuta colecciones xUnit en serie, preservando las pruebas de concurrencia explícitas dentro de cada test: `dotnet test Monitoring.slnx -- xUnit.ParallelizeTestCollections=false xUnit.MaxParallelThreads=1`. Volver a iniciar el core después. Los detalles están en el apply-progress y TRX/logs del change; [xUnit documenta esas opciones](https://xunit.net/docs/config-runsettings).

## Siguiente integración y límites

Connect descubre ambos plugins, pero aún no hay source/sink ni mapping/alias. K03 configura EventRouter con header `revision`, source de la publicación `monitoring_outbox` y sink16 INSERT con external version; no emplear offsets como revisión. El bootstrap restringe SELECT del usuario CDC al outbox; la aplicación usa un rol propio sin superuser, pool≤20 y timeout.

K04 **permanece incompleto**: fuente/sink/mapping, UI/generador/verifier vertical, Keycloak/EJBCA, TLS/mTLS/ACL y perfiles de seguridad siguen pendientes. Kafka es PLAINTEXT y Elasticsearch desactiva seguridad dentro de la red interna del core sintético; esto no satisface el despliegue productivo. Las imágenes y arranque de seguridad se ensayan en U07. CI vertical se completa en1.7. S17/S18 siguen siendo gates de capacidad, continuidad, políticas y liberación; este arranque no los acredita.

## Búsqueda, API y reconstrucción

La API arranca en el core con `Search__Elasticsearch__Url` (activa `ElasticsearchSessionSearch`, la comprobación de vigencia en PostgreSQL, los leases de snapshots y la reconciliación periódica), el ámbito de lectura de desarrollo `TrustedSessionRead__SiteId/SensorId` (`pipeline-site`/`pipeline-sensor`, solo Development/Testing hasta S12) y `DataProtection__KeysPath` para el anillo de claves que sella los cursores; sin anillo persistente compartido, los cursores no sobreviven a un reinicio ni entre instancias. Otras claves: `Search:Leases` (`MaxPerSubject`=2, `MaxGlobal`=20), `Search:Projection` (`Grace`, `MaxCheckAge`, `Interval`), `Search:Elasticsearch` (`Index`, `KeepAlive`, `BlockSize`, `ApiKey`).

`dotnet Monitoring.Host.dll --rebuild-search` construye una generación nueva de índice y conector, copia y pone al día desde la autoridad, toma el lock exclusivo de publicación (máx. 30 s), espera la convergencia verificada y cambia el alias; requiere `Search:Connect:Url`, `Search:Rebuild:MappingPath` (`deploy/elasticsearch/sessions-v2.json`) y `Search:Rebuild:SinkTemplatePath` (`deploy/connect/sink.json`). Salida 0 = cambiado, 2 = no cambiado (el índice anterior sigue sirviendo; reintentar es seguro), 1 = fallo o configuración inválida.

Verificación sin contenedores: `./scripts/lab/fetch-transform-libs.sh` descarga los jars de Kafka fijados por SHA-256 y `node --test tests/stack/session-contract.test.mjs` prueba el guard del sink (exige JDK21). El recorrido real (`tests/stack/search-pipeline.test.mjs`, `tests/stack/search-api.test.mjs`) requiere el stack levantado y `node scripts/lab/pipeline.mjs`; el job `stack` de GitHub Actions lo ejecuta solo con `workflow_dispatch` hasta que haya pasado una vez en un runner. **A fecha de este cambio ninguno de esos dos tests ni el job se ha ejecutado**: la sesión de desarrollo no tenía Docker.

## Cuota y cuarentena de ingestión

La cuota de eventos nuevos por origen es configurable (`Ingestion:MaxNewEventsPerMinute`, 6000 por defecto como punto de partida de laboratorio; debe ser positiva y S17 la valida). El lote que la excede se rechaza completo con 429. Un evento permanentemente incompatible (contrato inválido, identidad suprimida o proyección en conflicto) pasa a **cuarentena** en vez de quedar pendiente indefinidamente o detener el worker: `ingestion_inbox.quarantined_at`, `ingestion_quarantine` (identidad, causa mínima, estado, intentos, tiempos, quién y por qué lo resolvió) e `ingestion_quarantine_audit` (cada cambio de estado, una vez, en la misma transacción). El evento aceptado es inmutable (trigger) y no puede borrarse mientras exista su registro. El saldo se concilia como aceptados = procesados + en cuarentena + pendientes.

`dotnet Monitoring.Host.dll --quarantine list|summary|discard|replace` es la única vía de resolución: `discard <sitio> <sonda> <evento> --actor A --reason R` y `replace <sitio> <sonda> <evento> <correctivo> --actor A --reason R` (el evento correctivo debe ser otro evento aceptado y ya procesado del mismo origen). Salida 0 = hecho, 2 = no aplicable, 1 = uso inválido; nunca imprime payloads. Las métricas `monitoring.ingestion.*` (saldo, `quarantine_unresolved` por causa y edad del más antiguo) se publican desde el resumen de la base de datos.

