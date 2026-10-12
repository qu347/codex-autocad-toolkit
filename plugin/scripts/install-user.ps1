$ErrorActionPreference = 'Stop'

$repoRoot = [System.IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
& (Join-Path $PSScriptRoot 'build.ps1')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

& (Join-Path $PSScriptRoot 'install-package.ps1')
