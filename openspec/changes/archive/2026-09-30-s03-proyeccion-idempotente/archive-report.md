# Plan de archivo: S03 — proyección idempotente

Fecha prevista: 2026-09-30 (Europe/Madrid). Destino previsto: openspec/changes/archive/2026-09-30-s03-proyeccion-idempotente/.

## Resultado y foco de revisión

S03 proyecta eventos sintéticos aceptados en sesiones PostgreSQL y marca procesado en la misma transacción por evento. El detalle conserva aislamiento por sede/sonda y el mecanismo de lectura Development/Testing. Se conservan ACK, cuotas, conflictos e idempotencia S02. Captura real, correlación, cuarentena S08, OIDC/RBAC S12 y capacidad S17 siguen fuera del alcance aprobado.

Revisar conservación de requisitos, huellas y promociones ADR. Este informe prepara el cierre; la autoridad para escribir destinos vivos, mover el cambio y emitir el recibo final pertenece al runtime. El origen sigue activo.

## Verificación y puertas

- Ruta standard: propuesta, cuatro specs, diseño, progreso y 14/14 tareas completos.
- PASS WITH WARNINGS; 19 escenarios MUST y 109/109 pruebas, sin CRITICAL ni BLOCKER. Build registrado: cero errores/advertencias. Archive no repite pruebas.
- Quality gates: tests pass, required true, on_fail halt.
- Quality review: trust, runtime, evolution y efficiency completados una vez; cero hallazgos y linaje terminal aprobado. Coordinador comprobó identidad downstream de archive.
- Ambas suposiciones confirmadas. Single-pr con size:exception aprobado.

## Aviso convertido en seguimiento

S03-W001 conserva la limitación histórica legacy-unverifiable: TRX reales prueban resultados RED/GREEN y comportamiento actual, pero no todo el orden test-first anterior. Está convertido en trabajo programado en [follow-up.md](follow-up.md) y registrado en openspec/memory/known-issues.md. El siguiente slice capturará procedencia verificable desde su primera unidad o documentará la limitación del host. No se reconstruyen recibos ausentes. Esta disposición satisface el cierre con advertencias.

## Especificaciones preparadas

| Dominio | Preparación | Requisitos |
|---|---|---|
| contrato-sesion-sintetica | Nueva baseline | 2 |
| proyeccion-sesiones-idempotente | Nueva baseline | 3 |
| detalle-sesion-ambito | Nueva baseline | 2 |
| bandeja-ingestion-durable | Transición procesada añadida | 3 conservados + 1 nuevo |

Las cuatro preparaciones viven en archive-prepared/specs/. La bandeja conserva también las aclaraciones S02. Genesis de propuesta, diseño, deltas y ADR permanecen intactos. El plan contiene SHA-256 de bytes reales, target_before_sha256 y null para dominios nuevos; el runtime coteja el fingerprint de bandeja con state.yaml.

## Promociones ADR propuestas

El runtime permite docs/adr/, ubicación ya usada por promociones S01/S02. El índice de foundation permanece en docs/architecture/decisions/. Nombres únicos permitidos por schema; sin modificar genesis ni harness. Se promueven los contenidos originales sin cambiar su status histórico proposed.

- [decisions/adr-001.md](decisions/adr-001.md) → [docs/adr/adr-20260930-001-proyeccion-y-marcado-atomicos-por-identidad-de-evento.md](../../../docs/adr/adr-20260930-001-proyeccion-y-marcado-atomicos-por-identidad-de-evento.md).
- [decisions/adr-002.md](decisions/adr-002.md) → [docs/adr/adr-20260930-002-pasadas-paginadas-sin-bloqueo-por-invalidos.md](../../../docs/adr/adr-20260930-002-pasadas-paginadas-sin-bloqueo-por-invalidos.md).
- [decisions/adr-003.md](decisions/adr-003.md) → [docs/adr/adr-20260930-003-detalle-por-ambito-confiable-separado-de-ingestion.md](../../../docs/adr/adr-20260930-003-detalle-por-ambito-confiable-separado-de-ingestion.md).

## Inventario y reversión

El inventario contiene todos los archivos del origen excepto archive-plan.json: el runtime lo excluye de identidad para evitar hash autorreferente y lo copia igualmente. Conserva state, approvals, assumptions, progreso, seguimiento, evidencia, ADR y preparaciones. Los TRX crudos siguen como salidas locales ignoradas: el archivo conserva sus resúmenes/huellas, no afirma incluirlos.

Rollback staging-rename, propiedad del runtime. Reversión funcional conserva tablas, sesiones y marcas; sin Down ni resets.

La fecha prevista requiere runArchiveTransaction con now cuyo día UTC sea 2026-09-30; la CLI no tiene flag de fecha. El coordinador usará esa API con now=2026-09-30T00:00:00Z solo para el prefijo; recibos conservan timestamps reales. El destino previsto no afirma movimiento completado.

## Cost

Estimated token cost per phase, aggregated from .ospec/session/s03-proyeccion-idempotente/phase-costs.jsonl. All token values are estimated reporting fields, not exact metering.

| Phase | Invocations | Re-launches | Duration | Model Tiers | Statuses | Estimated Prompt Tokens | Estimated Artifact Tokens | Estimated Tool Output Tokens | Estimated Output Tokens |
|---|---:|---:|---|---|---|---:|---:|---:|---:|
| explore | 1 | 0 | 0ms | unknown | unknown | 80957 (estimated) | 0 (estimated) | 0 (estimated) | 21 (estimated) |
| propose | 2 | 1 | 0ms | unknown | success, blocked | 175940 (estimated) | 0 (estimated) | 0 (estimated) | 200 (estimated) |
| design | 1 | 0 | 0ms | unknown | success | 99472 (estimated) | 0 (estimated) | 0 (estimated) | 151 (estimated) |
| verify | 2 | 1 | 0ms | unknown | success, blocked | 260603 (estimated) | 0 (estimated) | 0 (estimated) | 198 (estimated) |

Telemetría parcial: spec/tasks/apply/review/archive carecen de filas en esta lectura. Duración 0, artifact/tool tokens 0 y tier unknown registrados con presencia ausente no acreditan tiempo ni consumo nulos. Los estados históricos de filas no sustituyen el estado canónico.

**Total user questions asked**: 0 (gates.*.questions_asked ausentes → 0 contractual; no es recuento inferido de approvals).
