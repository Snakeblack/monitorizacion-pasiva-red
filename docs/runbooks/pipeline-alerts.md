# Alertas de la tubería de monitorización

Cada alerta de `deploy/observability/alerts.rules.json` (formato de reglas de Prometheus; YAML 1.2 admite JSON) enlaza aquí por el nombre de la alerta en minúsculas. Las métricas salen del medidor `Monitoring.Pipeline`, `Monitoring.Ingestion`, `Monitoring.Probes` y de la sonda; ninguna incluye credenciales, direcciones ni contenido de eventos.

**Responsables:** `operaciones-red` (sondas y sedes), `plataforma` (API, base de datos, conectores, búsqueda), `seguridad` (identidad y PKI). **Severidad:** `page` exige respuesta inmediata; `ticket`, el siguiente día laborable.

**Origen de las métricas:** las de `monitoring_*` las empuja la API por OTLP ([ADR-021](../architecture/decisions/ADR-021.md)); las de `kafka_*`, `kafka_connect_*` y `elasticsearch_*` salen de los exportadores del perfil `observability` ([ADR-020](../architecture/decisions/ADR-020.md)). Los nombres se verificaron en un Prometheus real del laboratorio; los umbrales siguen siendo provisionales hasta S17. **Sin cobertura todavía:** las métricas de la sonda (`monitoring_probe_*`) las publica el proceso de la sonda, que aún no las exporta.

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

## SinkDeadLetterGrowing

- **Severidad / responsable / etapa:** page / plataforma / search
- **Condición:** `increase(kafka_topic_partition_current_offset{topic="monitoring.sessions.dlq"}[15m]) > 0` durante 0m
- **Qué significa:** El sink de Elasticsearch ha desviado mensajes a la cola de errores: esas sesiones no están en el índice.
- **Causa probable:** Un mensaje no cumple el contrato, Elasticsearch lo rechaza o la transformación de validación falla.
- **Qué hacer:** Inspeccionar los mensajes de monitoring.sessions.dlq (cabeceras de error) y corregir la causa; reconstruir el índice desde PostgreSQL cuando se resuelva (--rebuild-search).

## SinkConsumerLag

- **Severidad / responsable / etapa:** ticket / plataforma / search
- **Condición:** `sum(kafka_consumergroup_lag{consumergroup="connect-monitoring-sink"}) > 1000` durante 10m
- **Qué significa:** El sink de Elasticsearch acumula más de 1000 mensajes sin consumir.
- **Causa probable:** Connect o Elasticsearch van más lentos que la publicación, o el sink está detenido.
- **Qué hacer:** Comprobar ConnectTaskNotRunning y la salud de Elasticsearch; si es carga sostenida, medir en S17 antes de cambiar el umbral.

## ConnectTaskNotRunning

- **Severidad / responsable / etapa:** page / plataforma / cdc
- **Condición:** `kafka_connect_task_status{status!="running"} == 1` durante 2m
- **Qué significa:** Una tarea de Kafka Connect (Debezium o sink) no está en ejecución.
- **Causa probable:** La tarea falló (credenciales, conexión a PostgreSQL o Elasticsearch, contrato) o fue pausada.
- **Qué hacer:** Ver el estado y la traza del conector en la API REST de Connect y reiniciar la tarea; el WAL se retiene mientras tanto (WalRetentionPressure).

## SearchEngineRed

- **Severidad / responsable / etapa:** page / plataforma / search
- **Condición:** `elasticsearch_cluster_health_status{color="red"} == 1` durante 2m
- **Qué significa:** Elasticsearch está en rojo: hay índices sin shards primarios asignados.
- **Causa probable:** Un nodo cayó o el disco se llenó.
- **Qué hacer:** Restaurar el nodo o el espacio; si el índice se perdió, reconstruirlo desde PostgreSQL (--rebuild-search).

## SearchEngineYellow

- **Severidad / responsable / etapa:** ticket / plataforma / search
- **Condición:** `elasticsearch_cluster_health_status{color="yellow"} == 1` durante 15m
- **Qué significa:** Elasticsearch está en amarillo: faltan réplicas.
- **Causa probable:** Un nodo de réplica no está disponible o no hay capacidad para asignarlas.
- **Qué hacer:** Recuperar el nodo o la capacidad; las consultas siguen sirviéndose sin redundancia.

## SearchEngineUnreachable

- **Severidad / responsable / etapa:** page / plataforma / search
- **Condición:** `elasticsearch_exporter_build_info and on() absent(elasticsearch_cluster_health_status)` durante 2m
- **Qué significa:** El exportador de Elasticsearch está vivo pero Elasticsearch no responde.
- **Causa probable:** Elasticsearch está detenido, sin red o sin memoria; las búsquedas fallan y el sink acumula lag.
- **Qué hacer:** Restaurar Elasticsearch; la API responde 503 a las búsquedas mientras tanto. Después comprobar SinkConsumerLag y SinkDeadLetterGrowing.

## PipelineExportersDown

- **Severidad / responsable / etapa:** ticket / plataforma / search
- **Condición:** `up{job=~"kafka|elasticsearch|connect"} == 0` durante 5m
- **Qué significa:** Un exportador de Kafka, Connect o Elasticsearch no responde.
- **Causa probable:** El exportador o el servicio al que consulta están caídos, o la red entre ambos falló.
- **Qué hacer:** Comprobar el contenedor del exportador y del servicio; mientras tanto no hay señal de DLQ, lag ni salud.

## PipelineExportersMissing

- **Severidad / responsable / etapa:** ticket / plataforma / search
- **Condición:** `absent(kafka_brokers) or absent(elasticsearch_cluster_health_status) or absent(kafka_connect_task_status)` durante 10m
- **Qué significa:** Falta alguna de las métricas de Kafka, Connect o Elasticsearch.
- **Causa probable:** El exportador no está desplegado o la plataforma no lo recolecta.
- **Qué hacer:** Desplegar el perfil observability y añadir sus tres destinos a la recolección; sin ellos las reglas de la DLQ, del lag y de Elasticsearch no pueden disparar.
