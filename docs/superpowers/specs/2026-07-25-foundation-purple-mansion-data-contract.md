# 道基、紫府与修炼静态数据契约

状态：✅ 已锁定（D-FPD-SCHEMA-01A，2026-07-26）；本文件只定义供导入器、检查器与 fixture 消费的静态语义，不实现 CSV、Unity、BattleSim 或运行时行动。

## 一、范围、权威与拒绝原则

本契约承接《筑基、紫府与丹相承接系统设计》《五府府体效果机制设计》与《养基、筑府与闭关行动生命周期设计》。它为一个角色的唯一道基、五府状态、镇府神通、平铺效果、结丹锁和当前修炼行动提供同一份可验证输入；功法、术法与神通定义仍是各自内容表的权威来源。

- 一个角色恰有一个 `foundationState`；不得以多个道基、五段筑基小境界或道基品级替代它。
- 一个角色恰有五条按府属键控的 `mansionStates`，分别为 `MING`（命）、`HUN`（魂）、`SHI`（识）、`WU`（悟）、`YUN`（运）。`QI`（气府）不是本契约的合法府属。
- 所有引用必须解析到同一导入批次的稳定 ID，或解析到已声明的外部权威表；缺失、未知或歧义引用一律拒绝，不能填默认值、截断或从旧字段推断。
- 未经数值任务锁定的周期长度、资源量、阈值、进度边界、损耗和强度参数只能是必需的数值档案引用；生产数据不得填字面量。测试 fixture 可在其 `fixtureOnlyNumericProfile` 内显式提供字面量，且不得被导入为生产内容。
- 效果绑定只允许同一载体上的有序平铺列表。不得存在 `effectPackageId`、子绑定、递归效果引用、隐式共鸣包或另一套角色成长服务。

根对象为 `foundationPurpleMansionState`。`D-FPD-SCHEMA-01B` 可自行选择 CSV 分表或对象序列化，但不得改变以下字段语义、必填性或校验结果。

## 二、根对象与字段归属

```text
foundationPurpleMansionState
├─ characterId
├─ foundationState                         # 唯一道基
├─ mansionStates[5]                        # 五府的未建／府胚／建成状态
├─ effectBindings[]                        # 全部载体共享的平铺原子效果
├─ guardianAbilities[]                     # 建成府的一对一镇府神通实例
├─ enhancementNodes[]                      # 可选的神通强化／府间节点
├─ cultivationActionState?                 # 当前或最近结算的行动事实
├─ closedRetreatPlan?                      # 可选的闭关重复请求
└─ jindanLock                              # 未结丹或已结丹的不可逆快照
```

| 字段 | 必填 | 约束与唯一权威 |
|---|---|---|
| `schemaId` / `schemaVersion` | 是 | 必须精确标识本契约版本；未知版本不得兼容读取。 |
| `characterId` | 是 | 角色稳定 ID；同一导入批次不得重复。 |
| `foundationState` | 是 | 本节三定义的唯一对象；不得为 `null` 或数组。 |
| `mansionStates` | 是 | 恰好五项，覆盖五个合法 `mansionKind` 各一次；不得以缺行表示未建。 |
| `effectBindings` | 是，可为空 | 所有 ID 唯一；每项必须由已知载体引用，不接受未挂载效果。 |
| `guardianAbilities` | 是，可为空 | 与 `COMPLETE` 府一对一，且不多不少。 |
| `enhancementNodes` | 是，可为空 | 仅属于一个已知镇府神通实例；不建立全角色统一经验条。 |
| `cultivationActionState` | 否 | 没有正在保留的行动事实时省略；省略不等于创建默认行动。 |
| `closedRetreatPlan` | 否 | 只能引用当前可恢复或可执行的同一行动与同一目标。 |
| `jindanLock` | 是 | 结丹状态与锁定快照的唯一来源，见第七节。 |

## 三、唯一道基、连续四阶段与容量

`foundationState` 必须具备下列字段。阶段名称尚未命名，故数据只使用稳定次序 `PHASE_1` 至 `PHASE_4`，不把临时文案当成 schema 值。

| 字段 | 规则 |
|---|---|
| `foundationInstanceId` | 本角色内唯一且非空；是道基、行动与结丹锁的共同引用。 |
| `foundationDefinitionId` | 必填，指向功法的筑基方案与道基核心效果定义；改修功法不能更换它。 |
| `sourceGongFaId` | 必填，必须与 `foundationDefinitionId` 所属筑基方案一致。 |
| `phase` | 仅 `PHASE_1`、`PHASE_2`、`PHASE_3`、`PHASE_4`；未知、五段旧阶段或道基品级均拒绝。 |
| `continuousProgress` | 必填的连续值，不得由四个离散经验条替代；其解释必须引用 `phaseBoundarySetId`。 |
| `phaseBoundarySetId` | 必填的数值档案引用。档案负责从连续进度判定阶段边界；本契约不补写任何阈值或默认边界。导入器必须验证 `phase` 与该档案解析结果一致。 |
| `naturalMansionCapacity` | 必填整数，范围 `0..3`，由筑基时最高相容灵根决定；不是可手工叠加的效果值。 |
| `releasedNaturalCapacity` | 只读派生值：`min(naturalMansionCapacity, phaseIndex - 1)`，其中 `PHASE_1..4` 的 `phaseIndex` 为 `1..4`。生产输入若携带该字段，必须等于派生结果；不相等即拒绝。 |
| `expansionGrants` | 零至两条。每条必须有唯一 `grantId`、`sourceItemId` 与一个已知的永久容量效果绑定；每条只贡献一座容量，不能用未声明的数值或同一 `grantId` 叠加。 |
| `expandedMansionCapacity` | 只读派生值，等于合法 `expansionGrants` 数量，范围 `0..2`。 |
| `totalMansionCapacity` | 只读派生值，等于 `releasedNaturalCapacity + expandedMansionCapacity`；不得独立填写不同数值。 |

`COMPLETE` 府数加上 `EMBRYO` 府数为已承诺容量，必须不大于 `totalMansionCapacity`。府胚尚未建成，仍要占用承诺，避免多个暂停府胚重复承诺同一容量；府胚主动放弃时才释放承诺。没有天然容量的角色仍可处于四个阶段；获得合法扩府后才可能承诺或建成紫府。

扩府绑定必须声明为永久的原子“紫府容量上限 +1”效果，并且其 `carrierKind` 为物品／扩府来源；它不得直接改变府属、阶段、槽位、位格、神通数量或丹相。结丹锁生效后不得新增、删除或替换 `expansionGrants`。

## 四、五府状态与每府唯一镇府神通

每条 `mansionState` 包含 `mansionKind`、`state` 与该状态所需的负载。允许状态仅为 `NOT_BUILT`、`EMBRYO`、`COMPLETE`。

| 状态 | 必须存在 | 必须缺失 | 校验结果 |
|---|---|---|---|
| `NOT_BUILT` | `mansionKind`、`state` | 府胚、府体绑定、镇府神通、已建容量占用 | 不承诺容量，也不产生府体或神通效果。 |
| `EMBRYO` | `embryoId`、`sourceSpellId`、`upgradePlanId`、`continuousProgress`、`progressChannelId`、关联的府胚行动／暂停事实 | 府体绑定、镇府神通实例、已建府标记 | 目标府属和源术法固定；每次行动只推进同一府胚。 |
| `COMPLETE` | `mansionInstanceId`、`mansionBodyEffectBindingId`、`guardianAbilityInstanceId`、`sourceSpellId`、`upgradePlanId`、`sourceSpellDisposition` | 未完成府胚字段 | 同时拥有一项固定府体效果和恰一项镇府神通；二者不得只存在其一。 |

额外约束如下。

1. `mansionKind` 在五条状态中全局唯一；同类双府、缺少任一府属行或未知府属均失败关闭。
2. `EMBRYO` 与 `COMPLETE` 的 `sourceSpellId` 必须指向兼容该府属、具备升格方案的源术法；`upgradePlanId`、源术法处置（`RETAIN`／`REPLACE`／`INTERNALIZE`）和神通形态必须与该方案一致。契约不为缺失的兼容府属、知识、行为、材料、环境或防刷条件生成资格对象。
3. 一个 `COMPLETE` 府的 `guardianAbilityInstanceId` 必须在 `guardianAbilities` 中精确出现一次；一个镇府神通实例也不得由两座府共同承载。它不占普通术法／神通槽，也不因府数自动产生槽位、位格或丹相。
4. 府胚不得启用府体或镇府神通；开府成功才原子建立二者。开府失败、强制失败或读取存档都不得生成只含府体或只含神通的半成品。
5. `mansionBodyEffectBindingId` 必须分别对应下表的固定机制，而非镇府神通、通用槽位或第二效果包。

| `mansionKind` | 府体稳定键 | 触发／目标／效果类型边界 |
|---|---|---|
| `MING` | `MANSION_BODY_MING_YUAN_HUIHU`（命元回护） | 受击伤害结算后／持有者生命类资源／恢复资源。 |
| `HUN` | `MANSION_BODY_HUN_LINGTAI_DINGPO`（灵台定魄） | 负面状态将写入时／待写入状态实例／阻止状态写入。 |
| `SHI` | `MANSION_BODY_SHI_SHENGUAN_RUWEI`（神观入微） | 侦知查询结算时／可探知对象与信息／揭示信息。 |
| `WU` | `MANSION_BODY_WU_WUJI_SHANCHENG`（悟机善成） | 主动修炼行动完成时／该行动进度记录／修正进度。 |
| `YUN` | `MANSION_BODY_YUN_JIYUAN_SHIZHAO`（机缘示兆） | 合法机缘检索结算时／已合法生成的线索／揭示信息。 |

## 五、平铺效果、镇府神通与节点

### 5.1 `effectBinding`

每条 `effectBinding` 必须恰有下列语义字段：`effectBindingId`、`carrierKind`、`carrierId`、`order`、`trigger`、`conditions`、`target`、`atomicEffectType`、`parameters`。`order` 只在同一 `carrierId` 内排序，连续结果由同载体的多条绑定按序表达。

- `carrierKind` 只允许 `FOUNDATION`、`MANSION_BODY`、`GUARDIAN_ABILITY`、`ENHANCEMENT_NODE`、`EXPANSION_GRANT`、`CULTIVATION_ACTION`。绑定的 `carrierId` 必须解析到对应载体。
- `conditions` 是明确的事实条件列表；府间条件必须直接写为“持有指定另一类完整紫府”，不得通过全局共鸣或嵌套包推断。
- `parameters` 的每个键必须是原子效果类型的已知参数名，并以数值档案引用或 fixture 专用字面量表达。不存在空参数的隐式默认值。
- 下列任何字段或等价结构均非法：`effectPackageId`、`nestedEffectBindingIds`、`children`、`subEffects`、自引用 ID、循环引用、未声明的自动触发链。

### 5.2 `guardianAbility`

`guardianAbility` 的必填字段为 `abilityInstanceId`、`abilityDefinitionId`、`mansionInstanceId`、`sourceSpellId`、`upgradePlanId`、`sourceSpellDisposition`、`form`、`effectBindingIds`。`form` 仅 `ACTIVE`、`PASSIVE`、`TRIGGERED`，必须与源术法升格方案相符。每项都必须保留来源术法的核心主题；本契约不把它转换为新的丹相主枢实例。

`effectBindingIds` 可以为空，只有在神通本体确无可结算效果时允许；一旦引用任何效果，所有引用必须存在且属于该 `abilityInstanceId`。无法找到来源紫府、固定神通本体、源术法处置、形态或平铺效果绑定时，建成府无效。

### 5.3 `enhancementNode`

节点是镇府神通的可选配置，不是统一等级或经验条。每条节点必须包含 `nodeId`、`abilityInstanceId`、`nodeKind`、`requirements`、`effectBindingIds`；`nodeKind` 仅 `BEHAVIOR`、`CULTIVATION`、`RESOURCE`、`INTER_MANSION`、`SPECIAL`。

- `BEHAVIOR`、`CULTIVATION`、`RESOURCE`、`SPECIAL` 必须分别引用可记录行为、固定周期、明确资源或明确传承／事件／环境条件。
- `INTER_MANSION` 必须额外给出不同于来源府的 `requiredCompleteMansionKind`；该府未建或仍为府胚时节点不得生效。
- 节点只能增强其所属神通的明确平铺绑定，不能隐式改写本府固定府体效果、容量、府属平等或通用槽位。

## 六、固定周期行动与闭关停止事实

`cultivationActionState` 记录一个正在保留或刚在稳定边界结算的行动，而非创建另一套成长服务。必填字段为 `actionStateId`、`actionKind`、`status`、`targetRef`、`fixedCycleDefinitionId`、`lastStableBoundaryId`、`committedCycleIds`、`progressChannelId`、`numericProfileRefs`。

| `actionKind` | `targetRef` 必须指向 | 特有硬边界 |
|---|---|---|
| `FOUNDATION_TRIAL` | 主修功法、筑基方案、地点与准备资源 | 成功才创建唯一 `foundationState` 并进入 `PHASE_1`；失败不产生残缺或低品道基。 |
| `FOUNDATION_NURTURE` | 同一 `foundationInstanceId` 与阶段目标 | 只在筑基期、未完成第四阶段时合法；只提交同一连续进度。 |
| `MANSION_EMBRYO_NURTURE` | 同一 `embryoId`、府属与源术法 | 只推进该府胚；恢复时不得自动替换目标或源术法。 |
| `MANSION_OPENING_TRIAL` | 完整府胚与当前开府条件 | 不可中途离开；成功才原子建立府体与唯一镇府神通，失败不建成。 |

`status` 只允许 `READY`、`ACTIVE`、`PAUSED`、`COMPLETED`、`FAILED`、`TERMINATED`。每个 `committedCycleId` 都代表一次完整的时间、资源、世界事件与结果提交；重复 ID、半周期记录、读档后重复提交或无资源却标记已提交均为非法。数值成本、周期长度、损耗与阈值必须由 `numericProfileRefs` 指向已锁定档案，不得由本对象补默认值。

`closedRetreatPlan` 只能保存 `actionStateId`、同一 `targetRef` 与显式 `stopConditions`。允许停止原因为：

`WAITING_RESPONSE`、`INSUFFICIENT_NEXT_CYCLE_RESOURCES`、`TARGET_COMPLETED`、`ACTION_FAILED`、`INJURY_UNRESOLVED`、`ACTION_INVALIDATED`、`PLAYER_GUARD`、`CHAPTER_OR_UNLOCK_BOUNDARY`、`MANUAL_PAUSE`。

闭关每次只请求同一行动的下一个完整周期，不能预支收益、自动改选目标、自动重试失败考验或在开府考验内部暂停。若同一稳定边界同时满足终局和其他停止原因，`COMPLETED` 或 `FAILED` 必须先结算，不能被普通停止原因覆盖。

## 七、结丹永久锁

`jindanLock.status` 只能为 `PRE_JINDAN` 或 `FORMED`。

- `PRE_JINDAN`：不得携带锁定快照；养基、扩府、府胚与开府仍按当前事实逐项校验。
- `FORMED`：必须有 `formationSnapshot`，其中精确复制 `foundationInstanceId`、`phase`、`naturalMansionCapacity`、合法扩府 `grantId` 集合、五府状态、每座建成府的府体绑定和镇府神通实例 ID。当前值必须与快照逐字段相同。

导入 `FORMED` 状态前必须同时满足：`phase=PHASE_4`、至少一座 `COMPLETE` 府、没有 `EMBRYO` 府，并且每座建成府有完整府体和镇府神通。锁定后，新增／删除／替换道基、阶段、扩府、府属、府体绑定或镇府神通实例均失败关闭；仅已存在镇府神通的明确强化节点可继续完成。金丹相关的主／辅承载、丹枢与丹相在后续金丹契约中表达，本对象不能创建第二丹相或改变位格边界。

## 八、旧字段与现有 DataConfig 的迁移边界

当前 `Characters.csv` 没有本契约字段；`CharacterData.developedMansions`、`mansionBindings`、`realmStage`、`legacyDanJiType` 及运行时 `Character.CalculateSlotLimits` 是旧兼容结构，不是新 schema 的输入或默认值。尤其 `developedMansions` 的旧 `气府` 与按府数加通用槽位语义均与本契约冲突。

因此，未来导入器必须：

1. 只在显式 `foundationPurpleMansionState` 根对象出现时读取本契约；不得从旧字段补齐道基、容量、府胚、府体或镇府神通。
2. 把新根对象与任何旧道基品级、五段进度、`developedMansions`、`mansionBindings`、旧丹基字段的并存视为整表失败；迁移由 `D-FPD-MIGRATE-01` 另行授权。
3. 不把紫府数量映射为普通术法／神通槽、金丹位格或丹相数量；这些旧运行时行为不能反向成为静态契约。

## 九、fixture 规格与稳定失败原因

fixture 使用与生产相同的根结构，并额外填写 `fixtureId`、`expect` 与仅测试可用的 `fixtureOnlyNumericProfile`。`expect=ACCEPT` 的 fixture 只能含其显式测试字面量；`expect=REJECT` 必须只违反列出的目标规则，以便 `D-FPD-SCHEMA-01B` 产生稳定错误原因。

| Fixture ID | 预期 | 最小输入条件 | 必须验证的结果 |
|---|---|---|---|
| `fpm.valid.phase1-empty` | ACCEPT | 一个 `PHASE_1` 道基，天然／已释放／扩府／总容量均为零，五条 `NOT_BUILT` 府，无行动、无结丹锁。 | 四阶段链允许无天然容量起步；五府行完整且未误建府。 |
| `fpm.valid.one-complete-mansion` | ACCEPT | `PHASE_4`、天然容量一、总容量一；命府 `COMPLETE`，其余四府未建；命府有精确一条命元回护绑定和一项镇府神通。 | 已建府与容量一致；府体和神通并列且一对一。 |
| `fpm.valid.capacity-upper-bound` | ACCEPT | `PHASE_4`、天然容量三、两个不同扩府授权、总容量五，五府均建成且各有不同镇府神通实例。 | 五座是合法上界；两个扩府授权均为独立永久 +1 绑定。 |
| `fpm.valid.paused-embryo` | ACCEPT | 一座建成府加一座带固定目标／源术法的暂停府胚；两者承诺不超过总容量；闭关引用同一府胚行动。 | 府胚承诺容量、暂停可恢复、尚不产生府体或神通。 |
| `fpm.invalid.capacity-overflow` | REJECT `FPM_CAPACITY_OVERFLOW` | 已建府与府胚承诺数大于派生总容量，或手填总容量与派生值不等。 | 不截断府或自动增加容量。 |
| `fpm.invalid.duplicate-mansion-kind` | REJECT `FPM_DUPLICATE_MANSION_KIND` | 两条府状态同为任一合法府属，或五府覆盖不完整。 | 同类双府不被合并、替换或静默忽略。 |
| `fpm.invalid.complete-missing-binding` | REJECT `FPM_COMPLETE_MISSING_BINDING` | `COMPLETE` 府缺少府体绑定、镇府神通实例、来源术法、升格方案或任一一对一引用。 | 不生成半成品府。 |
| `fpm.invalid.recursive-effect` | REJECT `FPM_RECURSIVE_EFFECT_BINDING` | 任一效果绑定包含子绑定／效果包／自引用／循环引用。 | 不展开递归包，也不改写为隐式顺序效果。 |
| `fpm.invalid.jindan-add-mansion` | REJECT `FPM_JINDAN_LOCK_MUTATION` | `jindanLock=FORMED` 的快照没有某府，而当前状态新增该府、扩府授权或镇府神通。 | 结丹后不能新增府、扩府或替换既有输入。 |
| `fpm.invalid.legacy-and-new-mixed` | REJECT `FPM_LEGACY_SCHEMA_MIXED` | 新根对象同时出现旧五段／品级、`developedMansions`、`mansionBindings` 或旧丹基字段。 | 不从旧字段回退，也不允许两套 schema 并存。 |
| `fpm.invalid.unknown-phase` | REJECT `FPM_UNKNOWN_PHASE` | 阶段不在四个稳定值，或连续进度与边界档案解析出的阶段不一致。 | 不猜测相邻阶段或默认边界。 |

这些 fixture 是后续导入器、Unity 数据对象与 BattleSim 投影的共同验收输入；它们不代表生产角色、数值平衡、NPC 权重或运行时实现。

## 十、D-FPD-SCHEMA-01B 实施门槛

后续实施必须以本文件为唯一静态语义来源，并在整表创建或更新任何 asset 前完成字段、引用、派生容量、状态、平铺效果、行动与结丹锁校验。实现若需要默认数值、递归效果包、旧字段兼容、第二成长服务或修改本契约语义，必须停止并转为 `pending_decision`，不得在导入器内创作机制。
