# Alertas de la tubería de monitorización

Cada alerta de `deploy/observability/alerts.rules.json` (formato de reglas de Prometheus; YAML 1.2 admite JSON) enlaza aquí por el nombre de la alerta en minúsculas. Las métricas salen del medidor `Monitoring.Pipeline`, `Monitoring.Ingestion`, `Monitoring.Probes` y de la sonda; ninguna incluye credenciales, direcciones ni contenido de eventos.

**Responsables:** `operaciones-red` (sondas y sedes), `plataforma` (API, base de datos, conectores, búsqueda), `seguridad` (identidad y PKI). **Severidad:** `page` exige respuesta inmediata; `ticket`, el siguiente día laborable.

**No cubierto todavía:** profundidad de la DLQ de Kafka, lag del consumidor del sink y salud de Elasticsearch requieren exportadores (Kafka/Connect/Elasticsearch) que el stack interno aún no despliega; no se han escrito reglas sobre métricas cuyo nombre no se ha verificado. Las reglas tampoco se han ejecutado contra un Prometheus real.

## ProbeSpoolOldest

- **Severidad / responsable / etapa:** ticket / operaciones-red / probe
- **Condición:** `monitoring_probe_spool_oldest_seconds > 900` durante 5m
- **Qué significa:** La sonda conserva eventos sin entregar desde hace más de 15 min.
- **Causa probable:** La API no es alcanzable, rechaza la identidad de la sonda o la red entre sonda y API está degradada.
- **Qué hacer:** Comprobar conectividad y causas de rechazo de identidad (ProbeAuthRejected); la sonda reintenta sola y conserva el spool.

## ProbeSpoolLoss

- **Severidad / responsable / etapa:** page / operaciones-red / probe
- **Condición:** `increase(monitoring_probe_spool_loss_total[15m]) > 0` durante 0m
- **Qué significa:** La sonda ha perdido eventos del spool.
- **Causa probable:** El spool alcanzó su límite o se aisló por corrupción.
- **Qué hacer:** Restaurar espacio o conectividad y reconciliar la ventana afectada; registrar la pérdida en el expediente.

## ProbeCaptureDrops

- **Severidad / responsable / etapa:** ticket / operaciones-red / probe
- **Condición:** `increase(monitoring_probe_capture_drops_total[5m]) > 0` durante 5m
- **Qué significa:** El kernel descarta paquetes antes de que la sonda los lea.
- **Causa probable:** La interfaz recibe más tráfico del que la captura procesa.
- **Qué hacer:** Revisar carga de CPU y tamaño del búfer de captura; la cobertura de ese intervalo es incompleta.

## ProbeSpoolIsolated

- **Severidad / responsable / etapa:** ticket / operaciones-red / probe
- **Condición:** `monitoring_probe_spool_isolated > 0` durante 15m
- **Qué significa:** La sonda aisló eventos del spool que no puede enviar.
- **Causa probable:** Eventos que la API rechaza de forma permanente (contrato inválido) o registros corruptos del spool.
- **Qué hacer:** Inspeccionar los eventos aislados sin exponer su contenido y corregir el productor; no se reenvían solos.

## ProbeCaptureDegraded

- **Severidad / responsable / etapa:** page / operaciones-red / probe
- **Condición:** `monitoring_probe_capture_degraded > 0` durante 5m
- **Qué significa:** La captura está degradada o detenida.
- **Causa probable:** tshark terminó, la interfaz desapareció o faltan permisos de captura.
- **Qué hacer:** Revisar el servicio de la sonda y los permisos; hasta restablecerla no hay observación de esa sede.

## ProbeIdentitySuspended

- **Severidad / responsable / etapa:** page / seguridad / probe
- **Condición:** `monitoring_probe_identity_suspended > 0` durante 5m
- **Qué significa:** La identidad de la sonda está suspendida.
- **Causa probable:** El certificado fue revocado, desactivado o caducó.
- **Qué hacer:** Emitir y registrar un certificado nuevo (--probes register) y desactivar el anterior.

## ProbeAuthRejected

- **Severidad / responsable / etapa:** ticket / seguridad / ingestion
- **Condición:** `sum(rate(monitoring_probe_auth_rejections_total[5m])) by (cause) > 0.1` durante 10m
- **Qué significa:** La API rechaza autenticaciones de sonda de forma sostenida.
- **Causa probable:** Certificado caducado, revocado o no registrado; CRL no fresca (revocation-unknown) o intento ajeno.
- **Qué hacer:** Distinguir por la etiqueta cause; con revocation-unknown comprobar la publicación de la CRL.

## IngestionRejectionsSustained

- **Severidad / responsable / etapa:** ticket / operaciones-red / ingestion
- **Condición:** `sum(rate(monitoring_ingestion_rejections_total[5m])) > 1` durante 10m
- **Qué significa:** La ingestión rechaza lotes de forma sostenida.
- **Causa probable:** Cuota por origen superada, lotes inválidos o conflictos de reenvío.
- **Qué hacer:** Identificar el origen por sus etiquetas y revisar su configuración; la cuota es 6000 eventos/min por origen.

## ProjectionStalled

- **Severidad / responsable / etapa:** page / plataforma / projection
- **Condición:** `monitoring_pipeline_projection_oldest_pending_seconds > 300` durante 5m
- **Qué significa:** Hay eventos aceptados sin proyectar desde hace más de 5 min.
- **Causa probable:** El worker de proyección está detenido, bloqueado por un bloqueo de la base de datos o la base de datos no responde.
- **Qué hacer:** Revisar el servicio y el estado de PostgreSQL; el worker retoma por sí solo los pendientes.

## QuarantineUnresolved

- **Severidad / responsable / etapa:** ticket / plataforma / projection
- **Condición:** `monitoring_ingestion_quarantine_unresolved > 0` durante 15m
- **Qué significa:** Hay eventos en cuarentena sin resolver.
- **Causa probable:** Contrato inválido, identidad suprimida o conflicto de proyección (etiqueta cause).
- **Qué hacer:** Resolver con --quarantine (descartar o reemplazar con actor y motivo).

## QuarantineAging

- **Severidad / responsable / etapa:** ticket / plataforma / projection
- **Condición:** `monitoring_ingestion_quarantine_oldest_unresolved_seconds > 86400` durante 0m
- **Qué significa:** Un evento lleva más de 24 h en cuarentena.
- **Causa probable:** Nadie ha resuelto la cuarentena.
- **Qué hacer:** Resolver o descartar con motivo; no dejar envejecer el balance contable.

## CdcSlotInactive

- **Severidad / responsable / etapa:** page / plataforma / cdc
- **Condición:** `monitoring_pipeline_wal_slot_active == 0` durante 10m
- **Qué significa:** Un slot de replicación lleva 10 min sin consumidor.
- **Causa probable:** Kafka Connect/Debezium está detenido o no conecta; el WAL se acumula.
- **Qué hacer:** Restablecer el conector; si no se recupera, el slot retiene WAL hasta llenar el disco.

## CdcConfirmLag

- **Severidad / responsable / etapa:** page / plataforma / cdc
- **Condición:** `monitoring_pipeline_wal_slot_confirm_lag_bytes > 1073741824` durante 10m
- **Qué significa:** Debezium va más de 1 GiB por detrás del WAL.
- **Causa probable:** El conector no da abasto o Kafka no acepta escrituras.
- **Qué hacer:** Revisar el conector y Kafka; escalar o corregir antes de que se agote el disco.

## WalRetentionPressure

- **Severidad / responsable / etapa:** page / plataforma / cdc
- **Condición:** `monitoring_pipeline_wal_slot_retained_bytes > 5368709120` durante 10m
- **Qué significa:** Un slot retiene más de 5 GiB de WAL.
- **Causa probable:** Un consumidor lento o detenido impide reciclar el WAL.
- **Qué hacer:** Recuperar el consumidor; como último recurso eliminar el slot y reconstruir la proyección (--rebuild-search).

## SearchFreshnessLag

- **Severidad / responsable / etapa:** ticket / plataforma / search
- **Condición:** `monitoring_search_freshness_lag_seconds > 300` durante 10m
- **Qué significa:** El índice de búsqueda va más de 5 min por detrás de la autoridad.
- **Causa probable:** El sink de Elasticsearch está lento, detenido o rechaza documentos.
- **Qué hacer:** Revisar el conector sink y su DLQ; la API marca las respuestas como retrasadas.

## SearchNotCurrent

- **Severidad / responsable / etapa:** ticket / plataforma / search
- **Condición:** `monitoring_search_freshness_state > 0` durante 15m
- **Qué significa:** La búsqueda no está al día (Lagging o Recovering).
- **Causa probable:** Reconciliación pendiente, reconstrucción en curso o índice inalcanzable.
- **Qué hacer:** Comprobar el estado de la reconstrucción y de Elasticsearch.

## PipelineMetricsMissing

- **Severidad / responsable / etapa:** page / plataforma / projection
- **Condición:** `absent(monitoring_pipeline_wal_slot_active) and on() absent(monitoring_pipeline_projection_oldest_pending_seconds)` durante 10m
- **Qué significa:** La API dejó de publicar las métricas de la tubería.
- **Causa probable:** La API está caída o la base de datos no es alcanzable: sin métricas no se detectaría ningún otro fallo.
- **Qué hacer:** Restablecer la API y la base de datos antes de interpretar las demás alertas.

## IngestionMetricsMissing

- **Severidad / responsable / etapa:** page / plataforma / projection
- **Condición:** `absent(monitoring_ingestion_pending_events)` durante 10m
- **Qué significa:** La API dejó de publicar las métricas de la tubería.
- **Causa probable:** La API está caída o la base de datos no es alcanzable: sin métricas no se detectaría ningún otro fallo.
- **Qué hacer:** Restablecer la API y la base de datos antes de interpretar las demás alertas.

## RetentionStalled

- **Severidad / responsable / etapa:** page / plataforma / retention
- **Condición:** `monitoring_retention_seconds_since_last_run > 10800` durante 0m
- **Qué significa:** La retención no completa un ciclo desde hace más de 3 h.
- **Causa probable:** El worker de retención falla (bloqueo de publicación mantenido por una reconstrucción, base de datos saturada) o se detuvo.
- **Qué hacer:** Revisar el registro del worker y los bloqueos; los datos de tráfico caducados siguen almacenados mientras no se corrija.

## RetentionNeverRan

- **Severidad / responsable / etapa:** page / plataforma / retention
- **Condición:** `absent(monitoring_retention_seconds_since_last_run)` durante 3h
- **Qué significa:** La retención no ha completado ningún ciclo desde que arrancó la API.
- **Causa probable:** La retención falla en cada intento o la API se reinicia antes de terminar.
- **Qué hacer:** Revisar el registro del worker (tipo de fallo) y la base de datos; sin retención se incumple la promesa de borrado.
