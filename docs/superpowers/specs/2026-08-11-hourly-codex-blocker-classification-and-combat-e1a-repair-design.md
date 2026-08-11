# Codex 无 checkpoint blocker 与 Combat 01E1A 修复设计

## 一、问题与证据

2026-08-11 Codex run `187a0928-0811-4e9c-b124-fae78681244c` 在任务 `U-ARCH-REBUILD-01E2` 上返回 `failed/codex_checkpoint_invalid`，随后进入 `attention_required`。后续小时轮次只按 schema 5 规则重复报告该 run，没有领取新任务。

人工取证确认：

- runtime 为 schema 5，Codex run 是唯一活动 run；DeepSeek run 为空，进程持有型集成锁空闲。
- run 的 repository、taskId、taskCardDigest、worktree、candidate branch 与 baseCommit 一致。
- candidate worktree 干净，HEAD 精确等于 base `4dfefe30ac61768f7a068ff873e0d179793834b0`，没有 candidate／canonical 提交，也没有仍在运行的责任方进程。
- 模型实际发现的是任务级业务 blocker：新 Combat 内核缺少生产切换所需的移动、换法和合法行动集合；`U-ARCH-REBUILD-01E2` 的冻结路径还漏列 Gameplay asmdef 与对应程序集验证所有者。
- 模型却返回 `needs_decision`，同时 `candidateCommit` 为空、`changedPaths` 为空、决策选项键不是 `A/B/C`。候选适配器因此在 checkpoint 直接后继校验处返回 `codex_checkpoint_invalid`，没有把原业务 `detailCode` 送入既有 Block 状态转换。

根因分为两层：阶段 5 任务切片仍缺少一个纯内核切换就绪前置；候选适配器没有把“干净 base、无 checkpoint、显式 detailCode”的错误 `needs_decision` 机械归类为规则规定的业务 blocker。修复不改变 schema 5、集成锁、通知责任或恢复模型。

## 二、批准决定

采用新增窄前置任务方案，不重新打开已归档的 `U-ARCH-REBUILD-01E1`，也不把缺失内核能力并入生产切换任务：

```text
U-ARCH-REBUILD-01E1（已完成）
  -> U-ARCH-REBUILD-01E1A（新增：纯 Combat 切换就绪契约）
    -> U-ARCH-REBUILD-01E2（正式调用链原子切换）
      -> U-ARCH-REBUILD-01E3（旧运行时删除与阶段 5 总验收）
        -> U-ARCH-REBUILD-01E（父项关闭）
```

只有 `01E1A` 转为 `codex_execute/codex/ready` 并进入有序队列。`01E2` 明确从 `dispatchState=ready` 改为 `dispatchState=blocked`、设置 `blockedBy=[U-ARCH-REBUILD-01E1A]`、改写 `stateReason` 并移出队列；`01E3` 继续阻塞于 `01E2` 并同步说明新增前置链；父项 `01E` 的 `blockedBy` 与 `stateReason` 同步增加 `01E1A`。任务列表、当前队列和相关任务卡投影必须一次更新一致。`01F` 及更后阶段不提前解锁。

本次修复只建立准确任务和自动化合同，不代替后续自动轮次实施 `01E1A` 的游戏代码。

## 三、01E1A 纯内核边界

`U-ARCH-REBUILD-01E1A` 只修改 `TianZhang.Combat` 的纯运行时与直接确定性测试，不接触 Character、Game、Feature、UI、Scene、Prefab 或旧 `TacticalCombatController` 生产调用链。

制卡与实施前必须读取旧 `TacticalCombatController.ExecuteSwapSpell`、`Character.SwapSpellInCombat`／`MaxCombatSwaps`、`CombatResolver.Move`／`FindPathTowardTarget`、`EnemyAI.SimpleAI.ExecuteTurn`、`ExplorationController` 的敌方行动 CTB 消费逻辑、`SpatialQueryBoard.FindPath`／`FindReachable` 以及 `CombatRuntimeKernelTests` 的 legacy 对照夹具。它们是移动与换法语义的直接事实源，不从设计文字推导新规则。

### 3.1 命令与状态

- `CombatCommand` 增加移动和换法命令，使用稳定 combatant／profile ID 与 `HexCoord`，不接收 `Character`、GameObject 或 MonoBehaviour。
- `CombatantSnapshot` 保留已有 `Position`，只增加战斗内完成合法性判断所需的移动点数、按槽位装备的术法档案 ID、可换入术法档案 ID、换法次数与上限；这些都是生产切换时一次性投影的纯值。
- 移动沿用旧 `CombatResolver` 的可达路径、移动点数和目的格占用语义。旧生产链在 `EnemyAI` 返回移动结果后由 `ExplorationController` 统一消费一次行动；新内核把这项所有权收进 `CombatCommandService`，合法移动成功恰好消费一次 CTB，拒绝不消费。
- 换法沿用旧 `Character.SwapSpellInCombat` 与 `TacticalCombatController.ExecuteSwapSpell` 的可观察语义：槽位与候选必须合法、不能换入当前槽位同一术法、每场最多 2 次、成功后新术法获得固定 60 tick 冷却（现有基础 30×2 规则）并恰好消费一次 CTB。不得改成从临时公式或未验证配置推导的惩罚。
- 移动与换法的 CTB 冷却惩罚均为 0；换法的 60 tick 只写入新术法的可用冷却，不得误当成下一次行动阈值惩罚。
- `CombatActionResult` 明确返回移动或换法的成功结果与稳定拒绝原因；拒绝不得产生部分位置、装备、资源、冷却或 CTB 变化。

### 3.2 执行与合法行动

- `CombatSession`、`CombatCommandService` 与 `CombatActionResolver` 统一验证移动／换法并消费 CTB；成功行动只提交一次，失败行动不消费回合。
- Spatial 继续拥有格位与路径事实。Combat 只通过扩展后的只读 `ICombatSpatialQuery`，以来源格、目的格、移动点数和当前 session 占用值查询规范路径与移动代价；Combat 只在成功提交时更新自己的快照位置，不持有或写入 Unity Grid、Tilemap、`SpatialQueryBoard` 或场景对象。`01E2` 的生产组合层再用现有 `SpatialQueryBoard.FindPath`／`FindReachable` 实现该只读边界。
- 新建聚焦的 `CombatLegalActionService`，从当前 session、攻击档案和只读 Spatial 查询生成可执行命令集合。它不得调用 EnemyAI、Character、Feature 或 UI。
- 合法行动集合至少覆盖基础攻击、术法、神通、防御、等待、移动与换法，并与 `CombatCommandService` 使用同一套验证入口，不复制另一套近似规则。
- `EnemyAI` 迁移仍属于 `01E2`；届时只从合法行动集合选择，不在 AI 内重复范围、资源、冷却、移动或换法规则。

### 3.3 冻结路径

`01E1A` 的任务卡固定冻结以下管理与业务路径，不使用目录级通配：

- `开发管理/任务列表/场景与Unity任务.txt`
- `开发管理/当前任务队列.txt`
- `开发管理/任务卡/U-ARCH-REBUILD-01E1A.txt`
- `开发管理/任务归档/U-ARCH-REBUILD-01E1A.txt`
- `src/Assets/Scripts/Combat/CombatCommand.cs`
- `src/Assets/Scripts/Combat/CombatantSnapshot.cs`
- `src/Assets/Scripts/Combat/CombatActionResult.cs`
- `src/Assets/Scripts/Combat/CombatActionResolver.cs`
- `src/Assets/Scripts/Combat/CombatCommandService.cs`
- `src/Assets/Scripts/Combat/CombatSession.cs`
- `src/Assets/Scripts/Combat/CombatLegalActionService.cs` 及 `.meta`
- `src/Assets/Scripts/Combat/README.md`
- `src/Assets/Tests/EditMode/CombatRuntimeKernelTests.cs`

若实时完整逻辑证明还必须修改未冻结的 Combat 文件或依赖文件（包括 `TianZhang.Spatial`），先停止并修正任务卡；不得在候选内扩大到生产调用方。现有 `SpatialQueryBoard` 已提供 `FindPath`／`FindReachable`，因此默认不修改 Spatial；只有事实证明只读适配无法表达既有语义时才允许停下重划。

### 3.4 直接验证

`CombatRuntimeKernelTests` 必须覆盖：

- 合法与非法移动、目的格／占用／路径拒绝；
- 合法换法、无候选、重复装备、次数用尽与非法档案拒绝；
- 移动点数、规范路径、目的格占用和成功后单次 CTB 消费，与旧 `CombatResolver.Move` 加生产链统一消费行为的固定 legacy 夹具逐项一致；
- 换法槽位替换、每场最多 2 次、固定 60 tick 冷却和成功后单次 CTB 消费，与旧 `Character.SwapSpellInCombat`／`TacticalCombatController.ExecuteSwapSpell` 的固定 legacy 夹具逐项一致；
- 玩家与 AI 可见的合法行动集合一致；
- 所有失败不改变位置、装备、资源、冷却或 CTB；
- 所有成功行动恰好消费一次 CTB；
- 既有 1v1／2v2、伤害、范围、拒绝原因与结果夹具不回归。

`01E1A` 只有在移动、换法和七类合法行动契约全部就绪、上述 legacy 对照与既有夹具通过，并且 `01E2` 的每个生产命令入口都已有无需修改纯内核的目标命令时才算完成。任务卡必须把这项切换就绪条件写入完成条件。

同时运行程序集边界检查。若需要 Character／Feature 引用、生产双运行、旧 adapter、临时公式或 BattleSim 数值变化，立即停止。

## 四、01E2 路径修正

`01E2` 在 `01E1A` 完成后仍只负责生产调用链原子切换。除现有路径外，冻结路径只补入：

- `src/Assets/Scripts/Game/TianZhang.Gameplay.asmdef`
- `src/Assets/Tests/EditMode/AssemblyBoundaryEditorTests.cs`

`tools/check-unity-assembly-boundaries.ps1` 继续作为验证命令运行，但现有规则无需变化，因此不列入 `01E2` 的 expectedPaths。

`01E2` 在 GameplayContracts 新建实现无关的 `ICombatCommandHandler`。入口集合固定为基础攻击、术法、神通、防御、等待、移动与换法七类；参数只使用调用方稳定 ID、槽位／档案 ID 和坐标整数等稳定原语，不暴露 `CombatCommand`、`HexCoord`、Character、GameObject 或场景实现。Gameplay 组合层把这些请求转换为 `01E1A` 已验证的命令；旧 `TacticalCombatController` 中的同名接口不作为新合同来源，也不保留 adapter。

`TianZhang.Gameplay` 显式引用 GameplayContracts。`TianZhang.Gameplay.Contracts.asmdef` 保持只引用 Foundation，不纳入修改路径；Combat 不引用 GameplayContracts，GameplayContracts 也不引用 Game、Feature、Combat、Spatial 或场景实现。

`01E2` 可以按现卡修改 `EnemyAI`、`SpellData`、Combat README 与生产调用方，但不得再修改纯内核文件：`CombatCommand`、`CombatSession`、`CombatActionResolver`、`CombatCommandService`、`CombatActionResult`、`CombatantSnapshot`、`CombatAttackProfile`、`CombatantRegistry`、`CombatTurnScheduler`、`CombatResultBuilder`、`CombatLegalActionService` 及 `Combat/Turns`。若生产切换仍要求补上述内核能力，说明 `01E1A` 未完成，必须停止并返回前置任务，不能在 `01E2` 叠补。

## 五、候选终态确定性分类

保留合法 decision checkpoint 的全部既有合同。候选适配器只增加一个状态归类，不新增 runtime 状态、重试或恢复对象：

```text
terminal.status = needs_decision
├─ HEAD 是 base 的唯一直接后继、worktree 干净、checkpoint/path/ABC 决策合同完整
│  -> needs_decision，进入既有 PauseDecision
├─ HEAD = base、worktree 干净、candidateCommit 为空、changedPaths 为空、detailCode 非空
│  -> blocked，进入既有 Block
└─ 其他证据不一致
   -> failed/attention_required，保留现场
```

第二个分支只落实已有 prompt 与自动工作流规则：“业务 blocker 且没有合法 checkpoint 时返回 blocked/detailCode”。它不信任或发送无 checkpoint 决策卡，忽略该非法终态中的 question/options，并把显式 `detailCode` 作为任务 blocker 证据交给既有状态投影。

适配器测试至少覆盖：

- 干净 base、空 commit/path、非空 detailCode 被归类为 blocked；
- 合法直接后继 checkpoint 仍返回 needs_decision；
- HEAD 改变、worktree 脏、伪造 SHA、非空错误路径或不完整 ABC 决策仍失败；
- 候选过程中曾产生改动但终态已完整恢复为干净 base 时，与始终未改动的同证据终态一样归类为 blocked；若仍残留任何改动则失败；
- 归类后的 blocked 结果不包含 candidateCommit 或 decision candidateResult，继续由共享入口既有 Block 分支消费。

## 六、活动 run、隔离与集成顺序

1. 通过 Codex automation 管理能力暂停 `codex-hourly-worker`，保持 prompt、schedule、folder、model 与通知配置不变；不修改 DeepSeek automation。
2. 项目文件只在 `.worktrees/manual/` 下的独立手动 worktree 修改。主工作区现有 staged、unstaged 和 untracked 文件全部视为用户改动。
3. 重新调用 schema 5 `Show`，核对 runId、taskId、repository、base、taskCardDigest、worktree、branch、HEAD、清洁度、进程、candidate／canonical 字段、recoveryReason 与集成锁。
4. 只有所有证据仍与本设计第一节一致时，按 empty-attention 精确关闭合同调用 `CompleteRun`。关闭后再次核对该 worktree 的注册、路径、branch、HEAD 和清洁度，只删除该 run 的精确 worktree 与 candidate branch。
5. 不 stash、reset、checkout、clean、revert，不泛化删除 `.worktrees/automation/`，不处理其他历史 worktree。
6. 在隔离 worktree 完成任务图、状态、候选适配器和测试修改，形成一个路径限定提交；既有自动工作流规则正文保持不变。
7. 合并前重新 `Show`，确认两个 owner run 均为空、集成锁空闲、任务投影仍一致，且待合并路径不与主工作区人工改动冲突；随后只通过 `tools/invoke-project-integration.ps1` 取得共享排他锁并 fast-forward。

任一关键证据变化都停止，不先修改任务卡来让旧 run 失去 taskCardDigest 一致性。

## 七、验证与恢复

项目修改至少运行：

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-invoke-codex-candidate.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-task-cards.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-review-text.ps1 -Paths AGENTS.md,CLAUDE.md,开发管理
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-pending-whitespace.ps1 -ExpectedPaths 'docs/superpowers/specs/2026-08-11-combat-architecture-stage-5-task-rescoping-design.md|docs/superpowers/specs/2026-08-11-hourly-codex-blocker-classification-and-combat-e1a-repair-design.md|开发管理/自动工作流状态.txt|开发管理/当前任务队列.txt|开发管理/任务列表/场景与Unity任务.txt|开发管理/任务卡/U-ARCH-REBUILD-01E.txt|开发管理/任务卡/U-ARCH-REBUILD-01E1A.txt|开发管理/任务卡/U-ARCH-REBUILD-01E2.txt|开发管理/任务卡/U-ARCH-REBUILD-01E3.txt|tools/invoke-codex-candidate.ps1|tools/test-invoke-codex-candidate.ps1'
git diff --cached --check
```

集成后在最新 `master` 上运行 Codex 专用 canary。Canary 必须核验真实模型、结构化终态、候选元数据、隔离 worktree 与成功清理，且不得改变主工作区 HEAD 之外的人工状态。只有 canary 通过、schema 5 两个 owner 均为空、集成锁空闲时，才通过 automation 管理能力恢复 `codex-hourly-worker`；DeepSeek 配置和状态保持不变。

若测试、集成或 canary 失败，Codex automation 保持暂停并保留准确现场，不自动重试。

## 八、项目修改范围

本次修复允许修改：

- 新设计文档与原阶段 5 设计的当前修订说明；
- `开发管理/自动工作流状态.txt`（只记录旧 run 的精确关闭与本次修复结果；既有自动工作流规则已足够，不修改规则正文）；
- `开发管理/当前任务队列.txt`、`开发管理/任务列表/场景与Unity任务.txt`；
- `U-ARCH-REBUILD-01E`、新 `01E1A`、`01E2`、`01E3` 任务卡；
- `tools/invoke-codex-candidate.ps1`、`tools/test-invoke-codex-candidate.ps1`。

本次不修改 Unity C# 业务实现、asmdef、Scene、Prefab、asset、BattleSim、DeepSeek route、runtime schema、集成锁、通知发送器、automation TOML 或其他 worktree。

## 九、完成与停止条件

完成必须同时满足：

- 旧 `attention_required` run 已按一致证据精确关闭并清理；
- 只有 `01E1A` 为新增 ready 卡，`01E2`／`01E3`／父项与 backlog、队列投影一致；
- `01E2` 已明确为 `dispatchState=blocked`、`blockedBy=[U-ARCH-REBUILD-01E1A]`，相关 `stateReason` 与父项依赖链同步；
- 无 checkpoint blocker 测试证明进入既有 blocked 流程，合法 checkpoint 与其他失败语义未改变；
- 文本、任务卡、空白、差异检查和 Codex canary 全部通过；
- Codex 小时入口按原配置恢复，DeepSeek 未被修改；
- 主工作区人工改动未被覆盖、暂存或提交。

出现以下任一情况立即停止：

- 当前 run 证据与本设计不一致；
- 任务修复要求直接实施 `01E1A` 游戏代码或扩大到未批准系统；
- 无 checkpoint 分类需要新增 runtime 状态、重试、兼容层或后台恢复器；
- 路径与主工作区人工改动冲突；
- canary 无法证明真实模型、隔离或清理；
- 调整会提前解锁 `01F`、改变 BattleSim 数值语义或修改 DeepSeek 配置。
