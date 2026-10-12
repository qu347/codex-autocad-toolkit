$ErrorActionPreference = 'Stop'

$repoRoot = [System.IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$bundleRoot = [System.IO.Path]::GetFullPath((Join-Path $repoRoot 'deploy\AiCad.bundle'))
$manifestPath = Join-Path $bundleRoot 'PackageContents.xml'
$windowsContent = Join-Path $bundleRoot 'Contents\Windows'
$bridgeContent = Join-Path $bundleRoot 'Contents\Bridge'

function Assert-FileExists {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Required package file is missing: $Path"
    }
}

function Assert-UnderRoot {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Root
    )

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $fullRoot = [System.IO.Path]::GetFullPath($Root).TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    if (-not $fullPath.StartsWith($fullRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Path is outside its allowed root: $fullPath"
    }
}

Assert-UnderRoot -Path $bundleRoot -Root $repoRoot
Assert-FileExists -Path $manifestPath

try {
    [xml]$manifest = Get-Content -Raw -LiteralPath $manifestPath
}
catch {
    throw "PackageContents.xml is not valid XML: $($_.Exception.Message)"
}

$runtime = $manifest.ApplicationPackage.Components.RuntimeRequirements
if ($null -eq $runtime) { throw 'Package manifest has no RuntimeRequirements element.' }
if ($runtime.OS -ne 'Win64' -or $runtime.Platform -ne 'AutoCAD*') {
    throw 'Package runtime must be limited to Win64 AutoCAD.'
}
if ($runtime.SeriesMin -ne 'R25.1' -or $runtime.SeriesMax -ne 'R25.1') {
    throw 'Package runtime must be locked to AutoCAD 2026 series R25.1.'
}

$component = $manifest.ApplicationPackage.Components.ComponentEntry
if ($null -eq $component) { throw 'Package manifest has no ComponentEntry.' }
if ($component.ModuleName -ne './Contents/Windows/AiCad.AutoCAD.dll') {
    throw 'Package component points to an unexpected module.'
}
if ($component.LoadReasons.LoadOnAutoCADStartup -ne 'True') {
    throw 'Package component must load when AutoCAD starts.'
}

foreach ($name in @('AiCad.AutoCAD.dll', 'AiCad.Contracts.dll', 'AiCad.Mechanical.dll')) {
    Assert-FileExists -Path (Join-Path $windowsContent $name)
}

$forbidden = Get-ChildItem -LiteralPath $bundleRoot -Recurse -File -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -in @('AcMgd.dll', 'AcDbMgd.dll', 'AcCoreMgd.dll') }
if ($forbidden) {
    throw "Autodesk runtime assemblies must not be bundled: $($forbidden.FullName -join ', ')"
}

Assert-FileExists -Path (Join-Path $bridgeContent 'AiCad.Bridge.exe')
Assert-FileExists -Path (Join-Path $bridgeContent 'AiCad.Bridge.dll')

'AICAD_PACKAGE_VALID'
