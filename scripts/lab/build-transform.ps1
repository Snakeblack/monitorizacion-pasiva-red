[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
$taskRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
Push-Location $taskRoot
try {
    if (-not (Get-Command javac -ErrorAction SilentlyContinue)) { throw 'Java compiler 21 or newer is required to build the pure Connect transform.' }
    $taskBuild=Join-Path $taskRoot 'deploy/connect/transforms/build'
    New-Item -ItemType Directory -Force "$taskBuild/classes","$taskBuild/libs" | Out-Null
    $taskImage=(Get-Content deploy/versions.env | Where-Object {$_ -match '^KAFKA_IMAGE='}) -replace '^KAFKA_IMAGE=',''
    $taskContainer='monitoring-transform-build-'+[guid]::NewGuid().ToString('N')
    docker create --name $taskContainer --entrypoint true $taskImage | Out-Null
    if($LASTEXITCODE -ne 0){throw 'Cannot inspect pinned Kafka classpath.'}
    try {
        foreach($taskJar in @('connect-api-4.3.1.jar','kafka-clients-4.3.1.jar')) {
            docker cp "${taskContainer}:/opt/kafka/libs/$taskJar" "$taskBuild/libs/$taskJar"
            if($LASTEXITCODE -ne 0){throw 'Cannot copy pinned Kafka API.'}
        }
    } finally { docker rm $taskContainer | Out-Null }
    javac --release 21 -cp "$taskBuild/libs/*" -d "$taskBuild/classes" deploy/connect/transforms/src/SessionContract.java
    if($LASTEXITCODE -ne 0){throw 'Connect transform compilation failed.'}
    jar --create --file "$taskBuild/session-contract.jar" -C "$taskBuild/classes" .
    if($LASTEXITCODE -ne 0){throw 'Connect transform packaging failed.'}
} finally {Pop-Location}
