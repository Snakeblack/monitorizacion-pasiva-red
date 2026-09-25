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
    {"path":"src/Monitoring.Host/Program.cs","sha256":"9C699F8F6C6DA350288D6019019A38D185FCFC061BADB258D6A52E3C3C664304"},
    {"path":"src/Monitoring.Host/Ingestion/BatchEndpoint.cs","sha256":"94CAF0A90C8B84146EE4FD8A17D9AC051CF03E0375BDC1F705067223ABCB79B5"},
    {"path":"src/Monitoring.Host/Ingestion/TrustedSensorIdentity.cs","sha256":"465F4E39B32FAD00F1987B65B7D0D41F2312F45E8FC6C49322042C20203FD628"},
    {"path":"tests/Monitoring.Tests/IngestionHostTests.cs","sha256":"B30756B366E7115E8A216D387889F80228CD75E7D46FDF967B8C0B727F2056F7"}
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
    {"command":"dotnet test Monitoring.slnx --filter FullyQualifiedName~HostStartupTests","exit_code":0,"passed":1,"failed":0}
  ]
}
```
