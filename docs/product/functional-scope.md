# Alcance funcional del primer incremento

El [caso práctico](../references/processed/network-monitoring.md) describe capacidades sugeridas; [ADR-015–019](../architecture/decisions/README.md) adoptan las piezas y [ADR-013](../architecture/decisions/ADR-013.md) fija objetivos de aceptación de la primera entrega de producción. El perfil objetivo contempla **cuatro sedes y ocho sondas**; todo evento identifica ambos ámbitos. Estas cifras son requisitos de ensayo, no capacidad ya medida. El [modelo S00](../architecture/modelo-capacidad-s00.md) permite una elección técnica provisional según [ADR-014](../architecture/decisions/ADR-014.md); S17 comprobará la capacidad integrada. [ADR-012](../architecture/decisions/ADR-012.md) sigue vigente para semántica, roles y transporte donde no contradiga ADR-013.

| ID | Capacidad incluida y comportamiento comprobable | Referencia |
|---|---|---|
| F-01 | Sonda pasiva sobre SPAN/TAP con `tshark`; extrae metadatos sin persistir PCAP ni payload. Expone paquetes descartados, errores de parser y pérdida por spool. | Caso §§1–2; ADR-010/012, G-01 y G-02 |
| F-02 | Correlaciona flujos TCP/UDP bidireccionales inferidos por 5-tupla canonizada y ámbito sitio/sonda/VLAN; cierra por inactividad (TCP 5 min, UDP 1 min) o al llegar a 1 h. Otros protocolos producen observaciones, no sesiones. Una captura asimétrica puede producir sesiones parciales; no afirma reconstrucción exacta de TCP. | Caso §§1–2; ADR-012, G-05 |
| F-03 | Publica lotes JSON con `schemaVersion`, `batchId` y `eventId` estables, `siteId`, `sensorId` y tiempo UTC. Spool persistente por sonda para al menos 4 h a tasa sostenida más ráfaga 5× de 15 min; reintento hasta ACK durable, deduplicación y pérdida visible al agotar cuota. | ADR-010/013, G-01 y G-05 |
| F-04 | Valida en ingestión, conserva lote en bandeja durable, proyecta en worker idempotente y aísla eventos inválidos en cuarentena con causa mínima. | ADR-010/012, G-02 y G-05 |
| F-05 | Lista y detalla sesiones durante 30 días consultables: tiempo obligatorio, filtros por sitio/sonda, IP, protocolo y puerto; rango > 24 h y hasta 30 días solo con sitio/sonda e IP de un extremo. Cursor, página ≤ 100 y timeout ≤ 10 s. UI propone últimas 24 h. Sin barrido global de 30 días. | Caso §§1 y 4; ADR-013, G-03 |
| F-06 | Genera candidatos de dispositivo desde MAC/sitio/sonda/VLAN observada (o «sin etiqueta») y registra IP como asociación temporal. Si falta MAC, conserva la observación sin candidato. Un administrador crea o corrige dispositivos, y confirma o rechaza la fusión de candidatos; cada cambio queda auditado. No fusiona automáticamente por IP, MAC entre ámbitos o heurísticas de nombre. | Caso §3; ADR-012, G-05 |
| F-07 | UI interna Angular y API ASP.NET Core; inicio de sesión OIDC y RBAC aplicado en cada operación de la API. Roles iniciales según la matriz siguiente. | Caso §4; ADR-010/012, G-06 |
| F-08 | HTTPS/TLS validado en enlaces de aplicación y datos, mTLS sonda→ingestión, sin endpoints públicos. Certificado de sonda y token de persona cumplen funciones distintas. | ADR-010/012, G-07 y G-09 |
| F-09 | Métricas y alertas de captura, spool, ingestión, worker, consultas, réplica/conmutación, copias y certificados enviadas a una plataforma apta; receptor y acción asignados para cada alerta. | [ADR-011](../architecture/decisions/ADR-011.md), [ADR-013](../architecture/decisions/ADR-013.md), G-07 |

## Matriz de acceso

Los roles provienen del IdP OIDC; la API valida identidad y permisos. La administración de usuarios o grupos pertenece al IdP, no a esta aplicación. Denegar por defecto y registrar las denegaciones sin incluir payload ni secretos.

| Operación | Analista | Auditor | Administrador de inventario |
|---|:---:|:---:|:---:|
| Consultar sesiones y detalle | Sí | Sí | Sí |
| Consultar inventario confirmado | Sí | Sí | Sí |
| Consultar candidatos y observaciones | Sí | No | Sí |
| Crear/corregir dispositivos | No | No | Sí |
| Confirmar/rechazar candidatos y fusionar | No | No | Sí |
| Exportación masiva o cambiar roles | No | No | No |

El auditor accede al inventario confirmado, no a candidatos; la auditoría técnica de cambios se conserva en el registro operativo restringido y se entrega por el procedimiento de operación, no mediante un panel nuevo. La autorización de la sonda es independiente: solo puede entregar lotes a ingestión, nunca consultar sesiones ni modificar inventario.

## Límites y condiciones de salida

- El requisito inicial es 30 días consultables de sesiones; los plazos de bandeja, cuarentena, auditoría y copias se fijan con seguridad y cumplimiento antes de producción. El spool se dimensiona por sonda con datos de prueba, para al menos 4 h de desconexión. El borrado y la pérdida al agotar capacidad son verificables.
- Sin búsqueda libre, exportación masiva, PCAP, SIEM/SOAR, detección de anomalías o fingerprinting. Kafka, ClickHouse, Elasticsearch/Connect, Registry, Keycloak y EJBCA son evoluciones condicionadas por [ADR-010](../architecture/decisions/ADR-010.md).
- Los ejercicios didácticos de §§5–8 del caso se mantienen en la referencia procesada como trabajo de arquitectura, no como funciones del producto.
- S00 documenta supuestos y una decisión técnica provisional para iniciar S01. Antes de liberar, S17 y las demás pruebas de producción deben aportar evidencia empírica de pérdida, reenvío, idempotencia, permisos denegados, borrado, consultas representativas, conmutación y restauración conforme a [ADR-013](../architecture/decisions/ADR-013.md). [Brechas](../roadmap-gaps.md) especifica evidencias y responsables pendientes.
