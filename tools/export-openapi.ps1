[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot

# A Windows process must not write Windows obj files into a WSL checkout.
# Delegate transparently so the command is still launched from PowerShell or CMD.
if ($repositoryRoot -match '^\\\\wsl(?:\.localhost|\$)\\([^\\]+)\\(.+)$') {
    if (-not (Get-Command 'wsl.exe' -ErrorAction SilentlyContinue)) {
        throw 'This checkout is inside WSL, but wsl.exe is not available.'
    }

    $distribution = $Matches[1]
    $linuxRepositoryRoot = '/' + ($Matches[2] -replace '\\', '/')
    & wsl.exe --distribution $distribution --cd $linuxRepositoryRoot bash -ic './tools/export-openapi.sh'
    exit $LASTEXITCODE
}

$apiProject = Join-Path $repositoryRoot 'backend\Unload.Api\Unload.Api.csproj'
$schemaDirectory = Join-Path $repositoryRoot 'openapi'
$schemaPath = Join-Path $schemaDirectory 'Unload.Api.json'
$listenUrl = 'http://127.0.0.1:5099'
$temporarySchema = [System.IO.Path]::GetTempFileName()
$standardOutputLog = [System.IO.Path]::GetTempFileName()
$standardErrorLog = [System.IO.Path]::GetTempFileName()
$apiProcess = $null

function Assert-Command {
    param([Parameter(Mandatory)][string]$Name)

    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Required command is not available: $Name"
    }
}

function Test-TcpPort {
    param(
        [Parameter(Mandatory)][string]$HostName,
        [Parameter(Mandatory)][int]$Port
    )

    $client = New-Object System.Net.Sockets.TcpClient
    try {
        $connection = $client.ConnectAsync($HostName, $Port)
        if (-not $connection.Wait(500)) {
            return $false
        }

        return $client.Connected
    }
    catch {
        return $false
    }
    finally {
        $client.Dispose()
    }
}

function Write-ApiLog {
    foreach ($path in @($standardOutputLog, $standardErrorLog)) {
        if ((Test-Path -LiteralPath $path) -and (Get-Item -LiteralPath $path).Length -gt 0) {
            [Console]::Error.WriteLine((Get-Content -LiteralPath $path -Raw))
        }
    }
}

function Restore-EnvironmentValue {
    param(
        [Parameter(Mandatory)][string]$Name,
        [AllowNull()][string]$Value
    )

    if ($null -eq $Value) {
        Remove-Item "Env:$Name" -ErrorAction SilentlyContinue
    }
    else {
        Set-Item "Env:$Name" $Value
    }
}

try {
    Assert-Command 'dotnet'
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $env:DOTNET_NOLOGO = '1'

    if (Test-TcpPort '127.0.0.1' 5099) {
        throw 'Port 5099 is already in use; OpenAPI export was not started.'
    }

    & dotnet build $apiProject --nologo
    if ($LASTEXITCODE -ne 0) {
        throw "API build failed with exit code $LASTEXITCODE."
    }

    $previousOpenApiMode = [Environment]::GetEnvironmentVariable('OpenApiGenerationOnly')
    $previousEnvironment = [Environment]::GetEnvironmentVariable('ASPNETCORE_ENVIRONMENT')
    $previousUrls = [Environment]::GetEnvironmentVariable('ASPNETCORE_URLS')

    try {
        $env:OpenApiGenerationOnly = 'true'
        $env:ASPNETCORE_ENVIRONMENT = 'Development'
        $env:ASPNETCORE_URLS = $listenUrl

        $apiArguments = "run --project `"$apiProject`" --no-build --no-launch-profile"
        $apiProcess = Start-Process `
            -FilePath 'dotnet' `
            -ArgumentList $apiArguments `
            -WorkingDirectory $repositoryRoot `
            -RedirectStandardOutput $standardOutputLog `
            -RedirectStandardError $standardErrorLog `
            -WindowStyle Hidden `
            -PassThru
    }
    finally {
        Restore-EnvironmentValue 'OpenApiGenerationOnly' $previousOpenApiMode
        Restore-EnvironmentValue 'ASPNETCORE_ENVIRONMENT' $previousEnvironment
        Restore-EnvironmentValue 'ASPNETCORE_URLS' $previousUrls
    }

    $exported = $false
    for ($attempt = 0; $attempt -lt 30; $attempt++) {
        try {
            Invoke-WebRequest `
                -Uri "$listenUrl/openapi/v1.json" `
                -OutFile $temporarySchema `
                -UseBasicParsing `
                -TimeoutSec 2

            New-Item -ItemType Directory -Path $schemaDirectory -Force | Out-Null
            Move-Item -LiteralPath $temporarySchema -Destination $schemaPath -Force
            $temporarySchema = $null
            $exported = $true
            Write-Host "OpenAPI schema exported to $schemaPath"
            break
        }
        catch {
            if ($apiProcess.HasExited) {
                Write-ApiLog
                throw 'The API process exited before the OpenAPI schema was available.'
            }

            Start-Sleep -Seconds 1
        }
    }

    if (-not $exported) {
        Write-ApiLog
        throw 'Timed out waiting for the OpenAPI endpoint.'
    }
}
finally {
    if ($null -ne $apiProcess -and -not $apiProcess.HasExited) {
        Stop-Process -Id $apiProcess.Id -ErrorAction SilentlyContinue
        $apiProcess.WaitForExit(5000) | Out-Null
    }

    foreach ($path in @($temporarySchema, $standardOutputLog, $standardErrorLog)) {
        if ($null -ne $path -and (Test-Path -LiteralPath $path)) {
            Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue
        }
    }
}
