# ADR-002: Pasadas paginadas sin bloqueo por inválidos

- Status: proposed
- Change: s03-proyeccion-idempotente
- Date: 2026-09-29

## Contexto

REQ-proyeccion-sesiones-idempotente-003 mantiene desconocidos/ inválidos pendientes y exige progresar sobre válidos posteriores. Seleccionar siempre la primera página incumpliría ese contrato.

## Decisión

Capturar cota superior por pasada, recorrer pendientes mediante cursor de accepted_at y clave compuesta, y avanzar sobre todas las claves. Reiniciar cursor al terminar; bloqueos SKIP LOCKED y commits tardíos vuelven a ser candidatos. Añadir índice parcial de pendientes; página interna de 100.

## Alternativas

- Repetir primera página: puede impedir todo progreso.
- Marcar inválidos procesados: viola contrato.
- Cuarentena/estado durable nuevo: amplía S08.

## Consecuencias

Memoria acotada y prueba determinista de más de una página inválida. Relee inválidos en nuevas pasadas y no acredita capacidad de producción. Tamaño/esperas reversibles; cambiar la estrategia no exige alterar eventos aceptados.
