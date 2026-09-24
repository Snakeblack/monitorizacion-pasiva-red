# Apply Progress: S01 — base ejecutable

## Batch 1 — PR 1: runner, solución y frontera de dominio

**Delivery strategy:** auto-chain (`feature-branch-chain`). PR 1 is based on the S01 feature branch `feat/s01-base-ejecutable` and ends with a runnable .NET solution, xUnit runner, and an empty domain assembly that proves its dependency boundary.

### Completed tasks

- [x] 1.1 Created the .NET 10 SDK pin, solution, and shared build properties. `dotnet --version` returned `10.0.303`; `dotnet restore Monitoring.slnx` succeeded.
- [x] 1.2 Added the xUnit test project and wrote the domain dependency test before adding the domain project. The focal run found one test and failed because `Monitoring.Domain.dll` was absent.
- [x] 1.3 Registered `dotnet test Monitoring.slnx` and xUnit unit-test capability after the runner executed the failing test. The final full test command passed.
- [x] 2.1 Added an empty `Monitoring.Domain` project without persistence dependencies. The focal architecture test passed.

### TDD Cycle Evidence

| Task | Test file | Layer | Safety net | RED | GREEN | Triangulate | Refactor | Notes |
|---|---|---|---|---|---|---|---|---|
| 1.1 | — | Runner bootstrap | N/A (new solution) | N/A (runner setup) | `dotnet --version` = `10.0.303`; restore succeeded | N/A | N/A | Tooling had to exist before executable tests could be authored. |
| 1.2 / 2.1 | `tests/Monitoring.Tests/DomainDependencyTests.cs` | Unit / architecture | N/A (new test) | `dotnet test Monitoring.slnx --filter FullyQualifiedName~DomainDependency` exited 1; one test failed because the domain assembly was absent | Same focal command exited 0; 1 passed, 0 failed after adding the domain project | Not needed (one boundary scenario) | Not needed | Test reflects on the built domain assembly and rejects Persistence, EF Core, and Npgsql references. |
| 1.3 | `tests/Monitoring.Tests/DomainDependencyTests.cs` | Unit / runner | N/A (configuration) | Runner discovery and execution were demonstrated by the RED run above | `dotnet test Monitoring.slnx` exited 0; 1 passed, 0 failed | N/A | N/A | Runner metadata was recorded only after xUnit executed the architecture test. |

### Verification

- `dotnet restore Monitoring.slnx` — passed.
- `dotnet build Monitoring.slnx --no-restore` — passed with 0 warnings and 0 errors.
- `dotnet test Monitoring.slnx --filter FullyQualifiedName~DomainDependency` — RED before the domain project; GREEN after it was added (1 passed).
- `dotnet test Monitoring.slnx` — passed (1 passed, 0 failed).

### Files changed

| File | Change |
|---|---|
| `global.json` | Pins SDK 10.0.303 with latest feature-band roll-forward. |
| `Monitoring.slnx` | Adds the test and domain projects to the solution. |
| `Directory.Build.props` | Shares net10.0, nullable, and implicit-using settings. |
| `tests/Monitoring.Tests/Monitoring.Tests.csproj` | Configures xUnit and conditionally references the domain project so the missing assembly is a test failure during the RED cycle. |
| `tests/Monitoring.Tests/DomainDependencyTests.cs` | Verifies the built domain assembly has no persistence-related references. |
| `src/Monitoring.Domain/Monitoring.Domain.csproj` | Adds the empty domain assembly without persistence dependencies. |
| `openspec/config.yaml` | Records the working xUnit runner and test command. |
| `openspec/changes/s01-base-ejecutable/tasks.md` | Marks tasks 1.1–1.3 and 2.1 complete. |

### Deviations and remaining work

The first slice includes task 2.1's empty domain project so the architecture test is green and the PR is independently buildable. Host, liveness, persistence, migrations, CI, and README remain for later chained slices. No S02 behavior or capacity claim was added.

```json:strict-tdd-evidence
{
  "schema_version": 1,
  "cycles": [
    {
      "tasks": ["1.2", "2.1"],
      "test_file": "tests/Monitoring.Tests/DomainDependencyTests.cs",
      "test_name": "Monitoring.Tests.DomainDependencyTests.DomainProjectDoesNotReferencePersistence",
      "layer": "unit",
      "red": {
        "command": "dotnet test Monitoring.slnx --filter FullyQualifiedName~DomainDependency",
        "exit_code": 1,
        "observed": "The domain assembly must be built and referenced by the test project.",
        "discovered": 1,
        "passed": 0,
        "failed": 1
      },
      "green": {
        "command": "dotnet test Monitoring.slnx --filter FullyQualifiedName~DomainDependency",
        "exit_code": 0,
        "discovered": 1,
        "passed": 1,
        "failed": 0
      },
      "triangulation": "not-needed; one architecture boundary scenario",
      "refactor": "not-needed"
    },
    {
      "tasks": ["2.2", "2.3"],
      "test_file": "tests/Monitoring.Tests/HostStartupTests.cs",
      "test_name": "Monitoring.Tests.HostStartupTests.HostStartsWithoutPersistenceAndReportsLiveness",
      "layer": "integration",
      "red": {
        "command": "dotnet test Monitoring.slnx --filter FullyQualifiedName~HostStartup",
        "exit_code": 1,
        "observed": "GET /health/live returned 404 before the endpoint was mapped.",
        "discovered": 1,
        "passed": 0,
        "failed": 1
      },
      "green": {
        "command": "dotnet test Monitoring.slnx --filter FullyQualifiedName~HostStartup",
        "exit_code": 0,
        "discovered": 1,
        "passed": 1,
        "failed": 0
      },
      "triangulation": "not-needed; one startup/liveness scenario",
      "refactor": "not-needed"
    }
  ],
  "functional_snapshot": [
    {
      "path": "src/Monitoring.Domain/Monitoring.Domain.csproj",
      "sha256": "3E1A9F612F8CFCC2E2A8B2EF085685F9F3D1A55C4B901E8D88AF6EC37D283195"
    },
    {
      "path": "src/Monitoring.Host/Monitoring.Host.csproj",
      "sha256": "E40BA49EDC06CF43276A87AE3568DE3BF9D6094F4F3DBFCD9536AFE89CD8A600"
    },
    {
      "path": "src/Monitoring.Host/Program.cs",
      "sha256": "10DF04C957CA375D94477575AC8596B3A1FAB7B0DAF297FBFE4AA66A6A6A656B"
    },
    {
      "path": "tests/Monitoring.Tests/Monitoring.Tests.csproj",
      "sha256": "616AAD4CA7FC06A6BDE49B7D139438F7A414081A35B52DBD34D718FA70BAD733"
    },
    {
      "path": "tests/Monitoring.Tests/DomainDependencyTests.cs",
      "sha256": "27FC7999188285CEB99454200B2BD0EFFCE699963D4B27CBBD32463E4C0343BA"
    },
    {
      "path": "tests/Monitoring.Tests/HostStartupTests.cs",
      "sha256": "3F5FCE27DA403B624C9CD47237AA696F3554666E346455B0C68230733D87F7DF"
    }
  ],
  "full_verification": [
    {"command": "dotnet restore Monitoring.slnx", "exit_code": 0},
    {"command": "dotnet build Monitoring.slnx --no-restore", "exit_code": 0, "warnings": 0, "errors": 0},
    {"command": "dotnet test Monitoring.slnx", "exit_code": 0, "passed": 1, "failed": 0},
    {"command": "dotnet test Monitoring.slnx --filter FullyQualifiedName~HostStartup", "exit_code": 0, "passed": 1, "failed": 0},
    {"command": "dotnet test Monitoring.slnx", "exit_code": 0, "passed": 2, "failed": 0}
  ]
}
```

## Batch 2 — PR 2: host mínimo y liveness

**Delivery strategy:** auto-chain (`feature-branch-chain`). Esta slice se apoya en la PR 1 y termina con el host .NET 10 ejecutable y `GET /health/live`, sin conexión ni dependencia de persistencia.

### Completed tasks

- [x] 2.2 Added `HostStartupTests` first; the focal test returned 404 before the liveness route existed.
- [x] 2.3 Added `Monitoring.Host`, referenced the domain assembly, and mapped only `GET /health/live` to HTTP 200.

### TDD Cycle Evidence

| Task | Test file | Layer | Safety net | RED | GREEN | Triangulate | Refactor | Notes |
|---|---|---|---|---|---|---|---|---|
| 2.2 / 2.3 | `tests/Monitoring.Tests/HostStartupTests.cs` | Integration / ASP.NET TestServer | Existing suite: 1 passed | `dotnet test Monitoring.slnx --filter FullyQualifiedName~HostStartup` exited 1; expected 200, got 404 | Same focal command exited 0; 1 passed, 0 failed; full suite 2 passed | Not needed (single startup/liveness scenario) | Not needed | Host starts without a connection string and reports liveness through its HTTP pipeline. |

### Files changed

| File | Change |
|---|---|
| `Monitoring.slnx` | Registers `Monitoring.Host`. |
| `src/Monitoring.Host/Monitoring.Host.csproj` | Adds the executable ASP.NET Core host and domain reference. |
| `src/Monitoring.Host/Program.cs` | Composes the minimal host and maps only `/health/live`. |
| `tests/Monitoring.Tests/Monitoring.Tests.csproj` | Adds the host project and ASP.NET Core testing package. |
| `tests/Monitoring.Tests/HostStartupTests.cs` | Verifies host startup and liveness without persistence configuration. |
| `openspec/changes/s01-base-ejecutable/tasks.md` | Marks tasks 2.2 and 2.3 complete. |

### Verification

- `dotnet test Monitoring.slnx --filter FullyQualifiedName~HostStartup` — RED before mapping the route (404), then GREEN (1 passed).
- `dotnet test Monitoring.slnx` — passed (2 passed, 0 failed).

### Deviations and remaining work

None — implementation follows the design. Persistence/migration, CI, and README remain for later chained slices; no S02 route or behavior was added.
