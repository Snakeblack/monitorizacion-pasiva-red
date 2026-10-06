# Continuidad, copias y recuperación

Estado de esta guía: **el ensayo de recuperación punto-en-el-tiempo está probado en un solo nodo PostgreSQL local** (`scripts/lab/pitr-rehearsal.sh`). La alta disponibilidad con réplica en caliente, la promoción automática, la recuperación de Kafka/Connect/Elasticsearch con datos reales y los tiempos (RPO/RTO) en hardware de producción **no se han ensayado**. Los objetivos de abajo son propuestas pendientes de ratificar.

## Qué es la fuente de verdad y qué se reconstruye

| Componente | Papel | Si se pierde |
|---|---|---|
| PostgreSQL (`monitoring`) | **Autoridad**: bandeja, sesiones, identidades/tombstones, publicación (outbox), inventario, registro de sondas, auditorías | Recuperar de copia + WAL archivado; es lo único que no se reconstruye |
| Kafka (tópicos de publicación, DLQ) | Transporte; se reconstruye desde la autoridad | Reconstruir el índice (`--rebuild-search`); no hay datos únicos |
| Kafka Connect / Debezium | Lee el WAL (slot `monitoring_outbox`) | Recrear los conectores; un slot perdido no se puede reanudar: reconstruir el índice |
| Elasticsearch | Proyección de consulta | `--rebuild-search`; hasta entonces la API marca frescura `Recovering` |
| Anillo de claves de Data Protection (`DataProtection:KeysPath`) | Cursores sellados | Sin él los cursores previos caducan como mal formados (400); se emiten nuevos. Copiarlo con la configuración |
| Keycloak / EJBCA | Identidad humana y PKI | Respaldos propios de cada servicio; **no ensayado**. Perder la CA obliga a emitir y registrar certificados nuevos (`--probes register`) |
| Sondas (SQLite WAL spool) | Entregan con reintentos | Retienen lo no entregado; lo ya confirmado por la API y perdido por un restore **no se recupera** (ver RPO) |

## Objetivos propuestos (sin ratificar)

- **RPO** de la autoridad: ≤ 5 min con archivado continuo de WAL (`archive_timeout`); con réplica síncrona, 0. Los eventos confirmados (ACK) a una sonda y posteriores al último WAL recuperable se pierden: se deben conciliar contra la sonda (su contador de entregados) tras la recuperación.
- **RTO**: ≤ 1 h con restauración desde copia base + WAL; ≤ 15 min con promoción de una réplica (sin ensayar).

## Copias

1. Copia base periódica: `pg_basebackup -D <destino> -Fp -X fetch -c fast` (o la herramienta de copias del operador), almacenada **fuera del nodo**.
2. Archivado continuo: `archive_mode=on` y `archive_command` a almacenamiento externo; vigilar que no se acumule WAL local (alertas `WalRetentionPressure`, `CdcSlotInactive`).
3. Verificar cada copia (`pg_verifybackup`) y ensayar la restauración con el script antes de confiar en ella.
4. Las copias contienen datos de tráfico: **se rigen por la misma retención** que la base. Una copia más antigua que `SessionRetention + TombstoneMargin` no debe conservarse.

## Recuperación punto-en-el-tiempo (ensayada en un nodo)

1. Parar la API, los conectores y las sondas deberán reintentar (el spool las protege).
2. Restaurar la copia base en un directorio nuevo y configurar `restore_command`, `recovery_target_time` o `recovery_target_name` y `recovery_target_action='promote'`; crear `recovery.signal`; arrancar.
3. Comprobar que la recuperación terminó (`pg_is_in_recovery()` = falso) y los recuentos esperados.
4. `dotnet Monitoring.Host.dll --migrate` (idempotente).
5. **`--restore-finalize`** (obligatorio antes de reabrir nada): reaplica la retención hasta el final, de modo que la historia vencida que la restauración trae de vuelta pasa a tombstones y deja de servirse y publicarse, y, si hay búsqueda configurada, reconstruye el índice desde la autoridad (descartando cualquier documento que la autoridad restaurada ya no tenga).
6. Recrear los conectores Debezium/sink (el slot no viene en la copia) y reabrir la ingestión; vigilar `ProjectionStalled`, `CdcSlotInactive` y la frescura.
7. Conciliar con las sondas la ventana entre el último WAL recuperable y la caída.

## Fallo de nodo / alta disponibilidad (sin ensayar)

Se requiere una réplica con streaming y slots de replicación sincronizados (en PostgreSQL 18, slots lógicos con `failover=true` y `sync_replication_slots`) para que el conector sobreviva a la promoción; sin ello, tras promover hay que seguir el procedimiento de recuperación desde el paso 4 (finalize + reconstrucción + conectores nuevos). No debe haber dos primarias: usar un mecanismo de fencing del operador (el cierre de la antigua es previo a la promoción). La API es sin estado salvo el anillo de claves compartido, por lo que escala y se reemplaza libremente.

## Qué NO hacer

- No reabrir ingestión ni conectores tras una restauración sin ejecutar `--restore-finalize`.
- No restaurar sobre el nodo en servicio; restaurar siempre en un directorio nuevo y conmutar.
- No conservar copias fuera de la ventana de retención.
