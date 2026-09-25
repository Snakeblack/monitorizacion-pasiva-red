# Apply Progress: S02 — contrato v1 e ingestión durable

## Phase 1a — PR 1: parser del contrato de dominio

**Delivery strategy:** auto-chain (`feature-branch-chain`). **Branch:** `feat/s02-contract-unit`.

Este es un ciclo TDD nuevo e independiente en el worktree aislado, después de una implementación interrumpida y no verificada en el checkout original. Las pruebas se escribieron antes de añadir el contrato; la implementación del checkout original no se usa como evidencia de RED ni se incluye en este cambio.

### Progreso

- [x] 1.1 Añadidas y ejecutadas pruebas unitarias de versión y campos, tipos, IDs de 1–128 caracteres, timestamps UTC `Z` de hasta milisegundos, `data` objeto, 1–500 eventos y el límite de 1 MiB.
- [~] 1.2 Añadidos los modelos y el parser JSON estricto de dominio; el filtro focal pasa. Queda pendiente probar que el endpoint traduzca el rechazo contractual a HTTP 400 sin invocar persistencia, fuera del alcance de esta Phase 1a.

### TDD Cycle Evidence

| Task | Test file | Layer | Safety net | RED | GREEN | Triangulate | Refactor | Notes |
|------|-----------|-------|------------|-----|-------|-------------|----------|-------|
| 1.1 / 1.2 | `tests/Monitoring.Tests/IngestionContractTests.cs` | Unit | N/A (archivos nuevos) | ✅ Test-first: primer intento detectó que faltaba el contrato; con una firma mínima compilable, el filtro descubrió 29 casos y fallaron 6 aserciones de aceptación | ✅ Filtro focal: 29 aprobados, 0 fallidos | ✅ 29 casos con entradas válidas e inválidas en límites distintos | ➖ No hacía falta | Evidencia de este ciclo aislado; no se atribuyen resultados del checkout original. |

### Verificación

- `dotnet test Monitoring.slnx --filter FullyQualifiedName~IngestionContractTests` — RED observado: exit 1; 29 casos, 23 aprobados y 6 fallidos con el parser sin comportamiento.
- `dotnet test Monitoring.slnx --filter FullyQualifiedName~IngestionContractTests` — GREEN: exit 0; 29 aprobados, 0 fallidos.
- `dotnet build Monitoring.slnx --no-restore` — exit 0; 0 advertencias, 0 errores.

### Archivos

| Archivo | Cambio |
|---------|--------|
| `tests/Monitoring.Tests/IngestionContractTests.cs` | Pruebas unitarias del contrato JSON v1. |
| `src/Monitoring.Domain/Ingestion/BatchContract.cs` | Modelos de lote/evento y parser estricto, sin dependencias de Host o Persistence. |
| `openspec/changes/s02-contrato-ingestion-durable/tasks.md` | Estado de 1.1 y 1.2 actualizado; 1.2 conserva estado parcial hasta probar el endpoint. |

### Límites de esta unidad

No se implementó ni probó código Host, respuesta HTTP 400, identidad o acceso a persistencia. La verificación de esta unidad cubre la validación pura del cuerpo en Domain.

## Phase 1b — PR 1: identidad confiable y frontera HTTP del host

**Delivery strategy:** auto-chain (`feature-branch-chain`). **Branch:** `feat/s02-host-boundary`. **Base:** contrato PR #9.

La slice añade la frontera HTTP del host en un ciclo TDD nuevo en el worktree aislado. El progreso 1.2 de Phase 1a queda completado al verificar que un cuerpo contractual inválido obtiene HTTP 400 antes del writer.

### Progreso

- [x] 1.2 El endpoint traduce cuerpo inválido o sobredimensionado a HTTP 400 antes de invocar el writer.
- [x] 1.3 La identidad confiable se obtiene de una feature del host; ausencia devuelve HTTP 401 y ámbito discrepante HTTP 403 antes del writer. Cabeceras y bearer no crean identidad.
- [x] 1.4 Ruta Minimal API registrada, lectura limitada a 1 MiB y HTTP 200 vacío solo tras aceptación explícita del writer. El writer no configurado devuelve HTTP 500.
- [x] 1.5 Mapeo de errores consolidado y filtros focales de host y contrato ejecutados.

### TDD Cycle Evidence

| Task | Test file | Layer | Safety net | RED | GREEN | Triangulate | Refactor | Notes |
|------|-----------|-------|------------|-----|-------|-------------|----------|-------|
| 1.2 / 1.3 / 1.4 / 1.5 | `tests/Monitoring.Tests/IngestionHostTests.cs` | Integration (`WebApplicationFactory`) | ✅ `HostStartupTests`: 1 aprobado antes de modificar `Program.cs` | ✅ 6 casos fallaron con HTTP 501 en la ruta mínima | ✅ Filtro host: 7 aprobados, 0 fallidos | ✅ Casos HTTP 400, 401, 403, 200 vacío, writer predeterminado y claims ignorados | ➖ No hacía falta | Identidad inyectada por el host de prueba; cabeceras y bearer del cliente no son fuente de identidad. |

### Verificación

- `dotnet test Monitoring.slnx --filter FullyQualifiedName~IngestionHostTests` — RED observado: exit 1; 6 fallidos porque la ruta mínima devolvía HTTP 501.
- `dotnet test Monitoring.slnx --filter FullyQualifiedName~IngestionHostTests` — GREEN final: exit 0; 7 aprobados, 0 fallidos.
- `dotnet test Monitoring.slnx --filter FullyQualifiedName~IngestionContractTests` — exit 0; 29 aprobados, 0 fallidos.
- `dotnet test Monitoring.slnx --filter FullyQualifiedName~HostStartupTests` — exit 0; 1 aprobado, 0 fallidos.
- `dotnet build Monitoring.slnx --no-restore` — exit 0; 0 advertencias, 0 errores.

### Archivos

| Archivo | Cambio |
|---------|--------|
| `tests/Monitoring.Tests/IngestionHostTests.cs` | Pruebas HTTP de rechazo previo al writer, identidad confiable, cuerpo limitado, ACK vacío y fallo por writer sin configurar. |
| `src/Monitoring.Host/Ingestion/TrustedSensorIdentity.cs` | Límite de identidad mediante feature del host, contrato del writer y resultado de escritura. |
| `src/Monitoring.Host/Ingestion/BatchEndpoint.cs` | Validación HTTP, lectura limitada y ACK solo tras aceptación del writer. |
| `src/Monitoring.Host/Program.cs` | Registro de dependencias seguras por defecto y ruta de ingestión. |
| `openspec/changes/s02-contrato-ingestion-durable/tasks.md` | Fase 1 marcada completa tras verificación local. |

### Límite de esta unidad

No se implementó persistencia durable ni cuota. El writer predeterminado falla de forma cerrada; el único HTTP 200 probado usa un writer falso que confirma aceptación. La prueba no demuestra durabilidad PostgreSQL, que queda para Phase 2.

```json:strict-tdd-evidence
{
  "schema_version": 1,
  "cycles": [
    {
      "tasks": ["1.1", "1.2"],
      "test_file": "tests/Monitoring.Tests/IngestionContractTests.cs",
      "test_name": "Monitoring.Tests.IngestionContractTests",
      "layer": "unit",
      "red": {
        "command": "dotnet test Monitoring.slnx --filter FullyQualifiedName~IngestionContractTests",
        "exit_code": 1,
        "observed": "El primer intento, antes de cualquier código de producción, falló al compilar porque BatchContract no existía. Tras añadir solo la firma mínima para que las pruebas compilaran, el parser vacío produjo 6 fallos de aserción en los casos de aceptación; 23 rechazos pasaron.",
        "discovered": 29,
        "passed": 23,
        "failed": 6
      },
      "green": {
        "command": "dotnet test Monitoring.slnx --filter FullyQualifiedName~IngestionContractTests",
        "exit_code": 0,
        "discovered": 29,
        "passed": 29,
        "failed": 0
      },
      "triangulation": "29 casos cubren múltiples valores válidos e inválidos, límites y formas JSON distintas.",
      "refactor": "not-needed"
    },
    {
      "tasks": ["1.2", "1.3", "1.4", "1.5"],
      "test_file": "tests/Monitoring.Tests/IngestionHostTests.cs",
      "test_name": "Monitoring.Tests.IngestionHostTests",
      "layer": "integration",
      "safety_net": {
        "command": "dotnet test Monitoring.slnx --filter FullyQualifiedName~HostStartupTests",
        "exit_code": 0,
        "discovered": 1,
        "passed": 1,
        "failed": 0
      },
      "red": {
        "command": "dotnet test Monitoring.slnx --filter FullyQualifiedName~IngestionHostTests",
        "exit_code": 1,
        "observed": "La ruta mínima respondió HTTP 501 a los seis escenarios, por lo que fallaron las aserciones de estado HTTP.",
        "discovered": 6,
        "passed": 0,
        "failed": 6
      },
      "green": {
        "command": "dotnet test Monitoring.slnx --filter FullyQualifiedName~IngestionHostTests",
        "exit_code": 0,
        "discovered": 7,
        "passed": 7,
        "failed": 0
      },
      "triangulation": "Los escenarios distinguen cuerpo inválido/sobredimensionado, identidad ausente, ámbito discrepante, aceptación, writer no configurado y claims de cliente ignorados.",
      "refactor": "not-needed"
    },
    {
      "tasks": ["2.2"],
      "test_file": "tests/Monitoring.Tests/InboxSchemaTests.cs",
      "test_name": "Monitoring.Tests.InboxSchemaTests.IncrementalMigrationAddsInboxSchemaWithCompositeUniquenessAndTemporalIndex",
      "layer": "integration",
      "safety_net": {"command":"dotnet test Monitoring.slnx --no-restore --filter FullyQualifiedName~MigrationTests","exit_code":0,"discovered":1,"passed":1,"failed":0},
      "red": {"command":"dotnet test Monitoring.slnx --no-restore --filter FullyQualifiedName~InboxSchemaTests","exit_code":1,"observed":"Desde el historial limpio S01, --migrate no añadió la segunda migración: historial esperado 2, observado 1.","discovered":1,"passed":0,"failed":1},
      "green": {"command":"dotnet test Monitoring.slnx --no-restore --filter FullyQualifiedName~InboxSchemaTests","exit_code":0,"discovered":1,"passed":1,"failed":0},
      "triangulation": "La misma clave eventId rechaza la segunda fila dentro del mismo origen y permite una fila en otra sede; también se consulta tipo JSONB e índice por origen/accepted_at.",
      "refactor": "not-needed"
    },
    {
      "tasks": ["2.1", "2.3", "2.4", "2.5"],
      "test_file": "tests/Monitoring.Tests/IngestionPersistenceTests.cs",
      "test_name": "Monitoring.Tests.IngestionPersistenceTests",
      "layer": "integration",
      "safety_net": {"command":"dotnet test Monitoring.slnx --no-restore --filter 'FullyQualifiedName~IngestionHostTests|FullyQualifiedName~HostStartupTests'","exit_code":0,"discovered":8,"passed":8,"failed":0},
      "red": {"command":"dotnet test Monitoring.slnx --filter FullyQualifiedName~IngestionPersistenceTests","exit_code":1,"observed":"Compilación fallida antes de InboxWriter: CS0246 InboxWriteResult no encontrado.","discovered":0,"passed":0,"failed":0},
      "green": {"command":"dotnet test Monitoring.slnx --no-restore --filter FullyQualifiedName~IngestionPersistenceTests","exit_code":0,"discovered":4,"passed":4,"failed":0},
      "triangulation": "PostgreSQL confirma ACK tras commit, rollback sin ACK/filas, igualdad JSONB sin ordenar propiedades, conflicto al reordenar arrays, aislamiento por origen y dos envíos concurrentes con una sola fila.",
      "refactor": "not-needed"
    },
    {
      "tasks": ["3.3"],
      "test_file": "tests/Monitoring.Tests/IngestionHostTests.cs",
      "test_name": "Monitoring.Tests.IngestionHostTests.RejectedRequestsIncrementOnceWithTrustedIdentityAndExcludeInternalErrors",
      "layer": "integration",
      "safety_net": {
        "command": "dotnet test Monitoring.slnx --no-restore --filter 'FullyQualifiedName~IngestionHostTests|FullyQualifiedName~IngestionPersistenceTests'",
        "exit_code": 0,
        "discovered": 16,
        "passed": 16,
        "failed": 0
      },
      "red": {
        "command": "dotnet test Monitoring.slnx --no-restore --filter FullyQualifiedName~IngestionHostTests",
        "exit_code": 1,
        "observed": "El nuevo test falló al compilar porque aún no existía IngestionRejectionMetrics (CS0246).",
        "discovered": 0,
        "passed": 0,
        "failed": 0
      },
      "green": {
        "command": "dotnet test Monitoring.slnx --no-restore --filter FullyQualifiedName~IngestionHostTests",
        "exit_code": 0,
        "discovered": 8,
        "passed": 8,
        "failed": 0
      },
      "triangulation": "Cuatro solicitudes 400/403/409/429 producen una medida cada una con solo site.id y sensor.id de identidad confiable; 401 produce una medida sin etiquetas y el 500 interno no añade otra.",
      "refactor": "not-needed"
    },
    {
      "tasks": ["3.4"],
      "test_file": "tests/Monitoring.Tests/IngestionPersistenceTests.cs",
      "test_name": "Monitoring.Tests.IngestionPersistenceTests.LostResponseAfterCommitCanBeRetriedWithEmptyAckAndOneStoredRow",
      "layer": "integration",
      "safety_net": {
        "command": "dotnet test Monitoring.slnx --no-restore --filter 'FullyQualifiedName~IngestionHostTests|FullyQualifiedName~IngestionPersistenceTests'",
        "exit_code": 0,
        "discovered": 16,
        "passed": 16,
        "failed": 0
      },
      "red": {
        "command": "dotnet test Monitoring.slnx --no-restore --filter FullyQualifiedName~IngestionPersistenceTests",
        "exit_code": 0,
        "observed": "No se observó RED: los recorridos HTTP existentes ya cumplían la idempotencia y el rechazo atómico; estas pruebas de regresión pasaron en su primera ejecución.",
        "discovered": 11,
        "passed": 11,
        "failed": 0
      },
      "green": {
        "command": "dotnet test Monitoring.slnx --no-restore --filter FullyQualifiedName~IngestionPersistenceTests",
        "exit_code": 0,
        "discovered": 11,
        "passed": 11,
        "failed": 0
      },
      "triangulation": "El cliente descarta la primera respuesta HTTP ya completada y verifica una fila antes del retry; el retry idéntico responde 200 vacío y conserva una fila. El lote HTTP conflictivo conserva el contenido previo y no inserta su evento nuevo.",
      "refactor": "not-needed"
    }
  ],
  "functional_snapshot": [
    {
      "path": "src/Monitoring.Domain/Ingestion/BatchContract.cs",
      "sha256": "5AB1B520BCDEE32A261897ECF7433AADF414DC3CD7C700031CA578F11C67B5F0"
    },
    {
      "path": "tests/Monitoring.Tests/IngestionContractTests.cs",
      "sha256": "57D535F1EEC3FA5812120438C54C6A4FBABF90B8655193189A26C4A790EC1B97"
    },
    {"path":"src/Monitoring.Host/Program.cs","sha256":"0D86EE95A70660244D3C4A076F0E9604CBA0657E8ACB5219870FCF6618AD5615"},
    {"path":"src/Monitoring.Host/Ingestion/BatchEndpoint.cs","sha256":"58B3BE05D2CB58D28CFDBF1DB6400EA51D5DD45198D01ABB032F0463418DE65A"},
    {"path":"src/Monitoring.Host/Ingestion/TrustedSensorIdentity.cs","sha256":"465F4E39B32FAD00F1987B65B7D0D41F2312F45E8FC6C49322042C20203FD628"},
    {"path":"tests/Monitoring.Tests/IngestionHostTests.cs","sha256":"AEF2291AD2794682FDAD6D1C7B8589A4496A317881773F8C5C7F7831867A2D48"},
    {"path":"src/Monitoring.Persistence/MonitoringDbContext.cs","sha256":"E36C31B196FFEC01D9928F554F0D7171DA8355BB2955A76E4ED48E16CDF4B780"},
    {"path":"src/Monitoring.Persistence/Ingestion/IngestionOriginEntity.cs","sha256":"3A1E80A1209135B2EC97C01C0B4DEE6E5AEE7ACD4DCCA1453D8ADA664BFA0BFB"},
    {"path":"src/Monitoring.Persistence/Ingestion/IngestionInboxEntity.cs","sha256":"E0A293D14CEFFA52B9E6D2D1F6E4DE5247000B6B644EAC1127617820C9581B0A"},
    {"path":"src/Monitoring.Persistence/Ingestion/IngestionOriginEntityConfiguration.cs","sha256":"C705BFF80C715E8D190994AF493CBBE587084645F439FA21B348069DE1C90E7E"},
    {"path":"src/Monitoring.Persistence/Ingestion/IngestionInboxEntityConfiguration.cs","sha256":"292FB4F45571C51705C0CB24F461D97239FC39A2B342099760B8981CCE805B73"},
    {"path":"src/Monitoring.Persistence/Migrations/202609240002_DurableInbox.cs","sha256":"74940E6C04139513484DE88A964F4D2EE7F52FC539D8E1A3FCC47E12EED58CF3"},
    {"path":"tests/Monitoring.Tests/InboxSchemaTests.cs","sha256":"93D4BC89E6471C2AB61C6E906BAB6C96F8C9C6700485A98E6D0F3208AB077B9A"},
    {"path":"tests/Monitoring.Tests/MigrationTests.cs","sha256":"FF3F9AEE40A1723A8F44F6BD2CAEA66719D8ABA8386402996B6D92F52E6B97DA"},
    {"path":"src/Monitoring.Persistence/Ingestion/InboxWriter.cs","sha256":"3DDAE8A1F074300F017871467A2BAE7703778AC643D22E28DF6A55364D323F8E"},
    {"path":"tests/Monitoring.Tests/IngestionPersistenceTests.cs","sha256":"9D93D904275C3EF69486DAE9CBE1C6634465FFDE0FD6F70237D9DD935C12424C"},
    {"path":"src/Monitoring.Persistence/Monitoring.Persistence.csproj","sha256":"CE3B380AA87722919DBBB37FC1C6CF1827FF0EB459913D95311BD42A4366007D"}
  ],
  "full_verification": [
    {
      "command": "dotnet test Monitoring.slnx --filter FullyQualifiedName~IngestionContractTests",
      "exit_code": 0,
      "passed": 29,
      "failed": 0
    },
    {
      "command": "dotnet build Monitoring.slnx --no-restore",
      "exit_code": 0,
      "warnings": 0,
      "errors": 0
    },
    {"command":"dotnet test Monitoring.slnx --filter FullyQualifiedName~IngestionHostTests","exit_code":0,"passed":7,"failed":0},
    {"command":"dotnet test Monitoring.slnx --filter FullyQualifiedName~HostStartupTests","exit_code":0,"passed":1,"failed":0},
    {"command":"dotnet test Monitoring.slnx --no-restore --filter FullyQualifiedName~InboxSchemaTests|FullyQualifiedName~MigrationTests|FullyQualifiedName~MigrationFailureTests","exit_code":0,"passed":3,"failed":0},
    {"command":"dotnet build Monitoring.slnx --no-restore","exit_code":0,"warnings":0,"errors":0},
    {"command":"dotnet test Monitoring.slnx --no-restore --filter 'FullyQualifiedName~IngestionHostTests|FullyQualifiedName~IngestionPersistenceTests'","exit_code":0,"passed":19,"failed":0},
    {"command":"dotnet test Monitoring.slnx","exit_code":0,"passed":53,"failed":0},
    {"command":"dotnet build Monitoring.slnx --no-restore","exit_code":0,"warnings":0,"errors":0}
  ]
}
```

## Phase 2a — PR #2: esquema inbox PostgreSQL

**Delivery:** auto-chain, feature-branch-chain. **Branch:** `feat/s02-inbox-schema`. **Base:** host PR #10.

### Progreso y TDD

- [x] 2.2 Migración incremental, mapeos de origen/bandeja y verificación de forma, unicidad compuesta e índice temporal.
- [ ] 2.1 Aceptación, rollback, ACK, reenvío y conflicto del writer quedan para la siguiente porción de PR #2.

| Task | Test / layer | Safety net | RED | GREEN | Triangulate | Refactor |
|---|---|---|---|---|---|---|
| 2.2 | `InboxSchemaTests` / PostgreSQL | `MigrationTests`: 1 aprobado | historial S01 sin migración nueva; 1 fallo | 1 aprobado | duplicate composite key falla; otra sede pasa; columnas, JSONB e índice consultados | No requerido |

### Verificación y archivos

`InboxSchemaTests` + migraciones: 3 aprobados; `dotnet build Monitoring.slnx --no-restore`: 0 advertencias, 0 errores.

Archivos: `MonitoringDbContext.cs`, `IngestionOriginEntity.cs`, `IngestionInboxEntity.cs`, `IngestionOriginEntityConfiguration.cs`, `IngestionInboxEntityConfiguration.cs`, `202609240002_DurableInbox.cs`, `InboxSchemaTests.cs`, `MigrationTests.cs`, `tasks.md`.

## Phase 2b — PR #2: writer durable e idempotencia

**Delivery:** auto-chain, feature-branch-chain. **Branch:** `feat/s02-inbox-writer`. **Base:** esquema inbox PR #11.

### Progreso y TDD

- [x] 2.1, 2.3, 2.4 y 2.5: writer transaccional, ACK posterior al commit, rollback sin confirmación, idempotencia JSONB, conflicto 409, aislamiento y serialización concurrente.

| Tasks | Test / layer | Safety net | RED | GREEN | Triangulate | Refactor |
|---|---|---|---|---|---|---|
| 2.1 / 2.3 / 2.4 / 2.5 | `IngestionPersistenceTests` / PostgreSQL + HTTP | 8/8 host tests | API `InboxWriteResult` ausente; compilación fallida | 4/4 aprobados | Objetos reordenados idempotentes; arrays reordenados conflictivos; rollback, origen y concurrencia | No requerido |

### Verificación y archivos

`dotnet test Monitoring.slnx --no-restore --filter FullyQualifiedName~IngestionPersistenceTests`: 4 aprobados. `dotnet build Monitoring.slnx --no-restore`: 0 advertencias, 0 errores.

Archivos: `InboxWriter.cs`, `Monitoring.Persistence.csproj`, `Program.cs`, `IngestionPersistenceTests.cs`, `tasks.md`.

### Límite de esta unidad

No incluye cuota ni métricas; quedan asignadas a la Phase 3 / PR #3.

## Phase 3a — PR #3: cuota móvil por origen

**Delivery:** auto-chain, feature-branch-chain. **Branch:** `feat/s02-origin-quota`. **Scope:** tasks 3.1–3.2; métricas e integración posterior quedan para la siguiente porción.

### Progreso y TDD

- [x] 3.1 / 3.2: cuota exacta de 500 eventos nuevos confirmados por origen en los últimos 60 segundos; reenvíos idénticos no consumen cuota; lotes por encima del remanente se rechazan enteros y la admisión concurrente no supera el límite.

| Tasks | Test / layer | Safety net | RED | GREEN | Triangulate | Refactor |
|---|---|---|---|---|---|---|
| 3.1 / 3.2 | `IngestionPersistenceTests` / PostgreSQL + HTTP | 4/4 pruebas existentes aprobadas antes del cambio | Filtro focal: exit 1; 9 descubiertas, 6 aprobadas y 3 fallidas. Sin cuota se aceptaron los excesos, ambas solicitudes concurrentes y HTTP 200 en vez de 429. | Filtro focal: exit 0; 9/9 aprobadas. Build: exit 0, 0 advertencias, 0 errores. | Casos de límite 499→500, lote mixto rechazado sin inserción, reenvío con cuota llena, origen distinto, expiración a 61 s y dos admisiones concurrentes. | No requerido; la consulta usa el índice existente por origen y `accepted_at`. |

### Archivos

| Archivo | Cambio |
|---|---|
| `src/Monitoring.Persistence/Ingestion/InboxWriter.cs` | Añade `RateLimited`, cuenta aceptaciones recientes por origen y revierte el lote si excede 500. |
| `src/Monitoring.Host/Program.cs` | Propaga el resultado de cuota al estado HTTP 429; necesario porque el adaptador anterior convertía resultados desconocidos a 500. |
| `tests/Monitoring.Tests/IngestionPersistenceTests.cs` | Añade pruebas PostgreSQL de cuota, ventana móvil, atomicidad, aislamiento, reenvío, concurrencia y HTTP 429. |
| `openspec/changes/s02-contrato-ingestion-durable/tasks.md` | Marca 3.1 y 3.2 completas tras verificación local. |

### Verificación

- `dotnet test Monitoring.slnx --no-restore --filter FullyQualifiedName~IngestionPersistenceTests` — RED: exit 1; 9 descubiertas, 6 aprobadas y 3 fallidas. GREEN: exit 0; 9 aprobadas, 0 fallidas.
- `dotnet build Monitoring.slnx --no-restore` — exit 0; 0 advertencias, 0 errores.

### Límites de esta unidad

No añade métricas de rechazo ni los escenarios integrales 3.3–3.5. El índice `(site_id, sensor_id, accepted_at)` de la migración previa se reutiliza; no requiere esquema nuevo.

## Phase 3b — PR #3: señales de rechazo y aceptación integral

**Delivery:** auto-chain, feature-branch-chain. **Branch:** `feat/s02-rejection-signals`. **Base:** cuota PR #14. **Scope:** tasks 3.3–3.5.

### Progreso y TDD

- [x] 3.3 Un contador `System.Diagnostics.Metrics` incrementa una vez por respuesta 400/403/409/429. Usa etiquetas solo desde la identidad confiable; 401 registra una medida agregada sin etiquetas y 500 no incrementa.
- [x] 3.4 Pruebas HTTP + PostgreSQL cubren la pérdida de la primera respuesta después del commit y su reintento idéntico, además de un lote con conflicto y evento nuevo sin inserción parcial.
- [x] 3.5 Revisados identidad, límites y atomicidad; pruebas focales, suite completa y build pasan. No se requiere refactor adicional ni cambio de esquema.

| Task | Test / layer | Safety net | RED | GREEN | Triangulate | Refactor |
|---|---|---|---|---|---|---|
| 3.3 | `IngestionHostTests` / integración HTTP | 16/16 host + persistencia | Exit 1: CS0246, faltaba `IngestionRejectionMetrics` | 8/8 host tests | 400/403/409/429 con etiquetas confiables; 401 sin etiquetas; 500 sin incremento | No requerido |
| 3.4 | `IngestionPersistenceTests` / HTTP + PostgreSQL | 16/16 host + persistencia | Sin RED observado: el comportamiento ya existía en el código base; las pruebas de regresión pasaron al primer intento | 11/11 persistencia | Se confirma la fila antes del retry; respuesta 200 vacía y una fila después; 409 conserva evento previo y no guarda el nuevo | No requerido |
| 3.5 | Suite completa / PostgreSQL desechable | N/A | N/A — verificación final | 53/53 pruebas y build sin advertencias | Migraciones y escenarios de persistencia se ejecutan sobre bases vacías por fixture | No hacía falta |

### Verificación

- `dotnet test Monitoring.slnx --no-restore --filter "FullyQualifiedName~IngestionHostTests|FullyQualifiedName~IngestionPersistenceTests"` — exit 0; 19 aprobadas.
- `dotnet test Monitoring.slnx` — exit 0; 53 aprobadas, 0 fallidas. Testcontainers levantó PostgreSQL 18 y las pruebas de ingestión migraron bases vacías.
- `dotnet build Monitoring.slnx --no-restore` — exit 0; 0 advertencias, 0 errores.
- `git diff --check` — sin errores de whitespace.

### Archivos

| Archivo | Cambio |
|---|---|
| `src/Monitoring.Host/Ingestion/BatchEndpoint.cs` | Emite contadores de rechazo y limita las etiquetas a la identidad confiable. |
| `src/Monitoring.Host/Program.cs` | Registra el contador como singleton del host. |
| `tests/Monitoring.Tests/IngestionHostTests.cs` | Verifica incrementos por estado, etiquetas confiables, 401 agregado y exclusión de 500. |
| `tests/Monitoring.Tests/IngestionPersistenceTests.cs` | Verifica pérdida de respuesta tras commit, retry idempotente y conflicto mixto por HTTP. |
| `openspec/changes/s02-contrato-ingestion-durable/tasks.md` | Marca 3.3–3.5 completas tras la verificación. |

### Límite de esta unidad

El test simula pérdida en el cliente descartando la respuesta HTTP ya completada por el servidor; valida la fila antes del reintento. Estas pruebas no miden capacidad de producción ni resuelven S17.
