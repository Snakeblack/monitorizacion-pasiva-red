# Proposal: S01 — base ejecutable

## Intent

Construir el primer slice ejecutable tras S00. Faltan solución, pruebas y CI. S01 debe demostrar arranque y migración limpios sin afirmar capacidad de producción.

## Scope

### In Scope

- Solución ASP.NET Core modular con host ejecutable y proyecto de dominio independiente de infraestructura.
- PostgreSQL provisional y migraciones mínimas para S01.
- Runner .NET, pruebas de arranque y migración con instancia efímera, y CI que ejecute dichas pruebas desde cero.
- Comandos de compilación/prueba y despliegue documentados; runner acordado y configurado antes de implementar con TDD estricto.

### Out of Scope

- Contrato de eventos, ingestión, bandeja, worker, API funcional, captura y UI de S02 en adelante.
- Datos reales, alta disponibilidad y aprobación de PostgreSQL para producción.

## Capabilities

### New Capabilities

- `base-ejecutable`: host, dominio y migración reproducible sobre PostgreSQL desechable.
- `verificacion-base`: pruebas .NET y CI desde un checkout y almacén limpios.

### Modified Capabilities

None. No hay especificaciones base existentes que modificar.

## Approach

Construir una base vertical mínima. Aislar PostgreSQL tras persistencia conforme a ADR-014. Definir runner, CI y aprovisionamiento efímero en diseño/tareas. Dividir en PR encadenadas compilables y probadas dentro del presupuesto de 400 líneas.

## Affected Areas

| Area | Impact | Description |
|------|--------|-------------|
| Solución y proyectos `.NET` | New | Host, dominio y persistencia modular. |
| Pruebas y CI | New | Runner, integración con PostgreSQL efímero y verificación limpia. |
| `openspec/config.yaml` y documentación | Modified | Comandos y despliegue. |

## Risks

| Risk | Likelihood | Mitigation |
|------|------------|------------|
| Herramientas no disponibles | Med | Confirmar runner, CI y PostgreSQL antes de aplicar. |
| Exceso de 400 líneas por PR | Med | Encadenar entregas autónomas con validación propia. |
| Acoplamiento al almacén provisional | Med | Mantener persistencia encapsulada y contratos de dominio independientes. |

## Rollback Plan

Revertir las PR de S01 en orden inverso y retirar la base efímera. Reevaluar el almacén si fallan ensayos posteriores.

## Dependencies

- S00 y ADR-014 completados; entorno .NET, runner, CI y PostgreSQL efímero por concretar en diseño/tareas.

## Success Criteria

- [ ] El host compila y arranca desde un checkout limpio.
- [ ] CI crea una instancia PostgreSQL vacía, aplica migraciones y supera pruebas sin datos previos.
- [ ] Los comandos y convenciones necesarios se ejecutan y quedan documentados.

**Branch advisory:** Before `sdd-apply` begins, a feature branch SHOULD be created following the `<tipo>/<descripción>` convention defined in the `branch-pr` skill (e.g. `git checkout -b feat/my-change main`). This note is SHOULD, not MUST.
