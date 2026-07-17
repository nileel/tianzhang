#requires -Version 7.0

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot 'test-helpers.ps1')

$modulePath = Join-Path (Split-Path -Parent $PSScriptRoot) 'protocol.psm1'
if (-not (Test-Path -LiteralPath $modulePath -PathType Leaf)) {
  throw 'protocol.psm1 is missing'
}
Import-Module $modulePath -Force -DisableNameChecking

$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/')
$sandbox = Join-Path $tempRoot ('tzg-hourly-controller-v2-protocol-' + [guid]::NewGuid().ToString('N'))
$repositoryRoot = Join-Path $sandbox 'repo'
$outsideRoot = Join-Path $sandbox 'outside'
$junctionPath = Join-Path $repositoryRoot 'linked'
$originalUserProfile = $env:USERPROFILE

try {
  [IO.Directory]::CreateDirectory((Join-Path $repositoryRoot 'docs\角色 养成')) | Out-Null
  [IO.Directory]::CreateDirectory($outsideRoot) | Out-Null

  $response = New-ControllerResponse `
    -Action 'Start' `
    -RunId '11111111-1111-1111-1111-111111111111' `
    -TaskId 'TQ-057' `
    -Phase 'DISCOVERING' `
    -NextAction 'RecordTitleResult'
  $fieldNames = @($response.PSObject.Properties.Name) -join '|'
  Assert-Equal $fieldNames 'schemaVersion|ok|action|runId|taskId|phase|nextAction|errorCode|changedPaths|requiredSources|requiredChecks|decisionConstraints|result' 'stable response fields'
  Assert-Equal $response.schemaVersion 1 'response schema version'
  Assert-True ([bool]$response.ok) 'successful response ok'

  Assert-Throws `
    -Script { New-ControllerResponse -Action 'Start' -ErrorCode 'not_registered' } `
    -MessageLike 'Unknown controller error code' `
    -Label 'unknown error code'

  $redactedResponse = New-ControllerResponse `
    -Action 'Show' `
    -Result ([ordered]@{
      safe = 'visible'
      openId = 'private-open-id'
      nested = [ordered]@{
        rawEvent = 'private-event'
        values = @([ordered]@{ evidenceHash = 'private-hash' })
      }
    })
  $stdout = @(& { Write-ControllerResponse -Response $redactedResponse })
  Assert-Equal $stdout.Count 1 'stdout line count'
  Assert-False ([bool]($stdout[0] -match "[`r`n]")) 'stdout embedded newline'
  $written = $stdout[0] | ConvertFrom-Json
  Assert-Equal $written.result.safe 'visible' 'allowed response field'
  Assert-Equal $written.result.openId '[REDACTED]' 'top-level nested redaction'
  Assert-Equal $written.result.nested.rawEvent '[REDACTED]' 'deep redaction'
  Assert-Equal $written.result.nested.values[0].evidenceHash '[REDACTED]' 'array redaction'

  $legalPath = Normalize-ProjectPath -Path 'docs/角色 养成/术法.txt' -RepositoryRoot $repositoryRoot
  Assert-Equal $legalPath 'docs/角色 养成/术法.txt' 'legal Chinese project path'

  $invalidPaths = @(
    '.',
    'docs//file.txt',
    'docs/./file.txt',
    'docs/../file.txt',
    '../file.txt',
    'docs\file.txt',
    'docs/file.txt/',
    (Join-Path $repositoryRoot 'docs\file.txt')
  )
  foreach ($invalidPath in $invalidPaths) {
    Assert-Throws `
      -Script { Normalize-ProjectPath -Path $invalidPath -RepositoryRoot $repositoryRoot } `
      -MessageLike 'Invalid project path' `
      -Label "invalid path $invalidPath"
  }

  New-Item -ItemType Junction -Path $junctionPath -Target $outsideRoot -ErrorAction Stop | Out-Null
  Assert-Throws `
    -Script { Normalize-ProjectPath -Path 'linked/escape.txt' -RepositoryRoot $repositoryRoot } `
    -MessageLike 'reparse point' `
    -Label 'junction escape'

  Assert-Equal (Get-Sha256Text -Text 'abc') 'ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad' 'UTF-8 SHA-256'

  $fakeUserProfile = Join-Path $sandbox 'user'
  $env:USERPROFILE = $fakeUserProfile
  $privateRoot = Join-Path $fakeUserProfile '.codex\automation-state'
  [IO.Directory]::CreateDirectory($privateRoot) | Out-Null
  $requestPath = Join-Path $privateRoot 'request.json'
  Write-TestUtf8 -Path $requestPath -Value '{"schemaVersion":1,"action":"Start"}'
  $request = Read-ControllerRequest -Path $requestPath
  Assert-Equal $request.schemaVersion 1 'request schema'
  Assert-Equal $request.action 'Start' 'request payload'

  $wrongSchemaPath = Join-Path $privateRoot 'wrong-schema.json'
  Write-TestUtf8 -Path $wrongSchemaPath -Value '{"schemaVersion":2}'
  Assert-Throws `
    -Script { Read-ControllerRequest -Path $wrongSchemaPath } `
    -MessageLike 'schemaVersion' `
    -Label 'wrong request schema'

  $arrayPath = Join-Path $privateRoot 'array.json'
  Write-TestUtf8 -Path $arrayPath -Value '[]'
  Assert-Throws `
    -Script { Read-ControllerRequest -Path $arrayPath } `
    -MessageLike 'JSON object' `
    -Label 'array request'

  $invalidUtf8Path = Join-Path $privateRoot 'invalid-utf8.json'
  [IO.File]::WriteAllBytes($invalidUtf8Path, [byte[]](0xC3, 0x28))
  Assert-Throws `
    -Script { Read-ControllerRequest -Path $invalidUtf8Path } `
    -MessageLike 'UTF-8' `
    -Label 'invalid UTF-8 request'

  $outsideRequest = Join-Path $outsideRoot 'request.json'
  Write-TestUtf8 -Path $outsideRequest -Value '{"schemaVersion":1}'
  Assert-Throws `
    -Script { Read-ControllerRequest -Path $outsideRequest } `
    -MessageLike 'private state root' `
    -Label 'request outside private root'

  Assert-Throws `
    -Script { Read-ControllerRequest -Path 'request.json' } `
    -MessageLike 'absolute' `
    -Label 'relative request path'

  Write-Output 'protocol.tests: OK'
} finally {
  $env:USERPROFILE = $originalUserProfile
  if (Test-Path -LiteralPath $junctionPath) {
    Remove-Item -LiteralPath $junctionPath -Force
  }
  $resolvedSandbox = [IO.Path]::GetFullPath($sandbox)
  if ($resolvedSandbox.StartsWith($tempRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -and
      (Test-Path -LiteralPath $resolvedSandbox)) {
    [IO.Directory]::Delete($resolvedSandbox, $true)
  }
}
