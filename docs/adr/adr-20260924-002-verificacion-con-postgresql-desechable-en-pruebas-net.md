# ADR-002: Verificación con PostgreSQL desechable en pruebas .NET

- Status: proposed
- Change: s01-base-ejecutable
- Date: 2026-09-24

## Context

Las specs exigen arranque y migración desde una base vacía tanto localmente como en CI. El repositorio no tiene runner ni infraestructura de pruebas configurada.

## Decision

Configurar xUnit y `Microsoft.NET.Test.Sdk` bajo `dotnet test`; las pruebas de integración usan Testcontainers PostgreSQL. GitHub Actions ejecuta el mismo runner en `ubuntu-latest` con Docker, SDK 10, restore, build y test. El contenedor es desechable y cada caso que verifica estado inicial usa base vacía.

## Alternatives

- Servicio PostgreSQL de GitHub Actions y contenedor manual local: dos rutas de aprovisionamiento y mayor riesgo de diferencia entre entornos.
- SQLite o proveedor EF en memoria: no verifican la migración ni el comportamiento real de PostgreSQL.

## Consequences

El mismo comando comprueba la ruta real de migración y falla si Docker o PostgreSQL no están disponibles. Docker es un prerrequisito explícito local y de CI. Las pruebas no prueban volumen, recuperación ni aptitud de producción; esas evidencias permanecen en S17.
