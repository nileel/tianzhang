# 金丹组合首批生产范围与验收合同：单项候选

**状态：负责人在本对话明确选择 A；D-JD-COMBO-SCOPE-01 人工收口旧 checkpoint 后等待两项真实前置。** 本文冻结一项首批设计对象与验收方向，不登记正式 `comboProfileId`，不生产配方，`C-JD-COMBO-01` 仍为 blocked。2026-09-27 核对：旧 `DEC-20260921-CD08014CC7679C` 的 checkpoint 提交 `4888a905` 只列字段要求；本机对应 `accepted-replies/<decisionId>.json` 不存在，`decision-requests/<decisionId>.json` 仍是待发送结构，没有服务商接受后的 `pendingDecision` 记录。本次按负责人当前对话的直接 A 指令作人工任务投影，保留旧提交追溯，不伪造飞书回执或 `automationReply`。

## 一、既有事实和选题依据

| 事实 | 对本候选的约束 | 来源 |
|---|---|---|
| 每个稳定实位仅装配一项白名单效果；不同 `effect_id` 默认独立，只有预登记配方且位格、媒介、环境、容量与支付均合法才组合 | 本候选只能占两个真实位置、形成一个持续实例；当前没有正式组合可引用 | `docs/基础设定/金丹基础效果装配与冲突规则.txt#一、每位格一项效果与切换事务`、`#二、同效纵向嵌套与异效组合` |
| 火道路源位赫曦炉心和化位燃犀渡分别允许 `FIRE_MANIFEST`、`FIRE_ESCALATE`；源版可生成燃烧地格，化版升烈可从合法战场火源按预设走火 | 可提出同道路、两位置、两异效的局部战棋候选；白名单不等于配方批准 | `docs/基础设定/金丹位格/金丹位格索引.txt#六、正式具体位置登记表`；两份位置文档 `#六、效果白名单与逐项适配理由` |
| 化版升烈必须预设火种档案、起点和聚焰／走火，不能使用源版的通用火种兜底；走火只沿连续合法载体逐节点付预算 | 必须先证明源位留下的真实火源可作为化位预设档案的输入；不能凭“有火”跳过档案和账本 | `docs/基础设定/五行显化与环境交互规则.txt#四、升烈蔓延与火种`；燃犀渡 `#八、完整战棋玩法` |
| 战斗显化地表状态可持续占容量，但战斗结束清除；`JindanStaticStates.csv` 仍只有 schema 表头 | 候选限战棋，不产生山河常驻或运行时已支持的结论 | `docs/基础设定/五行显化与环境交互规则.txt#二、显化配方与运行载体`；`src/Assets/DataConfig/JindanStaticStates.csv` |

51 个初始具体位置均未登记横向组合，已批准正式组合及完成品路径数均为 **0**。岩浆、林地、风雪是规则示例，不是本轮生产清单。结构、位置与效果事实分别以 `docs/基础设定/元婴锚点与金丹位格设定.txt#三—八`、`docs/基础设定/元婴锚点与金丹位格矩阵.txt#二—六` 和上述位置原文为准。

## 二、建议的唯一首批对象、用途与规模

- **建议规模**：一项组合档案，战棋用途；至少金丹中期、已稳定占据下列两个真实位置时才有装配前提。不是主场景工程、山河周期规则或全火道路配方集合。
- **候选名称**：渡火引线（工作名）。**候选 `comboProfileId`**：`JD_COMBO_FIRE_FERRY_LINE_01`。两个标识仅供审批定位，尚未登记。
- **候选完成品精确路径**：`docs/基础设定/金丹组合/JD_COMBO_FIRE_FERRY_LINE_01_渡火引线.txt`。批准且前置完成后，由 `C-JD-COMBO-01` 的 DeepSeek 主责独立成文，Codex 独立复审；现在不得创建此文件。
- **`requiredEffectIds` 与位置**：`FIRE_MANIFEST` → `JD_FiveElements_Fire_SOURCE`／`ROAD_FIRE`／`SOURCE`／赫曦炉心；`FIRE_ESCALATE` → `JD_FiveElements_Fire_TRANSFORM01`／`ROAD_FIRE`／`TRANSFORM`／燃犀渡。两位置档案版本现均为 `1.1`，各只占自己的一个槽；不涉及第三位置或跨道路结构兼容档案。
- **用途**：在有燃犀渡阵眼与连续可燃渡口地格的局部战场，先由赫曦炉心以 `FIRE_MANIFEST` 的“燃烧地格”形态支付并留下真实火源，再让燃犀渡以预装的 `FIRE_ESCALATE` 化版“走火”沿一条固定、相邻、连续的合法燃料链传播。组合成立后把两位置及其持续承载归于**一个**可见、可切断的渡口燃烧地表实例；不赠送第二次行动、额外火种、额外效果槽或跨战斗产物。与两个独立效果相比，候选所需的配方语义是共同承载、共同占用和统一失败／结束账本，并非免费追加蔓延。

### 权限、媒介与结果字段

| 字段 | 本轮可确定的候选值或判定 | 必须保留的界限 |
|---|---|---|
| 源位权限贡献 | `FIRE_MANIFEST` 源版手动选择一处普通距离内合法燃烧地格，以炉心直连、灵力和实际燃料形成火焰／热压；`resultCarrierType` 采用既有**地表状态**分类 | 不把源位改为固定自动触发；不扩大为广域，也不把火焰当永久物品 |
| 化位权限贡献 | 燃犀渡阵眼只筛选预设的该火源及渡域内连续燃料链，固定执行一次“走火”；每进入一节点消耗公开预算 | 不临场改选聚焰、不跳过断裂燃料链、不逐对象选结果；没有合法火源或预装档案则不触发 |
| 真实媒介与环境 | 可检查的源位燃烧地格、渡域阵眼、渡口地形、相邻且可燃的地格链；水、隔绝、拆除燃料、扑灭或拆毁阵眼都能阻断 | 地图上不存在这些对象时失败关闭；不得用叙事中的“火势”替代实体及燃料记录 |
| 结果与原型 | 候选 `resultCarrierType=地表状态`；候选 `resultPrototypeId=JD_FIRE_FERRY_BURNING_LINE_01`，只代表战斗中的一条固定走火地表链 | 原型尚未登记；只准使用既有地表载体，不新增单位、CT、山河持久状态或战利品 |
| 解除与 `breakPolicy` | 建议 `END`，不提供 `fallbackProfileId`；任一位置退出、阵眼失效、燃料链断开、资源／容量不合法或战斗结束即停止组合未来作用 | 已合法落地的结果及已付费用不倒退；不得临场拆出免费独立实例或寻找后备 |

候选 `comboProfileId`、结果原型和上述固定操作都是**待批准设计输入**，不是当前仓库已存在的数据。尤其“燃烧地格能否成为燃犀渡预装火种档案的合法战场火源”需由下节设计前置给出逐字段证据；若交集不成立，此对象不能生产，不能补造权限。

## 三、付款与容量合同，以及最小前置任务

2026-09-30 用户明确实现边界并要求准备 ready：渡火引线的真实火源、连续燃料链、阻断、地表结果与费用／容量行为由 Unity 场景实现和验证；不以 BattleSim 补齐该场景为前置。BattleSim 只承担另行明确的、可抽象的角色数值平衡问题。当前源码只证实通用环境档案、空间查询及战斗入口，本候选实现和正式配置不存在已验证交付；一次建立技术准备、场景实现、费用容量验收三个步骤，只将技术准备设为 ready，不把它当成场景功能已实现。

本轮只确定**账本类别与支付顺序**：赫曦炉心显化先支付合法行动、灵力、燃料及持续地表显化容量；燃犀渡建立／触发组合前，再原子预览并支付其合法行动、展开费、逐节点火势预算和必要维持费；组合同时占用两个真实位置的持续承载，以及档案声明的地表／环境容量。若复用已付火源预算，必须记录实际消耗，不重复收费，也不得把已付费用折成免费传播。任何一项不足时支付前失败并保留旧合法状态；已完成结算按原账本保持。`JindanBaseEffectLoadoutTuning`、显化配方、组合费用／容量档案的数值和真实引用目前没有交付，本文不填写零值、倍率或伪造预算。

以下是已按人工 A 选择及本次 Unity 方向建立的**完整已知前置链**。`D-JD-COMBO-FIRE-INPUT-01` 已完成；技术准备卡先定位所有者、冻结实现与验收路径，然后由实现卡交付、N 卡复验。ID 和交付路径由任务维护，无需负责人设计技术字段。

| 前置任务 | 单一结果与精确交付路径 | 验收／停止 |
|---|---|---|
| `D-JD-COMBO-FIRE-INPUT-01`（Codex，设计） | `docs/基础设定/金丹组合/前置/JD_COMBO_FIRE_FERRY_LINE_01_媒介载体合同.txt`：冻结显化燃烧地格的来源引用、燃犀渡预装火种档案与合法战场火源映射、起点／触发／固定走火路线、既有地表载体和 `resultPrototypeId`、行动先后、解除与不可双算账本；列明两位置各自可用的兼容合同及版本，按现行编译顺序核验交集。 | 每个对象、操作、媒介和结果均回指矩阵／位置／五行规则；空交集、需新权限或需新增载体时停止并回到范围决定，不写正式配方。 |
| `D-JD-COMBO-FIRE-UNITY-01`（Codex，技术准备，ready） | 在本文冻结实际 Unity 入口、规则/数据所有者、候选输入来源、字面量代码/配置/资源与 .meta、定向测试和场景验收矩阵，并准备 U/N 两张既有下游卡。 | 只读代码、只写合同与管理投影；输入、权限、原子范围和风险门禁齐全才使 U 卡 ready，不改源码或扩充 BattleSim。 |
| `U-JD-COMBO-FIRE-SCENE-01`（Codex，Unity 实现，blocked） | 按技术准备合同交付单项可操作场景及同版本配置、直接测试证据，供 N 卡复验。 | 依赖技术准备；不扩展完整金丹系统、不以测试参数宣称平衡已定、不把候选场景当正式组合已生产。 |
| `N-JD-COMBO-FIRE-BUDGET-01`（Codex，Unity） | `docs/基础设定/金丹组合/前置/JD_COMBO_FIRE_FERRY_LINE_01_费用容量验证.txt`：绑定实际 Unity 场景、运行版本、显化／升烈／展开／维持／容量配置引用，记录火源、走火、支付及持续边界的实际运行结果。 | 连续燃料链、阻断、逐节点预算、容量占用／释放与行动／资源支付均在同版本场景复现，没有免费动作、重复付费或无燃料永动；ready 前冻结实际实现与测试路径，不能以模拟器缺功能要求复制一套场景。 |

依赖顺序：对象／用途／规模确认（已发生）→ `D-JD-COMBO-FIRE-INPUT-01`（已完成）→ `D-JD-COMBO-FIRE-UNITY-01`（ready）→ `U-JD-COMBO-FIRE-SCENE-01` → `N-JD-COMBO-FIRE-BUDGET-01` → `D-JD-COMBO-SCOPE-01` 收口首批范围／验收 → `C-JD-COMBO-01` 评估并进入正式档案生产。验收只在 U 卡实现交付后领取；前置未完成时，费用、容量、火种与结果原型的真实配置仍待交付，不把 C 卡改为 ready。技术准备必须区分候选功能测试输入和正式档案，不能构成“正式档案等测试、测试又等正式档案”的循环；需要新增权限或越过单项边界时按停止合同交回，不顺带替换第二项。

## 四、获批准后的逐项验收入口

1. **身份与白名单**：核对唯一 `comboProfileId`、两个不同 `effect_id`、两份已审核独立位置的 `positionId`／`positionType`／版本与白名单；两个稳定实位各只装配一项效果，组合只占一个持续实例。
2. **静态权限**：先按 `金丹基础效果装配与冲突规则.txt#三、跨道路静态兼容契约` 的白名单、位置理念、对象／操作／媒介、成本与容量顺序编译；本项同道路，仍须验证两项异效的贡献不越权。出现 `INCOMPATIBLE` 或未消歧的 `AMBIGUOUS` 即失败关闭。
3. **现场与媒介**：在战棋预览中核对已支付且仍合法的源位燃烧地格、燃犀渡阵眼、相邻连续燃料链、地表载体空位和目标层级；水、断燃料或毁阵眼的反例必须阻止后续走火，不产生半状态。
4. **成本与生命周期**：读取前置交付的真实配置 ID 和同版本 Unity 场景运行证据，核对行动、显化、火种投入、逐节点预算、展开／维持和容量占用／释放；切换／退出按 `END` 停止未来作用，已付费和已提交结果不返还，战斗结束清除战斗显化。该功能验证不代替抽象角色数值平衡结论。
5. **文件与职责**：批准后只有一份上述字面量完成品，DeepSeek 生产，Codex 独立复审；文档逐字段给来源和失败例。无需把组合写入 `JindanStaticStates.csv` 表头或声称 Unity 已支持；若生产数超过一项，须在同一次规划中建立全部叶子和父项关闭条件。

剧情内容仍遵守 `docs/剧情/剧情生产规范.txt#零、金丹与战斗事实源路由`、`#七、信息边界与禁止项`、`#十、AI 生产流程`、`#十一、审核清单`。本候选不设定具体持有者、宗门独占、地区事件或 NPC 全知信息。

## 五、交给负责人的最小语义决定

2026-09-27，负责人先在本对话回复“可以”，随后说明未收到原决策卡并明确要求“你直接选A”。据此，**A 的设计选择已明确**：首批为“渡火引线”一项，战棋局部渡口用途，源位显化火源与化位预设走火共同承载。负责人无需编写技术字段或预算；ID、文件路径、字段、费用及容量由上列任务落实。本对话的 A 选择不是飞书桥鉴权后的旧决策卡回执。本次人工收口旧 checkpoint，只将 D 卡改为等待真实前置的 blocked 状态，不生成 `automationReply`、不调用自动 `ResumeReady`、不解锁 C 卡。

## 六、渡火引线 Unity 单项场景实现与验收范围合同

### 1. 已证明的现行所有者与缺口

| 链路 | 已证明的现行职责 | 对本单项的结论 |
|---|---|---|
| `EnvironmentProfileRuntime` 与 `EnvironmentProfileAsset` | 只校验并保存环境档案的有向边、地表原型引用、现象配对、五行关系和查询上限；`EnvironmentProfileAsset.TryCreateDefinition` 只把序列化字段投影为该不可变定义。 | 可作为环境档案的既有事实参考，不能保存单格燃料、水、阵眼、火源、费用、容量或地表持续实例。不得把一个 `surfacePrototypeRef` 误作已存在的燃烧地格实现。 |
| `SpatialQueryBoard` 与 `SpatialQueryBoardFactory.TryCreate` | 前者是只读空间查询；后者把 `TacticalGridModel` 和环境档案投影为 `SpatialQuerySnapshot`。工厂仅保留高度、移动／效果阻断、实体障碍、占位和有向边；`TacticalTerrainType`、燃料和持续状态不进入快照。 | 可复用 `HexCoord` 的相邻判定及现有空间事实，不在本卡改写空间查询或给它附加火势状态。固定走火链由本单项场景的局部状态逐格核验，不能假称现有 Board 已具备燃料传播。 |
| `CombatEntryAdapter.TryCreateSession` → `CombatSession` → `EncounterCoordinator` | 入口当前新建半径 12 的空格盘；`CombatSession.ValidateCommand` 仅接受普攻、术法、神通、防御、待机、移动和换术；`EncounterCoordinator.Complete` 只清理当前双单位表现并回调 Adventure。 | 没有火种、地表状态、组合账本或金丹装配入口。向这条正式 Adventure／Combat 链硬塞候选行为会跨越 `U-JD-RULE-01A` 的冻结边界，故本单项不得修改这些文件、正式 `AdventureScene`、`JindanStaticStates.csv` 或 `JindanStaticStateData`。 |
| 正式场景与表现 | `AdventureSceneInstaller` 绑定正式目录、环境档案、攻击档案、`EncounterCoordinator` 和单一静态 3D 表现；`AdventureSceneBuilder.BuildGuanzhongBattlefield` 只按现有环境边端点摆放关中地块。 | 正式场景不是未完成金丹组合的测试替身。新场景必须独立、非 BuildSettings、无 `GameBootstrap`、无 `AdventureSceneInstaller`，且不读取／写入正式角色、战斗、存档或内容目录。 |

因此，现有代码的可复用边界是坐标相邻、场景构建和 PlayMode／EditMode 测试约定；缺口是候选专属的火源、路线、阻断、账本、容量和可见地表结果。该缺口只能由一个隔离的单项场景切片承接，不能用“已有空间模块”或“已有环境卡”冒充运行时支持。

### 2. `U-JD-COMBO-FIRE-SCENE-01` 的冻结实现边界

- 新增的唯一运行时所有者是 `src/Assets/Scripts/Modules/Features/Adventure/JindanFireFerryLineScenarioController.cs`。它只服务 `JD_COMBO_FIRE_FERRY_LINE_01_SCENE_FIXTURE`：维护源位火源、燃犀渡阵眼、固定相邻燃料链、局部费用／容量账本、一个地表状态链和对应可见标记；不注册正式 `comboProfileId`、`fireSeedProfileId`、`resultPrototypeId` 或金丹装配。
- `src/Assets/Scripts/Editor/JindanFireFerryLineScenarioSceneBuilder.cs` 唯一创建／重建 `src/Assets/Tests/Scenes/JindanFireFerryLineScenario.unity`。该场景只含 `JindanFireFerryLineScenarioRoot`、控制器、测试用阵眼／地格／火焰标记和状态文本；不修改 `EditorBuildSettings.asset`，不接入 `AdventureScene`，不创建新美术资源。现有 `CombatPiece2DExperimentSceneBuilder` 的非 BuildSettings、可重复构建模式是唯一场景构建参考。
- 场景 Builder 序列化候选测试输入：唯一 `fixtureId`、源格、阵眼、固定燃料格序列、初始行动／灵力／燃料／火势预算、地表／环境容量，以及水、隔绝、断燃料和阵眼失效的受控反例开关。它们只用于证明支付顺序、原子失败、单一承载和释放；不写入 CSV、ScriptableObject 正式内容或数值平衡结论。
- 控制器先完整预览源位行动、灵力、燃料、地表容量与源格空位，全部满足时才一次性建立 `sourceRef`。随后完整预览阵眼、仍燃烧的 `sourceRef`、相邻连续可燃路线、逐节点火势预算和环境容量；任一条件不足时不扣本次走火费用、不生成半条链，既有独立源火按其原账本保留。成功时仅建立一条可见、可切断的地表状态链，并把两位置的持续承载和容量记为同一场景实例，禁止免费传播、重复收费或第二条链。
- 控制器的结束入口以及 `OnDisable`／场景卸载清理未来作用、标记和地表／环境容量；已经结算的测试费用不回滚。水、隔绝、拆燃料、扑灭源火、毁阵眼、资源／容量不足和战斗结束都必须走同一 `END` 语义，不寻找 fallback。

### 3. 字面量路径、验证和下游边界

`U-JD-COMBO-FIRE-SCENE-01` 只能修改以下 Unity 业务路径及其新建文件的 `.meta`：

| 用途 | 精确路径 |
|---|---|
| 单项状态、账本和可见结果所有者 | `src/Assets/Scripts/Modules/Features/Adventure/JindanFireFerryLineScenarioController.cs`、`src/Assets/Scripts/Modules/Features/Adventure/JindanFireFerryLineScenarioController.cs.meta` |
| 非正式场景 Builder | `src/Assets/Scripts/Editor/JindanFireFerryLineScenarioSceneBuilder.cs`、`src/Assets/Scripts/Editor/JindanFireFerryLineScenarioSceneBuilder.cs.meta` |
| 单项可操作场景 | `src/Assets/Tests/Scenes/JindanFireFerryLineScenario.unity`、`src/Assets/Tests/Scenes/JindanFireFerryLineScenario.unity.meta` |
| Builder／序列化隔离检查 | `src/Assets/Tests/EditMode/JindanFireFerryLineScenarioEditorTests.cs`、`src/Assets/Tests/EditMode/JindanFireFerryLineScenarioEditorTests.cs.meta` |
| 运行时账本、反例和可见标记检查 | `src/Assets/Tests/PlayMode/JindanFireFerryLineScenarioPlayModeTests.cs`、`src/Assets/Tests/PlayMode/JindanFireFerryLineScenarioPlayModeTests.cs.meta` |

| 验收项 | 直接证据 |
|---|---|
| 正常链 | PlayMode 以 Builder 同源的 fixture 输入依次建立源火、走完唯一连续链，核对每项费用只扣一次、地表／环境容量仅占一份、源火和链标记均可见。 |
| 前置和阻断 | 缺源火、无效阵眼、水／隔绝、断燃料、非相邻格、容量不足或预算不足必须返回稳定失败原因；本次走火前后账本、标记和容量相同。 |
| 生命周期 | 位置／阵眼失效和场景结束经同一 `END` 清理标记与容量，不撤回既有结算，也不留下半状态。 |
| 场景隔离 | EditMode 重建两次仍只有一个控制器和规定标记，正式四个 BuildSettings 场景不变，场景中不存在 `GameBootstrap` 或 `AdventureSceneInstaller`。 |

U 卡完成后，`N-JD-COMBO-FIRE-BUDGET-01` 只读取这一场景、同版本代码、Builder 和两类直接测试，写入实际运行记录 `docs/基础设定/金丹组合/前置/JD_COMBO_FIRE_FERRY_LINE_费用容量验证.txt`。N 卡不补建实现、不将 fixture 值称为正式预算，也不要求 BattleSim 复刻该场景；正式组合生产仍由 `D-JD-COMBO-SCOPE-01` 与 `C-JD-COMBO-01` 的既有链负责。
