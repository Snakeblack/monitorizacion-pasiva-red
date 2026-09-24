## Exploration: S01 — base ejecutable

### Current State
S01 es el primer slice implementable tras S00. Su salida incluye una solución ASP.NET Core modular, proyecto de dominio, runner de pruebas .NET, CI y almacenamiento desechable para PostgreSQL provisional. La aceptación exige que arranque y migre desde estado limpio en CI, sin depender de datos previos. El alcance excluye contrato de ingestión y lógica de S02 en adelante.

El repositorio aún no contiene solución, proyectos, código de aplicación ni configuración de CI. `openspec/config.yaml` confirma modo Strict TDD, pero declara runner y comandos de build/test vacíos y herramientas de test no disponibles. ADR-014 permite iniciar con PostgreSQL como candidato provisional y exige mantener aislada la persistencia; no valida capacidad, HA ni aptitud para producción. La validación empírica integrada sigue pendiente para S17.

### Affected Areas
- `docs/development/slices.md` — define alcance, aceptación, dependencias y trazabilidad de S01.
- `docs/architecture/decisions/ADR-014.md` — establece PostgreSQL provisional, límites y secuencia de validación.
- `docs/architecture/technical-baseline.md` — fija responsabilidades, separación modular y restricciones de arquitectura.
- `openspec/config.yaml` — stack propuesto y política Strict TDD; faltan comandos y runner.
- `openspec/changes/s01-base-ejecutable/state.yaml` — confirma aceptación del briefing, alcance S01, modo automático y estrategia auto-chain.
- Solución .NET, proyecto de dominio, pruebas, CI y configuración del almacén — áreas nuevas; no existen actualmente.

### Approaches
1. **Una base vertical mínima** — crear solución modular ASP.NET Core, dominio sin dependencias de infraestructura, host ejecutable, pruebas, migración inicial mínima y PostgreSQL efímero de integración.
   - Pros: demuestra arranque y persistencia desde estado limpio con el menor número de piezas; sigue la separación de responsabilidades de ADR-014.
   - Cons: requiere resolver y configurar runner, versión de .NET, mecanismo reproducible de PostgreSQL efímero y proveedor de CI antes de poder demostrar la aceptación.
   - Effort: Medium.

2. **Separar infraestructura de la solución base** — entregar primero estructura y pipeline, y añadir base PostgreSQL y migración en un slice posterior.
   - Pros: PR inicial pequeño y fácil de revisar.
   - Cons: la primera entrega no satisface por sí sola la aceptación S01 y deja sin probar conjuntamente el arranque y la migración limpia.
   - Effort: Medium.

### Recommendation
Usar la base vertical mínima y organizar tareas como PR encadenadas autónomas por cimientos de solución, persistencia/migración y CI/aceptación, ajustando los cortes al pronóstico de líneas. Cada PR debe dejar la solución compilable y aportar pruebas ejecutables. Acordar y configurar primero un runner .NET y el comando de prueba, tal como exige Strict TDD; preparar PostgreSQL efímero sin conservar estado entre ejecuciones. Mantener la capa de persistencia reemplazable y un solo almacén candidato; no añadir broker ni almacén analítico anticipadamente. No describir resultados de capacidad como medidos.

### Risks
- Sin runner ni comando de test acordado, Strict TDD impide iniciar implementación; esta decisión debe resolverse y persistirse en configuración antes de `sdd-apply`.
- El proveedor CI y el mecanismo de aprovisionamiento desechable de PostgreSQL no aparecen definidos en la configuración consultada; evitar asumir servicios corporativos disponibles.
- La solución introduce arquitectura y tooling nuevos en un repositorio sin código, por lo que el número de archivos puede superar 400 líneas; `sdd-tasks` debe delimitar cortes revisables y cada PR debe ser autónoma.
- PostgreSQL es provisional: S01 solo demuestra arranque y migraciones limpias, no volumen, latencia, disponibilidad ni capacidad objetivo.

### Ready for Proposal
Yes — el alcance funcional está delimitado por S01 y ADR-014. La propuesta puede fijar como fuera de alcance las funciones de S02 en adelante y registrar los pendientes de runner, CI y PostgreSQL efímero para resolución en diseño/tareas antes de aplicar.
