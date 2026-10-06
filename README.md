# Monitorización pasiva de red

Base ejecutable S01–S04 del sistema de monitorización: host ASP.NET Core, ingestión durable S02, proyección idempotente de sesiones sintéticas S03 con detalle aislado por sede/sonda, y vista Angular interna del detalle. Las pruebas usan PostgreSQL desechable. Captura real, inventario y acceso humano de producción pertenecen a slices posteriores.

## Requisitos

- .NET SDK 10.0.303, fijado en `global.json`.
- Node 24.16.0 y npm para la SPA en `src/monitoring-web/` (CI usa esa versión de Node; el lockfile se versiona).
- Docker Desktop o Docker Engine en ejecución. Las pruebas de integración .NET y la cadena fixture→vista crean su propio contenedor PostgreSQL con Testcontainers.

## Restaurar, compilar y probar

Desde la raíz del repositorio:

```sh
dotnet restore Monitoring.slnx
dotnet build Monitoring.slnx --no-restore
dotnet test Monitoring.slnx
```

La suite de migraciones necesita Docker accesible. GitHub Actions ejecuta esos mismos pasos sobre un checkout limpio; las pruebas aprovisionan y eliminan PostgreSQL automáticamente. El job también instala la SPA con `npm ci` y ejecuta `npm test` en `src/monitoring-web` (incluye la cadena hasta la vista; no añadir `-- --watch=false` con npm 12).

Para la vista sola, desde la raíz:

```sh
npm --prefix src/monitoring-web test
```

## Arranque y liveness

El arranque normal no necesita PostgreSQL ni aplica migraciones. Para iniciar el host en el puerto 5080:

```sh
dotnet run --project src/Monitoring.Host -- --urls http://localhost:5080
```

Comprueba el endpoint de liveness, que devuelve HTTP 200 sin consultar la base de datos:

```sh
curl.exe --fail http://localhost:5080/health/live
```

## Vista Angular (solo desarrollo)

La SPA vive en `src/monitoring-web/` y no entra en `Monitoring.slnx`. El host no llama a `UseStaticFiles`; `dotnet publish` del host no copia `wwwroot` ni artefactos de `monitoring-web`. La vista interna no se publica en producción.

Con el host en el puerto 5080, el proxy de `ng serve` reenvía `/api` a `http://127.0.0.1:5080` sin añadir ámbito:

```sh
npm --prefix src/monitoring-web start
```

Abre `/sessions/{eventId}` en el origen del CLI. El navegador no envía sede ni sonda. En Development o Testing el servidor instala el ámbito solo si existen ambas claves, sin espacios:

```
TrustedSessionRead:SiteId
TrustedSessionRead:SensorId
```

Equivalente en variables de entorno: `TrustedSessionRead__SiteId` y `TrustedSessionRead__SensorId`. En PowerShell:

```powershell
$env:TrustedSessionRead__SiteId = "site-a"
$env:TrustedSessionRead__SensorId = "sensor-a"
dotnet run --project src/Monitoring.Host -- --urls http://localhost:5080
```

Sin las dos claves, o fuera de Development/Testing, el detalle responde 401 y la vista muestra error.

## Aplicar las migraciones

La migración es explícita e incremental: esquema S01, bandeja S02 y proyección/marca S03. Para una base local desechable, inicia PostgreSQL con Docker:

```sh
docker run --name s01-postgres \
  -e POSTGRES_USER=monitoring \
  -e POSTGRES_PASSWORD=monitoring-local \
  -e POSTGRES_DB=monitoring \
  -p 5432:5432 -d postgres:18-alpine
```

Configura la cadena de conexión y ejecuta el comando de migración:

```sh
export ConnectionStrings__Monitoring='Host=localhost;Port=5432;Database=monitoring;Username=monitoring;Password=monitoring-local'
dotnet run --project src/Monitoring.Host -- --migrate
```

En PowerShell, define la variable con `$env:ConnectionStrings__Monitoring = "Host=localhost;Port=5432;Database=monitoring;Username=monitoring;Password=monitoring-local"` antes de ejecutar la migración. El comando puede repetirse; el arranque normal sigue sin modificar el esquema. Al terminar, elimina el contenedor desechable con `docker rm -f s01-postgres`.

## Ingestión y sesiones sintéticas

Con `ConnectionStrings__Monitoring` configurada y las migraciones aplicadas, el arranque normal activa el worker S03. `--migrate` aplica el esquema y termina sin arrancar workers. Sin conexión, liveness sigue disponible y el worker no se registra.

`POST /api/v1/ingestion/batches` conserva el contrato S02: HTTP 200 vacío tras aceptar durablemente el lote, sin esperar proyección. Cualquier objeto `data` sigue siendo aceptable para ingestión. S03 reconoce exclusivamente este contrato de desarrollo/pruebas:

```json
{"kind":"synthetic-session","version":1,"sourceIp":"192.0.2.1","destinationIp":"2001:db8::2","sourcePort":0,"destinationPort":65535,"protocol":"TCP","startedAt":"2026-09-29T12:00:00Z","endedAt":"2026-09-29T12:00:00.123Z"}
```

Todos los campos son obligatorios; no se admiten adicionales. Protocolos TCP/UDP, IP válidas, puertos enteros 0–65535, instantes UTC `Z` con 0–3 decimales y fin ≥ inicio. Los datos inválidos/desconocidos permanecen pendientes sin impedir el procesamiento de los válidos; su tratamiento durable se reserva a S08.

La sesión conserva la identidad `(siteId, sensorId, eventId)`, `occurredAt` original y `data` JSONB. Proyección y `processed_at` se confirman en una transacción por evento; replay y concurrencia no duplican ni sobrescriben. El worker usa scopes nuevos por pasada, páginas de 100, espera cancelable de 1 segundo y reintento transitorio hasta 30 segundos; los errores no transitorios detienen el host. Revertir el código conserva proyecciones y marcas; no se admite un downgrade destructivo de S03.

`GET /api/v1/sessions/{eventId}` devuelve exactamente `eventId`, `siteId`, `sensorId`, `occurredAt` y `data` si existe dentro del ámbito confiable (200), o 404 si falta/es ajena. Exige un feature interno de lectura distinto de la identidad de ingestión y solo admite Development/Testing. Sin ese contexto responde 401; headers y query no conceden acceso. El host público no crea ese feature: las pruebas lo suministran desde el servidor. Production y entornos desconocidos responden 401 incluso con proveedor sustituido. OIDC/RBAC humano corresponde a S12.

PostgreSQL se adopta como autoridad según ADR-015; ADR-014 conserva la elección histórica provisional. Las pruebas S01/S02/S03 verifican migración, aceptación, rollback, replay, recuperación, concurrencia y aislamiento; la capacidad y preparación para producción se acreditarán en S17.
