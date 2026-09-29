# Slices de desarrollo de la primera entrega de producción

El [alcance F-01–F-09](../product/functional-scope.md) define las funciones. [ADR-013](../architecture/decisions/ADR-013.md) fija los objetivos de aceptación de varias sedes, 30 días y continuidad; prevalece sobre las cifras de piloto de [ADR-012](../architecture/decisions/ADR-012.md). [ADR-010](../architecture/decisions/ADR-010.md) explica la arquitectura candidata y [ADR-011](../architecture/decisions/ADR-011.md), la observabilidad. Las [brechas G-01–G-09](../roadmap-gaps.md) indican evidencias y responsables pendientes. El [flujo](../architecture/flujo-completo.md) muestra la ruta completa.

**No ha comenzado la implementación. S00 establece una decisión de diseño revisable antes de S01.** Los números de ADR-013 son objetivos propuestos, no capacidad medida ni SLA ratificado. Cada slice posterior tiene un resultado revisable, una prueba que primero falla según TDD estricto y un presupuesto orientativo de 400 líneas cambiadas por revisión. Un slice grande se divide en unidades con comprobación propia; no se pierde su criterio de salida. Los S01–S04 usan datos sintéticos y entorno aislado. Ningún enlace con datos reales se habilita antes de cumplir seguridad, datos y operación.

## Ruta y dependencias

| Etapa | Slices | Resultado |
|---|---|---|
| Decisión previa | S00 | Modelo de capacidad por escenarios, sensibilidad y ADR de elección provisional. |
| Ruta vertical | S01–S04 | Lote → bandeja → worker → API → Angular, en entorno aislado. |
| Captura y entrega | S05–S08 | Metadatos, sesiones inferidas, spool dimensionado, reintento, idempotencia y cuarentena. |
| Uso | S09–S11 | Consultas selectivas de 30 días e inventario manual auditado. |
| Protección y continuidad | S12–S16 | Acceso, transporte, alertas, borrado, alta disponibilidad y recuperación. |
| Aceptación | S17–S18 | Ensayo integrado con datos representativos y decisión documentada de producción. |

## S00

**Decisión de diseño por modelo reproducible antes del desarrollo del producto.**

- **Entrega:** [modelo S00](../architecture/modelo-capacidad-s00.md) con hipótesis explícitas para cuatro sedes y ocho sondas, 10 M sesiones/día, 30 días, ráfaga 5× y 20 consultas; fórmulas de tasa, volumen, almacenamiento y spool; escenarios y sensibilidad de las variables cuantificadas. Registrar observaciones, WAL, copias y compresión como variables por medir y qué entradas requieren muestra de Redes. No se exige captura real, carga de 300 M filas ni esperar 72 h para terminar S00.
- **Evaluación:** comparar PostgreSQL con alternativas ante cuellos concretos de histórico, consulta o replay; estimar coste y carga operativa como rangos, sin presentar latencias, IOPS, disponibilidad ni RPO/RTO como mediciones. Indicar qué resultado futuro obligaría a revisar la elección.
- **Decisión:** [ADR-014](../architecture/decisions/ADR-014.md) fija la ruta provisional, límites de uso y reversión. El esquema inicial de S01–S03 será migrable y evitará acoplar contratos de ingestión a un motor. El almacén definitivo y su tamaño se confirman mediante ensayos con la implementación, no por extrapolación del modelo.
- **Puerta:** se puede iniciar S01 al quedar documentados modelo, incertidumbre, decisión provisional y pruebas pendientes. Las pruebas de carga y consultas se automatizan de forma incremental en S03/S09/S15; S16 ensaya conmutación y restauración; **S17 acredita la aptitud extremo a extremo con datos representativos antes de liberar**. **Depende de:** ADR-013. **Traza:** F-01/F-03/F-04/F-05/F-09; G-01/G-03/G-04/G-07/G-08.

## S01

**Base ejecutable conforme a la decisión provisional S00.**

- **Entrega:** solución ASP.NET Core modular, proyecto de dominio, runner de pruebas .NET, CI y almacén desechable de la ruta elegida en S00. Crear solo las migraciones necesarias para el primer contrato; usar una instancia efímera para integración. Registrar comandos de pruebas y convenciones de despliegue.
- **Aceptación TDD:** arranque y migración se verifican desde estado limpio en CI; el resultado no depende de datos previos. **Depende de:** S00. **Traza:** F-03/F-04; G-07/G-08; [base técnica](../architecture/technical-baseline.md).

## S02

**Contrato versionado e ingestión durable.**

- **Entrega:** JSON v1 con `schemaVersion`, `batchId`, `eventId`, `siteId`, `sensorId` y UTC; validación y bandeja durable con unicidad por fuente e ID. ACK solo tras commit. Aislamiento de cuotas y señales por sede/sonda.
- **Aceptación TDD:** lote válido persiste; reenvío idéntico no duplica; mismo ID con contenido distinto se rechaza; rollback no genera ACK; fuente incorrecta no cruza ámbito. **Depende de:** S01. **Traza:** F-03/F-04; G-05/G-08.

## S03

**Proyección idempotente y lectura mínima.**

- **Entrega:** worker que convierte un evento sintético de sesión en registro consultable; API de detalle por ID. Con la ruta provisional de PostgreSQL, proyectar y marcar procesado en una transacción. Una separación futura de almacenes requiere un ADR que fije idempotencia, estado de procesamiento y recuperación tras fallos parciales.
- **Aceptación TDD:** ejecutar dos veces o fallar entre proyección y marcado deja una sola sesión consultable y permite recuperar el pendiente; detalle respeta el ámbito. **Depende de:** S02 y decisión S00. **Traza:** F-02/F-04/F-05; G-05/G-08.

## S04

**Vista Angular de extremo a extremo.**

- **Entrega:** UI interna que muestra detalle, vacío y error usando el fixture S02–S03; runner frontend y CI. Construcción de la UI con la skill `linear-attio-ui` (perfil híbrido: chrome sobrio tipo Linear + superficies de datos tipo Attio) como lenguaje de diseño de herramientas internas densas en datos, también en las extensiones de UI de S09–S12. Acceso limitado al entorno de desarrollo hasta S12–S13.
- **Aceptación TDD:** prueba de componente e integración fixture → ACK → worker → API → UI. **Depende de:** S03. **Traza:** F-05/F-07; G-03/G-07.

## S05

**Extracción pasiva en varias sondas.**

- **Entrega:** adaptador de `tshark` que extrae campos TCP/UDP y observaciones, sin guardar PCAP ni payload. Configuración independiente por sede/sonda; contadores de paquetes vistos/descartados y errores de parseo.
- **Aceptación TDD:** trazas de varias fuentes conservan ámbito; líneas inválidas no se convierten en sesiones ni paran captura; laboratorio autorizado mide permisos, CPU y pérdida. **Depende de:** S02. **Traza:** F-01; G-01/G-02/G-05.

## S06

**Sesiones inferidas por ámbito.**

- **Entrega:** correlación bidireccional por 5-tupla canonizada y sitio/sonda/VLAN; cierre TCP a 5 min de inactividad, UDP a 1 min o duración máxima de 1 h, parámetros versionados. Otros protocolos quedan como observaciones.
- **Aceptación TDD:** direcciones inversas, VLAN/sitios/sondas distintos, NAT, captura asimétrica, reinicio y ventanas no crean fusiones indebidas; se señala que la sesión es inferida. **Depende de:** S05 y S03. **Traza:** F-02; G-05; [glosario](../product/glossary.md).

## S07

**Spool por sonda y reenvío con pérdida visible.**

- **Entrega:** lotes con IDs estables en disco hasta ACK; cuota **calculada por sonda** para al menos 4 h a su tasa sostenida más una ráfaga 5× de 15 min dentro del corte y margen estimado en S00, ajustado con bytes/evento medidos durante S05–S07. Retries acotados, alarmas de ocupación/edad/drenaje y descarte contabilizado al agotarse.
- **Aceptación TDD:** corte, reinicio, ACK perdido, ráfaga y spool lleno conservan identidad e idempotencia; bytes y tiempos cumplen la cuota dimensionada; cualquier pérdida tiene contador y alerta. **Depende de:** S02, S05 y S06. **Traza:** F-01/F-03; G-01/G-04/G-05.

## S08

**Ingestión robusta, cuarentena y reconciliación.**

- **Entrega:** distinguir error permanente/transitorio; cuarentena restringida con causa mínima; reintento del worker y contadores conciliables de captura, emisión, aceptación, duplicados, cuarentena, proyección y descarte. Pendientes sin resolver no se purgan automáticamente.
- **Aceptación TDD:** un evento inválido no bloquea los válidos; fallos temporales y respuestas ambiguas no duplican ni se cuentan como éxito; la diferencia entre etapas se ve en telemetría. **Depende de:** S02, S03 y S07. **Traza:** F-03/F-04/F-09; G-01/G-02/G-05.

## S09

**Consultas selectivas de 30 días.**

- **Entrega:** detalle por ID y listado con tiempo obligatorio, sitio/sonda y filtros de IP, protocolo y puerto; hasta 24 h con filtros, y entre 24 h y 30 días solo con sitio/sonda e IP de un extremo. Cursor estable, página ≤ 100 y timeout ≤ 10 s. Angular propone últimas 24 h. Índices/particiones siguen S00.
- **Aceptación TDD:** rango amplio sin filtros obligatorios y páginas excesivas se rechazan; cursor no repite ni omite; ámbito y permisos filtran resultados; timeout no deja consultas ilimitadas. El p95 con 20 consultas se mide en S17. **Depende de:** S03 y S04. **Traza:** F-05; G-03/G-08.

## S10

**Observaciones y candidatos.**

- **Entrega:** observaciones por MAC + sitio + sonda + VLAN, incluida «sin etiqueta»; IP como asociación temporal. Si falta MAC, persiste observación sin candidato. UI de candidatos.
- **Aceptación TDD:** cambios de IP o ámbito no fusionan activos; duplicados no multiplican candidatos; NAT/MAC aleatoria no se promueven por heurística. **Depende de:** S05 y S08. **Traza:** F-06; G-05.

## S11

**Inventario confirmado con fusión manual.**

- **Entrega:** crear/corregir dispositivos, confirmar/rechazar candidatos y fusionar explícitamente con auditoría de actor, acción y tiempo, usando reglas compartidas por API y worker.
- **Aceptación TDD:** ninguna observación confirma automáticamente; conflictos no pisan correcciones manuales; decisiones quedan trazables. **Depende de:** S10 y S04. **Traza:** F-06/F-07; G-05/G-06.

## S12

**OIDC y RBAC por operación.**

- **Entrega:** integración con IdP apto y autorización en API según [matriz de acceso](../product/functional-scope.md#matriz-de-acceso); UI muestra funciones autorizadas sin sustituir la comprobación del servidor. Test con proveedor aislado.
- **Aceptación TDD:** operaciones permitidas y denegadas, token inválido/caducado y separación entre sonda y persona; denegaciones auditadas sin secretos. Identidad real y roles se validan con seguridad. **Depende de:** S04, S09 y S11. **Traza:** F-07; G-06/G-07.

## S13

**TLS validado y mTLS de sondas.**

- **Entrega:** HTTPS/TLS en enlaces de aplicación y datos; mTLS sonda→ingestión, despliegue privado, emisión, renovación y revocación de certificados de máquina. Sin fallback en claro.
- **Aceptación TDD:** certificados ausentes, inválidos, vencidos o revocados se rechazan; cliente rechaza servidor inválido; CA real o alternativa elegida se ensaya antes de conectar sondas. **Depende de:** S07, S08 y S12. **Traza:** F-08; G-07/G-09.

## S14

**Telemetría, alertas y guardia.**

- **Entrega:** señales de captura, spool, ACK, worker, consultas, retención, réplica/conmutación, backup/PITR y certificados. Receptor compatible, umbral, destinatario y runbook por alerta; inventario de capacidad corporativa conforme a [ADR-011](../architecture/decisions/ADR-011.md). Prometheus/Grafana solo cubren una carencia demostrada y costeada.
- **Aceptación TDD:** fallos inducidos llegan al receptor y tienen acción; etiquetas y logs no exponen IP/MAC, secretos ni cardinalidad sin límite; operación confirma guardia. **Depende de:** S05–S09 y S13. **Traza:** F-09; G-01/G-04/G-07.

## S15

**Retención y borrado.**

- **Entrega:** 30 días consultables de sesiones y expiración verificable; plazos de bandeja, cuarentena, auditoría y copias fijados con producto, seguridad y cumplimiento antes de datos reales. Pendientes excluidos de purga automática; restauración contempla datos ya caducados.
- **Aceptación TDD:** reloj controlado prueba caducidad y exclusión de pendientes; fallo de borrado alerta; restaurar no vuelve a exponer datos vencidos. El plan dimensiona borrado y almacenamiento con S00. **Depende de:** S08–S10 y S14. **Traza:** F-01/F-04/F-05; G-02/G-08.

## S16

**Alta disponibilidad y recuperación.**

- **Entrega:** alta disponibilidad gestionada apta o primaria/réplica con conmutación operada, copia base y archivo continuo de WAL fuera del dominio de fallo, PITR y runbooks. Si S00 elige otro almacén, demostrar continuidad equivalente sin duplicar histórico. Integrar alertas S14.
- **Aceptación TDD:** fallo ordinario de nodo con conmutación objetivo ≤ 15 min; restauración aislada de datos confirmados con RPO ≤ 15 min y RTO ≤ 2 h; verificar integridad, reingesta idempotente, borrados y señales de fallo. Producto/operación deciden calendario y tratamiento contractual de desastres frente a disponibilidad ordinaria 99,9 %. **Depende de:** S13–S15. **Traza:** F-03/F-04/F-09; G-02/G-04/G-07.

## S17

**Repetición de aptitud extremo a extremo con datos representativos.**

- **Entrega:** validar los objetivos de ADR-013 y contrastar las hipótesis S00 sobre la ruta construida y una muestra autorizada representativa: cuatro sedes/ocho sondas, 30 días completos, 10 M sesiones/día durante 72 h, ráfagas 5× de 15 min y 20 consultas simultáneas. Incluir ingestión, worker, borrado, conmutación, PITR, capturas perdidas, spool y coste operativo. Publicar hardware y distribución para que el resultado sea reproducible.
- **Aceptación:** p95 ≤ 2 s para 24 h, ≤ 5 s para 30 días selectivos y timeout ≤ 10 s; frescura p95 ≤ 60 s tras ACK, backlog drenado ≤ 15 min tras ráfaga; pérdidas reconciliadas; continuidad y coste cumplen ADR-013. Si falla cualquier objetivo, revisar el cuello y ADR, repetir después del ajuste; no extrapolar desde carga parcial. **Depende de:** S05–S16. **Traza:** F-01–F-09; G-01/G-03/G-04/G-05/G-07/G-08.

## S18

**Liberación de la primera entrega de producción.**

- **Entrega:** paquete privado para varias sedes/sondas, configuración y secretos por entorno, procedimientos de operación, evidencia F-01–F-09 y G-01–G-09, y decisión de salida con responsables. Conectar datos reales solo tras seguridad, políticas y operación verificadas.
- **Aceptación:** recorrido funcional y pruebas de permisos, mTLS, pérdida, consulta, borrado, alertas, failover, PITR y capacidad con resultados de S17; producto, redes, seguridad y operación aceptan o documentan el bloqueo. Sin cierre de [validaciones de producción](../roadmap-gaps.md#validaciones-para-liberar-la-primera-entrega-de-producción), no se declara aptitud. **Depende de:** S00–S17. **Traza:** F-01–F-09; G-01–G-09; [roadmap](../roadmap.md), [ADR](../architecture/decisions/README.md).

## Siguiente paso

Con S00 documentado, abrir el cambio SDD de S01 para construir la base ejecutable conforme a la decisión provisional. Incorporar pruebas de capacidad pequeñas y repetibles a medida que existan ingestión, worker y consultas; S17 exige validación representativa completa para liberar producción.
