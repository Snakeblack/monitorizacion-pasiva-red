# Propuesta: S03 — proyección idempotente y lectura mínima

## Intención

Convertir eventos sintéticos S02 en sesiones consultables; probar idempotencia, recuperación y aislamiento por sede/sonda.

## Alcance

### Incluido

- Contrato de sesión sintética dentro de `data`, preservando S02.
- Worker y proyección PostgreSQL con identidad `(siteId, sensorId, eventId)` y marcado procesado en la misma transacción.
- Detalle por ID limitado por un contexto confiable de lectura.
- Migración aditiva y pruebas Strict TDD de replay, rollback, recuperación y aislamiento.

### Excluido

Captura real (S05), correlación (S06), cuarentena y reconciliación completas (S08), listado (S09), Angular (S04), OIDC/RBAC humano (S12), retención y capacidad de producción.

## Capacidades

### Nuevas

- `contrato-sesion-sintetica`: discriminador, campos y validación de eventos sintéticos reconocidos por S03.
- `proyeccion-sesiones-idempotente`: proyección única por origen/evento, atomicidad y recuperación.
- `detalle-sesion-ambito`: detalle por ID con ámbito confiable y respuestas delimitadas.

### Modificadas

- `bandeja-ingestion-durable`: transición de pendiente a procesado atómica con la proyección; conserva ACK, cuotas y conflictos S02.

`contrato-ingestion-v1` conserva la aceptación de cualquier objeto `data`.

## Enfoque

Transacción por evento y clave única de origen/evento. El worker consume el ámbito persistido; el detalle aplica su ámbito confiable en la consulta parametrizada. Se conservan las capas existentes.

## Contrato aprobado

Confirmado mediante `approvals.s03-contract-001` en `state.yaml`:

- `data.kind = "synthetic-session"`, `data.version = 1`; `sourceIp`/`destinationIp` válidas, `sourcePort`/`destinationPort` enteros 0–65535, `protocol` TCP/UDP, `startedAt`/`endedAt` UTC terminado en `Z`, precisión máxima de milisegundos y fin ≥ inicio. Todos obligatorios; sin campos adicionales ni correlación/VLAN.
- Desconocidos o inválidos permanecen pendientes, sin proyección ni marcado, y no impiden avanzar sobre válidos; S08 resolverá su tratamiento durable.
- `eventId` identifica el detalle dentro del ámbito confiable sede/sonda; respuesta con `eventId`, `siteId`, `sensorId`, `occurredAt` y el `data` validado.
- Contexto de lectura exclusivo de desarrollo/pruebas, distinto de identidad de sonda; HTTP 401 sin contexto, 404 si falta o está fuera de ámbito, 200 si existe. OIDC/RBAC queda en S12.

## Áreas afectadas

| Área | Impacto |
|---|---|
| `src/Monitoring.Host/Program.cs`, `Sessions/` | Worker y endpoint |
| `src/Monitoring.Domain/Sessions/` | Contrato y tipos |
| `src/Monitoring.Persistence/MonitoringDbContext.cs`, `Migrations/`, `Sessions/` | Estado, proyección y consultas |
| `tests/Monitoring.Tests/` | PostgreSQL real y API |

## Riesgos

Marcado separado perdería eventos; un ámbito incompleto expondría sesiones; inválidos pendientes requieren tratamiento futuro. Mitigación: transacción única, pruebas negativas y S08.

## Reversión

Desactivar worker y detalle; revertir código conservando bandeja, proyecciones y marcas. No borrar ni reiniciar marcas automáticamente; reconstruir solo mediante operación explícita posterior.

## Dependencias

S02, S00/ADR-014 y PostgreSQL/Testcontainers.

## Criterios de éxito

- [ ] Replay deja una sesión por origen/evento.
- [ ] Fallo entre inserción y marcado revierte ambos; el reinicio recupera el pendiente.
- [ ] Detalle no cruza sede/sonda; S02 conserva su contrato y sus pruebas.

> **Branch advisory:** Before `sdd-apply` begins, a feature branch SHOULD be created following the `<tipo>/<descripción>` convention defined in the `branch-pr` skill (e.g. `git checkout -b feat/my-change main`). This note is SHOULD, not MUST.
