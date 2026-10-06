# Arquitectura adoptada para la primera entrega de producción

**Estado:** arquitectura adoptada por [ADR-015](decisions/ADR-015.md)–[ADR-019](decisions/ADR-019.md). ADR-010/014 se conservan como historia; ADR-013 mantiene los objetivos de aceptación. La aptitud, el coste y la continuidad exigen evidencia S17/S18 e inventario corporativo. Los [documentos procesados](../references/processed/README.md) aportan sugerencias y análisis inicial, no requisitos tecnológicos.

## Escala y principio de diseño

La primera entrega debe servir varias sedes y millones de sesiones, con soporte de operación real. El contrato de prueba es cuatro sedes, ocho sondas, 10 millones de sesiones/día durante 72 h, ráfagas 5× de 15 min y 30 días consultables (unos 300 millones de sesiones). Estos son **objetivos de aceptación propuestos, no capacidad demostrada**. [ADR-013](decisions/ADR-013.md) define consultas, frescura, continuidad y evidencia exigida. El equipo de seis personas y presupuesto medio exige responsables y coste operable para los servicios adoptados.

## Responsabilidades y flujo

El [diagrama completo](flujo-completo.md) ilustra los enlaces y fallos; [slices](../development/slices.md) planifica la construcción. Las piezas son procesos y módulos por responsabilidad, no microservicios por entidad.

| Pieza | Responsabilidad y frontera |
|---|---|
| Sonda con `tshark` y adaptador .NET | Observa copia SPAN/TAP, extrae campos, infiere sesiones TCP/UDP por ámbito y cuenta paquetes descartados. No persiste PCAP ni payload. |
| Spool persistente por sonda | Conserva lotes e IDs hasta ACK; capacidad calculada para ≥ 4 h de tasa sostenida local más una ráfaga 5× de 15 min, con alarma y pérdida explícita al agotarse. |
| Ingestión .NET | Autentica sonda por mTLS, valida contrato versionado y confirma solo tras transacción durable en bandeja. |
| Worker .NET | Proyecta sesiones y observaciones de forma idempotente, marca procesado junto con la proyección y aísla inválidos en cuarentena. Un candidato no se convierte solo en inventario confirmado. |
| API modular y Angular | Consultas selectivas y paginadas, y confirmación manual de candidatos. API aplica RBAC por operación con identidad OIDC. |
| Almacén de datos | PostgreSQL es autoridad para bandeja, sesiones, observaciones, inventario y cuarentena. Sesión, metadatos, marcado y outbox confirman juntos. Debezium/Connect → Kafka → sink Connect → Elasticsearch mantiene una proyección reemplazable de búsqueda; el detalle lee autoridad. S17 ensaya histórico, carga y coste antes de liberar. |
| Plataforma operativa | Receptor de telemetría, guardia, certificados, copias y procedimiento de conmutación/restauración con responsables. Aprovechar servicios corporativos aptos. |

El evento lleva `schemaVersion`, `batchId`, `eventId`, `siteId`, `sensorId` y UTC. Un ACK perdido permite reenvío del mismo ID. La sesión es un flujo bidireccional **inferido** por 5-tupla y ámbito sitio/sonda/VLAN, no una reconstrucción exacta de TCP. La IP observada no confirma por sí sola un activo. [ADR-012](decisions/ADR-012.md) conserva esas reglas y la matriz de roles donde no contradiga ADR-013. Ingestión, API y worker comparten módulos de dominio; pueden escalarse o aislarse como procesos si lo demuestra la prueba.

## Escenarios de calidad

| Estímulo y respuesta exigida | Criterio de ensayo | Responsable |
|---|---|---|
| Varias sondas publican simultáneamente | 4 sedes/8 sondas, 10 M sesiones/día por 72 h y ráfaga 5× durante 15 min; contar pérdidas y backlog | Redes + backend |
| Enlace de una sonda cae | Spool ≥ 4 h a tasa sostenida más ráfaga 5× de 15 min, reintento idempotente, alertas de ocupación/edad y drenaje medido | Redes + operación |
| Analistas consultan durante la escritura | 20 consultas simultáneas; p95 ≤ 2 s para 24 h y ≤ 5 s para 30 días con sitio/sonda e IP de un extremo, cursor y página ≤ 100; sin barrido global | Producto + backend |
| Worker acumula pendientes | Frescura p95 ≤ 60 s tras ACK a carga sostenida y backlog de ráfaga drenado ≤ 15 min; alarma con dueño | Backend + operación |
| Falla un nodo ordinario | Conmutación ensayada ≤ 15 min; medir disponibilidad ordinaria hacia objetivo 99,9 % mensual | Operación |
| Desastre de base/sitio | Restauración aislada con RPO ≤ 15 min y RTO ≤ 2 h; medir pérdida real de ACK confirmados | Operación + producto |
| Certificado, token o permiso falla | Rechazo OIDC/RBAC y mTLS, auditoría sin secretos, rotación y revocación probadas | Seguridad + operación |

La disponibilidad ordinaria y la recuperación de desastre se informan por separado: un desastre de 2 h supera el presupuesto mensual del 99,9 %. No se promete un SLA externo hasta acordar calendario, exclusiones y guardia. La captura puede perder paquetes antes de crear eventos; cada etapa publica contadores reconciliables y la pérdida se informa explícitamente.

## Datos, continuidad y seguridad

Retener 30 días **consultables** de sesiones como requisito de diseño; producto, seguridad y cumplimiento deben ratificar el plazo y establecer bandeja, cuarentena, auditoría y copias. Borrado automatizado y comprobable, incluida restauración que pueda reintroducir datos caducados. Dimensionar tablas, índices, WAL, réplica, copias y crecimiento con S00. PostgreSQL documenta particionado temporal y poda, útiles según consultas y tamaño ([manual](https://www.postgresql.org/docs/current/ddl-partitioning.html)); no equivalen a rendimiento asegurado.

Para continuidad, elegir alta disponibilidad gestionada compatible o primaria/réplica con procedimiento de conmutación probado, más copia base y archivo de WAL fuera del dominio de fallo. Una réplica no reemplaza una copia ni garantiza por sí sola RPO: probar failover y restauración/PITR con datos confirmados ([archivo y PITR](https://www.postgresql.org/docs/current/continuous-archiving.html), [servidores en espera](https://www.postgresql.org/docs/current/warm-standby.html)). Si la plataforma compartida no ofrece esto con dueño y guardia, registrar alternativa y coste antes del despliegue.

Acceso privado por LAN/VPN, TLS validado en enlaces de aplicación y datos, mTLS sonda→ingestión, OIDC para personas y RBAC en API. Separar certificado de máquina de autorización de usuario. Keycloak/OIDC identifica personas; EJBCA emite certificados de sondas. Integrar observabilidad corporativa cuando cumple el contrato; [ADR-011](decisions/ADR-011.md) condiciona Prometheus/Grafana a un hueco real. La telemetría cubre captura, spool, ingesta, worker, consultas, backup, réplica, certificados y alarmas con destinatario y runbook.

## Contratos y continuación

La identidad completa `(siteId,sensorId,eventId)` se codifica en tres segmentos base64url. Revisiones semánticas y barreras mínimas de supresión evitan resurrección en replay; Elasticsearch recibe INSERT con external version desde el header `revision`, sin usar offsets como versión. Mappings strict y alias versionado permiten reconstrucción desde PostgreSQL. Los [ADR-015–019](decisions/README.md) comparan alternativas y fijan reversión conservando datos/outbox/offsets.

S01–S04 siguen implementados y archivados. K01/K04/K02/K03/S09 amplían la ruta primero con sesiones ingresadas buscables; S05–S16 incorpora captura, inventario, identidad y operación. El stack interno de demo se reproduce con [los comandos versionados](../development/stack.md). Su arranque no acredita S17 ni habilita datos reales.

**S17 precede a la liberación:** ensaya 30 días poblados, 72 h de escritura, ráfagas, 20 consultas concurrentes, retención y fallo. Publica p95/p99, recursos, backlog, pérdida, tamaños, recuperación, coste y horas de operación. Un objetivo fallido exige corrección y reensayo; cualquier cambio arquitectónico requiere un sucesor, sin reinterpretar retrospectivamente la decisión adoptada.
