#requires -Version 7.0

[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot 'automation-commit-metadata.ps1')

function Assert-Equal {
  param($Actual, $Expected, [string]$Message)
  if ($Actual -cne $Expected) {
    throw "$Message (actual=$Actual expected=$Expected)"
  }
}

function Assert-Rejected {
  param([string]$Message, [string]$ExpectedTask = 'TASK-META-001', [string]$ExpectedState = 'completed')
  try {
    $null = ConvertFrom-TzgAutomationCommitMessage `
      -Message $Message `
      -ExpectedTask $ExpectedTask `
      -ExpectedState $ExpectedState
  } catch {
    return
  }
  throw 'Invalid automation metadata was accepted.'
}

$valid = @'
test: valid automation metadata

Automation: tzg-hourly-controller
Task: TASK-META-001
State: completed
Result: 问题=缺少统一契约；完成=所有调用点复用同一解析器
Impact: 影响=元数据只维护一份；边界=不修改任务与租约
Verify: 验证=直接测试通过；后续=等待调用点接入
Plain: 发生=自动化提交说明使用同一套规则；影响=失败原因不再因调用点不同而变化；需要=无需处理
'@
$metadata = ConvertFrom-TzgAutomationCommitMessage `
  -Message $valid `
  -ExpectedTask 'TASK-META-001' `
  -ExpectedState 'completed'
Assert-Equal $metadata.Goal '缺少统一契约' 'Result goal was parsed incorrectly'
Assert-Equal $metadata.Completed '所有调用点复用同一解析器' 'Result completion was parsed incorrectly'
Assert-Equal $metadata.Impact '元数据只维护一份' 'Impact was parsed incorrectly'
Assert-Equal $metadata.Next '等待调用点接入' 'Next relationship was parsed incorrectly'
Assert-Equal $metadata.PlainAction '无需处理' 'Plain action was parsed incorrectly'

$pendingReview = $valid.Replace('State: completed', 'State: pending_review')
$pending = ConvertFrom-TzgAutomationCommitMessage `
  -Message $pendingReview `
  -ExpectedTask 'TASK-META-001' `
  -ExpectedState 'pending_review'
Assert-Equal $pending.State 'pending_review' 'Pending-review state was rejected'

$invalidMessages = @(
  $valid.Replace('Automation: tzg-hourly-controller', 'Automation: other'),
  $valid.Replace('Task: TASK-META-001', 'Task: TASK-META-002'),
  $valid.Replace('State: completed', 'State: failed'),
  $valid.Replace('Result: 问题=缺少统一契约；完成=所有调用点复用同一解析器', 'Result: 完成=所有调用点复用同一解析器'),
  $valid.Replace('Result: 问题=缺少统一契约；完成=所有调用点复用同一解析器', 'Result: 问题=缺少统一契约'),
  $valid.Replace('Impact: 影响=元数据只维护一份；边界=不修改任务与租约', 'Impact: 边界=不修改任务与租约'),
  $valid.Replace('Impact: 影响=元数据只维护一份；边界=不修改任务与租约', 'Impact: 影响=元数据只维护一份'),
  $valid.Replace('Verify: 验证=直接测试通过；后续=等待调用点接入', 'Verify: 后续=等待调用点接入'),
  $valid.Replace('Verify: 验证=直接测试通过；后续=等待调用点接入', 'Verify: 验证=直接测试通过'),
  $valid.Replace('Plain: 发生=自动化提交说明使用同一套规则；影响=失败原因不再因调用点不同而变化；需要=无需处理', 'Plain: 影响=失败原因不再因调用点不同而变化；需要=无需处理'),
  $valid.Replace('Plain: 发生=自动化提交说明使用同一套规则；影响=失败原因不再因调用点不同而变化；需要=无需处理', 'Plain: 发生=自动化提交说明使用同一套规则；需要=无需处理'),
  $valid.Replace('Plain: 发生=自动化提交说明使用同一套规则；影响=失败原因不再因调用点不同而变化；需要=无需处理', 'Plain: 发生=自动化提交说明使用同一套规则；影响=失败原因不再因调用点不同而变化'),
  $valid.Replace('Verify: 验证=直接测试通过；后续=等待调用点接入', "Verify: 验证=直接测试通过`n后续=等待调用点接入"),
  $valid.Replace('需要=无需处理', "需要=无需`t处理"),
  $valid.Replace('需要=无需处理', "需要=$('长' * 201)"),
  ($valid + "`nVerify: 验证=重复；后续=重复")
)
foreach ($invalidMessage in $invalidMessages) {
  Assert-Rejected -Message $invalidMessage
}

'test-automation-commit-metadata: OK'
