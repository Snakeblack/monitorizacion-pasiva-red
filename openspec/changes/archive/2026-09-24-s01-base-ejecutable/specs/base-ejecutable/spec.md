# base-ejecutable Specification

## Purpose

Definir la base mínima que permite iniciar el host ASP.NET Core y preparar su esquema S01 desde una base PostgreSQL vacía. Esta capacidad no incluye el contrato ni la ingestión de eventos de S02.

## Requirements

### Requirement: Host modular de S01 {#REQ-base-ejecutable-001}

La solución MUST incluir un host ASP.NET Core ejecutable y un módulo de dominio separado. El módulo de dominio MUST permanecer independiente de los detalles de persistencia. El host MUST poder iniciar sin datos previos ni capacidades posteriores a S01.

#### Scenario: Arranque desde un checkout limpio

- GIVEN un checkout limpio y la configuración documentada
- WHEN se inicia el host de S01
- THEN el proceso completa el arranque sin requerir datos creados previamente
- AND el resultado de arranque puede comprobarse sin invocar funciones de S02 o posteriores

#### Scenario: Dominio independiente de persistencia

- GIVEN la solución de S01 compilada
- WHEN se comprueban sus dependencias entre módulos
- THEN el módulo de dominio no depende del módulo de persistencia

### Requirement: Esquema inicializable y repetible {#REQ-base-ejecutable-002}

La solución MUST poder aplicar las migraciones necesarias para el esquema mínimo de S01 sobre PostgreSQL vacío. Repetir la operación MUST dejar el esquema en la misma versión aplicable sin duplicar ni corromper objetos. El uso de PostgreSQL en S01 MUST conservar su condición provisional de ADR-014 y MUST NOT presentarse como validación de capacidad para producción.

#### Scenario: Primera migración en base vacía

- GIVEN una instancia desechable de PostgreSQL sin esquema ni datos de aplicación
- WHEN se aplican las migraciones de S01
- THEN la base alcanza el esquema inicial esperado
- AND el resultado no depende de datos existentes

#### Scenario: Migración repetida

- GIVEN una base que ya alcanzó el esquema inicial de S01
- WHEN se aplican otra vez las migraciones disponibles
- THEN la versión del esquema permanece válida
- AND no se crean duplicados ni se alteran datos de aplicación

#### Scenario: PostgreSQL no disponible

- GIVEN que PostgreSQL no está disponible al solicitar una migración
- WHEN se ejecuta la operación
- THEN la operación informa fallo y no declara la migración como aplicada

### Requirement: Frontera de S01 explícita {#REQ-base-ejecutable-003}

La base MUST limitarse a host, dominio y esquema inicial necesarios para preparar los slices siguientes. MUST NOT implementar contrato de eventos, bandeja, ingestión, worker, API funcional ni interfaz de usuario.

#### Scenario: Revisión del alcance instalado

- GIVEN los módulos y migraciones de S01
- WHEN se comparan con el alcance del slice S01
- THEN se identifican únicamente capacidades de base ejecutable
- AND no existe comportamiento funcional de los slices S02 en adelante