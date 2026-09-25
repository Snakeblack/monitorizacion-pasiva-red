# ADR-002: Admisión y cuota móvil en una transacción por origen

- Status: proposed
- Change: s02-contrato-ingestion-durable
- Date: 2026-09-24

## Context

`REQ-bandeja-ingestion-durable-001/003` exige lote atómico, ACK tras commit y máximo 500 eventos nuevos confirmados por origen en cualquier ventana móvil de 60 segundos. Dos instancias pueden recibir lotes concurrentes.

## Decision

Crear o localizar una fila de coordinación `(site_id, sensor_id)` y bloquearla `FOR UPDATE` durante la transacción. Resolver duplicados/conflictos, contar aceptaciones en los últimos 60 segundos, insertar solo eventos nuevos y confirmar antes de devolver 200. Marcar `accepted_at` con el reloj de PostgreSQL después del bloqueo; indexar `(site_id, sensor_id, accepted_at)`.

## Alternatives

- Contador local por proceso: no protege concurrencia entre instancias ni reinicios.
- Caché externa: introduce otro sistema operativo y una frontera de atomicidad nueva antes de demostrar necesidad.

## Consequences

La cuota y la bandeja comparten commit, pero una sonda serializa sus lotes; la capacidad de esa ruta debe medirse en S17. Un fallo previo al commit revierte los eventos nuevos y nunca produce ACK de éxito.
