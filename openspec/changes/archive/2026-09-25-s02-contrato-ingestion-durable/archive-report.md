# Archive report: s02-contrato-ingestion-durable

**Fecha:** 2026-09-25  
**Ruta:** estándar por degradación (config.yaml no declara outing:; el estado no persiste oute: en esta modalidad).  
**Destino propuesto:** openspec/changes/archive/2026-09-25-s02-contrato-ingestion-durable/

## Resultado de cierre

La verificación tiene veredicto **PASS WITH WARNINGS** y la puerta requerida de pruebas está en pass. El hallazgo crítico C-01 está cerrado en la lineage de verificación generación 2 (ll-findings-verified). No quedan hallazgos críticos abiertos.

El warning W-01 documenta que la prueba de pérdida de respuesta de la tarea 3.4 pasó en su primera ejecución y no tiene evidencia RED histórica. Se acepta como seguimiento de proceso, no como fallo funcional: el trabajo de mejora está registrado en [GitHub issue #18](https://github.com/Snakeblack/monitorizacion-pasiva-red/issues/18). No se fabricará evidencia retroactiva.

## Specs preparadas

Los dos dominios son nuevos; no hay archivos base en openspec/specs/ para ellos. Se propone crear los destinos usando íntegro el contenido verificado de cada delta, sin eliminar ni reemplazar requisitos existentes:

| Dominio | Acción | Requisitos |
|---|---|---|
| contrato-ingestion-v1 | Crear | 2 requisitos normativos y sus escenarios |
| andeja-ingestion-durable | Crear | 3 requisitos normativos y sus escenarios |

Los SHA-256 del contenido preparado y 	arget_before_sha256: null se incluyen en rchive-plan.json; el runtime comprobará de nuevo la ausencia de los destinos.

## ADR propuestas

Se proponen tres promociones, manteniendo los originales change-locales como registro de auditoría:

- decisions/adr-001.md → docs/adr/adr-20260925-001-bandeja-por-evento-y-comparacion-json-estructural.md
- decisions/adr-002.md → docs/adr/adr-20260925-002-admision-y-cuota-movil-en-una-transaccion-por-origen.md
- decisions/adr-003.md → docs/adr/adr-20260925-003-ambito-de-sonda-provisto-por-el-host.md

Los hashes de cada fuente están en rchive-plan.json; el runtime comprobará colisiones y aplicará las promociones.

## Artefactos de cierre

- proposal.md ✅
- specs/ ✅ (dos deltas estándar)
- design.md ✅
- 	asks.md ✅ (15/15 tareas marcadas completas)
- pply-progress.md ✅
- erify-report.md ✅ (PASS WITH WARNINGS; C-01 cerrado; W-01 ligado al issue #18)
- state.yaml ✅
- decisions/ ✅ (tres ADR propuestas)

El inventario completo de los archivos presentes en el cambio, incluidas evidencias de lineage, se fija en rchive-plan.json para que el runtime verifique sus bytes.

## Cost

No se registraron datos de coste por fase (.ospec/session/s02-contrato-ingestion-durable/phase-costs.jsonl ausente o vacío).

**Total user questions asked**: 5