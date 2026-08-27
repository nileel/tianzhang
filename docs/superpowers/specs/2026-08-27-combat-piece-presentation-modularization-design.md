# 战斗棋子功能与表现模块化设计

状态：2026-08-27 用户已批准修订稿；本文是后续原子拆卡与实施的直接设计事实源。

## 一、决策

- 后续正式战场角色表现以静态 3D 为默认方向，优先推进依赖该方向的正式内容。
- 选择静态 3D 的原因是当前 2D 样例与场景的整体协调度仍不足，且缺少足够的配套内容；这不是对 2D 路线的永久否定。
- 2D 保留为独立实验与调研方向，不阻塞静态 3D 正式链，也不再作为正式内容的默认资产前置。
- 2D／静态 3D 的可替换性只服务开发期组合。正式游戏不提供玩家运行时切换选项，同一正式场景同时只装配一个棋子表现实现。
- 后续实现必须使棋子的规则功能和可见表现完全分离；替换表现实现不得修改格位、朝向、行动、伤害、AI、结算或存档规则。

## 二、现状与根因

正式链当前为：

`AdventureSceneInstaller.unitMarkerPrefab`
→ `AdventureController`
→ `EncounterCoordinator.TryBegin()`
→ `AdventureUnitSpawner.TrySpawn()`
→ 同一方法同时创建 `CombatantSnapshot` 和 `PlayerMarker`／`EnemyMarker`。

`AdventureSpawnSet` 目前同时保存战斗快照与两个 `GameObject`；`EncounterCoordinator` 在战斗结束时直接销毁这两个对象。因此正式 Adventure 的规则准备、表现创建和表现生命周期仍耦合在同一条 Feature 链中，仅替换 `unitMarkerPrefab` 不能满足可替换要求。

现有 `StaticChessPresentationController`、`BattleAnimationSpritePresentationController` 与 `BattleVisualComparisonController` 只属于 `AdventureScene/VisualBaselineBoard` 的隔离比较入口。它们已证明两种载体可消费共同的 `StaticChessPresentationEvent`，但尚未接入正式 `AdventureUnitSpawner`、正式行动结果或存档链，不能直接视为正式棋子模块。

## 三、目标与非目标

### 3.1 目标

1. Adventure／Combat 只拥有战斗事实与规则结果，不创建、保存或销毁具体 2D／3D 表现对象。
2. CombatPresentation 只消费只读棋子身份、格位、朝向和表现事件，不反向修改战斗状态。
3. Bootstrap 只在开发期选择并注入一个表现实现，正式默认静态 3D。
4. 静态 3D 与后续 2D 实验实现消费同一表现合同；切换实现不需要修改 Adventure／Combat 规则代码。
5. 缺少正式表现资料时失败关闭，不静默回退到技术 Marker、2D 或另一套 3D 资产。

### 3.2 非目标

- 不提供玩家设置、运行时热切换、同场景双实现或存档中的表现路线字段。
- 不在本设计中生产新模型、Sprite、动画、音效或 VFX。
- 不恢复已冻结的旧 Tripo 绑定动画、人形骨架或“正式动画 3D 替换”路线。
- 不把隔离比较入口改造成正式战斗入口，也不让实验 2D 依赖成为静态 3D 正式链的 blocker。
- 不调整战斗数值、CTB、六角格、AI、命令合法性、伤害、结算或保存结构。

## 四、模块边界

### 4.1 跨模块表现合同

在现有 `TianZhang.Gameplay.Contracts` 中建立不引用具体 Prefab、Renderer、Sprite、Mesh 或 `GameObject` 的只读合同。合同承载三类消息：

- 棋子生命周期：准备、生成、移除、整场清理。
- 规则投影：稳定 combatant ID、正式 presentation profile ID、阵营显示角色、六角格坐标与规则已确定的朝向。
- 表现事件：`Idle`、`Move`、`Attack`、`Hit`、`Cast`、`Death`，以及该事件所需的起止格位、方向和已完成规则结果的只读数值投影，例如命中目标 ID、逐目标最终伤害、规则确认后的终点与死亡状态。

合同只表达“已经发生什么”，不携带 `CombatActionResult` 引用、伤害计算器、可写 `CombatantSnapshot`、AI、命令服务或存档对象。只读数值投影由 Adventure 在规则结果已经确定后复制形成，不要求 GameplayContracts 引用 Combat。表现实现不得通过返回值改变命令是否合法、伤害数值、行动完成或胜负结果。

首个合同切片将现有 `StaticChessPresentationEvent` 原子改名为载体中立的 `CombatUnitPresentationEvent`；不保留兼容枚举，也不复制第二套 2D／3D 专属规则枚举。正式链需要的只读事件 DTO 继续在同一 GameplayContracts 逻辑单元内定义。

原子改名必须一次覆盖当前全部九个命中文件，不得只改静态 3D 控制器：

- 定义：`src/Assets/Scripts/Modules/GameplayContracts/CombatPresentationContracts.cs`。
- 四个控制器：`StaticChessPresentationController.cs`、`TacticalSpritePresentationController.cs`、`BattleAnimationSpritePresentationController.cs`、`BattleVisualComparisonController.cs`。
- 四个 PlayMode 测试：`StaticChessPresentationPlayModeTests.cs`、`TacticalSpritePresentationPlayModeTests.cs`、`BattleAnimationSpritePresentationPlayModeTests.cs`、`BattleVisualComparisonPlayModeTests.cs`。

同一合同切片移除 `TianZhang.Features.CombatPresentation.asmdef` 中当前未被代码消费的 `TianZhang.Combat` 引用，并同步 `UNITY_STRUCTURE.assemblies.md`、程序集边界检查及直接测试。CombatPresentation 只保留 Foundation、Gameplay.Contracts 与其实际使用的 Unity UI 依赖。

### 4.2 既有 HUD 端口保持不变

- 现有 `ICombatPresentationSink` 与 `CombatHudSnapshot` 继续只负责 HUD、行动栏和战斗日志，不改名、不迁移，也不并入新的棋子对象生命周期合同。
- 新棋子表现端口与 HUD sink 是两个并存接口：前者消费棋子生成、格位、方向与动作事件，后者消费文字和状态快照。
- `EncounterCoordinator` 可分别持有并调用两个端口，但不得建立一个同时拥有 HUD、棋子对象、规则命令和日志的复合 presenter 或新 Hub。

### 4.3 Adventure 与 Combat

- `AdventureUnitSpawner` 继续负责把角色／敌人事实转换为 `CombatantSnapshot`，但不再实例化、染色或返回可见对象。
- `AdventureSpawnSet` 移除 `PlayerMarker`、`EnemyMarker` 等具体表现引用，只保存正式战斗所需的快照和稳定内容引用。
- `EncounterCoordinator` 在规则动作成功并形成确定结果后，向表现端口单向发布事件；表现失败不得重算或改写规则结果。
- 战斗开始前必须一次性验证本场全部 presentation profile 可解析。验证失败时不创建 `CombatSession`，并返回精确原因；不得在战斗已开始后才发现基础 Prefab 缺失。
- 战斗结束、失败退出或场景销毁时，通过表现端口按稳定 combatant ID 清理；Adventure 不直接 `Destroy` 具体棋子对象。

### 4.4 CombatPresentation

- 正式静态 3D 提供器拥有 Prefab 解析、实例创建、材质、模型／底座、根节点动效、VFX、音效和对象清理。
- 现有静态 3D pilot 控制器只能在正式提供器明确消费其已验证逻辑时复用；不得把 `VisualBaselineBoard`、六个 probe 或比较 UI 带入正式链。
- 后续 2D 实验提供器可消费同一生命周期与事件合同，拥有 atlas、SpriteRenderer、帧播放和 billboard 行为，但只接入隔离实验组合。
- 两个提供器之间不得直接引用、互相回退或共享可写运行时状态。可共享的只有 GameplayContracts 中的只读合同和已经证明与载体无关的纯事件语义。
- CombatPresentation 不再直接引用 `TianZhang.Combat` 程序集；若某项表现需要更多规则信息，只能扩展经过裁剪的 GameplayContracts 只读投影，不得重新添加 Combat 实现依赖。

### 4.5 Bootstrap、Editor Builder 与开发期选择

- `AdventureSceneInstaller` 保存一个明确的表现提供器序列化引用，并在 `Awake()` 中验证该对象实现批准的表现合同。
- 正式接入切片必须同步修改 `AdventureSceneBuilder.Build()`：删除 `unitMarkerPrefab` 的序列化写入，改为写入唯一静态 3D 提供器引用，并由 Builder 确定性保存和回读 `AdventureScene`；不手工编辑场景 YAML。
- 正式 `AdventureScene` 最终固定绑定静态 3D 提供器；不保存 2D 提供器、正式路线枚举或面向玩家的切换按钮。
- 2D 实验只在独立实验入口或测试组合中注入 2D 提供器。开发者通过场景／测试组合选择实现，不通过正式游戏 UI 切换。
- Bootstrap 只负责引用校验和依赖注入，不承载 Prefab 选择、事件动画、资产回退或战斗规则。

当前过渡期的 `VisualBaselineBoard`、`BattleVisualComparisonPanel` 和路线按钮仍序列化在 build-enabled `AdventureScene`，但它们只控制隔离 probe，不实现、不注册也不调用新的正式棋子表现端口，因此在合同与资产准备切片中不计为正式提供器切换。正式静态 3D 接入切片关闭前，必须把这些比较对象和按钮从 build-enabled `AdventureScene` 移除；既有比较资产、控制器、证据和直接测试保留。后续确需人工观察 2D 时，由独立实验切片建立不进入 `EditorBuildSettings`、不含第二个正式 Adventure／Bootstrap 的测试入口。

## 五、数据与资产边界

- 每个正式棋子必须具有稳定 `presentationProfileId`；该 ID 的内容来源和 Unity 资产映射必须在正式接入任务中冻结并通过数据链验证。
- 玩家与敌人的表现资料分别从其批准的角色／敌人内容事实投影，不以 `player`、`enemy` 对象名或临时颜色分支代替正式 profile。
- 静态 3D 首批正式接入必须同时具备正式玩家与石甲兽的稳定 profile、批准资产及 Unity 映射；两类资产的批准和 QA 先于任何正式接线。缺少任一方时正式 provider 接入保持阻塞，不以单角色 pilot 代替完整遭遇输入。
- 现有 `FuYuan_StaticChess` 只受 `docs/superpowers/specs/2026-08-21-static-3d-chess-character-production-contract.md` 的隔离比较授权约束；该合同明确禁止把苻渊样例接入第一章、正式遭遇、`AdventureUnitSpawner` 或常规可战斗单位，因此它只能作为机制与 QA 证据，不能直接成为正式玩家资产。
- 旧 `U-CHAR-3D-FORMAL-01` 的文字包含玩家与石甲兽，但该卡属于已冻结的动画 3D 正式替换路线，不构成两项静态 3D 资产的现行批准。
- 获得正式批准的静态 3D 资产必须核验来源、材质、缩放、朝向、接地和六方向合同。
- 缺少 profile、重复 ID、未知 Prefab、缺失材质／关键组件或方向合同不完整时，正式场景失败关闭，并报告精确 profile 与原因。
- 不保留 `UnitMarker.prefab` 作为隐藏兜底。它可在解耦迁移的专用回归 fixture 中验证旧行为，但不得成为静态 3D 正式提供器的运行时 fallback。

## 六、事件流

正式事件顺序固定为：

1. Adventure 形成玩家与敌人的纯战斗快照。
2. 表现端口预检本场全部 profile、位置与方向输入。
3. 预检通过后创建 `CombatSession`，表现提供器按稳定 ID 生成棋子。
4. Combat 完成命令合法性与结果计算。
5. Adventure 将成功结果投影成只读表现事件；失败命令不伪造攻击、受击或移动事件。
6. 表现提供器只播放该事件，不向规则层回写位置、命中、伤害、死亡或完成信号作为规则依据。
7. Combat 的胜负结果独立成立；随后表现端口清理全部对象，正式场景继续既有返回流程。

攻击／术法事件必须区分施放者动作和目标受击；移动事件的终点来自规则确认后的格位；死亡事件只来自规则确认的死亡状态。表现时长不得成为 CTB 推进、AI 决策或结算的必要输入。

## 七、失败与隔离

- 配置错误在战斗开始前失败关闭，不自动选择另一表现实现。
- 表现端口不得吞掉未知 profile、缺失组件或未支持事件；错误信息必须包含稳定 unit ID、profile ID 与事件。
- 表现播放不成功不能回滚或重放已经提交的规则动作，也不能触发第二次伤害、奖励或存档写入。
- 2D 实验失败只影响实验入口，不改变正式静态 3D 场景配置、任务状态或内容生产方向。
- 正式静态 3D 推进不得要求 2D 实验先完成；2D 实验也不得修改正式 profile 或静态 3D provider。

## 八、实施切片边界

后续规划应按以下独立结果与依赖拆分，不在一个任务中同时完成合同、正式资产生产、正式接入和 2D 实验：

1. 载体中立合同：建立共同 DTO／端口，完成九个枚举命中文件的原子改名，移除 CombatPresentation → Combat 的悬空程序集引用并同步结构事实与直接测试；本切片不改 Adventure 的现行 Marker 所有权或场景接线。
2. 正式静态 3D 资产与 profile 冻结：分别完成正式玩家与石甲兽的资产批准、QA、稳定 profile 和 Unity 映射；不接入正式 Adventure，不使用苻渊隔离样例充当正式资产。
3. 静态 3D provider 与正式链解耦接入：同时依赖切片 1、2；在一个原子结果中实现正式 provider，移除 `AdventureUnitSpawner`／`AdventureSpawnSet`／`EncounterCoordinator` 对具体 Marker 的创建、保存和销毁，同步 `AdventureSceneInstaller`、`AdventureSceneBuilder`、正式场景、Validator 和直接测试，并移除 `UnitMarker.prefab` 的正式运行时引用及 build-enabled 场景中的比较切换入口。
4. 静态 3D 内容扩展：依赖切片 3；在 provider 合同不变的前提下逐批增加其他玩家、敌人和表现资料。
5. 2D 实验适配：只依赖切片 1；独立把既有 2D pilot 接到同一合同的非 BuildSettings 实验组合，只产出调研证据，不阻塞切片 2～4。

切片 1 与切片 2 可并行准备；切片 3 必须同时等待两者完成。这样每次合入都保持主分支可运行，不引入“Adventure 已移除 Marker、正式静态 3D 资产却尚未可用”的半解耦状态。

旧 `U-CHAR-3D-FORMAL-01` 绑定的是已冻结的动画 3D 正式替换路线，不能因本次选择而直接解冻。后续任务必须按本设计重新确认静态 3D 所有者、资产形状、精确路径和验证入口。

## 九、验证与完成条件

### 9.1 架构验证

- `AdventureUnitSpawner`、`AdventureSpawnSet` 与 `EncounterCoordinator` 不再保存或销毁具体 2D／3D 对象。
- Adventure 与 Combat 不引用静态 3D／2D 控制器、Prefab、Sprite、Mesh 或具体提供器实现。
- GameplayContracts 不引用 `TianZhang.Combat`，棋子事件 DTO 不包含 `CombatActionResult` 或其他 Combat 实现类型。
- `TianZhang.Features.CombatPresentation.asmdef` 不再引用 `TianZhang.Combat`；CombatPresentation 不取得可写战斗状态，也不被 Combat 领域程序集引用。
- 既有 `ICombatPresentationSink`／`CombatHudSnapshot` 保持原名、原职责和独立接口，没有与棋子表现端口合并。
- 旧 `StaticChessPresentationEvent` 在定义、四个控制器和四个 PlayMode 测试中均已原子迁移，仓库相关代码零残留旧枚举名。
- 正式接入完成后的 `AdventureScene` 只绑定一个静态 3D 提供器，不包含 `VisualBaselineBoard`、`BattleVisualComparisonPanel`、玩家切换入口或隐藏 2D fallback；过渡期比较入口只因尚未开始正式接入切片而允许存在，且不得连接新端口。
- `AdventureSceneBuilder.Build()` 不再写入 `unitMarkerPrefab`，而是确定性写入唯一 provider；场景保存后重新打开仍保持该引用。
- 程序集边界检查通过，Feature 之间不新增实现直连。

### 9.2 行为验证

- 使用测试提供器、正式静态 3D 提供器分别运行相同战斗输入，Combatant 快照、合法行动、伤害、胜负、奖励和保存结果完全一致。
- 正式接线前，正式玩家与石甲兽的 profile、资产批准和 Unity 映射均已独立通过；苻渊隔离样例不出现在正式 provider 映射中。
- 正式静态 3D 能按稳定 ID 生成、接收六类共同事件、使用规则确认的格位／方向并在结束时精确清理。
- 缺少 profile 或资产时在创建 CombatSession 前返回确定性失败，不生成技术 Marker，不切换到 2D。
- 2D 实验提供器可在独立入口消费同一事件 DTO，且正式 `AdventureScene` 的序列化配置和运行结果不变化。

### 9.3 最小门禁

- `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-unity-assembly-boundaries.ps1`
- 相关 EditMode 测试。
- 正式 Adventure 的相关 PlayMode 端到端测试。
- 场景／Prefab／asset 路径变化时运行 `SceneArchitectureValidator` 与对应数据链检查。
- 预期路径 whitespace 与 `git diff --check`。

设计完成条件是：静态 3D 成为唯一正式默认实现，2D 保留为非阻塞实验实现，且功能结果能够在不读取或依赖具体表现实现的情况下独立通过验证。

## 十、停止条件

- 需要让 GameplayContracts 引用 Combat、Adventure、`CombatActionResult` 或具体 Unity 资产实现。
- 需要让 Adventure／Combat 根据表现完成回调决定规则推进、伤害或胜负。
- 需要在正式场景同时常驻 2D 与 3D，或增加玩家运行时选择。
- 需要用未知 profile 的技术 Marker、另一条表现路线或对象名猜测作为 fallback。
- 需要直接恢复已冻结的旧绑定动画／统一骨架任务，或把 2D 实验设为静态 3D 正式链前置。
- 单个实施任务开始同时承担合同、批量资产、正式接入与实验调研多个独立结果；此时必须先拆卡再执行。
