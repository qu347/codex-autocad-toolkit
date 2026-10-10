$ErrorActionPreference = 'Stop'

$pluginsRoot = [System.IO.Path]::GetFullPath((Join-Path $env:APPDATA 'Autodesk\ApplicationPlugins'))
$destination = [System.IO.Path]::GetFullPath((Join-Path $pluginsRoot 'AiCad.bundle'))
$allowedPrefix = $pluginsRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
if (-not $destination.StartsWith($allowedPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to uninstall outside the current user's Autodesk ApplicationPlugins directory: $destination"
}

if (Test-Path -LiteralPath $destination) {
    Remove-Item -LiteralPath $destination -Recurse -Force
    Write-Output "AI-CAD removed from: $destination"
}
else {
    Write-Output 'AI-CAD is not installed for the current user.'
}

$backups = Get-ChildItem -LiteralPath $pluginsRoot -Directory -Filter 'AiCad.bundle.backup-*' -ErrorAction SilentlyContinue
if ($backups) {
    Write-Output 'Backup bundles were left untouched:'
    $backups.FullName
}
