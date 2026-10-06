# Apply progress: producto-monitorizacion-final

## Foundation batch U01–U03 — 2026-10-05

Mode: Strict TDD. Delivery: exception-ok / accepted size:exception. No commits/push/PR. Runtime owns state.yaml.

- [x]1.1 / U01: live ADR-015–019 and architecture/product/roadmap/slices/config describe adopted infrastructure. Earlier ADRs and archived S01–S04 unchanged. Documentation-only work adds no executable behavior; local links (0 missing) and git diff --check pass.
- [~]1.2 / U02: real core with pinned image digests/plugins, secrets outside git, migrations/publication bootstrap and healthchecks. PostgreSQL/Kafka/Connect/Elasticsearch/API healthy; migrate/bootstrap exit0. Empty task-owned volumes initialized and PostgreSQL restart preserved canonical schema. Full K04 remains incomplete: source/sink/mapping/alias, web/generator/verifier, Keycloak/EJBCA, TLS/mTLS/ACL and vertical CI are later owned tasks, never credited as complete.
- [x]1.3 / U03: captured and synthetic canonical parsing, unambiguous triple identity/documentKey, additive0004 schema, paged validated historical initialization and session+metadata+identity+marker+outbox transaction. Five-column session_projection and accepted JSON/timestamp text preserved. Concurrent/replay, rollback/restart, different sites, backfill repetition and suppressed-identity replay exercised.

### Executed evidence

- Safety net:45/45 (foundation-safety.trx).
- Behavioral RED: canonical normalization/historical metadata/document key assertions; captured projection expected2 got0; missing canonical tables; corrected historical backfill missing table; normalized timestamp expected2 got0. Retained TRX and logs. Invalid migration-target test setup files are diagnostic failures, not RED credit.
- Focal GREEN:24/24 (foundation-canonical-green.trx), followed by final complete serial regression141/141,0 skipped (foundation-regression-serial.trx/log). New .NET tests:14 unit +4 real PostgreSQL integration.
- Core native integration:1/1 (foundation-stack-green-wsl.log), with both connector plugins discovered, canonical schema and PostgreSQL restart. Foundation-stack-restart.log/health.txt confirms latest build healthy after regression.
- Parallel regression attempts:139/140 exposed the superseded migration-count expectation (updated3→4); a later129/141 and140/141 encountered Ryuk disappearance. These failures are preserved; no GREEN or behavioral RED claimed. No OOM observed in kernel evidence. Serial xUnit execution passed all141 without changing Docker/WSL or disabling test cleanup.
- Docker WSL engine29.8.2, Compose5.6.0, host SDK10.0.303, Node24.16/npm12; core runs on3.8GiB daemon memory. Avoid core and many Testcontainers fixtures simultaneously; tested serial command is documented.

### TDD Cycle Evidence

| Task | Test File | RED | GREEN | TRIANGULATE | REFACTOR |
|------|-----------|-----|-------|-------------|----------|
| 1.2 | tests/stack/foundation.test.mjs | written | passed | passed | passed |
| 1.3 | tests/Monitoring.Tests/CanonicalSessionTests.cs | written | passed | passed | passed |
| 1.3 | tests/Monitoring.Tests/OutboxTests.cs | written | passed | passed | passed |

The schema-v1 block is content-addressed historical evidence. Commands/logs/TRX and actual pre-write snapshots are retained; no runtime-authenticated live receipts are claimed. Sealed test-reference JSON is stored both in canonical change evidence and runtime .ospec/strict-tdd-historical for validation. A clean checkout can restore those exact sealed files to the runtime cache; never recreate historical outputs. Current final test/source digests are clearly distinct from the original pre-write copies.

```json:strict-tdd-evidence
{
  "schema_version": 1,
  "evidence_mode": "historical",
  "change": "producto-monitorizacion-final",
  "functional_snapshot": {
    "projection": "strict-tdd-functional-v1",
    "base_tree": "7ae9181de411b1d249fe08bd6992222ec0951c5e",
    "genesis_paths": [
      ".gitattributes",
      ".gitignore",
      "README.md",
      "compose.yaml",
      "deploy/api/Dockerfile",
      "deploy/api/entrypoint.sh",
      "deploy/api/health.sh",
      "deploy/connect/Dockerfile",
      "deploy/connect/connect.properties",
      "deploy/connect/entrypoint.sh",
      "deploy/connect/health.sh",
      "deploy/connect/sink.json",
      "deploy/connect/source.json",
      "deploy/connect/transforms/src/SessionContract.java",
      "deploy/elasticsearch/sessions-v1.json",
      "deploy/postgres/bootstrap.sh",
      "deploy/postgres/init.sh",
      "deploy/versions.env",
      "docs/architecture/decisions/ADR-015.md",
      "docs/architecture/decisions/ADR-016.md",
      "docs/architecture/decisions/ADR-017.md",
      "docs/architecture/decisions/ADR-018.md",
      "docs/architecture/decisions/ADR-019.md",
      "docs/architecture/decisions/README.md",
      "docs/architecture/flujo-completo.md",
      "docs/architecture/technical-baseline.md",
      "docs/development/slices.md",
      "docs/development/stack.md",
      "docs/product/brief.md",
      "docs/product/functional-scope.md",
      "docs/roadmap-gaps.md",
      "docs/roadmap.md",
      "openspec/config.yaml",
      "scripts/lab/build-transform.ps1",
      "scripts/lab/compose.mjs",
      "scripts/lab/core.ps1",
      "scripts/lab/pipeline.mjs",
      "scripts/lab/prepare.ps1",
      "src/Monitoring.Domain/Sessions/CanonicalSession.cs",
      "src/Monitoring.Domain/Sessions/CapturedSessionContract.cs",
      "src/Monitoring.Domain/Sessions/SessionIdentity.cs",
      "src/Monitoring.Host/Program.cs",
      "src/Monitoring.Persistence/Migrations/202610050004_CanonicalOutbox.cs",
      "src/Monitoring.Persistence/MonitoringDbContext.cs",
      "src/Monitoring.Persistence/Sessions/CanonicalEntities.cs",
      "src/Monitoring.Persistence/Sessions/CanonicalSessionInitializer.cs",
      "src/Monitoring.Persistence/Sessions/OutboxStore.cs",
      "src/Monitoring.Persistence/Sessions/SessionProjector.cs",
      "src/monitoring-web/src/app/app.component.css",
      "src/monitoring-web/src/app/app.component.html",
      "src/monitoring-web/src/app/app.component.spec.ts",
      "src/monitoring-web/src/app/app.component.ts",
      "src/monitoring-web/src/app/app.routes.ts",
      "src/monitoring-web/src/app/session-detail/session-detail-api.ts",
      "src/monitoring-web/src/app/session-detail/session-detail-page.component.css",
      "src/monitoring-web/src/app/session-detail/session-detail-page.component.html",
      "src/monitoring-web/src/app/session-detail/session-detail-page.component.ts",
      "src/monitoring-web/src/app/session-detail/session-detail-scope.spec.ts",
      "src/monitoring-web/src/app/sessions/session-http-error.ts",
      "src/monitoring-web/src/app/sessions/session-query.spec.ts",
      "src/monitoring-web/src/app/sessions/session-query.ts",
      "src/monitoring-web/src/app/sessions/session-search-api.spec.ts",
      "src/monitoring-web/src/app/sessions/session-search-api.ts",
      "src/monitoring-web/src/app/sessions/session-search-page.component.css",
      "src/monitoring-web/src/app/sessions/session-search-page.component.html",
      "src/monitoring-web/src/app/sessions/session-search-page.component.spec.ts",
      "src/monitoring-web/src/app/sessions/session-search-page.component.ts",
      "tests/Monitoring.Tests/CanonicalSessionTests.cs",
      "tests/Monitoring.Tests/InboxSchemaTests.cs",
      "tests/Monitoring.Tests/MigrationTests.cs",
      "tests/Monitoring.Tests/OutboxTests.cs",
      "tests/Monitoring.Tests/SessionSchemaTests.cs",
      "tests/stack/foundation.test.mjs",
      "tests/stack/search-pipeline.test.mjs"
    ],
    "files": [
      {
        "path": ".gitattributes",
        "digest": "sha256:2a66ada2df67a918f811048a8304d99234f9970e9d806a24f3aaf0fed85ca1bd"
      },
      {
        "path": ".gitignore",
        "digest": "sha256:49715794c76b71a3d67697810f3de427216531a47171e2c1ed1a63c7c38b06a8"
      },
      {
        "path": "README.md",
        "digest": "sha256:f0938eca62bb5f8046b9c1eee86c98f26e88f51195f6d34f61e1600d239e5b34"
      },
      {
        "path": "compose.yaml",
        "digest": "sha256:23233356cc7f527a551d489bddf3dbae6027e44088aabf685ef6cc3c38bdecc4"
      },
      {
        "path": "deploy/api/Dockerfile",
        "digest": "sha256:247f83f9c43fbfd528742a05a713812d45ad5c141a7a17a7ad338187076ecfa5"
      },
      {
        "path": "deploy/api/entrypoint.sh",
        "digest": "sha256:bf51a60f4afce051ced2f798f5ac0578c74d3a1d021bac374267189cf718f448"
      },
      {
        "path": "deploy/api/health.sh",
        "digest": "sha256:7a2bc47566be283542ade060ad2016452f814d8adf1b9f5f2b0fc063dd4eb899"
      },
      {
        "path": "deploy/connect/Dockerfile",
        "digest": "sha256:addf1297ac3c284420ae91860d9613ec94922b13b9ce162bd20eb060a5c14998"
      },
      {
        "path": "deploy/connect/connect.properties",
        "digest": "sha256:d8fc2a2d431706fd9b9ba1618f280037b239ff168267dff6ca6a628d41463eb3"
      },
      {
        "path": "deploy/connect/entrypoint.sh",
        "digest": "sha256:8aa609b3981aada2ba1031c87232ec337668a895e4cefe2543bf3ad621cf33d7"
      },
      {
        "path": "deploy/connect/health.sh",
        "digest": "sha256:7333e8b846ba09a4cc81915e4f0c98e150c8c0c76b47b2191322356e059fb197"
      },
      {
        "path": "deploy/connect/sink.json",
        "digest": "sha256:4c0c307d1ea8c18684882423038d064f62ddc9ca47cf3d8ac4046141f08722a4"
      },
      {
        "path": "deploy/connect/source.json",
        "digest": "sha256:2de8884b127538c17031e945364b8bae45ac9942fbb7137626e3305d8b926df3"
      },
      {
        "path": "deploy/connect/transforms/src/SessionContract.java",
        "digest": "sha256:f9b4f444faa994b0bec516ecb701594dfc940c8e76f5c100197cb9645b8288ce"
      },
      {
        "path": "deploy/elasticsearch/sessions-v1.json",
        "digest": "sha256:e10d15e4b275032977cce7cc63e0af4f6d2168c0db9c222eb371c79aa70dace9"
      },
      {
        "path": "deploy/postgres/bootstrap.sh",
        "digest": "sha256:6189e452f37a872f6b0b72df956dcbd264c1b696aa6d7961ea446d63c15463ef"
      },
      {
        "path": "deploy/postgres/init.sh",
        "digest": "sha256:a7a39cb87873adee0f72122ca984e9343c09f1c2dc4b4d76df37bad9f95f0a71"
      },
      {
        "path": "deploy/versions.env",
        "digest": "sha256:195b7b6c592dc0355f0f519c623e88c09d6f38833b979e4fcbe5663146ca036c"
      },
      {
        "path": "docs/architecture/decisions/ADR-015.md",
        "digest": "sha256:3e0e13d9a12f34f5e594b1f3bdf3a485bc89a936ccd4a8bcb3c8be2786abdf1a"
      },
      {
        "path": "docs/architecture/decisions/ADR-016.md",
        "digest": "sha256:be46f15e13fbf361792abcba8f48afe5af969a423e288a374a2e578c5a3b9008"
      },
      {
        "path": "docs/architecture/decisions/ADR-017.md",
        "digest": "sha256:705ed90ec2dfff2b44e0216e7f74232fcf60b65c684523c3cfea347c22a48a5e"
      },
      {
        "path": "docs/architecture/decisions/ADR-018.md",
        "digest": "sha256:6ac0875d2681e3543f709b36e3f74a17f35d18e72fe3895e3768922cab211699"
      },
      {
        "path": "docs/architecture/decisions/ADR-019.md",
        "digest": "sha256:ffe0e557b2acaee3e5c542a0e72fc0dd2998512e80fe430521590394d7934032"
      },
      {
        "path": "docs/architecture/decisions/README.md",
        "digest": "sha256:828e69b13156422698b8f56c4a2effad6b04e3e71369fc837e28635022efad4b"
      },
      {
        "path": "docs/architecture/flujo-completo.md",
        "digest": "sha256:cc04e0b1d5ff5dafcd817e5dc68525394373a4df9090fe285bf4a99b15638b5d"
      },
      {
        "path": "docs/architecture/technical-baseline.md",
        "digest": "sha256:1ee86d6e2861f4796349d012c86cc5a7118d035477543fe5eae2d109782623d2"
      },
      {
        "path": "docs/development/slices.md",
        "digest": "sha256:40a23123769f9959ea480a217be766f772e8f3e3dc8376624d00064b094686a7"
      },
      {
        "path": "docs/development/stack.md",
        "digest": "sha256:fdeaf5f3df187601fb360bb1380c42cb5a2b907381a93c468552d5f5f1ff4e53"
      },
      {
        "path": "docs/product/brief.md",
        "digest": "sha256:4ce43ecc198ffe79b24fdd9634cc6197c7667363455769075187291fd84ad793"
      },
      {
        "path": "docs/product/functional-scope.md",
        "digest": "sha256:57fac4b2467b5ba7ea58686be99fb300d35779b67c0d27a02d4538cf3301a5c2"
      },
      {
        "path": "docs/roadmap-gaps.md",
        "digest": "sha256:7d9952b25b8e038c2b6e8a2fc1a2782077f0354b169432ded66278505bf7c14d"
      },
      {
        "path": "docs/roadmap.md",
        "digest": "sha256:dd8cb28088869cb2c17b344723e3dcd9354fbc44345a95d5a4da9a08a40f782a"
      },
      {
        "path": "openspec/config.yaml",
        "digest": "sha256:5e6ac361de9d48a561da95cb1d0409b1e9a54cb4d78d16105ac1ec34b88664e3"
      },
      {
        "path": "scripts/lab/build-transform.ps1",
        "digest": "sha256:053d692d73ec69a573360fe3489245135308db8fb58dd97057968aa78d29f2a3"
      },
      {
        "path": "scripts/lab/compose.mjs",
        "digest": "sha256:6f8e43ecb6706c465208866d581405b43058bc03036d81b1ee92113620ebe273"
      },
      {
        "path": "scripts/lab/core.ps1",
        "digest": "sha256:b68aaa31eb81167ebe57cb6a4bbda9d62ff979ee535a8a271ae6f06a3cec4828"
      },
      {
        "path": "scripts/lab/pipeline.mjs",
        "digest": "sha256:c41bc270bc28c01234721b53367dfbdb154760c51af833593c9f93eaf9ce0533"
      },
      {
        "path": "scripts/lab/prepare.ps1",
        "digest": "sha256:9c035dc4a656f208b59add16cb12562c4485310e1b42655a417133ac9290cb22"
      },
      {
        "path": "src/Monitoring.Domain/Sessions/CanonicalSession.cs",
        "digest": "sha256:cdcb20bba98e7259ceb938204fff28bae9c886cf420e35f2ec5d6dce89c52395"
      },
      {
        "path": "src/Monitoring.Domain/Sessions/CapturedSessionContract.cs",
        "digest": "sha256:388aa3518f93352d333319b05100dc7a5685aa120093a5d38ccbe8d697a189cc"
      },
      {
        "path": "src/Monitoring.Domain/Sessions/SessionIdentity.cs",
        "digest": "sha256:7d53cc64943e1e53f480c08f0ad0924e9bff5f375776ff0c8db6a193645de202"
      },
      {
        "path": "src/Monitoring.Host/Program.cs",
        "digest": "sha256:2b56307cf67bbd5a093c3337c8526ad7063c011533f21abfd0450f50567fe643"
      },
      {
        "path": "src/Monitoring.Persistence/Migrations/202610050004_CanonicalOutbox.cs",
        "digest": "sha256:106392a7f55d3f36990754b267fbb013bcbd145149b18f3491ff388df5814f94"
      },
      {
        "path": "src/Monitoring.Persistence/MonitoringDbContext.cs",
        "digest": "sha256:45c3acef5c1e2920ffb210a4ebbddcfc4eaba9739c3aa2c8b7393b804da49b3c"
      },
      {
        "path": "src/Monitoring.Persistence/Sessions/CanonicalEntities.cs",
        "digest": "sha256:a611d47d2bcc6ff4af102967cbad18c8c8c770d81ee57ef0668c122070d3d2a5"
      },
      {
        "path": "src/Monitoring.Persistence/Sessions/CanonicalSessionInitializer.cs",
        "digest": "sha256:20a6dc277fa787e38b88bec683f01cd6fd0dd60fe96b34d3b620a7718e0d1bfc"
      },
      {
        "path": "src/Monitoring.Persistence/Sessions/OutboxStore.cs",
        "digest": "sha256:cd9183df14ef18637f21b98b874856243d42b08684a74f0fc91326d427e475fe"
      },
      {
        "path": "src/Monitoring.Persistence/Sessions/SessionProjector.cs",
        "digest": "sha256:9303f65242193cd81f6748aacb23e5d5710b88b2e20de6a93c0632a8934998c8"
      },
      {
        "path": "src/monitoring-web/src/app/app.component.css",
        "digest": "sha256:1febcdf95cd97b43e7fc20741c540458918e01f5e8454130761da19698d04986"
      },
      {
        "path": "src/monitoring-web/src/app/app.component.html",
        "digest": "sha256:9b80d37b4985f605543966e8ec46783959ecb160d2f16f4d647f4452c8bfe019"
      },
      {
        "path": "src/monitoring-web/src/app/app.component.spec.ts",
        "digest": "sha256:a62afa63bba0f8e4485815d68e3ef2ef7eec422493492869ca0b7751aadfac9d"
      },
      {
        "path": "src/monitoring-web/src/app/app.component.ts",
        "digest": "sha256:99f29d4533a200c02de602295722c1b7d0634c4336ca664f999811fab704f6b5"
      },
      {
        "path": "src/monitoring-web/src/app/app.routes.ts",
        "digest": "sha256:9413360e6709920c7052969388a98317967fe6d81bf779a0154d550796eb85ae"
      },
      {
        "path": "src/monitoring-web/src/app/session-detail/session-detail-api.ts",
        "digest": "sha256:9b85c5b53de513e7a4113bfce737140940c9b4f87e9f20ec155ea04108643d15"
      },
      {
        "path": "src/monitoring-web/src/app/session-detail/session-detail-page.component.css",
        "digest": "sha256:915722fe0ff08a92a610ca23d48a9cbe7c2b4deffb9d9335e75f991d97d92b29"
      },
      {
        "path": "src/monitoring-web/src/app/session-detail/session-detail-page.component.html",
        "digest": "sha256:91494e7e59ab561ed1b241ce1ab26ad44c0db54f06af19721c06ebe2f3876dba"
      },
      {
        "path": "src/monitoring-web/src/app/session-detail/session-detail-page.component.ts",
        "digest": "sha256:45eef51c3fa970b36803069a5befcd3f494ef3ef1d3bba3ef19966408da75586"
      },
      {
        "path": "src/monitoring-web/src/app/session-detail/session-detail-scope.spec.ts",
        "digest": "sha256:f1fd17754964884a510fd7e594dd8c9cc077cd802f2a9cc99a7077d35e4f3067"
      },
      {
        "path": "src/monitoring-web/src/app/sessions/session-http-error.ts",
        "digest": "sha256:8157a8e3f240137665fd637ea824b92466f74b9d8355728080afb4c44f8028dc"
      },
      {
        "path": "src/monitoring-web/src/app/sessions/session-query.spec.ts",
        "digest": "sha256:cd2fdfacba1ad4e454990623e04de2438c66ebea8734f1d565b73847f24d7e3a"
      },
      {
        "path": "src/monitoring-web/src/app/sessions/session-query.ts",
        "digest": "sha256:023c8b71de6c9c724c395210b7ad68d0660fa666a76cbfa8876bfd03f323d79f"
      },
      {
        "path": "src/monitoring-web/src/app/sessions/session-search-api.spec.ts",
        "digest": "sha256:2de69ecf35e91544ddc667ab6b136c5e9afada7c30faee31f695ce1fe7eb894e"
      },
      {
        "path": "src/monitoring-web/src/app/sessions/session-search-api.ts",
        "digest": "sha256:26d2c52952075f446c33ab9c2b26264dfa5962bfadffaff6f55fccc32f661f43"
      },
      {
        "path": "src/monitoring-web/src/app/sessions/session-search-page.component.css",
        "digest": "sha256:cdcb724a19695800de7e5f2d4210e5f04f0da94b9f4327288e38ec8a4590ded7"
      },
      {
        "path": "src/monitoring-web/src/app/sessions/session-search-page.component.html",
        "digest": "sha256:e3adc56cf4b6188290170e52a50db9c70f249ffa85c4d4de4a2eb2ac6d657bee"
      },
      {
        "path": "src/monitoring-web/src/app/sessions/session-search-page.component.spec.ts",
        "digest": "sha256:7da47840eab6da5ca6deda6f651e897055cbd0ec6a497029a17d38927e916da3"
      },
      {
        "path": "src/monitoring-web/src/app/sessions/session-search-page.component.ts",
        "digest": "sha256:7006cd12e4218fe42c842f1ca33b1cc9f5276f6c4863742a382db5acfd42aaaa"
      },
      {
        "path": "tests/Monitoring.Tests/CanonicalSessionTests.cs",
        "digest": "sha256:6a4fd74271a89744d64460b97b084ed9214214a884d03dc0d94110ff924a574b"
      },
      {
        "path": "tests/Monitoring.Tests/InboxSchemaTests.cs",
        "digest": "sha256:7d3c99e2a98ac909f8be4a68f90ca9ba1b4148c343e1c6ec953d9e2b6c98109e"
      },
      {
        "path": "tests/Monitoring.Tests/MigrationTests.cs",
        "digest": "sha256:2483724f98519a0ec8bc4a7ce2611a4c3f7f8bdcefd3032a3fb8defce5188f65"
      },
      {
        "path": "tests/Monitoring.Tests/OutboxTests.cs",
        "digest": "sha256:7e58f9676b6830c82427a96d0d4fca1779aaaf8fd8ec89e7e89c8bb1704e5bea"
      },
      {
        "path": "tests/Monitoring.Tests/SessionSchemaTests.cs",
        "digest": "sha256:4aa5226633c920374245fb11a1c10dff7aed208051b288a0203c8c245a55b49f"
      },
      {
        "path": "tests/stack/foundation.test.mjs",
        "digest": "sha256:44bf7c4c902a5d2fe877ab2bb40345879b55cad3937db4b759c884207cdb7667"
      },
      {
        "path": "tests/stack/search-pipeline.test.mjs",
        "digest": "sha256:2349d211897192e8e5b5d52c28cfcc95631fcf25f375c0bf9e8002ba374b869f"
      }
    ]
  },
  "cycles": [
    {
      "task": "1.3",
      "test_file": "tests/Monitoring.Tests/CanonicalSessionTests.cs",
      "red": "written",
      "green": "passed",
      "triangulate": "passed",
      "refactor": "passed",
      "provenance": {
        "command": "dotnet test Monitoring.slnx --no-restore --logger trx --results-directory openspec/changes/producto-monitorizacion-final/evidence -- xUnit.ParallelizeTestCollections=false xUnit.MaxParallelThreads=1",
        "red_report": "openspec/changes/producto-monitorizacion-final/evidence/foundation-domain-outbox-red.trx",
        "green_report": "openspec/changes/producto-monitorizacion-final/evidence/foundation-regression-serial.trx",
        "limitation": "Executed behavioral RED/GREEN and retained original pre-write test copy. Content-addressed historical evidence; no runtime-authenticated receipt claimed.",
        "source": "content-addressed-snapshot",
        "test_file": "tests/Monitoring.Tests/CanonicalSessionTests.cs",
        "test_digest": "sha256:6a4fd74271a89744d64460b97b084ed9214214a884d03dc0d94110ff924a574b",
        "snapshot_digest": "sha256:87dd098e10fc05038b4d94f9e26192c0e35925f197c9d26183c6ede813ceb2ee",
        "snapshot_file": "openspec/changes/producto-monitorizacion-final/evidence/strict-tdd-historical/87dd098e10fc05038b4d94f9e26192c0e35925f197c9d26183c6ede813ceb2ee.json"
      }
    },
    {
      "task": "1.3",
      "test_file": "tests/Monitoring.Tests/OutboxTests.cs",
      "red": "written",
      "green": "passed",
      "triangulate": "passed",
      "refactor": "passed",
      "provenance": {
        "command": "dotnet test Monitoring.slnx --no-restore --logger trx --results-directory openspec/changes/producto-monitorizacion-final/evidence -- xUnit.ParallelizeTestCollections=false xUnit.MaxParallelThreads=1",
        "red_report": "openspec/changes/producto-monitorizacion-final/evidence/foundation-domain-outbox-red.trx",
        "green_report": "openspec/changes/producto-monitorizacion-final/evidence/foundation-regression-serial.trx",
        "additional_red_reports": [
          "openspec/changes/producto-monitorizacion-final/evidence/foundation-backfill-behavior-red.trx",
          "openspec/changes/producto-monitorizacion-final/evidence/foundation-timestamp-red.trx"
        ],
        "limitation": "Only behavioral assertion/missing-table failures count as RED. Earlier invalid migration-target setup and infrastructure failures are excluded. Historical test digest seals final test content, not a fabricated earlier snapshot.",
        "source": "content-addressed-snapshot",
        "test_file": "tests/Monitoring.Tests/OutboxTests.cs",
        "test_digest": "sha256:7e58f9676b6830c82427a96d0d4fca1779aaaf8fd8ec89e7e89c8bb1704e5bea",
        "snapshot_digest": "sha256:e6b273b84601360977a3be562399dd47270f4abefef5a0e2b090f01add5d50cd",
        "snapshot_file": "openspec/changes/producto-monitorizacion-final/evidence/strict-tdd-historical/e6b273b84601360977a3be562399dd47270f4abefef5a0e2b090f01add5d50cd.json"
      }
    },
    {
      "task": "1.2",
      "test_file": "tests/stack/foundation.test.mjs",
      "red": "written",
      "green": "passed",
      "triangulate": "passed",
      "refactor": "passed",
      "provenance": {
        "command": "node --test tests/stack/foundation.test.mjs",
        "red_report": "openspec/changes/producto-monitorizacion-final/evidence/foundation-stack-red.log",
        "green_report": "openspec/changes/producto-monitorizacion-final/evidence/foundation-stack-green-wsl.log",
        "limitation": "Core-only native integration test. Later wrapper up/restart/health logs observe latest runtime. Full K04 security/vertical remains incomplete; no live runtime receipt claimed.",
        "source": "content-addressed-snapshot",
        "test_file": "tests/stack/foundation.test.mjs",
        "test_digest": "sha256:44bf7c4c902a5d2fe877ab2bb40345879b55cad3937db4b759c884207cdb7667",
        "snapshot_digest": "sha256:095d6e6d49da34715c91d99a6315668408df474a5d3e722c0000049213b2ae4c",
        "snapshot_file": "openspec/changes/producto-monitorizacion-final/evidence/strict-tdd-historical/095d6e6d49da34715c91d99a6315668408df474a5d3e722c0000049213b2ae4c.json"
      }
    },
    {
      "task": "1.6",
      "test_file": "src/monitoring-web/src/app/sessions/session-query.spec.ts",
      "red": "written",
      "green": "passed",
      "triangulate": "passed",
      "refactor": "passed",
      "provenance": {
        "source": "content-addressed-snapshot",
        "test_file": "src/monitoring-web/src/app/sessions/session-query.spec.ts",
        "test_digest": "sha256:cd2fdfacba1ad4e454990623e04de2438c66ebea8734f1d565b73847f24d7e3a",
        "snapshot_digest": "sha256:26b80d3994b3f70fcaafc3a979341acd828c94b85a917af883cb9bfbb8d67e58",
        "snapshot_file": "openspec/changes/producto-monitorizacion-final/evidence/strict-tdd-historical/26b80d3994b3f70fcaafc3a979341acd828c94b85a917af883cb9bfbb8d67e58.json",
        "command": "node node_modules/@angular/cli/bin/ng.js test --watch=false --exclude='src/app/session-detail/session-detail-view-chain.spec.ts'",
        "cwd": "src/monitoring-web",
        "red_command": "node node_modules/@angular/cli/bin/ng.js test --watch=false --include='src/app/sessions/*.spec.ts'",
        "red_report": "openspec/changes/producto-monitorizacion-final/evidence/ui/sessions-red.log",
        "green_report": "openspec/changes/producto-monitorizacion-final/evidence/ui/ui-regression.log",
        "prewrite_test_copy": "openspec/changes/producto-monitorizacion-final/evidence/ui/session-query.red.spec.ts.txt",
        "additional_red_reports": [],
        "limitation": "Actual behavioral RED and GREEN logs with original pre-write test copies. Final test digests seal current content after locator/timing corrections and formatting. Historical content authentication is not a runtime-authenticated receipt or real API/E2E proof."
      }
    },
    {
      "task": "1.6",
      "test_file": "src/monitoring-web/src/app/sessions/session-search-api.spec.ts",
      "red": "written",
      "green": "passed",
      "triangulate": "passed",
      "refactor": "passed",
      "provenance": {
        "source": "content-addressed-snapshot",
        "test_file": "src/monitoring-web/src/app/sessions/session-search-api.spec.ts",
        "test_digest": "sha256:2de69ecf35e91544ddc667ab6b136c5e9afada7c30faee31f695ce1fe7eb894e",
        "snapshot_digest": "sha256:afd9b24ca10a2a0a4e29da908945420cfa882ca7b80f52749c42b372daf7698d",
        "snapshot_file": "openspec/changes/producto-monitorizacion-final/evidence/strict-tdd-historical/afd9b24ca10a2a0a4e29da908945420cfa882ca7b80f52749c42b372daf7698d.json",
        "command": "node node_modules/@angular/cli/bin/ng.js test --watch=false --exclude='src/app/session-detail/session-detail-view-chain.spec.ts'",
        "cwd": "src/monitoring-web",
        "red_command": "node node_modules/@angular/cli/bin/ng.js test --watch=false --include='src/app/sessions/*.spec.ts'",
        "red_report": "openspec/changes/producto-monitorizacion-final/evidence/ui/sessions-red.log",
        "green_report": "openspec/changes/producto-monitorizacion-final/evidence/ui/ui-regression.log",
        "prewrite_test_copy": "openspec/changes/producto-monitorizacion-final/evidence/ui/session-search-api.red.spec.ts.txt",
        "additional_red_reports": [],
        "limitation": "Actual behavioral RED and GREEN logs with original pre-write test copies. Final test digests seal current content after locator/timing corrections and formatting. Historical content authentication is not a runtime-authenticated receipt or real API/E2E proof."
      }
    },
    {
      "task": "1.6",
      "test_file": "src/monitoring-web/src/app/sessions/session-search-page.component.spec.ts",
      "red": "written",
      "green": "passed",
      "triangulate": "passed",
      "refactor": "passed",
      "provenance": {
        "source": "content-addressed-snapshot",
        "test_file": "src/monitoring-web/src/app/sessions/session-search-page.component.spec.ts",
        "test_digest": "sha256:7da47840eab6da5ca6deda6f651e897055cbd0ec6a497029a17d38927e916da3",
        "snapshot_digest": "sha256:7649e5b5f4f435d09711b591f3b8d0b093646af3309767bc20234e5eae6b97d2",
        "snapshot_file": "openspec/changes/producto-monitorizacion-final/evidence/strict-tdd-historical/7649e5b5f4f435d09711b591f3b8d0b093646af3309767bc20234e5eae6b97d2.json",
        "command": "node node_modules/@angular/cli/bin/ng.js test --watch=false --exclude='src/app/session-detail/session-detail-view-chain.spec.ts'",
        "cwd": "src/monitoring-web",
        "red_command": "node node_modules/@angular/cli/bin/ng.js test --watch=false --include='src/app/sessions/*.spec.ts'",
        "red_report": "openspec/changes/producto-monitorizacion-final/evidence/ui/sessions-red.log",
        "green_report": "openspec/changes/producto-monitorizacion-final/evidence/ui/ui-regression.log",
        "prewrite_test_copy": "openspec/changes/producto-monitorizacion-final/evidence/ui/session-search-page.red.spec.ts.txt",
        "additional_red_reports": [],
        "limitation": "Actual behavioral RED and GREEN logs with original pre-write test copies. Final test digests seal current content after locator/timing corrections and formatting. Historical content authentication is not a runtime-authenticated receipt or real API/E2E proof."
      }
    },
    {
      "task": "1.6",
      "test_file": "src/monitoring-web/src/app/session-detail/session-detail-scope.spec.ts",
      "red": "written",
      "green": "passed",
      "triangulate": "passed",
      "refactor": "passed",
      "provenance": {
        "source": "content-addressed-snapshot",
        "test_file": "src/monitoring-web/src/app/session-detail/session-detail-scope.spec.ts",
        "test_digest": "sha256:f1fd17754964884a510fd7e594dd8c9cc077cd802f2a9cc99a7077d35e4f3067",
        "snapshot_digest": "sha256:559492998fa3cbcdb9fcdc6218562316a13ce01427d382f947911571aa82d755",
        "snapshot_file": "openspec/changes/producto-monitorizacion-final/evidence/strict-tdd-historical/559492998fa3cbcdb9fcdc6218562316a13ce01427d382f947911571aa82d755.json",
        "command": "node node_modules/@angular/cli/bin/ng.js test --watch=false --exclude='src/app/session-detail/session-detail-view-chain.spec.ts'",
        "cwd": "src/monitoring-web",
        "red_command": "node node_modules/@angular/cli/bin/ng.js test --watch=false --include='src/app/session-detail/session-detail-scope.spec.ts'",
        "red_report": "openspec/changes/producto-monitorizacion-final/evidence/ui/detail-scope-red.log",
        "green_report": "openspec/changes/producto-monitorizacion-final/evidence/ui/ui-regression.log",
        "prewrite_test_copy": "openspec/changes/producto-monitorizacion-final/evidence/ui/session-detail-scope.red.spec.ts.txt",
        "additional_red_reports": [
          "openspec/changes/producto-monitorizacion-final/evidence/ui/detail-cancel-red.log"
        ],
        "limitation": "Actual behavioral RED and GREEN logs with original pre-write test copies. Final test digests seal current content after locator/timing corrections and formatting. Historical content authentication is not a runtime-authenticated receipt or real API/E2E proof."
      }
    },
    {
      "task": "1.6",
      "test_file": "src/monitoring-web/src/app/app.component.spec.ts",
      "red": "written",
      "green": "passed",
      "triangulate": "passed",
      "refactor": "passed",
      "provenance": {
        "source": "content-addressed-snapshot",
        "test_file": "src/monitoring-web/src/app/app.component.spec.ts",
        "test_digest": "sha256:a62afa63bba0f8e4485815d68e3ef2ef7eec422493492869ca0b7751aadfac9d",
        "snapshot_digest": "sha256:7212aaf93218c6e051c017fa883566543c824dbed7ae1ed56e843ef38c05d485",
        "snapshot_file": "openspec/changes/producto-monitorizacion-final/evidence/strict-tdd-historical/7212aaf93218c6e051c017fa883566543c824dbed7ae1ed56e843ef38c05d485.json",
        "command": "node node_modules/@angular/cli/bin/ng.js test --watch=false --exclude='src/app/session-detail/session-detail-view-chain.spec.ts'",
        "cwd": "src/monitoring-web",
        "red_command": "node node_modules/@angular/cli/bin/ng.js test --watch=false --include='src/app/app.component.spec.ts'",
        "red_report": "openspec/changes/producto-monitorizacion-final/evidence/ui/app-red.log",
        "green_report": "openspec/changes/producto-monitorizacion-final/evidence/ui/ui-regression.log",
        "prewrite_test_copy": "openspec/changes/producto-monitorizacion-final/evidence/ui/app.red.spec.ts.txt",
        "additional_red_reports": [],
        "limitation": "Actual behavioral RED and GREEN logs with original pre-write test copies. Final test digests seal current content after locator/timing corrections and formatting. Historical content authentication is not a runtime-authenticated receipt or real API/E2E proof."
      }
    },
    {
      "task": "1.4",
      "test_file": "tests/stack/search-pipeline.test.mjs",
      "red": "written",
      "green": "passed",
      "triangulate": "passed",
      "refactor": "passed",
      "provenance": {
        "source": "content-addressed-snapshot",
        "test_file": "tests/stack/search-pipeline.test.mjs",
        "test_digest": "sha256:2349d211897192e8e5b5d52c28cfcc95631fcf25f375c0bf9e8002ba374b869f",
        "snapshot_digest": "sha256:24300bbf1c195a1646fefb2498dc4bc7e829d20632c3144e99d31c08178ed38d",
        "snapshot_file": "openspec/changes/producto-monitorizacion-final/evidence/strict-tdd-historical/24300bbf1c195a1646fefb2498dc4bc7e829d20632c3144e99d31c08178ed38d.json",
        "command": "node --test tests/stack/search-pipeline.test.mjs",
        "red_report": "openspec/changes/producto-monitorizacion-final/evidence/search-pipeline-red.log",
        "additional_red_reports": [
          "openspec/changes/producto-monitorizacion-final/evidence/search-contract-red.log"
        ],
        "green_report": "openspec/changes/producto-monitorizacion-final/evidence/search-pipeline-triangulation.log",
        "prewrite_test_copy": "openspec/changes/producto-monitorizacion-final/evidence/search-pipeline.red.mjs.txt",
        "limitation": "Original first test copy retained before production configuration. Real 404 missing-index and schemaVersion999 indexed RED observed; four final real-container checks pass. Later test content is sealed as final, not reconstructed historical content. No separate pre-write copy for the second contract test and no live runtime receipt are claimed. Partial K03: key design mismatch, projection status/rebuild and API remain unimplemented."
      }
    }
  ]
}
```

### Handoff to K03 / API

The active Compose project is monitoring-foundation-test. Use scripts/lab/core.ps1 with that Project; execute Compose in Ubuntu/WSL on Windows so secret mounts use Linux paths. No service port is published. The core is synthetic/private with TLS/security profiles pending; do not expose or claim product readiness.

Migration0004 creates monitoring.session_identity (triple PK,document_key,revision,state,deleted_at), session_metadata (normalized IP/time/endpoints/provenance/revision/capture fields), and projection_outbox (UUID id,aggregateid,aggregatetype=sessions,target_topic,revision,schema_version,payload,created_at; unique aggregateid/revision/topic). The app role is monitoring_app; CDC is monitoring_cdc with SELECT only on outbox, publication monitoring_outbox. OutboxStore.SessionTopic is monitoring.sessions.v1. OutboxStore.PublicationLock7182041001 is shared by writers; rebuild must use the exclusive counterpart at final switch.

Payload names: schemaVersion,operation,documentKey,revision,siteId,sensorId,eventId,startedAt,endedAt,sourceIp,destinationIp,sourcePort,destinationPort,protocol,vlanId,provenance,inferred,partial,closeReason,packetCount,byteCount,acceptedAt. Times normalize to UTCZ/milliseconds; session_projection.data retains original source time strings. Synthetic inferred/partial/counters remain null. Source code is OutboxStore.cs; no SessionSearchDocument class is introduced in this batch. Domain CanonicalSession exposes typed normalized metadata and original JsonElement; SessionIdentity.DocumentKey is three base64url UTF8 segments separated by periods.

K03 owns actual source/sink configuration, contract SMT, strict mapping/generation/reconciliation/delete events and rebuild. Source must route target_topic, include numeric header revision, expand JSON payload and preserve full key. Sink16 uses INSERT,key.ignore=false,schema.ignore=true,external.version.header=revision,max.in.flight.requests=1,never UPSERT/offset-versioning. API list/schema comes from adopted design; raw session detail remains authority. U06 owns permanent quarantine/receipts and quota changes; U07 owns real Keycloak/EJBCA/TLS/ACL. S17/S18 are unexecuted gates, not inferred from this demo.

Rollback: stop writers/connectors; preserve PostgreSQL/outbox/volumes/offsets and use compatible binary. Down migration refuses destructive rollback. Ordinary core down preserves volumes. No unrelated resources were removed.


## Angular batch U05 / task 1.6 — 2026-10-05

Mode: Strict TDD. Delivery: exception-ok / accepted size:exception. Ownership: src/monitoring-web/**. No backend edits, commits, push or PR.

- [~]1.6: standalone sessions work surface proposes recent 24 UTC hours, sends mandatory from/to and page50, validates retention/range/exact IP/protocol/ports and page1–100, explains >24h site+sensor+IP selectivity. HTTP adapter calls the real API endpoint; no fixture service is production behavior.
- Cursor pagination retains query bounds, filter changes clear the page/cursor and cancel pending HTTP, and 410 offers a native restart action. Text states distinguish loading, actual empty200, 400/401/403/429/503/504/network errors and current/lagging/recovering freshness; unknown lag is never zero.
- Scoped identity links open the preserved detail route with siteId/sensorId. Detail retains five fields and the historic no-selector request, rejects incomplete client identities, cancels superseded selections and differentiates access/dependency failures. Server authorization remains authoritative. Native labelled form/link/button controls, announcements, focus styles and responsive44px touch targets reuse semantic tokens.
- Full1.6 remains open: this batch proves HTTP adapter/component behavior with HttpTestingController, not PostgreSQL→Kafka→Elasticsearch→API→Angular integration or browser E2E. OIDC/identity-change/logout cleanup is completed with task3.2; the initial demo continues using the design-approved Development/Testing server context. Task1.7 remains open. No runtime or capacity claim is made.

### Executed UI evidence

- Safety net: prior UI15/15 supplied by coordinator; not rerun before RED. Final affected regression excludes the .NET-launching historical view-chain to avoid concurrent backend fixtures.
- Behavioral RED: sessions33 failed/34 on compiled minimal declarations; scoped detail7/7 failed; shell1 failed/2; later incomplete-identity cancellation1 failed/8. Logs and original test copies in evidence/ui.
- Focal GREEN43/43 before final detail cancellation triangulation; final local regression56/56 across7 files,0 skipped. This comprises42 newly added checks and one revised shell navigation check plus the preserved detail adapter/component checks.
- Final Angular build passes (354.51kB initial raw,93.59kB estimated transfer). This is bundle output, not a capacity measurement. git diff --check passes.
- Initial npm12 invocation consumed --include as npm configuration; it was interrupted and excluded. Subsequent tests call Angular CLI directly. An incorrect CLI path and initial NG8022 formField/name build errors are excluded from RED. Test locators now use semantic input IDs because Signal Forms owns name; pending-HTTP tests do not await stability before flushing; a negative substring assertion was narrowed to actual dd values. These fixture corrections do not establish production behavior.
- Discovered Angular22 resource behavior: loadEffect returns on undefined params before aborting a pending stream. Explicit RxJS takeUntil cancellation handles filter/identity invalidation; RED detail-cancel and stale-list cases verify the correction.
- Content-addressed historical snapshots authenticate preserved content and references. They do not supply runtime-authenticated live receipts. No such receipts are fabricated.

### TDD Cycle Evidence — UI

| Task | Test file | Layer | Safety net | RED | GREEN | TRIANGULATE | REFACTOR |
|---|---|---|---|---|---|---|---|
|1.6|src/monitoring-web/src/app/sessions/session-query.spec.ts|pure unit|new|written|passed56/56|passed|passed after formatting|
|1.6|src/monitoring-web/src/app/sessions/session-search-api.spec.ts|Angular/HTTP boundary unit|new|written|passed56/56|passed|passed after formatting|
|1.6|src/monitoring-web/src/app/sessions/session-search-page.component.spec.ts|Angular/HTTP boundary unit|new|written|passed56/56|passed|passed after formatting|
|1.6|src/monitoring-web/src/app/session-detail/session-detail-scope.spec.ts|Angular/HTTP boundary unit|prior15/15|written|passed56/56|passed|passed after formatting|
|1.6|src/monitoring-web/src/app/app.component.spec.ts|Angular/HTTP boundary unit|prior15/15|written|passed56/56|passed|passed after formatting|

### UI → API handoff

GET /api/v1/sessions accepts mandatory UTC-Z from/to and optional exact siteId,sensorId,sourceIp,destinationIp,protocol,sourcePort,destinationPort,pageSize,cursor. AND semantics; start selection [from,to); default page50; maximum100; >24h requires site+sensor+oneIP. Response: {items,nextCursor?:string|null,freshness:{state:current|lagging|recovering,measuredAt:string,lagSeconds?:number|null}}. Items flat: eventId,siteId,sensorId,sourceIp,destinationIp,sourcePort,destinationPort,protocol:TCP|UDP,startedAt,endedAt,provenance:synthetic|capture,inferred?:boolean|null,partial?:boolean|null. Detail: GET /api/v1/sessions/{encodedEventId}?siteId&sensorId, exact five-field response; no trust headers or inferred counters. The Angular proxy remains /api→127.0.0.1:5080 for local serving.

Next: finish real search API/index pipeline and task1.7 vertical; attach real-browser integration evidence to1.6 before marking it complete, then integrate identity lifecycle under3.2.


## Search pipeline batch U04 / task1.4 — 2026-10-05

Mode: Strict TDD. Delivery: exception-ok / accepted size:exception. Production writes halted on verified design-mismatch. No state/spec/design/baseline edits, commits, push or PR.

- [~]1.4: strict versioned sessions-v1-000001 mapping and sessions-read alias; actual Debezium source from restricted monitoring_outbox publication; Kafka complete key; sink16 INSERT with numeric authority revision, explicit DLQ and pure Java contract SMT. Four native integrations pass. K03 is incomplete: projection freshness/checkpoints, authority reconciliation, production suppression command, generation snapshot/concurrent catch-up/rebuild and schema-wide identity solution remain pending. No production retention worker is implemented.
- Fixture tests deliberately commit controlled authority identity/outbox rows using PostgreSQL transactions and observe the actual CDC/broker/sink/index. They do not invoke the ingestion endpoint/projector and do not constitute API/UI/E2E evidence. Later generator/vertical must exercise the application path.
- First behavioral RED: confirmed outbox cannot appear because index is absent (404 index_not_found); contract RED: schemaVersion999 was indexed (200 instead of404). Initial shell/curl argument failure is diagnostic only. Original first-test pre-write copy and final triangulation copy are retained separately.
- GREEN/triangulation:4/4,0 skipped,72.8s in search-pipeline-triangulation.log: complete document/revision/unique alias match; incompatible record has contract-version in durable DLQ while later valid record proceeds; minimal permanent delete document survives old upserts at new Kafka offsets and Connect restart; real Elasticsearch outage retains outbox and retries publication after recovery. Sink source/sink tasks are RUNNING after restart; core remains healthy and volumes preserved. No .NET or Angular suite was rerun in this batch.
- Actual plugin corrections: topic-mutating RegexRouter fails under async flush; flush.synchronously=true required ([sink documentation](https://docs.confluent.io/kafka-connectors/elasticsearch/current/configuration_options.html)). EventRouter defaults to dropping JSON null fields; table.json.payload.null.behavior=optional_bytes preserves the complete synthetic payload ([Debezium documentation](https://debezium.io/documentation/reference/stable/transformations/outbox-event-router.html)). Initial failure logs are retained, never counted as GREEN.
- Java compiler21+ builds --release21 against connect-api/kafka-clients4.3.1 copied from the pinned Kafka image; no Maven/resolution at service startup. Transform jar is generated/ignored, included beside sink dependencies, and validates version/operation/key/full identity/revision header/exact canonical fields without external calls. Connect reads CDC password through a file provider generated from the mounted secret. Kafka/ES security remains U07.

### TDD Cycle Evidence — search

|Task|Test file|Layer|Safety net|RED|GREEN|TRIANGULATE|REFACTOR|
|---|---|---|---|---|---|---|---|
|1.4 partial|tests/stack/search-pipeline.test.mjs|real PostgreSQL→Debezium→Kafka→Connect16→ES9.5.4|foundation141/141 and native core1/1 supplied|written;404 and999 assertion fail|passed4/4|four distinct failure/replay scenarios|not needed|

Historical content authentication validates the one merged record with requireHistoricalAuth and requireProvenanceDigest. It does not supply runtime-authenticated receipts. Eight previous foundation/UI cycles and their sealed references are preserved; the ninth cycle and current functional manifest are merged.

### Blocking design mismatch: documentary key length

SessionIdentity.DocumentKey and design.md “Modelo, migraciones y proyección” encode each triple segment as base64url UTF8. With three allowed128-character ASCII identifiers the key is515 bytes (Unicode can exceed that further). Elasticsearch9.5.4 rejects a515-byte _id with HTTP400, preserved in search-document-key-limit.json. [Official Elasticsearch _id limit](https://www.elastic.co/docs/reference/elasticsearch/mapping-reference/mapping-id-field) is512 bytes. Therefore a valid ingestion identity cannot always be indexed with the adopted key as _id; reducing admitted identifiers or silently dropping these sessions would break the contract.

Proposed minimum design revision: preserve the canonical triple and existing encoded document_key/Kafka key, add a PostgreSQL-assigned compact search_document_id with UNIQUE and persistent triple mapping, publish it in a versioned compatible record, and use a pure sink transform to select that durable search ID. A UUID text is36 bytes, verified against PostgreSQL18. This retains deterministic reuse, revision/suppression and collision rejection in authority. Alternative: compact SHA256-derived search ID (64hex bytes, verified with built-in PostgreSQL sha256) plus an explicit UNIQUE authority registry detecting/resolving collisions; it avoids random allocation but needs a collision protocol. Neither alternative has migration/backfill/rebuild compatibility evidence yet. Design must specify handling legacy outbox/history/indices and new publication before resumed apply.

Affected revision paths: design.md/decisions, Domain/Sessions/SessionIdentity.cs, Persistence/Sessions/{OutboxStore,CanonicalSessionInitializer}.cs, new sequential migration0005_SearchProjectionControl (reserved, not created), deploy/connect transform/config and mapping, pipeline tests; future search adapter/authority status/rebuild will use the revised persistent mapping. Migrations0001–0004 and original accepted data are preserved. Coordinator reserves0005 now; future units use subsequent free IDs.

### Runtime handoff and remaining scope

Active project monitoring-foundation-test; no host data/admin/API ports published. Bootstrap command: node scripts/lab/pipeline.mjs (after scripts/lab/prepare.ps1 + core up). Internal endpoints: http://connect:8083/connectors/monitoring-source/status, http://connect:8083/connectors/monitoring-sink/status, http://elasticsearch:9200/sessions-read/_search. source/sink JSON is versioned; never print resolved credentials. Do not rerun bootstrap to reset an alias after future rebuild: current helper installs the first generation only.

UI/API shape is unchanged and task1.5 is not started. Complete design revision, authority status/rebuild/suppression, then API PIT/leases/limits and real generator/vertical1.7. Core smoke does not claim production TLS/OIDC/PKI, capacity or S17/S18. Rollback stops connectors, keeps PostgreSQL/outbox/offsets/barrier documents/volumes, and retains the current read alias. No global resource options or unrelated containers changed.

## Recovery checkpoint — 2026-10-06

The user requested publication of the current work to the repository. This checkpoint preserves incomplete implementation; it does not complete verification, review or archive. The canonical state remains `blocked`, revision12, with `apply: partial` and the documented Elasticsearch identity design mismatch unresolved.

Probe source and focused tests are preserved under `src/Monitoring.Probe/` and `tests/Monitoring.Probe.Tests/`. Recovery validation ran `dotnet test tests/Monitoring.Probe.Tests/Monitoring.Probe.Tests.csproj --no-restore --verbosity minimal`: exit0,32 passed,0 failed,0 skipped. This proves the local project compiles and its focused tests pass; Linux packaging, real PCAP/tshark integration and the application vertical remain unverified. The probe projects are not yet registered in `Monitoring.slnx`. The interrupted probe batch did not finish its shared progress/evidence merge, so tasks2.1–2.2 remain open and the existing historical TDD record is preserved without claiming additional cycles.

Resume from `state.yaml`, this progress, `tasks.md` and the preserved evidence. Resolve the compact search identity design and migration/backfill compatibility, finish K03 and the search API, then execute the real API/Angular browser vertical. Inspect and complete the probe batch separately. Earlier backend141/141, Angular56/56 and native pipeline4/4 results are historical evidence, not a new full-suite run for this checkpoint.


## Identidad compacta de búsqueda — 2026-10-06

Mode: Strict TDD. Resuelve el bloqueo «Blocking design mismatch: documentary key length» (clave de515 bytes frente al `_id` máximo de512 de Elasticsearch). No cierra K03 ni la tarea1.4; no edita `state.yaml` (el runtime/orquestador decide el desbloqueo y la revisión).

- Decisión: UUID de búsqueda persistente asignado por PostgreSQL, conservando la identidad triple y la clave documental completa como identidad canónica y clave Kafka. Alternativa SHA-256 descartada por exigir protocolo de colisiones. Detalle y reversión en `design.md` («Modelo, migraciones y proyección»), ADR-016 y `decisions/adr-002.md`.
- Migración `202610060005_SearchDocumentIdentity`: `session_identity.search_document_id uuid NOT NULL UNIQUE DEFAULT gen_random_uuid()` (valor distinto por fila existente), trigger que impide cambiarlo y `schema_version IN (1,2)` en el outbox. Down sigue prohibido. Las tablas de frescura/checkpoint de K03 usarán identificadores posteriores.
- Contrato `schemaVersion`2 con `searchDocumentId`, publicado en `monitoring.sessions.v2`; historial v1 intacto. `OutboxStore` añade `WriteDeleteBarrierAsync` (barrera mínima, mismo UUID). `CanonicalSessionInitializer` reproyecta una vez a v2 cada identidad activa (con su revisión actual) y cada barrera; la unicidad identidad/revisión/topic hace idempotente la repetición.
- SMT `monitoring.SessionContract`: exige schemaVersion2 y UUID canónico en minúsculas y, tras validar, sustituye la clave Kafka por `searchDocumentId` conservando topic, partición, offset y headers. Sink: `monitoring.sessions.v2` → `sessions-v2-000001`; mapping `sessions-v2.json` con `searchDocumentId` keyword; `scripts/lab/pipeline.mjs` retira el alias de generaciones `sessions-v1-*` y no sobrescribe una generación v2 ya aliasada.

### Evidencia ejecutada (esta sesión)

- Entorno: SDK .NET 10.0.112 instalado por apt (el `global.json` fija 10.0.303; se ajustó solo localmente y se restauró, no forma parte del cambio), PostgreSQL16 local en lugar de Testcontainers/postgres18 (parche local del fixture, revertido). Sin daemon Docker.
- RED backend: 4 de7 fallan en `OutboxTests` (columna `search_document_id` inexistente; contrato v1). GREEN: `OutboxTests` 7/7 con migración, `OutboxStore` e inicializador. Nuevos: identificadores máximos (515 bytes) con UUID de 36 bytes y estable al repetir; unicidad e inmutabilidad (23000/23505); actualización desde el estado de la migración0004 con historial v1 intacto, UUID estable entre dos `--migrate`, una sola republicación v2 por identidad y barrera sin datos de tráfico.
- RED/GREEN Java: `SessionContractTest` falló (`contract-version`) contra el guard v1 y pasa 21 comprobaciones (clave de515 bytes → clave de36, delete, schema1, UUID mal formado, clave/identidad/header). `node --test tests/stack/session-contract.test.mjs`: 1/1 sin contenedores, con jars Kafka4.3.1 descargados de Maven Central en `deploy/connect/transforms/build/libs` (ignorado por git).
- Suite .NET (`Monitoring.Tests` sin las clases `VistaAngularDelivery`/`SessionViewChain`, que lanzan la compilación Angular y se colgaron en esta sesión; no se verificaron): 137/138 en PostgreSQL16. El único fallo, `SessionSchemaTests.UpgradeS02...`, espera `23001` (RESTRICT) y PostgreSQL16 devuelve `23503` para un `ON DELETE RESTRICT` (comprobado con psql en una tabla temporal, sin relación con 0005); la imagen fijada es postgres18, donde debe reevaluarse. Tres pruebas existentes que contaban4 migraciones se actualizaron a5 (`MigrationTests`, `InboxSchemaTests`, `SessionSchemaTests`). `Monitoring.Probe.Tests` 32/32. No es una ejecución completa de la suite ni sustituye la del CI/laboratorio.

### No verificado

- `tests/stack/search-pipeline.test.mjs` (actualizado a v2 y con un caso nuevo de identidad de515 bytes indexada bajo su UUID) y `scripts/lab/pipeline.mjs` **no se ejecutaron**: no hay Docker en esta sesión. Deben correrse en el laboratorio (`prepare.ps1` + core up + `node scripts/lab/pipeline.mjs`) antes de dar por cerrada la compatibilidad con el sink real, en particular que `key.ignore=false` use la clave reescrita como `_id`.
- Sigue pendiente K03: frescura/checkpoints, reconciliación, comando de supresión, snapshot de generación/rebuild, y las tareas1.5–1.7 y siguientes. `acceptedAt` de la reproyección v2 se toma de `ingestion_inbox`; sin retención implementada no hay pérdida, pero la futura caducidad de la bandeja debe conservar ese dato o recuperarlo del outbox v1.


## Supresión en la autoridad (K03, parcial) — 2026-10-06

PR [#24](https://github.com/Snakeblack/monitorizacion-pasiva-red/pull/24) (identidad compacta) está fusionado en `main` con CI `verify` en verde sobre PostgreSQL18, lo que cierra la reserva anterior sobre `SessionSchemaTests.UpgradeS02...` (el `23001` de RESTRICT sí se cumple en la imagen fijada).

- `SessionSuppressor.SuppressAsync(identity)` (`src/Monitoring.Persistence/Sessions/`): en una transacción toma el lock compartido de publicación y luego el de fila, pasa la identidad a `deleted`, incrementa la revisión, borra sesión y metadatos (cascada desde la proyección; la bandeja aceptada conserva su propia retención) y confirma la barrera mínima `operation=delete` con el mismo `searchDocumentId`. Resultado `Suppressed`, `AlreadySuppressed` o `NotFound`; repetir o competir no duplica barrera.
- Evidencia (PostgreSQL16 local, parche temporal del fixture ya revertido): RED por tipo inexistente; GREEN `SessionSuppressionTests` 5/5 (barrera atómica sin datos de tráfico a revisión 2 e historial v1/v2 intacto, idempotencia/desconocida, rollback ante fallo del outbox, 4 supresiones concurrentes → una barrera, replay de evento aceptado no resucita) y `OutboxTests` 7/7. Suite sin las clases Angular: 142/143; el fallo es el artefacto PostgreSQL16 ya descrito.
- Aún sin conectar a un disparador: no hay endpoint, worker ni retención que lo invoque (S15 / tarea4.2). Tampoco prueba el efecto en el índice real: eso requiere el stack Docker (barrera sobre documento previo, ya cubierto por `search-pipeline.test.mjs` con fixtures).
- Siguen pendientes de K03: frescura/checkpoints (`ProjectionStatus`), reconciliación contra la autoridad, snapshot/rebuild de generación y la ejecución en el laboratorio de los tests del stack actualizados a v2.


## API de consulta, corte 1 (tarea1.5, parcial) — 2026-10-06

`GET /api/v1/sessions` valida, autoriza y acota la búsqueda detrás del puerto `ISessionSearch` (`Monitoring.Domain/Sessions/Search/`). Sigue abierta la tarea1.5: faltan el cursor protegido con PIT/`search_after` y los leases, el adaptador Elasticsearch con verificación de vigencia en PostgreSQL, y la autorización por permisos reales (S12).

- Validación previa a cualquier búsqueda (`SessionSearchQuery`, 400 con código estable sin eco de entrada): `from`/`to` UTC-Z de hasta milisegundos, `[from,to)`, sin futuro, ventana ≤30 días dentro de la retención; >24 h exige `siteId` y `sensorId` y al menos una IP; IP literal (sin formas cortas, ceros iniciales ni zona IPv6) normalizada como en el índice; protocolo exacto TCP/UDP; puertos 0–65535; `pageSize` 1–100 (defecto50); parámetros desconocidos, repetidos o vacíos rechazados.
- Ámbito: 401 sin identidad y fuera de Development/Testing (como el detalle); los selectores del cliente solo restringen el conjunto autorizado y un selector ajeno es 403 antes de buscar. El conjunto se pasa a `ISessionSearch` para filtrarlo dentro de la consulta.
- Respuesta `{items,nextCursor,freshness:{state,measuredAt,lagSeconds}}` con ítems planos del contrato Angular; vacío real 200, lag desconocido `null`. Errores: timeout configurable (10 s por defecto) con cancelación propagada → 504; `SessionSearchException` y fallos de red → 503/429/410/400/403 sin filtrar detalles; tope de búsquedas simultáneas por proceso (20 por defecto) → 429 inmediato; sin backend registrado → 503.
- Evidencia (PostgreSQL16 local para el resto de la suite; estas pruebas no usan base de datos): `SessionSearchEndpointTests` 52/52 (31 solicitudes inválidas, filtros normalizados, ámbito, 401, forma de respuesta, vacío, mapeo de fallos, 504 con cancelación observada, 429 con liberación de ranura). Las pruebas se escribieron junto al código y no hubo RED observado por separado; se comprobó que discriminan con dos mutaciones del validador (6 fallos y vuelta a verde). Suite sin las clases Angular: 194/195, con el único fallo conocido de PostgreSQL16 (`23001`/`23503`), que el CI de postgres18 ya pasa.
- No verificado: ningún adaptador real ni el recorrido Angular→API; el límite de concurrencia es local al proceso, no el registro PostgreSQL por sujeto del diseño.

### API de consulta, corte 2: cursor protegido — 2026-10-06

- El puerto trabaja con `SessionSearchPosition(snapshotId, startedAt, documentKey)`; el host lo sella en un cursor opaco (`SessionCursor`, ASP.NET Data Protection) que liga versión, sujeto, hash del conjunto autorizado actual, hash de los filtros normalizados, tamaño de página, posición y plazo absoluto de10 min fijado en la primera página y nunca ampliado. Respuestas: alterado/mal formado/filtros o página distintos → 400; otro sujeto o ámbito cambiado → 403 (se evalúa antes que la caducidad); caducado → 410; ninguno ejecuta la búsqueda. `TrustedSessionReadContext` gana `Subject` (fijo en Development/Testing hasta S12).
- El anillo de claves se persiste con `DataProtection:KeysPath`; sin él, los cursores no sobreviven a un reinicio ni se comparten entre instancias.
- Evidencia: `SessionSearchEndpointTests` 63/63 (cursor opaco y reanudación, alterado, mal formado, filtros/intervalo/página distintos, otro sujeto y ámbito cambiado con anillo compartido, caducidad con reloj controlado y plazo no ampliable). Suite sin Angular 205/206 (único fallo: artefacto PostgreSQL16 conocido).
- Pendiente de1.5: leases de snapshots por sujeto, adaptador Elasticsearch con PIT y verificación de vigencia en PostgreSQL.

### API de consulta, corte 3: adaptador, leases y vigencia — 2026-10-06

- `ISessionVisibility` / `PostgresSessionVisibility`: una consulta indexada por `search_document_id` devuelve solo identidades `active`; cualquier bloque de hits se verifica contra la autoridad (migración sin cambios).
- `ISnapshotLeases` / `PostgresSnapshotLeases` (migración `202610060006_SearchSnapshotLeases`): admisión serializada con advisory lock, 2 snapshots por sujeto y20 globales por defecto (`Search:Leases`), caducados purgados al admitir, digest SHA-256 del PIT y contador de cambios, `RecordPitAsync` falso si el lease se liberó o caducó. 8 admisiones simultáneas concedieron exactamente 2.
- `ElasticsearchSessionSearch` (`Persistence/Search`): primera página adquiere lease y abre PIT; ámbito, filtros, `[from,to)` y `operation=upsert` forman parte de la consulta del motor; orden `startedAt desc, documentKey asc` con `search_after`; `track_total_hits=false`; bloques de100 con comprobación de vigencia y un hit visible de anticipación para decidir la continuación sin página vacía final; el PIT rotado se propaga y se registra; cada página revalida el lease; PIT perdido/lease caducado → 410 y liberación; caída del motor → 503/429 conservando el snapshot en páginas posteriores y liberándolo si falla la primera; la última página cierra PIT y libera lease. Un documento ilegible se omite; un bloque entero ilegible falla visiblemente. `IProjectionStatus` por defecto es `UnknownProjectionStatus` (recovering, lag desconocido) hasta que exista el checkpoint de K03.
- Configuración: `Search:Elasticsearch:Url` (y opcionalmente `ApiKey`, `Index`, `KeepAlive`, `BlockSize`) activa el adaptador; sin ella el endpoint responde503.
- Evidencia: `SearchAuthorityTests` 4/4 (PostgreSQL real), `ElasticsearchSessionSearchTests` 19/19 y `SessionSearchVerticalTests` 1/1 (HTTP + cursor sellado + adaptador + PostgreSQL real + supresión con índice atrasado). Un test de caducidad destapó que el lease solo se validaba al rotar el PIT; corregido y reprobado. Suite sin Angular 229/230 (fallo conocido de PostgreSQL16).
- **Limitación importante:** el motor es `FakeElasticsearch`, un doble en proceso que interpreta el subconjunto de DSL que genera el adaptador. Verifica la forma de las peticiones y la lógica, no el comportamiento de Elasticsearch 9.5.4 (descargas bloqueadas en esta sesión). Debe ejecutarse contra el stack real en la tarea1.7, en particular: `search_after` con sort por fecha/keyword sobre PIT, `format` del rango, `sort` devuelto como epoch millis y el cuerpo de error `search_context_missing_exception`.

### K03: frescura, reconciliación y rebuild de generaciones — 2026-10-06

- **Reconciliación y frescura** (migración `202610060007_SearchProjectionCheck`): `ProjectionReconciler` toma la última fila de outbox de cada identidad más antigua que el margen de gracia (reloj de PostgreSQL), hace refresh del índice y compara por bloques, vía `_mget`, revisión y operación visibles; huecos/obsoletos se miden como lag desde `acceptedAt`. El resultado se registra y `PostgresProjectionStatus` devuelve `Current` solo con una verificación completa, sin huecos y de menos de 5 min; con huecos `Lagging` con lag creciente; sin verificación vigente `Recovering` con lag `null`. Un barrido interrumpido queda registrado incompleto y nunca cuenta. `ProjectionReconciliationWorker` lo ejecuta cada minuto si hay Elasticsearch configurado. `Current` informa lag `null` (no se mide, no se afirma cero).
- **Generaciones** (migración `202610060008_SearchGenerations`): tabla `search_generation` (generación1 sembrada = `monitoring.sessions.v2` / `sessions-v2-000001`, una sola activa), `projection_outbox.source_outbox_id`. `OutboxStore`, el inicializador y el reconciliador usan el topic de la generación activa. `SearchGenerationStore`: alta con un solo rebuild abierto, barrido keyset y catch-up por anti-join (revisión), plazo de snapshot, activación que verifica antes de retirar la generación en servicio (un test destapó que la primera versión la retiraba aunque fallara).
- **Coordinador** (`SearchRebuildCoordinator`, `ElasticsearchIndexAdmin`, `ConnectGenerationSink`): crea índice y conector por generación, copia y pone al día, y en la fase final toma el lock exclusivo de publicación (los escritores esperan), repite el catch-up ya comprometido, espera a que el sink converja (verificación por identidad/revisión/operación más igualdad del recuento del índice) dentro de 30 s, cambia el alias de forma atómica y activa la generación; si la autoridad falla tras mover el alias, lo restaura. Fuera de plazo (`lock-timeout`, `sink-not-caught-up`) libera escritores, deja el índice anterior sirviendo y la generación `ready` para reintentar con `SwitchAsync`. `--rebuild-search` (host) lo ejecuta con códigos de salida 0/2/1 y mensajes sin secretos.
- **Evidencia (PostgreSQL16 local + dobles en proceso):** `ProjectionReconciliationTests` 9/9, `SearchGenerationTests` 8/8, `SearchRebuildTests` 7/7 + `SearchAdminHttpTests` 1/1 + `SearchRebuildCommandTests` 2/2. Las mutaciones (lock exclusivo anulado, convergencia anulada) rompen 3 pruebas. Suite completa sin las clases Angular: 256/257 en PostgreSQL16 (fallo conocido `23001`/`23503`). La primera ejecución completa falló 7 pruebas con `FATAL: too many clients already`: cada base temporal de test tenía su propio pool de Npgsql con conexiones inactivas y las ~260 pruebas agotaban `max_connections=100`; el CI (postgres18, mismo valor por defecto) habría fallado igual. `PostgresFixture` ahora crea las cadenas de conexión con `Pooling=false`.
- **No verificado / limitaciones:** el sink, Kafka y Elasticsearch son dobles (`PumpingSink`, `FakeElasticsearch`); falta ejecutar contra Debezium/Connect/ES reales (alta de conector por generación, alias atómico, `_mget` sobre alias, `_count`). No hay monitorización de presión WAL durante el barrido ni suspensión de la retención del outbox para el horizonte de una generación (aún no existe retención del outbox). El recuento por ámbito del diseño se sustituye por verificación de cada identidad más igualdad del total del índice, que implica que no sobran ni faltan documentos en ningún ámbito. La supresión sigue sin disparador (S15).
