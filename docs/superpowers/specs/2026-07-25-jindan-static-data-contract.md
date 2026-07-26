# 金丹静态数据契约

状态：✅ 已锁定（D-JD-SCHEMA-01A，2026-07-27）；本文件为后续导入器、检查器与 fixture 定义金丹静态语义。它不实现 CSV、asset、Unity／BattleSim 行为、生产角色、正式效果内容或数值。

## 一、范围、权威与失败关闭

本契约承接《元婴锚点与金丹位格设定》《金丹基础效果装配与冲突规则》《金丹位格证位与争位系统设计》以及 `foundationPurpleMansionState`。它只定义已经成丹并拥有至少一个稳定真实位格的角色状态；证位履历、空位占据、争位进程与世界位置注册表仍由各自系统拥有。

- 根对象为 `jindanStaticState`。后续实现可以拆为 CSV 分表或对象序列化，但不得改变本文件的字段语义、基数、派生规则或拒绝原因。
- `roadId` 只能解析到现行十七道路；`positionId`、`proofProfileId`、基础效果、兼容档案、镇府神通、数值档案和表现档案必须解析到同一批次或已声明的外部权威表。未知、缺失、歧义或跨角色引用一律整表拒绝。
- 本根对象只能引用同角色、已锁定的 `foundationPurpleMansionState`：其 `jindanLock.status=FORMED`、道基为 `PHASE_4`、没有 `EMBRYO` 府，且至少一座府为 `COMPLETE`。不从旧字段、运行时槽位或名称反推任何缺失输入。
- 所有未锁定的资源量、冷却、充能、展开／维持／冲突费用和数值强度只能引用已锁定的档案；生产数据不得填字面量或默认值。fixture 可在 `fixtureOnlyNumericProfile` 中声明其最小字面量，且不得被导入为生产内容。
- `jindanStaticState` 只保存稳定真实位格。临时权限、证位候选、争位尝试与失位／死亡历史不属于本根对象，不能伪装为第四实位、稳定装配或替代承载。

## 二、根对象与唯一归属

```text
jindanStaticState
├─ schemaId / schemaVersion
├─ characterId
├─ foundationPurpleMansionStateRef          # 同角色、FORMED 的唯一紫府输入根
├─ mansionInputs[5]                         # 五府完整冻结输入，不可省略第四、第五府
├─ jindanCoreBinding                        # 恰好一个、不可变
├─ danxiang                                 # 恰好一个聚合载体
├─ stablePositionBindings[1..3]             # 源／化／界各至多一项
└─ abilityLedgerBindings[]                  # 全部已建府神通的唯一实例账本
```

| 字段 | 必填 | 约束与唯一权威 |
|---|---|---|
| `schemaId` / `schemaVersion` | 是 | 精确标识本契约版本；未知版本不得兼容读取。 |
| `characterId` | 是 | 角色稳定 ID；导入批次内只能出现一次。 |
| `foundationPurpleMansionStateRef` | 是 | 只引用同一 `characterId` 的唯一紫府状态根；不得指向旧 `developedMansions`、`mansionBindings`、`realmStage`、`legacyDanJiType` 或运行时槽位。 |
| `mansionInputs` | 是 | 恰五行，覆盖 `MING`、`HUN`、`SHI`、`WU`、`YUN` 各一次，逐字段镜像被引用根的冻结输入；不是可手填的部分选择列表。 |
| `jindanCoreBinding` | 是 | 单对象，不能为数组或可选后备；见第三节。 |
| `danxiang` | 是 | 单对象，不能为数组、分身或第二聚合载体；见第三节。 |
| `stablePositionBindings` | 是 | 一至三项，且只可各有一项 `SOURCE`、`TRANSFORMATION`、`DOMAIN`；见第四节。 |
| `abilityLedgerBindings` | 是 | 对 `mansionInputs` 中每项 `COMPLETE` 府的镇府神通各有且仅有一条；未建府不生成账本。 |

`mansionInputs` 的每行至少含 `mansionKind`、`state`。当状态为 `COMPLETE` 时，还必须含 `mansionInstanceId`、`mansionBodyEffectBindingId`、`guardianAbilityInstanceId`、`sourceSpellId`、`upgradePlanId`、`sourceSpellDisposition`；这些值必须精确等于 `foundationPurpleMansionStateRef` 当前值及其 `formationSnapshot` 中可比较的冻结值。结丹后不得增加、删除、替换或以缩短数组方式排除任何一行。

`mansionInputs` 始终表达全部五府：一府、三府与五府结丹的差别只在其中 `COMPLETE` 行数，不在根对象形状，也不会推导出第四位格、第二丹枢或第二丹相。

## 三、唯一丹枢核心与唯一丹相

`jindanCoreBinding` 必须含 `jindanCoreBindingId`、`jindanInstanceId`、`boundDanshuCoreId`、`formationTransactionId` 与 `formationVersion`。`jindanCoreBindingId` 对应现有 `JindanCoreState.CoreBindingId` 的唯一绑定身份；`boundDanshuCoreId` 是成丹时不可替换的丹枢核心。二者与第一稳定真实位格、全部五府输入和唯一丹相在同一原子事务建立。

- 后续第二、第三位只能沿用同一个 `jindanCoreBindingId` 与 `boundDanshuCoreId`，追加新位格的独立主承载；不得重筑基、重结丹、移植、复制、拆分或换绑丹枢。
- `danxiang` 必须含 `danxiangInstanceId`、`jindanInstanceId`、`danxiangNameKey`、`danxingDefinitionId`（若丹性定义存在）与 `danxiangPresentationProfileId`。它只能引用既有位格、镇府神通、府体效果、丹性和丹枢接口，不能复制神通或效果实例，也不能拥有资源、充能、冷却、代价或独立主承载账本。
- `mansionInputs` 中全部 `COMPLETE` 府均进入同一丹枢与同一丹相。未任主承载的府仍保留府体、镇府神通及显式辅助接口；第四、第五府绝不可因三实位上限而被静默忽略或降格为无效输入。
- `jindanCoreBinding`、`danxiang`、`boundDanshuCoreId`、`jindanInstanceId` 与 `danxiangInstanceId` 在角色内各自唯一；任何第二值、候补值、别名并存或跨角色共享均拒绝。

## 四、道路、基础效果与三项稳定真实位格

道路、效果与位格定义由各自外部静态表拥有；本根对象只持有稳定引用，并按下列受控字段交叉校验。

| 外部定义 | 必需稳定字段 | 本契约的校验边界 |
|---|---|---|
| 道路 | `roadId`、`jindanNameKey`、`roadPresentationProfileId`、恰三项 `baseEffectCandidateIds` | 必须属于现行十七道路；三项候选不表示同时装配。 |
| 基础效果 | `effectId`、`effectNameKey`、`effectPresentationProfileId`、适用的 `PositionCompatibilityContract` 引用 | 只能由本道路候选选择；效果名称与表现不改变道路变量、位别、目标或权限。 |
| 真实位格 | `positionId`、`roadId`、`positionType`、`positionNameKey`、`positionPresentationProfileId`、`proofProfileId` | `positionType` 仅 `SOURCE`、`TRANSFORMATION`、`DOMAIN`；须与世界 `JindanPositionRegistry` 的位格、档案、当前版本和占据事实一致。 |

每条 `stablePositionBinding` 必须含：

```text
positionId
expectedPositionVersion
roadId
positionType                         # SOURCE / TRANSFORMATION / DOMAIN
proofProfileId
equippedBaseEffectId
compatibilityProfileId
primaryCarrierAbilityInstanceId
auxiliaryCarrierAbilityInstanceIds[]
```

约束如下。

1. `stablePositionBindings` 的数量只能为一、二或三；各 `positionType`、`positionId` 和 `primaryCarrierAbilityInstanceId` 必须全局唯一。现有 `JindanCoreState.SeatBindings` 的 `PositionId`、`SeatType`、`CarrierAbilityInstanceId` 分别投影为这三项稳定事实，不另建平行的核心或承载列表。
2. 稳定实位数量是金丹初期／中期／圆满的唯一结构计数：一、二、三项。它不引入高低阶段的 `rulePriority`、自动压制、隐藏胜负权重或第四效果槽。
3. `equippedBaseEffectId` 必须属于同一 `roadId` 的三项候选之一；每个实位同一时刻只装配一项效果。持续嵌套、显式组合、费用、容量、后备和运行实例继续由效果／组合档案及运行时拥有，不另增效果槽。
4. `compatibilityProfileId` 必须是所选道路、位格、效果和主／辅接口的唯一、非空、无越权静态求交结果。空交集、歧义、缺媒介、禁止契约或失效的人工消歧均拒绝；不因名称或表现相似自动兼容。
5. `expectedPositionVersion` 是最终绑定时的条件值。导入或读档时世界位格已被占据、版本不符、档案／道路／位别不符或证位前置失效，都不得写入部分占据、半个承载或新的核心。

## 五、主承载、辅助连接与五府平等

`primaryCarrierAbilityInstanceId` 必须引用一项来自 `mansionInputs` 中 `COMPLETE` 府的镇府神通实例，并与该位格的道路、效果、档案和丹枢接口兼容。一个稳定实位恰有一项主承载；同一 `abilityInstanceId` 绝不能主承载两个稳定实位。

`auxiliaryCarrierAbilityInstanceIds` 是显式的零至多项辅助连接。每项同样必须来自已建府、通过对应兼容档案并持有合法接口；列表内不得重复，也不得包含本位格的主承载。一个神通实例可以主承载一位并辅助其他位，也可以辅助多个位，但辅助只提供档案明确开放的媒介、触发、代价改道或组合接口：它不增加稳定度、位格、效果槽、紫府数量或第二主动效果。

- `guardianAbilityInstanceId` 的来源府只说明神通出处，绝不把该府称为主府、辅府或位格独占府。
- 每座 `COMPLETE` 府仍只有一项镇府神通；血脉神通、通用神通和其他来源能力不能替代主承载，也不凭名称相同成为辅助。
- 安全洞府中的合法重铸只能原子替换某一已有实位的主承载引用；新主承载仍须兼容、来自结丹前固化的神通集合，且重铸后全部主承载仍不重复。它不得改写 `mansionInputs`、丹枢、丹相、位格占据或镇府神通内容。

## 六、`abilityInstanceId` 唯一账本与冲突引用

每项已建府镇府神通恰有一条 `abilityLedgerBinding`：

```text
abilityInstanceId                       # 主键，也是所有可变结算的唯一所有者
resourceDebitLedgerRef?
cooldownLedgerRef?
chargeLedgerRef?
costLedgerRef?
conflictReserveLedgerRef?
conflictCostProfileId?
```

问号表示该神通没有此类机制时省略；一旦机制存在，对应引用必须非空、归属同一个 `abilityInstanceId`，且其规则／数值来自已锁定档案。共享角色资源类型不授权共享可变账本：每笔资源扣除、冷却、充能、次数、代价与冲突储备的实例级所有者都必须是该神通的 `abilityInstanceId`。同一账本引用不得挂给两个实例，也不得由丹相、位格、组合或辅助连接复制、借用、并行透支或重新结算。

冲突相关引用只允许落在实际贡献该效果的神通账本上：`conflictReserveLedgerRef` 对应该 `abilityInstanceId` 的唯一公开储备，`conflictCostProfileId` 指向配置化费用档案。它们不能改由 `jindanCoreBinding`、`danxiang`、道路、位格或“组合共享池”拥有。缺失账本所有者、重复账本、冲突储备越权或以第二资源池补账均失败关闭。

## 七、稳定键、表现与本地化边界

正式显示文本不进入本契约。道路使用 `jindanNameKey`，位格使用 `positionNameKey`，效果使用 `effectNameKey`，丹相使用 `danxiangNameKey`；它们都必须是稳定键，而非候选名称、拼接标题、快捷栏文案或本地化后的字符串。

- `roadPresentationProfileId` 只提供道路共同意象；`positionPresentationProfileId` 只区分源／化／界的展开层级；`effectPresentationProfileId` 才描述具体动作与音画；`danxiangPresentationProfileId` 只描述同一丹相的聚合表现。
- 表现／本地化档案不得反向授予道路变量、效果权限、目标、范围、资源、承载或位格。未确认正式名称时保留稳定键引用，不在字段中写入候选名。
- `displayName`、`localizedName`、`roadDisplayName`、`positionDisplayName`、拼接模板、由名称推断道路／位别的字段，及任何用表现档案替代 `effectId` 的结构均非法。

## 八、旧结构与导入原子性

旧 `developedMansions`、`mansionBindings`、`realmStage`、`legacyDanJiType`、旧九品金丹、按府数增加通用槽位、按名称匹配承载，以及 `Character.CalculateSlotLimits` 的旧语义都不是本契约输入或默认值。新根与上述任一字段／等价字段并存时整表拒绝，不进行映射、截断、填默认值或双 schema 回退。

`D-JD-SCHEMA-01B` 必须先完成本根、引用、五府输入、核心／丹相唯一性、三实位、主辅承载、账本所有者、冲突引用和稳定键的整表预校验，才可创建或更新任何 asset。结丹第一位的核心、丹相、全输入快照、实位与主承载必须原子建立；追加第二、第三位同样必须原子验证后写入。任一失败不得产生半个 `SeatCarrierBinding`、半个丹相、局部账本或替代核心。

## 九、fixture 规格与稳定拒绝原因

fixture 与生产使用相同根结构，另加 `fixtureId`、`expect` 和仅测试可用的 `fixtureOnlyNumericProfile`。`ACCEPT` 仅含其显式最小值；`REJECT` 每次只违反所列目标规则，以便导入器产生稳定原因。

| Fixture ID | 预期 | 最小输入条件 | 必须验证的结果 |
|---|---|---|---|
| `jd.valid.one-mansion-one-seat` | ACCEPT | 五府行完整，只有一府 `COMPLETE`；唯一核心、唯一丹相、一项 `SOURCE` 稳定位格，以该府唯一神通作主承载并有一条实例账本。 | 一府一位合法；未建四府仍作为完整输入行，不被误认为缺行或第二输入。 |
| `jd.valid.three-mansion-three-seats` | ACCEPT | 三府建成、三项不同镇府神通、一个核心／丹相；`SOURCE`、`TRANSFORMATION`、`DOMAIN` 各一项，三项主承载不同，可有已声明辅助引用。 | 三府三位合法；每位仅一项基础效果，辅助不复制账本。 |
| `jd.valid.five-mansion-three-seats` | ACCEPT | 五府均建成、五条实例账本、一个核心／丹相、三项不同实位与主承载；另两府保留为同一丹相输入，可按档案辅助或仅保留原有效果。 | 五府三位合法；第四、第五府不失效，也不生成第四、第五实位或第二丹相。 |
| `jd.invalid.input-not-formed` | REJECT `JD_FPM_INPUT_NOT_FORMED` | 紫府根未锁定、不是 `PHASE_4`、含府胚、无已建府，或五府输入与冻结源不一致。 | 不从可变筑基状态创建金丹根。 |
| `jd.invalid.missing-mansion-input` | REJECT `JD_MANSION_INPUT_INCOMPLETE` | 少任一府属行，或未收录已建的第四／第五府。 | 所有五府均参与同一输入结构。 |
| `jd.invalid.unknown-static-reference` | REJECT `JD_UNKNOWN_STATIC_REFERENCE` | 单独将道路、效果、位格、道证档案、兼容档案、神通、数值档案或表现／本地化稳定键之一指向未知或歧义 ID。 | 不猜测候选、名称或默认档案。 |
| `jd.invalid.effect-outside-road-candidates` | REJECT `JD_EFFECT_LOADOUT_INVALID` | 为某实位装配不属于其 `roadId` 三项候选的效果，或使用与位格不兼容的档案。 | 每位只装配本道路的一项合法效果。 |
| `jd.invalid.fourth-stable-position` | REJECT `JD_STABLE_POSITION_LIMIT` | 稳定位格多于三项，或试图新增第四种／重复位别作为稳定实位。 | 不截断、合并或把第四项改名为临时项。 |
| `jd.invalid.second-core` | REJECT `JD_CORE_NOT_UNIQUE` | 出现第二核心、候补核心、不同 `jindanCoreBindingId` 或替换已绑定核心。 | 只保留唯一不可变丹枢核心。 |
| `jd.invalid.second-danxiang` | REJECT `JD_DANXIANG_NOT_UNIQUE` | 出现第二丹相、分丹相、第二／第三位各自丹相或丹相私有账本。 | 第二、第三位只扩展同一丹相。 |
| `jd.invalid.duplicate-primary-carrier` | REJECT `JD_PRIMARY_CARRIER_DUPLICATE` | 同一 `abilityInstanceId` 主承载多个稳定实位。 | 主承载按实例而非名称、模板或快捷栏唯一。 |
| `jd.invalid.illegal-carrier-reference` | REJECT `JD_CARRIER_REFERENCE_INVALID` | 主／辅引用未建府、其他角色、通用能力、来源不符或不兼容神通。 | 不创建主府／辅府或隐式辅助连接。 |
| `jd.invalid.shared-instance-ledger` | REJECT `JD_ABILITY_LEDGER_OWNERSHIP_INVALID` | 缺实例账本、两个实例共用可变账本、丹相复制账本，或账本所有者不是 `abilityInstanceId`。 | 冷却、充能、资源、代价与冲突储备只结算一次。 |
| `jd.invalid.conflict-reference-foreign` | REJECT `JD_CONFLICT_REFERENCE_INVALID` | 冲突储备／费用档案由核心、丹相、其他实例或共享池拥有。 | 冲突引用归实际神通实例账本。 |
| `jd.invalid.legacy-or-display-string` | REJECT `JD_LEGACY_OR_DISPLAY_FIELD` | 携带旧九品／旧槽位／旧紫府字段，或写入显示名、候选名、拼接标题。 | 不兼容读取旧语义；只接受稳定 ID／键。 |

这些 fixture 不代表十七道路的正式效果内容、生产数值、角色、战斗行为或 UI。`D-JD-SCHEMA-01B` 必须使用相同原因完成整表失败关闭；若实施需要第二丹枢／丹相、第四实位、共享可变账本、旧九品兼容或默认值，必须停止并转为 `pending_decision`，不得在导入器内创作机制。

## 十、D-JD-SCHEMA-01B 实施门槛

后续实现只能以本文件和已锁定的 `foundationPurpleMansionState` 契约为静态语义来源。它必须能直接消费第三、四、五、六、七、九节的字段与拒绝码，且不需要补写名称、规则、数值、默认承载、第二导入链或生产内容；否则不得开始写入实现。
