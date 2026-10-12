$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
& (Join-Path $PSScriptRoot 'verify-package.ps1')

$runtimeAvailable = $false
$runtimeCandidates = @((Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'), (Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe'))
$runtimeCommand = Get-Command dotnet -ErrorAction SilentlyContinue
if ($null -ne $runtimeCommand) { $runtimeCandidates += $runtimeCommand.Source }
foreach ($candidate in ($runtimeCandidates | Select-Object -Unique)) {
    if ((Test-Path -LiteralPath $candidate) -and ((& $candidate --list-runtimes 2>$null) -match '^Microsoft.NETCore.App 8\.')) {
        $runtimeAvailable = $true
        break
    }
}
if (-not $runtimeAvailable) { throw 'Install the .NET 8 x64 Runtime before using the compiled AI-CAD package.' }
if (Get-Process acad,accoreconsole -ErrorAction SilentlyContinue) {
    throw 'Close AutoCAD before updating its AI-CAD plugin; open drawings are never closed automatically.'
}

$source = [IO.Path]::GetFullPath((Join-Path $repoRoot 'deploy\AiCad.bundle'))
$pluginsRoot = [IO.Path]::GetFullPath((Join-Path $env:APPDATA 'Autodesk\ApplicationPlugins'))
$destination = [IO.Path]::GetFullPath((Join-Path $pluginsRoot 'AiCad.bundle'))
$allowedPrefix = $pluginsRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if (-not $destination.StartsWith($allowedPrefix, [StringComparison]::OrdinalIgnoreCase)) { throw "Invalid plugin destination: $destination" }
New-Item -ItemType Directory -Path $pluginsRoot -Force | Out-Null
$backup = $null
if (Test-Path -LiteralPath $destination) {
    $backup = Join-Path $pluginsRoot ('AiCad.bundle.backup-' + [Guid]::NewGuid().ToString('N'))
    if (-not $backup.StartsWith($allowedPrefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid backup path.' }
    Move-Item -LiteralPath $destination -Destination $backup
    Write-Output "Previous bundle backed up: $backup"
}
try {
    Copy-Item -LiteralPath $source -Destination $destination -Recurse
}
catch {
    if (Test-Path -LiteralPath $destination) { Remove-Item -LiteralPath $destination -Recurse -Force }
    if ($null -ne $backup) { Move-Item -LiteralPath $backup -Destination $destination }
    throw
}
Write-Output "AI-CAD 1.1.0 installed: $destination"
Write-Output "STDIO command: $(Join-Path $destination 'Contents\Bridge\AiCad.Bridge.exe')"
Write-Output 'Restart AutoCAD and refresh the Codex MCP server, then call cad_health and cad_get_context.'
