# 赤野燎原：隔离 Unity 场景实施与独立验收合同

日期：2026-10-15。设计责任：D-JD-COMBO-FIRE-DOMAIN-01。状态：冻结实施输入；尚未实现、运行或证明平衡。

## 1. 唯一结果、依据与依赖

交付燃心灯 FIRE_MANIFEST 源版「燃烧地格」＋不熄炉 FIRE_ESCALATE 界版「固定走火」的一项隔离、可操作、非正式 Unity 场景。U 实现并直接测试；N 在 U 正式版本上独立复验；正式生产仍由原有范围收口与内容生产链负责。

依据缩写仅用于下文逐条回指：

- R：`docs/superpowers/specs/2026-10-14-jindan-fire-domain-redesign.md` 完整已批准稿，尤其「玩家看到的战斗过程」「两个位置分别贡献什么」「启动、维护与结束」「设计验收目标」。
- S：`docs/基础设定/金丹位格/源位/JD_FiveElements_Fire_SOURCE_燃心灯.txt#八、战棋玩法` 的 8.1，以及「【位格附性】」。
- F：`docs/基础设定/金丹位格/界位/JD_FiveElements_Fire_DOMAIN01_不熄炉.txt#七、搭配与边界`、「【位格附性】」及 8.3。
- A：`docs/基础设定/金丹基础效果装配与冲突规则.txt#一、每位格一项效果与切换事务`（含附性）、二、三。
- 入口线索为 `docs/基础设定/设定事实速查.txt#二、金丹结构（17 道路 · 51 位置 → 引用索引）`；其渡火引线指针是旧对象，不能替代上述新版原文。

固定依赖：D-JD-COMBO-FIRE-DOMAIN-01 → U-JD-COMBO-FIRE-DOMAIN-01 → N-JD-COMBO-FIRE-BUDGET-01 → D-JD-COMBO-SCOPE-01 → C-JD-COMBO-01。不存在反向等待正式配方或 BattleSim 的依赖。本轮仅准备 U/N 合同；按入口限制保留 U 的 D 前置与 N 的 U 前置，不提升下游。D 正式进入 master 后，由后续 QueueMaintenance 解锁 U。

## 2. 当前所有者证据与选择

结构入口：`UNITY_STRUCTURE.md`、`UNITY_STRUCTURE.runtime.md`、`UNITY_STRUCTURE.assemblies.md`。项目是 `src/`、Unity 6000.3.18f1。Adventure 模块及 Editor 的 README、实际 asmdef 均已核对；已有两类测试程序集可引用 Adventure，无需改 asmdef。

| 当前字面量路径 | 读到的实际逻辑及调用者 | 本切片如何使用 |
|---|---|---|
| `src/Assets/Scripts/Modules/Features/Adventure/JindanFireFerryLineScenarioController.cs` | `TryManifest` 建单格；`TryEscalate` 一次生成三节点固定链；`CaptureLedger` 记录局部账本；`EndScenario` 一次清空。无周期、单位生命、余火；阻断开关直接结束整链。 | 仅参考隔离生命周期和账本观察方式；不修改、继承或调用该旧 fixture。 |
| `src/Assets/Scripts/Editor/JindanFireFerryLineScenarioSceneBuilder.cs` | `Build`、`ConfigureFixture` 创建旧控制器并保存独立场景，序列化坐标、费用和标记；其直接代码调用者为旧 EditMode 测试。 | 参考 Editor 生成与重开检查方式，不调用旧 Build；禁止手写 scene YAML。 |
| `src/Assets/Tests/Scenes/JindanFireFerryLineScenario.unity` | 序列化旧控制器，脚本 GUID 为 `03218d61165d9f343bb4fec87892ce3d`；对该 GUID 的 Assets 检索只命中此场景及脚本 meta。 | 保留旧场景原样，不重命名或移植证据。 |
| `src/Assets/Tests/EditMode/JindanFireFerryLineScenarioEditorTests.cs` | 重建两次、重开、核对标记与四个正式 BuildSettings 场景。 | 新测试独立验证新资源与新 GUID。 |
| `src/Assets/Tests/PlayMode/JindanFireFerryLineScenarioPlayModeTests.cs` | `CreateRuntimeFixture` 创建内存空物体，并未加载持久化旧 scene；直接调用旧控制器操作。 | 新测试必须加载新 scene，不能复制该内存替身作为场景证据。 |

对旧类型名的 `src/Assets` 检索所见直接引用仅为上述 Builder、两个直接测试和旧 scene；正式入口未引用它。旧范围稿 `docs/superpowers/specs/2026-09-20-jindan-combo-scope-design.md#六、渡火引线 Unity 单项场景实现与验收范围合同` 只作边界比较，不能批准新版预算或验收。

新切片仍归现有 `TianZhang.Features.Adventure` 隔离测试职责。单一规则所有者 `JindanFireDomainScenarioState` 负责格状态、确定性计划、费用、实例、单位生命与结束；`JindanFireDomainScenarioController` 只把序列化配置和场景操作交给该状态，并投影真实结果。两者同模块，无新生产服务或跨模块桥。规则不复制进 Builder、UI 或测试；渲染不决定伤害或传播。配置类型、快照与账本类型可同文件定义，不另建全局系统。

新直接链：新 Builder → 新 scene 的配置、格对象、单位对象、控制器引用 → 控制器操作 → 同一个 State 的预览/提交 → 格燃烧预算、单位 HP、实例/容量账本 → 标记、Transform 和状态面板。运行时调用者只限该 scene 和直接测试；正式 Adventure、CombatSession、Bootstrap、内容导入及存档不接入。不是玩家正式金丹功能或正式配方登记。

## 3. U 完整业务路径（每项均为新增）

以下 12 个 Unity 文件逐一授权，既有目录已存在，不新增目录 meta。U 卡另列自己的活跃/归档卡、场景任务列表、队列四个生命周期路径。

| 用途 | 精确仓库相对路径 |
|---|---|
| 唯一局部规则状态 | `src/Assets/Scripts/Modules/Features/Adventure/JindanFireDomainScenarioState.cs` |
| 对应 meta | `src/Assets/Scripts/Modules/Features/Adventure/JindanFireDomainScenarioState.cs.meta` |
| 场景控制/投影 | `src/Assets/Scripts/Modules/Features/Adventure/JindanFireDomainScenarioController.cs` |
| 对应 meta | `src/Assets/Scripts/Modules/Features/Adventure/JindanFireDomainScenarioController.cs.meta` |
| 唯一场景生成入口 | `src/Assets/Scripts/Editor/JindanFireDomainScenarioSceneBuilder.cs` |
| 对应 meta | `src/Assets/Scripts/Editor/JindanFireDomainScenarioSceneBuilder.cs.meta` |
| 非 BuildSettings 场景 | `src/Assets/Tests/Scenes/JindanFireDomainScenario.unity` |
| 对应 meta | `src/Assets/Tests/Scenes/JindanFireDomainScenario.unity.meta` |
| EditMode 直接测试 | `src/Assets/Tests/EditMode/JindanFireDomainScenarioEditorTests.cs` |
| 对应 meta | `src/Assets/Tests/EditMode/JindanFireDomainScenarioEditorTests.cs.meta` |
| PlayMode 场景直接测试 | `src/Assets/Tests/PlayMode/JindanFireDomainScenarioPlayModeTests.cs` |
| 对应 meta | `src/Assets/Tests/PlayMode/JindanFireDomainScenarioPlayModeTests.cs.meta` |

持久化 U 证据仅写：

- `docs/superpowers/specs/2026-10-14-jindan-fire-domain-implementation-evidence.md`
- `docs/superpowers/evidence/2026-10-14-jindan-fire-domain-opening.png`
- `docs/superpowers/evidence/2026-10-14-jindan-fire-domain-expansion.png`
- `docs/superpowers/evidence/2026-10-14-jindan-fire-domain-gap.png`
- `docs/superpowers/evidence/2026-10-14-jindan-fire-domain-ending.png`

N 的唯一业务文本为 `docs/superpowers/specs/2026-10-14-jindan-fire-domain-verification.md`；独立场景截图为 `docs/superpowers/evidence/2026-10-14-jindan-fire-domain-independent.png`。N 不得写 U 源码、场景或测试。证据索引由各卡完成归档的「验证结果」链接上述记录，场景任务列表只承担未完成投影；不扩写基础设定 L1 或正式配方。

不得改 `src/ProjectSettings/EditorBuildSettings.asset`、任何正式 scene/prefab、asmdef、CSV、ScriptableObject 内容、旧渡火引线文件、BattleSim 或本合同。缺新路径/所有者时停回准备，不能边实现边扩卡。

## 4. 固定功能测试档案与可见对象

以下全是非生产测试配置，按 scene 序列化，State 从配置读取，不能硬编码成正式数值。`fixtureId=JD_COMBO_FIRE_DOMAIN_01_SCENE_FIXTURE`，版本 1；本地候选配方必须完整声明两项效果、位置/位别、媒介、地表结果、成本、容量与 `breakPolicy=END`，只在 fixture 内可见；缺字段/身份不符拒绝，禁止注册正式 comboProfileId 或借旧周天焰档案。

| 输入 | 冻结值/约束 |
|---|---|
| 拓扑 | 轴坐标六邻接，`max(abs(q),abs(r),abs(q+r))<=2` 的 19 格；初始火场 `(-1,0),(0,0),(1,0)`，落点 `(0,0)`，源位普通距离上限 2。中心长明之根 `(0,1)`、HP=10，是独立可受攻击对象，不占地表燃烧名额。 |
| 燃料 | 默认每格真实燃料库存 12；每次点燃把该格 4 单位库存转入该次燃烧的已付预算，普通燃烧剩余周期=4；每周期消耗 1 已付单位，不能自动续满。扑灭/水/拆燃料取消剩余已付预算，不退款；未付库存不能冒充已付燃烧。 |
| 传播 | 每周期最多 2 节点；覆盖边界半径 2；最多实际到达 7 个不同地格（含初始 3 格），不是每回合新增 7 格。已到达且被扑灭的格可再次付费点燃，不增加不同格计数；同时地表容量仍逐格检查。 |
| 起始资源 | 行动=6，灵力=60；源火传播预算=8，由本次已付源位显化生成并绑定 sourceRef；每次后续点燃消耗 1，不被维护补回；预算归零不能传播。 |
| 分阶段启动 | 有自然支点时显化行动 1、灵力 6、3 格各燃料 4、地表容量各 1；开域另付行动 1、灵力 4，并为实际接管的 3 个火格各预付余火灵力 1。默认启动后行动=4、灵力=47、地表占用=3、环境占用=1、传播预算仍为 8。 |
| 无自然支点变体 | 显化额外支付行动 1、灵力 2，以配置声明的主动补足生成合法支点；默认库存仍须存在。完成两阶段后行动=3、灵力=45；缺燃料或补足费用不足则显化前失败。补支点不能生成免费燃料。 |
| 周期费用 | 维护灵力 2；每个实际后续点燃另付扩张灵力 2＋该次余火预付灵力 1，另消耗传播预算 1 和该格燃料 4。周期不额外消耗行动（档案选择）；维护、扩张、余火三项分账。 |
| 容量/承载 | 地表总容量=7、环境总容量=1。每个活跃燃烧/余火地格恰有一份地表登记；领域一份环境登记。源火独立阶段占源位一次承载；组合后原子接管为同一组合实例，两位置各映射该实例，不能保留第二个独立源位实例。 |
| 灼烧 | 普通燃烧进入伤害 4、周期停留伤害 2；余火进入/停留伤害各 1、最多 2 周期。同一格同次事件只结算一次，不叠普通火＋余火两份伤害。所有测试单位 HP=30、火抗=0，友/敌/中立均受真实地表影响。 |
| 附性边界 | 本切片测试单位均为无魂测试载体，魂伤=0；不实现神魂战斗通道，不把源席附性复制给领域/余火。正式有魂敌人的独立有限魂伤仍受 S 原文约束，本切片不声称验证该通道。 |
| 余火 | 固定载体为燃烧地表，预付 1 灵力对应该次 2 周期弱尾效；不另创建对象灼烧状态。只允许已经被领域实际接管/到达的合法地格；同源普通燃烧预算耗尽后转为一份余火，不能叠加、传播或触发下一份余火。显式扑灭则立即清空该次普通火/余火及尾效资格。 |
| 重置 | 只在显式测试重置时恢复初始配置；不是战斗中的免费恢复操作。主效果结束不重置账本，战斗结束也不退款。 |

场景根 `JindanFireDomainScenarioRoot` 下持久化唯一控制器、`FixtureGrid` 的 19 个按坐标命名格对象及对应火/余火投影、`DomainCenter`、三个测试单位、相机、单一 fixture Canvas 和 Input System EventSystem。可燃/水/隔绝/无燃料、当前火/余火/熄灭格、初始预览、最大边界必须可区分。局部对象实际 HP、坐标、燃料及预付预算由 State 唯一拥有，Transform/可见状态随成功结算刷新，不能用独立演示动画冒充结果。

面板提供「预览/显化/开域/下一周期/选格扑灭/水/隔绝/拆燃料/选单位相邻移动/攻击中心/关闭/源位退出/界位退出/战斗结束/重置」操作；测试通过同一控制器入口调用，不另写规则。未指明的生产技能/灭火费用不在本切片实现；反制是标明用途的场景测试输入，不声明正式免费行动。默认单位：友 `(0,1)`、敌 `(1,1)`、中立 `(-2,2)`；不在初始火场上。

## 5. 同一确定性计划、支付与结束顺序

依据 R「启动、维护与结束」、S 8.1、F 8.3、A 一至三；以下排序/数值只是落实公开固定规则的测试档案。

1. 预览纯读取当前对象快照，不扣费、不推进周期、不伤害单位。State 的同一计划计算供预览与提交使用；提交重验当前版本，快照有变则重新计算并展示，不执行旧计划，不静默替换目标。
2. 显化校验三格连通、距离、燃料/载体空位、行动、灵力和地表容量后一次性提交；非法落点、非相邻初始格或缺任一条件都不生成半片火。开域另验有效中心、仍燃烧合法 sourceRef、两位置空位、环境容量及全部展开/余火预算。后阶段失败只保留已付源火，不退前阶段费用，不扣后阶段费用。
3. 周期只由「下一周期」产生一次离散步进，不按帧扩张。先检查中心/位置/领域、仍有效的普通火源、燃料及边界，再计划维护与实际扩张。水、隔绝、无燃料格不作为来源或候选；候选仅来自周期开始时普通燃烧格的六邻接，本周期新火不能再作为来源连跳。
4. 候选去重，先排列已到达后被扑灭的合法格，再按 `r` 升序、`q` 升序；只用空间/燃料等公开信息，不读取敌方位置来临场择优。按每周期上限、不同格覆盖上限、空余地表容量、传播预算及维护后可支付的完整单节点费用取确定性前缀。不能跳过前缀中昂贵节点临时改选；每个提交节点的各项费用同时支付，未支付节点不生成。
5. 维护不足：本周期维护与扩张均不扣、不新增；维护足而扩张不足：只扣维护，不扣扩张/余火/传播预算/未点燃燃料。两种情况下旧火仍消耗其已付剩余预算。无合法火源时不扣维护、不扩张；余火不是合法传播来源。无候选或到达面积上限但仍有普通火源时，维护仍扣 2，扩张扣 0，不延长旧火。
6. 成功周期先提交计划，然后对当前火格上的单位各结算一次停留伤害；周期开始时已有的普通火/余火各消耗一周期，新点燃格保留完整 4 周期，新转换的余火保留完整 2 周期。从当前格进入相邻燃烧格另结算一次进入伤害，空地不伤害。火不改变地格可通行性，不强制位移；格上有单位也不免费阻挡火。阵营选择固定为全部，不赠友方免疫。
7. 扑灭立即使该格停止伤害、释放其地表容量、取消该次尾效；同一周期反复预览、刷新、移动不能复燃。仅下一周期仍有相邻普通火源、合法燃料且完整付费才可复燃。水/隔绝/拆燃料也即时取消该格当前火与尾效，并阻断后续传播，不把单格反制变成整域无差别清空。
8. 中心 HP 到 0、主动关闭、任一位置退出立即结束主组合：两位置承载和环境容量释放，未来维护/扩张停止；不拆出免费独立领域。已付普通火按剩余预算衰减，到该段结束时在合法且实际到达处使用已付尾效；同地表登记转余火不再占第二份容量，不再收同一份余火费。此后最多再步进 6 周期所有余火清空，已付费用/伤害不倒放。
9. 战斗结束、禁用控制器、卸载 scene 直接清空普通火、余火、引用及所有容量/承载；重复调用幂等且不退款，结束后全部施放/周期请求拒绝。主效果关闭与战斗结束有不同遗留，不能照搬旧 `EndScenario` 一次清空语义。

## 6. 逐项验收矩阵（U 实现与直接测试，N 独立重放）

每行独立从重置配置开始，未指明改动使用 §4 默认值。控制器必须能捕获只读快照：cycle、sourceRef、comboInstanceId、两位置到实例映射、centerHP、格坐标/燃料/普通剩余/余火剩余/尾效资格/地表登记、到达集合、维护/扩张/余火/显化/展开累计实扣、行动/灵力/传播预算余额、环境与地表可用/占用、单位 ID/阵营/HP/坐标、失败原因。记录均来自实际运行对象；日志与截图不可替代数字断言。

| ID / 依据 | 初始值和动作 | 必须断言的实际结果及运行入口 |
|---|---|---|
| E01 / 代码结构、R 验收 | 新 Builder `Build` 两次并重开保存 scene。 | EditMode `JindanFireDomainScenarioEditorTests`：唯一控制器、19 格、三个单位、所有配置/引用及新脚本 meta GUID 可解析；四个正式 BuildSettings 前后相同且不含新 scene，无 GameBootstrap/AdventureSceneInstaller/旧控制器；持久化值与 §4 一致。 |
| E02 / R 启动、S 8.1、A 二 | 分别预览显化/开域两次，再逐步提交；另测无自然支点变体。 | EditMode 同一计划确定性与预览无副作用；PlayMode 加载 scene 后三格立刻连通可见，默认余额行动 4/灵力 47、表面 3/7、环境 1/1、单组合/两承载。无自然支点版本余额 3/45，不凭空加燃料。 |
| E03 / R 周期、F 8.3 | 正常启动，推进第 1、2、3 周期，逐步比较预览计划与提交。 | 第 1 周期新增 `(-1,-1),(0,-1)`，灵力 39、传播预算 6、覆盖 5；第 2 新增 `(0,-2),(1,-2)`，灵力 31、预算 4、覆盖 7；第 3 无新增，灵力 29、预算仍 4。第 1/2 周期维护各 2、扩张各 4、余火各 2；初始燃料累计实付 12，前两周期各另付 8；没有同周期连跳。 |
| E04 / R 封路与灼烧、S 8.1 | 启动后友方从 `(0,1)` 经 `(0,0),(1,0)` 到 `(1,1)`，每一步均无占位冲突时操作；为此先把敌方相邻移开。另重置测友/敌/中立进入火格并停留一周期及全程绕行。 | PlayMode：穿过两格 HP 30→22，火格仍可通行；单次进入 HP 30→26，停留后 24；各阵营同值，绕行 HP=30。位置/Transform、实际 HP 与面板一致，刷新/预览不重复扣血。单位占位冲突需拒绝且不扣血；中心标记不作为战斗单位占位。 |
| E05 / R 扑灭、F 反制 | 正常第 1 周期后扑灭 `(-1,-1)`，同周期移动穿过缺口、重复预览；再推进一周期。另测维护后灵力只剩 2 的复燃反例。 | 立即标记灭、地表容量减 1、穿越无伤害；本周期不复燃。下周期合法时该格优先入计划，实付扩张 2＋余火 1、燃料 4、预算 1，才恢复一份地表登记；钱不足则保持缺口、扩张实扣 0。 |
| E06 / R 障碍、F 连续载体 | 对尚未点燃格分别设置水、隔绝、燃料=0；另用受控变体让唯一可燃远格 `(2,0)` 与起火 `(0,0)` 中间 `(1,0)` 断燃料，其他格均不可燃。再在活跃火格上施加三类反制。 | EditMode/PlayMode：无非法候选或远点跳燃，无相关燃料/扩张支出；正常其余合法边缘仍可推进。活跃被反制格即刻熄灭且不留余火，局部开路，不免费跨越断裂，不无故清空其他格。变体仅改输入，仍用同一规则。 |
| E07 / R 上限、F 预算 | 正常两周期至不同格覆盖 7；另测每周期上限 2、传播预算=0、表面容量已满，以及恰好只够一个节点的变体。 | 覆盖/传播/容量分别限流；无可支付节点时不新增、不扣节点费；一个完整节点可支付时只提交确定性首节点及其 2＋1/燃料 4/预算 1，不能部分扣第二节点。到达集合不因扑灭缩小后继续向新格无限铺开。 |
| E08 / R 分账、A 原子性 | 显化前逐项令行动、灵力、某格燃料或表面容量不足；显化后再令开域行动、灵力、中心、界位承载或环境容量不满足。 | 对应阶段支付前失败，稳定原因且该阶段完整快照不变；后阶段失败保留已付三格源火、源位承载及显化费用，无 combo/env 登记、无展开/余火费；已付源火仍有限衰减。缺档案字段、非法距离/初始非连通同样失败。 |
| E09 / R 维护与扩张 | 启动后灵力=1；另测灵力=4（维护后仅 2，不够单节点 3）；另测没有普通火源仅余火。 | 前者维护/扩张均 0，后者维护 2/扩张及余火 0；两者旧火 4→3，非冻结或免费续期。仅余火不维护、不传播。不得直接设置“失败=true”跳过真实余额检查。 |
| E10 / R 单实例、A 一二 | 成功组合后重复显化、开域，尝试让任一位置另占独立实例或第二组合；再结束主效果。 | 真实位置映射都是同一 comboInstanceId，activeComboCount=1，无独立源实例；重复请求/占用冲突不扣费。退出后 combo=0、两承载=0、环境=0；仍燃烧地格的容量未提前归零。 |
| E11 / R 中心与结束、F 附性 | 正常开域后攻击中心 10、主动关闭、源位退出、界位退出，分别重置测四条入口；继续步进至最多 6 次。 | 立即停止新增/扣维护，不生成独立领域；既有普通预算逐步耗尽，同一格转弱余火 2→1→0，各格只一份登记，实际到达外绝不生余火。普通期/余火期单位伤害分别为 2/1，魂伤始终 0；余火不扩张不触发自身；最终容量全释放。 |
| E12 / R 战斗结束、S/F 遗留 | 普通燃烧与余火阶段分别调用战斗结束两次，另测禁用/场景卸载。 | PlayMode：全部火/尾效/来源/承载/容量清零、单位历史 HP 与已扣资源不回滚；结束后不能再扣费或生成格。卸载恢复先前 active scene，不留 fixture 对象、订阅或测试状态。 |
| E13 / R 预览与场景 | 保存 scene 实际加载，操作上述正常→扩张→扑灭→结束，截取四张 Game view；改变输入后检查旧预览。 | PlayMode 同版本对象/标记/HP/费用与快照对应；旧快照变更后不静默执行原计划，重新预览一致。空物体替身、仅场景 YAML、截图或旧测试通过均不算此项完成。 |

E04 每个独立用例先安排相邻无占位冲突路径；敌方可先从 `(1,1)` 移至 `(2,0)`，不引入穿人或强制位移规则。反例配置通过重置时提供局部配置副本/序列化输入，运行操作不能偷偷改初始正式配置，测试结束恢复场景设置。

## 7. 可运行入口、命令、证据与恢复

当前已存在：`tools/run-unity-editmode-tests.ps1`、`tools/run-unity-playmode-tests.ps1`；默认 Unity 可执行路径 `C:\Program Files\Unity\Hub\Editor\6000.3.18f1\Editor\Unity.exe`，D静态核对文件存在，执行时仍核实。前者支持 ProjectPath/ResultXmlPath，不支持 TestFilter，实际日志写系统Temp下的tzg-editmode-test-runner.log，须在该次成功返回后立即复制到当前worktree证据目录；后者额外支持 TestFilter/EnableGraphics，日志与ResultXmlPath同名改为.log。两者校验非零、全部通过的 XML，保护并恢复已跟踪 Assets/ProjectSettings。不能把不存在的过滤参数传给前者，不能因恢复源文件而遗漏 Builder 新生成的未跟踪资源。

新 Builder 公开 `TianZhang.Editor.JindanFireDomainScenarioSceneBuilder.Build`（菜单「天章/测试/重建赤野燎原单项场景」）。首次通过当前 worktree Unity Editor 调用生成并保存新 scene/meta，随后使用显式当前 worktree 的 `src`。以下均从当前 worktree 根运行，U 用 U、N 用 N 作为 `$phase`，不把输出送到其他 worktree：

```powershell
$phase = 'U'
$fireProject = (Resolve-Path -LiteralPath 'src').Path
$fireOutput = Join-Path $fireProject ('Temp/JindanFireDomain/' + $phase)
New-Item -ItemType Directory -Path $fireOutput -Force | Out-Null
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/run-unity-editmode-tests.ps1 -ProjectPath $fireProject -ResultXmlPath (Join-Path $fireOutput 'editmode.xml')
if ($LASTEXITCODE -ne 0) { throw 'fire_domain_editmode_failed' }
Copy-Item -LiteralPath (Join-Path ([IO.Path]::GetTempPath()) 'tzg-editmode-test-runner.log') -Destination (Join-Path $fireOutput 'editmode.log') -Force
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/run-unity-playmode-tests.ps1 -ProjectPath $fireProject -ResultXmlPath (Join-Path $fireOutput 'playmode.xml') -TestFilter 'TianZhang.Tests.PlayMode.JindanFireDomainScenarioPlayModeTests' -EnableGraphics
if ($LASTEXITCODE -ne 0) { throw 'fire_domain_playmode_failed' }
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-unity-assembly-boundaries.ps1
if ($LASTEXITCODE -ne 0) { throw 'fire_domain_assembly_boundary_failed' }
```

EditMode 包含现有完整测试，是现有脚本不可过滤的实际入口，不为本卡改测试工具。PlayMode 测试在 Editor 内通过 `UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode` 加载 `Assets/Tests/Scenes/JindanFireDomainScenario.unity`（Additive，Editor 调用由 `UNITY_EDITOR` 保护），设为 active，定位此 scene 根下唯一控制器，校验配置后才操作；不用依赖 BuildSettings 的 SceneManager.LoadScene，也不在 PlayMode 引用 Editor Builder 程序集。Teardown 恢复原 active scene 并卸载新 scene。无法加载真实场景时失败，不退回内存重建替身。

图像取证在有图形的实际 Editor PlayMode/Game view 进行；U 按 §3 固定四个 PNG 路径保存真实画面，N 保存独立 PNG。没有 Editor/图形工具时停报环境缺口，不用代码渲染的示意图代替。每个画面与当次操作、cycle 和快照绑定；最大边界、火、扑灭缺口、中心和费用面板需可辨认，不要求生产美术。

U/N 各自持久记录：实现源码/scene/meta 的 Git 版本或内容 SHA-256 清单、Unity 版本、配置身份、实际执行命令、XML/log SHA-256、逐项 E01–E13 运行结果（含反例）、前后数字与截图哈希、退出清理和测试前后 `git status --short`。测试原始 XML/log 在上述 Temp 输出；易随 worktree 清理而消失，必须把测试计数、逐项数字/失败原因和必要原始日志摘录写入持久记录，不能仅留一个 Temp 链接。U 记录尚未形成的提交不能填猜测 SHA，可用同版资产哈希；N 必须填写 U 的正式 master 提交并重新核对哈希。

恢复边界：Builder 只保存新 scene；测试不得持久化反例配置、改 BuildSettings 或正式资源。U 检查新文件和 meta 均进入授权集；N 复验前后对 U 源码/scene/meta 哈希必须一致，任何测试生成的非预期变化先查来源并恢复本轮自己造成的变化，不能覆盖既有人工改动。验证失败记录后停止，N 不修补实现、不归档为通过，也不解锁下一项。

## 8. 分工、风险门禁与完成

U 是一个场景结果，传播、预览、支付、伤害和清理由同一局部规则所有者统一复核，测试/证据随同一次交付闭合。建议 gpt-6.1-sol/high 单模型；无独立工作面，不额外派发。N 是独立执行轮次上的同版本复验，由入口默认单模型处理；不是把 U 自述当独立证据。执行时按 AI 协作规则核实实际工具能力。

D 本轮只作静态原文/路径/入口/完整 meta/依赖无环及风险预检验证；不运行 Unity/BattleSim，不声称 E01–E13 已通过。U/N 在准备/执行时分别运行经验预检，消费 mustRead、核对 schema 2 投影和所有 gate 入口。新增路径/对象/运行时所有者/技术路线变化即停写并重预检，不用此合同授权正式系统扩张。

D 完成是合同和完整 U/N 卡互相一致且可供后续维护解锁；U 完成须新场景、真实直接测试及四张同版证据全部通过；N 完成须在 U 正式版本独立重放全部矩阵及反例、费用容量观察和恢复证据。只有 N 新证据才能供 D-JD-COMBO-SCOPE-01 收口，不能重放旧 candidate。费用/容量属于 Unity 场景功能验证；角色数值平衡另按明确问题交 BattleSim，本合同没有平衡结论。

停止：已批准 R 与 L2 硬规则冲突、缺不可推导的玩法输入、需要新生产所有者/正式功能/额外文件/公共接口、规则出现两份解释、工具不足以取得场景或运行数字，均停止并具体报告。不得减少矩阵、改旧 fixture、追加兜底或扩大模拟器掩盖缺口。
