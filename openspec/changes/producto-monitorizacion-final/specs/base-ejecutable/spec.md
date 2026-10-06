# Delta for base-ejecutable

## MODIFIED Requirements

### Requirement: Esquema inicializable y repetible {#REQ-base-ejecutable-002}

Solución MUST aplicar migraciones secuenciales explícitas de producto sobre PostgreSQL vacío o esquema S01–S04 existente. Repetición MUST dejar versión válida sin duplicar/corromper objetos/datos. Migraciones MUST preservar identidad/contenido aceptados, añadir modelo canónico/outbox/inventario y adaptar sintéticos válidos existentes. Fallo MUST declararse sin marcar versión aplicada; despliegues concurrentes MUST NOT aplicar dos veces. PostgreSQL MUST ser autoridad adoptada; su elección MUST NOT presentarse como capacidad/continuidad medida. Reversión MUST conservar datos/outbox y declarar incompatibilidades antes de degradar binario.
(Previously: esquema mínimo S01 y PostgreSQL provisional de ADR-014.)

#### Scenario: Primera migración en base vacía

- GIVEN PostgreSQL desechable vacío
- WHEN aplica migraciones disponibles
- THEN alcanza esquema esperado sin depender de datos anteriores

#### Scenario: Migración repetida

- GIVEN base en versión actual con datos
- WHEN repite migraciones
- THEN mantiene versión/datos y no duplica objetos

#### Scenario: PostgreSQL no disponible

- GIVEN PostgreSQL inaccesible
- WHEN solicita migración
- THEN informa fallo sin declarar versión aplicada

#### Scenario: Actualización de S01–S04

- GIVEN esquema previo con eventos/sesiones sintéticas
- WHEN migra a producto
- THEN conserva identidad/JSON y habilita representación buscable/publicación idempotente

### Requirement: Frontera de S01 explícita {#REQ-base-ejecutable-003}

Base MUST conservar responsabilidades de host/dominio/esquema separadas, ahora permitiendo slices funcionales en módulos delimitados. Dominio MUST permanecer independiente de ORM, web, Kafka/Elasticsearch/Keycloak/EJBCA; contratos y puertos MUST mapear fronteras sin compartir implementación de host con sonda. Configuración/wiring MUST declarar dependencia productiva y fallar explícitamente ante configuración insegura/incompatible; MUST NOT activar modos de prueba silenciosamente.
(Previously: prohibía contrato, ingestión, worker, API e interfaz posteriores a S01.)

#### Scenario: Revisión del alcance instalado

- GIVEN módulos/migraciones de producto
- WHEN comprueba dependencias
- THEN base conserva límites y funcionalidades posteriores usan módulos/puertos sin SDK/ORM en dominio
