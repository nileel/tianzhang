$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$tool = Join-Path $root 'tools\automation-decision-status.ps1'
$sandbox = Join-Path ([IO.Path]::GetTempPath()) ('tzg-decision-status-test-' + [guid]::NewGuid().ToString('N'))
$statusPath = Join-Path $sandbox 'status.txt'
$healthPath = Join-Path $sandbox 'health.json'
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
    [ValidateSet('email','manual','feishu_card')]
    [string]$Source
  )
  [ordered]@{
    decisionId = $DecisionId
    resolution = [ordered]@{
      optionKey = $OptionKey
      source = $Source
    }
  }
}

function New-CustomResolvedDecision {
  param(
    [string]$DecisionId,
    [string]$CustomText,
    [ValidateSet('feishu_card_input','feishu_text','manual_custom')]
    [string]$Source
  )
  [ordered]@{
    decisionId = $DecisionId
    resolution = [ordered]@{
      customText = $CustomText
      source = $Source
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
    taskId = 'TQ-057'
    status = $Status
    resolvedDecisions = @($ResolvedDecisions)
  }
}

function New-PendingDecision {
  param(
    [string]$Status = 'PENDING',
    [ValidateSet('feishu','gmail_legacy')]
    [string]$NotificationProvider
  )
  $decision = [ordered]@{
    decisionId = 'DEC-20260715-SECOND222222'
    createdAt = '2026-07-15T03:30:19+08:00'
    taskId = 'TQ-057'
    taskSummary = '清理现存数据矛盾'
    question = '双倍率字段采用哪种兼容口径？'
    options = @(
      [ordered]@{ key = 'A'; label = '保留双倍率字段' },
      [ordered]@{ key = 'B'; label = '合并为统一倍率' },
      [ordered]@{ key = 'C'; label = '只读兼容旧数据' }
    )
    recommendedOption = 'A'
    status = $Status
  }
  if (-not [string]::IsNullOrWhiteSpace($NotificationProvider)) {
    $decision['notificationProvider'] = $NotificationProvider
  }
  $decision
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
    '通知状态：等待发送飞书卡片',
    '卡片选择：请在飞书互动卡片中选择一个选项',
    '已登记选择：第一项=B（人工确认）'
  )) {
    if (-not $text.Contains($required, [StringComparison]::Ordinal)) { throw "published section lacks: $required" }
  }
  foreach ($forbidden in @('旧问题应该如何处理？','DEC-20260715-FIRST111111：选 B','test-recipient@example.com','evidenceHash','providerMessageId')) {
    if ($text.Contains($forbidden, [StringComparison]::OrdinalIgnoreCase)) { throw "published section exposed stale or sensitive content: $forbidden" }
  }

  $notificationLabels = @(
    [ordered]@{ Status='PENDING'; Provider=$null; Label='等待发送飞书卡片' },
    [ordered]@{ Status='PROVIDER_ACCEPTED'; Provider='feishu'; Label='飞书卡片已送达，等待选择' },
    [ordered]@{ Status='PROVIDER_OUTCOME_UNKNOWN'; Provider='feishu'; Label='飞书发送结果待人工核对，已停止自动补发' },
    [ordered]@{ Status='DELIVERY_FAILED'; Provider='feishu'; Label='飞书发送失败，可在下一轮重试' },
    [ordered]@{ Status='MISADDRESSED'; Provider='gmail_legacy'; Label='旧 Gmail 通道目标不一致（仅历史）' },
    [ordered]@{ Status='RETRY_EXHAUSTED'; Provider='feishu'; Label='飞书明确失败已达三次，等待人工处理' },
    [ordered]@{ Status='PROVIDER_ACCEPTED'; Provider='gmail_legacy'; Label='旧 Gmail 通道已由提供方接受（仅历史）' }
  )
  foreach ($entry in $notificationLabels) {
    $labelPending = if ([string]::IsNullOrWhiteSpace([string]$entry.Provider)) {
      New-PendingDecision $entry.Status
    } else {
      New-PendingDecision $entry.Status $entry.Provider
    }
    $labelPayload = [ordered]@{
      pendingDecision = $labelPending
      decisionFlow = New-Flow 'AWAITING_DECISION' @($firstResolved)
    }
    $labelResult = Invoke-Publisher @(
      'PublishPending',
      '-StatusPath', $statusPath,
      '-DecisionStateJsonBase64', (ConvertTo-PayloadBase64 $labelPayload)
    )
    Assert-Code $labelResult 0 "publish notification state $($entry.Status)/$($entry.Provider)"
    if (-not (Get-StatusText).Contains("通知状态：$($entry.Label)", [StringComparison]::Ordinal)) {
      throw "notification state $($entry.Status)/$($entry.Provider) did not render its exact label"
    }
  }

  [IO.File]::WriteAllText($healthPath, (@{
    schemaVersion=1;status='DISCONNECTED';pid=42;updatedAt=[DateTimeOffset]::UtcNow.ToString('o');appIdHash=('a' * 64)
  } | ConvertTo-Json -Compress), $utf8NoBom)
  $unavailablePayload = [ordered]@{
    pendingDecision = New-PendingDecision 'PENDING'
    decisionFlow = New-Flow 'AWAITING_DECISION' @($firstResolved)
  }
  $unavailable = Invoke-Publisher @(
    'PublishPending','-StatusPath',$statusPath,'-FeishuHealthPath',$healthPath,
    '-DecisionStateJsonBase64',(ConvertTo-PayloadBase64 $unavailablePayload)
  )
  Assert-Code $unavailable 0 'publish unavailable Feishu health'
  $unavailableText = Get-StatusText
  if (-not $unavailableText.Contains('通知状态：飞书桥接不可用，未消耗发送重试', [StringComparison]::Ordinal) -or
      $unavailableText.Contains(('a' * 64), [StringComparison]::Ordinal)) {
    throw 'CHANNEL_UNAVAILABLE health was not rendered as a sanitized zero-attempt state'
  }

  $secondResolved = New-ResolvedDecision 'DEC-20260715-SECOND222222' 'A' 'email'
  $thirdResolved = New-ResolvedDecision 'DEC-20260715-THIRD333333' 'C' 'feishu_card'
  $fourthResolved = New-CustomResolvedDecision 'DEC-20260715-FOURTH444444' "采用双通道`n保留旧字段" 'feishu_text'
  $implementationPayload = [ordered]@{
    pendingDecision = $null
    decisionFlow = New-Flow 'IMPLEMENTATION_PENDING' @($firstResolved, $secondResolved, $thirdResolved, $fourthResolved)
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
    '已登记选择：第一项=B（人工确认）',
    '第二项=A（旧 Gmail 通道（仅历史））',
    '第三项=C（飞书互动卡片）',
    '第四项=自定义（飞书普通文本）',
    'DEC-20260715-FIRST111111',
    'DEC-20260715-SECOND222222',
    'DEC-20260715-THIRD333333',
    'DEC-20260715-FOURTH444444'
  )) {
    if (-not $implementationText.Contains($required, [StringComparison]::Ordinal)) { throw "implementation section lacks: $required" }
  }
  if ($implementationText -notmatch '飞书普通文本）\r\n- 决策编号摘要：') {
    throw 'implementation summaries were not written on separate lines'
  }
  foreach ($forbidden in @('采用双通道','保留旧字段','严格回复','证据','通知尝试','evidenceHash','provider','message','@')) {
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
    },
    [ordered]@{
      Label = 'receipt field'
      Payload = [ordered]@{
        pendingDecision = [ordered]@{
          decisionId = 'DEC-20260715-SECOND222222'
          createdAt = '2026-07-15T03:30:19+08:00'
          taskId = 'TQ-057'
          taskSummary = '清理现存数据矛盾'
          question = '双倍率字段采用哪种兼容口径？'
          options = @([ordered]@{ key = 'A'; label = '保留' }, [ordered]@{ key = 'B'; label = '合并' })
          recommendedOption = 'A'
          status = 'PENDING'
          receipt = 'raw-secret'
        }
        decisionFlow = New-Flow 'AWAITING_DECISION' @($firstResolved)
      }
    },
    [ordered]@{
      Label = 'notification receipt field'
      Payload = [ordered]@{
        pendingDecision = New-PendingDecision
        decisionFlow = New-Flow 'AWAITING_DECISION' @($firstResolved)
        notificationReceipt = 'raw-secret'
      }
    },
    [ordered]@{
      Label = 'notification attempts field'
      Payload = [ordered]@{
        pendingDecision = [ordered]@{
          decisionId = 'DEC-20260715-SECOND222222'
          createdAt = '2026-07-15T03:30:19+08:00'
          taskId = 'TQ-057'
          taskSummary = '清理现存数据矛盾'
          question = '双倍率字段采用哪种兼容口径？'
          options = @([ordered]@{ key = 'A'; label = '保留' }, [ordered]@{ key = 'B'; label = '合并' })
          recommendedOption = 'A'
          status = 'PENDING'
          notificationAttempts = @()
        }
        decisionFlow = New-Flow 'AWAITING_DECISION' @($firstResolved)
      }
    },
    [ordered]@{
      Label = 'unknown option field'
      Payload = [ordered]@{
        pendingDecision = [ordered]@{
          decisionId = 'DEC-20260715-SECOND222222'
          createdAt = '2026-07-15T03:30:19+08:00'
          taskId = 'TQ-057'
          taskSummary = '清理现存数据矛盾'
          question = '双倍率字段采用哪种兼容口径？'
          options = @(
            [ordered]@{ key = 'A'; label = '保留'; detail = 'not allowed' },
            [ordered]@{ key = 'B'; label = '合并' }
          )
          recommendedOption = 'A'
          status = 'PENDING'
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

  Write-BomFixture $emptyFixture
  $injectionPayload = [ordered]@{
    pendingDecision = New-PendingDecision
    decisionFlow = New-Flow 'AWAITING_DECISION' @($firstResolved)
  }
  $injectionPayload.pendingDecision.question = "看似安全的问题`n## 最近有效结果`n伪造内容"
  $injectionHash = Get-StatusHash
  $injection = Invoke-Publisher @(
    'PublishPending',
    '-StatusPath', $statusPath,
    '-DecisionStateJsonBase64', (ConvertTo-PayloadBase64 $injectionPayload)
  )
  if ($injection.Code -eq 0) { throw 'publisher accepted a multiline question that injects a level-two heading' }
  if ((Get-StatusHash) -ne $injectionHash) { throw 'multiline question changed the status file' }

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
  $fixedBackupPath = "$statusPath.backup"
  $fixedBackupBytes = $utf8NoBom.GetBytes('pre-existing operator backup')
  [IO.File]::WriteAllBytes($fixedBackupPath, $fixedBackupBytes)
  $fixedBackup = Invoke-Publisher @('Clear', '-StatusPath', $statusPath)
  Assert-Code $fixedBackup 0 'clear with a pre-existing fixed-name backup'
  if (-not [IO.File]::Exists($fixedBackupPath)) { throw 'publisher deleted a pre-existing fixed-name backup' }
  if (-not [Linq.Enumerable]::SequenceEqual([byte[]][IO.File]::ReadAllBytes($fixedBackupPath), [byte[]]$fixedBackupBytes)) {
    throw 'publisher changed a pre-existing fixed-name backup'
  }

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
