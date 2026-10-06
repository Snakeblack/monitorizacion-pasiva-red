# Tasks: producto de monitorización pasiva completo

## Spec/Design Reconciliation

| Requisitos | Prioridad | Asignación del diseño | Estado y evidencia de cierre |
|---|---|---|---|
| REQ-base-ejecutable-002–003, REQ-verificacion-base-001–003 | MUST | Modelo/migraciones, arranque y CI; `src/Monitoring.{Domain,Persistence,Host}`, `tests/`, `.github/workflows/`, `docs/`, `openspec/config.yaml` | covered-by-design; migración desde PostgreSQL vacío, suites documentadas y pipeline falla ante fallo/no ejecución. |
| REQ-sesiones-canonicas-001–003, REQ-proyeccion-sesiones-idempotente-001–003, REQ-bandeja-ingestion-durable-003–005 | MUST | Contratos independientes, proyección transaccional, marcador/outbox, cuotas y cuarentena en Domain/Persistence/Host; escenarios de duplicado, rollback, restart, inválido y válido posterior | covered-by-design; tests de dominio y PostgreSQL de commit/replay/recuperación. |
| REQ-pipeline-busqueda-001–004, REQ-consulta-sesiones-001–004 | MUST | Outbox→Debezium→Kafka→Connect sink→índice versionado; API de consulta con PIT/search_after y reconciliación | covered-by-design; stack real de prueba cubre commit, sink caído, lag, tombstone, rebuild, filtros y cursor. |
| REQ-detalle-sesion-ambito-001–003, REQ-vista-detalle-sesion-001,003–006 | MUST | Autorización API, listado/detalle Angular y pruebas de contrato/vertical | covered-by-design; vertical real lista y abre detalle; cubre vacío/error/403/401, filtros, reset cursor y teclado. |
| REQ-captura-y-entrega-sonda-001–004, REQ-inventario-dispositivos-001–004, REQ-identidad-y-acceso-001–004, REQ-transporte-y-pki-001–004 | MUST | Adaptador tshark y proceso de sonda aislado con SQLite WAL; servicios de inventario, Keycloak y EJBCA/TLS | covered-by-design; tests focales de aislamiento/correlación/spool, auditoría/concurrencia, permisos y laboratorio EJBCA real. |
| REQ-operacion-y-ciclo-datos-001–004, REQ-aptitud-y-liberacion-001–004 | MUST | Telemetría, reconciliación, retención/supresión, continuidad/PITR, ensayo S17 y gate S18 | covered-by-design; runbooks y evidencia de ensayo completa. 72 h/volumen/coste son gates de aptitud final, nunca sustituibles por smoke ni inferidos. |

### Reconciliation Verdict
- MUST coverage: complete; los seis grupos de requisitos están asignados a componentes y a evidencia observable en `design.md` → “File Changes y asignación de escenarios” / “Testing Strategy y rollout finito”.
- SHOULD/MAY gaps: none identified.
- Ambiguities to track: la capacidad y los acuerdos corporativos son evidencia/gates pendientes de release, no bloquean el demo funcional.

## Review Workload Forecast

Decision needed before apply: No
Chained PRs recommended: Yes
Chain strategy: size-exception
400-line budget risk: High

| Campo | Estimación |
|---|---|
| Líneas cambiadas | 2.500–5.000 a lo largo del cambio; primer vertical 700–1.200. |
| Estrategia | `exception-ok`; decisión `size:exception` ya aceptada para entrega conjunta. Ejecutar en lotes/commits revisables, sin gate adicional antes de apply. |
| División sugerida | Lotes dependientes K01→K04→K02→K03→S09; luego S05–S08; S10–S13; S14–S16; S17–S18. |
| Motivo del riesgo | 15 dominios, servicios externos, migraciones, API/UI, CI y seguridad exceden 400 líneas. Lotes hacen cada avance comprobable; el umbrella conserva alcance completo. |

### Suggested Work Units

| Unidad | Resultado autónomo y propiedad de archivos | Dependencia y verificación |
|---|---|---|
| U01 / K01 | ADR sucesores, `docs/architecture/decisions/**`, `docs/architecture/technical-baseline.md`, `docs/development/slices.md`, `openspec/config.yaml`. | Inicio; documentos/config coherentes con decisión adoptada y comandos existentes. |
| U02 / K04 | `compose.yaml`, `scripts/**`, `docs/development/**`, workflow de stack reproducible. | U01; arranque desde vacío con servicios fijados, healthchecks y secretos externos; no evidencia de capacidad. |
| U03 / K02 | Dominio/modelo y migraciones PostgreSQL + outbox: `src/Monitoring.Domain/**`, `src/Monitoring.Persistence/**`, `tests/**`. | U01; RED prueba rollback/duplicado/reinicio antes del handler; GREEN verifica sesión, marcador y outbox atómicos. |
| U04 / K03 | Configuración Debezium/Kafka/Connect/Elasticsearch en `infra/**`/`compose.yaml`, contratos/proyección y tests de integración. | U02–U03; RED caída/replay/tombstone; GREEN demuestra sink, revisión autoritativa, lag y rebuild reproducible. |
| U05 / S09 | Consulta API y UI inicial: `src/Monitoring.Host/**`, `src/monitoring-web/**`, `tests/**`. | U04; RED límites/ámbito/filtro/cursor, UI error y stale request; GREEN vertical con fixture desde PostgreSQL hasta listado y detalle. Primer demo al terminar U01–U05. |
| U06 / S05–S08 | Sonda aislada/tshark, contratos, SQLite WAL/spool y endpoint de ingestión/cuarentena en áreas asignadas por diseño. | U03; RED correlación, reinicio, cuota, ACK y cuarentena; GREEN reenvío idempotente sin perder válidos. |
| U07 / S10–S13 | `src/**` inventario, autenticación/autorización, TLS, integración Keycloak/EJBCA; `docs/runbooks/**`. | U06; RED permisos/ámbito/auditoría/certificados revocados; GREEN laboratorio con CA/servicios reales. |
| U08 / S14–S16 | Métricas/alertas, retención y tombstones, HA/PITR/runbooks en `src/**`, `infra/**`, `docs/runbooks/**`. | U04, U06–U07; RED fallos, caducidad y restore sin resurrección; GREEN reconciliación y recuperación documentadas/ensayables. |
| U09 / S17–S18 | Harness finito, CI y expediente de liberación en `tests/**`, `.github/workflows/**`, `docs/**`. | U01–U08; RED gate cuando falte evidencia/aceptación; GREEN solo con ensayo ADR-013 completo y autorizaciones registradas. Mantener como gate final. |

Cada unidad es un límite sugerido de commit/trabajo; workers solo se despachan cuando sus dependencias y propiedad están claras. No dividir ni sobrescribir `apply-progress.md`: runtime/orquestador coordina su merge.

## Fases de implementación

### Fase 1 — Base y camino demostrable (orden obligatorio)

- [x] 1.1 K01: documentar decisiones sucesoras de ADR-010/014 y contratos/dependencias adoptados; actualizar baseline, slices, config y comandos sin reescribir S01–S04. Verificar referencias cruzadas y consistencia documental. [REQ-base-ejecutable-003, REQ-verificacion-base-003]
- [~] 1.2 K04: crear entorno finito reproducible con PostgreSQL, Kafka, Connect, Elasticsearch y servicios de demo; healthchecks, versiones fijadas y secretos por entorno. RED arranque vacío/healthcheck fallido → GREEN preparación limpia, migración y apagado reproducibles. [REQ-aptitud-y-liberacion-001, REQ-base-ejecutable-002]
- [x] 1.3 K02: RED tests de proyección duplicada/concurrente, origen distinto, rollback y reinicio en `tests/`; GREEN transacción PostgreSQL atómica sesión+marcador+outbox e inicialización aditiva repetible, preservando fixtures S01–S04. [REQ-sesiones-canonicas-002, REQ-proyeccion-sesiones-idempotente-001–003, REQ-base-ejecutable-002]
- [~] 1.4 K03 (identidad compacta resuelta 2026-10-06; frescura, reconciliación, supresión y rebuild pendientes): RED integración de sink caído, contrato inválido, lag, tombstone y rebuild interrumpido; GREEN configurar Debezium/Kafka/Connect/sink versionado, barreras persistentes de revisión/supresión y reconciliación contra autoridad. [REQ-pipeline-busqueda-001–004]
- [~] 1.5 S09/API (validación, ámbito, cursor protegido, leases, adaptador y vigencia PostgreSQL implementados contra un Elasticsearch simulado; falta ejecutarlo contra el motor real y autorización por permisos reales S12): RED pruebas de `[from,to)`, IPv6 normalizada, filtros/límites, autorización por operación, PIT/cursor vencido/reutilizado y respuesta acotada; GREEN endpoint con Elasticsearch, verificación autoritativa y errores diferenciados. [REQ-sesiones-canonicas-003, REQ-consulta-sesiones-001–004, REQ-detalle-sesion-ambito-001–003]
- [~] 1.6 UI/demo: RED tests Angular de última-24h, filtros, cambio/reset cursor, página ≤100, selección stale, vacío/error/401/403/410/lag y teclado; GREEN listado→detalle autenticado vía API real mostrando cinco campos. No añadir exportación ni búsqueda libre. [REQ-vista-detalle-sesion-001,003–006]
- [ ] 1.7 CI vertical: RED pipeline ante suite/migración fallida o no ejecutada; GREEN checkout limpio .NET/Angular + stack desechable pasando fixture→autoridad/outbox→Kafka→índice→API→UI. El demo no espera pruebas de capacidad/producto final. [REQ-verificacion-base-001–002, REQ-vista-detalle-sesion-005]

### Fase 2 — Captura y entrega durable

- [ ] 2.1 S05–S06: RED fixtures de extracción aislada, contrato versionado inválido, correlación y límites; GREEN adaptador tshark y sesiones inferidas sin bloquear captura. [REQ-captura-y-entrega-sonda-001–002, REQ-sesiones-canonicas-001]
- [ ] 2.2 S07: RED spool lleno/restart/red caída/reenvío duplicado; GREEN SQLite WAL transaccional, reintento con identidad estable, cuotas configurables y pérdida visible. [REQ-captura-y-entrega-sonda-003–004]
- [ ] 2.3 S08: RED falta cuota, ingestión duplicada, evento inválido seguido de válido y fallo entre proyección/ACK; GREEN cuotas por origen, cuarentena conciliable y ACK solo tras commit durable, sin bloqueo de válidos. [REQ-bandeja-ingestion-durable-003–005, REQ-proyeccion-sesiones-idempotente-003]

### Fase 3 — Inventario, identidad y PKI

- [ ] 3.1 S10–S11: RED observación repetida/sin MAC, cambio de IP/ámbito, conflicto concurrente y auditoría obligatoria; GREEN candidatos versionados e inventario confirmado solo manualmente. [REQ-inventario-dispositivos-001–004]
- [ ] 3.2 S12: RED token inválido, expirado, rol/cruce de ámbito y cierre de sesión; GREEN OIDC Keycloak validado, matriz de roles y denegación sin datos residuales. [REQ-identidad-y-acceso-001–004]
- [ ] 3.3 S13: RED CA/nombre inválidos, downgrade, certificado vencido/revocado y permisos backend excesivos; GREEN TLS validado/mTLS ligado a sonda, rotación/renovación ensayada con EJBCA real y credenciales mínimas. [REQ-transporte-y-pki-001–004]

### Fase 4 — Operación y gates de aptitud/liberación

- [ ] 4.1 S14: RED cada etapa detenida/WAL/lag/reconciliación; GREEN métricas y alertas accionables con causa/responsable sin secretos. [REQ-operacion-y-ciclo-datos-001]
- [ ] 4.2 S15: RED caducidad con pendientes y replay anterior; GREEN retención y tombstones autoritativos en PostgreSQL/proyección sin resurrección. [REQ-operacion-y-ciclo-datos-002, REQ-pipeline-busqueda-004]
- [ ] 4.3 S16: RED fallo de nodo/restauración con histórico vencido; GREEN dependencias de continuidad, backup/PITR y reconciliación ensayados sin reintroducir expirados. [REQ-operacion-y-ciclo-datos-003–004]
- [ ] 4.4 S17: RED gate rechaza suite parcial, dataset corto o métricas no ejecutadas; GREEN ensayo finito ADR-013 con 30 días/72 h, carga, consultas, frescura, pérdida, continuidad y coste, guardando resultado/entorno. No extrapolar capacidad desde smoke. [REQ-aptitud-y-liberacion-002–003]
- [ ] 4.5 S18: RED paquete/gate bloqueado ante autorización/evidencia faltante o fallo; GREEN paquete privado y decisión de liberación solo con aceptaciones explícitas y expediente completo; fallos requieren corrección y reensayo. [REQ-aptitud-y-liberacion-004]

## Comandos de verificación

- Base .NET: `dotnet test Monitoring.slnx` (RED/GREEN focal xUnit/Testcontainers antes de cada cambio funcional).
- UI: `npm --prefix src/monitoring-web test` (RED/GREEN Angular por contrato/estado).
- Stack: comandos versionados en `docs/development/**`; crear almacenes desechables, nunca reutilizar estado de ejecución previa.
- Cierre vertical: job CI definido en U01–U05 desde checkout limpio. S17/S18 son gates separados y no se marcan aprobados hasta tener evidencia real y aprobaciones.
