# Monitorización pasiva de red

Base ejecutable S01 del sistema de monitorización. Este slice ofrece un host ASP.NET Core mínimo, el límite vacío del dominio, una migración inicial y pruebas con PostgreSQL desechable. No incluye ingestión, sesiones, inventario, API funcional ni interfaz.

## Requisitos

- .NET SDK 10.0.303, fijado en `global.json`.
- Docker Desktop o Docker Engine en ejecución. Las pruebas de integración crean su propio contenedor PostgreSQL con Testcontainers.

## Restaurar, compilar y probar

Desde la raíz del repositorio:

```sh
dotnet restore Monitoring.slnx
dotnet build Monitoring.slnx --no-restore
dotnet test Monitoring.slnx
```

La suite de migraciones necesita Docker accesible. GitHub Actions ejecuta esos mismos pasos sobre un checkout limpio; las pruebas aprovisionan y eliminan PostgreSQL automáticamente.

## Arranque y liveness

El arranque normal no necesita PostgreSQL ni aplica migraciones. Para iniciar el host en el puerto 5080:

```sh
dotnet run --project src/Monitoring.Host -- --urls http://localhost:5080
```

Comprueba el endpoint de liveness, que devuelve HTTP 200 sin consultar la base de datos:

```sh
curl.exe --fail http://localhost:5080/health/live
```

## Aplicar la migración inicial

La migración es explícita. Para una base local desechable, inicia PostgreSQL con Docker:

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

PostgreSQL se adopta provisionalmente para S01 según ADR-014. Estas pruebas verifican el esquema mínimo y la migración; no validan capacidad, seguridad ni preparación para producción.
