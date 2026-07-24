# External AI Automation Closeout Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the existing hourly controller name DeepSeek correctly, close successful external runs with `RecordResult` and `Release`, and reconcile the current `C-ENV-PROFILE-01` residue without adding a new wrapper.

**Architecture:** Keep the current direct Claude CLI external route. Strengthen the canonical prompt and workflow checker so the controller owns only the external terminal closeout after validating identity, commit shape, and workspace residue; existing Codex routes remain unchanged. Reconcile the already-failed historical run separately, then synchronize the paused production automation through the Codex automation API.

**Tech Stack:** PowerShell 7, Git, Codex cron automation configuration, existing workspace guard/finalizer/lease scripts.

## Global Constraints

- Do not add an external responsibility wrapper, progress database, retry layer, second state machine, or alternate queue.
- Do not modify `tools/invoke-codex-responsibility.ps1`.
- Do not read external business diffs or rerun unchanged Unity EditMode tests as part of controller closeout.
- Keep `tzg-hourly-controller` `PAUSED` until the branch, current residue, runtime, and production prompt all pass their direct checks.
- Keep `C-ENV-PROFILE-01` pending Codex review and keep `U-2_5D-01D` blocked.
- Preserve the main-workspace changes to `.agents/summary_state.json` and `设计总结.txt`.
- Use PowerShell 7 for project scripts.

---

### Task 1: Add failing workflow-contract tests

**Files:**
- Modify: `tools/test-check-automation-workflow.ps1`

**Interfaces:**
- Consumes: `tools/check-automation-workflow.ps1` and its fixture repository.
- Produces: failing cases named `Missing external closeout contract` and `Missing DeepSeek identity contract`.

- [ ] **Step 1: Extend the canonical fixture with the desired contracts**

Add these paths beside the existing fixture paths:

```powershell
$claudePath = Join-Path $repositoryRoot 'CLAUDE.md'
$collaborationPath = Join-Path $repositoryRoot '开发管理/AI协作规则.txt'
```

Replace the fixture prompt’s external route line and append the closeout line:

```text
5. Codex 路由只调用 `tools/invoke-codex-responsibility.ps1`；外部 AI 只调用既有 wrapper。外部身份先读进程 `ANTHROPIC_BASE_URL`，为空时补读 `~/.claude/settings.json`；`http://127.0.0.1:15721` 同源地址统一命名为 `DeepSeek V4 Pro`。
7. 外部 AI 返回 completed 后，只核验 identity、businessCommit、handoffCommit、提交父子关系、Automation 元数据和相对基线新增未提交路径；合法且无残留时调用 `RecordResult -Category success`，成功后调用 `Release`。终态无效且无残留时记录 failed 后释放；存在新增未提交路径时保留现场和租约并转人工阻塞。
```

Add minimal fixture contents:

```powershell
$canonicalClaude = @'
# Claude / DeepSeek

- 进程 `ANTHROPIC_BASE_URL` 为空时补读 `~/.claude/settings.json`。
- `http://127.0.0.1:15721` 同源地址（含 `/claude-desktop`）实际身份与修改方为 `DeepSeek V4 Pro`。
'@

$canonicalCollaboration = @'
# AI协作规则

- 进程 `ANTHROPIC_BASE_URL` 为空时补读 `~/.claude/settings.json`。
- `http://127.0.0.1:15721` 同源地址（含 `/claude-desktop`）实际身份与修改方为 `DeepSeek V4 Pro`。
'@
```

Write both files in the fixture’s initial file map.

- [ ] **Step 2: Add mutation cases that the current checker cannot reject**

After the canonical fixture passes, add:

```powershell
$externalCloseoutLine = '7. 外部 AI 返回 completed 后，只核验 identity、businessCommit、handoffCommit、提交父子关系、Automation 元数据和相对基线新增未提交路径；合法且无残留时调用 `RecordResult -Category success`，成功后调用 `Release`。终态无效且无残留时记录 failed 后释放；存在新增未提交路径时保留现场和租约并转人工阻塞。'
$missingExternalCloseout = $canonicalPrompt.Replace($externalCloseoutLine, '7. 外部 AI 返回 completed 后只报告两个提交 SHA。')
Write-Utf8File -Path $promptPath -Content $missingExternalCloseout
Write-Automation -Root $automationRoot -Id 'tzg-hourly-controller' -Status 'PAUSED' -Prompt $missingExternalCloseout
Assert-Fails -Result (Invoke-Checker -RepositoryRoot $repositoryRoot -AutomationRoot $automationRoot) -Context 'Missing external closeout contract' -Contains 'external closeout contract'
Write-Utf8File -Path $promptPath -Content $canonicalPrompt
Write-Automation -Root $automationRoot -Id 'tzg-hourly-controller' -Status 'PAUSED' -Prompt $canonicalPrompt

Write-Utf8File -Path $claudePath -Content $canonicalClaude.Replace('同源地址', '仅该路径')
Assert-Fails -Result (Invoke-Checker -RepositoryRoot $repositoryRoot -AutomationRoot $automationRoot) -Context 'Missing DeepSeek identity contract' -Contains 'DeepSeek identity contract'
Write-Utf8File -Path $claudePath -Content $canonicalClaude
```

- [ ] **Step 3: Run the test and verify RED**

Run:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-check-automation-workflow.ps1
```

Expected: nonzero exit with `Missing external closeout contract unexpectedly passed` because the production checker does not yet enforce the new behavior.

---

### Task 2: Enforce the minimal identity and external-closeout contract

**Files:**
- Modify: `tools/check-automation-workflow.ps1`
- Modify: `CLAUDE.md`
- Modify: `开发管理/AI协作规则.txt`
- Modify: `开发管理/DeepSeek工作提示词.txt`
- Modify: `开发管理/自动工作流规则.txt`
- Modify: `开发管理/自动工作流控制器提示词.txt`

**Interfaces:**
- Consumes: the failing fixture from Task 1.
- Produces: canonical text contracts that the live controller prompt must match.

- [ ] **Step 1: Make the checker read and enforce the identity sources**

After reading `$leaseTool`, add:

```powershell
$claudeRules = Read-Utf8Contract -Path (Join-Path $root 'CLAUDE.md')
$collaborationRules = Read-Utf8Contract -Path (Join-Path $root '开发管理\AI协作规则.txt')
```

After the invocation-timeout assertions, add:

```powershell
Assert-Contains -Text $prompt -Context 'external closeout contract' -Values @(
  'ANTHROPIC_BASE_URL',
  '~/.claude/settings.json',
  'http://127.0.0.1:15721',
  'DeepSeek V4 Pro',
  'identity',
  'businessCommit',
  'handoffCommit',
  'RecordResult -Category success',
  'Release',
  '相对基线新增未提交路径',
  '保留现场和租约'
)
$identityTokens = @(
  '~/.claude/settings.json',
  'http://127.0.0.1:15721',
  '同源地址',
  'DeepSeek V4 Pro'
)
Assert-Contains -Text $claudeRules -Context 'DeepSeek identity contract in CLAUDE.md' -Values $identityTokens
Assert-Contains -Text $collaborationRules -Context 'DeepSeek identity contract in collaboration rules' -Values $identityTokens
```

- [ ] **Step 2: Update the production identity wording**

Use this exact rule in `CLAUDE.md` and the Claude CLI section of `开发管理/AI协作规则.txt`:

```text
- 先读取当前进程的 `ANTHROPIC_BASE_URL`；为空时只补读 `~/.claude/settings.json` 的同名字段。`http://127.0.0.1:15721` 同源地址（含 `/claude-desktop` 路径）的实际身份与修改方统一为 `DeepSeek V4 Pro`，不得自称 Codex 或 Claude。
```

Keep the existing “other Claude CLI environments are Claude Code” rule.

In `开发管理/DeepSeek工作提示词.txt`, replace the external terminal paragraph with:

```text
外层 Codex 只接收严格终态：`completed` 必须同时返回 `identity=DeepSeek V4 Pro`、真实 `businessCommit`、真实 `handoffCommit` 和 session ID；`needs_decision`、`blocked` 或 `failed` 只返回该状态及真实可用的恢复 session。不得在终态 JSON 前后输出摘要、Markdown 代码块或其他正文。外层不读取业务 diff、不重验、不 stage、不 commit。外部责任方不得自审、扩大授权路径、stash、reset、checkout、clean、另行派发并行代理或推送远端。
```

- [ ] **Step 3: Update the workflow rule exception**

In `开发管理/自动工作流规则.txt`, keep Codex closeout unchanged and add the narrow external exception:

```text
- 调度器只读取状态、选择任务、取得租约、调用责任方入口并报告已核验结果；不实施、不做领域验证、不 stage、不 commit、不复审。Codex 路由的结果记录与租约释放仍由固定调用器统一完成。外部 AI 路线只有在严格终态、双提交形状和相对启动基线无新增未提交路径均核验通过后，才由持有该 Run ID 的控制器依次调用 `RecordResult -Category success` 与 `Release`；责任方不得自行调用二者。
```

Add the matching failure rule:

```text
- 外部终态无效但相对启动基线没有新增未提交路径时，控制器记录 `failed` 后释放租约；存在新增未提交路径时保留现场和租约并转人工阻塞，不伪造 recovery，不启动第二责任方。
```

- [ ] **Step 4: Update the canonical controller prompt**

Keep steps 1–4 and the wait/timeout rules. Replace the external routing and result section with:

```text
5. Codex 执行、复审、队列维护只调用 `tools/invoke-codex-responsibility.ps1`。外部 AI 只调用既有 wrapper；预检身份时先读进程 `ANTHROPIC_BASE_URL`，为空时只补读 `~/.claude/settings.json`，`http://127.0.0.1:15721` 同源地址统一命名为 `DeepSeek V4 Pro`。身份与候选主责不匹配时不启动外部 CLI，记录 failed 并释放当前租约。
6. 固定调用器或外部 wrapper 的 `tools.shell_command` 不得使用 180000 毫秒（三分钟）硬超时；`timeout_ms` 必须设为 3300000 毫秒作为单轮上限，与现有 3600 秒租约对齐并保留 5 分钟边界。调用返回 `Script running with cell ID ...` 时，保留同一 cell ID 并继续调用 `functions.wait`；空输出、yield 或尚未返回都不是终态，不得据此结束本轮、记录结果、释放租约或启动第二责任方。等待同一次调用返回，不重新启动、不启动第二责任方。
7. Codex 路线仍由固定调用器根据 Git 与 runtime 统一收尾。外部 AI 返回 completed 后，控制器只核验 `identity=DeepSeek V4 Pro`、`businessCommit`、`handoffCommit`、提交父子关系、业务提交的当前 Task/`State: pending_review`/Automation 元数据、handoff 无 Automation 元数据，以及相对启动基线没有新增未提交路径；不得读取业务 diff或重跑领域验证。全部成立后依次调用 `RecordResult -Category success` 与 `Release`。
8. 外部终态无效且没有相对基线新增未提交路径时，控制器调用 `RecordResult -Category failed` 后 `Release`；存在新增未提交路径时保留现场和租约，报告人工阻塞并结束，不伪造 recovery，不启动第二责任方。
9. 最终只报告 route、TaskId、category、sessionId、commitSha 或 recovery 状态。相同全阻塞原因达到规则阈值时只报告逻辑暂停，自动化任务不得管理自身配置。
```

- [ ] **Step 5: Run the focused test and verify GREEN**

Run:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-check-automation-workflow.ps1
```

Expected: `test-check-automation-workflow: OK`.

- [ ] **Step 6: Confirm production prompt drift is the only live-config failure**

Run:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-automation-workflow.ps1 -RequireLegacyRetired
```

Expected before API synchronization: nonzero with `controller prompt does not match the canonical prompt`. Any other diagnostic must be fixed before continuing.

- [ ] **Step 7: Commit the control-plane contract**

Run:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-pending-whitespace.ps1 -ExpectedPaths 'tools/test-check-automation-workflow.ps1|tools/check-automation-workflow.ps1|CLAUDE.md|开发管理/AI协作规则.txt|开发管理/DeepSeek工作提示词.txt|开发管理/自动工作流规则.txt|开发管理/自动工作流控制器提示词.txt'
git add -- tools/test-check-automation-workflow.ps1 tools/check-automation-workflow.ps1 CLAUDE.md 开发管理/AI协作规则.txt 开发管理/DeepSeek工作提示词.txt 开发管理/自动工作流规则.txt 开发管理/自动工作流控制器提示词.txt
git diff --cached --check
git commit -m "fix(automation): close external runs explicitly"
```

Expected: one commit containing only the listed control-plane files.

---

### Task 3: Reconcile the current C-ENV residue

**Files:**
- Create: `src/Assets/Data/EnvironmentProfiles.meta`
- Modify: `docs/基础设定/关中野外最小环境档案.txt`
- Modify: `开发管理/AI合作沟通.txt`
- Modify: `开发管理/当前任务队列.txt`

**Interfaces:**
- Consumes: business commit `922a0751f52d81d4432d0e3e8abb3570012a7427` and handoff commit `b955f52df35a1d1d6ed9a6890eeaa3067ba35b04`.
- Produces: a clean, internally consistent pending-review handoff.

- [ ] **Step 1: Run the one-off state assertion and verify RED**

Run:

```powershell
$fail = @()
git ls-files --error-unmatch -- 'src/Assets/Data/EnvironmentProfiles.meta' 2>$null | Out-Null
if ($LASTEXITCODE -ne 0) { $fail += 'folder meta is not tracked' }
if (Select-String -Quiet -LiteralPath 'docs/基础设定/关中野外最小环境档案.txt' -Pattern '修改方：Claude Code') { $fail += 'environment doc misnames the worker' }
if (Select-String -Quiet -LiteralPath '开发管理/AI合作沟通.txt' -Pattern '修改方：Claude Code') { $fail += 'handoff misnames the worker' }
if (Select-String -Quiet -LiteralPath '开发管理/当前任务队列.txt' -Pattern '^- 当前状态：待处理；依赖：D-ENV-SCHEMA-01、N-ENV-01 已完成。') { $fail += 'task body is still pending' }
if ($fail.Count -gt 0) { $fail | ForEach-Object { Write-Error $_ }; exit 1 }
```

Expected: nonzero with all four diagnostics.

- [ ] **Step 2: Add the exact Unity folder metadata**

Create `src/Assets/Data/EnvironmentProfiles.meta` with:

```yaml
fileFormatVersion: 2
guid: 2e997dc57327d5b479a5b13dd11f272d
folderAsset: yes
DefaultImporter:
  externalObjects: {}
  userData:
  assetBundleName:
  assetBundleVariant:
```

- [ ] **Step 3: Correct identity and task-state text**

Replace both `修改方：Claude Code` occurrences in the environment document and handoff with `修改方：DeepSeek V4 Pro`.

In the `C-ENV-PROFILE-01` task card, replace:

```text
- 当前状态：待处理；依赖：D-ENV-SCHEMA-01、N-ENV-01 已完成。
```

with:

```text
- 当前状态：已完成（待复审）；依赖：D-ENV-SCHEMA-01、N-ENV-01 已完成。
```

Keep the rest of the sentence unchanged. In the `U-2_5D-01D` card, replace only the stale dependency sentence with:

```text
- 当前状态：阻塞（C-ENV-PROFILE-01 待复审）；依赖：U-2_5D-01C 已完成，C-ENV-PROFILE-01 已完成（待复审）。自动化不得在复审通过前选取本卡。
```

In the `C-ENV-PROFILE-01` completion condition, replace the stale final sentence with:

```text
本卡只标记为已完成（待复审），不把 U-2_5D-01D 写成待处理或已完成。
```

- [ ] **Step 4: Run the one-off assertion and verify GREEN**

Repeat Step 1.

Expected: exit 0 and no diagnostics.

- [ ] **Step 5: Run the direct content/data checks**

Run:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-data-chain.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-review-text.ps1 -Paths 'docs/基础设定/关中野外最小环境档案.txt,开发管理/AI合作沟通.txt,开发管理/当前任务队列.txt'
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-pending-whitespace.ps1 -ExpectedPaths 'src/Assets/Data/EnvironmentProfiles.meta|docs/基础设定/关中野外最小环境档案.txt|开发管理/AI合作沟通.txt|开发管理/当前任务队列.txt'
git diff --check -- src/Assets/Data/EnvironmentProfiles.meta docs/基础设定/关中野外最小环境档案.txt 开发管理/AI合作沟通.txt 开发管理/当前任务队列.txt
```

Expected: all four commands exit 0; do not rerun Unity EditMode because the CSV, asset, importer, and test inputs have not changed.

- [ ] **Step 6: Commit the residue repair**

Run:

```powershell
git add -- src/Assets/Data/EnvironmentProfiles.meta docs/基础设定/关中野外最小环境档案.txt 开发管理/AI合作沟通.txt 开发管理/当前任务队列.txt
git diff --cached --check
git commit -m "fix(env): reconcile C-ENV external handoff"
```

Expected: one commit containing only the four listed paths.

---

### Task 4: Verify, merge, reconcile runtime, and restore scheduling

**Files:**
- Modify via Codex automation API: `C:\Users\WINDOWS\.codex\automations\tzg-hourly-controller\automation.toml`
- Modify via lease tool: `C:\Users\WINDOWS\.codex\automation-state\tzg-hourly-controller-runtime\runtime.json`
- Merge branch commits into: `D:\天章游戏开发`

**Interfaces:**
- Consumes: Tasks 1–3 and the paused production controller.
- Produces: canonical production prompt, truthful historical last result, `lease=null`, and an active controller.

- [ ] **Step 1: Run the complete worktree verification**

Run:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-check-automation-workflow.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-hourly-automation-lease.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-invoke-codex-responsibility.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-data-chain.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-review-text.ps1 -Paths 'CLAUDE.md,开发管理,docs/基础设定/关中野外最小环境档案.txt'
git status --short
```

Expected: all tests/checks exit 0. Git status contains no unstaged changes and only the committed branch history.

- [ ] **Step 2: Confirm the exact paused historical runtime**

From the main checkout, run:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/hourly-automation-lease.ps1 -Action Show
```

Expected: `leaseStatus=expired`, Run ID `0333984b-40d3-42ba-ad5f-9abeeeee5608`, Task ID `C-ENV-PROFILE-01`, `recovery=null`, and controller configuration `PAUSED`. Stop if any value differs.

- [ ] **Step 3: Record the historical run truthfully and release it**

Run from the main checkout:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/hourly-automation-lease.ps1 -Action RecordResult -RunId '0333984b-40d3-42ba-ad5f-9abeeeee5608' -Category failed -TaskId 'C-ENV-PROFILE-01' -DetailCode 'external_task_residue'
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/hourly-automation-lease.ps1 -Action Release -RunId '0333984b-40d3-42ba-ad5f-9abeeeee5608'
```

Expected: `RECORDED` followed by `RELEASED`.

- [ ] **Step 4: Protect the untracked main-workspace meta and fast-forward**

Verify the untracked file is byte-identical to the branch version, move it to a private backup, and merge:

```powershell
$mainMeta = 'D:\天章游戏开发\src\Assets\Data\EnvironmentProfiles.meta'
$branchMeta = 'D:\天章游戏开发\.worktrees\fix-external-automation-closeout\src\Assets\Data\EnvironmentProfiles.meta'
if ((Get-FileHash -LiteralPath $mainMeta -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath $branchMeta -Algorithm SHA256).Hash) { throw 'EnvironmentProfiles.meta differs from the verified branch copy' }
$backupRoot = 'C:\Users\WINDOWS\.codex\automation-state\tzg-hourly-controller-runtime\manual-backups'
[IO.Directory]::CreateDirectory($backupRoot) | Out-Null
$backupPath = Join-Path $backupRoot 'EnvironmentProfiles.meta.20260724'
Move-Item -LiteralPath $mainMeta -Destination $backupPath
git merge --ff-only codex/fix-external-automation-closeout
```

Expected: fast-forward succeeds; `.agents/summary_state.json` and `设计总结.txt` remain modified and untouched.

- [ ] **Step 5: Synchronize the paused production prompt through the automation API**

Call `automation_update` for `tzg-hourly-controller` with the existing name, hourly schedule, project, model `gpt-5.6-terra`, reasoning effort `high`, local execution environment, the full canonical prompt from `开发管理/自动工作流控制器提示词.txt`, and `status=PAUSED`.

Expected: the automation card confirms the updated paused configuration. Do not edit TOML directly.

- [ ] **Step 6: Verify the real production boundary**

Run from the main checkout:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-automation-workflow.ps1 -RequireLegacyRetired
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/hourly-automation-lease.ps1 -Action Show
git -c core.quotepath=false status --short
```

Expected:

- `check-automation-workflow: OK`.
- `lease=null`, `recovery=null`, `lastResult.category=failed`, `detailCode=external_task_residue`.
- Git status contains only `.agents/summary_state.json` and `设计总结.txt`.

- [ ] **Step 7: Restore the controller**

Call `automation_update` again with the same complete configuration and `status=ACTIVE`.

Run:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-automation-workflow.ps1 -RequireActive -RequireLegacyRetired
```

Expected: `check-automation-workflow: OK`. Do not manually trigger another responsibility; the next scheduled hour handles the pending Codex review.

- [ ] **Step 8: Remove the private duplicate only after verification**

After Step 7 succeeds, verify the merged tracked meta still matches the backup, then remove only the exact backup file:

```powershell
$trackedMeta = 'D:\天章游戏开发\src\Assets\Data\EnvironmentProfiles.meta'
$backupPath = 'C:\Users\WINDOWS\.codex\automation-state\tzg-hourly-controller-runtime\manual-backups\EnvironmentProfiles.meta.20260724'
if ((Get-FileHash -LiteralPath $trackedMeta -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath $backupPath -Algorithm SHA256).Hash) { throw 'Backup differs from the merged tracked meta' }
Remove-Item -LiteralPath $backupPath -Force
```

Expected: only the verified duplicate backup is removed; no project file is deleted.
