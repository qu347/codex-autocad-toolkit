$ErrorActionPreference = 'Stop'

$repoRoot = [System.IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
& (Join-Path $PSScriptRoot 'build.ps1')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$source = [System.IO.Path]::GetFullPath((Join-Path $repoRoot 'deploy\AiCad.bundle'))
$pluginsRoot = [System.IO.Path]::GetFullPath((Join-Path $env:APPDATA 'Autodesk\ApplicationPlugins'))
$destination = [System.IO.Path]::GetFullPath((Join-Path $pluginsRoot 'AiCad.bundle'))
$allowedPrefix = $pluginsRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
if (-not $destination.StartsWith($allowedPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to install outside the current user's Autodesk ApplicationPlugins directory: $destination"
}

New-Item -ItemType Directory -Path $pluginsRoot -Force | Out-Null
if (Test-Path -LiteralPath $destination) {
    $timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $backup = Join-Path $pluginsRoot "AiCad.bundle.backup-$timestamp"
    Move-Item -LiteralPath $destination -Destination $backup
    Write-Output "Previous AI-CAD bundle moved to: $backup"
}

Copy-Item -LiteralPath $source -Destination $destination -Recurse
Write-Output "AI-CAD installed for the current user: $destination"
Write-Output "Codex MCP executable: $(Join-Path $destination 'Contents\Bridge\AiCad.Bridge.exe')"
