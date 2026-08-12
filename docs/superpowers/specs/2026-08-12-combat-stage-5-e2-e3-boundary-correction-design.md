# Combat 阶段 5：01E2／01E3 边界修正设计

日期：2026-08-12
状态：负责人已审核并批准实施；2026-08-12 飞书决策 A 的最新增补见 §11，当前顺序为 01E1B、01E2、01E3。

## 一、背景与根因

`U-ARCH-REBUILD-01E2` 的自动化 checkpoint `f381ffa07b72748d642e292279d52c427ff09657` 已完成大部分正式战斗链切换，但原冻结设计漏掉了三类直接依赖：

1. 新正式组合根仍调用待由 01E3 删除的 `DamageCalculator.ResolveElement`／`GetGongFaElement`。
2. `AbilityRequirementPolicy` 仍位于 Combat 并接收 `Character`，使 01E3 无法兑现“Combat 不引用 Character／Domain”；编辑器导入器也仍直接调用旧 `DamageCalculator`。
3. 多个直接 EditMode 测试和 01E1A legacy 对照仍构造旧 `TacticalCombatController`、旧 `CombatResolver`、Core `CTBEngine` 或 `Character.CTBUnit`，而 01E3 的冻结路径没有覆盖迁移这些调用所需的文件；01E3 同时漏列删除 `Character.CTBUnit` 与旧 AI 编译残件所需的源文件。

负责人已经选择并批准：不新增 01E2A，不把 01E2 与 01E3 合并；修正现有两个任务的冻结边界。现有自动化 checkpoint 和负责人授权的 `GuanzhongFormalUiTextPlayModeTests` 迁移保留为实施输入，但在本设计审核前不集成到 `master`。

## 二、目标与非目标

### 2.1 目标

- 01E2 以单一提交完成正式调用链和所有直接测试的原子切换，使新 `CombatSession` 成为唯一生产可达战术运行时。
- 01E2 后，旧 Controller、Resolver、DamageCalculator 与 Core CTB 仅作为相互依赖的编译期遗留存在；生产代码、场景、Prefab、编辑器导入器和测试均不再调用它们。
- 01E3 以单一提交删除旧实现、兼容字段与遗留程序集依赖，并完成阶段 5 总验收。
- 保持既有战斗数值、范围、CTB、元素和结算语义不变。

### 2.2 非目标

- 不修改 `CombatCommand`、`CombatSession`、`CombatActionResolver`、`CombatCommandService`、`CombatActionResult`、`CombatantSnapshot`、`CombatAttackProfile`、`CombatantRegistry`、`CombatTurnScheduler`、`CombatResultBuilder`、`CombatLegalActionService` 或 `Combat/Turns`。
- 不增加双运行、shadow compare、兼容 adapter、运行时开关、fallback 或新状态。
- 不修改 Scene、Prefab、正式内容 asset、CSV、伤害公式、BattleSim 数据、GameRuntime 或 Feature 职责。
- 不借本次修正拆分 `ExplorationController`；只替换已证明的旧依赖调用。

## 三、任务与提交边界

### 3.1 `U-ARCH-REBUILD-01E2`：正式调用链原子切换

01E2 负责：

- 吸收既有 checkpoint 的 `CombatSession`、命令、合法行动、Adventure 结果与 GameplayContracts 切换。
- 完成负责人已批准的正式 UI 文本 PlayMode 测试迁移。
- 把元素事实与能力要求门禁迁出旧 Combat 实现，并切换 Gameplay、Editor 与直接测试调用方。
- 把旧运行时直接测试迁到新 session／命令／合法行动入口；旧实现只允许被自身遗留源码引用。
- 保留尚供旧源码编译的 `Character.CTBUnit`、`IAIController`、`SimpleAI` 等残件，但生产路径不得读写或解析它们。

01E2 完成时不删除旧 Controller、Resolver、DamageCalculator 或 Core CTB 源码，以维持与 01E3 的原子删除边界。

### 3.2 `U-ARCH-REBUILD-01E3`：遗留删除与阶段 5 总验收

01E3 负责：

- 删除旧 `TacticalCombatController`、旧 `CombatResolver`、旧 `DamageCalculator`、Core `CTBEngine` 及对应 `.meta`。
- 同批删除 `Character.CTBUnit` 和 `EnemyAI.cs` 中仅供旧控制器编译的 `IAIController`、`SimpleAI`、旧 `TryResolve`；保留新 `ICombatActionPolicy`、`LegalActionAI` 与 `TryResolveCombatActionPolicy`。
- 收紧 `TianZhang.Combat.asmdef`：移除旧实现带来的 Domain 与 Content 依赖，只保留新纯 Combat 实际使用的 Foundation、Spatial 与 `TianZhang.Combat.Turns`。
- 更新 Combat README、程序集边界证据，并与 01E 父项同批归档。

## 四、组件与数据流

### 4.1 Content 元素事实源

将 `src/Assets/Scripts/Combat/SpellData.cs` 中的 `AbilityRequirementPolicy` 迁入 Content 模块的聚焦文件：

`src/Assets/Scripts/Modules/Content/CombatElementFacts.cs`

实时文件核验固定以下三个边界，后续不得再按同名文件推断：

- `src/Assets/Scripts/Combat/SpellData.cs` 只有 39 行，只定义静态 `AbilityRequirementPolicy`；其 `.meta` GUID 为 `9ac7220e2d1e4a90be4b8c9ea1a220d5`，当前没有任何非 `.meta` 序列化引用。
- `src/Assets/Scripts/Modules/Content/SpellData.cs` 是 86 行的 `SpellData : ScriptableObject`，同时定义 `SpellType`／`SpellRange`；其 GUID 为 `0d2294bd6db111a4da8262a00f7142e2`，被现有术法 asset 使用，本设计不移动、不改名、不修改该文件或 `.meta`。
- 旧 `DamageCalculator` 独立定义于 `src/Assets/Scripts/Combat/DamageCalculator.cs`，GUID 为 `086b47075d18e5c4ab038329ac78c8e7`；它与上述两个 `SpellData.cs` 不是同一文件，继续按原计划保留到 01E3 删除。

因此 01E2 只把第一项静态门禁文件连同原 `.meta` 移到新路径，保留其 GUID；这不会同时移动或提前删除旧 `DamageCalculator`。迁移后的聚焦文件提供以下纯静态职责：

- `CombatElementFacts.ResolveElement(string)`：原样迁移旧 `DamageCalculator.ResolveElement` 与其标准化表。
- `CombatElementFacts.ResolveGongFaElement(string)`：原样迁移旧功法名称／稳定 ID 到主元素的映射。
- `AbilityRequirementPolicy.IsSatisfied(float realmMultiplier, string visibleRootElement, string realmRequirement, string elementRequirement)`：以稳定原语替代 `Character` 参数，并复用同一元素标准化事实。投影范围严格只有 `RealmMultiplier` 与 `VisibleRootElement`；`Character` 的生命、灵力、功法、槽位、状态等其他字段均不进入 Content 门禁。

这些类型继续使用当前 Content 数据类型既有的命名空间约定，归属程序集以 `TianZhang.Content.asmdef` 为准。它们不引用 Unity 场景对象、Gameplay、Domain 或 Combat 实现。

移动前必须以原 `SpellData.cs.meta` GUID 扫描 Scene、Prefab、asset、controller 与 anim；虽然当前目标是纯静态类型，仍以序列化零引用作为安全移动证明。发现引用即停止，不猜测其无害。

未知功法仍返回空元素；空、未知或不合法的能力要求仍按现有行为拒绝。不得扩展元素集合或改变别名语义。

### 4.2 正式运行时流

正式战斗数据流固定为：

```text
Character / AttackProfileData
-> ExplorationController 组合投影
-> CombatantSnapshot / CombatAttackProfile
-> CombatSession
-> CombatCommandService + CombatLegalActionService
-> CombatActionResult / CombatSessionOutcome
-> BattleUIManager / AdventureSceneController
```

- `ExplorationController` 使用 `CombatElementFacts` 投影 snapshot 元素与按钮显示文字，不再调用旧 `DamageCalculator`。
- `SectSelectionManager` 把 `player.RealmMultiplier` 与 `player.VisibleRootElement` 投影给新的 `AbilityRequirementPolicy`，不把 `Character` 传入 Content 门禁。
- `ContentImportCoordinator` 使用同一 `CombatElementFacts.ResolveElement`，不再引用旧 `DamageCalculator`。
- `BattleUIManager` 只通过 `ICombatCommandHandler` 发送七类稳定命令；不得恢复场景类型、Character 或 Combat 实现依赖。
- `EnemyAI` 正式路径只消费 `CombatLegalActionService` 已准入的 `IReadOnlyList<CombatCommand>`。

### 4.3 测试迁移

- `AbilityRequirementPolicyTests` 直接定位 Content 程序集中的新门禁，并按稳定原语验证允许／拒绝。
- `AttackProfileDataTests` 与 `TacticalGridModelTests` 的旧引用迁移全部归属 01E2：其中仍有价值的攻击档案、范围、行动、AI 和结果语义迁到新 session／command／legal-action API；只验证旧控制器自身生命周期的用例删除，不保留旧入口。01E3 不再承担这两个文件的测试迁移，只验证删除旧源码后的零引用与完整回归。
- `CombatRuntimeKernelTests` 将 legacy runtime 动态对照改成冻结的明确期望值；保留相同命中、伤害、CT 消耗、范围和结果断言，但不构造旧类型。
- `GuanzhongFormalUiTextPlayModeTests` 在 `CombatSession` snapshot 中布置确定性一血夹具，再通过公开 `ExplorationController.RequestBasicAttack` 完成正式击败、结算、掉落和悬赏流程，不直接修改 `Character` 生命。
- UI 命令按钮测试必须真实证明基础攻击、防御、等待、换法、术法与神通把稳定上下文路由给 `ICombatCommandHandler`；不得以重复的“未绑定点击被忽略”测试替代路由验证。

### 4.4 新旧 CTB 身份锁

- `src/Assets/Scripts/Combat/Turns/CTBEngine.cs` 属于 `TianZhang.Combat.Turns`，GUID 为 `e9b8528db6e24899ac644e67e408a112`；它由 01E1 建立并以 `CombatRuntimeKernelTests` 5/5、完整 EditMode 450/452 验收，01E1A 又以定向测试 11/11 验证其移动、换法和七类合法行动消费语义。它是新内核正式保留的唯一 CTB。
- `src/Assets/Scripts/Core/CTBEngine.cs` 属于 `TianZhang.Core`，GUID 为 `3564ab48a0a06ef4e9110b900398eb49`；它是 01E3 删除对象。
- 所有扫描、任务卡和验收结论必须使用完整路径、命名空间或 GUID 区分二者；不得把字符串 `CTBEngine` 的笼统命中误报为新 CTB 也应删除。

## 五、冻结路径修正

### 5.1 01E2 新增或明确纳入

- `docs/superpowers/specs/2026-08-12-combat-stage-5-e2-e3-boundary-correction-design.md`
- `src/Assets/Scripts/Modules/Content/CombatElementFacts.cs`
- `src/Assets/Scripts/Modules/Content/CombatElementFacts.cs.meta`
- `src/Assets/Scripts/Game/SectSelectionManager.cs`
- `src/Assets/Scripts/Game/SectSelectionManager.cs.meta`
- `src/Assets/Scripts/Editor/ContentImportCoordinator.cs`
- `src/Assets/Scripts/Editor/ContentImportCoordinator.cs.meta`
- `src/Assets/Tests/EditMode/CombatRuntimeKernelTests.cs`
- `src/Assets/Tests/EditMode/CombatRuntimeKernelTests.cs.meta`
- `src/Assets/Tests/PlayMode/GuanzhongFormalUiTextPlayModeTests.cs`
- `src/Assets/Tests/PlayMode/GuanzhongFormalUiTextPlayModeTests.cs.meta`
- `开发管理/任务卡/U-ARCH-REBUILD-01E2.txt`
- `开发管理/任务卡/U-ARCH-REBUILD-01E3.txt`
- `开发管理/任务列表/场景与Unity任务.txt`
- `开发管理/当前任务队列.txt`

原 01E2 已冻结路径继续有效。`Combat/SpellData.cs` 与 `.meta` 作为只含静态门禁的迁移来源保留在路径集合中；迁移后原路径删除，GUID `9ac7220e2d1e4a90be4b8c9ea1a220d5` 随文件移动到 `Modules/Content/CombatElementFacts.cs`。既有 `Modules/Content/SpellData.cs` 及其 GUID `0d2294bd6db111a4da8262a00f7142e2` 不在修改范围。

### 5.2 01E3 新增或明确纳入

- `src/Assets/Scripts/Entity/Character.cs`
- `src/Assets/Scripts/Entity/Character.cs.meta`
- `src/Assets/Scripts/Combat/EnemyAI.cs`
- `src/Assets/Scripts/Combat/EnemyAI.cs.meta`

原 01E3 冻结路径继续有效。实施前若 Unity YAML、GUID、Resources／注册表扫描发现其他真实引用，必须停止并修订任务卡；不得临时扩大删除范围。

## 六、Unity 路由卡

```text
Task type: Unity 模块化结构修正与旧运行时删除
Structural trigger: 新 Content 事实文件、asmdef 收紧、旧源码与 .meta 删除
Runtime-visible target: 正式 Adventure 战斗按钮、日志、击败与结算；无布局或视觉资产变更
Current owner files: ExplorationController.cs, BattleUIManager.cs, AdventureSceneController.cs, FormalEncounterResult.cs
Project structure source: AGENTS.md + UNITY_STRUCTURE.md + live asmdef/source scan + 批准架构设计
Placement layer/category: TianZhang.Content 纯事实；TianZhang.Gameplay 组合；TianZhang.Combat 纯运行时
Module/system name: Content / GameplayContracts / Combat
Data/definition source: AttackProfileData、Character 投影、旧元素映射的逐项等价迁移
Runtime source-of-truth values: CombatSession、CombatantSnapshot、CombatAttackProfile
Allowed runtime callers: ExplorationController、SectSelectionManager、ContentImportCoordinator
Forbidden callers/surfaces: Scene/Prefab 新绑定、GameRuntime、Feature sibling、BattleSim、旧生产控制器
Shared helper touched: 新 CombatElementFacts；调用方已限定为 Gameplay、Content 门禁与 Editor 导入
New responsibility added: 是；仅 Content 模块中的纯元素事实，不进入现有 Hub
Cross-module communication: 通过已有 TianZhang.Content 引用；不新增反向或 sibling 依赖
Hub risk: medium；ExplorationController 超过 500 行，但本轮只替换调用，不新增职责
Visual source asset required: no
Files explicitly not touched: 新 Combat 内核、Combat/Turns、Scene、Prefab、CSV、asset、BattleSim
```

## 七、验证与完成证明

### 7.1 01E2 验收

1. 运行完整 Unity EditMode；只允许以下两项已登记基线失败原样存在：
   - `CharterConflictRulesTests.AuthorizedGrantBindsEveryConflictIdentityField`
   - `ContentImportCoordinatorContentScopeTests.LianShenReservedAbilitiesRemainExcludedAtPlayerLoad`
2. 运行完整 PlayMode，包含 `GuanzhongBasicAttackPlayModeTests` 与 `GuanzhongFormalUiTextPlayModeTests`。
3. 运行 `tools/check-unity-assembly-boundaries.ps1`。
4. 扫描生产 C#、Scene、Prefab、资源与注册路径：旧 Controller、Resolver、DamageCalculator、Core CTB 和 `Character.CTBUnit` 除旧源码自身及明确留到 01E3 的编译残件外不可达；测试与 Editor 引用为零。
5. 运行任务卡、审核文本、pending whitespace 与 staged diff 检查；相关 Content C# 路径变化时运行 `tools/check-data-chain.ps1`。
6. 证明正式战斗结果、命令拒绝原因、CT 消耗和元素映射与冻结期望一致。

### 7.2 01E3 验收

1. 对旧类型完整命名空间、旧源码 GUID、Unity YAML、Prefab、Scene、asset、Resources／注册表和程序集引用做全仓零引用扫描；明确保留 `TianZhang.Combat.Turns.CTBEngine` 及其 GUID，只清零 `TianZhang.Core.CTBEngine` 和旧 GUID。
2. 运行完整 Unity EditMode 与完整 PlayMode；失败集合不得新增。
3. 运行程序集边界、任务卡、审核文本、pending whitespace 与 staged diff 检查。
4. 验证 `TianZhang.Combat.asmdef` 不再引用 Domain 或 Content，且 GameplayContracts 仍不引用 Combat／Gameplay 实现。
5. 更新 README 和阶段 5 架构证据，并同批归档 01E3 与父项 01E。

### 7.3 BattleSim 边界

本设计不修改公式、倍率、CTB 数值或 BattleSim 输入，因此正常验收不重复 BattleSim。若冻结期望出现差异、固定战斗结果变化或必须修改公式，立即停止，把数值问题作为独立任务交给 BattleSim 验证，不在 01E2／01E3 叠补。

## 八、失败处理与停止条件

出现任一情况立即停止：

- 需要修改任一 01E1／01E1A 新 Combat 内核文件。
- 需要双运行、兼容 adapter、fallback、运行时开关或临时公式。
- 新增 Unity 编译／EditMode／PlayMode 失败，或既有两项基线失败发生变化。
- 删除证明发现任务卡未冻结的真实 Scene、Prefab、asset、GUID、Resources 或注册表引用。
- Combat 仍需依赖 Character／Domain，或依赖方向必须跨 Feature／Sibling 反转。
- 固定数值、元素、范围、CT 消耗或结算语义变化。
- 实施开始跨越多个未批准边界或需要继续追加补丁才能成立。

停止时保留隔离 worktree 与可审计 checkpoint，不集成、不归档任务、不把部分通过误报为完成。

## 九、实施与集成顺序

1. 本文审核通过后，先在独立管理切片中修正 01E2／01E3 任务卡、backlog 与队列；验证并集成该设计／管理提交。
2. 从最新 `master` 创建新的 01E2 隔离 worktree，仅重放 checkpoint 的业务路径和已授权 PlayMode 修改，不继承当前候选中的错误归档状态。
3. 完成 01E2 漏项迁移、验证、路径限定提交和安全集成；只有验收全过才归档 01E2。
4. 由队列维护机械解阻塞 01E3；从新的最新 `master` 创建独立 01E3 worktree。
5. 完成删除证明、旧源码删除、程序集收紧、完整验收和父项归档，再安全集成。
6. 每次集成前重新执行 schema 5 `Show`，确认两个 owner run 均未占用目标 taskId、进程持有型集成锁空闲、任务卡与队列投影未变化，且主工作区目标路径无 staged、unstaged 或 untracked 冲突。

## 十、完成定义

- 01E2：正式 Gameplay、Adventure、UI、AI、Editor 和直接测试均只消费新 Combat／Content 契约；旧战术运行时只剩待删源码内部互引。
- 01E3：旧 Controller、Resolver、DamageCalculator、Core CTB、`Character.CTBUnit`、旧 AI 接口和全部引用清零；Combat 程序集依赖收紧；完整 Unity 验收通过；01E3 与 01E 父项归档。
- 两个提交均没有 Scene／Prefab／asset／CSV／BattleSim 或新 Combat 内核改动，也没有覆盖用户或其他运行中的工作。

## 十一、2026-08-12 纯内核等价决策增补

### 11.1 决策与根因

迁移 `TacticalGridModelTests.CombatMechanismTests` 时确认，现行生产战斗仍通过 `Character`、旧 `DamageCalculator` 与旧 `CombatResolver` 使用玄感神魂攻击加值、含弘／载物防御、守一／符胆／雷劫按境界上限、雷劫受伤叠层及按境界每层倍率。新 `CombatantSnapshot` 未投影这些完整原语，`CombatActionResolver` 仍保留固定 2 层和固定 5% 等非等价实现。

负责人已在飞书决策 `DEC-20260812-E2KERNELPARITY` 选择 A：新增独立前置 `U-ARCH-REBUILD-01E1B`，先修复纯 Combat 数值等价性，再恢复 01E2／01E3。本增补优先于本文 §§2.2、3、4.3、5—10 中“新 Combat 内核不修改”和原实施顺序的冲突表述；其余边界继续有效。

### 11.2 `U-ARCH-REBUILD-01E1B` 边界

- 只修改 `CombatantSnapshot`、`CombatActionResolver`、Combat README、`CombatRuntimeKernelTests` 与 `TacticalGridModelTests` 中对应数值用例；旧 `Character`、`DamageCalculator`、`CombatResolver` 只读不改。
- 快照只投影稳定数值原语，不持有 Character／Domain／表现对象；解析器必须按快照当前生命重算载物防御，并使用投影上限与倍率兑现玄感、含弘、守一、符胆和雷劫现行语义。
- 将只保护上述数值的 legacy 用例迁到纯内核固定夹具；不处理旧控制器生命周期、生产组合、Content／Editor、UI 或删除工作。
- 运行定向与完整 Unity EditMode、程序集边界、BattleSim 当前结果核验及管理／文本／Git 检查；不修改 BattleSim 输入。

### 11.3 调整后的顺序与完成定义

1. 先执行并独立提交、验证、归档 01E1B。
2. 由队列维护解除 01E2 的具名前置；从最新 `master` 恢复已保留的生产切换 checkpoint 与已批准 PlayMode 迁移，完成 Content／Editor／直接测试旧引用清理。
3. 01E2 完成后再执行 01E3 删除与阶段 5 总验收。

01E1B 完成表示纯 Combat 内核已逐项覆盖现行功法战斗数值语义，且 01E2 不再需要修改纯内核。原文关于 01E2／01E3 不修改公式、BattleSim 数据或新内核的限制从此点继续生效；最终三项业务提交均不得包含 Scene／Prefab／asset／CSV／BattleSim 数据变化。
