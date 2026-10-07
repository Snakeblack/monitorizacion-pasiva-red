[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$taskRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
Push-Location $taskRoot
try {
    # global.json pins 10.0.303 with rollForward latestFeature, so a CI runner may carry a newer 10.0 SDK; the published DLL is portable for the pinned runtime.
    if (-not (dotnet --version).Trim().StartsWith('10.0.')) { throw 'A .NET 10.0 SDK is required.' }
    New-Item -ItemType Directory -Force lab-secrets,deploy/connect/vendor | Out-Null
    foreach ($taskName in @('postgres-admin','postgres-app','postgres-cdc','keycloak-lab')) {
        $taskPath = Join-Path $taskRoot "lab-secrets/$taskName.txt"
        if (-not (Test-Path -LiteralPath $taskPath)) {
            [IO.File]::WriteAllText($taskPath, [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32)))
        }
    }
    # Laboratory identity-provider TLS (ADR-025): a throw-away CA and one server certificate for Keycloak, named for the three ways the lab reaches it
    # (the browser's loopback address, the Compose service name and localhost). Both keys stay under lab-secrets/ (ignored by git); delete the
    # directory to issue new ones. This is not the production PKI (EJBCA, task 3.3).
    $taskTls = Join-Path $taskRoot 'lab-secrets/tls'
    if (-not (Test-Path -LiteralPath (Join-Path $taskTls 'keycloak.pem'))) {
        New-Item -ItemType Directory -Force $taskTls | Out-Null
        $taskHash = [Security.Cryptography.HashAlgorithmName]::SHA256
        $taskPadding = [Security.Cryptography.RSASignaturePadding]::Pkcs1
        $taskCaKey = [Security.Cryptography.RSA]::Create(3072)
        $taskCaRequest = [Security.Cryptography.X509Certificates.CertificateRequest]::new('CN=Monitoring lab identity CA', $taskCaKey, $taskHash, $taskPadding)
        $taskCaRequest.CertificateExtensions.Add([Security.Cryptography.X509Certificates.X509BasicConstraintsExtension]::new($true, $false, 0, $true))
        $taskCaRequest.CertificateExtensions.Add([Security.Cryptography.X509Certificates.X509KeyUsageExtension]::new('KeyCertSign, CrlSign', $true))
        $taskCaRequest.CertificateExtensions.Add([Security.Cryptography.X509Certificates.X509SubjectKeyIdentifierExtension]::new($taskCaRequest.PublicKey, $false))
        $taskCa = $taskCaRequest.CreateSelfSigned([DateTimeOffset]::UtcNow.AddMinutes(-5), [DateTimeOffset]::UtcNow.AddYears(2))
        $taskServerKey = [Security.Cryptography.RSA]::Create(2048)
        $taskServerRequest = [Security.Cryptography.X509Certificates.CertificateRequest]::new('CN=keycloak', $taskServerKey, $taskHash, $taskPadding)
        $taskNames = [Security.Cryptography.X509Certificates.SubjectAlternativeNameBuilder]::new()
        $taskNames.AddDnsName('keycloak'); $taskNames.AddDnsName('localhost'); $taskNames.AddIpAddress([Net.IPAddress]::Parse('127.0.0.1'))
        $taskServerRequest.CertificateExtensions.Add($taskNames.Build())
        $taskServerRequest.CertificateExtensions.Add([Security.Cryptography.X509Certificates.X509BasicConstraintsExtension]::new($false, $false, 0, $false))
        $taskServerRequest.CertificateExtensions.Add([Security.Cryptography.X509Certificates.X509KeyUsageExtension]::new('DigitalSignature, KeyEncipherment', $true))
        $taskServerOids = [Security.Cryptography.OidCollection]::new(); [void]$taskServerOids.Add([Security.Cryptography.Oid]::new('1.3.6.1.5.5.7.3.1'))
        $taskServerRequest.CertificateExtensions.Add([Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension]::new($taskServerOids, $false))
        $taskSerial = [byte[]]::new(16); [Security.Cryptography.RandomNumberGenerator]::Fill($taskSerial); $taskSerial[0] = $taskSerial[0] -band 0x7F
        $taskServer = $taskServerRequest.Create($taskCa, [DateTimeOffset]::UtcNow.AddMinutes(-5), [DateTimeOffset]::UtcNow.AddYears(1), $taskSerial)
        [IO.File]::WriteAllText((Join-Path $taskTls 'ca.pem'), $taskCa.ExportCertificatePem())
        [IO.File]::WriteAllText((Join-Path $taskTls 'keycloak.pem'), $taskServer.ExportCertificatePem())
        [IO.File]::WriteAllText((Join-Path $taskTls 'keycloak.key'), $taskServerKey.ExportPkcs8PrivateKeyPem())
    }
    $taskPlugins = @(
      @{file='debezium-connector-postgres-3.6.3.Final-plugin.tar.gz';hash='68dfbd3aa0e22cbc164017311449f69dfe6b96b70e72c72c7a3a24ef5c7810e0';url='https://repo.maven.apache.org/maven2/io/debezium/debezium-connector-postgres/3.6.3.Final/debezium-connector-postgres-3.6.3.Final-plugin.tar.gz'},
      @{file='jmx_prometheus_javaagent-1.0.1.jar';hash='7d61f737fd661610ccc14aea79764faa1ea94a340cbc8f0029b3d2edea3d80c1';url='https://repo.maven.apache.org/maven2/io/prometheus/jmx/jmx_prometheus_javaagent/1.0.1/jmx_prometheus_javaagent-1.0.1.jar'},
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
