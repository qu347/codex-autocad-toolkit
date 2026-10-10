$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
. (Join-Path $repoRoot "scripts\environment.ps1")
& $dotnet build "$repoRoot\src\AiCad.AutoCAD\AiCad.AutoCAD.csproj" -c Release --no-restore
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$testOutput = Join-Path $repoRoot 'TestResults\AutoCAD'
New-Item -ItemType Directory -Path $testOutput -Force | Out-Null
$inputDwg = Join-Path $testOutput 'rounded-plate-input.dwg'
$generatedScript = Join-Path $testOutput 'rounded-plate.scr'
$pluginPath = Join-Path $repoRoot 'src\AiCad.AutoCAD\bin\Release\net8.0-windows\AiCad.AutoCAD.dll'
Copy-Item -LiteralPath (Join-Path $autoCadPath 'Express\brkline.dwg') -Destination $inputDwg -Force

$template = Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot 'rounded-plate.scr')
$template.Replace('__PLUGIN_PATH__', $pluginPath).Replace('__PLUGIN_DIR__', (Split-Path $pluginPath).Replace('\','/')) | Set-Content -LiteralPath $generatedScript -Encoding Ascii

$startInfo = New-Object System.Diagnostics.ProcessStartInfo
$startInfo.FileName = (Join-Path $autoCadPath 'accoreconsole.exe')
$startInfo.Arguments = "/i `"$inputDwg`" /s `"$generatedScript`" /l en-US"
$startInfo.WorkingDirectory = $testOutput
$startInfo.UseShellExecute = $false
$startInfo.CreateNoWindow = $true
$startInfo.RedirectStandardOutput = $true
$startInfo.RedirectStandardError = $true
$startInfo.StandardOutputEncoding = [System.Text.Encoding]::Unicode
$startInfo.StandardErrorEncoding = [System.Text.Encoding]::Unicode
$process = [System.Diagnostics.Process]::Start($startInfo)
$output = $process.StandardOutput.ReadToEnd()
$errorOutput = $process.StandardError.ReadToEnd()
$process.WaitForExit()
$output
$errorOutput
if ($process.ExitCode -ne 0) { exit $process.ExitCode }
foreach ($sentinel in @(
    'plate2d3d',
    'approval_cancel',
    'approval_apply',
    'duplicate_request',
    'batch_undo',
    'native_undo_group')) {
    if ($output -notmatch "AICAD_TEST_PASS $sentinel") {
        throw "AutoCAD integration sentinel AICAD_TEST_PASS $sentinel was not found."
    }
}
