# 经验预检触发收窄与冻结样本复跑设计

## 状态与目标

- 状态：2026-08-28，用户已批准“收窄 `EXP-UNITY-001` / `EXP-BS-003` 后重新运行原冻结十样本”的处理方向。
- 单一目标：消除 `Builder` 与 `Simulate` 宽泛字面量导致的两次已证实误报，并在不改冻结样本、不改匹配器合同的前提下重新验证首期试运行是否达到高置信误报率 `<15%`。
- 本设计不解锁或实施 schema 2；只有原试运行卡重新通过后，才由该卡解除 `M-EXP-TASK-SCHEMA2-01` 的前置。

## 已确认根因

当前 `tools/get-experience-risk-preflight.ps1` 对 `path_and_text` 的文本部分执行区分大小写的字面量子串匹配，输入范围固定为标题、必查范围和实施范围。匹配器行为符合已批准规格，问题位于两条索引触发词比经验卡语义更宽：

1. `EXP-UNITY-001` 使用裸 `builder` / `Builder`。冻结卡 `U-URP-MIGRATE-01` 只把 Adventure Builder 当作只读资产闸门，却因该裸词命中场景重建经验。
2. `EXP-BS-003` 使用 `Simulate` / `Simulate2v2`。冻结卡 `N-GROUP-02C` 只修改 `Simulate2v2Detailed` 的 2v2 候选优先级，却因 `Simulate` 子串命中只针对 1v1 `Combat.Simulate` A/B 状态分支的经验。

因此不修改匹配器，也不增加正则、排除词、权重或第二索引。

## 方案比较

### 方案 A：收窄现有索引和经验卡（采用）

把裸通用词替换为精确代码符号或明确动作术语，并同步经验卡的适用／排除边界。现有 `path_and_text` 与 schema 1 均保持不变；用两个已证实反例、一个既有 Unity 正例和一个精确 1v1 正例锁定回归。

优点是改动最小、符合规格中“`textPatterns` 必须是精确代码符号、实体 ID 或明确术语”的约束，失败时也能直接归因于索引语义。

### 方案 B：扩展匹配器的词边界或排除词语义（不采用）

给索引增加 `excludeTextPatterns`、正则或符号边界语法可以表达更多条件，但会修改索引 schema、匹配器验证和全部现有 fixture。当前两个误报不需要这类新能力，且会突破试运行卡禁止新增兼容分支和第二模式的边界。

### 方案 C：把两条经验改为 `explicit_only`（不采用）

可以彻底避免自动误报，但会丢失已经能稳定识别的 Unity 场景保存链和 1v1 双侧机制工作。误报根因是两个裸词，不是两类风险无法确定性匹配，因此没有必要取消自动预检。

## 原子任务与状态流

两条经验具有不同的适用边界判断，不能塞入同一执行卡；而试运行也不能一边改触发规则一边验证自己。规划状态一次性建立以下完整依赖链：

`M-EXP-PREFLIGHT-NARROW-UNITY-01` → `M-EXP-PREFLIGHT-NARROW-BS-01` → `M-EXP-PREFLIGHT-TRIAL-01` → `M-EXP-TASK-SCHEMA2-01`

两个收窄卡必须串行，因为它们都会修改同一风险索引和匹配器测试文件；这个依赖只隔离共享可变路径，不把两条经验的语义判断合并。

### 第一步：`M-EXP-PREFLIGHT-NARROW-UNITY-01`

新建 P1、`codex_execute`、owner 为 Codex 的独立收窄卡并置为 `ready`。其唯一结果是更新 `EXP-UNITY-001` 的索引／经验卡边界，补齐 Unity 聚焦正反回归；完成后归档本卡并把下一张 BattleSim 收窄卡转为 `ready`。

允许路径限定为：

- `开发管理/经验库/风险索引.json`
- `开发管理/经验库/经验卡/EXP-UNITY-001.txt`
- `tools/test-get-experience-risk-preflight.ps1`
- `开发管理/任务列表/管理与自动化任务.txt`
- `开发管理/当前任务队列.txt`
- `开发管理/任务卡/M-EXP-PREFLIGHT-NARROW-UNITY-01.txt`
- `开发管理/任务归档/M-EXP-PREFLIGHT-NARROW-UNITY-01.txt`
- `开发管理/任务卡/M-EXP-PREFLIGHT-NARROW-BS-01.txt`

### 第二步：`M-EXP-PREFLIGHT-NARROW-BS-01`

新建 P1、`codex_execute`、owner 为 Codex 的独立收窄卡，初始阻塞于 Unity 收窄卡。其唯一结果是更新 `EXP-BS-003` 的索引／经验卡边界，补齐 BattleSim 聚焦正反回归；完成后归档本卡并把原试运行卡恢复为 `ready`。

允许路径限定为：

- `开发管理/经验库/风险索引.json`
- `开发管理/经验库/经验卡/EXP-BS-003.txt`
- `tools/test-get-experience-risk-preflight.ps1`
- `开发管理/任务列表/管理与自动化任务.txt`
- `开发管理/当前任务队列.txt`
- `开发管理/任务卡/M-EXP-PREFLIGHT-NARROW-BS-01.txt`
- `开发管理/任务归档/M-EXP-PREFLIGHT-NARROW-BS-01.txt`
- `开发管理/任务卡/M-EXP-PREFLIGHT-TRIAL-01.txt`

规划状态同步把 `M-EXP-PREFLIGHT-TRIAL-01` 的具名前置设为 `M-EXP-PREFLIGHT-NARROW-BS-01`，并修正管理 backlog 中仍写“已进入 ready”的过期概述。两个收窄步骤都不修改匹配器脚本、试运行报告、冻结提交或 schema 2 卡。

### 第三步：复用 `M-EXP-PREFLIGHT-TRIAL-01`

原试运行卡保持其单一职责：从提交 `ccb1127bcd78d6bb192b4527a449acda41cfc782` 重新物化同十张归档卡，以当前索引运行十次只读匹配，更新原报告并重新计算所有指标。

- 全部阈值通过：归档试运行卡；从 `M-EXP-TASK-SCHEMA2-01.blockedBy` 移除本 ID，按原 route / owner 转为 `ready` 并入队。
- 任一阈值失败：试运行卡继续 `blocked`；schema 2 卡不变。报告必须记录新的实际命中和失败原因。

## 两条触发的精确收窄

### `EXP-UNITY-001`

- 从 `textPatterns` 删除裸 `builder` 与 `Builder`。
- 自动触发只保留明确保存／重建动作，或精确调用符号，例如 `EditorSceneManager.SaveScene`、`StartMenuSceneBuilder.Build`、`WorldSceneBuilder.Build`、`SettlementSceneBuilder.Build`、`AdventureSceneBuilder.Build`、`VisualBaselineBuilder`、`场景重建`、`重建场景`。
- `VisualBaselineBuilder` 保留为精确代码符号，用于维持冻结正例 `U-CHAR-2D-TACTICAL-PROTO-01` 的命中；不得重新加入任何只凭大小写变化的裸 `builder` 词。
- 经验卡明确排除：只读检查 builder、只列出 builder 路径、仅做渲染管线／材质迁移且不调用保存链的任务。

### `EXP-BS-003`

- 从 `textPatterns` 删除 `Simulate` 与 `Simulate2v2`。
- 增加精确 1v1 符号 `Combat.Simulate`；保留已经指向双侧状态机制的 `符胆`、`雷劫`、`受击减防`、`物抗率`、`魂抗率`。
- 经验卡把自动适用范围限定为 1v1 `Combat.Simulate` 的 A/B 对称分支及上述具名双侧机制。
- 经验卡明确排除：只修改 `Simulate2v2` / `Simulate2v2Detailed` 的候选、走位、占格或团队 AI，且不触及 1v1 双侧分支的任务。无法由精确术语表达、但确实涉及该风险的任务，继续使用既有 `riskPreflight.explicitRefs`，不扩展匹配器。

## 聚焦回归

两张收窄任务必须分别在现有匹配器测试中加入对应的生产语义回归：

1. `AdventureScene.unity` + 只读 `AdventureSceneBuilder` / `Builder` 文本不得命中 `EXP-UNITY-001`。
2. `AdventureScene.unity` + `VisualBaselineBuilder` 或精确 `.Build` / `SaveScene` 符号必须命中 `EXP-UNITY-001`。
3. `Combat.cs` + `Simulate2v2Detailed` 不得命中 `EXP-BS-003`。
4. `Combat.cs` + 精确 `Combat.Simulate` 必须命中 `EXP-BS-003`。

随后用冻结卡做聚焦核验：

- `U-URP-MIGRATE-01` 不再命中 `EXP-UNITY-001`。
- `U-CHAR-2D-TACTICAL-PROTO-01` 仍命中 `EXP-UNITY-001`。
- `N-GROUP-02C` 仍命中相关的 `EXP-BS-001`，但不再命中 `EXP-BS-003`。

这些聚焦结果只用于证明对应收窄任务完成；完整十样本指标仍由恢复后的原试运行卡重新计算。

## 完成标准与停止条件

两张收窄任务全部完成需同时满足：

- 匹配器脚本及 schema 未变化；两条经验卡与索引语义一致。
- 四个聚焦正反例和现有匹配器测试全部通过。
- 原冻结卡、冻结提交和既有报告结果未被改写以迁就新触发。
- Unity 卡只裁决 `EXP-UNITY-001`，BattleSim 卡只裁决 `EXP-BS-003`；共享文件按依赖串行修改，不发生并发所有权重叠。
- `M-EXP-PREFLIGHT-TRIAL-01` 已准确恢复为 `ready`；管理 backlog 的概述、任务表和队列与三张卡的状态一致。

原试运行卡只有在 active 种子为 8～12、每卡 `must_read<=3`、必读正文 `<=600` Unicode 字符、高置信误报率 `<15%`、平均 token 代理 `<=1000` 且十次运行无未解释失败时才完成。通过前不得解锁 schema 2。

如果任一聚焦正反例必须依赖修改匹配器、增加排除字段／权重／正则、改写冻结样本，或对应收窄卡无法同时排除反例并保留指定正例，立即停止该卡并报告，不叠加补丁。

## 验证命令

每张收窄任务至少运行：

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-get-experience-risk-preflight.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-task-cards.ps1 -OutputJson
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-review-text.ps1 -Paths docs,开发管理
```

原试运行卡继续按其正文执行十样本物化、digest 复核、十次只读匹配和报告指标计算；状态写入前后再运行任务卡与审核文本检查。两张收窄卡及原试运行卡都需按项目规则执行预暂存空白检查及 `git diff --cached --check`。
