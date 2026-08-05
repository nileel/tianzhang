#requires -Version 7.0

[CmdletBinding()]
param(
    [ValidateSet('Status', 'Hierarchy', 'Inspect')]
    [string]$Action,
    [string]$ProjectPath = (Join-Path $PSScriptRoot '..\src'),
    [string]$Scene,
    [string]$NameContains,
    [switch]$IncludeInactive,
    [ValidateRange(1, 200)]
    [int]$MaxResults = 100,
    [Nullable[int]]$InstanceId,
    [string]$HierarchyPath,
    [ValidateRange(1, 30)]
    [int]$TimeoutSeconds = 5
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Stop-UnityRuntimeProbe {
    param([string]$Code, [string]$Message)
    throw [InvalidOperationException]::new("${Code}: $Message")
}

function Normalize-UnityRuntimeProbePath {
    param([Parameter(Mandatory = $true)][string]$Path)

    $full = [IO.Path]::GetFullPath($Path.TrimEnd())
    $full = $full.Replace([IO.Path]::AltDirectorySeparatorChar, [IO.Path]::DirectorySeparatorChar)
    $root = [IO.Path]::GetPathRoot($full)
    while ($full.Length -gt $root.Length -and $full[$full.Length - 1] -eq [IO.Path]::DirectorySeparatorChar) {
        $full = $full.Substring(0, $full.Length - 1)
    }
    $full
}

function Get-UnityRuntimeProbeProjectContext {
    param([Parameter(Mandatory = $true)][string]$Path)

    $normalized = Normalize-UnityRuntimeProbePath $Path
    foreach ($required in @('Assets', 'ProjectSettings\ProjectSettings.asset', 'Library')) {
        if (-not (Test-Path -LiteralPath (Join-Path $normalized $required))) {
            Stop-UnityRuntimeProbe 'invalid_project_path' "ProjectPath is missing $required`: $normalized"
        }
    }

    $settingsPath = Join-Path $normalized 'ProjectSettings\ProjectSettings.asset'
    $matches = @([IO.File]::ReadAllLines($settingsPath) | ForEach-Object {
        if ($_ -cmatch '^\s*productGUID:\s*([0-9A-Fa-f]{32})\s*$') { $Matches[1] }
    })
    if ($matches.Count -ne 1) {
        Stop-UnityRuntimeProbe 'invalid_project_guid' 'ProjectSettings.asset must contain exactly one 32-character productGUID.'
    }

    $channelRoot = Join-Path $normalized 'Library\UnityRuntimeProbe'
    $requestDirectory = Join-Path $channelRoot 'requests'
    $responseDirectory = Join-Path $channelRoot 'responses'
    [IO.Directory]::CreateDirectory($requestDirectory) | Out-Null
    [IO.Directory]::CreateDirectory($responseDirectory) | Out-Null
    [pscustomobject]@{
        ProjectPath = $normalized
        ProjectGuid = ([string]$matches[0]).ToLowerInvariant()
        RequestDirectory = $requestDirectory
        ResponseDirectory = $responseDirectory
    }
}

function Remove-UnityRuntimeProbeFile {
    param([AllowNull()][string]$Path)
    if ([string]::IsNullOrWhiteSpace($Path)) { return }
    try { [IO.File]::Delete($Path) }
    catch [IO.FileNotFoundException] { }
    catch [IO.DirectoryNotFoundException] { }
}

function Remove-UnityRuntimeProbeArtifacts {
    param(
        [Parameter(Mandatory = $true)]$Context,
        [Parameter(Mandatory = $true)][string]$RequestId,
        [string]$RequestTemporary,
        [string]$RequestPath,
        [string]$ResponsePath
    )

    foreach ($path in @($RequestTemporary, $RequestPath, $ResponsePath)) {
        Remove-UnityRuntimeProbeFile $path
    }
    foreach ($directory in @($Context.RequestDirectory, $Context.ResponseDirectory)) {
        try {
            foreach ($file in @(Get-ChildItem -LiteralPath $directory -File -Filter ".$RequestId.*.tmp" -ErrorAction Stop)) {
                Remove-UnityRuntimeProbeFile $file.FullName
            }
        }
        catch [IO.DirectoryNotFoundException] { }
    }
}

function New-UnityRuntimeProbeEnvelope {
    param(
        [Parameter(Mandatory = $true)][ValidateSet('status', 'hierarchy', 'inspect')][string]$ProbeAction,
        [string]$SceneName,
        [string]$ContainsName,
        [bool]$Inactive,
        [int]$ResultLimit,
        [Nullable[int]]$ObjectInstanceId,
        [string]$ObjectHierarchyPath,
        [ValidateRange(1, 30)][int]$LifetimeSeconds
    )

    $now = [DateTimeOffset]::UtcNow
    [pscustomobject]@{
        RequestId = [guid]::NewGuid().ToString('N').ToLowerInvariant()
        Payload = [ordered]@{
            schemaVersion = 1
            requestId = $null
            clientProcessId = $PID
            createdAtUtc = $now.ToString('O', [Globalization.CultureInfo]::InvariantCulture)
            expiresAtUtc = $now.AddSeconds($LifetimeSeconds).ToString('O', [Globalization.CultureInfo]::InvariantCulture)
            action = $ProbeAction
            scene = if ([string]::IsNullOrWhiteSpace($SceneName)) { $null } else { $SceneName }
            nameContains = if ([string]::IsNullOrWhiteSpace($ContainsName)) { $null } else { $ContainsName }
            includeInactive = $Inactive
            maxResults = $ResultLimit
            instanceId = if ($null -eq $ObjectInstanceId) { $null } else { [int]$ObjectInstanceId }
            hierarchyPath = if ([string]::IsNullOrWhiteSpace($ObjectHierarchyPath)) { $null } else { $ObjectHierarchyPath }
        }
    }
}

function Invoke-UnityRuntimeProbeChannelRequest {
    param(
        [Parameter(Mandatory = $true)]$Context,
        [Parameter(Mandatory = $true)][ValidateSet('status', 'hierarchy', 'inspect')][string]$ProbeAction,
        [string]$SceneName,
        [string]$ContainsName,
        [bool]$Inactive,
        [int]$ResultLimit,
        [Nullable[int]]$ObjectInstanceId,
        [string]$ObjectHierarchyPath,
        [ValidateRange(1, 30)][int]$WaitSeconds
    )

    $envelope = New-UnityRuntimeProbeEnvelope -ProbeAction $ProbeAction -SceneName $SceneName -ContainsName $ContainsName -Inactive $Inactive -ResultLimit $ResultLimit -ObjectInstanceId $ObjectInstanceId -ObjectHierarchyPath $ObjectHierarchyPath -LifetimeSeconds $WaitSeconds
    $requestId = $envelope.RequestId
    $envelope.Payload.requestId = $requestId
    $requestTemporary = Join-Path $Context.RequestDirectory ".$requestId.$PID.tmp"
    $requestPath = Join-Path $Context.RequestDirectory "$requestId.json"
    $responsePath = Join-Path $Context.ResponseDirectory "$requestId.json"
    try {
        $json = $envelope.Payload | ConvertTo-Json -Compress -Depth 8
        [IO.File]::WriteAllText($requestTemporary, $json, [Text.UTF8Encoding]::new($false))
        [IO.File]::Move($requestTemporary, $requestPath)

        $stopwatch = [Diagnostics.Stopwatch]::StartNew()
        while ($stopwatch.Elapsed.TotalSeconds -lt $WaitSeconds) {
            if (Test-Path -LiteralPath $responsePath -PathType Leaf) {
                try {
                    $responseText = [Text.UTF8Encoding]::new($false, $true).GetString([IO.File]::ReadAllBytes($responsePath))
                    $response = $responseText | ConvertFrom-Json -Depth 40
                }
                catch {
                    Stop-UnityRuntimeProbe 'invalid_response' "Response JSON could not be read: $($_.Exception.Message)"
                }
                if ([int]$response.schemaVersion -ne 1 -or [string]$response.requestId -cne $requestId) {
                    Stop-UnityRuntimeProbe 'invalid_response' 'Response schemaVersion or requestId does not match the request.'
                }
                return $response
            }
            Start-Sleep -Milliseconds 50
        }
        Stop-UnityRuntimeProbe 'editor_not_connected' "No response arrived within $WaitSeconds second(s). Ensure Unity has the same ProjectPath open and the probe has compiled."
    }
    finally {
        Remove-UnityRuntimeProbeArtifacts -Context $Context -RequestId $requestId -RequestTemporary $requestTemporary -RequestPath $requestPath -ResponsePath $responsePath
    }
}

function Test-UnityRuntimeProbeIdentity {
    param(
        [Parameter(Mandatory = $true)]$Response,
        [Parameter(Mandatory = $true)]$Context,
        [scriptblock]$ProcessResolver = { param([int]$RequestedProcessId) Get-Process -Id $RequestedProcessId -ErrorAction Stop }
    )

    if ($null -eq $Response.editor) { Stop-UnityRuntimeProbe 'editor_identity_mismatch' 'Response has no editor identity.' }
    $editor = $Response.editor
    try { $responseProject = Normalize-UnityRuntimeProbePath ([string]$editor.projectPath) }
    catch { Stop-UnityRuntimeProbe 'editor_identity_mismatch' 'Editor projectPath is invalid.' }
    if (-not [string]::Equals($responseProject, $Context.ProjectPath, [StringComparison]::OrdinalIgnoreCase)) {
        Stop-UnityRuntimeProbe 'editor_identity_mismatch' "Editor projectPath does not match ProjectPath. Editor=$responseProject; Client=$($Context.ProjectPath)"
    }

    $responseGuid = ([string]$editor.projectGuid).ToLowerInvariant()
    if ($responseGuid -cnotmatch '^[0-9a-f]{32}$' -or $responseGuid -cne $Context.ProjectGuid) {
        Stop-UnityRuntimeProbe 'editor_identity_mismatch' 'Editor projectGuid does not match ProjectSettings.productGUID.'
    }

    try { $process = & $ProcessResolver ([int]$editor.processId) }
    catch { Stop-UnityRuntimeProbe 'editor_identity_mismatch' "Unity process $($editor.processId) is not running." }
    if ($null -eq $process -or @($process).Count -ne 1 -or [string]$process.ProcessName -ine 'Unity') {
        Stop-UnityRuntimeProbe 'editor_identity_mismatch' 'Editor processId does not identify exactly one Unity process.'
    }

    try {
        $responseStartValue = $editor.processStartTimeUtc
        $responseStart = if ($responseStartValue -is [DateTimeOffset]) {
            $responseStartValue.UtcDateTime.Ticks
        }
        elseif ($responseStartValue -is [DateTime]) {
            $responseStartValue.ToUniversalTime().Ticks
        }
        else {
            [DateTimeOffset]::Parse([string]$responseStartValue, [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::RoundtripKind).UtcDateTime.Ticks
        }
        $actualStart = ([DateTime]$process.StartTime).ToUniversalTime().Ticks
    }
    catch { Stop-UnityRuntimeProbe 'editor_identity_mismatch' 'Editor processStartTimeUtc is invalid.' }
    if ($responseStart -ne $actualStart) {
        Stop-UnityRuntimeProbe 'editor_identity_mismatch' 'Editor process start time does not match the running Unity process.'
    }

    [pscustomobject]@{
        ProcessId = [int]$editor.processId
        ProcessStartUtcTicks = $responseStart
        ProjectPath = $responseProject
        ProjectGuid = $responseGuid
    }
}

function Assert-UnityRuntimeProbeStableIdentity {
    param([Parameter(Mandatory = $true)]$Handshake, [Parameter(Mandatory = $true)]$Actual)
    if ($Handshake.ProcessId -ne $Actual.ProcessId -or
        $Handshake.ProcessStartUtcTicks -ne $Actual.ProcessStartUtcTicks -or
        $Handshake.ProjectGuid -cne $Actual.ProjectGuid -or
        -not [string]::Equals($Handshake.ProjectPath, $Actual.ProjectPath, [StringComparison]::OrdinalIgnoreCase)) {
        Stop-UnityRuntimeProbe 'editor_changed' 'Unity Editor identity changed after the status handshake.'
    }
}

function Assert-UnityRuntimeProbeResponseSucceeded {
    param([Parameter(Mandatory = $true)]$Response)
    if ([string]$Response.status -cne 'ok') {
        $code = if ($null -ne $Response.error -and -not [string]::IsNullOrWhiteSpace([string]$Response.error.code)) { [string]$Response.error.code } else { 'probe_error' }
        $message = if ($null -ne $Response.error) { [string]$Response.error.message } else { 'Unity probe returned an error.' }
        Stop-UnityRuntimeProbe $code $message
    }
}

function Invoke-UnityRuntimeProbeOperation {
    param(
        [Parameter(Mandatory = $true)]$Context,
        [Parameter(Mandatory = $true)][ValidateSet('Status', 'Hierarchy', 'Inspect')][string]$RequestedAction,
        [string]$SceneName,
        [string]$ContainsName,
        [bool]$Inactive,
        [int]$ResultLimit,
        [Nullable[int]]$ObjectInstanceId,
        [string]$ObjectHierarchyPath,
        [ValidateRange(1, 30)][int]$WaitSeconds,
        [scriptblock]$ProcessResolver = { param([int]$RequestedProcessId) Get-Process -Id $RequestedProcessId -ErrorAction Stop }
    )

    if ($RequestedAction -eq 'Inspect') {
        $byId = $null -ne $ObjectInstanceId -and [int]$ObjectInstanceId -ne 0
        $byPath = -not [string]::IsNullOrWhiteSpace($SceneName) -and -not [string]::IsNullOrWhiteSpace($ObjectHierarchyPath) -and $ObjectHierarchyPath.StartsWith('/')
        if ($byId -eq $byPath) { Stop-UnityRuntimeProbe 'invalid_selector' 'Inspect requires InstanceId or Scene plus HierarchyPath, but not both.' }
    }

    if ($RequestedAction -eq 'Status') {
        $response = Invoke-UnityRuntimeProbeChannelRequest -Context $Context -ProbeAction status -Inactive $false -ResultLimit 100 -WaitSeconds $WaitSeconds
        $null = Test-UnityRuntimeProbeIdentity -Response $response -Context $Context -ProcessResolver $ProcessResolver
        Assert-UnityRuntimeProbeResponseSucceeded $response
        return $response
    }

    $handshakeResponse = Invoke-UnityRuntimeProbeChannelRequest -Context $Context -ProbeAction status -Inactive $false -ResultLimit 100 -WaitSeconds $WaitSeconds
    $handshake = Test-UnityRuntimeProbeIdentity -Response $handshakeResponse -Context $Context -ProcessResolver $ProcessResolver
    Assert-UnityRuntimeProbeResponseSucceeded $handshakeResponse
    $probeAction = $RequestedAction.ToLowerInvariant()
    $response = Invoke-UnityRuntimeProbeChannelRequest -Context $Context -ProbeAction $probeAction -SceneName $SceneName -ContainsName $ContainsName -Inactive $Inactive -ResultLimit $ResultLimit -ObjectInstanceId $ObjectInstanceId -ObjectHierarchyPath $ObjectHierarchyPath -WaitSeconds $WaitSeconds
    $actual = Test-UnityRuntimeProbeIdentity -Response $response -Context $Context -ProcessResolver $ProcessResolver
    Assert-UnityRuntimeProbeStableIdentity -Handshake $handshake -Actual $actual
    Assert-UnityRuntimeProbeResponseSucceeded $response
    $response
}

if ($MyInvocation.InvocationName -ne '.') {
    try {
        if ([string]::IsNullOrWhiteSpace($Action)) { Stop-UnityRuntimeProbe 'invalid_arguments' 'Action is required.' }
        $context = Get-UnityRuntimeProbeProjectContext $ProjectPath
        $response = Invoke-UnityRuntimeProbeOperation -Context $context -RequestedAction $Action -SceneName $Scene -ContainsName $NameContains -Inactive ([bool]$IncludeInactive) -ResultLimit $MaxResults -ObjectInstanceId $InstanceId -ObjectHierarchyPath $HierarchyPath -WaitSeconds $TimeoutSeconds
        [Console]::Out.WriteLine(($response | ConvertTo-Json -Compress -Depth 40))
        exit 0
    }
    catch {
        [Console]::Error.WriteLine($_.Exception.Message)
        exit 1
    }
}
