# 数据管线说明 (v2 — 文本 ID 化)

## 设计理念

生产配置以 CSV 表格为主要数据源；批准的静态目录资产仍是其字段引用的外部权威。**显示文本统一通过 Language.csv 管理**，其他 CSV 表中仅引用文本 ID。Excel 编辑 → 导出 CSV → Unity 菜单 `天章/导入全部配置` → 领域 importer 验证并生成 `.asset`。`ContentImportCoordinator` 只负责确定性顺序，不拥有领域字段、投影或提交。

## 文本 ID 系统

- `Language.csv`：唯一文本映射表，`id, zh_CN` 格式，后续可追加 `en_US` 等列
- 所有其他 CSV 的文本字段均填写 ID（如 `spell_xuanshuizhou`），导入时自动解析为中文
- ID 命名规则：`类型_拼音`，如 `gongfa_baoyuanshouyi`、`realm_lianqi`、`item_lingshi_low`

## CSV 文件结构

| 文件 | 内容 | 生成目标 |
|------|------|----------|
| `Language.csv` | 文本 ID → 中文映射 | （导入时内存解析） |
| `GongFa.csv` | 功法配置（ID化） | `Data/GongFa/GongFa_*.asset` |
| `Spells.csv` | 术法配置（ID化） | `Data/Spells/Spell_*.asset` |
| `Skills.csv` | 神通配置（ID化） | `Data/Skills/Skill_*.asset` |
| `Characters.csv` | 角色模板（ID化） | `Data/Characters/Char_*.asset` |
| `Enemies.csv` | 敌人模板与正式物种定义（ID化） | `Data/Characters/Char_Enemy_*.asset`、`Data/Enemies/Enemy_*.asset` |
| `Settlements.csv` | 正式据点投影 | `Data/Settlements/Settlement_*.asset` |
| `Items.csv` | 正式物品投影 | `Data/Items/Item_*.asset` |
| `Bounties.csv` | 正式悬赏投影 | `Data/Bounties/Bounty_*.asset` |
| `Adventures.csv` | 正式 Adventure 地图 | `Data/Adventures/AdventureMap_*.asset` |
| `AttackProfiles.csv` | 正式攻击档案 | `Data/AttackProfiles/AttackProfile_*.asset` |
| `CharacterCreationPointBuy.csv` | 角色创建点购配置 | `Resources/Data/CharacterCreation/CharacterCreationPointBuyConfig.asset` |
| `EnvironmentProfiles.csv` | 环境档案纯数据契约 | `Data/EnvironmentProfiles/EnvironmentProfile_*.asset` |
| `NpcCultivationActionWeightProfiles.csv` | NPC 修炼行动权重 | `Data/NpcCultivationActionWeightProfiles/NpcCultivationActionWeightProfile_*.asset` |
| `FoundationPurpleMansionStates.csv` | 五名演武角色的道基、紫府与修炼根状态 | `Data/FoundationPurpleMansionStates/FoundationPurpleMansionState_*.asset` |
| `JindanStaticStates.csv` | 五名演武角色的金丹静态根 | `Data/JindanStaticStates/JindanStaticState_*.asset` |
| `JindanDemoPresets/Seats/Abilities/Commands/Contracts/LoadoutTuning/Segments/Demolition/SurfaceRules/SurfaceCells/SurfaceFires.csv` | 金丹演武预设、唯一席位、能力来源、命令、有限合同、切装、选段与水火地表 | `Data/JindanDemo/JindanDemoCatalog.asset` |
| `CharterRuleDefinitions.csv` | 册界规则定义 | `Data/CharterRuleDefinitions/CharterRuleDefinition_*.asset` |
| `CharterSites.csv` | 册界单据点站点契约 | `Data/CharterSites/CharterSite_*.asset` |

## CSV 格式规则

- 首行为字段名，后续每行为一条数据
- `#` 开头为注释，导入时跳过
- 字段分隔：`,` | 数组分隔：`|` | 键值分隔：`:`
- **文本字段填 ID**（不是中文），ID 在 Language.csv 中定义

## 关中首批正式内容目录

`Settlements.csv`、`Items.csv`、`Bounties.csv` 与 `Enemies.csv` 中带有
`contentScope=guanzhong` 的石甲兽、裂石甲兽行共同构成正式内容投影。导入器会先读取并验证四张表、
Language 引用和跨表稳定 ID，再原位更新 asset，并生成唯一的
`Data/ContentCatalog/ContentCatalog.asset`。

- 只有 `enemy_shijiahou` 与 `enemy_lieshijia` 进入正式敌人目录；其他 `Enemies.csv` 行保持战斗模板输入，不因新增字段升格进目录。
- `footprintKind` 与 `bodyScaleRank` 是每行必填的独立物种资格：前者只接受 `single`／`adjacent_pair`，后者为正 Int32。两兽显式为 `adjacent_pair`／2，其余现有敌人为 `single`／1；缺列、缺值、未知类型、非正档与溢出均在写 asset 前拒绝。
- 这两个字段由 `SettlementContentImporter` 读取全部行并校验，正式物种保存到 `EnemyData`，经 `AdventureUnitSpawner` 进入 `CombatantSnapshot`；不复制进 `combatTemplate` 数值派生、不乘 `combatStatScale`。玩家由 `AdventureUnitSpawner.CreatePlayer` 明确赋 `single`／1，现有新建及读档链不增加长期字段。
- `Enemies.csv.dropTable` 是旧候选文本；正式石甲兽掉落只由 `dropEntries` 的
  `itemId@dropChancePercent@quantity` 表达。
- `Enemies.csv.combatStatScale` 是正式敌人的战斗结果缩放参数，范围 `(0,1]`。
  石甲兽为 `0.5`；原始属性、境界系数及 AP／移动效率镜像保持基线。由
  `AdventureUnitSpawner.ProjectEnemyStats` 派生后统一缩放一次，Unity 与 BattleSim
  共用 `EnemyCombatStats.Scale`；不得把已缩放一级属性再次代入派生公式。
- `features` 使用 `featureId~displayNameKey~availability~disabledReasonKey`，多个条目用
  `|` 分隔；`adventureEntranceIds` 用 `|` 分隔。
- `Bounties.csv.rewardEntries` 使用 `itemId@quantity`，多个条目用 `|` 分隔。
- 任一表头、行数、Language、稳定 ID、参数、路径或跨表引用不合法时，导入在写入任何
  正式内容 asset 前失败；目录只提供查找，不接入场景、战斗、掉落、背包、悬赏或存档。

## Adventures.csv 出生姿态

表头为 `adventureId,displayNameKey,contentScope,nodeId,nodeTypeId,q,r,contentId,additionalEnemies,enemyFacing`。
`enemyFacing` 仅遭遇行必填，必须为 0–5；无敌人的节点留空，夹带朝向拒绝。
`additionalEnemies` 每项为 `enemyId@q:r:facing`，多项用 `|` 分隔；保留声明顺序对应的稳定 `enemy_2`、`enemy_3` ID。字段缺失、非法方向、重复锚点、未知列或错误行宽均拒绝。
`AdventureContentImporter` 生成 `AdventureMapData.nodes[].enemyFacing` 与 `additionalEnemies[].facing`，正式地图第一遭遇前格为 `(7,8)`／`(8,8)`、第二遭遇为 `(3,5)`／`(1,5)`，两兽均朝向 2。第一遭遇主敌向内留出转身空间，避免 q=9 边界锁死；第二遭遇副敌向左一格，避免岩石与两兽身体互锁。整个身体的障碍、边界与重叠仍由战斗空间权威入口检查。

## AttackProfiles.csv D4空间与环境资格

attackSchemaVersion固定为3，旧schema2不能缺省补值。表尾严格追加sourceModeMask、targetModeMask、verticalReachRank、isMelee、environmentTargetTypes、environmentDamage；前三项分别为1～15位掩码、1～15位掩码及非负整数，isMelee仅0/1。GROUND=1、FLY=2、BURROW=4、SWIM=8；当前生产全部来源／目标掩码为1。基础攻击及贯石击显式近战、触及档1，其余现有术法触及档2（自施聚灵术无需虚构外部高度比较）。水平射程、垂直触及及实体遮挡独立检查，不增加高地伤害倍率。

environmentTargetTypes以分号分隔且不允许空项／重复／未知类型；空集合时environmentDamage必须显式0，有集合时须为正整数且只能用于伤害效果。当前只有spell_guanshiji配置low_wall;high_wall;stone_support;low_plant及30；其余生产档为空／0，徒手始终无环境作用资格。贯石击单位倍率／费用不变，环境耐久损伤独立固定结算，整次动作末统一提交。CSV修改仍沿“天章/导入全部配置”同步asset；BattleSim和数据链校验共用CombatRuleSet的严格schema门。

## EnvironmentProfiles.csv 契约

生产行固定为 `env_guanzhong_wild`（旧平地地图）与 `env_guanzhong_jindan_demo`（D1 三档演武地图）。表头固定为：

`profileId,queryLimits,battlefieldCells,surfacePrototypeRefs,phenomenonChannels,phenomenonPairs,elementRelationRefs,spiritRecoveryEnvironment,edgeClearanceLimits,mapSchemaVersion,mapVersion,directedEdgeOverrides,physicalSurfaceProfiles,mapMarkers,sceneObjects,flightSpace`

`mapSchemaVersion` 留空表示旧版（v1）三段格契约，唯一显式值 `2` 表示固定高差地图；其他值失败关闭，不回退推断。v1 行从mapSchemaVersion起的七个追加列必须留空，按旧九列语义导入，不改变百格、四个岩石或平地有向边。

- `queryLimits` 格式为 `unitsPerRange=<正整数>;maxQueryRange=<正整数>`。
- v1 `battlefieldCells` 以 `|` 分隔，每项 `q:r@blocksGroundMove@blocksLineOfSight`，标志只接受 0／1，按 q、r 排序且坐标唯一。导入器从全部拓扑相邻格生成平接有向边，边长度为 `unitsPerRange`。
- v2 `battlefieldCells` 以 `|` 分隔，每项
  `q:r@groundBlocked@losBlocked@integerHeightLevel@nonnegativeCellBurdenUnits@surfaceKind@physicalSurfaceId`；
  高度档用于规则，单元格负担用于涉水／泥地，物理支承档案引用 `physicalSurfaceProfiles`。记录仍按 q、r 排序且唯一。
- `edgeClearanceLimits` 在 v1 已生成边上附加稀疏净空限制：`fromQ:fromR>toQ:toR@limit`，多项用 `|` 分隔，无约束显式写 `none`。条目 limit 须为正 Int32；端点须存在、拓扑相邻、对应边已生成、同向键唯一。缺值、错格式、非法引用与溢出拒绝，反向边必须单独声明。未列边的 `clearanceLimit=0` 明确表示不限；正上限要求单位 `bodyScaleRank <= clearanceLimit`。v2 必须写 `none`，净空改由有向边覆盖唯一承载。
- v2 `directedEdgeOverrides` 以 `|` 分隔，每项
  `fromQ:fromR>toQ:toR@edgeKind@positiveMetricDistanceUnits@nonnegEdgeBurdenUnits@allowsMovement@allowsEffects@nonnegClearanceLimit@connectionIdOrNone`。
  先为全部六邻接有向对生成默认边（同高 `flat` 可走、异高 `open_cliff` 不可走），再应用覆盖。`edgeKind` 接受 `flat`、`slope`、`stairs`、`one_way_drop`、`open_cliff`、`entity_wall`（`climb`／`bridge` 仅保留枚举，不在本轮实现通行）。坡／阶必须具名，且异高移动连接须两向同类型、同 `connectionId`、两向可走；单向落差只能由高处落下；封墙同时阻断地面移动与普通作用路径。
- v2 `physicalSurfaceProfiles` 是一个带引号的 JSON 单元，包含 `gridGeometry`、`flatProfiles`、`rampProfiles`、`stairConnections`；导入器校验 JSON、支承 ID 唯一、格与坡／阶引用、坡肩阻挡、三平台高度和台阶连接 ID。几何细节留待 D3 读取，不用表现层反推规则。
- v2 `mapMarkers` 是一个带引号的 JSON 数组，标记 ID 唯一且格存在；标记不创造占位、阻挡或接触资格，只有所属遭遇启用后才成为对象。
- `sceneObjects` 是D4主要场景交互物的JSON配置，旧平地行留空；地图schema仍为2。顶层显式包含lightCoverHitPenalty=10、heavyCoverHitPenalty=20、types与instances。导入器直接解析为EnvironmentSceneObjectConfiguration并完整验证，运行时消费已解析asset，不另读取JSON。
- `flightSpace` 是D6空中许可JSON，旧地图留空。cells与全部地格、edges与全部有向邻边一一对应、坐标唯一；每格显式allowsFlight、maximumEffectiveHeight、movementBurdenUnits，每边显式allowsFlight、movementBurdenUnits。演武144柱、760有向边基础许可全开、最高有效高度4、空中额外负担0；独立的墙／场景物／空间封锁／占柱和原型跨高限制仍生效。空配置不开放飞行。导入器验证后写入asset，运行时不读CSV或按模型猜测；数据链脚本核对完整CSV→asset投影。
- 类型只允许low_wall/high_wall/stone_support/low_plant；每型声明materialType（stone/plant）、anchorMode（edge/cell）、maxDurability、coverMode（edge/none）、coverTier、maxProtectedBodyScaleRank、blocksMovement/blocksSight、stateRules=half_durability、residueType（rubble/failed/charred）、targetFlags=attack、interactionType=destructible、reactionRules=fixed_residue。固定耐久分别为30/60/60/15；剩余耐久<=一半进入受损，低墙重2降轻1（保护体型至2），高墙受损仍阻挡，归零后所有残留无阻挡／掩体。
- 实例声明instanceId、contentType、cell、edgeOther、sourceRef、replacesWallConnectionId。实例与原型ID、占槽唯一；格边须两端存在且相邻。高墙必须逐无向格边精确绑定已有entity_wall正反边及connectionId，两段共用mid_sealed_wall名称但分别解除；不能按连接名一次拆掉整组墙。普通支点与低植不提供领域或燃烧周期，四个public_planned_support标记仍未激活。
- 有向边负担与格负担分别投影到 `SpatialEdgeRules.MovementBurdenUnits` 与 `SpatialCellRules.MovementBurdenUnits`；攻击距离仍只读 `metricDistanceUnits`，不把上坡负担算成射程。高度资格、边类型与双向连接由 `SpatialQueryBoard` 统一判定，不从 Transform 或模型高度推断。
- `surfacePrototypeRefs`：以 `|` 分隔的地表原型 ID。
- `phenomenonChannels`：以 `;` 分隔的六个通道，格式 `channel=typeA+typeB`；`airflow`、`visibility`、`temperature`、`precipitation`、`suspendedHazard`、`cloudDischarge` 各一次。
- `phenomenonPairs`：以 `|` 分隔的无序配对，格式 `channel:typeA+typeB>resultType`；类型须在对应通道声明，翻转的重复配对拒绝。
- `elementRelationRefs`：五行 `element_wood`、`element_fire`、`element_earth`、`element_metal`、`element_water` 各一次，以 `|` 分隔。
- `spiritRecoveryEnvironment`：`ordinary`、`spirit_vein_node` 或 `blessed_land`，无默认值。

`WorldContentImporter` 验证整张表后才创建或更新 asset；导入沿 `天章/导入全部配置` 的现有顺序执行。

## FoundationPurpleMansionStates.csv 契约

本表只消费 `docs/superpowers/specs/2026-07-25-foundation-purple-mansion-data-contract.md` 的
`foundationPurpleMansionState` 根对象；D5 按装配规则的演示预设节登记五名生产角色。
表头固定，未知列（包括旧
`developedMansions`、`mansionBindings`、`realmStage` 与 `legacyDanJiType`）会失败关闭。

- `schemaId` 固定为 `foundationPurpleMansionState`，`schemaVersion` 固定为 `2`。
- 复合列不用 CSV 内嵌逗号：集合使用 `|`，记录字段使用 `~`，记录内列表使用 `+`；
  空集合写 `none`。`mansionStates` 始终显式给出五府，不得省略未建府。
- `fixtureId`、`expect` 和 `fixtureOnlyNumericProfile` 仅供 EditMode fixture 使用；生产
  导入拒绝它们，故测试字面量不能生成生产 asset。
- `phaseBoundarySetId`、功法／术法／神通／行动和数值档案仍是外部权威 ID；本表只保存
  稳定引用，不从旧角色字段或运行时槽位推断任何状态。
- 府体绑定使用各府的 `mansionInstanceId + _body` 实例 ID；其效果记录的
  `atomicEffectType` 必须仍是该府唯一的既有标准府体模块，不能用实例命名绕过模块校验。

## CharterRuleDefinitions.csv 契约

本表只消费 `docs/superpowers/specs/2026-07-22-tianzhang-charter-data-contract.md` 的
十八字段静态条目定义；当前唯一生产条目是 `charter_entry_suifu_diji`（水府地纪）。
它与 `CharterRuntimeStateData` 严格分离，不能把地区长期状态写入
`EnvironmentProfileData`。

- 表头固定为 `ruleEntryId,displayName,ruleFamily,relationElement,compatiblePhenomena,positiveCommit,negativeCommit,requiredAuthority,requiredNodeTypes,scopeType,scopeTierCap,anchorNodeIds,propagationBoundaryProfileId,currentCoverageSet,affectedWorldVariables,conflictProfileId,failurePolicy,worldEventOutputs`；未知列、缺列和空的生产字段失败关闭。
- 集合以 `|` 分隔；`worldEventOutputs` 的单项格式为
  `eventId~environmentProfileId`。`displayName` 必须是 Language 键而非显示文本。
- 生产行必须通过唯一静态目录 `Assets/Data/CharterRuleStaticCatalog/CharterRuleStaticCatalog.asset`
  中批准的 `CharterRuleReferenceCatalog` 解析遗物权限、组织授权、节点、边界、现实供给、提交、
  变量、冲突／跨阶资格、事件和环境档案。导入器只读取该 canonical asset 的批准目录，不再持有
  第二份硬编码生产目录；十八字段与全部外部引用由 `CharterRuleCatalogValidator` 这一共享校验
  解析，玩家运行时与导入器调用同一实现。当前目录只声明旧水驿／地下泵房门禁、太玄界印管理
  场景、已接通水工／册界节点、已登记现实供给、`conflict_charter_water_basin` 和
  `env_guanzhong_wild`；不能用 fixture、显示文本或路径补齐。
- 首条样例保持 `CONNECTED_NODES`／`AREA`，仅列出同一流域内已接通节点的可枚举覆盖；它只输出 `env_guanzhong_wild`，不把地区长期状态反写入环境档案，也不表示规则事务、存档、场景或 UI 已实现。
- `scopeType` 只接受 `SINGLE_NODE`、`CONNECTED_NODES`、`REGIONAL_HUB`；
  `scopeTierCap` 只接受 `NODE`、`AREA`、`REGION`；`failurePolicy` 只接受
  `REJECT`、`SUSPEND`、`SAFE_DOWNGRADE`，均不得依靠默认值。

## CharterRuleStaticCatalog 静态目录

`Assets/Data/CharterRuleStaticCatalog/CharterRuleStaticCatalog.asset` 是玩家运行时唯一的册界
静态校验来源，只保存两类静态输入：已导入 `CharterRuleDefinitionData` 的直接 asset 引用，以及
一个已批准、可序列化的 `CharterRuleReferenceCatalog`。`ContentCatalogData` 只保存这个 asset 的
单一引用并暴露失败关闭的取得方法；Editor 导入、fixture 和默认化数据不参与保存或恢复。

- 目录显式声明、目录内无重复稳定 ID、每个定义具有唯一 `ruleEntryId`，且每个定义的十八字段及
  全部外部引用都由同一目录解析；零、缺失或不匹配的 `definitionCatalogVersion` 不得从默认值推断。
- `GameSessionSnapshot` schema 4 保存／恢复成对记录状态 presence、定义目录版本与深复制状态；
  schema 0～3 只恢复明确未接入状态，读档不重放供给、占用、提交、冲突或事件。

## CharterSites.csv 契约

本表只消费 `docs/superpowers/specs/2026-08-04-tianzhang-charter-site-vertical-slice-task-decomposition-design.md`
的站点数据契约；当前唯一生产站点是 `charter_site_old_water_station`（旧水驿，归属
`guanzhong_city`）。`CharterSiteData` 只拥有静态站点契约，不保存玩家已完成动作；
`ContentCatalogData` 只持有唯一生产 asset 并提供只读精确查询。

- 表头按语义分组固定为：站点身份（`siteId`、`displayNameKey`、`settlementId`）、通行
  （`passageCapabilityId`、`passageOperatorId`、`passageTargetId`、`passageProtocolState`、
  `passageStructureState`、`passagePowerState`、`interactionTimeProfileId`、`recognitionTiming`、
  `operationTiming`、`cancellationPolicy`）、管理（`facilityId`、`sealRelicId`、
  `sealManagerId`、`sealBeneficiaryId`、`sealAuthorizationVersionId`）、册界（`ruleEntryId`、
  `ruleEntryOccupancyId`、`nodeOccupancyId`）、金丹样例（`jindanConflictEventId`、
  `jindanChallengeEventId` 与 `grant*`、`leftCandidate*`、`rightCandidate*`、`charterCandidateId`
  前缀列）和元婴样例（`yuanying*` 五列）；未知列、缺列和空的生产字段失败关闭。
- 站点自有 ID 必须在本表内唯一、非空并满足固定语义：`passageCapabilityId` 固定为
  `capability_kaihe_jiuzhang_v1`，`interactionTimeProfileId` 固定为
  `interaction_time_old_water_station_gate_v1`，同一行显式声明“识别瞬时
  （`recognitionTiming=instant`）、操作持续引导（`operationTiming=sustained_guided`）、取消不提交
  （`cancellationPolicy=no_commit_on_cancel`）”；门禁协议、结构与供能必须显式为
  `compatible`／`intact`／`available`。操作者、管理者和受益者是本站点职责 ID，不伪装成全局
  角色 ID，通行资格不能成为管理资格。
- 跨契约字段由既有定义与静态目录解析：`settlementId` 必须为唯一正式据点 `guanzhong_city`；
  `sealRelicId`、`sealAuthorizationVersionId`、`ruleEntryId`、grant／元婴的世界变量与节点必须
  在 `CharterRuleStaticCatalog.asset` 的批准目录中解析；锚点、覆盖、正负提交和现实供给不在
  本表重复抄写。
- 金丹 grant 由本站点静态数据完整拥有：`grantId` 必须已经列在水府地纪 `conflictProfileId`
  冲突档案的 `crossTierChallengeGrantIds` 目录中，`grantDefinitionVersion` 必须等于当前静态
  目录版本；版本、变量、操作、对象、范围、受益者、现实支点、资源／容量账本、层级、生效／
  失效 tick、撤销状态／原因和显示来源均显式声明。左右候选以 `leftCandidate*`／
  `rightCandidate*` 前缀完整承载十二字段，不得由运行时补默认值；两侧候选 ID 互异，
  `charterCandidateId` 唯一锁定册界侧。
- 导入验证用同一 shared 决定消费完整 grant、版本化请求与左右候选；确定性赢家不是
  `charterCandidateId` 时整行才通过（册界侧稳定未获胜），字段变化使该结论不再稳定时整表拒绝。
- 元婴样例只携带冲突事件、目标变量、对象、范围与道路锚点五个身份 ID，结果固定为受锚且不
  提交状态；夹带金丹候选、grant 或可覆盖结果时整表拒绝。任一非法输入都在写入任何 asset 前
  失败关闭。

## JindanStaticStates.csv 契约

本表只消费 `docs/superpowers/specs/2026-07-25-jindan-static-data-contract.md` 的
`jindanStaticState` 根对象。D5 生产行与五名演武预设逐一对应。
表头固定，未知列以及旧 `developedMansions`、`mansionBindings`、`realmStage`、
`legacyDanJiType` 和显示文本字段均会失败关闭。

- `schemaId` 固定为 `jindanStaticState`，`schemaVersion` 固定为 `1`。集合用 `|`，记录字段用
  `~`，辅助承载列表用 `+`；无可选引用写 `none`。
- `mansionInputs` 必须明确包含命、魂、识、悟、运五行；已建府完整镜像同角色、`FORMED` 的
  `foundationPurpleMansionState` 冻结输入。稳定实位为一至三项 `SOURCE`／`TRANSFORMATION`／`DOMAIN`，
  每项一个不重复主承载。
- 道路、效果、位格、道证、兼容档案、丹相、账本和冲突费用只保存稳定引用；导入器在 asset
  写入前要求它们全部由同一批次或声明的外部权威目录解析。演武行由下述有限目录解析，
  不能把 fixture ID 当成生产内容。
- `fixtureId`、`expect` 和 `fixtureOnlyNumericProfile` 仅供 EditMode 直接 fixture；生产导入拒绝它们，
  因而 fixture 字面量不能生成生产 asset。

## JindanDemo 演武目录

唯一规则来源为 `docs/基础设定/金丹基础效果装配与冲突规则.txt` 的演示预设节、七份位格原文，
以及 `docs/角色养成/演示原型/` 的七源术、七镇府神通、御风飞行和获准破垒派生原文。十一张表按公开类型严格读写：

- `Presets`：五名固定角色、根资质、容量、来源、共享 AP/MP、移动效率、视野、身份与席位引用。D6追加basicAttackSourceModeMask／basicAttackTargetModeMask，五名均显式3；演武会话从原金丹徒手档案派生空间资格，保留原ID定价、伤害、射程和垂直触及1。普通AttackProfiles资产仍为GROUND掩码1，不随飞行能力自动扩权。
- `Seats`：七个唯一席位、版本与道证、唯一主承载、四项作用范围、候选效果与默认装配。
- `Abilities`：十五项定义、源术升格、内化处置、合法府属及对象／操作／媒介／结果范围。
- `Commands`：每个候选效果各一项展开和终止，以及五项飞行命令；费用、时钟、容量、射程、
  模式、支点、遥施、失效与禁用原因均为显式字段。`SPACE_SCALE`、`SPACE_SEAL`、`EARTH_MANIFEST`、`EARTH_GRAVITY`、`WATER_TIDE`、`FIRE_MANIFEST`展开／关闭及五种 `FLIGHT` 动作已启用；
  `JindanDemoSealRules.csv`固定封锁方向、锚点、局部维持半径与破层期限；费用和6tick期限仍读Commands。Presets的equippedArtProfileIds显式登记space_yang的现有贯石击。
  其余只可浏览，尚待 D11 的真实显隐效果实现，不能用空账本执行。潮汐涨／退、普通／蓄力、无源潮眼共用一份主枢命令，附费与效果参数由SurfaceRules读取。
- `Contracts`：八项固定效果合同。`compatibilityProfileId` 必须写 `compile`，导入时用来源版本、
  具体主承载、席位／道证版本、四项交集与费用容量语义生成稳定 SHA-256 标识；不接受伪造兼容 ID。
  静态根写 `compile_<contractId>`（不使用结构保留的冒号），同批导入替换为编译结果。仅验证八项明确合同，未实现通用效果推导。
- `LoadoutTuning`：安全态免费切装；战斗中本回合未用完整行动、每席每本人行动至多一次，
  扣本回合最大 AP 快照，MP/CT/CD 为零，下个本人回合开始生效。
- `Segments`：三段固定演员、队伍、六角坐标与朝向；第二段默认土＋水对火，战前可切为执火守阵（同阵容仅对换操控方）；第三段六合／阳对封关／阴。
- `Demolition`：D7获准的土系预设破垒派生，明确引用原贯石击、独立profileId、环境白名单及375固定耐久伤害；复用原费用、人物攻击和形态资格，只在演武会话授予术栏。
- `SurfaceRules`：D8演武专用水面移动负担、固定潮汐模板、水线、推力、蓄力／潮眼附费、水击与冲蚀；这些参数加入WATER_TIDE兼容哈希，其他合同不变。
- `SurfaceCells`：仅第二段三格有限燃料与一个初始燃烧格；低植燃料引用真实场景物实例，破坏后不能继续供燃。自然水／泥由实际EnvironmentProfiles.battlefieldCells.surfaceKind初始化，不把基础地形与不可走格推断为火源。
- `SurfaceFires`：环境火源的唯一来源、伤害承载角色、10tick周期、40tick期限、3格容量和12总燃料；此表仅定义D8环境火，不提供领域余火或永久场景改造。水火地表与本地燃料均随新演武会话重置。

- `FireDomainRules`：D10中心(7,12)、总燃料15、每次锁定尾效6、尾效2个本人结束周期、2400神攻承载及0.01弱燃烧；全部进入FIRE_MANIFEST兼容哈希。主周期与费用读Commands。仅合法展开时中心成为规则对象；停止后保留场内燃料／耐久，撤离重入重置。起火资格来自尚有本地燃料的公开格或存活low_plant；后者可借中心有限燃料，不回填D8燃料。

初始 AP=1000、MP=6000、v=4.8 来自 D1。`normalVisionRange=4` 为本轮普通视野输入。
基础体魄等先天输入引用既有 `char_sanxiuqiangdao`，不继承其身份、境界或能力；
金丹倍率 6 来自既有 `CombatGongFaRules.GetRealmMultiplier`，基础攻击引用 `basic_body_human_jindan`。
主修复用《秋水游心经》任意灵根／凡品以上的既有资格。两者仅作未平衡验收基准，
不虚构累计成长次数、不从 MP 倒算属性。初期一府、中期两府及五府显式状态均来自 D1。

Unity 菜单 `天章/内容/导入金丹演武目录` 或 `天章/导入全部配置` 调用
`JindanDemoContentImporter.Import()`；先验证 CSV、规则原文、语言键、既有功法／角色资产、
五府与金丹静态根及编译合同，再写入五份 Foundation、五份 Jindan 与一份目录资产。
导入全部配置时功法与角色先于本目录导入。ConfigEditor 的两张身份表使用同一预览与导入链。
源定义数组中空集合或可空引用写 `none`；生产身份表的三项 fixture 列必须留空。
`positionVersion` 与 `proofVersion` 保留 `major.minor`；旧静态根整数版本编码为 `major*100+minor`。

## 添加多语言

1. 在 Language.csv 中追加列：`id, zh_CN, en_US, ja_JP`
2. 修改 Character／Combat／Cultivation 领域 importer 的文本解析策略，按当前语言选择对应列
3. 重新导入即可

## 工作流

1. 在对应 CSV 中编辑数据（文本字段用 ID）
2. 如需新增文本，先在 Language.csv 中添加 ID→中文映射
3. Unity 菜单 → `天章/导入全部配置`（Coordinator 依次调用领域 importer）
4. 生成的 .asset 中显示为已解析的中文
