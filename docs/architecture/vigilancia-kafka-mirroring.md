# Vigilancia tecnológica: replicación nativa entre clústeres Kafka

**Registro:** 24-09-2026. **Alcance:** opción futura; Kafka no forma parte del primer incremento según [ADR-010](decisions/ADR-010.md).

[KIP-1279](https://cwiki.apache.org/confluence/spaces/KAFKA/pages/406620973/KIP-1279+Cluster+Mirroring) está **aceptada como propuesta**, pero esa condición no acredita su disponibilidad en una versión publicada. Plantea que el broker de destino obtenga los registros del origen mediante Fetch API, conservando lotes comprimidos y offsets sin traducción. Conserva los ID de topics si el origen los ofrece; con orígenes anteriores a Kafka 2.8 se identifica el topic por nombre. La matriz propuesta permite origen desde 2.1 y destino 4.x. Antes de elegirla habrá que confirmar la versión concreta que la incorpora y sus límites en las notas de lanzamiento. La [versión 4.3](https://kafka.apache.org/blog/2026/05/22/apache-kafka-4.3.0-release-announcement/) se publicó antes de la aceptación de KIP-1279; su [documentación operativa](https://kafka.apache.org/43/operations/) sigue describiendo MirrorMaker 2.

El diseño inicial es **asíncrono**: el desfase de replicación implica un RPO potencialmente mayor que cero. [KIP-1360](https://cwiki.apache.org/confluence/spaces/KAFKA/pages/430409277/KIP-1360+Cluster+Synchronous+Mirroring) propone un modo síncrono, pero está en **borrador**. El alcance inicial tampoco cubre activo-activo ni tiered storage, y no permite asumir una migración transparente desde MirrorMaker 2. Por tanto, MirrorMaker no queda obsoleto por el estado actual de estas propuestas.

## Aplicación a este proyecto

Si [ADR-003](decisions/ADR-003.md) se reactiva por volumen, replay o consumidores independientes, comparar la replicación nativa con MirrorMaker 2 para recuperación entre clústeres y migraciones. Ninguna de las dos justifica introducir Kafka únicamente para este proyecto. La posible transición de un clúster antiguo con ZooKeeper a uno nuevo con KRaft mediante *nuevo clúster → espejo → corte controlado* es una **estrategia por validar**, no un procedimiento aprobado: comprobar soporte en versiones reales, contratos y consumidores, lag y RPO bajo carga, cambio de productores, reconexión de clientes y plan de retorno. Una observación puntual de `lag=0` no prueba ausencia de pérdida durante el corte.

**Decisión futura:** adoptar solo tras medir el RPO exigido y demostrar compatibilidad, con responsable y coste operativo del segundo clúster. Registrar el resultado en un ADR sucesor.
