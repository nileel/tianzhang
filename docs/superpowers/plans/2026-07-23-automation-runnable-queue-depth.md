# Automation Runnable Queue Depth Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Prevent the hourly automation from settling into a one-card refill/execution loop by requiring queue maintenance to leave at least two legal runnable cards when authoritative sources permit.

**Architecture:** Keep the thin controller, lease, invoker, and queue data structure unchanged. Express the new postcondition in both authoritative maintenance rules, and extend the existing static workflow checker so a future weakening of either rule fails its direct test.

**Tech Stack:** PowerShell 7, repository text contracts, Git.

## Global Constraints

- Queue maintenance must leave at least 2 legal runnable cards when authoritative sources permit.
- One maintenance run may add at most 3 cards.
- Completed, blocked, frozen, decision-waiting, unavailable-executor, and workspace-conflicting cards do not count as runnable.
- When fewer than 2 safe cards can be formed, add every safe card available and record the shortage reason; never invent a task.
- Do not change the controller, lease, CLI session, invoker, queue schema, or automation configuration.

---

### Task 1: Enforce the runnable queue depth contract

**Files:**
- Modify: `开发管理/自动工作流规则.txt`
- Modify: `开发管理/状态与建议维护规则.txt`
- Modify: `tools/check-automation-workflow.ps1`
- Test: `tools/test-check-automation-workflow.ps1`

**Interfaces:**
- Consumes: the existing UTF-8 contract reader and `Assert-Contains` helper in `tools/check-automation-workflow.ps1`.
- Produces: a static contract requiring both rule files to contain the same queue-depth postcondition and shortage behavior.

- [ ] **Step 1: Write the failing contract test**

Add a canonical maintenance fixture and negative mutations to `tools/test-check-automation-workflow.ps1`:

```powershell
$canonicalMaintenanceRules = @'
# 状态与建议维护规则

- 队列维护结束时，权威来源足够时至少包含 2 张合法可执行任务卡；单次最多新增 3 张。
- 权威来源不足时不得制造任务，补入全部安全卡并记录不足原因。
'@
```

Extend `$canonicalRules` with the same two requirements, create `开发管理/状态与建议维护规则.txt` in the fixture, then replace each file's queue-depth line with the old permissive `新增 1–3 个最小任务` wording and assert that the checker fails with `queue depth contract`.

- [ ] **Step 2: Run the test to verify RED**

Run:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-check-automation-workflow.ps1
```

Expected: non-zero exit because the current checker accepts a fixture whose queue-depth requirement was removed.

- [ ] **Step 3: Implement the minimal checker and rule changes**

In `tools/check-automation-workflow.ps1`, read the maintenance rules and enforce identical stable tokens in both files:

```powershell
$maintenanceRules = Read-Utf8Contract -Path (Join-Path $root '开发管理\状态与建议维护规则.txt')
$queueDepthTokens = @(
  '至少包含 2 张合法可执行任务卡',
  '单次最多新增 3 张',
  '不得制造任务',
  '不足原因'
)
Assert-Contains -Text $rules -Context 'queue depth contract in workflow rules' -Values $queueDepthTokens
Assert-Contains -Text $maintenanceRules -Context 'queue depth contract in maintenance rules' -Values $queueDepthTokens
```

Replace the permissive queue-refill sentence in both rule files with a postcondition that:

```text
队列维护结束时，权威来源足够时至少包含 2 张合法可执行任务卡；单次最多新增 3 张。完成、阻塞、冻结、待决定、执行器不可用或与人工改动冲突的卡不计入深度。权威来源不足时不得制造任务，应补入全部能够安全形成的卡并记录不足原因。
```

Preserve the existing “promote complete backlog first” ordering and the rule that maintenance does not execute new business work.

- [ ] **Step 4: Run the direct tests to verify GREEN**

Run:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-check-automation-workflow.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-automation-workflow.ps1
```

Expected: both commands exit 0 and print their `OK` results.

- [ ] **Step 5: Run repository text and whitespace checks**

Run:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-review-text.ps1 -Paths AGENTS.md,CLAUDE.md,开发管理
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-pending-whitespace.ps1 -Paths docs/superpowers/plans/2026-07-23-automation-runnable-queue-depth.md,开发管理/自动工作流规则.txt,开发管理/状态与建议维护规则.txt,tools/check-automation-workflow.ps1,tools/test-check-automation-workflow.ps1
git diff --check
```

Expected: all commands exit 0 with no whitespace errors.

- [ ] **Step 6: Commit the implementation**

```powershell
git add -- docs/superpowers/plans/2026-07-23-automation-runnable-queue-depth.md 开发管理/自动工作流规则.txt 开发管理/状态与建议维护规则.txt tools/check-automation-workflow.ps1 tools/test-check-automation-workflow.ps1
git diff --cached --check
git commit -m "fix(automation): maintain runnable queue depth"
```
