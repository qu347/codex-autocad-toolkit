$ErrorActionPreference = 'Stop'

function Invoke-CadRpc {
    param(
        [Parameter(Mandatory = $true)]$Descriptor,
        [Parameter(Mandatory = $true)][string]$Method,
        $Payload = @{}
    )

    $messageId = [Guid]::NewGuid().ToString()
    $request = [ordered]@{
        messageId = $messageId
        method = $Method
        sessionToken = $Descriptor.sessionToken
        payload = $Payload
    } | ConvertTo-Json -Compress -Depth 8

    $options = [System.IO.Pipes.PipeOptions]::Asynchronous -bor [System.IO.Pipes.PipeOptions]::CurrentUserOnly
    $pipe = New-Object System.IO.Pipes.NamedPipeClientStream('.', $Descriptor.pipeName, [System.IO.Pipes.PipeDirection]::InOut, $options)
    try {
        $pipe.Connect(5000)
        $writer = New-Object System.IO.StreamWriter($pipe, (New-Object System.Text.UTF8Encoding($false)), 1024, $true)
        $reader = New-Object System.IO.StreamReader($pipe, [System.Text.Encoding]::UTF8, $false, 1024, $true)
        try {
            $writer.NewLine = "`n"
            $writer.WriteLine($request)
            $writer.Flush()
            $readTask = $reader.ReadLineAsync()
            if (-not $readTask.Wait(5000)) { throw "RPC $Method timed out while reading." }
            $line = $readTask.Result
        }
        finally {
            $writer.Dispose()
            $reader.Dispose()
        }
    }
    finally {
        $pipe.Dispose()
    }

    $response = $line | ConvertFrom-Json
    if ($response.messageId -ne $messageId) { throw 'RPC message identifier mismatch.' }
    if (-not $response.success) { throw "RPC $Method failed: $($response.error.code) $($response.error.message)" }
    return $response.result
}

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
. (Join-Path $repoRoot "scripts\environment.ps1")
& $dotnet build "$repoRoot\src\AiCad.AutoCAD\AiCad.AutoCAD.csproj" -c Release --no-restore
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$testOutput = Join-Path $repoRoot 'TestResults\AutoCAD'
New-Item -ItemType Directory -Path $testOutput -Force | Out-Null
$inputDwg = Join-Path $testOutput 'pipe-input.dwg'
$pluginPath = Join-Path $repoRoot 'src\AiCad.AutoCAD\bin\Release\net8.0-windows\AiCad.AutoCAD.dll'
Copy-Item -LiteralPath (Join-Path $autoCadPath 'Express\brkline.dwg') -Destination $inputDwg -Force

$startInfo = New-Object System.Diagnostics.ProcessStartInfo
$startInfo.FileName = (Join-Path $autoCadPath 'accoreconsole.exe')
$startInfo.Arguments = "/i `"$inputDwg`" /l en-US"
$startInfo.WorkingDirectory = $testOutput
$startInfo.UseShellExecute = $false
$startInfo.CreateNoWindow = $true
$startInfo.RedirectStandardInput = $true
$startInfo.RedirectStandardOutput = $true
$startInfo.RedirectStandardError = $true
$startInfo.StandardOutputEncoding = [System.Text.Encoding]::Unicode
$startInfo.StandardErrorEncoding = [System.Text.Encoding]::Unicode
$process = [System.Diagnostics.Process]::Start($startInfo)
$outputTask = $process.StandardOutput.ReadToEndAsync()
$errorTask = $process.StandardError.ReadToEndAsync()
$descriptorPath = Join-Path (Join-Path $env:LOCALAPPDATA 'AiCad\sessions') "$($process.Id).json"

try {
    $process.StandardInput.WriteLine('(setq aicadOldTrustedPaths (getvar "TRUSTEDPATHS"))')
    $process.StandardInput.WriteLine("(setvar `"TRUSTEDPATHS`" (strcat aicadOldTrustedPaths `";$((Split-Path $pluginPath).Replace('\','/'))`"))")
    $process.StandardInput.WriteLine('_.NETLOAD')
    $process.StandardInput.WriteLine($pluginPath)
    $process.StandardInput.WriteLine('(setvar "TRUSTEDPATHS" aicadOldTrustedPaths)')
    $process.StandardInput.Flush()

    $deadline = [DateTime]::UtcNow.AddSeconds(10)
    while (-not (Test-Path -LiteralPath $descriptorPath) -and [DateTime]::UtcNow -lt $deadline) {
        Start-Sleep -Milliseconds 100
    }
    if (-not (Test-Path -LiteralPath $descriptorPath)) {
        throw 'AutoCAD session descriptor was not created.'
    }

    $descriptor = Get-Content -Raw -LiteralPath $descriptorPath | ConvertFrom-Json
    $process.StandardInput.WriteLine('AICADWAITFORRPC')
    $process.StandardInput.Flush()
    $health = Invoke-CadRpc -Descriptor $descriptor -Method 'health'
    if ($health.protocolVersion -ne '1.0') { throw 'Unexpected health protocol version.' }
    if ($health.autoCadProcessId -ne $process.Id) { throw 'Health process identifier mismatch.' }

    $process.StandardInput.WriteLine('AICADWAITFORRPC')
    $process.StandardInput.Flush()
    $context = Invoke-CadRpc -Descriptor $descriptor -Method 'get_context'
    if ([string]::IsNullOrWhiteSpace($context.documentFingerprint)) { throw 'Document fingerprint was empty.' }
    if ($context.drawingName -ne 'pipe-input.dwg') { throw "Unexpected drawing name '$($context.drawingName)'." }
    if ($context.currentUcs.Count -ne 16) { throw 'Current UCS matrix did not contain 16 values.' }

    $requestId = '44444444-4444-4444-4444-444444444444'
    $plan = [ordered]@{
        schemaVersion = '1.0'
        requestId = $requestId
        documentFingerprint = $context.documentFingerprint
        unit = 'millimeter'
        origin = @{ x = 0; y = 0; z = 0 }
        declaredOperation = 'create'
        steps = @(
            [ordered]@{
                '$type' = 'rounded_plate'
                semanticId = 'plate'
                operation = 'create'
                width = 100
                depth = 60
                thickness = 10
                cornerRadius = 5
            },
            [ordered]@{
                '$type' = 'four_hole_pattern'
                semanticId = 'mounting-holes'
                operation = 'create'
                targetSemanticId = 'plate'
                diameter = 8
                edgeOffsetX = 10
                edgeOffsetY = 10
            }
        )
        output = @{ create3D = $true; create2D = $true; addDimensions = $true }
        assumptions = @()
    }

    $process.StandardInput.WriteLine('AICADWAITFORRPC')
    $process.StandardInput.Flush()
    $created = Invoke-CadRpc -Descriptor $descriptor -Method 'create_rounded_plate' -Payload $plan
    if ([string]::IsNullOrWhiteSpace($created.batchId)) { throw 'Create RPC returned no batch identifier.' }

    $process.StandardInput.WriteLine('AICADWAITFORRPC')
    $process.StandardInput.Flush()
    $status = Invoke-CadRpc -Descriptor $descriptor -Method 'get_request_status' -Payload @{ requestId = $requestId }
    if ($status.status -ne 'completed' -or $status.result.batchId -ne $created.batchId) {
        throw 'Request status did not return the created batch.'
    }

    $process.StandardInput.WriteLine('AICADWAITFORRPC')
    $process.StandardInput.Flush()
    $cancelProposal = Invoke-CadRpc -Descriptor $descriptor -Method 'propose_hole_diameter_change' -Payload @{
        batchId = $created.batchId
        newHoleDiameter = 10
    }
    $process.StandardInput.WriteLine('AICADWAITFORRPC')
    $process.StandardInput.Flush()
    $cancelled = Invoke-CadRpc -Descriptor $descriptor -Method 'cancel_change' -Payload @{
        changeId = $cancelProposal.changeId
        approvalToken = $cancelProposal.approvalToken
    }
    if (-not $cancelled.cancelled) { throw 'Cancel RPC did not invalidate the proposal.' }

    $process.StandardInput.WriteLine('AICADWAITFORRPC')
    $process.StandardInput.Flush()
    $applyProposal = Invoke-CadRpc -Descriptor $descriptor -Method 'propose_hole_diameter_change' -Payload @{
        batchId = $created.batchId
        newHoleDiameter = 10
    }
    $process.StandardInput.WriteLine('AICADWAITFORRPC')
    $process.StandardInput.Flush()
    $modified = Invoke-CadRpc -Descriptor $descriptor -Method 'apply_change' -Payload @{
        changeId = $applyProposal.changeId
        approvalToken = $applyProposal.approvalToken
    }
    if ($modified.batchId -eq $created.batchId) { throw 'Apply RPC did not create a new modification batch.' }

    $process.StandardInput.WriteLine('AICADWAITFORRPC')
    $process.StandardInput.Flush()
    $undone = Invoke-CadRpc -Descriptor $descriptor -Method 'undo_batch' -Payload @{ batchId = $modified.batchId }
    if (-not $undone.success -or $undone.undoneBatchId -ne $modified.batchId) {
        throw 'Undo RPC did not undo the modification batch.'
    }

    $process.StandardInput.WriteLine('_.QUIT')
    $process.StandardInput.WriteLine('_Y')
    $process.StandardInput.Flush()
    $process.StandardInput.Close()
    if (-not $process.WaitForExit(10000)) { throw 'AutoCAD Core Console did not exit.' }
    if ($process.ExitCode -ne 0) { throw "AutoCAD Core Console exited with $($process.ExitCode)." }
    if (Test-Path -LiteralPath $descriptorPath) { throw 'AutoCAD session descriptor was not removed on shutdown.' }

    'AICAD_TEST_PASS pipe_end_to_end'
}
finally {
    if (-not $process.HasExited) {
        $process.Kill()
        $process.WaitForExit()
    }
    $process.Dispose()
    $null = $outputTask.Result
    $null = $errorTask.Result
}
