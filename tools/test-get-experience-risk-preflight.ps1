#requires -Version 7.0

[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Assert-True {
  param([bool]$Condition, [string]$Message)
  if (-not $Condition) { throw $Message }
}

function Write-Utf8 {
  param([string]$Path, [string]$Text)
  $parent = Split-Path -Parent $Path
  [IO.Directory]::CreateDirectory($parent) | Out-Null
  [IO.File]::WriteAllText($Path, $Text, [Text.UTF8Encoding]::new($false))
}

function Get-RuneCount {
  param([string]$Text)
  $count = 0
  foreach ($rune in $Text.EnumerateRunes()) { $count++ }
  $count
}

function New-TaskCard {
  param(
    [string]$Id,
    [string]$Domain = 'automation',
    [string]$Stage = 'implementation',
    [string]$Title = 'fixture',
    [string[]]$ExpectedPaths = @(),
    [string]$Bichan = '',
    [string]$Shishi = '',
    [string[]]$ExplicitRefs = @()
  )
  $meta = [ordered]@{
    schemaVersion = 1
    id = $Id
    title = $Title
    priority = 'P2'
    route = 'external_execute'
    owner = 'deepseek'
    domain = $Domain
    stage = $Stage
    dispatchState = 'ready'
    blockedBy = @()
    stateReason = 'fixture'
    expectedPaths = @($ExpectedPaths)
    sourceBacklog = '开发管理/任务列表/管理与自动化任务.txt'
  }
  if ($ExplicitRefs.Count -gt 0) {
    $meta['riskPreflight'] = [ordered]@{ explicitRefs = @($ExplicitRefs) }
  }
  $json = $meta | ConvertTo-Json -Depth 10
  $body = @("# $Id · $Title", '## 必查范围', $Bichan, '', '## 实施范围', $Shishi) -join "`n"
  @('---TASK-META---', $json, '---TASK-BODY---', $body) -join "`n"
}

function New-Exp {
  param(
    [string]$Id,
    [string]$Title = '风险',
    [string]$Summary = '开工前提示',
    [string]$Status = 'active',
    [string]$Level = 'notice',
    [string]$Trigger = 'path',
    [string[]]$Domains = @(),
    [string[]]$Stages = @(),
    [string[]]$Paths = @(),
    [string[]]$Texts = @(),
    [string]$DetailRef = '',
    [string[]]$GateRefs = @()
  )
  [ordered]@{
    id = $Id
    title = $Title
    preflightSummary = $Summary
    status = $Status
    level = $Level
    triggerMode = $Trigger
    domains = @($Domains)
    stages = @($Stages)
    pathPatterns = @($Paths)
    textPatterns = @($Texts)
    detailRef = $DetailRef
    gateRefs = @($GateRefs)
    lastVerified = '2026-08-24'
  }
}

function New-Gate {
  param(
    [string]$Id,
    [string]$InstructionRef = '开发管理/开发-技术经验.txt#模拟器',
    [string[]]$EntryPaths = @()
  )
  [ordered]@{ id = $Id; instructionRef = $InstructionRef; entryPaths = @($EntryPaths); lastVerified = '2026-08-24' }
}

function New-ExpCard {
  param([string]$Id, [string]$Title = '风险', [string[]]$BodyLines = @())
  (@("# $Id · $Title", '', '## 状态', 'active', '', '## 开工前') + $BodyLines + @('', '## 正确处理', '已验证顺序。')) -join "`n"
}

function New-Index {
  param([object[]]$Experiences = @(), [object[]]$Gates = @())
  $obj = [ordered]@{ schemaVersion = 1; experiences = @($Experiences); gates = @($Gates) }
  $obj | ConvertTo-Json -Depth 30 -Compress
}

function Set-Index {
  param([string]$Root, [string]$Json)
  Write-Utf8 (Join-Path $Root '开发管理/经验库/风险索引.json') $Json
}

function Set-Card {
  param([string]$Root, [string]$Text, [string]$Id)
  Write-Utf8 (Join-Path $Root "开发管理/任务卡/$Id.txt") $Text
}

function Set-ExpCard {
  param([string]$Root, [string]$Id, [string]$Text)
  Write-Utf8 (Join-Path $Root "开发管理/经验库/经验卡/$Id.txt") $Text
}

function Invoke-Matcher {
  param([string]$Root, [string[]]$MatcherArgs)
  $matcher = Join-Path $PSScriptRoot 'get-experience-risk-preflight.ps1'
  $psi = [Diagnostics.ProcessStartInfo]::new()
  $psi.FileName = 'pwsh'
  $psi.UseShellExecute = $false
  $psi.RedirectStandardOutput = $true
  $psi.RedirectStandardError = $true
  $psi.CreateNoWindow = $true
  $psi.StandardOutputEncoding = [Text.UTF8Encoding]::new($false)
  $psi.StandardErrorEncoding = [Text.UTF8Encoding]::new($false)
  foreach ($arg in @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $matcher, '-RepositoryRoot', $Root) + $MatcherArgs) {
    $psi.ArgumentList.Add($arg)
  }
  $proc = [Diagnostics.Process]::new()
  $proc.StartInfo = $psi
  if (-not $proc.Start()) { throw 'cannot start matcher process' }
  $stdout = $proc.StandardOutput.ReadToEnd()
  $stderr = $proc.StandardError.ReadToEnd()
  $proc.WaitForExit()
  [pscustomobject]@{ ExitCode = $proc.ExitCode; Stdout = $stdout.Trim(); Stderr = $stderr.Trim() }
}

function Invoke-Ok {
  param([string]$Root, [string[]]$MatcherArgs)
  $result = Invoke-Matcher $Root $MatcherArgs
  Assert-True ($result.ExitCode -eq 0) "expected ok, got exit $($result.ExitCode): $($result.Stdout) $($result.Stderr)"
  Assert-True (@($result.Stdout -split "`n").Count -eq 1) "expected single JSON line, got: $($result.Stdout)"
  $json = $result.Stdout | ConvertFrom-Json
  [pscustomobject]@{ Result = $result; Json = $json }
}

function Invoke-Error {
  param([string]$Root, [string]$Code, [string[]]$MatcherArgs)
  $result = Invoke-Matcher $Root $MatcherArgs
  Assert-True ($result.ExitCode -ne 0) "expected error exit, got 0: $($result.Stdout)"
  $json = $result.Stdout | ConvertFrom-Json
  Assert-True ($json.status -ceq 'error') "expected error status, got: $($result.Stdout)"
  Assert-True ($json.code -ceq $Code) "expected code '$Code', got '$($json.code)': $($result.Stdout)"
  $json
}

$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ('exp-preflight-test-' + [guid]::NewGuid().ToString('N'))
try {
  [IO.Directory]::CreateDirectory($tempRoot) | Out-Null

  # ---- 1. 精确路径命中与不相关路径零命中 ----
  $caseRoot = Join-Path $tempRoot 'case1'
  Set-Index $caseRoot (New-Index @((New-Exp -Id 'EXP-BS-001' -Level 'notice' -Paths @('simulations/BattleSim/Combat.cs'))))
  Set-Card $caseRoot (New-TaskCard -Id 'T-HIT-01' -Domain 'battlesim' -ExpectedPaths @('simulations/BattleSim/Combat.cs')) 'T-HIT-01'
  Set-Card $caseRoot (New-TaskCard -Id 'T-MISS-01' -Domain 'battlesim' -ExpectedPaths @('src/Other.cs')) 'T-MISS-01'
  $hit = Invoke-Ok $caseRoot @('-TaskId', 'T-HIT-01')
  Assert-True ($hit.Json.matched.Count -eq 1 -and $hit.Json.matched[0] -ceq 'EXP-BS-001') 'case1: exact path should hit'
  $miss = Invoke-Ok $caseRoot @('-TaskId', 'T-MISS-01')
  Assert-True ($miss.Json.matched.Count -eq 0) 'case1: irrelevant path should zero-hit'

  # ---- 2. path_and_text 同时/仅路径/仅文本 ----
  $caseRoot = Join-Path $tempRoot 'case2'
  Set-Index $caseRoot (New-Index @((New-Exp -Id 'EXP-BS-003' -Level 'notice' -Trigger 'path_and_text' -Paths @('simulations/BattleSim/Combat.cs') -Texts @('Simulate'))))
  Set-Card $caseRoot (New-TaskCard -Id 'T-BOTH-01' -Domain 'battlesim' -ExpectedPaths @('simulations/BattleSim/Combat.cs') -Shishi '调用 Simulate') 'T-BOTH-01'
  Set-Card $caseRoot (New-TaskCard -Id 'T-PATH-01' -Domain 'battlesim' -ExpectedPaths @('simulations/BattleSim/Combat.cs') -Shishi '无关键词') 'T-PATH-01'
  Set-Card $caseRoot (New-TaskCard -Id 'T-TEXT-01' -Domain 'battlesim' -ExpectedPaths @('src/Other.cs') -Shishi '调用 Simulate') 'T-TEXT-01'
  Assert-True ((Invoke-Ok $caseRoot @('-TaskId', 'T-BOTH-01')).Json.matched.Count -eq 1) 'case2: path+text should hit'
  Assert-True ((Invoke-Ok $caseRoot @('-TaskId', 'T-PATH-01')).Json.matched.Count -eq 0) 'case2: path only should miss'
  Assert-True ((Invoke-Ok $caseRoot @('-TaskId', 'T-TEXT-01')).Json.matched.Count -eq 0) 'case2: text only should miss'

  # ---- 3. explicit_only 不自动触发，显式引用后生效 ----
  $caseRoot = Join-Path $tempRoot 'case3'
  Set-Index $caseRoot (New-Index @((New-Exp -Id 'EXP-MGMT-001' -Level 'notice' -Trigger 'explicit_only')))
  Set-Card $caseRoot (New-TaskCard -Id 'T-EXP-01' -Domain 'management') 'T-EXP-01'
  Assert-True ((Invoke-Ok $caseRoot @('-TaskId', 'T-EXP-01')).Json.matched.Count -eq 0) 'case3: explicit_only should not auto-trigger'
  Assert-True ((Invoke-Ok $caseRoot @('-TaskId', 'T-EXP-01', '-ExplicitRefs', 'EXP-MGMT-001')).Json.matched.Count -eq 1) 'case3: explicit ref should activate'
  Set-Card $caseRoot (New-TaskCard -Id 'T-EXP-02' -Domain 'management' -ExplicitRefs @('EXP-MGMT-001')) 'T-EXP-02'
  Assert-True ((Invoke-Ok $caseRoot @('-TaskId', 'T-EXP-02')).Json.matched.Count -eq 1) 'case3: card explicitRefs should activate'

  # ---- 4. candidate/review_required/retired 不进入结果 ----
  $caseRoot = Join-Path $tempRoot 'case4'
  Set-Index $caseRoot (New-Index @(
    (New-Exp -Id 'EXP-BS-010' -Level 'notice' -Paths @('simulations/BattleSim/Combat.cs') -Status 'active')
    (New-Exp -Id 'EXP-BS-011' -Level 'notice' -Paths @('simulations/BattleSim/Combat.cs') -Status 'candidate')
    (New-Exp -Id 'EXP-BS-012' -Level 'notice' -Paths @('simulations/BattleSim/Combat.cs') -Status 'review_required')
    (New-Exp -Id 'EXP-BS-013' -Level 'notice' -Paths @('simulations/BattleSim/Combat.cs') -Status 'retired')
  ))
  Set-Card $caseRoot (New-TaskCard -Id 'T-STAT-01' -Domain 'battlesim' -ExpectedPaths @('simulations/BattleSim/Combat.cs')) 'T-STAT-01'
  $stat = Invoke-Ok $caseRoot @('-TaskId', 'T-STAT-01')
  Assert-True ($stat.Json.matched.Count -eq 1 -and $stat.Json.matched[0] -ceq 'EXP-BS-010') 'case4: only active should match'

  # ---- 5. 门禁全保留、notice 限量、去重 ----
  $caseRoot = Join-Path $tempRoot 'case5'
  Write-Utf8 (Join-Path $caseRoot '开发管理/开发-技术经验.txt') "# 开发-技术经验`n`n## 模拟器`n模拟器经验正文。"
  Write-Utf8 (Join-Path $caseRoot 'tools/fake-gate.ps1') "# fixture gate"
  $body019 = @('- 适用条件：修改战斗机制。', '- 排除条件：纯数值。', '- 必须定位：Combat.Simulate 双侧。') -join "`n"
  Set-ExpCard $caseRoot 'EXP-BS-019' (New-ExpCard -Id 'EXP-BS-019' -Title '双侧对称' -BodyLines @($body019))
  Set-Index $caseRoot (New-Index `
    -Experiences @(
      (New-Exp -Id 'EXP-BS-014' -Level 'gate' -Paths @('simulations/BattleSim/Combat.cs') -GateRefs @('g1'))
      (New-Exp -Id 'EXP-BS-015' -Level 'gate' -Paths @('simulations/BattleSim/Combat.cs') -GateRefs @('g2'))
      (New-Exp -Id 'EXP-BS-016' -Level 'notice' -Paths @('simulations/BattleSim/Combat.cs') -Summary '通知甲')
      (New-Exp -Id 'EXP-BS-017' -Level 'notice' -Paths @('simulations/BattleSim/Combat.cs') -Summary '通知乙')
      (New-Exp -Id 'EXP-BS-018' -Level 'notice' -Paths @('simulations/BattleSim/Combat.cs') -Summary '通知丙')
      (New-Exp -Id 'EXP-BS-019' -Level 'must_read' -Paths @('simulations/BattleSim/Combat.cs') -DetailRef '开发管理/经验库/经验卡/EXP-BS-019.txt#开工前' -GateRefs @('g1'))
    ) `
    -Gates @(
      (New-Gate -Id 'g1' -EntryPaths @('tools/fake-gate.ps1'))
      (New-Gate -Id 'g2' -EntryPaths @('tools/fake-gate.ps1'))
    ))
  Set-Card $caseRoot (New-TaskCard -Id 'T-GATE-01' -Domain 'battlesim' -ExpectedPaths @('simulations/BattleSim/Combat.cs')) 'T-GATE-01'
  $gate = Invoke-Ok $caseRoot @('-TaskId', 'T-GATE-01')
  Assert-True ($gate.Json.matched.Count -eq 6) 'case5: all six experiences should match'
  Assert-True (@($gate.Json.gates).Count -eq 2 -and @($gate.Json.gates) -ccontains 'g1' -and @($gate.Json.gates) -ccontains 'g2') 'case5: gates should be preserved and deduped'
  Assert-True (@($gate.Json.notice).Count -eq 2) 'case5: notice should be limited to 2'
  Assert-True (@($gate.Json.mustRead).Count -eq 1) 'case5: exactly one must_read'
  Assert-True ($gate.Json.mustRead[0].id -ceq 'EXP-BS-019') 'case5: must_read id'
  Assert-True ($gate.Json.mustRead[0].body -ceq $body019) 'case5: must_read exact body'
  Assert-True ([int]$gate.Json.mustRead[0].chars -eq (Get-RuneCount $body019)) 'case5: must_read char count'
  Assert-True ([int]$gate.Json.mustReadChars -eq (Get-RuneCount $body019)) 'case5: total must_read chars'

  # ---- 6. 非法 schema/重复 ID/非法枚举/缺失正文指针/缺失门禁 ----
  $caseRoot = Join-Path $tempRoot 'case6'
  Set-Index $caseRoot '{"schemaVersion":2,"experiences":[],"gates":[]}'
  Set-Card $caseRoot (New-TaskCard -Id 'T-ERR-01') 'T-ERR-01'
  Invoke-Error $caseRoot 'invalid_index_schema' @('-TaskId', 'T-ERR-01') | Out-Null

  Set-Index $caseRoot (New-Index @((New-Exp -Id 'EXP-BS-001'), (New-Exp -Id 'EXP-BS-001')))
  Invoke-Error $caseRoot 'duplicate_experience_id' @('-TaskId', 'T-ERR-01') | Out-Null

  Set-Index $caseRoot (New-Index @((New-Exp -Id 'EXP-BS-001' -Status 'bad')))
  Invoke-Error $caseRoot 'invalid_experience_enum' @('-TaskId', 'T-ERR-01') | Out-Null

  Set-Index $caseRoot (New-Index @((New-Exp -Id 'EXP-BS-001' -Level 'must_read' -Paths @('simulations/BattleSim/Combat.cs') -DetailRef '')))
  Set-Card $caseRoot (New-TaskCard -Id 'T-ERR-02' -Domain 'battlesim' -ExpectedPaths @('simulations/BattleSim/Combat.cs')) 'T-ERR-02'
  Invoke-Error $caseRoot 'missing_body_pointer' @('-TaskId', 'T-ERR-02') | Out-Null

  Set-Index $caseRoot (New-Index @((New-Exp -Id 'EXP-BS-001' -Level 'gate' -Paths @('simulations/BattleSim/Combat.cs') -GateRefs @())))
  Invoke-Error $caseRoot 'missing_gate' @('-TaskId', 'T-ERR-02') | Out-Null

  # ---- 7. Windows 路径分隔符与大小写归一化 ----
  $caseRoot = Join-Path $tempRoot 'case7'
  Set-Index $caseRoot (New-Index @(
    (New-Exp -Id 'EXP-UNITY-001' -Level 'notice' -Paths @('src/Runtime/Foo.cs'))
    (New-Exp -Id 'EXP-AUTO-001' -Level 'notice' -Paths @('tools/*.ps1'))
  ))
  Set-Card $caseRoot (New-TaskCard -Id 'T-NORM-01' -Domain 'unity' -ExpectedPaths @('src\RUNTIME\FOO.CS')) 'T-NORM-01'
  $norm = Invoke-Ok $caseRoot @('-TaskId', 'T-NORM-01')
  Assert-True ($norm.Json.matched.Count -eq 1 -and $norm.Json.matched[0] -ceq 'EXP-UNITY-001') 'case7: separator/case normalization should hit'
  Set-Card $caseRoot (New-TaskCard -Id 'T-GLOB-01' -Domain 'automation' -ExpectedPaths @('tools/foo.ps1')) 'T-GLOB-01'
  Assert-True ((Invoke-Ok $caseRoot @('-TaskId', 'T-GLOB-01')).Json.matched[0] -ceq 'EXP-AUTO-001') 'case7: glob path should hit'

  # ---- 8. expectedPaths 扩大后产生新增命中 ----
  $caseRoot = Join-Path $tempRoot 'case8'
  Set-Index $caseRoot (New-Index @(
    (New-Exp -Id 'EXP-AUTO-001' -Level 'notice' -Paths @('tools/foo.ps1'))
    (New-Exp -Id 'EXP-UNITY-001' -Level 'notice' -Paths @('src/bar.cs'))
  ))
  Set-Card $caseRoot (New-TaskCard -Id 'T-GROW-01' -Domain 'automation' -ExpectedPaths @('tools/foo.ps1')) 'T-GROW-01'
  $grow1 = Invoke-Ok $caseRoot @('-TaskId', 'T-GROW-01')
  Assert-True ($grow1.Json.matched.Count -eq 1 -and $grow1.Json.matched[0] -ceq 'EXP-AUTO-001') 'case8: initial single hit'
  Set-Card $caseRoot (New-TaskCard -Id 'T-GROW-01' -Domain 'automation' -ExpectedPaths @('tools/foo.ps1', 'src/bar.cs')) 'T-GROW-01'
  $grow2 = Invoke-Ok $caseRoot @('-TaskId', 'T-GROW-01')
  Assert-True ($grow2.Json.matched.Count -eq 2) 'case8: expanded paths should add a hit'

  # ---- 9. 超过 3 条 must_read 与 600 字符边界返回 preflight_overbroad ----
  $caseRoot = Join-Path $tempRoot 'case9'
  Write-Utf8 (Join-Path $caseRoot '开发管理/开发-技术经验.txt') "# 开发-技术经验`n`n## 模拟器`n模拟器经验正文。"
  Write-Utf8 (Join-Path $caseRoot 'tools/fake-gate.ps1') "# fixture gate"
  $expList = @()
  for ($index = 1; $index -le 4; $index++) {
    $eid = 'EXP-BS-' + $index.ToString('D3')
    Set-ExpCard $caseRoot $eid (New-ExpCard -Id $eid -Title '风险' -BodyLines @('- 开工前正文。'))
    $gr = if ($index -eq 3) { @('g2') } else { @('g1') }
    $expList += (New-Exp -Id $eid -Level 'must_read' -Paths @('simulations/BattleSim/Combat.cs') -DetailRef "开发管理/经验库/经验卡/$eid.txt#开工前" -GateRefs $gr)
  }
  Set-Index $caseRoot (New-Index $expList -Gates @(
    (New-Gate -Id 'g1' -EntryPaths @('tools/fake-gate.ps1'))
    (New-Gate -Id 'g2' -EntryPaths @('tools/fake-gate.ps1'))
  ))
  Set-Card $caseRoot (New-TaskCard -Id 'T-OVER-01' -Domain 'battlesim' -ExpectedPaths @('simulations/BattleSim/Combat.cs')) 'T-OVER-01'
  $over = Invoke-Matcher $caseRoot @('-TaskId', 'T-OVER-01')
  Assert-True ($over.ExitCode -eq 0) 'case9: overbroad is a terminal result, not a crash'
  $overJson = $over.Stdout | ConvertFrom-Json
  Assert-True ($overJson.status -ceq 'preflight_overbroad') 'case9: overbroad status'
  Assert-True ($overJson.reason -ceq 'must_read_count_exceeds_3') 'case9: overbroad reason'
  Assert-True (@($overJson.gates).Count -eq 2 -and @($overJson.gates) -ccontains 'g1' -and @($overJson.gates) -ccontains 'g2') 'case9: overbroad gates preserved and deduped'
  Assert-True (@($overJson.gatePointers).Count -eq 2 -and @($overJson.gatePointers | ForEach-Object { [string]$_.instructionRef }) -ccontains '开发管理/开发-技术经验.txt#模拟器') 'case9: overbroad gate pointers preserved'

  $longBody = '甲' * 301
  Set-ExpCard $caseRoot 'EXP-BS-101' (New-ExpCard -Id 'EXP-BS-101' -Title '长正文甲' -BodyLines @($longBody))
  Set-ExpCard $caseRoot 'EXP-BS-102' (New-ExpCard -Id 'EXP-BS-102' -Title '长正文乙' -BodyLines @($longBody))
  Set-Index $caseRoot (New-Index @(
    (New-Exp -Id 'EXP-BS-101' -Level 'must_read' -Paths @('simulations/BattleSim/Combat.cs') -DetailRef '开发管理/经验库/经验卡/EXP-BS-101.txt#开工前' -GateRefs @('g1'))
    (New-Exp -Id 'EXP-BS-102' -Level 'must_read' -Paths @('simulations/BattleSim/Combat.cs') -DetailRef '开发管理/经验库/经验卡/EXP-BS-102.txt#开工前' -GateRefs @('g1'))
  ) -Gates @((New-Gate -Id 'g1' -EntryPaths @('tools/fake-gate.ps1'))))
  $overChars = Invoke-Matcher $caseRoot @('-TaskId', 'T-OVER-01')
  $overCharsJson = $overChars.Stdout | ConvertFrom-Json
  Assert-True ($overCharsJson.status -ceq 'preflight_overbroad' -and $overCharsJson.reason -ceq 'must_read_chars_exceeds_600') 'case9: 600 char boundary'
  Assert-True (@($overCharsJson.gates).Count -eq 1 -and @($overCharsJson.gates)[0] -ceq 'g1') 'case9: chars overbroad gates preserved and deduped'
  Assert-True (@($overCharsJson.gatePointers).Count -eq 1 -and [string]$overCharsJson.gatePointers[0].id -ceq 'g1') 'case9: chars overbroad gate pointers preserved'

  # ---- 10. 匹配器只读 + 合法零命中 fixture ----
  $repo = Split-Path -Parent $PSScriptRoot
  $before = & git -C $repo status --porcelain | Out-String
  $zero = Invoke-Ok $repo @('-TaskId', 'M-EXP-PREFLIGHT-01A')
  Assert-True ($zero.Json.status -ceq 'ok') 'case10: zero-hit ok status'
  Assert-True ($zero.Json.matched.Count -eq 0) 'case10: zero-hit matched empty'
  Assert-True (-not [string]::IsNullOrWhiteSpace($zero.Json.taskCardDigest) -and -not [string]::IsNullOrWhiteSpace($zero.Json.indexDigest)) 'case10: digests bound'
  $after = & git -C $repo status --porcelain | Out-String
  Assert-True ($before -ceq $after) 'case10: matcher must not modify the worktree'

  Write-Output 'test-get-experience-risk-preflight: PASS'
} finally {
  if (Test-Path -LiteralPath $tempRoot) {
    $resolved = (Resolve-Path -LiteralPath $tempRoot).Path
    Assert-True ($resolved.StartsWith([IO.Path]::GetTempPath(), [StringComparison]::OrdinalIgnoreCase)) 'refusing to remove a non-temp test directory'
    Remove-Item -LiteralPath $resolved -Recurse -Force
  }
}
