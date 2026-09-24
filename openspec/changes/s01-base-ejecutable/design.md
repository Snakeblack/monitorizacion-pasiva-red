# Design: S01 — base ejecutable

## Technical Approach

S01 crea una solución .NET 10 mínima con host ASP.NET Core, ensamblado de dominio sin referencias a infraestructura y módulo PostgreSQL. La única función del almacén en este slice es aplicar y comprobar una migración inicial: esquema `monitoring` y tabla de historial de EF Core, sin tablas de eventos, sesiones ni inventario. PostgreSQL sigue siendo candidato provisional según [ADR-014](../../../docs/architecture/decisions/ADR-014.md). El host expone únicamente `/health/live` para comprobar el arranque, sin API funcional. La migración es una operación explícita (`dotnet run --project src/Monitoring.Host -- --migrate`); el arranque normal no modifica el esquema.

Se usa `dotnet test` como runner acordado y se configura antes de escribir comportamiento bajo TDD estricto. Las pruebas de integración arrancan PostgreSQL desechable con Testcontainers; el mismo comando funciona localmente y en GitHub Actions con Docker. El CI restaura, compila y ejecuta las pruebas sobre un checkout limpio.

## Architecture Decisions

### Decision: Frontera de dominio y migración provisional

| Opción | Trade-off | Decisión |
|---|---|---|
| `Monitoring.Domain` sin dependencias, `Monitoring.Persistence` con EF Core y Npgsql, host que compone ambos | Tres proyectos y paquetes de persistencia; impide que la elección provisional entre en el dominio | Elegida; ver [ADR-001](decisions/adr-001.md) |
| DbContext y migraciones en el host | Menos archivos ahora; mezcla arranque y persistencia y dificulta revisar el motor en S17 | Descartada |

La migración inicial solo asegura el espacio de nombres y el historial de versiones. No se crea un modelo de evento especulativo para S02. EF Core `MigrateAsync` aplica migraciones pendientes y deja intacta una versión ya aplicada. La cadena de conexión procede de `ConnectionStrings:Monitoring` o de su variable de entorno equivalente; no se guarda una credencial en el repositorio. El comando de migración termina con código distinto de cero si no conecta o falla la migración y registra el error sin imprimir la cadena de conexión. El host no declara disponibilidad de base cuando solo responde `/health/live`.

### Decision: PostgreSQL efímero en el runner

| Opción | Trade-off | Decisión |
|---|---|---|
| xUnit + `Microsoft.NET.Test.Sdk` + Testcontainers PostgreSQL | Exige Docker local y en CI; cada ejecución obtiene instancia aislada y el mismo mecanismo | Elegida; ver [ADR-002](decisions/adr-002.md) |
| Servicio PostgreSQL distinto en CI y contenedor manual local | Menos dependencia en el proyecto de pruebas; dos rutas de configuración e aislamiento | Descartada |

Las pruebas de migración y arranque comparten un contenedor por colección, crean una base vacía por caso cuando sea necesario y eliminan el recurso al terminar. El test de indisponibilidad usa un destino local no escuchado con timeout acotado. No se usa la base de una ejecución anterior.

## Data Flow

```text
CLI --migrate → Host (configuración) → Persistence (EF Core/Npgsql) → PostgreSQL efímero
                       │                        │
                       │                        └→ monitoring + historial de migraciones
                       └→ salida no cero ante error

GET /health/live → Host → 200 (sin consultar PostgreSQL)
```

El host referencia dominio y persistencia; persistencia puede referenciar dominio, pero dominio no referencia a ninguno. No existe contrato de ingestión ni interfaz de repositorio antes de que haya una operación de dominio que lo necesite.

## File Changes

| Path | Action | Purpose |
|---|---|---|
| `Monitoring.slnx`, `global.json`, `Directory.Build.props` | Create | Solución, SDK 10 y convenciones comunes reproducibles. |
| `src/Monitoring.Domain/Monitoring.Domain.csproj` | Create | Límite de dominio sin paquetes de persistencia. |
| `src/Monitoring.Persistence/Monitoring.Persistence.csproj`, `src/Monitoring.Persistence/MonitoringDbContext.cs`, `src/Monitoring.Persistence/Migrations/**` | Create | Proveedor Npgsql y migración inicial sin tablas funcionales. |
| `src/Monitoring.Host/Monitoring.Host.csproj`, `src/Monitoring.Host/Program.cs` | Create | Composición, comando `--migrate` y liveness. |
| `tests/Monitoring.Tests/Monitoring.Tests.csproj`, `tests/Monitoring.Tests/**` | Create | xUnit, verificación de frontera, arranque y migración con PostgreSQL desechable. |
| `.github/workflows/ci.yml` | Create | `setup-dotnet` 10, restore, build y test en `ubuntu-latest` con Docker. |
| `README.md`, `openspec/config.yaml` | Create/modify | Prerrequisitos, comandos, configuración desechable y runner/puertas de calidad. |

## Interfaces / Contracts

- El único contrato HTTP de S01 es `GET /health/live` con 200 cuando el proceso está en marcha. No afirma conectividad, migración ni preparación para tráfico funcional.
- `--migrate` exige `ConnectionStrings:Monitoring`; sin valor, conexión o migración válida devuelve salida no cero. Repetirlo contra la misma base deja una sola fila para la migración inicial y conserva el esquema.
- `Monitoring.Domain` no referencia `Monitoring.Persistence`, EF Core ni Npgsql. No se define aún un puerto de almacenamiento sin consumidor de dominio.
- El esquema `monitoring` y `public.__EFMigrationsHistory` son el estado inicial verificable; S02 añadirá sus tablas mediante una nueva migración. Mantener el historial en `public` evita depender de un esquema que todavía no existe al iniciar la primera migración.

## Testing Strategy

| Requirement / quality concern | Trigger and conditions | Expected response | Verification |
|---|---|---|---|
| REQ-base-ejecutable-001 | Host con configuración limpia | Arranca y responde 200 en `/health/live` sin datos de S02 | `WebApplicationFactory`/host test; inspección de referencias de proyectos |
| REQ-base-ejecutable-002 | PostgreSQL vacío; ejecutar migración dos veces | Esquema e historial creados, una sola versión; segunda ejecución no altera objetos | Integración Testcontainers y consulta a catálogo/historial |
| REQ-base-ejecutable-002 | PostgreSQL inaccesible al migrar | Salida no cero, error visible sin secretos, ninguna declaración de éxito | Integración del comando con endpoint cerrado |
| REQ-base-ejecutable-003 | Inspección de solución y migración | Sin entidades/tablas o endpoints de S02 | Revisión de diff y aserciones del esquema |
| REQ-verificacion-base-001/002 | Checkout limpio en CI, Docker disponible | Restore, build y tests concluyen; cualquier fallo vuelve rojo el job | Ejecución real de `.github/workflows/ci.yml` y comandos documentados |
| REQ-verificacion-base-003 | Persona sigue README con SDK 10 y Docker | Puede probar, migrar y arrancar; entiende provisionalidad | Reproducción de comandos en entorno limpio |

Antes de implementar cada comportamiento se registra una prueba que falla por la capacidad ausente; después se implementa lo mínimo para pasarla y se ejecuta el conjunto focal. `dotnet test Monitoring.slnx` es el comando único de verificación. El CI ejecuta `dotnet restore Monitoring.slnx`, `dotnet build Monitoring.slnx --no-restore` y `dotnet test Monitoring.slnx --no-build`. La prueba de arquitectura puede examinar referencias de ensamblados, sin acoplarse al texto del `.csproj`.

## Migration / Rollout

No hay datos preexistentes que migrar. Para un entorno desechable: arrancar PostgreSQL vacío, fijar `ConnectionStrings__Monitoring`, ejecutar `--migrate` y después iniciar el host. La base y credenciales de prueba se descartan al terminar. No se ejecutan migraciones automáticamente al responder peticiones. La reversión de S01 retira host, pruebas y esquema desechable; ninguna conclusión de S17 se adelanta.

## Open Questions

Ninguna bloquea S01. La plataforma y el mecanismo de despliegue de producción siguen pendientes para los slices de seguridad y operación.
