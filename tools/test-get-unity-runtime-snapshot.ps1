#requires -Version 7.0

[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$clientPath = Join-Path $PSScriptRoot 'get-unity-runtime-snapshot.ps1'
$probeSourcePath = Join-Path $PSScriptRoot '..\src\Assets\Scripts\Editor\Diagnostics\UnityRuntimeProbe.cs'
if (-not (Test-Path -LiteralPath $clientPath -PathType Leaf)) { throw "Missing client: $clientPath" }
if (-not (Test-Path -LiteralPath $probeSourcePath -PathType Leaf)) { throw "Missing probe source: $probeSourcePath" }
. $clientPath

function Assert-True {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}

function Assert-ThrowsLike {
    param([scriptblock]$Action, [string]$Pattern, [string]$Label)
    try { & $Action; throw "Expected failure: $Label" }
    catch {
        if ($_.Exception.Message -notmatch $Pattern) {
            throw "$Label returned the wrong error. Actual: $($_.Exception.Message)"
        }
    }
}

function New-FakeProject {
    param([string]$Root, [string]$ProductGuid)
    [IO.Directory]::CreateDirectory((Join-Path $Root 'Assets')) | Out-Null
    [IO.Directory]::CreateDirectory((Join-Path $Root 'Library')) | Out-Null
    [IO.Directory]::CreateDirectory((Join-Path $Root 'ProjectSettings')) | Out-Null
    [IO.File]::WriteAllText(
        (Join-Path $Root 'ProjectSettings\ProjectSettings.asset'),
        "PlayerSettings:`n  productGUID: $ProductGuid`n",
        [Text.UTF8Encoding]::new($false))
}

function Start-FakeResponder {
    param(
        [string]$ProjectRoot,
        [int]$ExpectedCount,
        [string]$ResponseProjectPath,
        [string]$ResponseProjectGuid,
        [int]$ResponseProcessId,
        [string]$ResponseProcessStartUtc,
        [string]$ForcedErrorAction
    )
    Start-Job -ScriptBlock {
        param($Root, $Count, $EditorProject, $EditorGuid, $EditorPid, $EditorStart, $ErrorAction)
        $requests = Join-Path $Root 'Library\UnityRuntimeProbe\requests'
        $responses = Join-Path $Root 'Library\UnityRuntimeProbe\responses'
        [IO.Directory]::CreateDirectory($requests) | Out-Null
        [IO.Directory]::CreateDirectory($responses) | Out-Null
        $handled = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        $watch = [Diagnostics.Stopwatch]::StartNew()
        while ($watch.Elapsed.TotalSeconds -lt 15 -and $handled.Count -lt $Count) {
            foreach ($path in @(Get-ChildItem -LiteralPath $requests -Filter '*.json' -File -ErrorAction SilentlyContinue)) {
                $requestId = [IO.Path]::GetFileNameWithoutExtension($path.Name)
                if (-not $handled.Add($requestId)) { continue }
                $request = [IO.File]::ReadAllText($path.FullName) | ConvertFrom-Json
                if ([string]$request.requestId -cne $requestId) { throw 'fake responder saw a filename/requestId mismatch' }
                $isError = -not [string]::IsNullOrWhiteSpace($ErrorAction) -and [string]$request.action -ceq $ErrorAction
                $objects = @()
                if ([string]$request.action -ceq 'hierarchy') {
                    $objects = @([ordered]@{ instanceId = 101; name = 'FakeObject'; scene = 'FakeScene'; hierarchyPath = '/FakeObject'; componentTypes = @('UnityEngine.Transform') })
                }
                elseif ([string]$request.action -ceq 'inspect') {
                    $objects = @([ordered]@{ instanceId = [int]$request.instanceId; name = 'FakeObject'; scene = 'FakeScene'; hierarchyPath = '/FakeObject'; componentTypes = @('UnityEngine.Transform') })
                }
                $response = [ordered]@{
                    schemaVersion = 1
                    requestId = $requestId
                    status = if ($isError) { 'error' } else { 'ok' }
                    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
                    editor = [ordered]@{
                        processId = $EditorPid
                        processStartTimeUtc = $EditorStart
                        projectPath = $EditorProject
                        projectGuid = $EditorGuid
                        unityVersion = '6000.3.18f1'
                        isPlaying = $false
                        isPaused = $false
                        isCompiling = $false
                        activeScene = 'FakeScene'
                    }
                    scenes = @([ordered]@{ name = 'FakeScene'; path = 'Assets/Scenes/FakeScene.unity'; isLoaded = $true; isActive = $true })
                    objects = $objects
                    error = if ($isError) { [ordered]@{ code = 'forced_error'; message = 'Forced fake response.' } } else { $null }
                }
                $temporary = Join-Path $responses ".$requestId.$PID.tmp"
                $final = Join-Path $responses "$requestId.json"
                [IO.File]::WriteAllText($temporary, ($response | ConvertTo-Json -Compress -Depth 20), [Text.UTF8Encoding]::new($false))
                [IO.File]::Move($temporary, $final)
                try { [IO.File]::Delete($path.FullName) } catch [IO.FileNotFoundException] { }
            }
            Start-Sleep -Milliseconds 20
        }
        if ($handled.Count -ne $Count) { throw "fake responder handled $($handled.Count) of $Count request(s)" }
        @($handled)
    } -ArgumentList $ProjectRoot, $ExpectedCount, $ResponseProjectPath, $ResponseProjectGuid, $ResponseProcessId, $ResponseProcessStartUtc, $ForcedErrorAction
}

function Complete-FakeResponder {
    param([System.Management.Automation.Job]$Job)
    $null = Wait-Job -Job $Job -Timeout 20
    if ($Job.State -ne 'Completed') {
        $details = @(Receive-Job -Job $Job -ErrorAction SilentlyContinue) -join "`n"
        throw "Fake responder did not complete: state=$($Job.State); $details"
    }
    @(Receive-Job -Job $Job)
}

function New-FakeProcessResolver {
    param([int]$ExpectedProcessId, [DateTime]$LocalStartTime)
    { param([int]$RequestedProcessId)
        if ($RequestedProcessId -ne $ExpectedProcessId) { throw 'unexpected process id' }
        [pscustomobject]@{ ProcessName = 'Unity'; StartTime = $LocalStartTime }
    }.GetNewClosure()
}

$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) ('tzg-unity-runtime-probe-' + [guid]::NewGuid().ToString('N'))
$jobs = [Collections.Generic.List[System.Management.Automation.Job]]::new()
try {
    $productGuid = 'ABCDEF0123456789ABCDEF0123456789'
    New-FakeProject -Root $fixtureRoot -ProductGuid $productGuid
    $context = Get-UnityRuntimeProbeProjectContext $fixtureRoot
    Assert-True ($context.ProjectGuid -ceq $productGuid.ToLowerInvariant()) 'Project productGUID was not normalized to lowercase.'
    $forwardPath = ($context.ProjectPath -replace '\\', '/') + "/   "
    Assert-True ([string]::Equals((Normalize-UnityRuntimeProbePath $forwardPath), $context.ProjectPath, [StringComparison]::OrdinalIgnoreCase)) 'Forward/backslash project path normalization failed.'
    Write-Host 'PASS normalizes project path separators and productGUID casing'

    $fakePid = 424242
    $fakeStartLocal = [DateTime]::Now.AddMinutes(-2)
    $fakeStartUtc = $fakeStartLocal.ToUniversalTime().ToString('O', [Globalization.CultureInfo]::InvariantCulture)
    $resolver = New-FakeProcessResolver -ExpectedProcessId $fakePid -LocalStartTime $fakeStartLocal

    $job = Start-FakeResponder -ProjectRoot $fixtureRoot -ExpectedCount 1 -ResponseProjectPath $forwardPath -ResponseProjectGuid $productGuid -ResponseProcessId $fakePid -ResponseProcessStartUtc $fakeStartUtc
    $jobs.Add($job)
    $status = Invoke-UnityRuntimeProbeOperation -Context $context -RequestedAction Status -Inactive $false -ResultLimit 100 -WaitSeconds 5 -ProcessResolver $resolver
    Assert-True ([string]$status.editor.activeScene -ceq 'FakeScene') 'Status response was not returned.'
    $handled = @(Complete-FakeResponder $job)
    Assert-True ($handled.Count -eq 1) 'Status request was not handled exactly once.'
    Write-Host 'PASS validates path, GUID, PID, and UTC process start identity'

    $job = Start-FakeResponder -ProjectRoot $fixtureRoot -ExpectedCount 2 -ResponseProjectPath $forwardPath -ResponseProjectGuid $productGuid -ResponseProcessId $fakePid -ResponseProcessStartUtc $fakeStartUtc
    $jobs.Add($job)
    $hierarchy = Invoke-UnityRuntimeProbeOperation -Context $context -RequestedAction Hierarchy -SceneName FakeScene -ContainsName Fake -Inactive $true -ResultLimit 10 -WaitSeconds 5 -ProcessResolver $resolver
    Assert-True (@($hierarchy.objects).Count -eq 1 -and [string]$hierarchy.objects[0].name -ceq 'FakeObject') 'Hierarchy request did not follow a successful status handshake.'
    $handled = @(Complete-FakeResponder $job)
    Assert-True ($handled.Count -eq 2 -and @($handled | Select-Object -Unique).Count -eq 2) 'Handshake and hierarchy request IDs were not isolated.'
    Write-Host 'PASS performs a distinct status handshake before hierarchy'

    $wrongPathJob = Start-FakeResponder -ProjectRoot $fixtureRoot -ExpectedCount 1 -ResponseProjectPath 'C:\wrong-project' -ResponseProjectGuid $productGuid -ResponseProcessId $fakePid -ResponseProcessStartUtc $fakeStartUtc
    $jobs.Add($wrongPathJob)
    Assert-ThrowsLike { Invoke-UnityRuntimeProbeOperation -Context $context -RequestedAction Status -Inactive $false -ResultLimit 100 -WaitSeconds 5 -ProcessResolver $resolver } 'editor_identity_mismatch' 'wrong project identity'
    $null = Complete-FakeResponder $wrongPathJob
    Write-Host 'PASS rejects the wrong Editor project identity'

    $wrongStartJob = Start-FakeResponder -ProjectRoot $fixtureRoot -ExpectedCount 1 -ResponseProjectPath $forwardPath -ResponseProjectGuid $productGuid -ResponseProcessId $fakePid -ResponseProcessStartUtc ([DateTimeOffset]::UtcNow.AddHours(-1).ToString('O'))
    $jobs.Add($wrongStartJob)
    Assert-ThrowsLike { Invoke-UnityRuntimeProbeOperation -Context $context -RequestedAction Status -Inactive $false -ResultLimit 100 -WaitSeconds 5 -ProcessResolver $resolver } 'editor_identity_mismatch' 'wrong process start time'
    $null = Complete-FakeResponder $wrongStartJob
    Write-Host 'PASS rejects a stale or reused Unity PID'

    $errorJob = Start-FakeResponder -ProjectRoot $fixtureRoot -ExpectedCount 1 -ResponseProjectPath $forwardPath -ResponseProjectGuid $productGuid -ResponseProcessId $fakePid -ResponseProcessStartUtc $fakeStartUtc -ForcedErrorAction status
    $jobs.Add($errorJob)
    Assert-ThrowsLike { Invoke-UnityRuntimeProbeOperation -Context $context -RequestedAction Status -Inactive $false -ResultLimit 100 -WaitSeconds 5 -ProcessResolver $resolver } 'forced_error' 'Editor error response'
    $null = Complete-FakeResponder $errorJob
    Write-Host 'PASS propagates a validated Editor error response'

    Assert-ThrowsLike { Invoke-UnityRuntimeProbeOperation -Context $context -RequestedAction Inspect -Inactive $false -ResultLimit 100 -WaitSeconds 1 -ProcessResolver $resolver } 'invalid_selector' 'missing inspect selector'
    Assert-ThrowsLike { Invoke-UnityRuntimeProbeOperation -Context $context -RequestedAction Status -Inactive $false -ResultLimit 100 -WaitSeconds 1 -ProcessResolver $resolver } 'editor_not_connected' 'Editor timeout'
    Assert-True (@(Get-ChildItem -LiteralPath $context.RequestDirectory -Force).Count -eq 0) 'Timed-out request artifacts were not cleaned.'
    Write-Host 'PASS rejects invalid selectors and cleans timed-out requests'

    $raceFile = Join-Path $context.RequestDirectory 'delete-race.tmp'
    [IO.File]::WriteAllText($raceFile, 'x')
    Remove-UnityRuntimeProbeFile $raceFile
    Remove-UnityRuntimeProbeFile $raceFile
    Remove-UnityRuntimeProbeFile (Join-Path $fixtureRoot 'missing-parent\missing.tmp')
    Write-Host 'PASS treats competing file deletion as idempotent'

    $concurrentJob = Start-FakeResponder -ProjectRoot $fixtureRoot -ExpectedCount 2 -ResponseProjectPath $forwardPath -ResponseProjectGuid $productGuid -ResponseProcessId $fakePid -ResponseProcessStartUtc $fakeStartUtc
    $jobs.Add($concurrentJob)
    $workers = 1..2 | ForEach-Object {
        Start-Job -ScriptBlock {
            param($Client, $Root, $PidValue, $LocalStart)
            . $Client
            $ctx = Get-UnityRuntimeProbeProjectContext $Root
            $resolverForWorker = { param([int]$RequestedProcessId) [pscustomobject]@{ ProcessName = 'Unity'; StartTime = $LocalStart } }.GetNewClosure()
            $response = Invoke-UnityRuntimeProbeOperation -Context $ctx -RequestedAction Status -Inactive $false -ResultLimit 100 -WaitSeconds 10 -ProcessResolver $resolverForWorker
            [string]$response.requestId
        } -ArgumentList $clientPath, $fixtureRoot, $fakePid, $fakeStartLocal
    }
    foreach ($worker in $workers) { $jobs.Add($worker) }
    $null = Wait-Job -Job $workers -Timeout 15
    $workerIds = @()
    foreach ($worker in $workers) {
        if ($worker.State -ne 'Completed') { throw "Concurrent client failed: $($worker.State)" }
        $workerIds += @(Receive-Job -Job $worker)
    }
    $handled = @(Complete-FakeResponder $concurrentJob)
    Assert-True ($workerIds.Count -eq 2 -and @($workerIds | Select-Object -Unique).Count -eq 2) 'Concurrent clients did not receive distinct request IDs.'
    Assert-True ($handled.Count -eq 2) 'Fake responder did not serialize both concurrent requests.'
    Write-Host 'PASS isolates two concurrent clients'

    if ($null -eq (Get-Command rg -ErrorAction SilentlyContinue)) { throw 'rg is required for the probe namespace boundary check.' }
    $usingLines = @(& rg --no-heading --no-line-number '^using\s+' $probeSourcePath)
    if ($LASTEXITCODE -ne 0) { throw 'rg could not enumerate probe using directives.' }
    $invalidUsings = @($usingLines | Where-Object { $_ -notmatch '^using\s+(?:static\s+)?(?:System|UnityEditor|UnityEngine)(?:\.|;)' })
    if ($invalidUsings.Count -ne 0) { throw "Probe has forbidden using directives: $($invalidUsings -join '; ')" }
    $null = & rg -n '\bTianZhang(?:\.|\b)' $probeSourcePath
    if ($LASTEXITCODE -eq 0) { throw 'Probe references a TianZhang project namespace.' }
    if ($LASTEXITCODE -ne 1) { throw 'rg failed while checking TianZhang namespace references.' }
    Write-Host 'PASS enforces the probe namespace boundary mechanically'

    Write-Host 'test-get-unity-runtime-snapshot: OK'
}
finally {
    foreach ($job in $jobs) {
        if ($null -ne $job) { Stop-Job -Job $job -ErrorAction SilentlyContinue; Remove-Job -Job $job -Force -ErrorAction SilentlyContinue }
    }
    $tempRoot = Normalize-UnityRuntimeProbePath ([IO.Path]::GetTempPath())
    $resolvedFixture = Normalize-UnityRuntimeProbePath $fixtureRoot
    if ($resolvedFixture.StartsWith($tempRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -and (Test-Path -LiteralPath $resolvedFixture)) {
        Remove-Item -LiteralPath $resolvedFixture -Recurse -Force
    }
}
