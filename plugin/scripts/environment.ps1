$ErrorActionPreference = 'Stop'
$dotnet = $null
$dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
$dotnetCandidates = @()
if ($null -ne $dotnetCommand) { $dotnetCandidates += $dotnetCommand.Source }
$dotnetCandidates += (Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe')
foreach ($candidate in ($dotnetCandidates | Select-Object -Unique)) {
    if ((Test-Path -LiteralPath $candidate) -and ((& $candidate --list-sdks 2>$null) -match '^8\.')) {
        $dotnet = $candidate
        break
    }
}
if ($null -eq $dotnet) { throw 'Install the .NET 8 SDK before building AI-CAD.' }
$autoCadPath = $env:AUTOCAD_PATH
if ([string]::IsNullOrWhiteSpace($autoCadPath)) {
    $autoCadPath = Join-Path $env:ProgramFiles 'Autodesk\AutoCAD 2026'
}
$autoCadPath = [IO.Path]::GetFullPath($autoCadPath)
foreach ($name in @('AcCoreMgd.dll','AcDbMgd.dll','AcMgd.dll','accoreconsole.exe')) {
    if (-not (Test-Path -LiteralPath (Join-Path $autoCadPath $name))) {
        throw "AutoCAD 2026 component not found: $name. Set AUTOCAD_PATH to your AutoCAD installation directory."
    }
}
$env:AUTOCAD_PATH = $autoCadPath
