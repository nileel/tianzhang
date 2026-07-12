# 自动化对话命名修复 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 停止独立定时控制器的标题请求超时，同时让手动纯 `1` / `2` 使用实际标题工具更新当前对话。

**Architecture:** cron 控制器没有可重命名的当前 thread，因此只在 automation memory 记录选中对象的中文简述。交互式纯 `1` / `2` 具有当前 thread，上述入口明确调用 `tools.codex_app__set_thread_title`；静态检查分别拒绝控制器调用标题工具、要求手动规则写明真实工具名。

**Tech Stack:** Codex Desktop automation configuration、Codex `set_thread_title` 能力、PowerShell 静态检查、Git。

---

### Task 1: 先写会失败的静态回归

**Files:**
- Modify: `tools/check-automation-workflow.ps1`

- [ ] **Step 1: 反转控制器标题工具断言**

将现有控制器正向断言替换为以下规则；中文模式必须沿用脚本的 UTF-8 Base64 解码方式，避免 Windows PowerShell 5.1 误读无 BOM UTF-8 源文件。

```powershell
$titleToolPattern = 'set_thread_title'
$memoryPattern = ConvertFrom-Utf8Base64 '5Lit5paH566A6L+w'
Reject-Match $controller $titleToolPattern 'controller still attempts to rename a thread'
Require-Match $controller $memoryPattern 'controller does not record the human-readable task summary in memory'
```

- [ ] **Step 2: 增加手动入口工具断言**

为 `AGENTS.md`、`CLAUDE.md`、`开发管理/审核入口.txt` 与 `开发管理/AI协作规则.txt` 添加路径变量，并要求每份文件包含实际工具名：

```powershell
Require-Match (Join-Path $root 'AGENTS.md') 'tools\.codex_app__set_thread_title' 'AGENTS manual workflow does not call the title tool'
Require-Match (Join-Path $root 'CLAUDE.md') 'tools\.codex_app__set_thread_title' 'CLAUDE manual workflow does not call the title tool'
Require-Match $reviewEntry 'tools\.codex_app__set_thread_title' 'review entry does not call the title tool'
Require-Match $collaboration 'tools\.codex_app__set_thread_title' 'collaboration rules do not call the title tool'
```

- [ ] **Step 3: 确认回归失败**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File tools/check-automation-workflow.ps1 -ExpectControllerActive
```

Expected: 失败信息指出控制器仍尝试调用标题工具，且手动入口尚未明确实际工具名。

### Task 2: 最小化规则和控制器修复

**Files:**
- Modify: `AGENTS.md`
- Modify: `CLAUDE.md`
- Modify: `开发管理/自动工作流规则.txt`
- Modify: `开发管理/审核入口.txt`
- Modify: `开发管理/AI协作规则.txt`
- Modify externally: `%USERPROFILE%/.codex/automations/tzg-wf2-codex-execute-1/automation.toml`（仅通过 Codex automation update 能力）

- [ ] **Step 1: 更新定时控制器项目规则**

将“每轮顺序”中的控制器改名要求替换为：

```text
选定真实工作对象后，把当前任务卡或复审事实源的面向人中文简述写入 automation memory 与最终结果；独立 cron 作业不调用 tools.codex_app__set_thread_title，因为它不附着于可重命名 thread。无候选、锁占用、身份失败或人工脏工作区不记录任务简述。
```

- [ ] **Step 2: 更新手动快捷入口规则**

在四份手动入口文件中，把“调用 Codex 的对话标题更新能力”改为 `调用 tools.codex_app__set_thread_title`；保留已确认的格式、时机及不阻断失败边界：

```text
纯 `1` 成功选定对象后调用 tools.codex_app__set_thread_title，将当前对话命名为 `TZG｜<中文简述>`；纯 `2` 使用 `TZG｜复审：<中文简述>`。无候选不调用；调用失败仅记录当前对话执行说明，不阻断原流程。
```

- [ ] **Step 3: 更新控制器完整提示**

通过 `automation_update` 保留现有控制器 ID、名称、每小时触发、模型、推理强度、项目、执行环境与启用状态，只把“任务标题”段替换为：

```text
任务摘要：确认真实工作对象后、读取实施细则前，将当前任务卡或复审事实源的面向人中文简述记录到 automation memory 与最终结果。独立 cron 作业不调用 tools.codex_app__set_thread_title，因为它不附着于可重命名 thread；无候选、锁占用、身份失败或人工脏工作区不记录任务摘要。
```

### Task 3: 验证与提交

**Files:**
- Modify: `tools/check-automation-workflow.ps1`
- Modify: `AGENTS.md`
- Modify: `CLAUDE.md`
- Modify: `开发管理/自动工作流规则.txt`
- Modify: `开发管理/审核入口.txt`
- Modify: `开发管理/AI协作规则.txt`

- [ ] **Step 1: 确认静态检查转绿**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File tools/check-automation-workflow.ps1 -ExpectControllerActive
powershell -ExecutionPolicy Bypass -File tools/check-review-text.ps1 -Paths AGENTS.md,CLAUDE.md,开发管理
git diff --check
```

Expected: 两个 PowerShell 检查均输出 `OK`，差异检查返回 0。

- [ ] **Step 2: 确认控制器配置持久化**

Run:

```powershell
$controller = "$env:USERPROFILE\.codex\automations\tzg-wf2-codex-execute-1\automation.toml"
$hasTitleTool = Select-String -Quiet -LiteralPath $controller -Pattern 'set_thread_title'
if ($hasTitleTool) { throw 'controller still contains title tool' }
$recordsTaskSummary = Select-String -Quiet -LiteralPath $controller -Pattern 'automation memory'
if (-not $recordsTaskSummary) { throw 'controller lacks task-summary memory requirement' }
```

Expected: 第一项不匹配，第二项匹配。

- [ ] **Step 3: 提交项目内改动**

```powershell
& tools/check-pending-whitespace.ps1 -ExpectedPaths 'AGENTS.md|CLAUDE.md|开发管理/自动工作流规则.txt|开发管理/审核入口.txt|开发管理/AI协作规则.txt|tools/check-automation-workflow.ps1' -Fix
& tools/check-pending-whitespace.ps1 -ExpectedPaths 'AGENTS.md|CLAUDE.md|开发管理/自动工作流规则.txt|开发管理/审核入口.txt|开发管理/AI协作规则.txt|tools/check-automation-workflow.ps1'
git add -- AGENTS.md CLAUDE.md '开发管理/自动工作流规则.txt' '开发管理/审核入口.txt' '开发管理/AI协作规则.txt' tools/check-automation-workflow.ps1
git diff --cached --check
git commit -m "fix: separate cron and manual conversation titles"
```

### Task 4: 真实运行验收

**Files:**
- Modify: none before observation

- [ ] **Step 1: 观察下一次定时控制器**

Expected: 选题后自动化 memory 包含中文任务简述，且不再记录线程标题请求超时。

- [ ] **Step 2: 手动发送纯 `1` 或纯 `2`**

Expected: 选定真实对象后当前交互式对话改为对应的 `TZG｜...` 标题。

## 自检

- 规格中的 cron 无 thread 边界由 Task 2 的规则与控制器提示覆盖，手动入口实际工具名由 Task 2 覆盖，防回归由 Task 1 与 Task 3 覆盖，真实运行验收由 Task 4 覆盖。
- 全部 PowerShell 中文断言遵循脚本既有 Base64 模式，避免 Windows PowerShell 5.1 编码回归。
- 计划中的每项验证都已给出具体命令与预期结果。
