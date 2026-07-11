# 自动化对话命名 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 让每小时控制器和手动纯 `1` / `2` 工作流在选中真实工作对象后，以 `TZG｜<中文简述>` 命名当前对话。

**Architecture:** 项目规则定义三类入口的共同命名时机、事实源和失败边界；手动快捷入口在其短规则中声明相同步骤；定时控制器提示执行实际的后台改名调用。静态检查同时验证规则与控制器提示，防止未来配置回归。

**Tech Stack:** Codex Desktop automation configuration、Codex `set_thread_title` 能力、PowerShell 静态检查、Git。

---

### Task 1: 写入统一的命名规则

**Files:**
- Modify: `开发管理/自动工作流规则.txt`
- Modify: `开发管理/审核入口.txt`
- Modify: `开发管理/AI协作规则.txt`
- Modify: `AGENTS.md`
- Modify: `CLAUDE.md`

- [ ] **Step 1: 增加定时控制器规则**

在 `开发管理/自动工作流规则.txt` 的“每轮顺序”中，在选定任务后加入：

```text
选定真实工作对象后，调用 Codex 的对话标题更新能力，把当前对话命名为 `TZG｜<中文简述>`；复审使用 `TZG｜复审：<中文简述>`。中文简述来自当前任务卡或复审事实源的面向人标题，不得只用 TQ/HANDOFF 编号。无候选、锁占用、身份失败或人工脏工作区不得改名；改名失败只记录 automation memory，不影响原任务、验证、提交或失败关闭。
```

- [ ] **Step 2: 增加手动快捷入口规则**

在 `审核入口.txt` 的纯 `1` / `2` 路由、`AI协作规则.txt` 的“快捷工作流：用户发送 `1` 或 `2`”通用步骤，以及 `AGENTS.md` / `CLAUDE.md` 的快捷推进段落中，加入一致要求：纯 `1` 成功选定执行对象后命名为 `TZG｜<中文简述>`；纯 `2` 成功选定复审对象后命名为 `TZG｜复审：<中文简述>`；在读取实施细则前完成；无法选题或改名失败的边界与定时控制器一致。

- [ ] **Step 3: 运行文本规则检查**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File tools/check-review-text.ps1 -Paths AGENTS.md,CLAUDE.md,开发管理
```

Expected: `check-review-text: OK`。

- [ ] **Step 4: 提交规则改动**

```powershell
& tools/check-pending-whitespace.ps1 -ExpectedPaths 'AGENTS.md|CLAUDE.md|开发管理/自动工作流规则.txt|开发管理/审核入口.txt|开发管理/AI协作规则.txt' -Fix
& tools/check-pending-whitespace.ps1 -ExpectedPaths 'AGENTS.md|CLAUDE.md|开发管理/自动工作流规则.txt|开发管理/审核入口.txt|开发管理/AI协作规则.txt'
git add -- AGENTS.md CLAUDE.md '开发管理/自动工作流规则.txt' '开发管理/审核入口.txt' '开发管理/AI协作规则.txt'
git diff --cached --check
git commit -m "docs: name workflow conversations by task"
```

### Task 2: 更新每小时控制器配置

**Files:**
- Modify externally: `%USERPROFILE%/.codex/automations/tzg-wf2-codex-execute-1/automation.toml`（仅通过 Codex automation update 能力）

- [ ] **Step 1: 构造控制器提示的命名步骤**

在现有提示的“动态路由”与“执行与验证”之间增加：

```text
任务标题：完成动态路由并确认真实工作对象后、读取实施细则前，调用 Codex 的 set_thread_title，把当前对话改为 `TZG｜<中文简述>`；复审使用 `TZG｜复审：<中文简述>`。简述必须来自当前事实源中的中文任务标题或复审主题，不得只用 TQ/HANDOFF 编号。无候选、锁占用、身份失败或人工脏工作区不改名。改名失败只记 automation memory，继续原有失败关闭与执行流程。
```

- [ ] **Step 2: 通过自动化管理能力更新现有控制器**

调用 `automation_update` 的 update 模式，保留控制器的 ID、每小时触发、模型、推理强度、项目、执行环境与启用状态；只替换完整提示文本，使其包含上述步骤。

- [ ] **Step 3: 读取配置确认持久化**

Run:

```powershell
Get-Content -Raw "$env:USERPROFILE\.codex\automations\tzg-wf2-codex-execute-1\automation.toml"
```

Expected: 控制器保持 `ACTIVE`，仍为 `TZG Hourly Controller`，且提示包含 `set_thread_title`、`TZG｜<中文简述>`、复审格式和失败不阻断语义。

### Task 3: 扩展静态回归与完成验证

**Files:**
- Modify: `tools/check-automation-workflow.ps1`

- [ ] **Step 1: 增加命名规则断言**

在现有 `$rules` 与 `$controller` 的静态断言后加入：

```powershell
Require-Match $rules '对话标题|标题更新' 'workflow rules do not define conversation titles'
Require-Match $controller 'set_thread_title' 'controller prompt does not rename its conversation'
Require-Match $controller 'TZG｜<中文简述>' 'controller prompt does not use a human-readable title format'
Require-Match $controller '改名失败|标题更新失败' 'controller prompt does not preserve execution when renaming fails'
```

- [ ] **Step 2: 运行静态工作流回归**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File tools/check-automation-workflow.ps1 -ExpectControllerActive
```

Expected: `check-automation-workflow: OK`。

- [ ] **Step 3: 运行完整文本与差异检查**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File tools/check-review-text.ps1 -Paths AGENTS.md,CLAUDE.md,开发管理
git diff --check
```

Expected: 两条命令均成功且无输出错误。

- [ ] **Step 4: 提交检查器改动**

```powershell
& tools/check-pending-whitespace.ps1 -ExpectedPaths 'tools/check-automation-workflow.ps1' -Fix
& tools/check-pending-whitespace.ps1 -ExpectedPaths 'tools/check-automation-workflow.ps1'
git add -- tools/check-automation-workflow.ps1
git diff --cached --check
git commit -m "test: guard automation conversation titles"
```

### Task 4: 运行验收记录

**Files:**
- Modify: none before real runs

- [ ] **Step 1: 等待一次真实每小时控制器选题**

Expected: 该对话在真实任务选定后显示 `TZG｜<中文简述>` 或 `TZG｜复审：<中文简述>`；无任务轮次不改名。

- [ ] **Step 2: 手动发送纯 `1` 或 `2` 验收**

Expected: 手动快捷工作流在选中对象后显示相同格式；任务编号不作为标题主体。

- [ ] **Step 3: 记录验收结果**

只有两类真实运行都能观察到标题变化时，才在 `开发管理/自动工作流状态.txt` 的最近有效结果补充验收事实；不为无任务或失败观察创建空提交。

## 自检

- 规格的每项要求都由 Task 1（统一入口规则）、Task 2（定时控制器实际调用）、Task 3（防回归）或 Task 4（两类真实运行验收）覆盖。
- 已检查计划不存在未完成标记或未指定的测试步骤。
- `set_thread_title`、`TZG｜<中文简述>` 与 `TZG｜复审：<中文简述>` 在所有任务中用词一致。
