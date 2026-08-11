# Combat 架构阶段 5 任务重划设计

## 一、背景与问题结论

`U-ARCH-REBUILD-01E` 当前把纯 Combat 内核、CTB 所有权迁移、正式调用链切换、旧 Hub 删除、测试迁移和完整验证放在同一个自动化 candidate 中。任务卡形式上满足 `ready` 合同，但一次候选需要同时处理 63 条预期路径和大量旧 API 直接引用，实际不具备稳定的小时自动执行边界。事后实时扫描还确认原 expectedPaths 漏列直接使用 `TacticalCombatEndOutcome` 的 `AdventureSceneController.cs` 和直接核验 `CTBUnit` 所有权的 `CharacterStateTests.cs` 及其 `.meta`，原卡不能合法覆盖完整迁移。

2026-08-11 的 Codex run `3413d1a5-da10-4965-a49c-7e9e0a4381da` 没有命中任务停止条件、路径冲突、集成锁冲突或责任方超时。候选在约 11 分钟内先改写核心类型，后发现 `TacticalGridModelTests`、`AttackProfileDataTests`、`GuanzhongBasicAttackPlayModeTests` 和正式调用方仍大面积依赖 `TacticalCombatController`、`Character.CTBUnit` 与旧 `CombatResolver`，遂把“尚未完成”错误归类为 `failed/combat_architecture_migration_incomplete`。候选按既有失败合同恢复到 base，留下干净且无提交的 `attention_required` run；后续小时轮次只能重复报告同一现场。

本次根因属于任务切片过大和切换顺序不适合自动调度，不修改 schema 5 runtime、失败停止语义、集成锁或通知机制，也不以自动重试、兼容 adapter、双运行 feature flag 或新恢复状态掩盖问题。

## 二、设计决定

将 `U-ARCH-REBUILD-01E` 从单个可执行卡改为阶段 5 汇总父项，并建立三个严格顺序、各自可编译和可验证的 Codex 子任务：

1. `U-ARCH-REBUILD-01E1`：纯 Combat 内核与确定性测试。
2. `U-ARCH-REBUILD-01E2`：正式调用链原子切换。
3. `U-ARCH-REBUILD-01E3`：旧运行时删除与阶段 5 总验收。

原 `U-ARCH-REBUILD-01E` 转为 `blocked`，`blockedBy` 固定为上述三个子任务；不再进入 ready 队列。`U-ARCH-REBUILD-01F` 继续只依赖父项 `U-ARCH-REBUILD-01E`，因此只有三个子任务全部完成、父项归档后才可解阻塞。父项 `U-ARCH-REBUILD-01` 的既有依赖表达不变。

子任务固定依赖为：

```text
U-ARCH-REBUILD-01E1
  -> U-ARCH-REBUILD-01E2
    -> U-ARCH-REBUILD-01E3
      -> U-ARCH-REBUILD-01E completed
        -> U-ARCH-REBUILD-01F
```

队列调整后只允许 `01E1` 为 `codex_execute/codex/ready`；`01E2`、`01E3` 与父项保持 active blocked，不提前进入队列。

`01E3` 是父项的最终闭环切片。其 expectedPaths 必须包含父项任务卡、父项归档、backlog 与队列投影；`01E3` 验收通过时在同一管理提交中归档 `01E3` 和父项 `01E`，避免父项在最后一个 blocker 消失后成为新的空执行卡。`01F` 仍由后续正常队列维护根据已完成的 `01E` 归档解除前置，本提交不提前把 `01F` 放入 ready 队列。

## 三、子任务边界

### 3.1 `U-ARCH-REBUILD-01E1` · 纯 Combat 内核与确定性测试

目标是在不改变正式场景和现有生产调用链的前提下，建立可独立测试的新战术运行时：

- 固定 `CombatantSnapshot`、攻击档案投影、命令、拒绝原因和结果契约。
- 建立 `CombatSession`、`CombatTurnScheduler`、`CombatCommandService`、`CombatActionResolver`、`CombatantRegistry` 与 `CombatResultBuilder`。
- 新 CTB 与纯 Combat 类型只接受稳定 ID、快照、空间查询和攻击档案投影，不引用 `Character`、Feature、Scene、MonoBehaviour、UI、Renderer、日志或动画。
- 建立无场景的 CTB、伤害、范围、拒绝原因及 1v1／2v2 确定性测试。

临时共存边界必须显式记录：`01E1` 完成后旧运行时仍是唯一生产可达路径；新内核只允许被直接测试访问，不得注册到 Bootstrap、场景、Prefab 或正式 Adventure 调用链。不得增加双运行、结果择优、runtime feature flag、旧控制器 adapter 或生产 fallback。新内核的数值和行动语义必须来自现有已验证规则与固定回归夹具，不引入临时公式。

`01E1` 不删除 `TacticalCombatController`、旧 `Core/CTBEngine.cs` 或 `Character.CTBUnit`，不修改正式场景行为，不迁移 Battle UI。其 exact expectedPaths 在制卡前按实时源码和新增测试重新冻结，包含所有新文件及 `.meta`。

### 3.2 `U-ARCH-REBUILD-01E2` · 正式调用链原子切换

目标是在一个独立提交中把所有生产调用方切到 `01E1` 已验证的新内核：

- `Character` 不再持有 CTB 运行时状态；Adventure 只投影参战快照并消费战斗结果。
- `ExplorationController`、`FormalEncounterResult`、`BattleUIManager` 和直接生产调用方改用新会话、命令与结果契约。
- `EnemyAI` 只消费合法行动集合，不直接读取 Character 或场景实现。
- 按钮、日志与表现只经现有 Feature／GameplayContracts 边界调用，不进入 Combat。
- 与正式调用链绑定的 EditMode／PlayMode 测试同步改用新入口。

切换必须是单向且原子的：不得保留新旧运行时双写、shadow compare、运行时选择开关或旧控制器 adapter。`01E2` 完成后新 Combat 内核是唯一生产可达战术运行时；旧类型可以暂留在仓库供 `01E3` 精确删除，但不得再被生产代码、场景、Prefab 或测试调用。

### 3.3 `U-ARCH-REBUILD-01E3` · 旧运行时删除与阶段 5 总验收

目标是删除已不可达的旧所有权并完成阶段 5 的完整闭环：

- 删除 `TacticalCombatController` 的旧 Hub、旧 `Core/CTBEngine.cs`、遗留 `Character.CTBUnit` 和只服务旧入口的类型、测试及 `.meta`。
- 核对 C#、场景、Prefab、资源、GUID、asmdef 与检查器中不存在旧运行时引用。
- 更新 Combat README、程序集边界和阶段 5 架构证据。
- 完成完整 Unity EditMode、相关 PlayMode、程序集边界、文本、空白与差异检查。
- 验收通过后同步归档 `01E3` 与阶段 5 父项 `01E`；不另建无业务内容的父项执行轮次。

`01E3` 不修改伤害倍率、CTB 数值、范围规则或 BattleSim 数据。若回归证明迁移改变了 BattleSim 数值语义，立即命中原阶段停止条件，不通过本任务补公式或兼容分支。

## 四、验证策略

验证按子任务影响面分层，相关输入未变化时不重复同范围检查：

- `01E1`：新纯运行时的定向 EditMode 测试、`tools/check-unity-assembly-boundaries.ps1`、路径与差异检查。
- `01E2`：受影响正式调用链的 EditMode、`GuanzhongBasicAttackPlayModeTests` 或实时冻结的等价 PlayMode 入口、程序集边界和旧生产引用扫描。
- `01E3`：完整 Unity EditMode、相关 PlayMode、全仓旧类型／GUID 引用扫描、程序集边界、审核文本、空白和 staged diff 检查。

Unity 生成的 `.csproj`／`.sln` 不是仓库事实。隔离 worktree 中存在实时 Unity 工程投影时可运行 `.NET` 快速构建；投影缺失时必须明确记录 `MISSING_PROJECT`，以 Unity 编译和 EditMode 为权威验证，不得把缺少被忽略的生成文件误报为任务 blocker。

现有已登记基线失败只能原样报告；任何新增失败都阻止对应子任务完成。未修改 BattleSim 输入且回归证明数值语义未变时不重复 BattleSim；一旦发现数值语义变化，按原任务停止条件停止并单独处置。

## 五、现有 run 与调度处置

实施任务重划前，先对现有 `attention_required` run 做精确人工关闭：

1. 重新 `Show`，核对 owner、runId、taskId、base、taskCardDigest、worktree、branch、HEAD、状态、进程与集成锁。
2. 只有 worktree 干净、branch HEAD 精确等于 base、无 candidate／canonical 提交且 recoveryReason 未变化时，才使用 schema 5 `CompleteRun` 的 empty attention close 合同关闭。
3. runtime 关闭后再次核对 worktree 注册、路径、branch、HEAD 和清洁度，再精确删除该空 worktree 与临时 branch；不使用泛化清理、stash、reset、checkout 或 `git clean`。
4. 不重复发送首次失败通知，不把该空 run 记录为业务成果。

随后在独立手动 worktree 中完成父卡、三个子卡、场景与 Unity backlog 投影及有序队列的同一管理切片，运行 `tools/check-task-cards.ps1`、`tools/check-review-text.ps1`、pending whitespace 和 cached diff 检查，并在重新核对 schema 5 runtime、集成锁、master 与主工作区路径冲突后通过 `tools/invoke-project-integration.ps1` 集成。

## 六、自动化配置边界

本设计不修改 `invoke-hourly-owner.ps1`、候选 terminal schema、runtime schema、失败关闭合同、通知策略或自动化 TOML。`codex-hourly-worker` 和 `deepseek-hourly-trigger` 在任务卡重划与管理验证完成前保持当前暂停状态。

该故障只属于 Codex 任务切片；不得借本次处置改变 DeepSeek route、主责或恢复逻辑。任务卡重划集成后，是否恢复小时入口只通过 Codex automation 管理能力执行；不得直接编辑 TOML。恢复前必须确认 runtime 两个 owner 均为空、集成锁空闲、`01E1` 是唯一新增 ready 子卡、主工作区相关路径无冲突。若只恢复 Codex 入口即可验证新切片，则不同时改变 DeepSeek 状态。

## 七、完成条件与停止条件

本次任务重划完成必须同时满足：

- 旧空 run 已按精确证据关闭并安全清理。
- `01E` 已成为 blocked 汇总父项，`01E1`／`01E2`／`01E3` 的依赖、任务卡、backlog 和队列投影一致。
- 只有 `01E1` 为 ready；`01F` 与架构父项依赖语义未提前解锁。
- 三个子任务均具有完整必查范围、expectedPaths、验证、完成条件和停止条件。
- 任务卡、审核文本、空白和 staged diff 检查通过，且集成没有覆盖用户现有改动。
- 自动化 runtime、失败语义和通知机制未被扩张。

出现以下任一情况立即停止，不继续叠加补丁：

- 不能在不引入生产双运行、兼容 adapter 或临时公式的前提下形成上述三个可验证边界。
- 实时扫描发现 `01E1` 必须修改正式调用链才能编译，或 `01E2` 必须提前进入 GameRuntime／Feature 重写范围。
- 当前 run、worktree、branch、进程或 recoveryReason 与本设计记录不一致。
- 任务管理路径与主工作区现有 staged、unstaged 或 untracked 改动冲突。
- 调整依赖会提前解锁 `01F`、改变 BattleSim 数值语义或突破阶段 5 既定职责。
