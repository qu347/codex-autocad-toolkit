$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot "environment.ps1")

& $dotnet restore "$repoRoot\AiCad.sln"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

& $dotnet build "$repoRoot\AiCad.sln" -c Release --no-restore
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

& $dotnet test "$repoRoot\AiCad.sln" -c Release --no-build
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

& $dotnet publish "$repoRoot\src\AiCad.Bridge\AiCad.Bridge.csproj" -c Release -r win-x64 --self-contained false
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$bundleRoot = [System.IO.Path]::GetFullPath((Join-Path $repoRoot 'deploy\AiCad.bundle'))
$windowsContent = [System.IO.Path]::GetFullPath((Join-Path $bundleRoot 'Contents\Windows'))
$bridgeContent = [System.IO.Path]::GetFullPath((Join-Path $bundleRoot 'Contents\Bridge'))
$allowedPrefix = $bundleRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
foreach ($target in @($windowsContent, $bridgeContent)) {
    if (-not $target.StartsWith($allowedPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to rebuild package content outside the bundle: $target"
    }

    if (Test-Path -LiteralPath $target) {
        Remove-Item -LiteralPath $target -Recurse -Force
    }

    New-Item -ItemType Directory -Path $target -Force | Out-Null
}

$autoCadOutput = Join-Path $repoRoot 'src\AiCad.AutoCAD\bin\Release\net8.0-windows'
foreach ($name in @(
    'AiCad.AutoCAD.dll',
    'AiCad.AutoCAD.deps.json',
    'AiCad.Contracts.dll',
    'AiCad.Mechanical.dll')) {
    Copy-Item -LiteralPath (Join-Path $autoCadOutput $name) -Destination $windowsContent
}

$bridgePublish = Join-Path $repoRoot 'src\AiCad.Bridge\bin\Release\net8.0\win-x64\publish'
Copy-Item -Path (Join-Path $bridgePublish '*') -Destination $bridgeContent -Recurse

& (Join-Path $PSScriptRoot 'verify-package.ps1')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
