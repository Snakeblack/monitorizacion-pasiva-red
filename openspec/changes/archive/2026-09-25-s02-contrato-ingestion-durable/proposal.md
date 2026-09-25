# Proposal: Contrato v1 e ingestión durable S02

## Intent

Aceptar lotes sintéticos multisede sin perder ni duplicar eventos confirmados. S01 aporta el host y esquema inicial; S02 establece el contrato funcional y la frontera previa al ACK.

## Scope

### In Scope

- Definir y validar JSON v1 con `schemaVersion`, `batchId`, `eventId`, `siteId`, `sensorId` y UTC.
- Bandeja durable única por origen y `eventId`: reenvío idéntico aceptado, conflicto rechazado.
- Emitir ACK tras commit; aislar cuotas y señales por sede/sonda y probar rollback, concurrencia y ámbito.

### Out of Scope

- Worker, proyección, consultas, UI, captura y spool (S03–S07).
- mTLS (S13), cuarentena/conciliación (S08) y capacidad de producción (S17).

## Capabilities

### New Capabilities

- `contrato-ingestion-v1`: formato, validación y aislamiento de lotes y eventos.
- `bandeja-ingestion-durable`: commit, ACK, idempotencia, conflictos y señales por ámbito.

### Modified Capabilities

None. `base-ejecutable` y `verificacion-base` conservan sus requisitos S01.

## Approach

Validar antes de transaccionar y vincular el ámbito a una identidad confiable. Usar PostgreSQL provisional (ADR-014), migración incremental, bandeja relacional y restricción única por origen e `eventId`. Persistir contenido suficiente para comparar reenvíos. Precisar en specs/diseño formato, límites, igualdad y vínculo de identidad. Responder éxito tras commit; permitir reintento si se pierde el ACK.

## Affected Areas

| Area | Impact | Description |
|------|--------|-------------|
| `src/Monitoring.Host/` | Modified | Endpoint y validación. |
| `src/Monitoring.Domain/` | Modified | Contrato y resultado. |
| `src/Monitoring.Persistence/` | Modified | Bandeja y migración. |
| `tests/Monitoring.Tests/` | Modified | Contrato e integración. |

## Risks

| Risk | Likelihood | Mitigation |
|------|------------|------------|
| Suplantación de fuente | Medium | Vincular ámbito a identidad confiable. |
| Igualdad ambigua o carrera | Medium | Especificar igualdad; unicidad y prueba concurrente. |
| ACK perdido tras commit | Medium | Reintento idempotente por clave estable. |
| Cuotas o métricas de cardinalidad excesiva | Medium | Límites y etiquetas acotadas por ámbito en spec/diseño. |

## Rollback Plan

Deshabilitar el endpoint y revertir código; conservar la bandeja hasta drenar o exportar eventos. Revertir migración solo tras verificar ausencia de datos pendientes.

## Dependencies

- S01 y su runner .NET/PostgreSQL desechable; decisión provisional ADR-014.

## Success Criteria

- [ ] Un lote válido queda persistido antes del ACK; un rollback no produce ACK.
- [ ] Reenvío idéntico no duplica; mismo ID con contenido distinto se rechaza.
- [ ] Origen incorrecto no cruza ámbito; cuotas y señales distinguen sede/sonda.
- [ ] Pruebas TDD de contrato e integración pasan desde base vacía.

**Branch advisory:** Before `sdd-apply` begins, a feature branch SHOULD be created following the `<tipo>/<descripción>` convention defined in the `branch-pr` skill (e.g. `git checkout -b feat/my-change main`). This note is SHOULD, not MUST.
