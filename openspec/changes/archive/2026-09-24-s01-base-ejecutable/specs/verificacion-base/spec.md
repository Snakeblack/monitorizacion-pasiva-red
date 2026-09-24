# verificacion-base Specification

## Purpose

Definir cómo se comprueba que la base S01 compila, arranca y migra desde cero mediante pruebas .NET y una ejecución limpia de CI.

## Requirements

### Requirement: Pruebas .NET ejecutables desde cero {#REQ-verificacion-base-001}

El proyecto MUST ofrecer un runner .NET configurado y un comando documentado para restaurar, compilar y ejecutar las pruebas de S01. Las pruebas de arranque y migración MUST poder ejecutarse sin datos persistidos de ejecuciones anteriores y MUST usar un PostgreSQL desechable para integración.

#### Scenario: Ejecución local de pruebas

- GIVEN el SDK y las dependencias documentadas instalados
- WHEN una persona ejecuta el comando de pruebas desde un checkout limpio
- THEN la solución se restaura y compila
- AND se ejecutan las pruebas unitarias y de integración de S01

#### Scenario: Integración sin estado previo

- GIVEN una instancia desechable de PostgreSQL vacía
- WHEN se ejecutan las pruebas de arranque y migración
- THEN las pruebas aplican el esquema inicial y verifican el resultado
- AND no leen ni dependen de una base usada por otra ejecución

### Requirement: CI verifica S01 en entorno limpio {#REQ-verificacion-base-002}

La integración continua MUST restaurar y compilar la solución, aprovisionar PostgreSQL desechable, aplicar las migraciones de S01 y ejecutar las pruebas de S01 desde un checkout sin datos previos. Cualquier fallo en esos pasos MUST producir un resultado de CI fallido.

#### Scenario: Pipeline exitoso desde cero

- GIVEN un checkout sin artefactos ni almacén de datos previo
- WHEN se ejecuta el pipeline de CI
- THEN se prepara PostgreSQL desechable y se aplican las migraciones de S01
- AND el pipeline solo termina correctamente si compilación, arranque y pruebas pasan

#### Scenario: Fallo de migración o prueba

- GIVEN que la migración o una prueba de S01 falla en CI
- WHEN el pipeline completa la etapa afectada
- THEN el resultado global de CI es fallido
- AND no se reporta la verificación de S01 como satisfactoria

### Requirement: Comandos y convenciones de S01 documentados {#REQ-verificacion-base-003}

La documentación MUST identificar los requisitos previos y comandos para compilar, probar, iniciar el host y aplicar las migraciones de S01, además de las convenciones mínimas para ejecutarlo en un entorno desechable. La documentación MUST distinguir la elección provisional de PostgreSQL de una aprobación para producción.

#### Scenario: Reproducción de la verificación

- GIVEN una persona con un checkout limpio y los requisitos previos indicados
- WHEN sigue los comandos documentados
- THEN puede compilar, ejecutar las pruebas, iniciar el host y comprobar la migración inicial
- AND entiende que los ensayos S01 no acreditan capacidad de producción