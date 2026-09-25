## Exploration: Contrato JSON v1 e ingestión durable S02

### Current State
S01 proporciona un host ASP.NET Core con `/health/live`, proyectos separados `Monitoring.Domain`, `Monitoring.Persistence` y `Monitoring.Host`, y migraciones EF Core sobre PostgreSQL provisional (ADR-014). La migración inicial crea únicamente el esquema `monitoring`; `MonitoringDbContext` no registra entidades y no hay endpoint, contrato JSON, bandeja ni telemetría de ingesta. Las pruebas S01 ya usan PostgreSQL desechable para validar migraciones y verifican que Domain no dependa de EF/Npgsql.

El alcance confirmado y `docs/development/slices.md` fijan JSON v1 (`schemaVersion`, `batchId`, `eventId`, `siteId`, `sensorId`, UTC), persistencia durable, unicidad por fuente e ID, ACK después del commit y cuotas/señales con ámbito de sede y sonda. S02 excluye worker/proyección, lectura y UI de S03+. La arquitectura candidata describe autenticación de sondas mTLS, pero el slice S02 no define todavía cómo representa la identidad autenticada del origen ni si mTLS se implementa aquí o en un slice posterior.

### Affected Areas
- `src/Monitoring.Host/Program.cs` — actualmente solo expone health; punto de composición HTTP para la ingesta.
- `src/Monitoring.Domain/` — contratos/validación y resultado de ingesta, manteniendo independencia de persistencia según S01.
- `src/Monitoring.Persistence/MonitoringDbContext.cs` — registro del modelo persistente de la bandeja.
- `src/Monitoring.Persistence/Migrations/` — migración incremental para eventos/batches, claves de idempotencia y restricciones de ámbito.
- `tests/Monitoring.Tests/` — patrones existentes con Testcontainers y pruebas de arranque/migración; añadir cobertura del contrato e integración de persistencia/ACK en el trabajo posterior.
- `openspec/specs/base-ejecutable/spec.md` — referencia de frontera: S01 explícitamente excluye contrato y bandeja, así que S02 debe quedar como delta/capacidad separada.
- `docs/development/slices.md`, `docs/architecture/technical-baseline.md`, `docs/architecture/decisions/ADR-014.md` — fuente del alcance S02 y límites de almacenamiento.

### Approaches
1. **Bandeja relacional con transacción PostgreSQL** — Persistir el contenido canónico del evento y su clave de deduplicación en una transacción; usar índice/constraint único sobre fuente autenticada + `eventId`, y confirmar duplicado idéntico como aceptación idempotente. Rechazar el mismo ID con contenido distinto. El endpoint solo emite ACK tras commit exitoso.
   - Pros: encaja con PostgreSQL provisional y con el patrón de migraciones S01; la constraint resuelve concurrencia y el ACK sigue una frontera durable clara.
   - Cons: requiere definir canonicalización/igualdad del JSON, esquema de origen autenticado y política exacta ante conflictos; una fila por evento puede requerir revisión de tamaño/índices en S17.
   - Effort: Medium.

2. **Bandeja con payload opaco y hash canónico** — Guardar bytes o JSON normalizado, hash de contenido y metadatos indexados; una clave única del origen y `eventId` más comparación del hash distingue reenvío idéntico de colisión.
   - Pros: preserva el evento recibido y hace explícita la comparación de identidad sin depender de reconstruir el objeto desde columnas.
   - Cons: exige acordar canonicalización estable entre versiones, tamaño máximo y si igualdad significa bytes exactos o contenido JSON equivalente; guardar ambas representaciones puede duplicar almacenamiento.
   - Effort: Medium.

### Recommendation
Usar una bandeja relacional en PostgreSQL con una restricción única sobre la identidad estable del origen y `eventId`; persistir `batchId`, `schemaVersion`, sede, sonda, UTC y contenido suficiente para comparar reintentos. Decidir antes del diseño si el contenido se almacena como `jsonb` y se compara semánticamente o se canonicaliza y calcula hash, definiendo explícitamente el tratamiento de propiedades JSON reordenadas. Validar el contrato antes de abrir la transacción y enlazar `siteId`/`sensorId` del payload con el origen autenticado para impedir cruce de ámbitos. Contabilizar cuotas y señales usando claves acotadas por sede/sonda, fuera del camino de aceptación durable o en la misma operación cuando la cuota sea una condición de admisión. Mantener el ACK condicionado al commit y tratar reenvíos idénticos como aceptación existente.

### Risks
- No está especificado el esquema exacto de eventos dentro del lote, límites de tamaño/recuento, campos UTC (nombre, precisión y tolerancia), reglas de propiedades desconocidas ni compatibilidad futura de `schemaVersion`.
- “Fuente” necesita una identidad autenticada estable; aceptar solo `siteId`/`sensorId` declarados por el cliente permitiría suplantación. Definir el enlace de credenciales/mTLS al ámbito, aunque la activación de mTLS se asigne a otro slice.
- La semántica de “contenido idéntico” requiere canonicalización; comparaciones de bytes rechazan JSON semánticamente igual si cambia el orden, mientras que hashes sin canonicalización estable pueden dar falsos conflictos.
- La cuota y las señales por ámbito necesitan cardinalidad, límites y semántica de rechazo/reintento; contadores etiquetados por IDs arbitrarios pueden crecer sin límite.
- El commit puede completarse mientras la conexión se pierde antes de devolver ACK. El contrato debe permitir reintento seguro por la clave única; S02 valida no duplicación, mientras la conciliación y cuarentena ampliada pertenecen a S08.
- El PostgreSQL de ADR-014 es candidato de desarrollo sujeto a S17; S02 no debe presentar sus pruebas pequeñas como evidencia de capacidad de producción.

### Ready for Proposal
Yes — el objetivo, aceptación, dependencias y exclusiones están suficientemente acotados. La propuesta puede registrar como preguntas de diseño las decisiones aún abiertas sobre forma del evento y batch, canonicalización/igualdad, límites del contrato y vínculo del origen autenticado al ámbito; estas no impiden iniciar la definición de requisitos.
