# Propuesta: producto de monitorización pasiva completo

## Intent

Completar [F-01–F-09](../../../docs/product/functional-scope.md): sesiones consultables desde Angular. Integra [S05–S18](../../../docs/development/slices.md#s05) y K01–K04, conservando S01–S04 archivados.

## Scope

### In Scope

Captura/correlación, spool/reenvío, cuarentena, búsqueda/detalle, inventario auditado, Keycloak/OIDC/RBAC, EJBCA/TLS/mTLS, observabilidad, retención, HA/PITR y liberación. Conservar semántica, roles y límites F-01–F-09/[ADR-013](../../../docs/architecture/decisions/ADR-013.md); actualizar documentación/configuración como producto adoptado.

### Out of Scope

Búsqueda libre, exportación masiva, PCAP/payload persistentes y fusión automática.

## Capabilities

### New Capabilities

- `sesiones-canonicas`: captura versionada; adaptación sintética compatible.
- `captura-y-entrega-sonda`: extracción/correlación, spool/reintento/pérdida (S05–S07).
- `pipeline-busqueda`: outbox/CDC, replay/borrado/rebuild (K02–K03).
- `consulta-sesiones`: filtros/cursor/límites/frescura (S09).
- `inventario-dispositivos`: candidatos/edición/auditoría (S10–S11).
- `identidad-y-acceso`: Keycloak/permisos (S12).
- `transporte-y-pki`: EJBCA/certificados/TLS/mTLS (S13).
- `operacion-y-ciclo-datos`: telemetría/retención/HA/PITR (S14–S16).
- `aptitud-y-liberacion`: entorno/ensayos/paquete privado (K04/S17–S18).

### Modified Capabilities

- `bandeja-ingestion-durable`: cuarentena S08/cuotas dimensionadas; conservar ACK/idempotencia.
- `proyeccion-sesiones-idempotente`: sesión/marcado/outbox atómicos.
- `detalle-sesion-ambito`: lectura canónica/OIDC productiva.
- `vista-detalle-sesion`: listado/navegación/autenticación.
- `base-ejecutable`: PostgreSQL adoptado; dominio independiente.
- `verificacion-base`: CI/documentación operativa actualizadas.

## Approach

PostgreSQL autoritativo → outbox transaccional → Debezium/Connect → Kafka → Connect sink → Elasticsearch reemplazable. Clave `(siteId,sensorId,eventId)`; mappings explícitos, índices versionados y PIT/`search_after`. API autoriza ámbitos; UI distingue lag/error/vacío.

| Lote | Entrega y dependencias |
|---|---|
| K01/K04 | ADR sucesores ADR-010/014, contratos/dependencias y entorno reproducible. |
| K02/K03/S09 | Vertical fixture → sesión/outbox → Kafka → Elasticsearch → API → Angular; Development/Testing hasta S12. |
| S05–S08 | Captura/correlación → spool → cuarentena; incorporar al vertical. |
| S10–S13 | Candidatos → inventario → Keycloak → EJBCA. |
| S14–S18 | Señales → borrado → recuperación → ensayo → liberación. |

Prerrequisitos originales; software probado por lote; entrega conjunta `exception-ok`.

## Affected Areas

| Área | Cambio |
|---|---|
| `src/Monitoring.{Domain,Persistence,Host}/`, nueva sonda | Contratos/migraciones/API/captura. |
| `src/monitoring-web/`, `tests/`, `.github/workflows/`, nuevo `compose.yaml` | UI/ensayos/servicios. |
| `docs/`, `openspec/config.yaml` | Arquitectura/operación. |

## Risks

| Riesgo | Mitigación |
|---|---|
| Lag/WAL/divergencia/resurrección | Reconciliación/tombstones/rebuild/retención. |
| 500/min × 8 = 5,76 M eventos/día | Cuotas configurables por origen para volumen/ráfagas. |
| Plataforma/políticas pendientes | Preparación, responsables y evidencia. |

## Rollback Plan

Desactivar productores/conectores/acceso; conservar PostgreSQL/outbox/offsets. Reponer binario compatible; migraciones aditivas, reconstrucción y alias reconciliado, sin resucitar expirados ni habilitar acceso de desarrollo en producción.

## Dependencies

Docker funcional, `tshark`, servicios adoptados, fixtures autorizados y responsables/políticas para liberar.

## Success Criteria

- [ ] Strict TDD/CI .NET/Angular: vertical, filtros/cursor/aislamiento, duplicados, reinicio/replay, borrado/rebuild y certificados.
- [ ] S05–S16: evidencias F-01–F-09/G-01–G-09 y runbooks.
- [ ] S17: ensayo finito completo ADR-013, incluidos 30 días/72 h, volumen/consultas/frescura/pérdida/continuidad/coste; sin extrapolar capacidad.
- [ ] S18: liberar con resultados/aceptaciones; fallos exigen corrección/reensayo.

**Branch advisory:** Before `sdd-apply` begins, a feature branch SHOULD be created following the `<tipo>/<descripción>` convention defined in the `branch-pr` skill (e.g. `git checkout -b feat/my-change main`). This note is SHOULD, not MUST.
