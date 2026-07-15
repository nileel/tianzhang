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

$before = "# 自动工作流状态（测试）`r`n`r`n## 使用规则`r`n`r`n保持不变。`r`n`r`n## 当前待决策`r`n"
$emptyBody = "`r`n当前无待决策项。`r`n"
$after = "`r`n## 最近有效结果`r`n`r`n| 字段 | 值 |`r`n|------|----|`r`n| 测试 | 保持不变 |`r`n"
$emptyFixture = $before + $emptyBody + $after

$decision = [ordered]@{
  decisionId = 'DEC-20260715-ABCDEF123456'
  createdAt = '2026-07-15T03:20:19+08:00'
  taskId = 'TQ-057'
  taskSummary = '清理现存数据矛盾'
  question = '采用哪一条已批准口径？'
  options = @(
    [ordered]@{ key = 'A'; label = '补齐数据链' },
    [ordered]@{ key = 'B'; label = '登记精确豁免' }
  )
  recommendedOption = 'A'
  status = 'PENDING'
  notification = $null
}
$decisionJson = [pscustomobject]$decision | ConvertTo-Json -Depth 6 -Compress
$decisionBase64 = [Convert]::ToBase64String($utf8NoBom.GetBytes($decisionJson))

New-Item -ItemType Directory -Path $sandbox | Out-Null
try {
  Write-BomFixture $emptyFixture
  $published = Invoke-Publisher @('Publish', '-StatusPath', $statusPath, '-DecisionJsonBase64', $decisionBase64)
  Assert-Code $published 0 'publish pending decision'

  $bytes = [IO.File]::ReadAllBytes($statusPath)
  if ($bytes[0] -ne 0xEF -or $bytes[1] -ne 0xBB -or $bytes[2] -ne 0xBF) { throw 'publisher did not preserve the UTF-8 BOM' }
  $text = Get-StatusText
  if (-not $text.StartsWith($before, [StringComparison]::Ordinal) -or -not $text.EndsWith($after, [StringComparison]::Ordinal)) {
    throw 'publisher changed content outside the pending-decision section'
  }
  if (($text -replace "`r`n", '').Contains("`n", [StringComparison]::Ordinal)) { throw 'publisher introduced a non-CRLF newline' }
  foreach ($required in @('DEC-20260715-ABCDEF123456','TQ-057','选项 A：补齐数据链','选项 B：登记精确豁免','推荐项：A','通知状态：PENDING','DEC-20260715-ABCDEF123456：选 A')) {
    if (-not $text.Contains($required, [StringComparison]::Ordinal)) { throw "published section lacks: $required" }
  }
  if ($text -match '[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}') { throw 'publisher exposed an email address' }

  $cleared = Invoke-Publisher @('Clear', '-StatusPath', $statusPath)
  Assert-Code $cleared 0 'clear pending decision'
  if ((Get-StatusText) -cne $emptyFixture) { throw 'clear did not restore the canonical empty section' }

  $unchangedHash = Get-StatusHash
  $invalidJson = [Convert]::ToBase64String($utf8NoBom.GetBytes('{broken json'))
  $invalid = Invoke-Publisher @('Publish', '-StatusPath', $statusPath, '-DecisionJsonBase64', $invalidJson)
  if ($invalid.Code -eq 0) { throw 'publisher accepted invalid decision JSON' }
  if ((Get-StatusHash) -ne $unchangedHash) { throw 'invalid JSON changed the status file' }

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

  'test-automation-decision-status: OK'
} finally {
  Remove-Item -LiteralPath $sandbox -Recurse -Force -ErrorAction SilentlyContinue
}
