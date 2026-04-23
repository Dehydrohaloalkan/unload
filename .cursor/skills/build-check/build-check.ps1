$ErrorActionPreference = 'Stop'

function Invoke-Step {
  param(
    [Parameter(Mandatory = $true)][string]$Name,
    [Parameter(Mandatory = $true)][scriptblock]$Action
  )

  Write-Host "==> $Name"
  & $Action
  if ($LASTEXITCODE -ne 0) {
    throw "$Name failed with exit code $LASTEXITCODE"
  }
}

Invoke-Step -Name 'Backend: dotnet build (repo root)' -Action {
  dotnet build
}

Invoke-Step -Name 'Frontend: npm run build (web/webApp)' -Action {
  Push-Location -Path (Join-Path $PSScriptRoot '..\..\..\web\webApp')
  try {
    npm run build
  } finally {
    Pop-Location
  }
}

Write-Host 'All builds succeeded.'

