# 飞书决策通道实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 把 `tzg-hourly-controller` 的人工决策确认从 Gmail 搜索/回复迁移为飞书企业自建应用的互动卡片，并以本机长连接桥接服务安全、确定性地消费 A/B/C 选择。

**Architecture:** Node.js 桥接包负责飞书 SDK、互动卡片、长连接回调、HMAC 信封和本机收件箱；PowerShell 控制器只调用经过净化的发送/消费 CLI，不读取聊天历史，也不接触 App Secret。状态机升级到 schema v7，保留 Gmail 历史作为只读迁移证据，并把当前 `DEC-20260715-75D7BA2AF210` 原地迁移到飞书，不新建决定。真实凭据、Open ID 和收件箱全部保存在用户级私有目录，Git 中只提交代码、测试和不含秘密的规则文档。

**Tech Stack:** PowerShell 7（唯一受支持 PowerShell 运行时）、Node.js `>=20`、`@larksuiteoapi/node-sdk@1.71.1`、Node 内置 `node:test`/`crypto`/`fs`、Windows 任务计划程序、现有 Codex 自动化控制器与状态机。

## Global Constraints

- 以 `docs/superpowers/specs/2026-07-15-feishu-decision-channel-design.md` 和 `docs/superpowers/specs/2026-07-15-powershell7-runtime-contract-design.md` 为批准规格；实现与规格冲突时先停下修订规格，不静默改变安全边界。
- 所有项目 PowerShell 命令使用 `pwsh -NoProfile -ExecutionPolicy Bypass -File ...`；不得调用 `powershell`、`powershell.exe` 或 Windows PowerShell 5.1。Task 0 门禁通过前不得开始飞书代码实现。
- 开始真实部署前暂停 `tzg-hourly-controller`，整个迁移期间只允许一个写入型控制器；完成灰度和状态核验后再恢复。
- 不直接编辑 Codex 自动化 TOML。提示词部署必须通过应用提供的自动化更新工具完成。
- 不把 App ID、App Secret、收件人、Open ID、HMAC key、原始飞书事件或原始消息 ID 写入 Git、自动化 memory、控制台、提交信息或项目状态文件。
- 私有配置固定为 `%USERPROFILE%\.codex\automation-state\tzg-hourly-controller.feishu.private.json`；桥接状态固定在 `%USERPROFILE%\.codex\automation-state\tzg-feishu-decision-bridge\`。
- Node CLI 只输出净化 JSON；PowerShell 通过临时文件/stdin 传业务载荷，禁止把秘密或原始身份值放入命令行参数。
- `CHANNEL_UNAVAILABLE` 表示 provider 调用前的长连接/SDK/client 或配置前置不可用，零 provider 调用、不建发送意图、不计入真实发送重试；`PROVIDER_OUTCOME_UNKNOWN` 表示 provider 可能已收到请求，不计失败重试且不允许换新 attempt/UUID 自动补发；只有明确 `DELIVERY_FAILED`、`MISADDRESSED` 才计入对应 provider 的失败尝试。
- 旧 Gmail 动作和历史字段可以保留用于回滚/迁移，但不得出现在活动 `Contract.actions`、`nextCommands`、控制器提示词或模型可选择的工作流中。
- 每个任务遵循红—绿—重构：先写会失败的直接测试，确认失败原因正确，再写最小实现，最后运行该切片的最小充分验证。
- 任何需要真实飞书账号或 Windows 任务计划程序的步骤均放在“真实部署”任务中；此前的所有测试使用临时目录、假 transport 和假 scheduler。
- 提交前只暂存本计划列出的路径；保留工作区中不相关的用户文件。运行 `tools/check-pending-whitespace.ps1` 后再 `git add`，暂存后运行 `git diff --cached --check`。

## Runtime Contract

### 私有配置 schema 1

```json
{
  "schemaVersion": 1,
  "appId": "stored-locally",
  "appSecret": "stored-locally",
  "expectedTenantKey": null,
  "recipient": {
    "type": "email",
    "value": "stored-locally"
  },
  "pairedOperatorOpenIdHash": null,
  "hmacKey": "base64-encoded-32-byte-key",
  "stateRoot": "absolute-user-level-path"
}
```

`recipient.type` 只允许 `email` 或 `open_id`。日志和 CLI 结果只允许暴露 `sha256(appId)`、`sha256(recipient.value)`、`sha256(open_id)` 等不可逆摘要。

### HMAC 信封 schema 1

```json
{
  "schemaVersion": 1,
  "payload": {
    "kind": "decision_reply",
    "decisionId": "DEC-20260715-75D7BA2AF210",
    "optionKey": "A",
    "cardNonceHash": "64-lowercase-hex",
    "providerMessageIdHash": "64-lowercase-hex",
    "providerEventIdHash": "64-lowercase-hex",
    "operatorOpenIdHash": "64-lowercase-hex",
    "tenantKeyHash": "64-lowercase-hex",
    "receivedAt": "2026-07-15T00:00:00.000Z"
  },
  "signature": "64-lowercase-hex"
}
```

签名输入是递归按键名排序、无额外空白的 `payload` JSON UTF-8 字节；使用 HMAC-SHA256。信封写入必须先写同目录临时文件，再原子 rename。

### 模型可见控制器动作

```text
CreateDecision -> SendDecisionNotification -> CompleteNoChange
Start(存在待决策) -> ConsumeDecisionReply
ConsumeDecisionReply(无有效回复) -> CompleteNoChange
ConsumeDecisionReply(有效回复) -> InspectCandidate(原始 TaskId)
ResolveDecisionManual -> InspectCandidate(原始 TaskId)
```

模型不得提交 `decisionId`、`ReplyText`、目标用户、provider message ID 或原始回调字段。`SendDecisionNotification` 和 `ConsumeDecisionReply` 均从受锁定的当前状态读取唯一待决策。

## File Map

### 新增文件

```text
tools/feishu-decision-bridge/package.json
tools/feishu-decision-bridge/package-lock.json
tools/feishu-decision-bridge/src/config.mjs
tools/feishu-decision-bridge/src/card.mjs
tools/feishu-decision-bridge/src/envelope.mjs
tools/feishu-decision-bridge/src/send-core.mjs
tools/feishu-decision-bridge/src/send-runtime.mjs
tools/feishu-decision-bridge/src/send-decision.mjs
tools/feishu-decision-bridge/src/send-intent-store.mjs
tools/feishu-decision-bridge/src/callback-core.mjs
tools/feishu-decision-bridge/src/inbox.mjs
tools/feishu-decision-bridge/src/bridge.mjs
tools/feishu-decision-bridge/src/consume-reply.mjs
tools/feishu-decision-bridge/test/core.test.mjs
tools/feishu-decision-bridge/test/send.test.mjs
tools/feishu-decision-bridge/test/callback.test.mjs
tools/feishu-decision-bridge/test/consume.test.mjs
tools/setup-feishu-decision-channel.ps1
tools/install-feishu-decision-bridge.ps1
tools/start-feishu-decision-bridge.ps1
tools/test-setup-feishu-decision-channel.ps1
tools/test-install-feishu-decision-bridge.ps1
tools/check-pwsh-runtime.ps1
tools/test-check-pwsh-runtime.ps1
```

### 修改文件

```text
docs/superpowers/specs/2026-07-15-feishu-decision-channel-design.md
docs/superpowers/plans/2026-07-15-feishu-decision-channel-implementation.md
tools/automation-controller-state.ps1
tools/test-automation-controller-state.ps1
tools/automation-controller.ps1
tools/test-automation-controller.ps1
tools/automation-decision-status.ps1
tools/test-automation-decision-status.ps1
tools/check-automation-workflow.ps1
tools/test-check-pending-whitespace.ps1
tools/tests/check-asset-versioning-tests.ps1
tools/tests/check-data-chain-tests.ps1
AGENTS.md
CLAUDE.md
开发管理/开发-技术经验.txt
开发管理/状态与建议维护规则.txt
开发管理/当前任务队列.txt
开发管理/任务列表/内容设计任务.txt
开发管理/任务列表/场景与Unity任务.txt
开发管理/任务列表/数值与战斗任务.txt
开发管理/任务列表/数据链路任务.txt
开发管理/自动工作流规则.txt
开发管理/自动工作流控制器提示词.txt
开发管理/自动工作流状态.txt
```

---

## Task 0: 建立 PowerShell 7 唯一运行时门禁

**Files:**

- Create: `tools/check-pwsh-runtime.ps1`
- Create: `tools/test-check-pwsh-runtime.ps1`
- Modify: `AGENTS.md`
- Modify: `CLAUDE.md`
- Modify: `开发管理/开发-技术经验.txt`
- Modify: `开发管理/状态与建议维护规则.txt`
- Modify: `开发管理/当前任务队列.txt`
- Modify: `开发管理/任务列表/内容设计任务.txt`
- Modify: `开发管理/任务列表/场景与Unity任务.txt`
- Modify: `开发管理/任务列表/数值与战斗任务.txt`
- Modify: `开发管理/任务列表/数据链路任务.txt`
- Modify: `tools/automation-controller.ps1`
- Modify: `tools/automation-controller-state.ps1`
- Modify: `tools/check-automation-workflow.ps1`
- Modify: `tools/check-review-text.ps1`
- Modify: `tools/check-data-chain.ps1`
- Modify: `tools/check-pending-whitespace.ps1`
- Modify: `tools/run-unity-editmode-tests.ps1`
- Modify: `tools/test-check-pending-whitespace.ps1`
- Modify: `tools/tests/check-asset-versioning-tests.ps1`
- Modify: `tools/tests/check-data-chain-tests.ps1`
- Modify: `docs/superpowers/plans/2026-07-15-feishu-decision-channel-implementation.md`

- [ ] **Step 1: 先写运行时门禁失败测试**

创建 `tools/test-check-pwsh-runtime.ps1`。测试在唯一临时目录写入独立 fixture，并通过以下接口调用尚不存在的 checker：

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-pwsh-runtime.ps1 `
  -RepositoryRoot $fixtureRoot `
  -DocumentPaths ($documentPaths -join '|') `
  -ScriptPaths ($scriptPaths -join '|') `
  -RequiredVersionPaths ($requiredVersionPaths -join '|')
```

测试必须包含以下实际 fixture 和断言：

```powershell
$windowsPowerShell = 'power' + 'shell'
$badDocumentCases = @(
  "$windowsPowerShell -File tools/check.ps1",
  "$windowsPowerShell -ExecutionPolicy Bypass -File tools/check.ps1",
  "${windowsPowerShell}.exe -NoProfile -ExecutionPolicy Bypass -File tools/check.ps1",
  "& $windowsPowerShell -File tools/check.ps1"
)
$allowedDocumentCases = @(
  'pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check.ps1',
  '```powershell',
  'PowerShell 7 is required',
  'Invoke-ChildPowerShell'
)
$badScript = @"
#requires -Version 7.0
& $windowsPowerShell -NoProfile -File tools/check.ps1
"@
$goodScript = @'
#requires -Version 7.0
& pwsh -NoProfile -File tools/check.ps1
$name = 'power' + 'shell -File is forbidden text, not a command'
'@
$missingRequires = @'
param()
'runtime gate missing'
'@
```

每个 bad document case 必须返回非零并含 `PW7_FORBIDDEN_DOCUMENT_COMMAND`；`$badScript` 必须返回非零并含 `PW7_FORBIDDEN_SCRIPT_COMMAND`；`$missingRequires` 必须含 `PW7_MISSING_REQUIRES`；全部 allowed cases 和 `$goodScript` 必须返回 0。测试最后输出 `test-check-pwsh-runtime: OK`，并在 `finally` 中只删除已验证位于 `$env:TEMP` 下的唯一 fixture 目录。

- [ ] **Step 2: 运行测试并确认红灯原因**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-check-pwsh-runtime.ps1
```

预期：失败信息明确指出 `tools/check-pwsh-runtime.ps1 is missing`；不得因为 fixture 路径、编码或测试语法错误而失败。

- [ ] **Step 3: 实现最小静态检查器**

`tools/check-pwsh-runtime.ps1` 的公开参数固定为：

```powershell
[CmdletBinding()]
param(
  [string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path,
  [string]$DocumentPaths,
  [string]$ScriptPaths,
  [string]$RequiredVersionPaths
)
```

未显式传路径时，默认 document 集合固定为：

```powershell
$defaultDocuments = @(
  'AGENTS.md',
  'CLAUDE.md',
  '开发管理/开发-技术经验.txt',
  '开发管理/状态与建议维护规则.txt',
  '开发管理/自动工作流规则.txt',
  '开发管理/自动工作流控制器提示词.txt',
  '开发管理/当前任务队列.txt',
  '开发管理/任务列表/内容设计任务.txt',
  '开发管理/任务列表/场景与Unity任务.txt',
  '开发管理/任务列表/数值与战斗任务.txt',
  '开发管理/任务列表/数据链路任务.txt',
  'docs/superpowers/plans/2026-07-15-feishu-decision-channel-implementation.md'
)
```

默认 script 集合为 `tools/**/*.ps1`；默认 required-version 集合固定为：

```powershell
$defaultRequiredVersions = @(
  'tools/automation-controller.ps1',
  'tools/automation-controller-state.ps1',
  'tools/check-automation-workflow.ps1',
  'tools/check-review-text.ps1',
  'tools/check-data-chain.ps1',
  'tools/check-pending-whitespace.ps1',
  'tools/run-unity-editmode-tests.ps1'
)
```

document 扫描只匹配真实命令形态；`.ps1` 扫描用 `[System.Management.Automation.Language.Parser]::ParseFile` 并遍历 `CommandAst`，当 `GetCommandName()` 为 `powershell` 或 `powershell.exe` 时拒绝。required-version 文件必须匹配 `(?im)^\s*#requires\s+-Version\s+7(?:\.0)?\s*$`。所有违规按 `CATEGORY relative/path:line` 输出到 stderr，最后退出 1；没有违规时只输出 `check-pwsh-runtime: OK`。

- [ ] **Step 4: 运行 fixture 测试，确认检查器转绿**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-check-pwsh-runtime.ps1
```

预期：输出 `test-check-pwsh-runtime: OK`，退出码 0。

- [ ] **Step 5: 运行仓库扫描并确认第二个红灯**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-pwsh-runtime.ps1
```

预期：当前活动事实源和三个测试脚本中的既有 Windows PowerShell 调用被报告；七个关键入口因缺少 `#requires -Version 7.0` 被报告。历史归档不在输出中。

- [ ] **Step 6: 迁移活动事实源和脚本调用**

在 `AGENTS.md` 与 `CLAUDE.md` 的高频检查前加入完全相同的硬规则：

```text
- 项目 PowerShell 脚本唯一支持 PowerShell 7；所有独立进程命令必须使用 `pwsh -NoProfile -ExecutionPolicy Bypass -File ...`。禁止调用 `powershell`、`powershell.exe` 或 Windows PowerShell 5.1。已在 PowerShell 7 会话内时可用 `& tools/<script>.ps1 ...`。
```

把 Task 0 文件列表中活动事实源和测试脚本的真实 Windows PowerShell 调用全部改为 `pwsh -NoProfile -ExecutionPolicy Bypass -File SCRIPT.ps1`；普通描述、Markdown 围栏和历史归档不改。`开发管理/开发-技术经验.txt` 的 PowerShell 7 条目改为“唯一受支持运行时”，删除“需要新版参数时才使用 pwsh”的条件语义。

- [ ] **Step 7: 给关键入口添加版本声明**

七个 required-version 文件的第一条有效声明均为：

```powershell
#requires -Version 7.0
```

不得改变脚本参数、输出或退出码。`automation-controller.ps1` 的 `Invoke-ChildPowerShell` 继续使用当前进程路径，因此父入口通过 `#requires` 后子进程天然保持 PowerShell 7。

- [ ] **Step 8: 运行新门禁和直接相关测试**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-check-pwsh-runtime.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-pwsh-runtime.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-check-pending-whitespace.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/tests/check-asset-versioning-tests.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/tests/check-data-chain-tests.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-automation-workflow.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-review-text.ps1 -Paths AGENTS.md,CLAUDE.md,开发管理
```

逐项检查退出码。预期：所有命令退出 0；输出不存在 Windows PowerShell 5.1 解析错误；最后两项分别输出 `check-automation-workflow: OK` 和 `check-review-text: OK`。

- [ ] **Step 9: 重新取得飞书实施基线**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-automation-controller-state.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-automation-controller.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-automation-decision-status.ps1
```

预期：三项均退出 0。`test-automation-controller.ps1` 现有 123 次子进程场景约需 4—5 分钟，应给单项至少 10 分钟上限，不得用包含全部检查的 120 秒共享超时。

- [ ] **Step 10: 检查范围并提交 Task 0**

```powershell
$task0Paths = @(
  'tools/check-pwsh-runtime.ps1',
  'tools/test-check-pwsh-runtime.ps1',
  'AGENTS.md',
  'CLAUDE.md',
  '开发管理/开发-技术经验.txt',
  '开发管理/状态与建议维护规则.txt',
  '开发管理/当前任务队列.txt',
  '开发管理/任务列表/内容设计任务.txt',
  '开发管理/任务列表/场景与Unity任务.txt',
  '开发管理/任务列表/数值与战斗任务.txt',
  '开发管理/任务列表/数据链路任务.txt',
  'tools/automation-controller.ps1',
  'tools/automation-controller-state.ps1',
  'tools/check-automation-workflow.ps1',
  'tools/check-review-text.ps1',
  'tools/check-data-chain.ps1',
  'tools/check-pending-whitespace.ps1',
  'tools/run-unity-editmode-tests.ps1',
  'tools/test-check-pending-whitespace.ps1',
  'tools/tests/check-asset-versioning-tests.ps1',
  'tools/tests/check-data-chain-tests.ps1',
  'docs/superpowers/plans/2026-07-15-feishu-decision-channel-implementation.md'
)
& tools/check-pending-whitespace.ps1 -ExpectedPaths ($task0Paths -join '|')
git add -- $task0Paths
git diff --cached --check
git diff --cached --name-only
git commit -m "chore: require PowerShell 7 runtime"
```

预期：提交只包含 `$task0Paths`；不包含历史归档、私有飞书配置、主工作区未跟踪文件或运行时状态。

## Task 1: 冻结写入器并保存迁移基线

**Files:**

- Inspect: `%USERPROFILE%\.codex\automation-state\tzg-hourly-controller.json`
- Inspect: `%USERPROFILE%\.codex\automation-state\tzg-hourly-controller-runs\`
- Inspect: `开发管理/自动工作流状态.txt`
- Modify later in Task 9: `开发管理/自动工作流状态.txt`

- [ ] **Step 1: 暂停唯一自动化写入器**

通过 Codex 应用的自动化查询工具找到标题为 `tzg-hourly-controller` 的自动化，再用 `codex_app__automation_update` 将其状态设为暂停。不要直接改 TOML。重新查询并确认返回状态为暂停。

- [ ] **Step 2: 读取并核对当前决定**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/automation-controller-state.ps1 Show
git status --short
```

预期：状态 JSON 为 schema v6；控制器没有活动租约（`state=IDLE`、`runId=null`，或租约已经明确过期并按现有恢复规则关闭）；`decisionFlow.status` 为 `AWAITING_DECISION`；`pendingDecision.decisionId` 为 `DEC-20260715-75D7BA2AF210`；已有两条 Gmail 尝试（一条 `DELIVERY_FAILED`、一条 `PROVIDER_ACCEPTED`）。若任一事实不同，停止部署并先更新本计划中的迁移断言。另用 `Get-Process node` 和任务计划程序确认没有旧的 `TianZhang-Feishu-Decision-Bridge` 实例。

- [ ] **Step 3: 保存用户级迁移备份并记录只读摘要**

```powershell
$migrationRoot = Join-Path $env:USERPROFILE '.codex\automation-state\tzg-feishu-migration-20260715'
New-Item -ItemType Directory -Force -Path $migrationRoot | Out-Null
$currentUser = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
& icacls.exe $migrationRoot /inheritance:r
& icacls.exe $migrationRoot /grant:r "$currentUser`:(OI)(CI)F" 'SYSTEM:(OI)(CI)F'
Copy-Item -LiteralPath (Join-Path $env:USERPROFILE '.codex\automation-state\tzg-hourly-controller.json') -Destination (Join-Path $migrationRoot 'state-v6-before.json')
Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $migrationRoot 'state-v6-before.json')
git rev-parse HEAD
```

收紧迁移目录 ACL 为当前用户和 SYSTEM 后，在执行日志中只记录备份 SHA-256、schema、决定 ID、状态、尝试数量和 Git HEAD；不得复制私有地址或原始消息 ID。此步骤不修改项目文件。

## Task 2: 建立 Node 包和可验证的安全核心

**Files:**

- Create: `tools/feishu-decision-bridge/package.json`
- Create: `tools/feishu-decision-bridge/package-lock.json`
- Create: `tools/feishu-decision-bridge/src/config.mjs`
- Create: `tools/feishu-decision-bridge/src/card.mjs`
- Create: `tools/feishu-decision-bridge/src/envelope.mjs`
- Create: `tools/feishu-decision-bridge/test/core.test.mjs`

- [ ] **Step 1: 创建固定版本的包清单**

`package.json` 使用以下完整内容：

```json
{
  "name": "tzg-feishu-decision-bridge",
  "private": true,
  "type": "module",
  "engines": {
    "node": ">=20"
  },
  "scripts": {
    "test": "node --test test/*.test.mjs"
  },
  "dependencies": {
    "@larksuiteoapi/node-sdk": "1.71.1"
  }
}
```

然后生成锁文件：

```powershell
Push-Location tools/feishu-decision-bridge
npm install --package-lock-only --ignore-scripts
Pop-Location
```

预期：生成 `package-lock.json`，根包和 SDK 都是锁定状态，无 lifecycle script 被执行。

- [ ] **Step 2: 先写核心失败测试**

`test/core.test.mjs` 至少覆盖：

```js
import test from 'node:test';
import assert from 'node:assert/strict';
import { parsePrivateConfig, sanitizeError, sha256 } from '../src/config.mjs';
import { buildDecisionCard } from '../src/card.mjs';
import { signEnvelope, verifyEnvelope } from '../src/envelope.mjs';

test('private config rejects unsupported recipient types and short HMAC keys', () => {
  assert.throws(() => parsePrivateConfig({ schemaVersion: 1, appId: 'a', appSecret: 's', recipient: { type: 'chat_id', value: 'x' }, hmacKey: 'eA==', stateRoot: 'C:/state' }));
});

test('card contains only the supplied decision and A B C actions with a nonce', () => {
  const card = buildDecisionCard({ decisionId: 'DEC-1', question: '选择？', options: [{ key: 'A', label: '甲' }, { key: 'B', label: '乙' }, { key: 'C', label: '丙' }], recommendedOption: 'A', impactSummary: '影响' }, 'nonce-1');
  const text = JSON.stringify(card);
  for (const key of ['A', 'B', 'C']) assert.match(text, new RegExp(`"optionKey":"${key}"`));
  assert.match(text, /"decisionId":"DEC-1"/);
  assert.doesNotMatch(text, /appSecret|hmacKey/);
});

test('tampered envelopes fail closed', () => {
  const envelope = signEnvelope({ kind: 'decision_reply', decisionId: 'DEC-1', optionKey: 'A' }, Buffer.alloc(32, 7).toString('base64'));
  envelope.payload.optionKey = 'B';
  assert.throws(() => verifyEnvelope(envelope, Buffer.alloc(32, 7).toString('base64')), /signature/i);
});

test('sanitized errors never expose configured secrets or identities', () => {
  const output = sanitizeError(new Error('secret-1 user@example.cn ou_123'), ['secret-1', 'user@example.cn', 'ou_123']);
  assert.equal(output.includes('secret-1'), false);
  assert.equal(output.includes('user@example.cn'), false);
  assert.equal(output.includes('ou_123'), false);
  assert.match(sha256('value'), /^[0-9a-f]{64}$/);
});
```

- [ ] **Step 3: 运行测试并确认红灯原因**

```powershell
Push-Location tools/feishu-decision-bridge
npm test
Pop-Location
```

预期：失败原因为 `config.mjs`、`card.mjs`、`envelope.mjs` 或导出函数尚不存在，而不是 Node/npm 环境错误。

- [ ] **Step 4: 实现纯函数核心**

实现并导出：

```js
// config.mjs
export function sha256(value) {}
export function parsePrivateConfig(raw) {}
export function sanitizeError(error, sensitiveValues) {}

// card.mjs
export function buildDecisionCard(decision, cardNonce) {}

// envelope.mjs
export function canonicalize(value) {}
export function signEnvelope(payload, hmacKeyBase64) {}
export function verifyEnvelope(envelope, hmacKeyBase64) {}
```

卡片按钮 value 只包含 `decisionId`、`optionKey`、`cardNonce`、`kind`；问题、选项、推荐项和影响摘要显示在卡片正文。配置解析必须拒绝未知字段类型、空秘密、相对 `stateRoot`、非 32 字节 HMAC key，并返回冻结后的标准化对象。HMAC 比较使用 `timingSafeEqual`。

- [ ] **Step 5: 运行核心测试**

```powershell
Push-Location tools/feishu-decision-bridge
npm test
Pop-Location
```

预期：4 个核心测试通过，退出码 0。

## Task 3: 实现飞书卡片发送适配器

**Files:**

- Create: `tools/feishu-decision-bridge/src/send-core.mjs`
- Create: `tools/feishu-decision-bridge/src/send-runtime.mjs`
- Create: `tools/feishu-decision-bridge/src/send-decision.mjs`
- Create: `tools/feishu-decision-bridge/src/send-intent-store.mjs`
- Create: `tools/feishu-decision-bridge/test/send.test.mjs`
- Modify: `docs/superpowers/specs/2026-07-15-feishu-decision-channel-design.md`
- Modify: `docs/superpowers/plans/2026-07-15-feishu-decision-channel-implementation.md`

官方接口依据：

- 消息发送：<https://open.feishu.cn/document/server-docs/im-v1/message/create>
- Node SDK：<https://open.feishu.cn/document/server-side-sdk/nodejs-sdk/overview>

- [ ] **Step 1: 先写 sender 失败测试**

测试通过注入的 `transport.sendInteractive` 验证：

- `email` 映射为 `receive_id_type=email`，`open_id` 映射为 `receive_id_type=open_id`。
- `msg_type` 固定为 `interactive`，`content` 是 `buildDecisionCard` 的 JSON 字符串。
- provider 成功只返回 `{ result, targetHash, providerMessageIdHash, cardNonceHash }`；缓存成功重跑也只返回这 4 个键。
- provider 显式非零业务码拒绝映射为 `DELIVERY_FAILED`；超时、断连、throw、非法响应或缺少 message ID 映射为 `PROVIDER_OUTCOME_UNKNOWN`，两者均不得返回原始响应、目标或秘密。
- health 文件缺失、过期超过 120 秒、PID 不存活、SDK 导入失败或 client 初始化失败时返回 `CHANNEL_UNAVAILABLE`，且 intent/provider 调用次数均为 0。
- 同一 `decisionId + provider + attemptNumber` 生成稳定 UUID；官方 UUID 去重只保证 1 小时，因此必须先持久化用户级 intent，55 分钟窗内同意图可用同 UUID/内容/目标/nonce 哈希重试，超窗后零 transport 并要求人工核对。
- API 接受后终态落盘崩溃、intent 损坏/不匹配/忙锁、两进程并发、accepted/rejected 缓存、原子文件不含原始秘密/身份/消息 ID 都有定向测试。
- Card nonce 使用私有 `hmacKey` 的领域隔 HMAC-SHA256 派生；同 intent 稳定，不同 attempt/key 不同，且不等于无密钥旧 digest。

代表性断言：

```js
assert.deepEqual(Object.keys(result).sort(), ['cardNonceHash', 'providerMessageIdHash', 'result', 'targetHash']);
assert.equal(result.result, 'PROVIDER_ACCEPTED');
assert.equal(JSON.stringify(result).includes('user@example.cn'), false);
assert.equal(sendCalls.length, 1);
```

- [ ] **Step 2: 运行 sender 测试并确认失败**

```powershell
Push-Location tools/feishu-decision-bridge
node --test test/send.test.mjs
Pop-Location
```

预期：失败原因为 sender 模块尚未实现。

- [ ] **Step 3: 实现注入式 sender 和 SDK runtime**

`send-core.mjs` 导出：

```js
export async function sendDecision({ config, decision, attemptNumber, transport, intentStore, health, now }) {}
```

`send-intent-store.mjs` 导出 `createSendIntentStore(stateRoot, options?)`，只访问 `<stateRoot>/send-intents/`。`intentKeyHash = SHA-256(domain + provider + decisionId + attemptNumber)`，文件 schema 1 只允许净化哈希、UUID、尝试号、时间和 `PREPARED | IN_FLIGHT | ACCEPTED | OUTCOME_UNKNOWN | REJECTED`。使用同目录随机临时文件、`FileHandle.sync()`、close 和原子 rename；排他 `<hash>.lock` 只保存 pid/time，活进程或不足 120 秒的锁都返回忙，仅死进程且超租约可清理一次。读取对大小、UTF-8/JSON、原型、未知字段、hex、时间、状态一律严格校验；损坏/不匹配证据不覆写。Core 必须在该锁内完成检查 → `IN_FLIGHT` 落盘 → provider 调用 → 终态落盘 → release。

`send-runtime.mjs` 仅在运行时创建 `new lark.Client({ appId, appSecret })`，并调用：

```js
client.im.message.create({
  params: { receive_id_type: config.recipient.type },
  data: {
    receive_id: config.recipient.value,
    msg_type: 'interactive',
    content: JSON.stringify(card),
    uuid: stableAttemptUuid
  }
});
```

Runtime 导出安全错误类：官方明确非零 code 抛 `ProviderRejectedError`；network/throw、code 0 但缺 ID、非法响应抛 `ProviderOutcomeUnknownError`，错误文本固定且不含 raw。SDK 导入/client 初始化失败是调用前 `CHANNEL_UNAVAILABLE`。

`send-decision.mjs` 从 `--request-file` 指定的用户级临时 JSON 读取决定，但私有配置路径使用默认值或 `FEISHU_DECISION_CONFIG_PATH` 环境变量；health 不健康时必须在创建 intent store/SDK 前直接返回 20。stdout 不原样透传下层对象，而是按结果类别重建严格白名单单行 JSON。退出码约定：0 为 `PROVIDER_ACCEPTED`，20 为 `CHANNEL_UNAVAILABLE`，21 为 `DELIVERY_FAILED`，22 为 `INVALID_INPUT`，23 为 `PROVIDER_OUTCOME_UNKNOWN`。

- [ ] **Step 4: 运行 sender 测试和完整 Node 测试**

```powershell
Push-Location tools/feishu-decision-bridge
node --test test/send.test.mjs
npm test
Pop-Location
```

预期：sender 测试和现有核心测试全部通过。

## Task 4: 实现长连接回调、HMAC 收件箱和确定性消费

**Files:**

- Create: `tools/feishu-decision-bridge/src/callback-core.mjs`
- Create: `tools/feishu-decision-bridge/src/inbox.mjs`
- Create: `tools/feishu-decision-bridge/src/bridge.mjs`
- Create: `tools/feishu-decision-bridge/src/consume-reply.mjs`
- Create: `tools/feishu-decision-bridge/test/callback.test.mjs`
- Create: `tools/feishu-decision-bridge/test/consume.test.mjs`

官方接口依据：

- 卡片回调：<https://open.feishu.cn/document/feishu-cards/card-callback-communication?lang=zh-CN>
- Node 长连接事件：<https://open.feishu.cn/document/server-side-sdk/nodejs-sdk/handling-callbacks>

- [ ] **Step 1: 先写回调拒绝路径测试**

使用固定事件 fixture 覆盖：错误 tenant、未配对操作人、过期卡片、错误 nonce、未知选项、错误决定 ID、重复 event ID。所有拒绝都必须 `accepted=false`、不写 inbox、返回不含内部细节的飞书 toast。

- [ ] **Step 2: 先写接受与消费测试**

在测试临时目录覆盖：

- 合法 `card.action.trigger` 生成 HMAC 信封并原子落盘。
- 回调只保存身份哈希，不保存原始 Open ID/tenant/event ID。
- `consumeCurrentReply` 只接受当前 `decisionId`、当前 `cardNonceHash`、当前已接受飞书通知的 `providerMessageIdHash`、允许的 A/B/C 和已配对操作人。
- 消费后文件移动到 `processed/`，不删除；签名损坏、陈旧决定和冲突选择移动到 `quarantine/`。
- 同 event 重放保持幂等；同决定出现 A/B 冲突时不自动决定，返回 `null` 并隔离两封信。
- CLI 无回复时输出 `{"result":"NO_REPLY"}`；有回复时只输出 option 和哈希证据。
- 接受回调返回更新后的只读卡片：“已选择 X”和登记时间，不再包含按钮；拒绝回调只返回“未登记/已过期”提示，不伪装成功。

净化回复的固定结构：

```json
{
  "result": "REPLY_ACCEPTED",
  "optionKey": "A",
  "source": "feishu_card",
  "providerMessageIdHash": "64-lowercase-hex",
  "providerEventIdHash": "64-lowercase-hex",
  "operatorOpenIdHash": "64-lowercase-hex",
  "tenantKeyHash": "64-lowercase-hex",
  "cardNonceHash": "64-lowercase-hex",
  "evidenceHash": "64-lowercase-hex"
}
```

- [ ] **Step 3: 运行回调/消费测试并确认失败**

```powershell
Push-Location tools/feishu-decision-bridge
node --test test/callback.test.mjs test/consume.test.mjs
Pop-Location
```

预期：失败原因为回调、inbox 或 consumer 模块尚未实现。

- [ ] **Step 4: 实现纯回调和收件箱**

导出以下接口：

```js
export function normalizeCardAction(rawEvent) {}
export function handleCardAction({ event, config, pendingBindings, now }) {}
export function writeSignedInbox({ stateRoot, envelope, eventIdHash }) {}
export function consumeCurrentReply({ stateRoot, config, pendingDecision, now }) {}
```

`pendingBindings` 是桥接目录中的用户级镜像，只包含 `decisionId`、允许选项、`expiresAt`、`cardNonceHash` 和 `providerMessageIdHash`。consumer 在校验签名后按 `receivedAt` 排序；只接受唯一一致选择。配对使用独立的 `kind=operator_pairing` 信封：可携带 tenant key 供私有配置登记，但不得写入项目状态、日志或 stdout；操作者始终只保存 Open ID 哈希。

- [ ] **Step 5: 实现 SDK 长连接 runtime**

`bridge.mjs` 使用 SDK 的 `EventDispatcher` 注册 `card.action.trigger`，再用 `WSClient.start({ eventDispatcher })` 启动长连接。启动成功后每 60 秒原子更新 `health.json`：

```json
{
  "schemaVersion": 1,
  "status": "CONNECTED",
  "pid": 1234,
  "updatedAt": "2026-07-15T00:00:00.000Z",
  "appIdHash": "64-lowercase-hex"
}
```

断线时把 status 改为 `DISCONNECTED`，SDK 重连交给官方客户端；所有日志经过 `sanitizeError`。

- [ ] **Step 6: 运行 Node 全套测试**

```powershell
Push-Location tools/feishu-decision-bridge
npm test
Pop-Location
```

预期：核心、发送、回调、消费测试全部通过，测试输出不存在 `appSecret`、邮箱地址或 `ou_` 原始值。

## Task 5: 实现私有配置、配对和 Windows 登录启动脚本

**Files:**

- Create: `tools/setup-feishu-decision-channel.ps1`
- Create: `tools/install-feishu-decision-bridge.ps1`
- Create: `tools/start-feishu-decision-bridge.ps1`
- Create: `tools/test-setup-feishu-decision-channel.ps1`
- Create: `tools/test-install-feishu-decision-bridge.ps1`

- [ ] **Step 1: 先写配置脚本测试**

`test-setup-feishu-decision-channel.ps1` 在临时目录中使用 `-ConfigValues` 注入假值，覆盖：

- `Configure` 生成 schema 1、32 字节随机 HMAC key 和绝对 `stateRoot`。
- 收件人类型只允许 `email|open_id`。
- 文件 ACL 禁止继承，只保留当前用户和 `SYSTEM` 完全控制。
- 脚本输出只包含 app/recipient 哈希。
- `Pair` 只接受与一次性 nonce 对应的配对信封，并写入 tenant/operator 哈希。
- `Canary` 不会绕过已配对身份。

测试必须传 `-StateRoot` 到临时目录，绝不读取真实私有配置。

- [ ] **Step 2: 先写安装脚本测试**

`test-install-feishu-decision-bridge.ps1` 使用 `-SchedulerAdapter` 假对象，覆盖：

- `Plan` 返回固定任务名 `TianZhang-Feishu-Decision-Bridge`、登录触发器和隐藏启动命令，不写系统状态。
- `Install` 先验证 Node `>=20`、`package-lock.json` 和私有配置，再请求创建唯一任务。
- 重复 `Install` 更新同名任务而不创建副本；同一时刻只允许一个 bridge 进程。
- `Uninstall` 只移除同名任务，不删除私有配置/inbox。
- 启动命令固定运行 `tools/start-feishu-decision-bridge.ps1`，不在命令行包含秘密。

- [ ] **Step 3: 运行 PowerShell 测试并确认失败**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-setup-feishu-decision-channel.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-install-feishu-decision-bridge.ps1
```

预期：失败原因为三个生产脚本尚未实现。

- [ ] **Step 4: 实现配置和配对脚本**

`setup-feishu-decision-channel.ps1` 的公开参数固定为：

```powershell
[ValidateSet('Configure','Pair','Canary','ShowSanitized')]
[string]$Action,
[string]$ConfigPath,
[string]$StateRoot,
[hashtable]$ConfigValues,
[int]$PairTimeoutSeconds = 300
```

真实 `Configure` 用 `Read-Host -AsSecureString` 读取 App Secret，App ID/recipient 用普通 `Read-Host`；测试才允许 `-ConfigValues`，且测试模式必须要求 `StateRoot` 位于当前 temp 根下。`Pair` 生成一次性 nonce，发送“绑定当前操作人”卡片，等待已签名配对信封，成功后保存 `expectedTenantKey` 的原始值和 `pairedOperatorOpenIdHash`；任何控制台输出都只显示哈希。

- [ ] **Step 5: 实现启动和安装脚本**

`start-feishu-decision-bridge.ps1` 验证私有配置 ACL、执行 `node tools/feishu-decision-bridge/src/bridge.mjs`，把净化日志写到用户级 bridge 目录。`install-feishu-decision-bridge.ps1` 的公开参数为 `Plan|Install|Uninstall|Status`；真实 `Install` 先在 Node 包目录执行 `npm ci --ignore-scripts`，再创建当前用户登录触发、隐藏窗口、单实例的计划任务。

- [ ] **Step 6: 运行脚本测试**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-setup-feishu-decision-channel.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-install-feishu-decision-bridge.ps1
```

预期：两组脚本测试通过，真实用户目录和 Windows 任务计划程序未被修改。

## Task 6: 把决定状态机升级到 schema v7

**Files:**

- Modify: `tools/automation-controller-state.ps1`
- Modify: `tools/test-automation-controller-state.ps1`

- [ ] **Step 1: 先写 v7 失败测试**

在现有状态测试中新增断言：

- 新状态为 schema 7。
- v1—v6 均能升级到 v7。
- v6 `recipientHash` 原样迁移为 `targetHash`；历史尝试 provider 标为 `gmail_legacy`。
- 当前决定 ID、状态、创建时间、截止时间和两条 Gmail 尝试不变。
- `RecordDecisionNotification -NotificationProvider feishu -NotificationStatus CHANNEL_UNAVAILABLE` 不增加 provider 真实发送计数；`PROVIDER_OUTCOME_UNKNOWN` 也不计失败重试、不允许新 attempt/UUID，并转人工核对。
- Feishu 三次实际失败才进入 `RETRY_EXHAUSTED`；Gmail 历史次数不占用 Feishu 限额。
- `ResolveDecision -ReplySource feishu_card` 接受卡片证据哈希；证据缺失、哈希格式错误、错误 option/decision 均失败关闭。
- 新尝试和 resolution 中不存在原始 target、message ID、event ID 或 operator ID。

- [ ] **Step 2: 运行状态测试并确认红灯**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-automation-controller-state.ps1
```

预期：失败于 schema 仍为 6、provider/source 枚举和新证据参数尚未实现。

- [ ] **Step 3: 实现 schema v7 和参数契约**

状态工具新增/调整参数：

```powershell
[ValidateSet('gmail_legacy','feishu')]
[string]$NotificationProvider,
[ValidateSet('PROVIDER_ACCEPTED','PROVIDER_OUTCOME_UNKNOWN','DELIVERY_FAILED','MISADDRESSED','CHANNEL_UNAVAILABLE')]
[string]$NotificationStatus,
[ValidateSet('email','manual','feishu_card')]
[string]$ReplySource,
[ValidatePattern('^[0-9a-f]{64}$')]
[string]$TargetHash,
[ValidatePattern('^[0-9a-f]{64}$')]
[string]$ProviderMessageIdHash,
[ValidatePattern('^[0-9a-f]{64}$')]
[string]$ProviderEventIdHash,
[ValidatePattern('^[0-9a-f]{64}$')]
[string]$OperatorHash,
[ValidatePattern('^[0-9a-f]{64}$')]
[string]$TenantKeyHash,
[ValidatePattern('^[0-9a-f]{64}$')]
[string]$CardNonceHash,
[ValidatePattern('^[0-9a-f]{64}$')]
[string]$EvidenceHash
```

保留 `RecipientHash`、原始 `ProviderMessageId` 和 email evidence 参数只用于 v6 兼容/legacy action。飞书路径只接受已哈希的 `ProviderMessageIdHash`。`CHANNEL_UNAVAILABLE` 不追加到 `notificationAttempts`；`PROVIDER_OUTCOME_UNKNOWN` 可作为项目可见脱敏证据但不计失败重试，且状态机必须禁止为其创建新 attempt/UUID。

- [ ] **Step 4: 运行状态测试**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-automation-controller-state.ps1
```

预期：全套状态迁移、通知、解决、修复和非法 schema 测试通过。

- [ ] **Step 5: 在当前状态副本上做 v6→v7 dry-run**

```powershell
$sourceState = Join-Path $env:USERPROFILE '.codex\automation-state\tzg-hourly-controller.json'
$dryRunRoot = Join-Path $env:TEMP ("tzg-feishu-v7-dry-run-{0}" -f [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $dryRunRoot | Out-Null
$dryRunState = Join-Path $dryRunRoot 'state.json'
Copy-Item -LiteralPath $sourceState -Destination $dryRunState
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/automation-controller-state.ps1 Acquire -StatePath $dryRunState -RunId 'feishu-v7-dry-run' -LeaseMinutes 5
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/automation-controller-state.ps1 Complete -StatePath $dryRunState -RunId 'feishu-v7-dry-run'
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/automation-controller-state.ps1 Show -StatePath $dryRunState
```

预期：副本变为 schema v7 且回到 `IDLE`；当前决定、两条 Gmail 尝试、decision flow 和 audit corrections 与源文件语义一致。核验 `$dryRunRoot` 的完整路径确实位于 `[IO.Path]::GetFullPath($env:TEMP)` 下后再删除该唯一临时目录；真实状态仍为 v6，哈希不变。

## Task 7: 接入控制器的发送与回复消费动作

**Files:**

- Modify: `tools/automation-controller.ps1`
- Modify: `tools/test-automation-controller.ps1`

- [x] **Step 1: 先写控制器失败测试**

在现有隔离临时仓库测试中新增：

- `Contract.actions` 包含 `SendDecisionNotification`、`ConsumeDecisionReply`、`ResolveDecisionManual`，不包含活动 Gmail 发送/搜索动作。
- `CreateDecision.nextCommands` 指向 `SendDecisionNotification`。
- `Start` 遇到当前待决定时，若没有 Feishu 成功尝试可返回发送动作；已有成功尝试时只返回 `ConsumeDecisionReply` 或安全结束。
- 发送动作从状态读取决定 ID，调用假 Node CLI；不接受模型提供的 `DecisionId`、target 或 provider ID。
- bridge 不健康映射到 `CHANNEL_UNAVAILABLE`，不调用 provider transport、不消耗 Feishu retry，并返回 `CompleteNoChange`。
- sender 返回 `PROVIDER_OUTCOME_UNKNOWN` 时不消耗 Feishu 失败 retry、不创建新 attempt/UUID，显示人工核对入口并安全结束。
- 发送成功写入 provider `feishu` 的净化尝试，并把 `decisionId/cardNonceHash/providerMessageIdHash` 绑定镜像写到用户级 bridge 目录。
- consumer 无回复返回 `CompleteNoChange`；签名/身份/冲突失败返回安全错误并保持待决定。
- consumer 有效回复 A 时，状态 resolution source 为 `feishu_card`，返回原始 TaskId 和 `InspectCandidate`。
- legacy 动作仍可被显式回滚测试调用，但不出现在 `Contract` 或任何活动 `nextCommands`。
- 子进程调用使用 `ProcessStartInfo.ArgumentList` 或安全等价方式；秘密不出现在 command line、stdout/stderr 或项目文件。

- [x] **Step 2: 运行控制器测试并确认红灯**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-automation-controller.ps1
```

预期：失败于新动作未进入 ValidateSet/Contract，仍存在 Gmail 活动路径。

- [x] **Step 3: 实现安全 Node 子进程边界**

控制器新增默认参数：

```powershell
[string]$FeishuConfigPath = "$env:USERPROFILE\.codex\automation-state\tzg-hourly-controller.feishu.private.json",
[string]$FeishuBridgeRoot = "$env:USERPROFILE\.codex\automation-state\tzg-feishu-decision-bridge",
[string]$NodeExecutable = 'node'
```

实现内部 `Invoke-FeishuDecisionCli`：请求 JSON 写入当前 run root 下 ACL 受限的临时文件；子进程只收到脚本路径和请求文件路径；结束后在 `finally` 删除请求文件；stdout 只解析单行净化 JSON，stderr 先净化再归类。

- [x] **Step 4: 实现两个模型可见动作**

`SendDecisionNotification`：读取唯一 `pendingDecision`，先检查 bridge health，再调用 sender，最后用状态工具记录 provider、结果和哈希证据。`CHANNEL_UNAVAILABLE` 只留下用户级净化健康诊断，不追加通知尝试；`PROVIDER_OUTCOME_UNKNOWN` 保留净化意图证据、禁止自动新尝试并指向人工核对；发送成功或失败均不要求模型拼装重试参数。

`ConsumeDecisionReply`：读取唯一 `pendingDecision`，调用 consumer；`NO_REPLY` 安全结束；`REPLY_ACCEPTED` 把净化字段逐项传给 `ResolveDecision`，再返回原始 `TaskId` 的 `InspectCandidate`。

保留 `PrepareDecisionNotification`、`MarkDecisionSubmitted`、`RetryDecisionNotification`、`MarkDecisionDeliveryFailed`、`ResolveDecisionEmailReply` 作为 legacy implementation，但把它们从活动契约和路由移除，并在结果中标 `legacyOnly=true`。

- [x] **Step 5: 运行控制器和状态测试**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-automation-controller-state.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-automation-controller.ps1
```

预期：两组测试通过；没有真实飞书调用或用户级真实状态修改。

## Task 8: 更新状态展示、规则、提示词与静态守卫

**Files:**

- Modify: `tools/automation-decision-status.ps1`
- Modify: `tools/test-automation-decision-status.ps1`
- Modify: `tools/check-automation-workflow.ps1`
- Modify: `开发管理/自动工作流规则.txt`
- Modify: `开发管理/自动工作流控制器提示词.txt`

- [x] **Step 1: 先写展示和静态守卫失败测试**

展示测试新增：

- `PROVIDER_ACCEPTED + feishu` 显示“飞书卡片已送达，等待选择”。
- `CHANNEL_UNAVAILABLE` 显示“飞书桥接不可用，未消耗发送重试”。
- `PROVIDER_OUTCOME_UNKNOWN` 显示“飞书发送结果待人工核对，已停止自动补发”。
- resolution source `feishu_card` 显示“飞书互动卡片”。
- Gmail 历史显示“旧 Gmail 通道（仅历史）”。
- 输出不包含目标地址、Open ID 或原始 provider/event ID。

静态守卫新增：

- 活动提示词必须提及 `SendDecisionNotification`、`ConsumeDecisionReply`、`ResolveDecisionManual`。
- 活动提示词不得要求搜索 Gmail/邮箱、读取聊天全文、根据自然语言猜 A/B/C、或拼装 retry 参数。
- 控制器活动 Contract/nextCommands 不得引用五个 legacy email 动作。
- 源码中允许存在明确标记为 legacy 的实现和迁移测试。

- [x] **Step 2: 运行测试并确认红灯**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-automation-decision-status.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-automation-workflow.ps1
```

预期：失败于状态标签和活动提示词仍为 Gmail 语义。

- [x] **Step 3: 更新状态展示和规则事实源**

在规则中明确：

1. 飞书卡片是唯一活动外部决策通道，手动解决是保底。
2. 模型不搜索任何邮件/聊天记录。
3. 发送前本地不可用、provider 明确拒绝和 provider 结果不明分开记账；只有明确拒绝允许新失败尝试。
4. 只有配对操作人的有效卡片回调可自动解决决定。
5. Gmail 仅保留历史和限时回滚代码，不再主动发送/读取。

`automation-decision-status.ps1` 增加可选 `FeishuHealthPath`，只读取 `health.json` 的 status/updatedAt/appIdHash；因此可以显示桥不可用，但不会把 `CHANNEL_UNAVAILABLE` 伪装成一条通知尝试。

- [x] **Step 4: 重写控制器提示词的待决定分支**

提示词只允许按控制器 JSON 的 `nextCommands` 执行：创建后发送一次；每小时消费一次；无回复直接安全结束；有回复返回原任务检查；不得调用 Gmail connector。

- [x] **Step 5: 运行展示、静态守卫和审阅文本检查**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-automation-decision-status.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-automation-workflow.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-review-text.ps1 -Paths AGENTS.md,CLAUDE.md,开发管理
```

预期：三条命令退出码均为 0。

## Task 9: 完成离线集成回归

**Files:**

- Verify: Tasks 2—8 的全部路径

- [x] **Step 1: 安装锁定依赖并运行 Node 测试**

```powershell
Push-Location tools/feishu-decision-bridge
npm ci --ignore-scripts
npm test
Pop-Location
```

预期：安装与全部 Node 测试通过，无网络回调或真实消息发送。

- [x] **Step 2: 运行 PowerShell 直接相关回归**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-setup-feishu-decision-channel.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-install-feishu-decision-bridge.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-automation-controller-state.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-automation-controller.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-automation-decision-status.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-automation-workflow.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-review-text.ps1 -Paths AGENTS.md,CLAUDE.md,开发管理
```

预期：所有命令退出码 0。

- [x] **Step 3: 检查秘密和活动 Gmail 路径**

```powershell
rg -n --hidden --glob '!node_modules/**' --glob '!package-lock.json' '(appSecret|hmacKey|ou_[A-Za-z0-9]+|@(?:qq|163|126|gmail|outlook)\.)' tools/feishu-decision-bridge tools/*.ps1 开发管理
rg -n 'PrepareDecisionNotification|MarkDecisionSubmitted|RetryDecisionNotification|MarkDecisionDeliveryFailed|ResolveDecisionEmailReply' 开发管理/自动工作流控制器提示词.txt 开发管理/自动工作流规则.txt
```

预期：第一条只命中字段名、假测试值和明确的脱敏断言，不出现真实值；第二条没有活动说明命中，或只命中清晰标记的 legacy/回滚说明。

- [x] **Step 4: 检查工作区范围并提交实现**

```powershell
git status --short
$expectedPaths = @(
  'docs/superpowers/specs/2026-07-15-feishu-decision-channel-design.md',
  'docs/superpowers/plans/2026-07-15-feishu-decision-channel-implementation.md',
  'tools/feishu-decision-bridge/package.json',
  'tools/feishu-decision-bridge/package-lock.json',
  'tools/feishu-decision-bridge/src/config.mjs',
  'tools/feishu-decision-bridge/src/card.mjs',
  'tools/feishu-decision-bridge/src/envelope.mjs',
  'tools/feishu-decision-bridge/src/send-core.mjs',
  'tools/feishu-decision-bridge/src/send-runtime.mjs',
  'tools/feishu-decision-bridge/src/send-decision.mjs',
  'tools/feishu-decision-bridge/src/send-pairing.mjs',
  'tools/feishu-decision-bridge/src/send-canary.mjs',
  'tools/feishu-decision-bridge/src/send-intent-store.mjs',
  'tools/feishu-decision-bridge/src/callback-core.mjs',
  'tools/feishu-decision-bridge/src/inbox.mjs',
  'tools/feishu-decision-bridge/src/bridge.mjs',
  'tools/feishu-decision-bridge/src/consume-reply.mjs',
  'tools/feishu-decision-bridge/test/core.test.mjs',
  'tools/feishu-decision-bridge/test/send.test.mjs',
  'tools/feishu-decision-bridge/test/callback.test.mjs',
  'tools/feishu-decision-bridge/test/consume.test.mjs',
  'tools/feishu-decision-bridge/test/pairing.test.mjs',
  'tools/setup-feishu-decision-channel.ps1',
  'tools/install-feishu-decision-bridge.ps1',
  'tools/start-feishu-decision-bridge.ps1',
  'tools/test-setup-feishu-decision-channel.ps1',
  'tools/test-install-feishu-decision-bridge.ps1',
  'tools/automation-controller-state.ps1',
  'tools/test-automation-controller-state.ps1',
  'tools/automation-controller.ps1',
  'tools/test-automation-controller.ps1',
  'tools/automation-decision-status.ps1',
  'tools/test-automation-decision-status.ps1',
  'tools/check-automation-workflow.ps1',
  '开发管理/自动工作流规则.txt',
  '开发管理/自动工作流控制器提示词.txt'
)
& tools/check-pending-whitespace.ps1 -ExpectedPaths ($expectedPaths -join '|')
git add -- $expectedPaths
git diff --cached --check
git diff --cached --stat
git commit -m "feat(automation): add Feishu decision channel"
```

预期：只提交列出的实现、测试和规则路径；不包含私有配置、bridge state、`node_modules` 或不相关用户文件。

## Task 10: 真实配置、配对和单卡灰度

**Files:**

- User-level only: `%USERPROFILE%\.codex\automation-state\tzg-hourly-controller.feishu.private.json`
- User-level only: `%USERPROFILE%\.codex\automation-state\tzg-feishu-decision-bridge\`
- Modify after proof: `开发管理/自动工作流状态.txt`

- [ ] **Step 1: 创建私有配置**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/setup-feishu-decision-channel.ps1 -Action Configure
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/setup-feishu-decision-channel.ps1 -Action ShowSanitized
```

预期：只显示配置 schema、app ID hash、recipient hash、是否已配对；不显示秘密或原始地址。

- [ ] **Step 2: 安装并检查登录桥接任务**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/install-feishu-decision-bridge.ps1 -Action Plan
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/install-feishu-decision-bridge.ps1 -Action Install
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/install-feishu-decision-bridge.ps1 -Action Status
```

预期：唯一任务 `TianZhang-Feishu-Decision-Bridge` 处于 Running/Ready；用户级 `health.json` 在 120 秒内更新为 `CONNECTED`。

- [ ] **Step 3: 绑定当前操作人**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/setup-feishu-decision-channel.ps1 -Action Pair -PairTimeoutSeconds 300
```

在飞书中点击“绑定当前操作人”。预期：脚本成功保存 expected tenant 和 operator hash；同一配对卡再次点击被判为重放，其他账号点击被拒绝。

- [ ] **Step 4: 发送无业务影响的 canary 卡片**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/setup-feishu-decision-channel.ps1 -Action Canary -PairTimeoutSeconds 300
```

点击 canary 的 A。预期：发送一次、回调一次、消费一次；收件箱清空到 `processed/`；控制台只显示哈希和 `CANARY_ACCEPTED`。

- [ ] **Step 5: 迁移当前决定并发送一张业务卡**

在控制器仍暂停时，手工调用控制器 `Start`。该动作通过 state `Acquire` 持久化 v6→v7 原地迁移，并返回待决定分支；按返回的 `nextCommands` 调用 `SendDecisionNotification`，然后用 `CompleteNoChange` 正常释放本次租约。不得创建新决定；发送对象必须仍为 `DEC-20260715-75D7BA2AF210`。只读的 state `Show` 仅用于迁移前后核验，不能替代这次受控写入。

预期：

- v7 保留两条 `gmail_legacy` 历史尝试。
- 新增一条 provider=`feishu`、result=`PROVIDER_ACCEPTED` 尝试。
- 飞书中只出现一张该决定卡片。
- 用户级 `send-intents/` 只有一份与本尝试匹配的净化 `ACCEPTED` 意图，不含原始目标、凭证、decision ID、nonce、卡片内容或 provider message ID。
- 控制器下一步为 `ConsumeDecisionReply`。

如果发送前 bridge/SDK/client 不健康，结果必须为 `CHANNEL_UNAVAILABLE`，不建 intent、不发送卡片、不消耗 Feishu retry。修复前置后再运行发送动作。如果结果为 `PROVIDER_OUTCOME_UNKNOWN`，不得新建 attempt/UUID 补发；55 分钟窗内只能重跑同一逻辑尝试，超窗后必须零 transport 并人工核对。

- [ ] **Step 6: 验证业务回复闭环**

在业务卡点击实际选择后，运行 `ConsumeDecisionReply`。预期：决定 resolution source 为 `feishu_card`，option 与点击一致，控制器返回原始 TaskId 和 `InspectCandidate`；重复消费返回 `NO_REPLY`，不会第二次解决。

## Task 11: 发布状态、部署提示词并恢复自动化

**Files:**

- Modify: `开发管理/自动工作流状态.txt`
- Deploy: Codex 自动化 `tzg-hourly-controller`

- [ ] **Step 1: 用现有受控 Finish 路径发布项目状态**

状态文件记录：schema v7 已启用、活动 provider 为飞书、长连接 canary 通过、当前决定的实际状态、Gmail 为 legacy rollback。只写哈希或计数，不写任何私密值。使用现有 decision-only 安全 Finish 路径持有 `开发管理/自动工作流状态.txt`，完成检查和定向提交；不要引入第二个写入器。

- [ ] **Step 2: 部署活动控制器提示词**

通过 `codex_app__automation_update`，把已测试的 `开发管理/自动工作流控制器提示词.txt` 内容部署到 `tzg-hourly-controller`。重新读取自动化配置，确认活动 prompt 含 `SendDecisionNotification`/`ConsumeDecisionReply`，且不含 Gmail 搜索指令。

- [ ] **Step 3: 最终验证后恢复唯一写入器**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/automation-controller-state.ps1 Show
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-automation-controller-state.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-automation-controller.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-automation-decision-status.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-automation-workflow.ps1
git status --short
git log -3 --oneline
```

预期：状态为 schema v7；没有错误的双 pending/双 resolution；直接相关测试全绿；工作区只剩用户原有不相关文件。随后用自动化更新工具恢复 `tzg-hourly-controller`，并确认没有第二个同职责自动化处于启用状态。

- [ ] **Step 4: 观察一个小时周期**

下一个周期只能出现四种安全结果之一：无回复→`CompleteNoChange`；有效卡片回复→原 TaskId/`InspectCandidate`；发送前本地不可用→`CHANNEL_UNAVAILABLE` 且不建 intent/不发送；provider 结果不明→`PROVIDER_OUTCOME_UNKNOWN` 且不创建新 attempt/UUID，等待人工核对。若出现 Gmail connector 调用、重复卡片、错误 decision ID 或未配对身份被接受，立即暂停自动化，保留 inbox/quarantine/send-intents 证据并回滚活动提示词到上一提交。

## Acceptance Checklist

- [ ] 飞书自建应用可以向配置目标发送互动卡片，且仅已配对操作人能解决决定。
- [ ] 长连接回调无需公网域名、端口映射或 SSL 证书。
- [ ] 模型不再搜索 Gmail、飞书或任何自然语言聊天历史。
- [ ] `DEC-20260715-75D7BA2AF210` 原地迁移，Gmail 历史完整保留且不占用 Feishu retry。
- [ ] channel health、provider 明确拒绝、provider outcome unknown、reply validity 分别记录。
- [ ] 跨小时同一逻辑尝试由用户级发送意图失败关闭；超窗或 provider 结果不明时不自动换 attempt/UUID 补发。
- [ ] 所有身份、消息和事件证据只以 SHA-256/HMAC 摘要进入状态或日志。
- [ ] 私有配置 ACL 仅允许当前用户和 SYSTEM，Git 不跟踪任何运行时私密文件。
- [ ] Node、PowerShell、状态展示、静态守卫和 review-text 检查全部通过。
- [ ] 活动自动化只有一个写入器，部署后 prompt 与仓库事实源一致。
- [ ] 一小时观察周期没有重复发送、错误决定绑定或 Gmail 调用。

## Rollback

1. 立即暂停 `tzg-hourly-controller`。
2. 通过自动化更新工具恢复上一版提示词；不要直接编辑 TOML。
3. 运行 `install-feishu-decision-bridge.ps1 -Action Uninstall` 停止登录桥接任务，但保留私有配置、`processed/` 和 `quarantine/` 证据。
4. 若真实 v7 写入尚未产生飞书通知或回复，可在核对备份哈希后恢复 `state-v6-before.json`；一旦已有 v7 飞书尝试或回复，禁止降级，代码层回滚必须继续读取 schema v7。
5. 如必须临时确认当前决定，只使用 `ResolveDecisionManual` 的显式人工覆盖，记录 Codex task/turn 哈希；不得重新启用 Gmail 全邮箱搜索。
6. 修复后重新执行 Task 9 的离线回归、Task 10 的 canary，再恢复自动化。
