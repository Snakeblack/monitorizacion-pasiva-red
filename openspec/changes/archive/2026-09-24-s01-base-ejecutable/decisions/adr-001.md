# ADR-001: Frontera de dominio y migración PostgreSQL inicial

- Status: proposed
- Change: s01-base-ejecutable
- Date: 2026-09-24

## Context

S01 debe demostrar una migración repetible sin anticipar el contrato de S02. ADR-014 permite PostgreSQL solo como candidato y exige mantener la persistencia separada para una revisión posterior.

## Decision

Crear `Monitoring.Domain` sin referencias de infraestructura, `Monitoring.Persistence` con EF Core y Npgsql y `Monitoring.Host` como compositor. La primera migración crea únicamente el esquema `monitoring` y el historial de EF Core. El host la ejecuta con `--migrate`, de forma explícita y con fallo observable.

## Alternatives

- DbContext en el host: menos proyectos, pero mezcla el ciclo de arranque con el almacén provisional.
- Tablas de eventos/sesiones en S01: adelanta un modelo todavía no especificado en S02/S03.
- SQL aplicado manualmente: evita EF Core, pero requiere implementar seguimiento e idempotencia de migraciones desde cero.

## Consequences

S02 puede añadir modelos y una nueva migración sin cambiar el dominio por el proveedor. EF Core/Npgsql añaden dependencias y convenciones que deberán mantenerse. Cambiar de motor exigirá nuevas migraciones y adaptación de persistencia, pero no de un contrato de ingestión S01 inexistente. Esta decisión no acredita capacidad de PostgreSQL para producción.
