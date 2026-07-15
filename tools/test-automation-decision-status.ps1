$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$tool = Join-Path $root 'tools\automation-decision-status.ps1'
$sandbox = Join-Path ([IO.Path]::GetTempPath()) ('tzg-decision-status-test-' + [guid]::NewGuid().ToString('N'))
$statusPath = Join-Path $sandbox 'status.txt'
$engine = (Get-Process -Id $PID).Path
$utf8NoBom = [Text.UTF8Encoding]::new($false)
$utf8Bom = [Text.UTF8Encoding]::new($true)

function Invoke-Publisher {
  param([string[]]$Arguments)
  $previousPreference = $ErrorActionPreference
  $ErrorActionPreference = 'Continue'
  try {
    $output = & $engine -NoProfile -ExecutionPolicy Bypass -File $tool @Arguments 2>&1
    [pscustomobject]@{ Code = $LASTEXITCODE; Output = ($output -join "`n") }
  } finally {
    $ErrorActionPreference = $previousPreference
  }
}

function Assert-Code {
  param($Result, [int]$Expected, [string]$Label)
  if ($Result.Code -ne $Expected) {
    throw "$Label expected exit $Expected but got $($Result.Code): $($Result.Output)"
  }
}

function ConvertTo-PayloadBase64 {
  param([Parameter(Mandatory = $true)]$Payload)
  $json = $Payload | ConvertTo-Json -Depth 12 -Compress
  [Convert]::ToBase64String($utf8NoBom.GetBytes($json))
}

function Write-BomFixture {
  param([string]$Content)
  [IO.File]::WriteAllText($statusPath, $Content, $utf8Bom)
}

function Get-StatusHash {
  (Get-FileHash -Algorithm SHA256 -LiteralPath $statusPath).Hash
}

function Get-StatusText {
  $bytes = [IO.File]::ReadAllBytes($statusPath)
  $offset = if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) { 3 } else { 0 }
  $utf8NoBom.GetString($bytes, $offset, $bytes.Length - $offset)
}

function New-ResolvedDecision {
  param(
    [string]$DecisionId,
    [string]$OptionKey,
    [ValidateSet('email','manual')]
    [string]$Source
  )
  [ordered]@{
    decisionId = $DecisionId
    question = '旧问题应该如何处理？'
    resolution = [ordered]@{
      optionKey = $OptionKey
      source = $Source
      resolvedAt = '2026-07-15T03:25:00+08:00'
    }
  }
}

function New-Flow {
  param(
    [ValidateSet('AWAITING_DECISION','IMPLEMENTATION_PENDING')]
    [string]$Status,
    [object[]]$ResolvedDecisions
  )
  [ordered]@{
    taskKind = 'execution'
    taskId = 'TQ-057'
    openedAt = '2026-07-15T03:20:00+08:00'
    status = $Status
    resolvedDecisions = @($ResolvedDecisions)
  }
}

function New-PendingDecision {
  param([string]$Status = 'PENDING')
  [ordered]@{
    decisionId = 'DEC-20260715-SECOND222222'
    createdAt = '2026-07-15T03:30:19+08:00'
    taskId = 'TQ-057'
    taskSummary = '清理现存数据矛盾'
    question = '双倍率字段采用哪种兼容口径？'
    options = @(
      [ordered]@{ key = 'A'; label = '保留双倍率字段' },
      [ordered]@{ key = 'B'; label = '合并为统一倍率' }
    )
    recommendedOption = 'A'
    status = $Status
  }
}

$before = "# 自动工作流状态（测试）`r`n`r`n## 使用规则`r`n`r`n保持不变。`r`n`r`n## 当前待决策`r`n"
$emptyBody = "`r`n当前无待决策项。`r`n"
$after = "`r`n## 最近有效结果`r`n`r`n| 字段 | 值 |`r`n|------|----|`r`n| 测试 | 保持不变 |`r`n"
$emptyFixture = $before + $emptyBody + $after
$firstResolved = New-ResolvedDecision 'DEC-20260715-FIRST111111' 'B' 'manual'

New-Item -ItemType Directory -Path $sandbox | Out-Null
try {
  Write-BomFixture $emptyFixture
  $pendingPayload = [ordered]@{
    pendingDecision = New-PendingDecision 'PENDING'
    decisionFlow = New-Flow 'AWAITING_DECISION' @($firstResolved)
  }
  $published = Invoke-Publisher @(
    'PublishPending',
    '-StatusPath', $statusPath,
    '-DecisionStateJsonBase64', (ConvertTo-PayloadBase64 $pendingPayload)
  )
  Assert-Code $published 0 'publish chained pending decision'

  $bytes = [IO.File]::ReadAllBytes($statusPath)
  if ($bytes[0] -ne 0xEF -or $bytes[1] -ne 0xBB -or $bytes[2] -ne 0xBF) { throw 'publisher did not preserve the UTF-8 BOM' }
  $text = Get-StatusText
  if (-not $text.StartsWith($before, [StringComparison]::Ordinal) -or -not $text.EndsWith($after, [StringComparison]::Ordinal)) {
    throw 'publisher changed content outside the pending-decision section'
  }
  if (($text -replace "`r`n", '').Contains("`n", [StringComparison]::Ordinal)) { throw 'publisher introduced a non-CRLF newline' }
  foreach ($required in @(
    'DEC-20260715-SECOND222222',
    'TQ-057',
    '双倍率字段采用哪种兼容口径？',
    '选项 A：保留双倍率字段',
    '选项 B：合并为统一倍率',
    '推荐项：A',
    '通知状态：尚未尝试发送',
    '严格回复：`DEC-20260715-SECOND222222：选 A`',
    '已登记选择：第一项=B（manual）'
  )) {
    if (-not $text.Contains($required, [StringComparison]::Ordinal)) { throw "published section lacks: $required" }
  }
  foreach ($forbidden in @('旧问题应该如何处理？','DEC-20260715-FIRST111111：选 B','test-recipient@example.com','evidenceHash','providerMessageId')) {
    if ($text.Contains($forbidden, [StringComparison]::OrdinalIgnoreCase)) { throw "published section exposed stale or sensitive content: $forbidden" }
  }

  $notificationLabels = [ordered]@{
    PENDING = '尚未尝试发送'
    PROVIDER_ACCEPTED = '发送请求已被提供方接受（不代表已收件）'
    DELIVERY_FAILED = '发送失败，可重试'
    MISADDRESSED = 'Sent 目标不一致，未完成通知'
    RETRY_EXHAUSTED = '已达三次尝试上限，等待人工处理'
  }
  foreach ($entry in $notificationLabels.GetEnumerator()) {
    $labelPayload = [ordered]@{
      pendingDecision = New-PendingDecision $entry.Key
      decisionFlow = New-Flow 'AWAITING_DECISION' @($firstResolved)
    }
    $labelResult = Invoke-Publisher @(
      'PublishPending',
      '-StatusPath', $statusPath,
      '-DecisionStateJsonBase64', (ConvertTo-PayloadBase64 $labelPayload)
    )
    Assert-Code $labelResult 0 "publish notification state $($entry.Key)"
    if (-not (Get-StatusText).Contains("通知状态：$($entry.Value)", [StringComparison]::Ordinal)) {
      throw "notification state $($entry.Key) did not render its exact label"
    }
  }

  $secondResolved = New-ResolvedDecision 'DEC-20260715-SECOND222222' 'A' 'email'
  $implementationPayload = [ordered]@{
    pendingDecision = $null
    decisionFlow = New-Flow 'IMPLEMENTATION_PENDING' @($firstResolved, $secondResolved)
  }
  $implementation = Invoke-Publisher @(
    'PublishImplementationPending',
    '-StatusPath', $statusPath,
    '-DecisionStateJsonBase64', (ConvertTo-PayloadBase64 $implementationPayload)
  )
  Assert-Code $implementation 0 'publish implementation-pending flow'
  $implementationText = Get-StatusText
  foreach ($required in @(
    'TQ-057',
    '等待原任务实施',
    '已登记选择：第一项=B（manual）',
    '第二项=A（email）',
    'DEC-20260715-FIRST111111',
    'DEC-20260715-SECOND222222'
  )) {
    if (-not $implementationText.Contains($required, [StringComparison]::Ordinal)) { throw "implementation section lacks: $required" }
  }
  foreach ($forbidden in @('严格回复','证据','通知尝试','evidenceHash','provider','message','@')) {
    if ($implementationText.Contains($forbidden, [StringComparison]::OrdinalIgnoreCase)) { throw "implementation section exposed forbidden content: $forbidden" }
  }

  $cleared = Invoke-Publisher @('Clear', '-StatusPath', $statusPath)
  Assert-Code $cleared 0 'clear pending decision'
  if ((Get-StatusText) -cne $emptyFixture) { throw 'clear did not restore the canonical empty section' }

  $unchangedHash = Get-StatusHash
  $invalidJson = [Convert]::ToBase64String($utf8NoBom.GetBytes('{broken json'))
  $invalid = Invoke-Publisher @('PublishPending', '-StatusPath', $statusPath, '-DecisionStateJsonBase64', $invalidJson)
  if ($invalid.Code -eq 0) { throw 'publisher accepted invalid decision JSON' }
  if ((Get-StatusHash) -ne $unchangedHash) { throw 'invalid JSON changed the status file' }

  foreach ($case in @(
    [ordered]@{
      Label = 'email address'
      Payload = [ordered]@{
        pendingDecision = New-PendingDecision
        decisionFlow = New-Flow 'AWAITING_DECISION' @($firstResolved)
        diagnostic = 'test-recipient@example.com'
      }
    },
    [ordered]@{
      Label = 'evidence hash'
      Payload = [ordered]@{
        pendingDecision = New-PendingDecision
        decisionFlow = New-Flow 'AWAITING_DECISION' @(
          [ordered]@{
            decisionId = 'DEC-20260715-FIRST111111'
            resolution = [ordered]@{ optionKey = 'B'; source = 'manual'; evidenceHash = 'SECRET-HASH' }
          }
        )
      }
    },
    [ordered]@{
      Label = 'provider field'
      Payload = [ordered]@{
        pendingDecision = [ordered]@{
          decisionId = 'DEC-20260715-SECOND222222'
          createdAt = '2026-07-15T03:30:19+08:00'
          taskId = 'TQ-057'
          taskSummary = '清理现存数据矛盾'
          question = '双倍率字段采用哪种兼容口径？'
          options = @([ordered]@{ key = 'A'; label = '保留' }, [ordered]@{ key = 'B'; label = '合并' })
          recommendedOption = 'A'
          status = 'PROVIDER_ACCEPTED'
          providerMessageId = 'provider-secret'
        }
        decisionFlow = New-Flow 'AWAITING_DECISION' @($firstResolved)
      }
    }
  )) {
    Write-BomFixture $emptyFixture
    $sensitiveHash = Get-StatusHash
    $sensitive = Invoke-Publisher @(
      'PublishPending',
      '-StatusPath', $statusPath,
      '-DecisionStateJsonBase64', (ConvertTo-PayloadBase64 $case.Payload)
    )
    if ($sensitive.Code -eq 0) { throw "publisher accepted payload containing $($case.Label)" }
    if ((Get-StatusHash) -ne $sensitiveHash) { throw "payload containing $($case.Label) changed the status file" }
  }

  $missingPath = Join-Path $sandbox 'missing.txt'
  $missingFile = Invoke-Publisher @('Clear', '-StatusPath', $missingPath)
  if ($missingFile.Code -eq 0) { throw 'publisher accepted a missing status file' }

  Write-BomFixture "# Missing heading`r`n`r`n## 最近有效结果`r`n"
  $missingHash = Get-StatusHash
  $missing = Invoke-Publisher @('Clear', '-StatusPath', $statusPath)
  if ($missing.Code -eq 0) { throw 'publisher accepted a missing pending-decision heading' }
  if ((Get-StatusHash) -ne $missingHash) { throw 'missing-heading failure changed the status file' }

  Write-BomFixture ($emptyFixture + "`r`n## 当前待决策`r`n`r`n当前无待决策项。`r`n")
  $duplicateHash = Get-StatusHash
  $duplicate = Invoke-Publisher @('Clear', '-StatusPath', $statusPath)
  if ($duplicate.Code -eq 0) { throw 'publisher accepted duplicate pending-decision headings' }
  if ((Get-StatusHash) -ne $duplicateHash) { throw 'duplicate-heading failure changed the status file' }

  Write-BomFixture "# No following section`r`n`r`n## 当前待决策`r`n`r`n当前无待决策项。`r`n"
  $boundaryHash = Get-StatusHash
  $noBoundary = Invoke-Publisher @('Clear', '-StatusPath', $statusPath)
  if ($noBoundary.Code -eq 0) { throw 'publisher accepted a pending-decision section without a following level-two heading' }
  if ((Get-StatusHash) -ne $boundaryHash) { throw 'missing-boundary failure changed the status file' }

  Write-BomFixture $emptyFixture
  $lockedHash = Get-StatusHash
  $lock = [IO.File]::Open($statusPath, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
  try {
    $writeFailure = Invoke-Publisher @('Clear', '-StatusPath', $statusPath)
    if ($writeFailure.Code -eq 0) { throw 'publisher unexpectedly succeeded while the status file was locked against replacement' }
    if ((Get-StatusHash) -ne $lockedHash) { throw 'write failure damaged the original status file' }
  } finally {
    $lock.Dispose()
  }

  'test-automation-decision-status: OK'
} finally {
  Remove-Item -LiteralPath $sandbox -Recurse -Force -ErrorAction SilentlyContinue
}
