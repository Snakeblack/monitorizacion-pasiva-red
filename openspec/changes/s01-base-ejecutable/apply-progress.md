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
    },
    {
      "tasks": ["3.1", "3.2", "3.4"],
      "test_file": "tests/Monitoring.Tests/MigrationTests.cs",
      "test_name": "Monitoring.Tests.MigrationTests.MigrateCreatesOnlyInitialSchemaAndCanBeRepeated",
      "layer": "integration",
      "red": {
        "command": "dotnet test Monitoring.slnx --filter FullyQualifiedName~MigrationTests",
        "exit_code": 1,
        "observed": "The old host ignored --migrate and remained running until the 15-second process timeout; the expected successful migration command was absent.",
        "discovered": 1,
        "passed": 0,
        "failed": 1
      },
      "green": {
        "command": "dotnet test Monitoring.slnx --filter FullyQualifiedName~MigrationTests",
        "exit_code": 0,
        "discovered": 1,
        "passed": 1,
        "failed": 0
      },
      "triangulation": "The test verifies a fresh database, first migration, repeated migration, public history, and no functional tables; unavailable PostgreSQL is covered separately.",
      "refactor": "not-needed"
    },
    {
      "tasks": ["3.3"],
      "test_file": "tests/Monitoring.Tests/MigrationFailureTests.cs",
      "test_name": "Monitoring.Tests.MigrationFailureTests.MigrateReportsUnavailableDatabaseWithoutPrintingCredentials",
      "layer": "integration",
      "red": {
        "command": "dotnet test Monitoring.slnx --filter FullyQualifiedName~MigrationFailure",
        "exit_code": 1,
        "observed": "The old host did not report a migration failure when --migrate targeted a closed local port.",
        "discovered": 1,
        "passed": 0,
        "failed": 1
      },
      "green": {
        "command": "dotnet test Monitoring.slnx --filter FullyQualifiedName~MigrationFailure",
        "exit_code": 0,
        "discovered": 1,
        "passed": 1,
        "failed": 0
      },
      "triangulation": "The closed-port failure path is distinct from the successful disposable-PostgreSQL path.",
      "refactor": "not-needed"
    },
    {
      "tasks": [
        "4.1",
        "4.2"
      ],
      "test_file": "C:\\Users\\sn4ke\\AppData\\Local\\Temp\\s01-slice4-acceptance.ps1",
      "test_name": "S01CiReadmeAndCapabilitiesAcceptance",
      "layer": "static",
      "red": {
        "command": "C:\\Users\\sn4ke\\AppData\\Local\\Temp\\s01-slice4-acceptance.ps1",
        "exit_code": 1,
        "observed": "Before implementation, the workflow and README were absent and four expected config commands/capabilities were missing.",
        "discovered": 6,
        "passed": 0,
        "failed": 6
      },
      "green": {
        "command": "C:\\Users\\sn4ke\\AppData\\Local\\Temp\\s01-slice4-acceptance.ps1",
        "exit_code": 0,
        "discovered": 19,
        "passed": 19,
        "failed": 0
      },
      "triangulation": "One static contract checks the workflow, setup steps, developer commands, provisional database notice, and recorded capabilities.",
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
      "sha256": "A5984114D009A0C73D2EB31D677E197CFD7653C3671BE95B0569D318671B9993"
    },
    {
      "path": "src/Monitoring.Host/Program.cs",
      "sha256": "41E59F8EAFE4B849AF3A11AF1807B54BBA2B195085F82A2A100AF38B0D800BD5"
    },
    {
      "path": "tests/Monitoring.Tests/Monitoring.Tests.csproj",
      "sha256": "2B5CAC192D47F53CCB8510379350E48F4454237C04659860AA3BD2A3528D65B7"
    },
    {
      "path": "tests/Monitoring.Tests/DomainDependencyTests.cs",
      "sha256": "27FC7999188285CEB99454200B2BD0EFFCE699963D4B27CBBD32463E4C0343BA"
    },
    {
      "path": "tests/Monitoring.Tests/HostStartupTests.cs",
      "sha256": "3F5FCE27DA403B624C9CD47237AA696F3554666E346455B0C68230733D87F7DF"
    },
    {
      "path": "src/Monitoring.Persistence/Monitoring.Persistence.csproj",
      "sha256": "04B77ECC3240781979FADA82AF57DF4830806EC6A6FEA0E0B27CF62F0F43F349"
    },
    {
      "path": "src/Monitoring.Persistence/MonitoringDbContext.cs",
      "sha256": "19918FA595F03F79B5AC0AA7A45871A16C71F422CE087D6D44CCF15F27DFBCD6"
    },
    {
      "path": "src/Monitoring.Persistence/Migrations/202609240001_InitialSchema.cs",
      "sha256": "B24E2E45C9F95D39C046A0E80F720F85342A059A60DE3A5314A61B6F6473A12D"
    },
    {
      "path": "tests/Monitoring.Tests/PostgresFixture.cs",
      "sha256": "DBC161101B74D21EB424DCE355B5F5636102F29D6D24F0D16592A4841942F8F1"
    },
    {
      "path": "tests/Monitoring.Tests/MigrationTests.cs",
      "sha256": "DF2224C849B895B5C20F1BF02FBC0F27FA91F9E23F8319020335015570910A17"
    },
    {
      "path": "tests/Monitoring.Tests/MigrationFailureTests.cs",
      "sha256": "01D0F113896D534FE719F3FBE430B71D0BDF535B35899F43110634CBE78087D4"
    }
  ],
  "full_verification": [
    {"command": "dotnet restore Monitoring.slnx", "exit_code": 0},
    {"command": "dotnet build Monitoring.slnx --no-restore", "exit_code": 0, "warnings": 0, "errors": 0},
    {"command": "dotnet test Monitoring.slnx", "exit_code": 0, "passed": 1, "failed": 0},
    {"command": "dotnet test Monitoring.slnx --filter FullyQualifiedName~HostStartup", "exit_code": 0, "passed": 1, "failed": 0},
    {"command": "dotnet test Monitoring.slnx", "exit_code": 0, "passed": 2, "failed": 0},
    {"command": "dotnet test Monitoring.slnx --filter FullyQualifiedName~MigrationTests", "exit_code": 0, "passed": 1, "failed": 0},
    {"command": "dotnet test Monitoring.slnx --filter FullyQualifiedName~MigrationFailure", "exit_code": 0, "passed": 1, "failed": 0},
    {"command": "dotnet test Monitoring.slnx", "exit_code": 0, "passed": 4, "failed": 0},
    {"command": "C:\\Users\\sn4ke\\AppData\\Local\\Temp\\s01-slice4-acceptance.ps1", "exit_code": 0, "passed": 19, "failed": 0},
    {"command": "dotnet clean Monitoring.slnx", "exit_code": 0},
    {"command": "dotnet restore Monitoring.slnx", "exit_code": 0},
    {"command": "dotnet build Monitoring.slnx --no-restore", "exit_code": 0, "warnings": 0, "errors": 0},
    {"command": "dotnet test Monitoring.slnx --no-build", "exit_code": 0, "passed": 4, "failed": 0},
    {"command": "python YAML parse of .github/workflows/ci.yml and openspec/config.yaml", "exit_code": 0}
,
    {"command": "dotnet run --project src/Monitoring.Host -- --urls http://localhost:5080", "observed": "Host listened on http://localhost:5080; no database connection was required."},
    {"command": "curl.exe --fail http://localhost:5080/health/live", "exit_code": 0, "observed": "GET /health/live returned HTTP 200."}
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

## Batch 3 — PR 3: persistencia PostgreSQL y migración explícita

**Delivery strategy:** auto-chain (`feature-branch-chain`). Esta slice se apoya en PR 2 y termina con persistencia EF Core/Npgsql, una migración S01 explícita y pruebas reales con PostgreSQL desechable. CI y README quedan para PR 4.

### Completed tasks

- [x] 3.1 Added Testcontainers PostgreSQL and an isolated disposable database fixture.
- [x] 3.2 Added red/green integration coverage for an empty database, repeated migration, schema/history creation, and no functional tables.
- [x] 3.3 Added red/green closed-port failure coverage; the command exits nonzero, emits a visible error, and does not print the configured password.
- [x] 3.4 Added `Monitoring.Persistence`, the empty `monitoring` schema migration, and the explicit `--migrate` command. Normal host startup remains independent of PostgreSQL.

### TDD Cycle Evidence

| Task | Test file | Layer | Safety Net | RED | GREEN | Triangulate | Refactor | Notes / Rationale |
|---|---|---|---|---|---|---|---|---|
| 3.1 / 3.2 / 3.4 | `tests/Monitoring.Tests/MigrationTests.cs` | Integration / Testcontainers PostgreSQL | 2/2 existing tests passed | `dotnet test Monitoring.slnx --filter FullyQualifiedName~MigrationTests` exited 1; `--migrate` timed out before the command existed | Same focal command exited 0; 1 passed; full suite 4 passed | Closed-port failure covered by separate test | None needed | Confirms first and repeated migration on a newly created database, public history, and an empty `monitoring` schema. |
| 3.3 | `tests/Monitoring.Tests/MigrationFailureTests.cs` | Integration / local closed TCP port | 2/2 existing tests passed | `dotnet test Monitoring.slnx --filter FullyQualifiedName~MigrationFailure` exited 1; no visible migration error | Same focal command exited 0; 1 passed; full suite 4 passed | Distinct failure path from Testcontainers success path | None needed | Verifies nonzero exit and secret-free diagnostic with a two-second connection timeout. |

### Files changed

| File | Change |
|---|---|
| `Monitoring.slnx` | Registers `Monitoring.Persistence`. |
| `src/Monitoring.Persistence/Monitoring.Persistence.csproj` | Adds EF Core 10 and the PostgreSQL provider. |
| `src/Monitoring.Persistence/MonitoringDbContext.cs` | Adds the persistence context with no speculative S02 entities. |
| `src/Monitoring.Persistence/Migrations/202609240001_InitialSchema.cs` | Creates only the `monitoring` schema. EF stores migration history in `public`. |
| `src/Monitoring.Host/Monitoring.Host.csproj` | References the persistence module. |
| `src/Monitoring.Host/Program.cs` | Runs migrations only for `--migrate`; reports sanitized failure and exits nonzero. |
| `tests/Monitoring.Tests/Monitoring.Tests.csproj` | Adds Npgsql and Testcontainers PostgreSQL. |
| `tests/Monitoring.Tests/PostgresFixture.cs` | Starts a disposable PostgreSQL container and creates isolated empty databases. |
| `tests/Monitoring.Tests/MigrationTests.cs` | Verifies first/repeated migration and the absence of functional tables. |
| `tests/Monitoring.Tests/MigrationFailureTests.cs` | Verifies closed-port failure and credential redaction. |
| `openspec/changes/s01-base-ejecutable/tasks.md` | Marks tasks 3.1–3.4 complete. |

### Verification

- `dotnet test Monitoring.slnx --filter FullyQualifiedName~MigrationTests` — RED before `--migrate` implementation, then GREEN (1 passed) against Testcontainers PostgreSQL.
- `dotnet test Monitoring.slnx --filter FullyQualifiedName~MigrationFailure` — RED before implementation, then GREEN (1 passed).
- `dotnet test Monitoring.slnx` — passed (4 passed, 0 failed), including the PostgreSQL container test.
- `git diff --check` — passed.

### Deviations and remaining work

None — implementation matches the design. PR 4 still owns CI and README tasks 4.1–4.3.


## Batch 4 — PR 4: CI y documentación reproducible

**Delivery strategy:** auto-chain (`feature-branch-chain`). Esta slice parte de la PR 3 e incorpora CI limpia y documentación de los comandos S01.

### Completed tasks

- [x] 4.1 Added `.github/workflows/ci.yml` with GitHub-hosted Ubuntu, the SDK pinned by `global.json`, and restore/build/test steps. Static acceptance checks confirm a failing command is not ignored; workflow YAML parsed successfully.
- [x] 4.2 Documented .NET/Docker prerequisites, local verification commands, explicit migration, liveness, cleanup, and PostgreSQL's provisional status. Updated verified OpenSpec commands and capabilities.
- [x] 4.3 Ran clean, restore, build, and the full test suite after the documentation and workflow changes.

### TDD Cycle Evidence

| Task | Test file | Layer | Safety net | RED | GREEN | Triangulate | Refactor | Notes |
|---|---|---|---|---|---|---|---|---|
| 4.1 / 4.2 | Temporary `s01-slice4-acceptance.ps1` | Static acceptance | Existing suite: 4 passed | Script exited 1 with 6 missing-contract assertions before implementation | Script exited 0; 19 assertions passed | Checks cover workflow, README, and config contracts | Not needed | The temporary assertion script stayed outside the repository; the real GitHub-hosted job has not run in this local environment. |
| 4.3 | `tests/Monitoring.Tests/**` | Integration | 4/4 passed before changes | N/A — verification task | `dotnet test Monitoring.slnx --no-build`: 4 passed | Includes disposable PostgreSQL migration tests | Not needed | Repeated from a `dotnet clean` state after restore and build. |

### Files changed

| File | Change |
|---|---|
| `.github/workflows/ci.yml` | Adds clean .NET restore/build/test workflow; Docker-backed integration tests use Testcontainers. |
| `README.md` | Documents prerequisites, commands, liveness, explicit migration, cleanup, and PostgreSQL's provisional status. |
| `openspec/config.yaml` | Records verified commands, xUnit/Testcontainers integration capability, and relevant stack capabilities. |
| `openspec/changes/s01-base-ejecutable/tasks.md` | Marks tasks 4.1–4.3 complete. |
| `openspec/changes/s01-base-ejecutable/apply-progress.md` | Merges this batch and its TDD/verification evidence with prior batches. |

### Verification

- `dotnet clean Monitoring.slnx` — passed.
- `dotnet restore Monitoring.slnx` — passed.
- `dotnet build Monitoring.slnx --no-restore` — passed, 0 warnings and 0 errors.
- `dotnet test Monitoring.slnx --no-build` — passed, 4 tests including Testcontainers PostgreSQL.
- Temporary static acceptance script — RED before changes (6 missing contract checks), GREEN after changes (19 checks passed).
- Python YAML parse of `.github/workflows/ci.yml` and `openspec/config.yaml` — passed.
- Documented `dotnet run` liveness command and `curl.exe --fail http://localhost:5080/health/live` — passed; HTTP 200.
- `git diff --check` — passed; Git reports only the repository's LF/CRLF normalization notice for `openspec/config.yaml`.

### Deviations and remaining work

The GitHub-hosted workflow itself cannot be dispatched from this local apply run, and `actionlint` is not installed. YAML parsing and contract checks passed locally; the hosted workflow result will be observable on the PR. PostgreSQL remains provisional and no production-capacity claim was added.
