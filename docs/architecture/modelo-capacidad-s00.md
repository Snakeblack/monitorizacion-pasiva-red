# S00 — Modelo inicial de capacidad

**Estado:** estimación de diseño, no ensayo ni capacidad demostrada. Los objetivos proceden de [ADR-013](decisions/ADR-013.md); [ADR-014](decisions/ADR-014.md) establece qué decisión permite tomar este modelo. Ninguna cifra representa tráfico observado, rendimiento de PostgreSQL ni coste contratado.

## Hipótesis y fórmulas

| Variable | Escenario | Naturaleza |
|---|---:|---|
| Sedes / sondas | 4 / 8 | Objetivo de aceptación |
| Sesiones agregadas `S` | 10 000 000/día | Objetivo de aceptación |
| Retención `D` | 30 días | Objetivo pendiente de ratificación |
| Ráfaga `F` | 5 × media durante 15 min | Objetivo de aceptación |
| Eventos por sesión `E` | 1 como base; sensibilidad 2 y 5 | Hipótesis de contrato |
| Bytes serializados por evento `B` | 0,5 / 1 / 2 KiB | Hipótesis a medir |
| Bytes de tabla e índices por sesión `P` | 0,5 / 1 / 2 KiB | Hipótesis a medir |
| Margen de reserva `M` | 2 × | Hipótesis de planificación |
| Lote | Hasta 500 eventos o límite por tiempo/tamaño | Hipótesis a cerrar con el contrato |

Un día tiene 86 400 s y 1 GiB = 2³⁰ bytes. La tasa media es `S/86400 = 115,74` sesiones/s; la ráfaga, `F×S/86400 = 578,70` sesiones/s. La tasa de **eventos** es ambas cifras multiplicadas por `E`: sesiones, eventos y paquetes no son equivalentes. Con lotes llenos de 500, `S×E/500 = 20 000×E` lotes/día; los vaciados por tiempo producen más lotes.

El histórico son `S×D = 300 millones` de sesiones. Solo la **tabla más sus índices**, `S×D×P`, requeriría 143 / 286 / 572 GiB para `P=0,5 / 1 / 2 KiB`; con `M=2`, reservar 286 / 572 / 1 144 GiB. La réplica física requiere aproximadamente otra copia; bandeja, observaciones, cuarentena, WAL, copias, sistema y espacio transitorio se calculan aparte. Esta aritmética no predice IOPS, CPU, latencia ni precio.

Con reparto uniforme, una sonda recibe `S/8` sesiones/día. Un corte de 4 h con una ráfaga 5× de 15 min **incluida** requiere `N = (S/8)×(4/24 + (F−1)×(0,25/24))×E = 260 417×E` eventos. A `E=1`, `N×B` es 127 / 254 / 509 MiB para `B=0,5 / 1 / 2 KiB`; con `M=2`, 254 / 509 / 1 018 MiB antes de metadatos del spool y reintentos. Si una sonda concentra el 25 % del tráfico agregado, se duplican esos valores. La cuota real debe partir de la sonda más cargada, `E`, `B` y overhead medidos, con alarma y pérdida explícita al agotarse.

El transporte diario, antes de cabeceras y reintentos, es `S×E×B`: para `E=1` y `B=0,5 / 1 / 2 KiB`, aproximadamente 4,8 / 9,5 / 19,1 GiB/día. El volumen de bandeja depende de su retención; WAL y copias, de actualizaciones, índices y compresión. Se medirán con el contrato implementado.

## Presupuesto y atributos de calidad

| Aspecto | Cálculo o comprobación | Límite del modelo |
|---|---|---|
| Coste mensual | `C = C_compute + C_primary + C_replica + C_backups + C_WAL + C_network + C_telemetry + C_operations`; cotizar recursos, volumen y horas de guardia en cada escenario. | Sin tarifas, hardware ni horas reales no hay importe defendible. |
| Worker y drenaje | Una ráfaga de 15 min que llega 5× más rápido que la capacidad media deja `4×S×E/86400×900` eventos extra. Drenarla durante los 15 min siguientes mientras sigue llegando la media requiere capacidad total cercana a `5×` la media. | Cota de flujo, no benchmark. Medir frescura p95 ≤ 60 s y drenaje ≤ 15 min en S17. |
| Disponibilidad | 99,9 % mensual ordinaria equivale a ~43,2 min por 30 días. Un desastre con RTO de 2 h excedería ese presupuesto si se incluyera. | Mantener aparte conmutación ≤ 15 min y desastre RPO ≤ 15 min/RTO ≤ 2 h; ensayar y acordar alcance de SLA. |
| Pérdida | Contadores reconciliables en captura, spool, ACK, bandeja, worker y consulta. | No mide pérdida previa al evento ni durante un fallo. |

## Resultado de S00 y puerta posterior

1. **Inicio del desarrollo:** PostgreSQL es el almacén candidato único. Los contratos separan captura, evento, ingestión, proyección y consulta; la persistencia de sesiones se encapsula para poder revisar el almacén sin reescribir esas etapas. Consultas selectivas, paginación, particiones temporales y retención automatizada son hipótesis de diseño sujetas a medida. Evitar de entrada Kafka y un segundo histórico reduce operación para un equipo de seis personas; este modelo no certifica 300 M de filas ni percentiles.
2. **Durante el desarrollo:** medir `E`, `B`, `P`, sesgo por sonda, bandeja/WAL/copias, selectividad de IP y coste con contratos y datos representativos. Recalcular el modelo cuando cambie una variable. Pruebas deterministas pequeñas validan corrección, ACK, duplicados e idempotencia sin fingir rendimiento a escala.
3. **S17, validación empírica:** ejecutar el perfil completo de [ADR-013](decisions/ADR-013.md): 30 días poblados, 72 h de escritura, ráfagas, 20 consultas simultáneas, retención, fallo, restauración y costes. Publicar configuración, resultados y límites. Si se distribuyen ejecuciones por ventanas, no declarar cumplido el objetivo de 72 h sin evidencia equivalente. Fallar un criterio exige revisar diseño antes de liberar.

**Disparadores de revisión:** más eventos/sesión; concentración por sonda; consultas globales de 30 días o nuevos filtros; mayor retención; coste/guardia fuera de presupuesto; p95, frescura o recuperación fallidos. Primero ajustar índice, partición, consulta y capacidad de PostgreSQL. Comparar ClickHouse con la misma mezcla si el cuello es histórico/consulta; estudiar Kafka si es amortiguación, replay o consumidores independientes. Cada pieza nueva exige ADR, responsable, coste y prueba extremo a extremo.
