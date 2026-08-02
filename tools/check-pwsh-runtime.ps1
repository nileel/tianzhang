[CmdletBinding()]
param(
  [string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path,
  [string]$DocumentPaths,
  [string]$ScriptPaths,
  [string]$RequiredVersionPaths
)

$ErrorActionPreference = 'Stop'
$root = [System.IO.Path]::GetFullPath($RepositoryRoot)
$findings = [System.Collections.Generic.List[string]]::new()

$defaultDocuments = @(
  'AGENTS.md',
  'CLAUDE.md',
  '开发管理/开发-技术经验.txt',
  '开发管理/状态与建议维护规则.txt',
  '开发管理/自动工作流规则.txt',
  '开发管理/自动工作流恢复规则.txt',
  '开发管理/自动工作流控制器提示词.txt',
  '开发管理/DeepSeek小时触发提示词.txt',
  '开发管理/当前任务队列.txt',
  'docs/superpowers/plans/2026-07-15-feishu-decision-channel-implementation.md'
)
$taskListRoot = Join-Path $root '开发管理/任务列表'
if (Test-Path -LiteralPath $taskListRoot -PathType Container) {
  $defaultDocuments += @(
    Get-ChildItem -LiteralPath $taskListRoot -Filter '*.txt' -File |
      Sort-Object -Property FullName |
      ForEach-Object { [System.IO.Path]::GetRelativePath($root, $_.FullName).Replace('\', '/') }
  )
}
$taskCardRoot = Join-Path $root '开发管理/任务卡'
if (Test-Path -LiteralPath $taskCardRoot -PathType Container) {
  $defaultDocuments += @(
    Get-ChildItem -LiteralPath $taskCardRoot -Filter '*.txt' -File |
      Sort-Object -Property FullName |
      ForEach-Object {
        [IO.Path]::GetRelativePath($root, $_.FullName).Replace('\', '/')
      }
  )
}
$defaultDocuments = @($defaultDocuments | Select-Object -Unique)
$defaultRequiredVersions = @(
  'tools/hourly-automation-lease.ps1',
  'tools/select-hourly-task.ps1',
  'tools/invoke-codex-hourly.ps1',
  'tools/invoke-codex-candidate.ps1',
  'tools/invoke-deepseek-hourly.ps1',
  'tools/invoke-deepseek-responsibility.ps1',
  'tools/set-task-pending-review.ps1',
  'tools/check-automation-workflow.ps1',
  'tools/check-review-text.ps1',
  'tools/check-data-chain.ps1',
  'tools/check-pending-whitespace.ps1',
  'tools/check-task-cards.ps1',
  'tools/run-unity-editmode-tests.ps1'
)

function Split-PathList {
  param([string]$Value)

  if ([string]::IsNullOrWhiteSpace($Value)) { return @() }
  return @($Value.Split('|', [System.StringSplitOptions]::RemoveEmptyEntries) | ForEach-Object { $_.Trim() })
}

function Resolve-ScanPath {
  param([Parameter(Mandatory = $true)][string]$Path)

  if ([System.IO.Path]::IsPathRooted($Path)) {
    return [System.IO.Path]::GetFullPath($Path)
  }
  return [System.IO.Path]::GetFullPath((Join-Path $root $Path))
}

function Get-DiagnosticPath {
  param([Parameter(Mandatory = $true)][string]$Path)

  return [System.IO.Path]::GetRelativePath($root, $Path).Replace('\', '/')
}

function Add-Finding {
  param(
    [Parameter(Mandatory = $true)][string]$Category,
    [Parameter(Mandatory = $true)][string]$Path,
    [Parameter(Mandatory = $true)][int]$Line
  )

  $findings.Add("$Category $(Get-DiagnosticPath -Path $Path):$Line")
}

function Test-PositionInsidePowerShellString {
  param(
    [Parameter(Mandatory = $true)][AllowEmptyString()][string]$Text,
    [Parameter(Mandatory = $true)][int]$Position
  )

  $quoteKind = $null
  $quoteStart = -1
  for ($index = 0; $index -lt $Text.Length; $index++) {
    $character = $Text[$index]
    if ($quoteKind -eq 'single') {
      if ($character -eq [char]39) {
        if ($index + 1 -lt $Text.Length -and $Text[$index + 1] -eq [char]39) {
          $index++
          continue
        }
        if ($Position -gt $quoteStart -and $Position -lt $index) { return $true }
        $quoteKind = $null
        $quoteStart = -1
      }
      continue
    }

    if ($quoteKind -eq 'double') {
      if ($character -eq [char]96 -and $index + 1 -lt $Text.Length) {
        $index++
        continue
      }
      if ($character -eq [char]34) {
        if ($Position -gt $quoteStart -and $Position -lt $index) { return $true }
        $quoteKind = $null
        $quoteStart = -1
      }
      continue
    }

    if ($character -eq [char]96 -and $index + 1 -lt $Text.Length) {
      $index++
      continue
    }
    if ($character -eq [char]39) {
      $quoteKind = 'single'
      $quoteStart = $index
    }
    elseif ($character -eq [char]34) {
      $quoteKind = 'double'
      $quoteStart = $index
    }
  }

  return $false
}

function Get-DocumentCommandCandidate {
  param([Parameter(Mandatory = $true)][AllowEmptyString()][string]$Line)

  $commands = [System.Collections.Generic.List[string]]::new()
  $trimmed = $Line.Trim()
  if ($trimmed -match '^(?:>\s*)?(?:[-*+]\s+)?\[[ xX]\]\s+(?<command>.+)$') {
    $commands.Add($Matches.command)
  }
  elseif ($trimmed -match '^(?:>\s*)?(?:(?:[-*+]|\d+\.)\s+)?(?<command>.+)$') {
    $commands.Add($Matches.command)
  }

  foreach ($candidate in @($commands)) {
    $guidedCommands = [regex]::Matches(
      $candidate,
      '(?i)(?:^|[，,；;])\s*准确调用\s*[:：]\s*(?<command>[^。；;\r\n]+)')
    foreach ($guidedCommand in $guidedCommands) {
      $commandGroup = $guidedCommand.Groups['command']
      if (-not (Test-PositionInsidePowerShellString -Text $candidate -Position $commandGroup.Index)) {
        $commands.Add($commandGroup.Value.Trim())
      }
    }
  }

  foreach ($inlineCode in [regex]::Matches($Line, '(?<!`)(?<delimiter>`+)(?<code>.+?)\k<delimiter>(?!`)')) {
    $prefix = $Line.Substring(0, $inlineCode.Index)
    if ($prefix -match '(?i)(?:\bdo\s+not\s+run|(?:禁止|不得|不要)(?:调用|运行)\s*[:：]?)\s*$') { continue }
    $code = $inlineCode.Groups['code'].Value.Trim()
    if (-not [string]::IsNullOrWhiteSpace($code)) {
      $commands.Add($code)
    }
  }

  return @($commands | Select-Object -Unique)
}

function Get-ContainingScriptBlock {
  param([Parameter(Mandatory = $true)]$Node)

  $current = $Node
  while ($current) {
    if ($current -is [System.Management.Automation.Language.ScriptBlockAst]) {
      return $current
    }
    $current = $current.Parent
  }
  return $null
}

function Get-StatementContainer {
  param([Parameter(Mandatory = $true)]$Node)

  $current = $Node
  while ($current) {
    if ($current -is [System.Management.Automation.Language.StatementBlockAst] -or
        $current -is [System.Management.Automation.Language.NamedBlockAst]) {
      return $current
    }
    $current = $current.Parent
  }
  return $null
}

function Get-ConstantStringValue {
  param(
    [Parameter(Mandatory = $true)]$Expression,
    [System.Management.Automation.Language.Ast]$AtNode,
    [int]$Depth = 0
  )

  if ($Depth -gt 8) { return $null }
  if (-not $AtNode) { $AtNode = $Expression }

  if ($Expression -is [System.Management.Automation.Language.CommandExpressionAst]) {
    return Get-ConstantStringValue -Expression $Expression.Expression -AtNode $AtNode -Depth $Depth
  }
  if ($Expression -is [System.Management.Automation.Language.ParenExpressionAst]) {
    return Get-ConstantStringValue -Expression $Expression.Pipeline -AtNode $AtNode -Depth $Depth
  }
  if ($Expression -is [System.Management.Automation.Language.PipelineAst] -and
      $Expression.PipelineElements.Count -eq 1 -and
      $Expression.PipelineElements[0] -is [System.Management.Automation.Language.CommandExpressionAst]) {
    return Get-ConstantStringValue -Expression $Expression.PipelineElements[0] -AtNode $AtNode -Depth $Depth
  }
  if ($Expression -is [System.Management.Automation.Language.StringConstantExpressionAst]) {
    return $Expression.Value
  }
  if ($Expression -is [System.Management.Automation.Language.ExpandableStringExpressionAst] -and
      $Expression.NestedExpressions.Count -eq 0) {
    return $Expression.Value
  }
  if ($Expression -is [System.Management.Automation.Language.BinaryExpressionAst] -and
      $Expression.Operator -eq [System.Management.Automation.Language.TokenKind]::Plus) {
    $left = Get-ConstantStringValue -Expression $Expression.Left -AtNode $AtNode -Depth $Depth
    $right = Get-ConstantStringValue -Expression $Expression.Right -AtNode $AtNode -Depth $Depth
    if ($null -ne $left -and $null -ne $right) {
      return $left + $right
    }
  }
  if ($Expression -is [System.Management.Automation.Language.VariableExpressionAst]) {
    $assignments = @(Get-ReachingVariableAssignments -VariableName $Expression.VariablePath.UserPath -AtNode $AtNode)
    if ($assignments.Count -eq 0) { return $null }

    $values = @($assignments | ForEach-Object {
        if ($_.Operator -ne [System.Management.Automation.Language.TokenKind]::Equals) { return }
        Get-ConstantStringValue -Expression $_.Right -AtNode $_ -Depth ($Depth + 1)
      })
    $uniqueValues = @($values | Where-Object { $null -ne $_ } | Select-Object -Unique)
    if ($values.Count -eq $assignments.Count -and $uniqueValues.Count -eq 1) {
      return $uniqueValues[0]
    }
  }
  return $null
}

function Get-ProvableCommandName {
  param([Parameter(Mandatory = $true)]$Expression)

  $value = Get-ConstantStringValue -Expression $Expression
  if ($null -ne $value) {
    return [System.IO.Path]::GetFileName($value.Trim([char[]]@([char]39, [char]34))).ToLowerInvariant()
  }

  if ($Expression -is [System.Management.Automation.Language.CommandExpressionAst]) {
    $Expression = $Expression.Expression
  }
  if ($Expression -is [System.Management.Automation.Language.ExpandableStringExpressionAst]) {
    $basename = [System.IO.Path]::GetFileName($Expression.Value)
    if (-not [string]::IsNullOrWhiteSpace($basename) -and $basename -notmatch '[$`]') {
      return $basename.ToLowerInvariant()
    }
  }
  return $null
}

function Get-ReachingVariableAssignments {
  param(
    [Parameter(Mandatory = $true)][string]$VariableName,
    [Parameter(Mandatory = $true)][System.Management.Automation.Language.Ast]$AtNode
  )

  $scope = Get-ContainingScriptBlock -Node $AtNode
  $commandContainer = Get-StatementContainer -Node $AtNode
  if (-not $scope -or -not $commandContainer) { return @() }

  $assignments = @($scope.FindAll({
        param($node)
          $node -is [System.Management.Automation.Language.AssignmentStatementAst] -and
          $node.Left -is [System.Management.Automation.Language.VariableExpressionAst] -and
          $node.Left.VariablePath.UserPath -ieq $VariableName
      }, $false) | Where-Object {
        $_.Extent.StartOffset -lt $AtNode.Extent.StartOffset
      } | Sort-Object { $_.Extent.StartOffset })
  if ($assignments.Count -eq 0) { return $null }

  $dominatingAssignments = @($assignments | Where-Object {
      $_.Operator -eq [System.Management.Automation.Language.TokenKind]::Equals -and
        [object]::ReferenceEquals((Get-StatementContainer -Node $_), $commandContainer)
    })
  if ($dominatingAssignments.Count -eq 0) { return $assignments }

  $cutoff = $dominatingAssignments[-1].Extent.StartOffset
  return @($assignments | Where-Object { $_.Extent.StartOffset -ge $cutoff })
}

function Get-ReachingCommandAssignments {
  param([Parameter(Mandatory = $true)][System.Management.Automation.Language.CommandAst]$Command)

  if ($Command.CommandElements.Count -eq 0 -or
      $Command.CommandElements[0] -isnot [System.Management.Automation.Language.VariableExpressionAst]) {
    return @()
  }

  return @(Get-ReachingVariableAssignments `
      -VariableName $Command.CommandElements[0].VariablePath.UserPath `
      -AtNode $Command)
}

function Get-NormalizedCommandNames {
  param([Parameter(Mandatory = $true)][System.Management.Automation.Language.CommandAst]$Command)

  $name = $Command.GetCommandName()
  if (-not [string]::IsNullOrWhiteSpace($name)) {
    $unquoted = $name.Trim([char[]]@([char]39, [char]34))
    return @([System.IO.Path]::GetFileName($unquoted).ToLowerInvariant())
  }

  if ($Command.CommandElements.Count -gt 0) {
    $directName = Get-ProvableCommandName -Expression $Command.CommandElements[0]
    if ($directName -in @('powershell', 'powershell.exe', 'pwsh', 'pwsh.exe')) {
      return @($directName)
    }
  }

  $assignments = @(Get-ReachingCommandAssignments -Command $Command)
  $names = foreach ($assignment in $assignments) {
    if ($null -eq $assignment -or $null -eq $assignment.Right) { continue }
    Get-ProvableCommandName -Expression $assignment.Right
  }
  return @($names | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique)
}

function Get-CommandElementText {
  param([Parameter(Mandatory = $true)]$Element)

  if ($Element -is [System.Management.Automation.Language.StringConstantExpressionAst]) {
    return $Element.Value
  }
  return $Element.Extent.Text
}

function Get-CommandArgumentText {
  param([Parameter(Mandatory = $true)][System.Management.Automation.Language.CommandAst]$Command)

  return @($Command.CommandElements | Select-Object -Skip 1 | ForEach-Object {
      $constantValue = Get-ConstantStringValue -Expression $_ -AtNode $_
      if ($null -ne $constantValue) { $constantValue } else { Get-CommandElementText -Element $_ }
    })
}

function Test-FileParameterAlias {
  param([Parameter(Mandatory = $true)][string]$Argument)

  return $Argument -match '(?i)^-f(?:i(?:l(?:e)?)?)?$'
}

function Test-Ps1ScriptArgument {
  param([Parameter(Mandatory = $true)][string]$Argument)

  $unquoted = $Argument.Trim([char[]]@([char]39, [char]34))
  return [System.IO.Path]::GetExtension($unquoted) -ieq '.ps1'
}

function Test-CanonicalPwshArguments {
  param([Parameter(Mandatory = $true)][string[]]$Arguments)

  return $Arguments.Count -ge 5 -and
    $Arguments[0] -ieq '-NoProfile' -and
    $Arguments[1] -ieq '-ExecutionPolicy' -and
    $Arguments[2] -ieq 'Bypass' -and
    $Arguments[3] -ieq '-File'
}

function Get-CommandViolationCategory {
  param(
    [Parameter(Mandatory = $true)][System.Management.Automation.Language.CommandAst]$Command,
    [switch]$Document
  )

  $commandNames = @(Get-NormalizedCommandNames -Command $Command)
  if ($commandNames.Count -eq 0) { return $null }

  $arguments = @(Get-CommandArgumentText -Command $Command)
  if (@($commandNames | Where-Object { $_ -in @('powershell', 'powershell.exe') }).Count -gt 0) {
    $documentCommandLike = $arguments.Count -gt 0 -and
      ($arguments[0] -match '^-' -or (Test-Ps1ScriptArgument -Argument $arguments[0]))
    if (-not $Document -or $documentCommandLike) {
      return $(if ($Document) { 'PW7_FORBIDDEN_DOCUMENT_COMMAND' } else { 'PW7_FORBIDDEN_SCRIPT_COMMAND' })
    }
    return $null
  }

  if (@($commandNames | Where-Object { $_ -in @('pwsh', 'pwsh.exe') }).Count -gt 0) {
    $usesFileParameter = @($arguments | Where-Object { Test-FileParameterAlias -Argument $_ }).Count -gt 0
    $usesPs1Script = @($arguments | Where-Object { Test-Ps1ScriptArgument -Argument $_ }).Count -gt 0
    if (($usesFileParameter -or $usesPs1Script) -and -not (Test-CanonicalPwshArguments -Arguments $arguments)) {
      return 'PW7_NONCANONICAL_PWSH_COMMAND'
    }
  }

  return $null
}

$documents = if ([string]::IsNullOrWhiteSpace($DocumentPaths)) { $defaultDocuments } else { Split-PathList -Value $DocumentPaths }
$requiredVersions = if ([string]::IsNullOrWhiteSpace($RequiredVersionPaths)) { $defaultRequiredVersions } else { Split-PathList -Value $RequiredVersionPaths }

if ([string]::IsNullOrWhiteSpace($ScriptPaths)) {
  $toolsRoot = Join-Path $root 'tools'
  $scripts = @(
    Get-ChildItem -LiteralPath $toolsRoot -Filter '*.ps1' -File -Recurse |
      ForEach-Object { $_.FullName }
  )
}
else {
  $scripts = Split-PathList -Value $ScriptPaths
}

foreach ($document in $documents) {
  $path = Resolve-ScanPath -Path $document
  if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
    Add-Finding -Category 'PW7_PATH_NOT_FOUND' -Path $path -Line 1
    continue
  }

  $lines = [System.IO.File]::ReadAllLines($path)
  $hereStringEnd = $null
  for ($index = 0; $index -lt $lines.Count; $index++) {
    if ($hereStringEnd) {
      if ($lines[$index] -match "^\s*$([regex]::Escape($hereStringEnd))\s*$") {
        $hereStringEnd = $null
      }
      continue
    }
    if ($lines[$index] -match '@(?<quote>[''"])\s*$') {
      $hereStringEnd = $Matches.quote + '@'
      continue
    }

    foreach ($candidate in Get-DocumentCommandCandidate -Line $lines[$index]) {
      $candidateTokens = $null
      $candidateErrors = $null
      $candidateAst = [System.Management.Automation.Language.Parser]::ParseInput($candidate, [ref]$candidateTokens, [ref]$candidateErrors)
      $candidateCommands = $candidateAst.FindAll({
          param($node)
          $node -is [System.Management.Automation.Language.CommandAst]
        }, $true)
      foreach ($candidateCommand in $candidateCommands) {
        $category = Get-CommandViolationCategory -Command $candidateCommand -Document
        if ($category) {
          Add-Finding -Category $category -Path $path -Line ($index + 1)
        }
      }
    }
  }
}

foreach ($script in $scripts) {
  $path = Resolve-ScanPath -Path $script
  if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
    Add-Finding -Category 'PW7_PATH_NOT_FOUND' -Path $path -Line 1
    continue
  }

  $tokens = $null
  $parseErrors = $null
  $ast = [System.Management.Automation.Language.Parser]::ParseFile($path, [ref]$tokens, [ref]$parseErrors)
  foreach ($parseError in $parseErrors) {
    Add-Finding -Category 'PW7_SCRIPT_PARSE_ERROR' -Path $path -Line $parseError.Extent.StartLineNumber
  }

  $commands = $ast.FindAll({
      param($node)
      $node -is [System.Management.Automation.Language.CommandAst]
    }, $true)
  foreach ($command in $commands) {
    $category = Get-CommandViolationCategory -Command $command
    if ($category) {
      Add-Finding -Category $category -Path $path -Line $command.Extent.StartLineNumber
    }
  }
}

foreach ($requiredVersion in $requiredVersions) {
  $path = Resolve-ScanPath -Path $requiredVersion
  if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
    Add-Finding -Category 'PW7_PATH_NOT_FOUND' -Path $path -Line 1
    continue
  }

  $requiredTokens = $null
  $requiredParseErrors = $null
  $requiredAst = [System.Management.Automation.Language.Parser]::ParseFile($path, [ref]$requiredTokens, [ref]$requiredParseErrors)
  foreach ($parseError in $requiredParseErrors) {
    Add-Finding -Category 'PW7_SCRIPT_PARSE_ERROR' -Path $path -Line $parseError.Extent.StartLineNumber
  }

  $requiredPSVersion = $requiredAst.ScriptRequirements.RequiredPSVersion
  if (-not $requiredPSVersion -or -not $requiredPSVersion.Equals([version]'7.0')) {
    Add-Finding -Category 'PW7_MISSING_REQUIRES' -Path $path -Line 1
  }
}

if ($findings.Count -gt 0) {
  foreach ($finding in $findings) {
    [System.Console]::Error.WriteLine($finding)
  }
  exit 1
}

Write-Output 'check-pwsh-runtime: OK'
