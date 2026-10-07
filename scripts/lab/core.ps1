[CmdletBinding()]
param([ValidateSet('up','down','health','test')][string]$Action='up', [string]$Project='monitoring-product-demo')
$ErrorActionPreference = 'Stop'
if ($Project -notmatch '^monitoring-[a-z0-9-]+$') { throw 'Use a task-owned monitoring-* Compose project.' }
$taskRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
Push-Location $taskRoot
try {
    $taskCompose = @('compose','--env-file','deploy/versions.env','-p',$Project,'-f','compose.yaml')
    function Invoke-CoreCompose([string[]]$Arguments) {
        # Docker Desktop's daemon is used on Windows unless the older Ubuntu-WSL daemon is requested explicitly.
        if ($IsWindows -and $env:MONITORING_COMPOSE_VIA_WSL -eq '1') {
            wsl.exe -d Ubuntu --cd $taskRoot env -u DOCKER_CONTEXT DOCKER_HOST=tcp://127.0.0.1:2375 TESTCONTAINERS_HOST_OVERRIDE=127.0.0.1 docker @taskCompose @Arguments
        } else { docker @taskCompose @Arguments }
        if ($LASTEXITCODE -ne 0) { throw 'Docker Compose action failed.' }
    }
    switch ($Action) {
        'up' { & "$PSScriptRoot/prepare.ps1"; Invoke-CoreCompose -Arguments @('up','--build','--wait','--wait-timeout','300') }
        'down' { Invoke-CoreCompose -Arguments @('down','--timeout','30') }
        'health' { Invoke-CoreCompose -Arguments @('ps','--all','--format','{{.Service}} {{.State}} {{.Health}} {{.ExitCode}}') }
        'test' { & "$PSScriptRoot/prepare.ps1"; $env:MONITORING_COMPOSE_PROJECT=$Project; node --test tests/stack/foundation.test.mjs }
    }
    if ($LASTEXITCODE -ne 0) { throw "Core action failed: $Action" }
} finally { Pop-Location }
