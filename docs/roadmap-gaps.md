# Decisiones de diseño y evidencias pendientes de producción

**Estado (05-10-2026):** [ADR-013](architecture/decisions/ADR-013.md) reemplaza los límites de piloto de [ADR-012](architecture/decisions/ADR-012.md). [ADR-014](architecture/decisions/ADR-014.md) y el [modelo S00](architecture/modelo-capacidad-s00.md) habilitan una ruta de desarrollo provisional. El objetivo es una primera entrega de producción para varias sedes y millones de sesiones. Sus cifras son requisitos de aceptación propuestos, **no capacidad medida, SLA firmado ni política corporativa aprobada**. [ADR-015–019](architecture/decisions/README.md) adoptan las piezas y suceden ADR-010/014; [ADR-011](architecture/decisions/ADR-011.md), la observabilidad.

| ID | Dirección de diseño vigente y motivo | Evidencia que puede cambiarla |
|---|---|---|
| G-01 Volumen | Probar 4 sedes/8 sondas, 10 M sesiones/día por 72 h y ráfagas 5× de 15 min. Spool por sonda para ≥ 4 h a tasa sostenida más una ráfaga 5× de 15 min, calculado con bytes/evento y margen. Evita una cuota arbitraria y obliga a medir pérdida. | Topología y muestra autorizada de redes; paquetes/eventos por sesión, tasa por sonda, CPU, disco, red, backlog y drenaje. |
| G-02 Retención y PCAP | 30 días consultables de sesiones (≈ 300 M a volumen objetivo), borrado verificable, sin PCAP ni payload. Plazos de bandeja, cuarentena, auditoría y copias se ratifican con seguridad/cumplimiento; S00 los estima por escenarios y S15 valida su dimensionamiento. | Política de datos, clasificación de IP/MAC, obligaciones forenses, supresión y restauraciones que reintroduzcan datos caducados. |
| G-03 Consultas | 20 simultáneas: detalle, ventana ≤ 24 h con filtros y más de 24 h hasta 30 días solo con sitio/sonda e IP de un extremo. Cursor, página ≤ 100, timeout ≤ 10 s; p95 ≤ 2 s/5 s respectivamente. Sin barrido global ni exportación masiva. | Mezcla y selectividad de analistas, incluidas IP frecuentes; planes, índices, latencia/coste con 30 días cargados. |
| G-04 Continuidad | Objetivo técnico 99,9 % mensual en operación ordinaria con failover de nodo ≤ 15 min. Para desastre: RPO ≤ 15 min de datos confirmados y RTO ≤ 2 h, medidos por separado; réplica y backup/WAL fuera del mismo dominio de fallo. | Inventario de HA/backup y guardia; pruebas de conmutación y restauración. Producto decide calendario, mantenimiento y si un SLA futuro incluye desastres. |
| G-05 Sesiones/dispositivos | Evento versionado e idempotente; sesión inferida por 5-tupla y ventanas de inactividad; observaciones crean candidatos y fusión de inventario manual y auditada. Evita falsas identidades. | Trazas de NAT, captura asimétrica, VLAN, MAC aleatorias, cambios IP y reenvíos; tasas de falsos positivos/negativos aceptadas. |
| G-06 Acceso | Tres roles aplicados en API; sin exportación masiva. Protege modificación de inventario y datos de red. | Matriz ratificada por producto/seguridad, pruebas de denegación y auditoría, necesidad de delegación o exportación. |
| G-07 Plataforma | Reutilizar IdP, CA, despliegue, HA y copias del almacén elegido, telemetría y guardia corporativos **si satisfacen** ADR-013. No presuponer que existen. | Inventario con responsable, soporte, coste y ensayo de integración; un hueco exige alternativa en ADR. |
| G-08 Sugerencias del caso | Arquitectura adoptada PostgreSQL/outbox/Debezium/Kafka/Connect/Elasticsearch/Keycloak/EJBCA conforme a ADR-015–019; ClickHouse/Registry no forman parte de la ruta. Aptitud S17 y aceptación S18 pendientes. | Cuello de ingesta, consulta, replay o capacidad; comparación reproducible, coste y dueño para cambiar. |
| G-09 TLS/mTLS | TLS validado en transporte y mTLS sonda→ingestión; OIDC para personas. Seguridad de la frontera sin certificados cliente universales. | Modelo de amenazas y pruebas de emisión, renovación, revocación y caducidad. |

## Puerta S00 histórica antes de S01

**S00 es el primer trabajo técnico.** El [modelo reproducible](architecture/modelo-capacidad-s00.md) declara hipótesis, calcula escenarios y sensibilidad de las variables cuantificadas, e identifica observaciones, WAL, copias y compresión como entradas pendientes de medición. Las cifras de 30 días (≈ 300 M sesiones a la meta), 72 h de escritura, ráfaga 5× y 20 consultas simultáneas son **cargas objetivo de validación posterior**, no ensayos ejecutados en S00. La decisión [ADR-014](architecture/decisions/ADR-014.md) permite empezar S01 con PostgreSQL como candidato provisional y esquemas migrables. S03/S09/S15 incorporan pruebas incrementales; S16 prueba continuidad; S17 exige la prueba integrada antes de liberar. No se atribuyen p95/p99, IOPS, pérdidas, costes reales ni RPO/RTO medidos a una estimación.

| Hallazgo o evidencia posterior | Acción de diseño |
|---|---|
| El modelo deja incertidumbre acotada y contratos migrables | Empezar S01 con PostgreSQL provisional; registrar límites de validez y pruebas pendientes. |
| Pruebas con la implementación cumplen objetivos y coste/guardia aceptados | Confirmar almacén y dimensionamiento en un ADR de validación. |
| Cuello solucionable con ajustes acotados de PostgreSQL | Repetir ensayo y publicar coste adicional; aprobar solo con objetivos completos. |
| Cuello persistente de histórico o consulta | Comparar ClickHouse bajo el mismo perfil; decidir separación de inventario e histórico y retiro de copia redundante en ADR. |
| Cuello de amortiguación/replay o consumidores independientes | Evaluar Kafka, operaciones y recuperación; repetir ensayo extremo a extremo y registrar ADR. |
| Falta de presupuesto, plataforma o perfil fiable | Bloquear la afirmación de aptitud de producción y revisar alcance/objetivos con producto y operación; S01 puede seguir en entorno aislado. |

La capacidad no se deduce de que PostgreSQL soporte particiones; su ventaja depende de tamaño y consultas. Las copias y WAL permiten diseñar recuperación puntual, pero RPO/RTO se acreditan únicamente con restauración cronometrada ([PITR oficial](https://www.postgresql.org/docs/current/continuous-archiving.html)).

## Validaciones para liberar la primera entrega de producción

| Orden | Brechas | Evidencia de salida y responsable |
|---|---|---|
| Antes de S01 | G-01, G-03, G-08 | Arquitectura y backend publican S00: hipótesis, escenarios, sensibilidad, decisión provisional y pruebas pendientes. |
| Antes de confirmar almacén y liberar | G-01, G-03, G-08 | Redes aporta perfil representativo; backend y operación ejecutan S17, publican capacidad, consultas y coste medidos y confirman o revisan el ADR. |
| Antes de operar datos reales | G-02, G-06 | Producto, seguridad y cumplimiento ratifican plazos y clasificación, borrado/backup, matriz de acceso y auditoría; pruebas de denegación y borrado. |
| Antes de conectar sondas | G-07, G-09 | Infraestructura/seguridad acreditan IdP OIDC, CA, despliegue privado, TLS/mTLS, ciclo de certificados, telemetría y receptor de alertas con guardia. |
| Antes de declarar continuidad | G-04, G-07 | Operación prueba réplica/conmutación ≤ 15 min, backup externo y PITR con RPO ≤ 15 min/RTO ≤ 2 h; producto acuerda calendario y diferencia entre disponibilidad ordinaria y desastre. |
| Antes de aceptación funcional | G-05, G-01 | Redes y analistas verifican captura asimétrica, NAT/VLAN, duplicados y candidatos; pérdida y cuarentena reconciliadas con datos fuente. |

**Regla de cambio:** un resultado contrario al objetivo exige revisar especificación y ADR con impacto, coste, responsable y reversión. La infraestructura adicional se decide por el cuello demostrado, no por la lista de herramientas de los documentos.
