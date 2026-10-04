# Daily Automation Briefing Content Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 让每日自动化简报用经核验的任务内容、产出、验证和后续影响解释自动化推进，同时保留严格的来源边界。

**Architecture:** 只调整用户级每日简报 `automation.toml` 的 prompt，不改变 cron、模型、只读权限或证据规则。新 prompt 为每项确认成果要求简短的“目标—产出—验证—影响”叙事；无交叉证据的提交继续与成果分离。

**Tech Stack:** TOML 配置、Codex Automation Prompt、Markdown 报告。

---

### Task 1: 扩展每日简报提示词

**Files:**
- Modify: `C:\Users\WINDOWS\.codex\automations\tzg-daily-automation-briefing\automation.toml`
- Test: 手动审阅该文件中的 `prompt` 字段

- [x] **Step 1: 检查当前提示词的报告边界**

Run:

```powershell
Get-Content -Raw 'C:\Users\WINDOWS\.codex\automations\tzg-daily-automation-briefing\automation.toml'
```

Expected: `prompt` 已包含上一自然日、只读、交叉证据、300–600 字及来源未确认规则。

- [x] **Step 2: 写入成果内容叙事要求**

在 `prompt` 的输出规则中加入以下内容：

```text
对每项可确认成果，用一至两句说明任务试图解决的问题、实际产出或行为变化、验证证据，以及对后续任务、依赖或风险的影响；队列维护必须说明其改变的优先级、依赖或阻塞。待决策项必须说明争议对象、可选口径、推荐理由及被影响的后续任务。不得根据任务标题猜测细节；无法由已读取事实源证实的内容只说明未知。
```

保留原有“只读”“共同支持”“来源未确认”“300–600 字”文字，不更改 `status`、`rrule`、`model` 或 `target`。

- [x] **Step 3: 复查配置未扩大权限或范围**

Run:

```powershell
$p = Get-Content -Raw 'C:\Users\WINDOWS\.codex\automations\tzg-daily-automation-briefing\automation.toml'
@('不修改文件、不执行任务、不 stage、不提交','未能确认是否由自动化产生','任务试图解决的问题','status = "ACTIVE"','RRULE:FREQ=DAILY;BYHOUR=1;BYMINUTE=0') | ForEach-Object { if ($p.Contains($_)) { "PASS: $_" } else { "FAIL: $_" } }
```

Expected: 五项均输出 `PASS`。

- [x] **Step 4: 保存本次配置更新记录**

在自动化 memory 追加本次修改的时间、保留的证据边界和验证结果；不改项目任务、状态或 Git 工作区。

- [x] **Step 5: 提交策略**

用户级 `C:\Users\WINDOWS\.codex\automations` 不属于项目 Git 工作区；不执行 `git add`、提交或推送。

### Task 2: 验收下一次日报输出

**Files:**
- Test: 下一次 `TZG Daily Automation Briefing` 输出

- [ ] **Step 1: 检查成果是否包含项目语义**

Expected: 每个确认成果均至少解释任务目标、产出或验证之一，且包含其对依赖、风险或后续队列的影响。

- [ ] **Step 2: 检查来源边界仍有效**

Expected: 无交叉证据的提交单列为来源未确认；没有成果时仍直接输出“未发现可确认的自动化推进”。

- [ ] **Step 3: 检查篇幅和只读性**

Expected: 正文为 300–600 字中文 Markdown；报告不执行项目任务、不 stage、不提交。

## 自查

- 覆盖：任务 1 保留全部原有只读、证据和调度约束，并加入任务内容叙事；任务 2 验证新输出行为。
- 无占位符、未定义接口或范围外文件；没有改变控制器、任务队列、项目状态或 Git 历史。
