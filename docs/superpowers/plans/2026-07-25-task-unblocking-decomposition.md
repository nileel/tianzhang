# 阻塞任务依赖拆分与自动化恢复实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将三个不可调度任务拆成有明确事实源和依赖关系的独立任务，使首批四张叶子任务卡进入 `ready`，并在一致性检查通过后解除小时自动化的逻辑暂停。

**Architecture:** 只修改开发管理层的任务投影，不提前实现内容设计、数据结构、数值规则或 Unity 行为。当前可执行的叶子任务创建完整任务卡；后继任务只在 backlog 中保留详细投影，等依赖完成后再由 QueueMaintenance 创建任务卡。队列、任务卡和 backlog 必须在同一个提交中完成状态切换，避免事实源短暂不一致。

**Tech Stack:** UTF-8 文本任务卡、PowerShell 7 管理脚本、Git、Codex 自动化管理 API

---

## 执行约束

- 本计划实施的是任务编排，不是四张首批任务卡里的业务工作。
- 保留 `N-SLOT-01` 的原完成定义和现有任务卡，不通过缩小目标使其进入 `ready`。
- `U-2_5D-01D` 进入 `ready` 只依据 2026-07-25 用户给予的独立重新实施授权。
- `C-FPD-MANSION-01` 与 `C-FPD-CULTIVATE-01` 的解冻范围仅限现有机制设计切片，不扩展到其他内容、M7 或 M8。
- `N-GROUP-02C` 继续保留原实现范围，只新增对 `N-GROUP-02C0` 的依赖。
- 首批队列固定为：
  1. `C-FPD-MANSION-01`
  2. `C-FPD-CULTIVATE-01`
  3. `U-2_5D-01D`
  4. `N-GROUP-02C0`
- 不修改 BattleSim、Unity、CSV 或 `docs/` 下的玩法事实源，因此本轮不运行 BattleSim 和数据链路检查。
- 自动化运行时状态只能在任务事实提交成功后清除；清除前必须再次确认 `lease=null` 且 `recovery=null`。

## Task 1：原子化建立完整依赖投影与首批任务卡

**Files:**

- Create: `开发管理/任务卡/C-FPD-MANSION-01.txt`
- Create: `开发管理/任务卡/C-FPD-CULTIVATE-01.txt`
- Create: `开发管理/任务卡/N-GROUP-02C0.txt`
- Modify: `开发管理/任务卡/N-GROUP-02C.txt`
- Modify: `开发管理/任务卡/U-2_5D-01D.txt`
- Modify: `开发管理/任务列表/内容设计任务.txt`
- Modify: `开发管理/任务列表/数据链路任务.txt`
- Modify: `开发管理/任务列表/数值与战斗任务.txt`
- Modify: `开发管理/任务列表/场景与Unity任务.txt`
- Modify: `开发管理/当前任务队列.txt`
- Preserve unchanged: `开发管理/任务卡/N-SLOT-01.txt`
- Reference: `docs/superpowers/specs/2026-07-25-task-unblocking-decomposition-design.md`
- Reference: `开发管理/状态与建议维护规则.txt`

- [ ] **Step 1：确认写入前状态与失败基线**

Run:

```powershell
git status --short
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/hourly-automation-lease.ps1 -Action Show
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-task-cards.ps1 -OutputJson
```

Expected:

- 工作区没有本计划以外的未提交改动；如有用户改动，停止并隔离本轮路径。
- `lease=null`、`recovery=null`。
- `check-task-cards` 返回 `cardCount=3`、`readyCount=0`，证明当前没有可调度任务。

- [ ] **Step 2：创建 `C-FPD-MANSION-01` 完整任务卡**

Create `开发管理/任务卡/C-FPD-MANSION-01.txt`，元数据必须为：

```yaml
schemaVersion: 1
id: C-FPD-MANSION-01
title: 五府府体机制
priority: P1
route: codex_execute
owner: codex
domain: content
stage: design
dispatchState: ready
blockedBy: []
stateReason: 2026-07-25 负责人选择完整依赖方案并解冻本机制设计切片。
sourceBacklog: 开发管理/任务列表/内容设计任务.txt
```

`expectedPaths` 至少覆盖：

```text
docs/superpowers/specs/2026-07-21-five-mansion-body-effects-design.md
docs/superpowers/specs/2026-07-20-foundation-purple-mansion-danxiang-integration-design.md
docs/基础设定/境界特性.txt
开发管理/任务列表/内容设计任务.txt
开发管理/当前任务队列.txt
开发管理/任务卡/C-FPD-MANSION-01.txt
开发管理/任务归档/C-FPD-MANSION-01.txt
```

任务卡正文使用现有标准七节：

1. 来源与当前边界
2. 必读与必查范围
3. 实施范围
4. 禁止项
5. 验证
6. 完成条件
7. 停止条件

正文必须明确：

- 只锁定五府、府体和府体反馈机制。
- 以已批准的五府设计与筑基—紫府—丹相集成设计为直接事实。
- 不实现数据 schema、不做数值定标、不修改 Unity。
- 若五府直接事实互相冲突或必须扩大到其他内容切片，进入 `pending_decision`，不得自行补设定。

- [ ] **Step 3：创建 `C-FPD-CULTIVATE-01` 完整任务卡**

Create `开发管理/任务卡/C-FPD-CULTIVATE-01.txt`，元数据必须为：

```yaml
schemaVersion: 1
id: C-FPD-CULTIVATE-01
title: 养基、筑府与闭关机制
priority: P1
route: codex_execute
owner: codex
domain: content
stage: design
dispatchState: ready
blockedBy: []
stateReason: 2026-07-25 负责人选择完整依赖方案并解冻本机制设计切片。
sourceBacklog: 开发管理/任务列表/内容设计任务.txt
```

`expectedPaths` 至少覆盖：

```text
docs/superpowers/specs/2026-07-21-foundation-mansion-cultivation-actions-design.md
docs/superpowers/specs/2026-07-20-foundation-purple-mansion-danxiang-integration-design.md
docs/基础设定/修行境界.txt
docs/基础设定/寿元与时间尺度设定.txt
开发管理/任务列表/内容设计任务.txt
开发管理/当前任务队列.txt
开发管理/任务卡/C-FPD-CULTIVATE-01.txt
开发管理/任务归档/C-FPD-CULTIVATE-01.txt
```

正文必须明确：

- 只锁定养基、筑府、闭关的动作、阶段、输入输出和失败语义。
- 不设计五府具体效果，不定义数据 schema，不做数值定标。
- 若现有境界或寿元事实无法支持动作闭环，进入 `pending_decision`，不得以实现便利改写设定。

- [ ] **Step 4：创建 `N-GROUP-02C0` 完整任务卡**

Create `开发管理/任务卡/N-GROUP-02C0.txt`，元数据必须为：

```yaml
schemaVersion: 1
id: N-GROUP-02C0
title: 每行动结算候选契约
priority: P3
route: codex_execute
owner: codex
domain: battlesim
stage: design
dispatchState: ready
blockedBy: []
stateReason: 2026-07-25 负责人选择完整依赖方案；本卡作为 N-GROUP-02C 的独立前置契约。
sourceBacklog: 开发管理/任务列表/数值与战斗任务.txt
```

`expectedPaths` 至少覆盖：

```text
docs/superpowers/specs/2026-07-25-group-action-settlement-candidate-contract.md
开发管理/2v2范围技能协同走位与团队AI参数事实源及决策输入.txt
simulations/BattleSim/Combat.cs
simulations/BattleSim/GameData.cs
开发管理/任务列表/数值与战斗任务.txt
开发管理/当前任务队列.txt
开发管理/任务卡/N-GROUP-02C0.txt
开发管理/任务归档/N-GROUP-02C0.txt
```

正文必须锁定以下契约，不得预先实现：

- 行动生产者拥有结算候选。
- 候选提供主稳定 ID、合法命中目标集合、已经结算的击杀目标集合、稳定输入顺序和 `resolved | unavailable` 证据状态。
- `resolved` 且击杀集合为空表示已确认零击杀。
- `unavailable` 不等于已确认零击杀；排序时其击杀证据计数按 0 处理并携带 `settlement_evidence_unavailable`，但不得声称该行动不可能击杀。
- 不预测随机伤害，不提前消耗 RNG，不伪造 `resolved` 空集合。
- 输出必须足以让 `N-GROUP-02C` 在不猜测战斗结果的前提下实现确定性排序。

- [ ] **Step 5：更新现有两张任务卡状态**

Modify `开发管理/任务卡/U-2_5D-01D.txt`：

- `dispatchState: ready`
- `blockedBy: []`
- `stateReason: 2026-07-25 负责人选择完整依赖方案并明确授予本卡独立重新实施授权。`
- 删除或改写“自动化不能选择是否重新授权”的过期阻塞说明。
- 不改变原实施范围、完成条件、验证范围和停止条件。

Modify `开发管理/任务卡/N-GROUP-02C.txt`：

- `dispatchState: blocked`
- `blockedBy: [N-GROUP-02C0]`
- `stateReason: 等待 N-GROUP-02C0 锁定每行动结算候选所有者、证据状态与缺失语义。`
- 正文“当前阻塞”同步为相同语义。
- 不改变原实现范围、完成条件和验证范围。

Do not modify `开发管理/任务卡/N-SLOT-01.txt`。

- [ ] **Step 6：更新内容与 Unity backlog 投影**

Modify `开发管理/任务列表/内容设计任务.txt`：

```text
| C-FPD-MANSION-01 | P1 | codex | 已排队 | — | 五府府体机制 | 开发管理/任务卡/C-FPD-MANSION-01.txt |
| C-FPD-CULTIVATE-01 | P1 | codex | 已排队 | — | 养基、筑府与闭关机制 | 开发管理/任务卡/C-FPD-CULTIVATE-01.txt |
```

- 保留并校准两个任务已有详细段，使范围、禁止项、完成条件与新任务卡一致。
- 不解冻同表中的其他内容任务。

Modify `开发管理/任务列表/场景与Unity任务.txt`：

```text
| U-2_5D-01D | P2 | codex | 已排队 | — | 共享空间查询契约实现与旧范围逻辑迁移 | 开发管理/任务卡/U-2_5D-01D.txt |
```

- 只同步授权和调度状态，不扩大 Unity 实施范围。

- [ ] **Step 7：建立数据链路的后继任务投影**

Modify `开发管理/任务列表/数据链路任务.txt`，保留父项并建立以下后继关系：

```text
D-FPD-SCHEMA-01
├─ D-FPD-SCHEMA-01A  blocked by C-FPD-MANSION-01, C-FPD-CULTIVATE-01
└─ D-FPD-SCHEMA-01B  blocked by D-FPD-SCHEMA-01A

D-JD-SCHEMA-01
├─ D-JD-SCHEMA-01A   blocked by D-FPD-SCHEMA-01B
├─ D-JD-SCHEMA-01B   blocked by D-JD-SCHEMA-01A
└─ D-JD-SAMPLE-01    blocked by D-JD-SCHEMA-01B
```

为五个子任务写可独立建卡的详细段，至少固定：

- 单一目标和唯一直接前置。
- 预计修改路径。
- 输入事实源。
- 明确禁止提前承担的后继职责。
- 验证命令或可观察产物。
- 完成条件和停止条件。

不得为这些仍阻塞的子任务创建任务卡；待它们成为叶子任务后由 QueueMaintenance 建卡。

- [ ] **Step 8：建立数值链路的后继任务投影**

Modify `开发管理/任务列表/数值与战斗任务.txt`：

```text
| N-GROUP-02C0 | P3 | codex | 已排队 | — | 每行动结算候选契约 | 开发管理/任务卡/N-GROUP-02C0.txt |
| N-GROUP-02C | P3 | codex | 阻塞 | N-GROUP-02C0 | 临时团队 AI 的确定性候选优先级 | 开发管理/任务卡/N-GROUP-02C.txt |
```

建立并详细描述：

```text
N-JD-RULE-01
├─ N-JD-RULE-01A  blocked by D-JD-SAMPLE-01
├─ N-JD-RULE-01B  blocked by N-JD-RULE-01A
└─ N-JD-RULE-01C  blocked by N-JD-RULE-01B
```

保留：

```text
N-FPD-MANSION-01   blocked by C-FPD-MANSION-01
N-FPD-DANXIANG-01  blocked by N-FPD-MANSION-01, N-JD-RULE-01C
N-SLOT-01          blocked by N-FPD-MANSION-01, N-FPD-DANXIANG-01
```

每个 `N-JD-RULE` 子任务详细段必须固定单一数值规则切片、直接输入、BattleSim 验证责任、禁止项、完成条件和停止条件。不得为这些仍阻塞的子任务建卡。

- [ ] **Step 9：重建当前任务队列**

Replace the ready-task rows in `开发管理/当前任务队列.txt` with exactly:

```text
| C-FPD-MANSION-01 | codex_execute | codex | P1 | content | design | 五府府体机制 | 开发管理/任务卡/C-FPD-MANSION-01.txt |
| C-FPD-CULTIVATE-01 | codex_execute | codex | P1 | content | design | 养基、筑府与闭关机制 | 开发管理/任务卡/C-FPD-CULTIVATE-01.txt |
| U-2_5D-01D | codex_execute | codex | P2 | unity | implementation | 共享空间查询契约实现与旧范围逻辑迁移 | 开发管理/任务卡/U-2_5D-01D.txt |
| N-GROUP-02C0 | codex_execute | codex | P3 | battlesim | design | 每行动结算候选契约 | 开发管理/任务卡/N-GROUP-02C0.txt |
```

Expected:

- 队列顺序与用户确认的优先级一致。
- 队列只含 `dispatchState=ready` 的完整任务卡。
- 所有 blocked 后继项只存在于 backlog 投影。

- [ ] **Step 10：运行任务事实一致性检查**

Run:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-task-cards.ps1 -OutputJson
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-review-text.ps1 -Paths AGENTS.md,CLAUDE.md,开发管理
git diff --check
```

Expected:

```json
{"status":"ok","cardCount":6,"readyCount":4}
```

`check-review-text` 和 `git diff --check` 均退出 0。

若检查器报告队列、backlog 与任务卡投影不一致，只修正对应事实源，不增加兼容状态或检查器例外。

- [ ] **Step 11：审核完整差异**

Run:

```powershell
git diff --stat
git diff -- 开发管理/任务卡 开发管理/任务列表 开发管理/当前任务队列.txt
```

Review:

- 只有本 Task 声明的十个路径发生变化。
- `N-SLOT-01` 未变化。
- 首批四项均有完整卡并进入队列。
- 后继任务均未建卡。
- 每条 blocker ID 都存在于完整投影中，无循环依赖。
- `N-GROUP-02C0` 清楚区分 `resolved empty` 与 `unavailable`。

- [ ] **Step 12：运行暂存前检查并提交单一事实切换**

Run:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-pending-whitespace.ps1 -Paths 开发管理/任务卡/C-FPD-MANSION-01.txt,开发管理/任务卡/C-FPD-CULTIVATE-01.txt,开发管理/任务卡/N-GROUP-02C0.txt,开发管理/任务卡/N-GROUP-02C.txt,开发管理/任务卡/U-2_5D-01D.txt,开发管理/任务列表/内容设计任务.txt,开发管理/任务列表/数据链路任务.txt,开发管理/任务列表/数值与战斗任务.txt,开发管理/任务列表/场景与Unity任务.txt,开发管理/当前任务队列.txt
git add -- 开发管理/任务卡/C-FPD-MANSION-01.txt 开发管理/任务卡/C-FPD-CULTIVATE-01.txt 开发管理/任务卡/N-GROUP-02C0.txt 开发管理/任务卡/N-GROUP-02C.txt 开发管理/任务卡/U-2_5D-01D.txt 开发管理/任务列表/内容设计任务.txt 开发管理/任务列表/数据链路任务.txt 开发管理/任务列表/数值与战斗任务.txt 开发管理/任务列表/场景与Unity任务.txt 开发管理/当前任务队列.txt
git diff --cached --check
git diff --cached --stat
git commit -m "chore(queue): split blocked task dependency chain"
```

Expected:

- 空白检查和 staged diff 检查退出 0。
- 提交只包含声明路径。
- 提交成功后 `git status --short` 为空。

## Task 2：解除逻辑暂停并恢复小时自动化

**Runtime state:**

- Inspect: `tools/hourly-automation-lease.ps1 -Action Show`
- Modify: 小时自动化运行时 blocking state
- Preserve: `C:\Users\WINDOWS\.codex\automations\tzg-hourly-controller\automation.toml` 的完整配置

- [ ] **Step 1：验证提交后的可调度后置条件**

Run:

```powershell
git status --short
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-task-cards.ps1 -OutputJson
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/hourly-automation-lease.ps1 -Action Show
```

Expected:

- Git 工作区干净。
- `cardCount=6`、`readyCount=4`。
- `lease=null`、`recovery=null`。
- blocking 仍显示原 `queue:no_runnable_candidate` 和 `pauseRequested=true`；这证明尚未提前清除暂停。

如果出现非空 lease 或 recovery，停止，不清除运行时状态，等待占用方完成或按恢复规则处理。

- [ ] **Step 2：清除已失效的连续阻塞状态**

Run:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/hourly-automation-lease.ps1 -Action ClearBlocking
```

Expected:

```text
BLOCKING_CLEARED
```

禁止直接编辑运行时状态文件。

- [ ] **Step 3：以完整配置保持自动化为 ACTIVE**

通过自动化管理 API 更新 `tzg-hourly-controller`，完整回传现有配置：

- id：沿用现有自动化 ID
- name：沿用现有名称
- kind：`cron`
- rrule：沿用现有每小时第 15 分钟规则
- projectId：沿用当前项目
- executionEnvironment：`local`
- model：`gpt-5.6-terra`
- reasoningEffort：`high`
- prompt：原样保留
- status：`ACTIVE`

不得只传 `status`，不得改写 prompt、调度规则、模型或项目。

- [ ] **Step 4：验证运行时恢复**

Run:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/hourly-automation-lease.ps1 -Action Show
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-task-cards.ps1 -OutputJson
git status --short
```

Expected:

- `lease=null`
- `recovery=null`
- blocking `fingerprint=null`
- blocking `count=0`
- blocking `pauseRequested=false`
- 自动化状态为 `ACTIVE`
- 任务卡仍为 `cardCount=6`、`readyCount=4`
- Git 工作区干净

- [ ] **Step 5：交付结果**

最终说明：

- 中断根因已通过任务依赖拆分消除。
- 新增三张任务卡，加上重新授权的 `U-2_5D-01D`，共有四项 ready。
- 原阻塞任务未被伪装成 ready，其完成定义未被削弱。
- 小时自动化已解除逻辑暂停并保持 ACTIVE。
- 给出任务事实提交哈希和下一次调度将选择的队首任务 `C-FPD-MANSION-01`。
