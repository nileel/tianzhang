# NPC 修炼行动权重静态数据契约

状态：✅ 已锁定（D-NPC-WEIGHT-01A，2026-07-27）；本文件只定义独立 CSV／asset 数据链、失败关闭语义与 fixture 规格，不实现 CSV、asset、导入器、BattleSim、Unity 或任何生产权重。

## 一、范围、唯一权威与拒绝原则

本契约承接《养基、筑府与闭关行动生命周期设计》《修行境界》与《金丹位格证位与争位规则》。NPC 与玩家先共享同一行动合法性、时间、资源、进度、暂停、完成及失败规则；本契约仅在合法行动集合内决定 NPC 的可配置优先级和同分裁定。

- 可编辑的唯一权威是版本化 `npcCultivationActionWeightProfile` CSV 源集；其生成的 `NpcCultivationActionWeightProfileData` asset，以及供 BattleSim 使用的同版本只读投影，都只是该源集的派生物，携带相同 `schemaId`、`schemaVersion`、`profileId` 与源集内容哈希。
- Unity 和 BattleSim 必须只读取同一已验证投影，不能各自保存常量、默认权重、覆盖表、行为顺序或第二份配置。运行时不得写回权威源或生成新的权重记录。
- 源集可因 CSV 的物理拆表而含清单、行动、修正、策略与重算事件行，但它们共同构成一个 `profileId` 的原子输入：先完整解析、交叉校验和生成固定投影，才允许创建或更新任一 asset／模拟投影。
- 生产源集只能使用已知稳定 ID、实际可观测的角色／世界事实和显式数值。未知 ID、缺失必填值、重复键、未知列、隐式值、旧优先级字段、Unity／BattleSim 覆盖、运行时回退或两个权威源一律整表失败；不得猜测、截断或选择其中一个继续导入。
- 本契约不复制行动成本、周期长度、资源扣费、行动硬门槛、道基／紫府状态、结丹锁、证位结果或 NPC 未知信息。它只引用对应行动和事实的权威来源；权重不能改变这些来源的结论。

逻辑根对象如下；它描述逻辑字段归属，不要求本卡决定 CSV 的分隔符或 Unity 类型名：

```text
npcCultivationActionWeightProfile
├─ schemaId / schemaVersion / profileId / sourceContentHash
├─ actionWeightRows[]                     # 合法行动的基础权重
├─ modifierRows[]                         # 六类可配置修正
├─ capPolicies[] / diminishingPolicies[]  # 显式封顶与边际递减
├─ tieBreakPolicy                         # 完全同分时的确定性裁定
├─ recalculationTriggers[]                # 事件驱动的重算边界
└─ stableFailureReasons[]                 # 导入和决策可复现的原因键
```

`schemaId` 固定为 `npcCultivationActionWeightProfile`。未知版本不兼容读取；新版本不得借由可选字段或默认值静默兼容旧版本。

## 二、行动稳定 ID 与合法性边界

`actionWeightRows.actionStableId` 是行动选择的唯一键，不是角色当前行动实例、目标 ID、府胚 ID、位格 ID 或优先级顺序。当前已确认的修炼行动必须使用下列稳定 ID；后续新行动只能在其自身行动规则、硬门槛和稳定 ID 同时锁定后，另行进入此集合。

| `actionStableId` | 现有行动语义权威 | 权重前不可绕过的边界 |
|---|---|---|
| `FOUNDATION_TRIAL` | 筑基考验 | 练气九品圆满、相容灵根、主修功法、地点、辅助资源及当前行动状态均合法。 |
| `FOUNDATION_NURTURE` | 连续养基 | 已成功筑基、仍在筑基期、未完成第四阶段，且只推进同一唯一道基。 |
| `MANSION_EMBRYO_NURTURE` | 府胚蕴养 | 目标府属、源术法、容量、同类府唯一性、知识、行为、资源与环境均合法。 |
| `MANSION_OPENING_TRIAL` | 正式开府考验 | 目标完整府胚、原目标和源术法，以及全部当前开府硬条件仍有效；考验不可中途离开。 |
| `JINDAN_PROOF` | 金丹位格证位 | 道基第四阶段、至少一座完整紫府及镇府神通、最低道证条件、已知空位与地点、兼容主承载／支点／设施／资源／护道准备，且无更高优先级生存危机或强制职责。 |

每个行动行还必须带有 `legalityRuleSetRef`，精确引用上表对应的既有行动规则集，而不能把条件重抄为可调权重列。行动过滤器以当前角色和世界事实先产生 `legalActionSet`；不在其中的 ID 不得进入分数、封顶、递减或并列裁定。

特别约束如下。

- `JINDAN_PROOF` 只读取 NPC 自己已知的空位、地点、竞争者、准备、路程、资源、仇敌、动机和风险偏好；不得读取后台真实成功率、未知条件或他人隐藏进度。只有具备持久成长、履历、寿元、知识和资源记录的 NPC 才能进入其完整决策。
- 寿元紧迫度可改变合法行动的优先级及主观风险阈值，但不能使非法行动合法，也不能绕过灵根、道基阶段、容量、紫府、知识、资源、结丹锁、证位最低条件或原子绑定。
- 行动资源可支付性由行动成本权威在周期提交前判定。本契约可读取“当前可支付／保留资源条件”事实作为修正选择器，却不得复制成本、预支周期或把资源不足解释为可用的高权重行动。

## 三、行与字段契约

所有记录都有非空稳定 `recordId`；同一 `profileId` 内不得重复。生产行不得使用 `none`、通配符、未声明的“默认”行或依赖 CSV 行号。测试 fixture 可在自己的隔离输入中使用显式字面量，不能生成生产投影。

### 3.1 清单与行动基础权重

清单只有一条，必填字段是 `schemaId`、`schemaVersion`、`profileId`、`sourceContentHash`、`authorityKind=CSV_SOURCE_SET`、`assetProjectionId`、`battleSimProjectionId` 与 `tieBreakPolicyId`。同一 `profileId` 出现第二清单、不同 `authorityKind`、不一致投影 ID 或任一 Unity／BattleSim 专用覆盖字段，均为双权威来源。

每条 `actionWeightRow` 必填：

| 字段 | 语义与约束 |
|---|---|
| `actionStableId` | 第二节中已知行动 ID；同一 `profileId` 内精确一条。 |
| `legalityRuleSetRef` | 对应行动硬门槛的权威引用；未知或与行动不匹配即拒绝。 |
| `baseWeight` | 可热调的显式数值；不得缺失、由常量补写、从行动成本推导或用另一行动继承。 |
| `subjectiveRiskGateRef` | 可为空的已知主观风险门引用；仅适用于已有这种决策边界的行动，例如证位。它只读取 NPC 已知事实和其风险阈值。 |
| `enabled` | 显式布尔值；`false` 只使该合法行动不进入此 profile 的选择候选，不改变其行动硬门槛或结果规则。 |

`baseWeight` 是后续生产数据必须填写的字段，本卡不提供任何行动的生产数值、阈值、默认行或排序名单。

### 3.2 六类修正行

每条 `modifierRow` 必填 `modifierId`、`sourceKind`、`actionStableId`、`selectorRef`、`priorityDelta`、`applicationOrder`、`capPolicyRef` 和 `diminishingPolicyRef`。`actionStableId` 必须已存在于行动行；`selectorRef` 必须解析到实际事实来源。没有匹配的显式修正行只表示该来源本次不贡献增量，不允许生成隐式默认修正。

`sourceKind` 只允许如下七值，且 `selectorRef` 的事实所有者固定：

| `sourceKind` | `selectorRef` 所指事实 | 额外字段／边界 |
|---|---|---|
| `PERSONALITY` | NPC 已拥有的性格档案 ID | 不从缺失性格猜测偏好。 |
| `SECT` | NPC 当前门派／组织身份及明确允许的门派档案 ID | 门派变化才可触发重算。 |
| `REALM_GOAL` | NPC 已声明且当前仍有效的境界目标 ID | 不替 NPC 创造未知目标或越级目标。 |
| `LIFESPAN` | 当前寿元压力档及其可观测进入／退出事实 | 另可有 `riskThresholdDelta`；只能调整主观风险阈值。 |
| `RESOURCE` | 当前已知资源／保留资源条件 ID | 不是资源成本表，不可改变可支付性判定。 |
| `ENVIRONMENT` | 当前地点的已知环境档案及行动相关环境条件 ID | 不搜索未访问地点或后台世界环境。 |

为保留《修行境界》的七项优先级口径（基础权重加六类修正），结算时的来源组顺序固定为：基础权重、`PERSONALITY`、`SECT`、`REALM_GOAL`、`LIFESPAN`、`RESOURCE`、`ENVIRONMENT`。`applicationOrder` 只在同一来源组内以升序记录和复现；所有操作均为显式加法 `priorityDelta`，不得暗含乘法、随机扰动或“先命中即停止”。

`riskThresholdDelta` 只允许出现在 `LIFESPAN` 行，且只被相应的 `subjectiveRiskGateRef` 读取。它不是行动合法性、成功率、证位条件或实际结果的修改器。

### 3.3 封顶、边际递减与主观风险门

每个被行引用的 `capPolicy` 必填 `capPolicyId`、`scope`、`minimum`、`maximum` 和 `appliesAfterSourceKind`；`scope` 只能为 `SOURCE_GROUP`、`ACTION_TOTAL` 或 `RISK_THRESHOLD`。每个 `diminishingPolicy` 必填 `diminishingPolicyId`、`scope`、`inputBasis`、`activationThreshold`、`segments` 与 `outputBound`。`segments` 必须以显式连续区间、输出规则和边界组成，区间不得重叠、遗漏或倒置。

这些策略在源集内是可热调的显式数据，不能由 BattleSim／Unity 常量实现。策略值、折点、上限和阈值目前均未锁定；生产源集建立前不得凭本契约填写它们。策略的结算顺序为：先按来源组汇总并应用其递减／封顶，再合计基础权重与组结果，最后应用 `ACTION_TOTAL` 封顶；主观风险门独立按其 `RISK_THRESHOLD` 策略结算，不能改变行动是否合法。

`subjectiveRiskGateRef` 所指门必须声明 `knownEvidenceRefs`、`riskAssessmentRef`、`baseRiskThreshold` 与 `lifespanCapPolicyRef`。任何引用未知后台成功率、未知竞争者、他人隐藏进度或未声明风险阈值的门都失败关闭。

## 四、重算、排序与结果边界

决策只在下列已发生事件到达稳定决策边界时重算；不进行全世界每日扫描，也不因读取已提交周期而重复结算。

| `triggerStableId` | 重算条件 |
|---|---|
| `ACTION_LEGALITY_CHANGED` | 行动的硬门槛、当前目标、容量、结丹锁或不可恢复行动状态发生实际变化。 |
| `PERSONALITY_OR_SECT_CHANGED` | NPC 性格档案或当前门派身份实际变化。 |
| `REALM_GOAL_CHANGED` | 已声明境界目标建立、替换、完成或失效。 |
| `LIFESPAN_PRESSURE_BAND_CHANGED` | 寿元进入或离开已定义危险档。 |
| `RESOURCE_AVAILABILITY_CHANGED` | 行动相关可支付／保留资源事实在完整周期或世界事件后实际变化。 |
| `ENVIRONMENT_CONTEXT_CHANGED` | NPC 当前可行动地点或行动相关环境条件实际变化。 |
| `JINDAN_KNOWN_OPPORTUNITY_CHANGED` | 已知空位、可靠情报、天地异象、已知竞争者、世界重大状态、准备或强制职责发生与证位相关的变化。 |
| `CURRENT_ACTION_STABLE_BOUNDARY` | 当前行动完整提交、暂停、完成、失败、终止或恢复后，需要重新选择下一个合法行动。 |

一次决策按下列固定顺序完成：

1. 以当前事实运行行动合法性与适用的主观风险门，记录被拒绝行动的稳定原因；任何非法行动立即排除。
2. 对每个剩余行动只读取精确匹配的基础行、修正行、封顶／递减策略和已知风险事实；缺失或歧义配置使整个 profile 失败，而不是降级为默认选择。
3. 以第三节的组顺序结算优先级，生成可审计的 `decisionInputHash`、`legalActionSetHash`、匹配 `modifierId` 序列、策略 ID 序列和最终分数。权重不改变单周期成本、世界时间、暂停、恢复、失败回退或完整成果。
4. 先按最终分数降序排序；完全相同的候选只按 `tieBreakPolicy.actionStableIdOrder=LEXICOGRAPHIC_ASC` 的 `actionStableId` 升序裁定。不得使用随机数、NPC ID、对象地址、注册／加载顺序、CSV 行号或隐藏优先级。

同一 `profileId`、内容哈希、当前事实快照与事件边界必须生成相同的候选、拒绝原因、分数和排序。这个确定性要求不代表以固定行为顺序代替权重：只有分数完全相等时才读取并列策略。

## 五、失败关闭与稳定原因

下列原因键由后续导入器、BattleSim 与 Unity 原样保留；显示文案可通过语言表映射，但不得改变键或以成功结果掩盖失败。

| 原因键 | 失败边界 |
|---|---|
| `NPC_WEIGHT_UNKNOWN_SCHEMA` | `schemaId`／版本未知或不一致。 |
| `NPC_WEIGHT_DOUBLE_AUTHORITY` | 发现第二清单、运行时／Unity／BattleSim 覆盖、旧优先级字段或不一致投影来源。 |
| `NPC_WEIGHT_UNKNOWN_ACTION` | 行动 ID、合法性规则、选择器、策略或风险门引用未知。 |
| `NPC_WEIGHT_DUPLICATE_RECORD` | 任何要求唯一的行动、修正、策略、触发器或清单键重复。 |
| `NPC_WEIGHT_MISSING_EXPLICIT_VALUE` | 基础权重、修正、策略边界、风险阈值或必需字段缺失，试图依赖默认值。 |
| `NPC_WEIGHT_INVALID_POLICY` | 封顶／递减范围、区间、顺序或适用域非法。 |
| `NPC_WEIGHT_UNOBSERVABLE_INPUT` | 输入需要 NPC 未知信息、后台成功率或全世界扫描。 |
| `NPC_WEIGHT_ILLEGAL_ACTION` | 当前候选未通过行动硬门槛；仅拒绝该候选，不由权重恢复。 |
| `NPC_WEIGHT_RISK_GATE_REJECTED` | 已知主观风险评估低于其已配置风险阈值；不等于行动硬门槛被满足或失败。 |
| `NPC_WEIGHT_NO_LEGAL_ACTION` | 当前快照没有可选行动；不自动替换为默认行动。 |
| `NPC_WEIGHT_FIXTURE_INVALID` | fixture 使用生产投影、缺少专用字面量或同时违反多个目标规则。 |

整表配置错误必须在创建／更新任一派生物前报出前七类失败；候选拒绝原因仅在有效 profile 的单次决策记录中出现。两类原因均不得触发兼容、重试、随机改选或第二权威来源。

## 六、fixture 规格

fixture 与生产使用同一根结构，额外带 `fixtureId`、`expect`、`fixtureOnlyNumericValues` 与固定事实快照。字面量只允许位于 `fixtureOnlyNumericValues`，并且 fixture 投影不得作为 production asset 或 BattleSim 校准输入。

| Fixture ID | 预期 | 最小输入条件 | 必须验证的结果 |
|---|---|---|---|
| `npc-weight.valid.fixed-input-order` | ACCEPT | 一个版本化 profile、两项以上合法行动、每类命中修正、显式封顶／递减／并列策略和固定事实快照。 | 同一输入得到相同分数、修正序列和排序；改变 CSV 行顺序不改变结果。 |
| `npc-weight.valid.legal-action-only` | ACCEPT | 一个高基础权重但不满足开府或证位硬门槛的行动，以及一个合法行动。 | 非法行动的权重不参与计算，合法行动被唯一选中，原因为 `NPC_WEIGHT_ILLEGAL_ACTION`。 |
| `npc-weight.valid.lifespan-risk-boundary` | ACCEPT | 固定寿元压力档、证位所需已知证据与明确风险门。 | 寿元只改变合法候选的优先级／主观风险阈值；不能让缺少第四阶段、完整紫府、知识或资源的证位进入候选。 |
| `npc-weight.invalid.unknown-reference` | REJECT `NPC_WEIGHT_UNKNOWN_ACTION` | 任一行动规则、性格／门派／目标／资源／环境选择器或策略引用未知。 | 整个 profile 在生成任一派生物前失败。 |
| `npc-weight.invalid.double-authority` | REJECT `NPC_WEIGHT_DOUBLE_AUTHORITY` | 同一 profile 出现 Unity／BattleSim 覆盖、旧优先级字段、第二清单或不一致源哈希。 | 不选择任一来源，不创建 asset／模拟投影。 |
| `npc-weight.invalid.missing-explicit-value` | REJECT `NPC_WEIGHT_MISSING_EXPLICIT_VALUE` | 行动基础权重、修正量、策略边界或风险阈值缺失，或试图从代码默认值取得。 | 不补零、不继承邻行、不回退固定行为顺序。 |
| `npc-weight.invalid.unobservable-input` | REJECT `NPC_WEIGHT_UNOBSERVABLE_INPUT` | 修正或风险门读取后台真实成功率、未知条件、隐藏进度或全世界扫描结果。 | 该 profile 不可导入；NPC 只使用自身可知事实。 |
| `npc-weight.invalid.non-deterministic-tie` | REJECT `NPC_WEIGHT_INVALID_POLICY` | 并列策略引用随机数、加载顺序、CSV 行号、NPC ID 或未声明键。 | 拒绝策略；同分候选不能随机改选。 |

## 七、后续实施门槛

后续导入／asset 任务必须先将本契约的源集完整校验为单一投影，再让 Unity 与 BattleSim 只读消费；不得把字段回填进 `FoundationPurpleMansionStates.csv`、`FoundationPurpleMansionStateData`、BattleSim `Program` 常量、行为顺序或另一张配置表。`N-FPD-NPC-01` 只能在本契约的生产权重、阈值、封顶／递减和校准目标另行获批后，以同一投影运行 BattleSim；本卡不产生任何数值平衡结论。
