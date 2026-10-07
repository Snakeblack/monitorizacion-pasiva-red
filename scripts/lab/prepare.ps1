[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$taskRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
Push-Location $taskRoot
try {
    # global.json pins 10.0.303 with rollForward latestFeature, so a CI runner may carry a newer 10.0 SDK; the published DLL is portable for the pinned runtime.
    if (-not (dotnet --version).Trim().StartsWith('10.0.')) { throw 'A .NET 10.0 SDK is required.' }
    New-Item -ItemType Directory -Force lab-secrets,deploy/connect/vendor | Out-Null
    foreach ($taskName in @('postgres-admin','postgres-app','postgres-cdc')) {
        $taskPath = Join-Path $taskRoot "lab-secrets/$taskName.txt"
        if (-not (Test-Path -LiteralPath $taskPath)) {
            [IO.File]::WriteAllText($taskPath, [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32)))
        }
    }
    $taskPlugins = @(
      @{file='debezium-connector-postgres-3.6.3.Final-plugin.tar.gz';hash='68dfbd3aa0e22cbc164017311449f69dfe6b96b70e72c72c7a3a24ef5c7810e0';url='https://repo.maven.apache.org/maven2/io/debezium/debezium-connector-postgres/3.6.3.Final/debezium-connector-postgres-3.6.3.Final-plugin.tar.gz'},
      @{file='confluentinc-kafka-connect-elasticsearch-16.0.0.zip';hash='3e658470966e1b419c349850a9db9da124fd8764e4d93710bb49b202ab51ba1e';url='https://hub-downloads.confluent.io/api/plugins/confluentinc/kafka-connect-elasticsearch/versions/16.0.0/confluentinc-kafka-connect-elasticsearch-16.0.0.zip'}
    )
    foreach ($taskPlugin in $taskPlugins) {
        $taskPath = Join-Path $taskRoot "deploy/connect/vendor/$($taskPlugin.file)"
        if (-not (Test-Path -LiteralPath $taskPath)) { Invoke-WebRequest -Uri $taskPlugin.url -OutFile $taskPath }
        if ((Get-FileHash -LiteralPath $taskPath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $taskPlugin.hash) {
            throw "Plugin checksum failed: $($taskPlugin.file)"
        }
    }
    & "$PSScriptRoot/build-transform.ps1"
    # Official registry has no SDK303 image; exact host SDK produces a portable DLL for pinned runtime10.0.12.
    dotnet publish src/Monitoring.Host/Monitoring.Host.csproj -c Release -p:UseAppHost=false -o .ospec/runtime/producto-monitorizacion-final/publish
    if ($LASTEXITCODE -ne 0) { throw 'API publish failed.' }
    Copy-Item -LiteralPath deploy/api/entrypoint.sh,deploy/api/health.sh -Destination .ospec/runtime/producto-monitorizacion-final/publish
} finally { Pop-Location }
