# Delta for verificacion-base

## MODIFIED Requirements

### Requirement: Pruebas .NET ejecutables desde cero {#REQ-verificacion-base-001}

Proyecto MUST ofrecer runner .NET/xUnit y comandos documentados para restaurar, compilar y ejecutar pruebas de producto; Angular MUST disponer de runner configurado. Arranque/migración/integración MUST ejecutarse en almacenes desechables sin estado previo. Strict TDD MUST conservar evidencia red→green por tarea funcional, resultado antes/después y trazabilidad a requisito. Integraciones reales MUST seguir `aptitud-y-liberacion`; red ausente/dependencia faltante MUST NOT simular aprobación.
(Previously: runner y pruebas limitadas a S01/PostgreSQL desechable.)

#### Scenario: Ejecución local de pruebas

- GIVEN SDK/dependencias documentados y checkout limpio
- WHEN ejecuta comandos .NET/Angular
- THEN restaura/compila y ejecuta pruebas unitarias/integradas de alcance declarado

#### Scenario: Integración sin estado previo

- GIVEN PostgreSQL desechable vacío y servicios aislados requeridos
- WHEN prueba arranque/migración/vertical
- THEN prepara esquema y verifica resultados sin base reutilizada

### Requirement: CI verifica S01 en entorno limpio {#REQ-verificacion-base-002}

CI MUST restaurar/compilar .NET/Angular, aprovisionar dependencias desechables, migrar y ejecutar suites de producto desde checkout limpio. MUST preservar comprobaciones S01–S04 y añadir vertical búsqueda, contratos/permisos/fallos definidos. Fallo o no ejecución de comprobación obligatoria MUST fallar resultado global; MUST publicar resultado y causa sin secretos. Ensayo S17 completo MUST ser job finito reproducible independiente de pruebas rápidas, sin convertir smoke test en evidencia de capacidad.
(Previously: CI verificaba solo S01, sin búsqueda/identidad/servicios adoptados.)

#### Scenario: Pipeline exitoso desde cero

- GIVEN checkout sin artefactos ni almacenes previos
- WHEN ejecuta CI
- THEN prepara servicios/esquema y solo termina correctamente si comprobaciones obligatorias pasan

#### Scenario: Fallo de migración o prueba

- GIVEN migración/prueba obligatoria fallida o bloqueada por dependencia
- WHEN completa etapa afectada
- THEN CI falla y no reporta verificación satisfactoria

### Requirement: Comandos y convenciones de S01 documentados {#REQ-verificacion-base-003}

Documentación MUST identificar requisitos/comandos para compilar, probar, migrar, iniciar stack, generar fixture, consultar Angular y operar fallos/replay/retención/recuperación/certificados. Configuración/roadmap/slices MUST reflejar arquitectura adoptada y dependencias K01–K04/S05–S18, conservando historial S01–S04 y ADR anteriores. MUST distinguir elección de PostgreSQL e infraestructura implementada de capacidad, políticas y continuidad acreditadas. Evidencia MUST enumerar ejecutado/fallido/no ejecutado con entorno/fecha/resultados.
(Previously: comandos S01 y elección provisional de PostgreSQL.)

#### Scenario: Reproducción de la verificación

- GIVEN persona con checkout y requisitos documentados
- WHEN sigue comandos
- THEN puede compilar/probar/arrancar/migrar y recorrer vertical, entendiendo límites reales de la evidencia
