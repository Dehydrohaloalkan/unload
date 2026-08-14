[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$frontendDirectory = Join-Path $repositoryRoot 'web\webApp'
$previousLocation = Get-Location

# A Windows process must not write Windows obj/node_modules into a WSL checkout.
# Delegate transparently so the command is still launched from PowerShell or CMD.
if ($repositoryRoot -match '^\\\\wsl(?:\.localhost|\$)\\([^\\]+)\\(.+)$') {
    if (-not (Get-Command 'wsl.exe' -ErrorAction SilentlyContinue)) {
        throw 'This checkout is inside WSL, but wsl.exe is not available.'
    }

    $distribution = $Matches[1]
    $linuxRepositoryRoot = '/' + ($Matches[2] -replace '\\', '/')
    & wsl.exe --distribution $distribution --cd $linuxRepositoryRoot bash -ic './tools/verify.sh'
    exit $LASTEXITCODE
}

function Assert-Command {
    param([Parameter(Mandatory)][string]$Name)

    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Required command is not available: $Name"
    }
}

function Invoke-Step {
    param(
        [Parameter(Mandatory)][string]$Label,
        [Parameter(Mandatory)][string]$Command,
        [Parameter()][string[]]$Arguments = @()
    )

    Write-Host "[$Label]"
    & $Command @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Step '$Label' failed with exit code $LASTEXITCODE."
    }
}

try {
    Assert-Command 'dotnet'
    Assert-Command 'node'
    Assert-Command 'npm'

    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $env:DOTNET_NOLOGO = '1'
    $env:NUGET_XMLDOC_MODE = 'skip'

    Set-Location $repositoryRoot
    Invoke-Step 'backend restore' 'dotnet' @('restore', 'unload.slnx')
    Invoke-Step 'backend format and analyzers' 'dotnet' @(
        'format',
        'unload.slnx',
        '--verify-no-changes',
        '--no-restore',
        '--verbosity',
        'minimal'
    )
    Invoke-Step 'backend build' 'dotnet' @('build', 'unload.slnx', '--no-restore')
    Invoke-Step 'backend tests' 'dotnet' @(
        'test',
        'unload.slnx',
        '--no-build',
        '--no-restore'
    )

    Set-Location $frontendDirectory
    Invoke-Step 'frontend dependencies' 'npm' @('ci')
    Invoke-Step 'frontend dependency audit' 'npm' @('audit', '--audit-level=moderate')
    Invoke-Step 'frontend tests and API contract' 'npm' @('test', '--', '--watch=false')
    Invoke-Step 'frontend build' 'npm' @('run', 'build')

    Write-Host 'Verification passed.'
}
finally {
    Set-Location $previousLocation
}
