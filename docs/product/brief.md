# Brief de producto

## Propósito

Construir una plataforma de **monitorización pasiva de red privada** para varias sedes que transforme copias de tráfico SPAN/TAP en metadatos consultables de sesiones y en observaciones de dispositivos. La sonda queda fuera de la ruta de producción de la red. El proyecto documenta decisiones, alternativas, flujos, atributos de calidad y pruebas de aptitud como parte del entregable.

## Personas y resultados

| Actor | Resultado esperado | Fuente |
|---|---|---|
| Analista | Investigar sesiones filtradas, inventario confirmado y candidatos conforme a permisos | [Alcance](functional-scope.md#matriz-de-acceso) |
| Auditor | Consultar sesiones e inventario confirmado sin modificarlos | [Alcance](functional-scope.md#matriz-de-acceso) |
| Administrador de inventario | Crear/corregir dispositivos y confirmar o rechazar candidatos con auditoría | [Alcance](functional-scope.md#matriz-de-acceso) |
| Operación y redes | Detectar pérdida, retrasos, fallos y capacidad; conmutar y restaurar con runbooks | [ADR-011](../architecture/decisions/ADR-011.md), [ADR-013](../architecture/decisions/ADR-013.md) |

## Primera entrega de producción

El perfil inicial de aceptación es **cuatro sedes, ocho sondas, 10 millones de sesiones inferidas al día y 30 días consultables**, con consultas concurrentes, fallos y recuperación definidos en [ADR-013](../architecture/decisions/ADR-013.md). No son cifras observadas ni promesas de capacidad actual. La primera entrega incluye captura, entrega durable, proyección, consulta, inventario con revisión humana, identidad y operación con responsables. La [secuencia de desarrollo](../development/slices.md) empieza por [S00](../architecture/modelo-capacidad-s00.md), un modelo reproducible de capacidad y costes que permite adoptar la [decisión provisional](../architecture/decisions/ADR-014.md) para S01. La aptitud de producción se comprueba después con el sistema integrado y datos representativos en S17; las [brechas](../roadmap-gaps.md) enumeran las demás evidencias y decisiones organizativas.

El caso inglés y el análisis español son fuentes de sugerencias, no una lista de herramientas obligatorias. [ADR-010](../architecture/decisions/ADR-010.md) justifica la ruta modular .NET/Angular y las alternativas. PostgreSQL es candidato por simplicidad operativa para un equipo de seis personas y presupuesto medio; S00 permite elegirlo provisionalmente bajo supuestos explícitos y S17 verifica su capacidad real. Kafka, ClickHouse y otros servicios se valoran ante cuellos medidos en el sistema. [ADR-012](../architecture/decisions/ADR-012.md) conserva las decisiones funcionales de sesión, identidad, roles y transporte, pero sus límites iniciales fueron reemplazados por ADR-013.

No se archivan PCAP ni payload. Las políticas de retención, clasificación de IP/MAC, guardia y objetivos de servicio deben ratificarse antes de operar con datos reales. La producción exige pruebas de carga, pérdida, reintento, seguridad, borrado, conmutación y restauración; ningún valor de diseño sustituye esa evidencia.
