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

`--migrate` serializa migración y backfill con lock PostgreSQL.0004 añade `session_identity`, `session_metadata` y `projection_outbox`; mantiene las cinco columnas de `session_projection`. El backfill valida contratos por páginas100, conserva originales y genera metadatos/outbox una vez. Una interrupción vuelve a intentarse sin duplicar publicación. Down destructivo está prohibido.

`core.ps1 -Action down` elimina solo contenedores/red del proyecto especificado y conserva volúmenes. Para comenzar una prueba realmente vacía, utilizar un proyecto `monitoring-*` nuevo. La limpieza de volúmenes es una acción separada explícita; verificar antes que pertenecen al proyecto elegido:

```powershell
wsl.exe -d Ubuntu --cd (Get-Location).Path env -u DOCKER_CONTEXT DOCKER_HOST=tcp://127.0.0.1:2375 docker compose --env-file deploy/versions.env -p monitoring-foundation-test -f compose.yaml down --volumes
```

En el laboratorio WSL de4GiB, ejecutar la suite Testcontainers con el core detenido, conservando volúmenes. Se observaron desapariciones intermitentes de Ryuk en ejecuciones paralelas, incluso una con el core parado; no hay evidencia de OOM que permita atribuir una causa. No se contabilizan como RED funcional ni como GREEN. La alternativa acotada ejecuta colecciones xUnit en serie, preservando las pruebas de concurrencia explícitas dentro de cada test: `dotnet test Monitoring.slnx -- xUnit.ParallelizeTestCollections=false xUnit.MaxParallelThreads=1`. Volver a iniciar el core después. Los detalles están en el apply-progress y TRX/logs del change; [xUnit documenta esas opciones](https://xunit.net/docs/config-runsettings).

## Siguiente integración y límites

Connect descubre ambos plugins, pero aún no hay source/sink ni mapping/alias. K03 configura EventRouter con header `revision`, source de la publicación `monitoring_outbox` y sink16 INSERT con external version; no emplear offsets como revisión. El bootstrap restringe SELECT del usuario CDC al outbox; la aplicación usa un rol propio sin superuser, pool≤20 y timeout.

K04 **permanece incompleto**: fuente/sink/mapping, UI/generador/verifier vertical, Keycloak/EJBCA, TLS/mTLS/ACL y perfiles de seguridad siguen pendientes. Kafka es PLAINTEXT y Elasticsearch desactiva seguridad dentro de la red interna del core sintético; esto no satisface el despliegue productivo. Las imágenes y arranque de seguridad se ensayan en U07. CI vertical se completa en1.7. S17/S18 siguen siendo gates de capacidad, continuidad, políticas y liberación; este arranque no los acredita.
