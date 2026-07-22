# Automation Commit Briefing Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make every hourly-controller business commit carry enforceable result metadata and generate the daily briefing from all verified tagged commits without duplicating external handoffs.

**Architecture:** Reuse `automation-finalize-commit.ps1` as the single commit boundary, adding an opt-in metadata validator before any index mutation. Propagate one schema through the controller, Codex, and external-worker rules; the read-only daily automation uses tagged commits as candidates and checks their committed facts before reporting. No new runtime state, parser service, database, or alternate commit tool is introduced.

**Tech Stack:** PowerShell 7, Git commit bodies, existing Codex automation configuration, existing PowerShell regression and external-worker canary scripts.

---

### Task 1: Add the opt-in commit metadata gate

**Files:**
- Modify: `tools/test-automation-finalize-commit.ps1`
- Modify: `tools/automation-finalize-commit.ps1`

- [ ] **Step 1: Add failing metadata tests**

Extend `Invoke-Helper` with a `RequireAutomationMetadata` switch and five structured metadata parameters. Add a valid Chinese field set:

```powershell
$automationFields = @{
  AutomationTask = 'TASK-AUTO-001'
  AutomationState = 'completed'
  AutomationResult = '完成自动化提交元数据测试'
  AutomationImpact = '验证日报候选可以从提交正文稳定读取'
  AutomationVerify = 'test-automation-finalize-commit 通过'
}
```

Before the valid case, assert that empty fields, invalid state, and multiline fields fail with `RequireAutomationMetadata`, leave `HEAD` unchanged, and leave the complete cached diff unchanged:

```powershell
$invalidAutomationFields = @(
  ($automationFields.Clone() | ForEach-Object { $_.AutomationTask = ''; $_ }),
  ($automationFields.Clone() | ForEach-Object { $_.AutomationState = 'failed'; $_ }),
  ($automationFields.Clone() | ForEach-Object { $_.AutomationResult = "完成自动化提交元数据测试`n额外一行"; $_ }),
  ($automationFields.Clone() | ForEach-Object { $_.AutomationImpact = ''; $_ }),
  ($automationFields.Clone() | ForEach-Object { $_.AutomationVerify = ''; $_ })
)
```

Commit the valid case, read it with `git log -1 --format=%B`, and assert that all six lines, Chinese text, and `State: completed` are preserved. Keep the existing unflagged single-line cases unchanged.

- [ ] **Step 2: Run the focused test and confirm the new cases fail**

Run:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-automation-finalize-commit.ps1
```

Expected: nonzero because `automation-finalize-commit.ps1` does not yet accept or enforce `RequireAutomationMetadata`.

- [ ] **Step 3: Implement the minimum validator**

Add the switch and structured fields to the parameter block:

```powershell
[switch]$RequireAutomationMetadata,
[string]$AutomationTask,
[string]$AutomationState,
[string]$AutomationResult,
[string]$AutomationImpact,
[string]$AutomationVerify
```

Validate that the subject and five supplied fields are non-empty single-line text, restrict state to `completed` or `pending_review`, then construct the commit message in the finalizer:

```powershell
$CommitMessage = @(
  $CommitMessage,
  '',
  'Automation: tzg-hourly-controller',
  "Task: $AutomationTask",
  "State: $AutomationState",
  "Result: $AutomationResult",
  "Impact: $AutomationImpact",
  "Verify: $AutomationVerify"
) -join "`n"
```

Perform this construction immediately after the existing non-empty `CommitMessage` check and before path conversion, `git add`, or any index mutation. The fixed `Automation` line prevents callers from supplying another automation identity and lets external CLIs keep a single-line allowed command.

- [ ] **Step 4: Run the focused regression**

Run:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-automation-finalize-commit.ps1
```

Expected: `test-automation-finalize-commit: OK`.

- [ ] **Step 5: Commit the gate and test**

Run the pending whitespace check, stage only the two files, run `git diff --cached --check`, and commit:

```text
fix(automation): require structured result metadata
```

### Task 2: Propagate one schema through every responsibility route

**Files:**
- Modify: `开发管理/自动工作流规则.txt`
- Modify: `开发管理/AI协作规则.txt`
- Modify: `开发管理/DeepSeek工作提示词.txt`
- Modify: `开发管理/自动工作流控制器提示词.txt`
- Modify: `tools/check-automation-workflow.ps1`
- Modify: `tools/test-check-automation-workflow.ps1`
- Modify: `tools/test-external-ai-self-commit.ps1`

- [ ] **Step 1: Make the workflow contract test require the new boundary**

Add these required literals to both the canonical prompt/rules checks and matching test fixtures:

```text
RequireAutomationMetadata
Automation: tzg-hourly-controller
State: completed
State: pending_review
handoffCommit 不使用 Automation 标记
```

Run `tools/test-check-automation-workflow.ps1` and confirm it fails before the project prompt and rules are updated.

- [ ] **Step 2: Update the project rules and responsibility prompts**

Apply the approved schema consistently:

```text
Automation: tzg-hourly-controller
Task: <稳定任务 ID 或 QUEUE-MAINTENANCE>
State: <completed 或 pending_review>
Result: <实际完成内容>
Impact: <已确认直接影响，或“无已确认的下游影响”>
Verify: <已通过的直接检查摘要>
```

Require controller-started Codex execution, review, and queue-maintenance commits to use `State: completed`; require every external `businessCommit` to use `State: pending_review`. Require all of them to call the existing finalizer with `RequireAutomationMetadata`. Keep the external `handoffCommit` on the existing second-call boundary without the automation marker or metadata switch.

In the canonical controller prompt, require the controller to include the schema and route-specific state in the responsibility session's first stdin message. Do not let the controller stage, commit, inspect business meaning, or amend a responsibility commit.

- [ ] **Step 3: Update the external self-commit canary**

Change the canary business finalizer command to pass a single-line subject plus `AutomationTask`, `AutomationState`, `AutomationResult`, `AutomationImpact`, and `AutomationVerify` with `-RequireAutomationMetadata`. Keep the handoff finalizer command unflagged. After the canary returns, read the business commit body and assert it contains:

```text
Automation: tzg-hourly-controller
Task: TASK-EXT-001
State: pending_review
Result: 完成外部责任方授权修改
Impact: TASK-EXT-001 已进入待复审状态
Verify: check-pending-whitespace 通过
```

Also assert the handoff commit body does not contain `Automation: tzg-hourly-controller`.

- [ ] **Step 4: Run the focused workflow checks**

Run:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-check-automation-workflow.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-automation-workflow.ps1 -RequireActive
```

Expected: both exit zero and the production controller prompt still equals the canonical prompt only after Task 3 updates the automation configuration. If the second command reports that mismatch before Task 3, retain that expected failure as the deployment gate and do not weaken the checker.

- [ ] **Step 5: Run the external-worker canary once**

Run:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-external-ai-self-commit.ps1
```

Expected: the canary completes two isolated commits, the business commit contains the metadata block, the handoff commit references its SHA without the automation marker, and the project worktree remains unchanged outside the planned files.

- [ ] **Step 6: Commit the synchronized rule and test changes**

Run the review-text check for `AGENTS.md,CLAUDE.md,开发管理`, the pending whitespace check for the seven modified files, stage only those files, run `git diff --cached --check`, and commit:

```text
docs(automation): standardize responsibility result metadata
```

### Task 3: Deploy the controller and成果日报 prompts

**Files:**
- External configuration: automation `tzg-hourly-controller`
- External configuration: automation `tzg-daily-automation-briefing`
- No direct edits to automation TOML

- [ ] **Step 1: Capture the cutover and preserve both complete configurations**

Immediately before updating, capture the current Asia/Hong_Kong timestamp with:

```powershell
Get-Date -Format "yyyy-MM-dd'T'HH:mm:sszzz"
```

Read both current automation configurations and preserve their IDs, names, kinds, schedules, models, reasoning efforts, project, execution environment, notification policy, destination, and ACTIVE/PAUSED status. Only prompts may change.

- [ ] **Step 2: Update the hourly controller through automation management**

Use `codex_app__automation_update` to set its prompt to the exact contents of `开发管理/自动工作流控制器提示词.txt`; preserve every other field. Do not edit `automation.toml` directly.

- [ ] **Step 3: Replace the daily prompt with bounded Git-backed reporting**

The prompt must remain read-only and must:

- use the previous Asia/Hong_Kong natural day;
- select every reachable commit containing `Automation: tzg-hourly-controller`;
- parse all six fields and report malformed tagged commits under the exact `统计完整性错误` heading;
- inspect each candidate's `git show` diff plus committed task/archive/handoff facts without rerunning checks;
- group without loss by `Task`, retain every distinct result and SHA, and apply no result-count cap;
- separate completed business output, external pending-review output, queue maintenance, and integrity errors;
- never infer automation provenance from untagged commits, titles, authors, controller tasks, memory, leases, or runtime;
- report a Git/read failure as briefing failure rather than “no output”;
- state the captured cutover when the first reporting interval starts before it, without rewriting history.

Preserve the existing daily schedule, model, reasoning effort, project, execution environment, destination, notification policy, and status.

- [ ] **Step 4: Verify deployed configuration and workflow contract**

Read both automation configurations back. Confirm only prompt/update timestamp changed, the controller prompt exactly matches the canonical file, and the daily prompt contains the schema, no-cap rule, four sections, integrity handling, and cutover. Then run:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-automation-workflow.ps1 -RequireActive
git status --short
```

Expected: workflow check exits zero; Git status is clean; both automations retain their original schedules and ACTIVE/PAUSED states.

### Task 4: Final minimal verification and handoff

**Files:**
- Verify only; no planned modifications

- [ ] **Step 1: Run the consolidated directly relevant checks once**

Run only checks whose inputs changed since their last successful execution:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-automation-finalize-commit.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-check-automation-workflow.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-review-text.ps1 -Paths AGENTS.md,CLAUDE.md,开发管理
git log -2 --pretty=fuller
git status --short
```

Expected: all checks exit zero, the two implementation commits contain only planned files, and the worktree is clean. Do not rerun the external-worker canary if its inputs did not change after its successful Task 2 run.
