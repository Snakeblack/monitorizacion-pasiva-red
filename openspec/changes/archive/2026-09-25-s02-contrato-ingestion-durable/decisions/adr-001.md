# ADR-001: Bandeja por evento y comparación JSON estructural

- Status: proposed
- Change: s02-contrato-ingestion-durable
- Date: 2026-09-24

## Context

`REQ-bandeja-ingestion-durable-002` exige una aceptación por origen e ID, igualdad por valor JSON sin ordenar arrays y conflicto sin reemplazo. S01 dispone de PostgreSQL provisional (ADR-014), sin bandeja previa.

## Decision

Persistir una fila por `(site_id, sensor_id, event_id)` con clave primaria compuesta y valor completo de evento en `jsonb`. Comparar el valor almacenado con el nuevo antes de insertar; conservar la primera fila. `batchId` se conserva como procedencia, pero no forma parte de la identidad del evento.

## Alternatives

- Bytes originales: una reordenación de propiedades produciría un conflicto falso.
- Hash del JSON canonicalizado: añade definición de canonicalización y manejo de colisiones sin necesidad en S02.

## Consequences

La base garantiza unicidad y `jsonb` ofrece igualdad estructural con arrays ordenados. La bandeja depende del candidato PostgreSQL hasta que S17 confirme o revise almacenamiento; el contrato HTTP permanece separado de ese formato.
