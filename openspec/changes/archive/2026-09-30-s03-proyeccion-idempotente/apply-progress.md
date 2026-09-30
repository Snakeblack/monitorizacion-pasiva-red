# Apply progress — S03

Strict TDD; delivery single-pr with approved `size:exception` (s03-size-exception-001). No commits or publication in this phase.

## Baseline and environment

Initial baseline: 40 passed / 14 infrastructure failures (DockerUnavailableException), not RED. Recovered installed Docker Desktop by preserving stale runtime socket directories under AppData/Local; no images, volumes, settings or secrets changed. Repeated baseline: 54/54 passed with PostgreSQL 18, `dotnet test Monitoring.slnx --no-restore --logger trx;LogFileName=s03-baseline.trx`.

## TDD Cycle Evidence

| Tasks | Test file / layer | Safety net | RED | GREEN | REFACTOR |
|---|---|---|---|---|---|
| [x] 1.1, 1.2 | SyntheticSessionContractTests.cs / unit | 54/54 baseline | Stub returns false; 4 valid session assertions fail, 34 invalid cases pass (`s03-u1-red.trx`) | 38/38 (`s03-u1-green.trx`) | Helpers for typed values; no separate refactor required |

Evidence reports live under `tests/Monitoring.Tests/TestResults/` (ignored, not committed). Commands use `dotnet test Monitoring.slnx --no-restore --filter FullyQualifiedName~<class> --logger trx;LogFileName=<report>`.

## Batch U1

Parser and Domain DTO added. Exactly nine fields, duplicate property rejection, protocol/IP/port/timestamp checks and cloned original values. No EF dependency. Tests cover IPv4/IPv6, TCP/UDP, limits and 0–3 decimal UTC instants plus all field omissions/types and invalid boundaries.

## Batch U2

[x] 1.3/1.4: S02-to-S03 upgrade with an existing arbitrary pending event, repeated migration, composite uniqueness/FK/delete restriction, partial pending index and preserved quota index. RED: existing S02 has 2 migrations vs expected 3 (`s03-u2-red.trx`, `s03-u2-schema-red.trx`). GREEN: 3/3 (`s03-u2-green.trx`). Refactor: none required. Additive migration preserves data on code rollback and rejects destructive Down. Test fixture uses explicit public history table like --migrate; no production S02 changes.

## Batch U3

[x] 2.1/2.2: exact pending-row lock with SKIP LOCKED, recognized data validation, insert/JSONB+timestamp comparison and processing mark in one transaction. RED: 3 behavioral failures (`s03-u3-red.trx`: 0 vs 3 sessions, missing invariant exception, missing marking-trigger exception). GREEN: 3/3 (`s03-u3-green.trx`). Rollback verified from independent connections and recovery with a newly constructed context; equal replay and shared IDs across site/sensor preserve identity. Readers disposed before writes. No separate refactor necessary.

## Batch U4

[x] 2.3/2.4: bounded upper key and exclusive cursor evaluated by PostgreSQL, pages of 100, advancing across invalid/skipped entries and reset each pass. RED 1/5 failure: 105 invalid/unknown entries starve 103 valid rows (0 vs 103, `s03-u4-red.trx`). GREEN 5/5 (`s03-u4-green.trx`) includes independent concurrent contexts, locked event skipped and recovered next pass, equal timestamp full-composite boundaries. Refactor introduced ReadPage/ReadKey/AddCursor helpers; final focal suite stays green.

## Batch U5

[x] 3.1/3.2: parameterized EF triple-identity read with detached JSON DTO; separate internal read feature/provider; Development/Testing endpoint guard before lookup; fallback reader without persistence. RED 6/6 behavioral failures (404 vs 200/401, missing feature, `s03-u5-red.trx`); GREEN 6/6 (`s03-u5-green.trx`). Tests assert exactly five fields, original occurredAt text, independent same-ID site/sensor contents, foreign/absent 404, headers/query ignored, sensor-only 401 and Production/Unknown 401 even after provider DI replacement. Refactor: none needed.

## Batch U6

[x] 4.1/4.2: worker scoped per pass, one-second wait, exponential transient backoff capped at 30 seconds, cancellation rolls back/disposes, fatal exceptions/logs sanitized before StopHost. Worker RED 3/3 behavioral waits fail against completed-task stub (`s03-u6-worker-red.trx`); GREEN 3/3 (`s03-u6-worker-green.trx`). Real PostgreSQL transient 40001 trigger, cancellation during observed PgSleep and fatal P0001 trigger prove recovery, fresh/disposed context and safe logs. No fake DB atomicity.

[x] 4.3: real host registers scoped projector and hosted worker only with connection, --migrate exits without consuming pending synthetic event, liveness without persistence, controlled ACK before projection, arbitrary pending data and identical re-send at saturated 500-event quota remain accepted without extra quota. Wiring RED 1/9: hosted worker absent (`s03-u6-wiring-red.trx`); GREEN 12/12 host/worker/startup (`s03-u6-green.trx`). Real pre-wiring-GREEN hashes recorded in evidence/wiring-red-snapshot.json; no reconstructed earlier snapshots.

[~] 4.4: README/roadmap updated for S01–S03 and remaining dev/test boundary; final regression/build running. No additional refactor required after helpers introduced in U4.

## Evidence authenticity limitation

All RED/GREEN executions above are real behavioral test runs and TRX reports retain outcomes. They were launched directly with exec_command, not the runtime receipt runner. Earlier production/test pre-write snapshots and runtime receipt stdout/stderr were not captured; they will not be fabricated. The schema-v1 record below uses historical working-tree provenance, explicitly legacy-unverifiable, with real report references and current functional hashes. This is not runtime-authenticated live evidence; verification must assess that limitation. Docker/bootstrap failures are excluded from RED.

## Final verification

[x] 4.4 and all 14 tasks verified. Full regression: `dotnet test Monitoring.slnx --no-restore --logger trx;LogFileName=s03-final.trx` — 109 passed, 0 failed, 0 skipped, 19 seconds; includes original 54 S02/S01 tests. `dotnet build Monitoring.slnx --no-restore` — exit 0, 0 warnings, 0 errors. No commits/PR created. Real TRX outcome summaries, timestamps and raw digests preserved in evidence/test-runs.json; full raw TRX remains ignored under test output folder. Functional manifest excludes OpenSpec bookkeeping and records current LF-normalized SHA256s.

```json:strict-tdd-evidence
{
  "schema_version": 1,
  "evidence_mode": "historical",
  "change": "s03-proyeccion-idempotente",
  "functional_snapshot": {
    "projection": "strict-tdd-functional-v1",
    "base_tree": "3740f9c91169c6d82527f011214a922489fc3895",
    "genesis_paths": [
      "README.md",
      "docs/roadmap.md",
      "src/Monitoring.Domain/Sessions/SessionDetail.cs",
      "src/Monitoring.Domain/Sessions/SyntheticSessionContract.cs",
      "src/Monitoring.Host/Program.cs",
      "src/Monitoring.Host/Sessions/SessionEndpoint.cs",
      "src/Monitoring.Host/Sessions/SessionProjectionWorker.cs",
      "src/Monitoring.Host/Sessions/TrustedSessionReadContext.cs",
      "src/Monitoring.Persistence/Ingestion/IngestionInboxEntity.cs",
      "src/Monitoring.Persistence/Ingestion/IngestionInboxEntityConfiguration.cs",
      "src/Monitoring.Persistence/Migrations/202609290003_SessionProjection.cs",
      "src/Monitoring.Persistence/MonitoringDbContext.cs",
      "src/Monitoring.Persistence/Sessions/SessionProjectionEntity.cs",
      "src/Monitoring.Persistence/Sessions/SessionProjectionEntityConfiguration.cs",
      "src/Monitoring.Persistence/Sessions/SessionProjector.cs",
      "src/Monitoring.Persistence/Sessions/SessionReader.cs",
      "tests/Monitoring.Tests/HostStartupTests.cs",
      "tests/Monitoring.Tests/InboxSchemaTests.cs",
      "tests/Monitoring.Tests/MigrationTests.cs",
      "tests/Monitoring.Tests/SessionHostTests.cs",
      "tests/Monitoring.Tests/SessionProjectionTests.cs",
      "tests/Monitoring.Tests/SessionSchemaTests.cs",
      "tests/Monitoring.Tests/SessionWorkerTests.cs",
      "tests/Monitoring.Tests/SyntheticSessionContractTests.cs"
    ],
    "files": [
      {
        "path": "README.md",
        "digest": "sha256:329fa348cc54fd85e0369c35409f1c1624ee0e4d807fa3757e6eab8c22de2147"
      },
      {
        "path": "docs/roadmap.md",
        "digest": "sha256:d8380ee5197a7d1e1b7a91565125652e839cdf9e37f58fe0407587d40db19919"
      },
      {
        "path": "src/Monitoring.Domain/Sessions/SessionDetail.cs",
        "digest": "sha256:22013bb713db40accbce6e9fd25c00e8ef9aeb8f1ddfbe070b35e74fe36a41c7"
      },
      {
        "path": "src/Monitoring.Domain/Sessions/SyntheticSessionContract.cs",
        "digest": "sha256:1243f7a3cec1c081a35923f90afa6e8054f5b4c3e028ac2ac1d5d4aee784c537"
      },
      {
        "path": "src/Monitoring.Host/Program.cs",
        "digest": "sha256:5f30cc832f8c90352ac3d09be804c2ea727f14c3dac396742ecdad6c40677321"
      },
      {
        "path": "src/Monitoring.Host/Sessions/SessionEndpoint.cs",
        "digest": "sha256:307c8f46d081c2ea1fffdf5870efc15111d7175ca666d1b9231ffc70150d22bc"
      },
      {
        "path": "src/Monitoring.Host/Sessions/SessionProjectionWorker.cs",
        "digest": "sha256:bed8ebdb612a7aaecb4e2d06048336e05a79278530f398d78889b3001f383233"
      },
      {
        "path": "src/Monitoring.Host/Sessions/TrustedSessionReadContext.cs",
        "digest": "sha256:e58c7a4bd8ab68b7887c4f4c7487a078386e05ac0e2b43f0c88484638da89dd9"
      },
      {
        "path": "src/Monitoring.Persistence/Ingestion/IngestionInboxEntity.cs",
        "digest": "sha256:2b9631d8558c09ea01606b818bd199027fe1d172f1d8d6c9e77ee273b3cb118d"
      },
      {
        "path": "src/Monitoring.Persistence/Ingestion/IngestionInboxEntityConfiguration.cs",
        "digest": "sha256:89f5725d0b0447040d07438ca60d31f5bd1215becd7336e3dea3e9c43ab5479a"
      },
      {
        "path": "src/Monitoring.Persistence/Migrations/202609290003_SessionProjection.cs",
        "digest": "sha256:6391d0557156e8a4d2c746525b34ad0fab56db46ef84828972ca5061347e28de"
      },
      {
        "path": "src/Monitoring.Persistence/MonitoringDbContext.cs",
        "digest": "sha256:01899128a884cb2ec770050504440381101a60f4d357de7e922dc3dbbcbe2677"
      },
      {
        "path": "src/Monitoring.Persistence/Sessions/SessionProjectionEntity.cs",
        "digest": "sha256:5f914dd463bc3371c13ebda740c5d714ad3620f1edf3e20693a8885300cc4840"
      },
      {
        "path": "src/Monitoring.Persistence/Sessions/SessionProjectionEntityConfiguration.cs",
        "digest": "sha256:d5ba52493e78dc08829d0304b65ee5e7da552e6ddc60d96383889b0565319607"
      },
      {
        "path": "src/Monitoring.Persistence/Sessions/SessionProjector.cs",
        "digest": "sha256:30e4b77af4050da635aa3b8d673e0a61d62377093705460d242230eb623ab5a4"
      },
      {
        "path": "src/Monitoring.Persistence/Sessions/SessionReader.cs",
        "digest": "sha256:839eba9e4c817675bda5046ff9cdbb86dad1fc9654a21dc8ba4f8f0b511f08a1"
      },
      {
        "path": "tests/Monitoring.Tests/HostStartupTests.cs",
        "digest": "sha256:bde50686de17c681c4bba7462374fd37adad903db8f59911b78ddd443415c7fa"
      },
      {
        "path": "tests/Monitoring.Tests/InboxSchemaTests.cs",
        "digest": "sha256:a8c50d47b7e2ec0973cf9705e51833847a7414a04b628bc614c9aed6d42e1c81"
      },
      {
        "path": "tests/Monitoring.Tests/MigrationTests.cs",
        "digest": "sha256:702129ddbc3f5976f0db31110ef5775f8b887b35f137e1ab3f5e808d4c637f20"
      },
      {
        "path": "tests/Monitoring.Tests/SessionHostTests.cs",
        "digest": "sha256:00c8a4ea4314f0a8ab030e7e8f22bfade29ca3f12e7c618c028c71f28d74663c"
      },
      {
        "path": "tests/Monitoring.Tests/SessionProjectionTests.cs",
        "digest": "sha256:e77ec697599acbe0d5f36d2ec7e950b4eb190512036dfd4e2d6d8ee9c4e8edb4"
      },
      {
        "path": "tests/Monitoring.Tests/SessionSchemaTests.cs",
        "digest": "sha256:a81883a843578ab7e6decdd6fa54643da88ed3dbeeeae093cf412626ca66543c"
      },
      {
        "path": "tests/Monitoring.Tests/SessionWorkerTests.cs",
        "digest": "sha256:431331e21e9cfe7b522bf71f725a08336cbad9da8d9d76231993c8c4b325fe01"
      },
      {
        "path": "tests/Monitoring.Tests/SyntheticSessionContractTests.cs",
        "digest": "sha256:9c7430fd7dd56ffd09930fa49638da787e79728d36079b8ad4e1b97a6ea92f44"
      }
    ]
  },
  "cycles": [
    {
      "task": "1.1",
      "test_file": "tests/Monitoring.Tests/SyntheticSessionContractTests.cs",
      "red": "written",
      "green": "passed",
      "triangulate": "passed",
      "refactor": "passed",
      "provenance": {
        "source": "working-tree",
        "commit": "working-tree",
        "test_file": "tests/Monitoring.Tests/SyntheticSessionContractTests.cs",
        "test_digest": "sha256:9c7430fd7dd56ffd09930fa49638da787e79728d36079b8ad4e1b97a6ea92f44",
        "command": "dotnet test Monitoring.slnx --no-restore --filter FullyQualifiedName~SyntheticSessionContractTests",
        "red_report": "tests/Monitoring.Tests/TestResults/s03-u1-red.trx",
        "green_report": "tests/Monitoring.Tests/TestResults/s03-u1-green.trx",
        "report_summary": "openspec/changes/s03-proyeccion-idempotente/evidence/test-runs.json",
        "limitation": "Historical executed TRX outcomes; no runtime receipts or pre-write snapshots claimed. Current test digest, not an earlier captured digest."
      }
    },
    {
      "task": "1.2",
      "test_file": "tests/Monitoring.Tests/SyntheticSessionContractTests.cs",
      "red": "written",
      "green": "passed",
      "triangulate": "passed",
      "refactor": "passed",
      "provenance": {
        "source": "working-tree",
        "commit": "working-tree",
        "test_file": "tests/Monitoring.Tests/SyntheticSessionContractTests.cs",
        "test_digest": "sha256:9c7430fd7dd56ffd09930fa49638da787e79728d36079b8ad4e1b97a6ea92f44",
        "command": "dotnet test Monitoring.slnx --no-restore --filter FullyQualifiedName~SyntheticSessionContractTests",
        "red_report": "tests/Monitoring.Tests/TestResults/s03-u1-red.trx",
        "green_report": "tests/Monitoring.Tests/TestResults/s03-u1-green.trx",
        "report_summary": "openspec/changes/s03-proyeccion-idempotente/evidence/test-runs.json",
        "limitation": "Historical executed TRX outcomes; no runtime receipts or pre-write snapshots claimed. Current test digest, not an earlier captured digest."
      }
    },
    {
      "task": "1.3",
      "test_file": "tests/Monitoring.Tests/SessionSchemaTests.cs",
      "red": "written",
      "green": "passed",
      "triangulate": "passed",
      "refactor": "passed",
      "provenance": {
        "source": "working-tree",
        "commit": "working-tree",
        "test_file": "tests/Monitoring.Tests/SessionSchemaTests.cs",
        "test_digest": "sha256:a81883a843578ab7e6decdd6fa54643da88ed3dbeeeae093cf412626ca66543c",
        "command": "dotnet test Monitoring.slnx --no-restore --filter FullyQualifiedName~SessionSchemaTests",
        "red_report": "tests/Monitoring.Tests/TestResults/s03-u2-schema-red.trx",
        "green_report": "tests/Monitoring.Tests/TestResults/s03-u2-green.trx",
        "report_summary": "openspec/changes/s03-proyeccion-idempotente/evidence/test-runs.json",
        "limitation": "Historical executed TRX outcomes; no runtime receipts or pre-write snapshots claimed. Current test digest, not an earlier captured digest."
      }
    },
    {
      "task": "1.4",
      "test_file": "tests/Monitoring.Tests/SessionSchemaTests.cs",
      "red": "written",
      "green": "passed",
      "triangulate": "passed",
      "refactor": "passed",
      "provenance": {
        "source": "working-tree",
        "commit": "working-tree",
        "test_file": "tests/Monitoring.Tests/SessionSchemaTests.cs",
        "test_digest": "sha256:a81883a843578ab7e6decdd6fa54643da88ed3dbeeeae093cf412626ca66543c",
        "command": "dotnet test Monitoring.slnx --no-restore --filter FullyQualifiedName~SessionSchemaTests",
        "red_report": "tests/Monitoring.Tests/TestResults/s03-u2-schema-red.trx",
        "green_report": "tests/Monitoring.Tests/TestResults/s03-u2-green.trx",
        "report_summary": "openspec/changes/s03-proyeccion-idempotente/evidence/test-runs.json",
        "limitation": "Historical executed TRX outcomes; no runtime receipts or pre-write snapshots claimed. Current test digest, not an earlier captured digest."
      }
    },
    {
      "task": "2.1",
      "test_file": "tests/Monitoring.Tests/SessionProjectionTests.cs",
      "red": "written",
      "green": "passed",
      "triangulate": "passed",
      "refactor": "passed",
      "provenance": {
        "source": "working-tree",
        "commit": "working-tree",
        "test_file": "tests/Monitoring.Tests/SessionProjectionTests.cs",
        "test_digest": "sha256:e77ec697599acbe0d5f36d2ec7e950b4eb190512036dfd4e2d6d8ee9c4e8edb4",
        "command": "dotnet test Monitoring.slnx --no-restore --filter FullyQualifiedName~SessionProjectionTests",
        "red_report": "tests/Monitoring.Tests/TestResults/s03-u3-red.trx",
        "green_report": "tests/Monitoring.Tests/TestResults/s03-u3-green.trx",
        "report_summary": "openspec/changes/s03-proyeccion-idempotente/evidence/test-runs.json",
        "limitation": "Historical executed TRX outcomes; no runtime receipts or pre-write snapshots claimed. Current test digest, not an earlier captured digest."
      }
    },
    {
      "task": "2.2",
      "test_file": "tests/Monitoring.Tests/SessionProjectionTests.cs",
      "red": "written",
      "green": "passed",
      "triangulate": "passed",
      "refactor": "passed",
      "provenance": {
        "source": "working-tree",
        "commit": "working-tree",
        "test_file": "tests/Monitoring.Tests/SessionProjectionTests.cs",
        "test_digest": "sha256:e77ec697599acbe0d5f36d2ec7e950b4eb190512036dfd4e2d6d8ee9c4e8edb4",
        "command": "dotnet test Monitoring.slnx --no-restore --filter FullyQualifiedName~SessionProjectionTests",
        "red_report": "tests/Monitoring.Tests/TestResults/s03-u3-red.trx",
        "green_report": "tests/Monitoring.Tests/TestResults/s03-u3-green.trx",
        "report_summary": "openspec/changes/s03-proyeccion-idempotente/evidence/test-runs.json",
        "limitation": "Historical executed TRX outcomes; no runtime receipts or pre-write snapshots claimed. Current test digest, not an earlier captured digest."
      }
    },
    {
      "task": "2.3",
      "test_file": "tests/Monitoring.Tests/SessionProjectionTests.cs",
      "red": "written",
      "green": "passed",
      "triangulate": "passed",
      "refactor": "passed",
      "provenance": {
        "source": "working-tree",
        "commit": "working-tree",
        "test_file": "tests/Monitoring.Tests/SessionProjectionTests.cs",
        "test_digest": "sha256:e77ec697599acbe0d5f36d2ec7e950b4eb190512036dfd4e2d6d8ee9c4e8edb4",
        "command": "dotnet test Monitoring.slnx --no-restore --filter FullyQualifiedName~SessionProjectionTests",
        "red_report": "tests/Monitoring.Tests/TestResults/s03-u4-red.trx",
        "green_report": "tests/Monitoring.Tests/TestResults/s03-u4-green.trx",
        "report_summary": "openspec/changes/s03-proyeccion-idempotente/evidence/test-runs.json",
        "limitation": "Historical executed TRX outcomes; no runtime receipts or pre-write snapshots claimed. Current test digest, not an earlier captured digest."
      }
    },
    {
      "task": "2.4",
      "test_file": "tests/Monitoring.Tests/SessionProjectionTests.cs",
      "red": "written",
      "green": "passed",
      "triangulate": "passed",
      "refactor": "passed",
      "provenance": {
        "source": "working-tree",
        "commit": "working-tree",
        "test_file": "tests/Monitoring.Tests/SessionProjectionTests.cs",
        "test_digest": "sha256:e77ec697599acbe0d5f36d2ec7e950b4eb190512036dfd4e2d6d8ee9c4e8edb4",
        "command": "dotnet test Monitoring.slnx --no-restore --filter FullyQualifiedName~SessionProjectionTests",
        "red_report": "tests/Monitoring.Tests/TestResults/s03-u4-red.trx",
        "green_report": "tests/Monitoring.Tests/TestResults/s03-u4-green.trx",
        "report_summary": "openspec/changes/s03-proyeccion-idempotente/evidence/test-runs.json",
        "limitation": "Historical executed TRX outcomes; no runtime receipts or pre-write snapshots claimed. Current test digest, not an earlier captured digest."
      }
    },
    {
      "task": "3.1",
      "test_file": "tests/Monitoring.Tests/SessionHostTests.cs",
      "red": "written",
      "green": "passed",
      "triangulate": "passed",
      "refactor": "passed",
      "provenance": {
        "source": "working-tree",
        "commit": "working-tree",
        "test_file": "tests/Monitoring.Tests/SessionHostTests.cs",
        "test_digest": "sha256:00c8a4ea4314f0a8ab030e7e8f22bfade29ca3f12e7c618c028c71f28d74663c",
        "command": "dotnet test Monitoring.slnx --no-restore --filter FullyQualifiedName~SessionHostTests",
        "red_report": "tests/Monitoring.Tests/TestResults/s03-u5-red.trx",
        "green_report": "tests/Monitoring.Tests/TestResults/s03-u5-green.trx",
        "report_summary": "openspec/changes/s03-proyeccion-idempotente/evidence/test-runs.json",
        "limitation": "Historical executed TRX outcomes; no runtime receipts or pre-write snapshots claimed. Current test digest, not an earlier captured digest."
      }
    },
    {
      "task": "3.2",
      "test_file": "tests/Monitoring.Tests/SessionHostTests.cs",
      "red": "written",
      "green": "passed",
      "triangulate": "passed",
      "refactor": "passed",
      "provenance": {
        "source": "working-tree",
        "commit": "working-tree",
        "test_file": "tests/Monitoring.Tests/SessionHostTests.cs",
        "test_digest": "sha256:00c8a4ea4314f0a8ab030e7e8f22bfade29ca3f12e7c618c028c71f28d74663c",
        "command": "dotnet test Monitoring.slnx --no-restore --filter FullyQualifiedName~SessionHostTests",
        "red_report": "tests/Monitoring.Tests/TestResults/s03-u5-red.trx",
        "green_report": "tests/Monitoring.Tests/TestResults/s03-u5-green.trx",
        "report_summary": "openspec/changes/s03-proyeccion-idempotente/evidence/test-runs.json",
        "limitation": "Historical executed TRX outcomes; no runtime receipts or pre-write snapshots claimed. Current test digest, not an earlier captured digest."
      }
    },
    {
      "task": "4.1",
      "test_file": "tests/Monitoring.Tests/SessionWorkerTests.cs",
      "red": "written",
      "green": "passed",
      "triangulate": "passed",
      "refactor": "passed",
      "provenance": {
        "source": "working-tree",
        "commit": "working-tree",
        "test_file": "tests/Monitoring.Tests/SessionWorkerTests.cs",
        "test_digest": "sha256:431331e21e9cfe7b522bf71f725a08336cbad9da8d9d76231993c8c4b325fe01",
        "command": "dotnet test Monitoring.slnx --no-restore --filter FullyQualifiedName~SessionWorkerTests",
        "red_report": "tests/Monitoring.Tests/TestResults/s03-u6-worker-red.trx",
        "green_report": "tests/Monitoring.Tests/TestResults/s03-u6-worker-green.trx",
        "report_summary": "openspec/changes/s03-proyeccion-idempotente/evidence/test-runs.json",
        "limitation": "Historical executed TRX outcomes; no runtime receipts or pre-write snapshots claimed. Current test digest, not an earlier captured digest."
      }
    },
    {
      "task": "4.2",
      "test_file": "tests/Monitoring.Tests/SessionWorkerTests.cs",
      "red": "written",
      "green": "passed",
      "triangulate": "passed",
      "refactor": "passed",
      "provenance": {
        "source": "working-tree",
        "commit": "working-tree",
        "test_file": "tests/Monitoring.Tests/SessionWorkerTests.cs",
        "test_digest": "sha256:431331e21e9cfe7b522bf71f725a08336cbad9da8d9d76231993c8c4b325fe01",
        "command": "dotnet test Monitoring.slnx --no-restore --filter FullyQualifiedName~SessionWorkerTests",
        "red_report": "tests/Monitoring.Tests/TestResults/s03-u6-worker-red.trx",
        "green_report": "tests/Monitoring.Tests/TestResults/s03-u6-worker-green.trx",
        "report_summary": "openspec/changes/s03-proyeccion-idempotente/evidence/test-runs.json",
        "limitation": "Historical executed TRX outcomes; no runtime receipts or pre-write snapshots claimed. Current test digest, not an earlier captured digest."
      }
    },
    {
      "task": "4.3",
      "test_file": "tests/Monitoring.Tests/SessionHostTests.cs",
      "red": "written",
      "green": "passed",
      "triangulate": "passed",
      "refactor": "passed",
      "provenance": {
        "source": "working-tree",
        "commit": "working-tree",
        "test_file": "tests/Monitoring.Tests/SessionHostTests.cs",
        "test_digest": "sha256:00c8a4ea4314f0a8ab030e7e8f22bfade29ca3f12e7c618c028c71f28d74663c",
        "command": "dotnet test Monitoring.slnx --no-restore --filter FullyQualifiedName~SessionHostTests",
        "red_report": "tests/Monitoring.Tests/TestResults/s03-u6-wiring-red.trx",
        "green_report": "tests/Monitoring.Tests/TestResults/s03-u6-green.trx",
        "report_summary": "openspec/changes/s03-proyeccion-idempotente/evidence/test-runs.json",
        "limitation": "Historical executed TRX outcomes; no runtime receipts or pre-write snapshots claimed. Current test digest, not an earlier captured digest."
      }
    },
    {
      "task": "4.4",
      "test_file": "tests/Monitoring.Tests/SessionHostTests.cs",
      "red": "written",
      "green": "passed",
      "triangulate": "passed",
      "refactor": "passed",
      "provenance": {
        "source": "working-tree",
        "commit": "working-tree",
        "test_file": "tests/Monitoring.Tests/SessionHostTests.cs",
        "test_digest": "sha256:00c8a4ea4314f0a8ab030e7e8f22bfade29ca3f12e7c618c028c71f28d74663c",
        "command": "dotnet test Monitoring.slnx --no-restore --filter FullyQualifiedName~SessionHostTests",
        "red_report": "tests/Monitoring.Tests/TestResults/s03-u6-wiring-red.trx",
        "green_report": "tests/Monitoring.Tests/TestResults/s03-final.trx",
        "report_summary": "openspec/changes/s03-proyeccion-idempotente/evidence/test-runs.json",
        "limitation": "Historical executed TRX outcomes; no runtime receipts or pre-write snapshots claimed. Current test digest, not an earlier captured digest."
      }
    }
  ]
}
```

## Final Derived Markdown Table

| Task | Test file | RED | GREEN | TRIANGULATE | REFACTOR |
|---|---|---|---|---|---|
| 1.1 | tests/Monitoring.Tests/SyntheticSessionContractTests.cs | written | passed | passed | passed |
| 1.2 | tests/Monitoring.Tests/SyntheticSessionContractTests.cs | written | passed | passed | passed |
| 1.3 | tests/Monitoring.Tests/SessionSchemaTests.cs | written | passed | passed | passed |
| 1.4 | tests/Monitoring.Tests/SessionSchemaTests.cs | written | passed | passed | passed |
| 2.1 | tests/Monitoring.Tests/SessionProjectionTests.cs | written | passed | passed | passed |
| 2.2 | tests/Monitoring.Tests/SessionProjectionTests.cs | written | passed | passed | passed |
| 2.3 | tests/Monitoring.Tests/SessionProjectionTests.cs | written | passed | passed | passed |
| 2.4 | tests/Monitoring.Tests/SessionProjectionTests.cs | written | passed | passed | passed |
| 3.1 | tests/Monitoring.Tests/SessionHostTests.cs | written | passed | passed | passed |
| 3.2 | tests/Monitoring.Tests/SessionHostTests.cs | written | passed | passed | passed |
| 4.1 | tests/Monitoring.Tests/SessionWorkerTests.cs | written | passed | passed | passed |
| 4.2 | tests/Monitoring.Tests/SessionWorkerTests.cs | written | passed | passed | passed |
| 4.3 | tests/Monitoring.Tests/SessionHostTests.cs | written | passed | passed | passed |
| 4.4 | tests/Monitoring.Tests/SessionHostTests.cs | written | passed | passed | passed |

## Review workload actual

Functional code/test/docs changed lines: 1120; 24 functional paths. OpenSpec planning/evidence artifacts add review lines. Single PR with approved size:exception. No >50% functional forecast growth (forecast 1200-1700). Evidence validation: valid, authenticity: legacy-unverifiable. No claim of runtime authenticity.
