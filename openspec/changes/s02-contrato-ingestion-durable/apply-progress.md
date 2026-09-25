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
    }
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
    }
  ]
}
```
