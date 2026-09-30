# ADR-001: Proyección y marcado atómicos por identidad de evento

- Status: proposed
- Change: s03-proyeccion-idempotente
- Date: 2026-09-29

## Contexto

La bandeja S02 conserva contenido e identidad compuesta. REQ-proyeccion-sesiones-idempotente-001/002 exige una sesión y commit conjunto con su marca.

## Decisión

Añadir processed_at nullable y una proyección con PK/FK de origen/evento, occurred_at_text y data JSONB. Bloquear el pendiente y proyectar/marcar en una transacción por evento. Replay exige igualdad antes de marcar, nunca reemplazo de contenido.

## Alternativas

- Commits separados: pueden perder pendientes o dejar sesiones sin marcado.
- Transacción del lote: amplía bloqueos y alcance del fallo.
- Otro almacén/broker: añade fallos distribuidos sin cuello medido, contrario a ADR-014.

## Consecuencias

Unicidad y rollback observables con PostgreSQL real; un commit por sesión requiere medición S17. Migración aditiva; rollback de código conserva datos y marcas. La garantía cubre los escritores autorizados, no corrupción manual.
