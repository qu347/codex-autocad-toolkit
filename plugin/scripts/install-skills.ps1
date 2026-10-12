$ErrorActionPreference = 'Stop'
$toolkitRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$codexDirectory = $env:CODEX_HOME
if ([string]::IsNullOrWhiteSpace($codexDirectory)) { $codexDirectory = Join-Path $env:USERPROFILE '.codex' }
$skillsRoot = [IO.Path]::GetFullPath((Join-Path $codexDirectory 'skills'))
$prefix = $skillsRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
New-Item -ItemType Directory -Path $skillsRoot -Force | Out-Null
foreach ($name in @('autocad-mcp-drawing','autocad-mcp-review')) {
    $source = Join-Path $toolkitRoot "skills\$name"
    if (-not (Test-Path -LiteralPath (Join-Path $source 'SKILL.md'))) { throw "Missing skill: $name" }
    $target = [IO.Path]::GetFullPath((Join-Path $skillsRoot $name))
    if (-not $target.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid skill install path.' }
    $backup = $null
    if (Test-Path -LiteralPath $target) {
        # Put backups outside discovery's skills directory.
        $backupRoot = Join-Path $codexDirectory 'skill-backups'
        New-Item -ItemType Directory -Path $backupRoot -Force | Out-Null
        $backup = Join-Path $backupRoot ($name + '-' + [Guid]::NewGuid().ToString('N'))
        Move-Item -LiteralPath $target -Destination $backup
        Write-Output "Skill backup: $backup"
    }
    try { Copy-Item -LiteralPath $source -Destination $target -Recurse }
    catch {
        if (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target -Recurse -Force }
        if ($null -ne $backup) { Move-Item -LiteralPath $backup -Destination $target }
        throw
    }
    Write-Output "Skill installed: $target"
}
