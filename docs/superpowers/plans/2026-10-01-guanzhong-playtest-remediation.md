# 关中2026-10-01实测反馈修复任务计划

## 本轮事实与授权

用户亲自从正式入口测试后指出8项缺口，并要求分为可直接修改、需方案设计、缺美术资源分别建卡，再修复重测。此计划仅规划这次反馈，既有 A-GZ-PRESENT-01 继续作为汇总父项，V-GZ-ART-SLICE-01继续负责用户最终接受；不另造父项或自动化控制面。
20张叶子已一次建立：首批7张ready，其余13张blocked；完整已知依赖见下表。当前任务卡是唯一调度事实，本文为需求与拓扑依据，状态变化不机械维护本表的历史首批状态。

2026-10-01用户反馈是未通过的真实体验证据，覆盖此前“只差用户接受”的旧说法。本轮没有修改场景、代码、模型、数值或运行游戏，不宣称任何缺口已修好；子智能体只做文件与来源调查。

## 八项反馈归属

| 用户反馈 | 已核对事实 | 处理分类与任务 |
|---|---|---|
| 1.至少30格，部分为不可通行障碍格型 | 正式Builder从环境边端点生成6格；逻辑radius12不代表可通行场地。现有tile有阻挡字段但生产档案只有边flags，没有障碍格型数据入口 | 小范围地图/格型合同 D-GZ-PLAYTEST-MAP-01 → U-GZ-PLAYTEST-MAP-01；建议36格含4障碍，无高差/可破坏系统 |
| 2.正交size4，常规战棋移动镜头 | 现有size6.2来自共享BeginScene，无正式平移控制器 | 可直接实施 U-GZ-PLAYTEST-CAMERA-01；仅Adventure覆盖，WASD/方向键/中键拖动、真实bounds钳制 |
| 3.角色相对标准地块放大1.4 | Provider会在生成/动作恢复时写scale；只改Hierarchy会丢失 | D-GZ-PLAYTEST-SCALE-01先量化持久显示层 → U-GZ-PLAYTEST-SCALE-01；地块大小不变，最终world视觉比例按现状×1.4 |
| 4.底座厚度减半，上抬0.25；设计两个通用底座让Tripo生产 | 当前是运行时Cylinder，未有底座模型；现有local中心-0.04、scale.y=0.08，必须换算世界厚度和地表支撑面 | U-GZ-PLAYTEST-BASE-FIT-01先修已有几何；另D-GZ-BASE-MODELS-01 → A-GZ-BASE-MODEL-01/02 → U-GZ-BASE-MODELS-01资源链 |
| 5.石甲兽是否错误放大100倍 | 冻结FBX内部100为单位转换、回读误差0，玩家亦有；导入globalScale=1、Prefab外根1。尚无用户运行实例额外100倍证据 | 与第3项共用D尺寸诊断；确认重复换算才修，不预设重导出/0.01补偿 |
| 6.UI与方案不符，行动轴/头顶血条缺失 | v6旧接入合同把CT行动轴/移动列为网页fixture；正式HUD主要三段文字。CTBEngine存在，但无只读顺序DTO | D-GZ-PLAYTEST-UI-AUDIT-01逐项对照 → U-GZ-PLAYTEST-TIMELINE-01、U-GZ-PLAYTEST-HP-01；技能和移动按钮分别归各功能卡 |
| 7.移动、位移动画、可达高光缺失 | RequestMove/Spatial路径已有；协调器丢MovementPath只发端点，provider瞬移；Move还被当整次行动扣CT，和移动分阶段规则冲突 | D-GZ-PLAYTEST-MOVE-01 → U-GZ-PLAYTEST-MOVE-RULE-01 → U-GZ-PLAYTEST-MOVE-UI-01 |
| 8.补两术法、降低石甲兽，能够击杀交任务 | 建角技能清空；spawn快照未传术法；正式AttackProfiles只有普攻；HUD只取首个术法。已有Art结算，但自疗/条件效果并非自动支持 | D-GZ-PLAYTEST-SPELLS-01 → U-GZ-PLAYTEST-SPELLS-01 → N-GZ-PLAYTEST-BALANCE-01 → V-GZ-PLAYTEST-FLOW-01 |

第3与5项共用同一尺寸根因调查，不重复建诊断卡。第4项的现有几何修正不等待新模型；新模型的两款产物可以分别选型/返修，因此分为两个生产叶子。
地图只关闭边不等同正式格型；方案须冻结最小单一数据源并复用现有TacticalTileData/Spatial规则，不复制场景列表或新增通用地形系统。两术法优先读取现有早期合法项；尚未决定具体ID，不替用户暗定门派资格、削弱已有学习门槛或裁掉原术法特效。

## 完整依赖与职责

| ID | 独立结果 | 直接前置 | 初始状态 |
|---|---|---|---|
| D-GZ-PLAYTEST-MAP-01 | 关中三十格以上战场与障碍格合同 | — | ready |
| U-GZ-PLAYTEST-MAP-01 | 关中扩展战场与不可通行格型接入 | D-GZ-PLAYTEST-MAP-01 | blocked |
| U-GZ-PLAYTEST-CAMERA-01 | 关中战棋相机近景与平移操作 | — | ready |
| D-GZ-PLAYTEST-SCALE-01 | 正式棋子尺寸与石甲兽百倍缩放诊断 | — | ready |
| U-GZ-PLAYTEST-SCALE-01 | 正式双棋子视觉比例修正到现状一点四倍 | D-GZ-PLAYTEST-SCALE-01 | blocked |
| U-GZ-PLAYTEST-BASE-FIT-01 | 现有棋子底座减薄与地面接触修正 | U-GZ-PLAYTEST-SCALE-01 | blocked |
| D-GZ-BASE-MODELS-01 | 两个通用棋子底座造型与生产方案 | — | ready |
| A-GZ-BASE-MODEL-01 | Tripo通用底座第1款资源生产与清理 | D-GZ-BASE-MODELS-01 | blocked |
| A-GZ-BASE-MODEL-02 | Tripo通用底座第2款资源生产与清理 | D-GZ-BASE-MODELS-01 | blocked |
| U-GZ-BASE-MODELS-01 | 双通用底座模型正式接入 | A-GZ-BASE-MODEL-01、A-GZ-BASE-MODEL-02、U-GZ-PLAYTEST-BASE-FIT-01 | blocked |
| D-GZ-PLAYTEST-UI-AUDIT-01 | 正式战斗UI逐项对照与缺口归属 | — | ready |
| U-GZ-PLAYTEST-TIMELINE-01 | 正式战斗行动轴与真实CT状态显示 | D-GZ-PLAYTEST-UI-AUDIT-01、U-GZ-PLAYTEST-MOVE-RULE-01 | blocked |
| U-GZ-PLAYTEST-HP-01 | 每单位头顶血条与资源信息接线 | D-GZ-PLAYTEST-UI-AUDIT-01、U-GZ-PLAYTEST-SCALE-01 | blocked |
| D-GZ-PLAYTEST-MOVE-01 | 战棋移动操作与行动阶段方案 | — | ready |
| U-GZ-PLAYTEST-MOVE-RULE-01 | 正式移动命令与CT行动阶段闭环 | D-GZ-PLAYTEST-MOVE-01、U-GZ-PLAYTEST-MAP-01 | blocked |
| U-GZ-PLAYTEST-MOVE-UI-01 | 移动输入可达高光路径预览与棋子位移动画 | U-GZ-PLAYTEST-MOVE-RULE-01、U-GZ-PLAYTEST-CAMERA-01、U-GZ-PLAYTEST-BASE-FIT-01 | blocked |
| D-GZ-PLAYTEST-SPELLS-01 | 关中初始两术法选择与获得装配合同 | — | ready |
| N-GZ-PLAYTEST-BALANCE-01 | 关中首战石甲兽削弱与双术法数值验证 | U-GZ-PLAYTEST-SPELLS-01 | blocked |
| U-GZ-PLAYTEST-SPELLS-01 | 两术法从建角装配到正式施法完整接入 | D-GZ-PLAYTEST-SPELLS-01 | blocked |
| V-GZ-PLAYTEST-FLOW-01 | 关中修复后可玩流程独立复验 | U-GZ-PLAYTEST-MAP-01、U-GZ-PLAYTEST-CAMERA-01、U-GZ-PLAYTEST-BASE-FIT-01、U-GZ-PLAYTEST-TIMELINE-01、U-GZ-PLAYTEST-HP-01、U-GZ-PLAYTEST-MOVE-UI-01、U-GZ-PLAYTEST-SPELLS-01、N-GZ-PLAYTEST-BALANCE-01 | blocked |

所有箭头均无环。数值任务使用已实际接入的两术法参数，故U术法先于N数值，不能建立反向依赖。U术法完成只表示功能可用，普通角色稳定可胜由N数值与V流程检验证明。
V-GZ-PLAYTEST-FLOW-01与U-GZ-BASE-MODELS-01完成后，V-GZ-ART-SLICE-01仍须用户在同版本正式入口重测并明确接受，才允许关闭A-GZ-PRESENT-01及原表现汇总。正式底座生产不阻止先交付修复功能的可玩版本。

## 首批调度与模型

新增卡均P1、Codex主责；旧队列项目保留原route/owner/相对顺序，新增项按既有优先级及下游解锁量插入。首批顺序：
1. D-GZ-PLAYTEST-SCALE-01 — 正式棋子尺寸与石甲兽百倍缩放诊断
2. D-GZ-PLAYTEST-MAP-01 — 关中三十格以上战场与障碍格合同
3. D-GZ-PLAYTEST-MOVE-01 — 战棋移动操作与行动阶段方案
4. D-GZ-BASE-MODELS-01 — 两个通用棋子底座造型与生产方案
5. D-GZ-PLAYTEST-SPELLS-01 — 关中初始两术法选择与获得装配合同
6. D-GZ-PLAYTEST-UI-AUDIT-01 — 正式战斗UI逐项对照与缺口归属
7. U-GZ-PLAYTEST-CAMERA-01 — 关中战棋相机近景与平移操作

- GPT-6.1 Sol（gpt-6.1-sol）已纳入本批：有界实现推荐medium，复杂诊断/技术合同推荐high。真正的规则冲突或高风险语义独立核查才选择gpt-6-astra/high；简单机械任务可沿用入口默认模型。
- 本轮三个只读证据面实际请求gpt-6.1-sol/high：地图/UI、模型尺度、术法/数值；会话分别为 /root/map_ui_audit、/root/model_asset_audit、/root/spell_balance_audit。返回证据由主责统一核对，没有独立写入或candidate。
- 任务卡模型正文是执行建议与辅助选择，不会切换已经启动的主模型。主模型按入口实际参数记录；本轮不改定时器配置，不把写了模型名当作已切换。
- 设计/诊断前置完成后，只更新已建立的直接子卡：所有者、实际字面量路径、数据/资源输入、.meta、测试/可见验收和风险投影齐全，且不越过停止条件才ready。不能只消除blockedBy就让缺路径的卡开工。
- 共享Builder/Provider/Encounter等路径的任务在同一Codex队列依次执行，后执行者读取最新结果；不为纯文件相交制造虚假的业务依赖。子智能体默认只读。

## 验收与美术边界

- 直接功能：相机、视觉尺度、现有底座落地、时间轴、血条、移动输入和逐格动画，用现有素材即可开始。移动使用根节点表现，不前置新骨骼/行走动画采购。
- 明确缺资源：两个可复用底座模型。D卡交付具体造型、尺寸/原点/顶底面、平台输入和费用方案；每个A卡只生产自己的一款。角色历史Tripo批次100credits授权与本批无关，不能借用；具体方案和报价具备时才处理必要授权，不在建卡阶段问空泛预算。
- UI审计若证明具体语义槽确实缺素材，应在排查记录明确名称/尺寸/来源及影响并交规划；本轮不预购全套新UI、障碍或技能特效。普通底板、字体和头像优先复用现有正式资源。
- 用户的size4、视觉1.4、厚度减半、上抬0.25均进入任务合同；不得无视这些值，也不得跳过层级/支撑面量化后在多个父节点重复补偿。
- 技术验收使用正式StartMenu→小族新角色→悬赏→战斗→领奖→保存/加载。禁止测试脚本灌高属性、直接伤害/击杀、跳过鼠标操作代替用户流程。
- 场景功能在Unity验证。只有敌人/术法角色数值平衡运行BattleSim；不让模拟器复制相机、地形显示、点击输入、具体场景传播。
- 本轮规划检查：任务schema2预检/投影、审核文本、docs数据链、限定路径空白与cached diff。无代码或数据变化，不跑Unity/BattleSim。

## 已核对的关键事实入口

- AdventureSceneBuilder.BuildGuanzhongBattlefield、EnvironmentProfiles.csv、CombatEntryAdapter、SpatialQueryBoardFactory与Adventures.csv部署数据。
- AdventureScene正交size6.2及SceneBuildSupport.BeginScene；只允许局部覆盖以保护其他正式场景。
- Static3DCombatUnitPresentationProvider.Spawn/ApplyPresentation/CreateFactionBase；两个正式Prefab/importer；石甲兽v2静态3D清理QA与Profile映射QA。
- 2026-09-20-guanzhong-ui-v6-unity-integration-contract.md第3节；战斗系统行动轴与移动规则；CombatTurnScheduler/CTBEngine/CombatCommandService及EncounterCoordinator。
- CharacterCreationRules、AdventureUnitSpawner.CreatePlayer、AttackProfiles.csv、CombatHudPresenter/CombatActionBarView/CombatCommandInput。
- 术法设计卡还须解决具体候选的docs/CSV冲突与合法授予：例如玄水咒灵根要求、流火灵符伤害类型有已发现不一致。本计划未选择候选、未给出平衡值、未改这些事实源。
