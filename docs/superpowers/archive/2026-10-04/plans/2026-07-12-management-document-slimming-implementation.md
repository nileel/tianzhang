# 开发管理文档瘦身 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在保留现有路径和全部历史证据的前提下，将管理文件收敛为当前规则、当前事实和当前任务。

**Architecture:** 现有目录结构保持不变。日常文件只保留稳定规则或当前状态；完成过程、旧快照和已替代调查报告移动到现有 `开发管理/任务归档/`，并以短链接保留可追溯关系。所有跨文件状态以当前队列、未通过审核清单和 AI 交接队列为准。

**Tech Stack:** Markdown/TXT 文档、PowerShell、ripgrep、Git。

## Global Constraints

- 不改动 `docs/` 中的游戏设定、`src/`、`simulations/`、CSV 或 Unity asset。
- 不删除历史文件；归档文件保留日期、主题和迁移来源。
- 日常任务仍使用 `开发管理/当前任务队列.txt`，审核仍使用 `开发管理/审核入口.txt`。
- 仅暂存本计划修改的文件；暂存前运行项目规定的行尾空白检查。

---

### Task 1: 归档过程记录并修复当前队列事实

**Files:**
- Modify: `开发管理/当前任务队列.txt`
- Modify: `开发管理/AI合作沟通.txt`
- Modify: `开发管理/未通过审核清单.txt`
- Modify: `开发管理/任务列表/审核与交接任务.txt`
- Create: `开发管理/任务归档/2026-07-12-管理入口瘦身前状态归档.txt`

**Consumes:** 当前队列中的五个待处理任务，以及 TQ-060、TQ-038 已完成/已复审事实。

**Produces:** 无历史完成日志的近期队列；当前为空的交接与审核清单；无 TQ-038 待复审误报的审核 backlog。

- [ ] **Step 1: 写入迁移前状态归档**

将上述四个文件中被移除的完成日志、旧队列卡和已消费交接记录复制到归档文件。归档首页写明：`本文件保存方案 2 瘦身时从日常入口迁出的过程记录；当前状态以对应原路径文件为准。`

- [ ] **Step 2: 收敛当前队列**

保留 TQ-055、TQ-057、TQ-059、TQ-064、TQ-068 的表头和完整任务卡。删除所有完成日志与 TQ-060 的过期任务卡；顶部仅保留内容冻结、队列用途和归档入口。

- [ ] **Step 3: 清理当前为空的审核与交接入口**

`AI合作沟通.txt` 仅保留用途、短格式和“当前无待复审交接”。`未通过审核清单.txt` 仅保留用途、“当前无未通过或待复核项”和复审路由。`审核与交接任务.txt` 删除所有完成及已消费项，保留无事项的说明和归档规则。

- [ ] **Step 4: 运行状态一致性检查**

Run: `rg -n 'TQ-060.*待处理|TQ-038.*待复审|当前待复审交接为' 开发管理/当前任务队列.txt 开发管理/AI合作沟通.txt 开发管理/未通过审核清单.txt 开发管理/任务列表/审核与交接任务.txt`

Expected: 无匹配；或仅剩历史归档文件中的匹配，不在当前入口中出现。

- [ ] **Step 5: 提交任务 1**

Run: `git add -- '开发管理/当前任务队列.txt' '开发管理/AI合作沟通.txt' '开发管理/未通过审核清单.txt' '开发管理/任务列表/审核与交接任务.txt' '开发管理/任务归档/2026-07-12-管理入口瘦身前状态归档.txt'; git commit -m 'docs: slim current management queues'`

### Task 2: 收敛稳定规则与 AGENTS 入口

**Files:**
- Modify: `AGENTS.md`
- Modify: `开发管理/审核入口.txt`
- Modify: `开发管理/审核规则.txt`
- Modify: `开发管理/AI协作规则.txt`
- Modify: `开发管理/DeepSeek工作提示词.txt`
- Modify: `开发管理/状态与建议维护规则.txt`
- Modify: `开发管理/总结规则.txt`

**Consumes:** 方案设计中的“单一事实源 + 短入口”原则。

**Produces:** 无审核历史标题、无重复完整流程的规则集合；总结流程与当前队列/任务列表分层一致。

- [ ] **Step 1: 定义规则职责边界**

保留 `AGENTS.md` 的项目硬约束和按需读取路由；让 `审核入口.txt` 只保留审核路由和最小流程；让 `审核规则.txt` 保留审核细则；让 `AI协作规则.txt` 保留主责与交接边界；让 `DeepSeek工作提示词.txt` 仅保留 DeepSeek 身份及任务卡路由。

- [ ] **Step 2: 移除历史与重复正文**

从各规则标题和正文移除“已审核”、日期、提交号和版本演进日志。对于重复的 `1`/`2` 过程，保留一处完整规范，其余文件改成明确链接与角色特有例外。

- [ ] **Step 3: 更新总结规则**

将“读取所有开发管理文件”改为：先读当前队列、当前状态、自动工作流状态和相关分线 backlog；只有发现事实缺口时按需读优先级、总结和归档。将更新目标改为当前状态、当前队列、分线 backlog 与必要的历史摘要，禁止把完成过程重新写回日常入口。

- [ ] **Step 4: 验证规则引用与快捷流程**

Run: `rg -n '已审核|复审通过|依据：[0-9a-f]|读取所有开发管理文件' AGENTS.md 开发管理/审核入口.txt 开发管理/审核规则.txt 开发管理/AI协作规则.txt 开发管理/DeepSeek工作提示词.txt 开发管理/状态与建议维护规则.txt 开发管理/总结规则.txt`

Expected: 不存在历史审核元数据；仅业务规则中必要的“未审核/待复审”状态语义可保留。

- [ ] **Step 5: 提交任务 2**

Run: `git add -- AGENTS.md 开发管理/审核入口.txt 开发管理/审核规则.txt 开发管理/AI协作规则.txt 开发管理/DeepSeek工作提示词.txt 开发管理/状态与建议维护规则.txt 开发管理/总结规则.txt; git commit -m 'docs: consolidate management workflow rules'`

### Task 3: 归档过期总览并提取当前状态摘要

**Files:**
- Modify: `开发管理/开发优先级.txt`
- Modify: `开发管理/设计-当前状态.txt`
- Modify: `开发管理/设计总结.txt`
- Modify: `开发管理/开发-下一步建议.txt`
- Modify: `开发管理/设计-下一步建议.txt`
- Move to `开发管理/任务归档/`: `开发管理/角色数值设计与BattleSim差异表.txt`, `开发管理/境界体系数据模拟迁移差异表.txt`, `开发管理/境界体系重构锁口径决策表.txt`, `开发管理/源化界数据模型迁移差异表.txt`, `开发管理/成丹与席位竞争数值验证方案.txt`
- Create: `开发管理/任务归档/2026-07-12-当前状态瘦身前归档.txt`

**Consumes:** 当前 G1 已通过、G2 等待 TQ-055、G3 等待 TQ-057/TQ-059，以及内容冻结的有效事实。

**Produces:** 可在一页内判断当前阶段、风险和下一步入口的状态总览；完整保留旧迁移决策和数值方案。

- [ ] **Step 1: 归档旧状态日志和已替代迁移方案**

将 `开发优先级.txt`、`设计-当前状态.txt`、`设计总结.txt` 中的时间序列完成记录与历史快照收入归档；移动五个已完成迁移/验证方案到任务归档。每个移动文件的首段补充“已归档；现行口径见”的链接。

- [ ] **Step 2: 重写当前摘要**

`开发优先级.txt` 只写三道闸门、内容冻结、P1 解锁条件和任务入口。`设计-当前状态.txt` 只写当前 BattleSim/Unity/数据链路事实、有效数量口径、G1/G2/G3 状态和风险。`设计总结.txt` 缩为面向人工的项目概览，并链接当前状态和项目事实源。

- [ ] **Step 3: 保持建议文件为索引**

两个下一步建议文件移除完成项和旧执行叙述，只保留当前判断、分线文件路径与“满足何种条件才可补入当前队列”。

- [ ] **Step 4: 验证当前事实没有旧状态误导**

Run: `rg -n '历史快照|当前阶段.*完成|闸门已解除|TQ-016|TQ-010' 开发管理/开发优先级.txt 开发管理/设计-当前状态.txt 开发管理/设计总结.txt`

Expected: 当前文件不使用被当前 G1/G2/G3 决策取代的结论；历史提及仅出现在归档文件。

- [ ] **Step 5: 提交任务 3**

Run: `git add -- 开发管理/开发优先级.txt 开发管理/设计-当前状态.txt 开发管理/设计总结.txt 开发管理/开发-下一步建议.txt 开发管理/设计-下一步建议.txt 开发管理/任务归档; git commit -m 'docs: archive superseded management snapshots'`

### Task 4: 收敛分线 backlog、调查报告和验证证据

**Files:**
- Modify: `开发管理/任务列表/场景与Unity任务.txt`
- Modify: `开发管理/任务列表/数据链路任务.txt`
- Modify: `开发管理/任务列表/数值与战斗任务.txt`
- Modify: `开发管理/任务列表/内容设计任务.txt`
- Modify: `开发管理/docs-csv-asset-alignment.txt`
- Move to `开发管理/任务归档/`: `开发管理/CSV高阶内容定位表.txt`, `开发管理/realm_lianshen专项检查.txt`, `开发管理/炼虚与高阶内容影响面清单.txt`, `开发管理/验证结果/2026-07-11-TQ-049-EditMode-47.xml`
- Modify: `开发管理/任务列表/审核与交接任务.txt`

**Consumes:** TQ-057 的当前数据矛盾范围、TQ-064/TQ-068 的当前 Unity 任务、内容冻结和既有归档。

**Produces:** 分线 backlog 只保存未完成/冻结任务及最短事实背景；当前数据风险拥有一个明确入口；方案 3 被登记为未来治理任务。

- [ ] **Step 1: 清理分线 backlog 的完成项**

从四个分线 backlog 移除已完成卡和长完成日志，只保留仍未完成、阻塞或冻结的卡。对当前任务已在 `当前任务队列.txt` 中完整定义的项目，backlog 仅保留短标题、依赖和来源链接。

- [ ] **Step 2: 处理历史调查与对齐报告**

将高阶内容/境界键三份扫描报告归档；在 TQ-057 的数据链路任务卡中保留它们的归档路径与未解决项。把 `docs-csv-asset-alignment.txt` 标记为旧对齐快照，顶部指向 TQ-057 和 `check-data-chain.ps1`，不得再宣称当前数据链已闭合。

- [ ] **Step 3: 登记方案 3 后续任务**

在 `审核与交接任务.txt` 新增 `M-MGMT-01`：在 G2/G3 闭环、当前队列稳定且无跨文件交接后，设计并实施 `规则/当前/归档/` 目录重组；验收包含全仓路径引用扫描、`1`/`2` 路由验证、自动工作流路径验证和历史归档可访问性。

- [ ] **Step 4: 验证归档引用与当前风险入口**

Run: `rg -n 'CSV高阶内容定位表.txt|realm_lianshen专项检查.txt|炼虚与高阶内容影响面清单.txt|docs-csv-asset-alignment.txt|M-MGMT-01' 开发管理 AGENTS.md`

Expected: 当前引用均指向现存文件或归档路径；`M-MGMT-01` 只存在于 backlog，不进入当前队列。

- [ ] **Step 5: 提交任务 4**

Run: `git add -- 开发管理/任务列表 开发管理/docs-csv-asset-alignment.txt 开发管理/任务归档; git commit -m 'docs: archive completed management investigations'`

### Task 5: 全量文本与路径验证

**Files:**
- Verify: `AGENTS.md`
- Verify: `开发管理/`

**Consumes:** 前四项迁移后的工作树。

**Produces:** 无失效日常路径、无当前状态冲突、无格式问题的管理文档集合。

- [ ] **Step 1: 扫描失效路径**

Run: `$paths = rg -o --no-filename '开发管理/[^`" ]+\.txt' AGENTS.md 开发管理 | Sort-Object -Unique; foreach ($p in $paths) { if (-not (Test-Path $p)) { "MISSING $p" } }`

Expected: 不输出 `MISSING`。

- [ ] **Step 2: 运行项目文本检查**

Run: `powershell -ExecutionPolicy Bypass -File tools/check-review-text.ps1 -Paths AGENTS.md,开发管理`

Expected: 退出码 0。

- [ ] **Step 3: 暂存并检查差异**

Run: `& tools/check-pending-whitespace.ps1 -ExpectedPaths 'AGENTS.md|开发管理'; git add -- AGENTS.md 开发管理; git diff --cached --check`

Expected: 两项检查均通过。

- [ ] **Step 4: 确认提交边界**

Run: `git status --short; git log -4 --oneline`

Expected: 仅本计划涉及的文件已进入前四个任务的提交；全量验证本身不制造空提交。
