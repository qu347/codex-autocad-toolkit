$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
. (Join-Path $repoRoot "scripts\environment.ps1")

& $dotnet build "$repoRoot\src\AiCad.AutoCAD\AiCad.AutoCAD.csproj" -c Release
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$testOutput = Join-Path $repoRoot 'TestResults\AutoCAD'
New-Item -ItemType Directory -Path $testOutput -Force | Out-Null
$inputDwg = Join-Path $testOutput 'status-input.dwg'
$scriptPath = Join-Path $testOutput 'status.scr'
$pluginPath = Join-Path $repoRoot 'src\AiCad.AutoCAD\bin\Release\net8.0-windows\AiCad.AutoCAD.dll'
Copy-Item -LiteralPath (Join-Path $autoCadPath 'Express\brkline.dwg') -Destination $inputDwg -Force

$commands = @(
    '(setq aicadOldTrustedPaths (getvar "TRUSTEDPATHS"))',
    "(setvar `"TRUSTEDPATHS`" (strcat aicadOldTrustedPaths `";$((Split-Path $pluginPath).Replace('\','/'))`"))",
    '_.NETLOAD',
    $pluginPath,
    '(setvar "TRUSTEDPATHS" aicadOldTrustedPaths)',
    'AICADSTATUS',
    '_.QUIT',
    '_Y'
)
Set-Content -LiteralPath $scriptPath -Value $commands -Encoding Ascii

$startInfo = New-Object System.Diagnostics.ProcessStartInfo
$startInfo.FileName = (Join-Path $autoCadPath 'accoreconsole.exe')
$startInfo.Arguments = "/i `"$inputDwg`" /s `"$scriptPath`" /l en-US"
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
$coreExit = $process.ExitCode
$output
$errorOutput
if ($coreExit -ne 0) { exit $coreExit }
if ($output -notmatch 'AICAD_READY') {
    throw 'AutoCAD integration status sentinel AICAD_READY was not found.'
}
