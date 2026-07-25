# 统一攻击档案数据契约

状态：已锁定的数据契约。本文只定义后续 CSV、Unity asset 与 BattleSim 投影的边界；不创建 CSV/asset、不导入、不修改 BattleSim/Unity 消费者，也不登记任何生产攻击参数。

## 目的与现状

现行攻击输入分散在 BattleSim 的 `GameData.AttackProfile`、`ArtConfig`、`DivineConfig`，以及 Unity 的 `SpellData`、`DivineSkillData`、`Spells.csv`、`Skills.csv`。它们没有共同的稳定 ID、共同表头或一对一的跨端投影。`Character.BasicAttackProfile` 目前以 `GameData.UnarmedBasicAttack` 初始化，留待主战法宝替换；术法和神通又分别使用不同的倍率、资源和冷却字段。

本契约把后续的 `AttackProfiles.csv` 行作为唯一攻击参数事实。CSV、Unity asset 与 BattleSim 对象不是三个可独立编辑的来源：它们是同一稳定 ID 的一条数据及其受控投影。

范围目标语义沿用已实施的 `AreaTargetingConfig` 与 `HexBattlefield.ResolveAreaTargeting`：范围中心、形状、施法距离、已声明效果阻断、阵营和状态资格均是攻击档案数据；它们不是 Unity 的 `SpellRange` 显示枚举、普通视线、角色阵营或调用方默认值。

## 唯一权威关系

后续实现固定以下单向关系：

```text
AttackProfiles.csv 的唯一 attackProfileId 行
  -> DataConfigImporter 的整表校验
  -> 由导入器生成/更新的 AttackProfileData asset
  -> Unity 运行时只读 asset 投影
  -> BattleSim 的只读 profile 投影
```

1. `attackProfileId` 是跨端、跨存档和 fixture 使用的稳定主键；显示名、asset 路径、数组槽位、`GameData` 静态字段名均不能充当 ID。格式为小写 ASCII `^[a-z][a-z0-9_]*$`，在整表内唯一且大小写不折叠。
2. `AttackProfiles.csv` 是攻击参数的唯一可编辑生产者。Unity asset 只能由同一导入器以 ID 生成或更新；BattleSim 只能从同一 CSV 行或其测试读取投影取得参数。两端均不得再以 `SpellData`、`DivineSkillData`、`GameData` 静态配置、构造器默认值或显示文本补齐字段。
3. asset 的稳定身份由 `attackProfileId` 决定，而非本地化后的名称。导入器必须检测同 ID 的重复行、同一路径的 ID 冲突和既有 asset 的 ID 不一致；任一冲突整表失败，不保留半导入结果。
4. 现有 `Spells.csv`、`Skills.csv`、既有 ScriptableObject、`GameData` 配置和 `GameData.UnarmedBasicAttack` 仅是迁移输入或旧消费者的事实，不与新表共同权威。某个消费者切换到 `attackProfileId` 后，不得再读这些遗留字段作为回退。

主战法宝的装备/主武器所有者不在本文新建。`profileKind=basic` 的档案通过那个既有或后续装备所有者引用 `attackProfileId`；`basicBindingKind` 只区分“由主战装备选择”与“明确的无装备兜底”。没有主战装备引用、也没有明确无装备档案时，装配失败；不得回落到 `GameData.UnarmedBasicAttack`。功法对普攻的修正或替代仍必须引用其独立攻击档案，不能把功法名称解释成普攻来源。

## CSV 表头与字段

后续文件为 `src/Assets/DataConfig/AttackProfiles.csv`。首行必须恰为下列表头，不能增删、重排或用未知列偷偷传递运行时语义：

```csv
attackProfileId,displayNameKey,profileKind,basicBindingKind,contentScope,sourceAffiliation,realmRequirementId,elementRequirementId,effectType,damageElementId,physicalDamageMultiplier,soulDamageMultiplier,healAmount,buffMultiplier,defensePenetration,resourceKind,resourceCost,cooldownTicks,minCastRange,maxCastRange,targetingMode,areaCenterKind,areaShapeKind,areaRadius,areaLength,areaFanHalfAngleSteps,areaFacing,areaInnerRadius,areaEffectBlockers,areaAllowedFactions,areaAllowedStates,isDomain,isBloodline,specialEffectTextKey
```

空值只表示该字段按本表的条件“不适用”；它绝不表示“让 Unity/BattleSim 使用现有默认值”。所有数值使用不依赖当前区域设置的十进制文本，布尔值只能是 `0` 或 `1`。

| 字段组 | 字段 | 适用范围与唯一语义 |
|---|---|---|
| 身份与展示 | `attackProfileId`、`displayNameKey` | 每行必填。前者用于全部引用与比对；后者是可本地化展示键，不能反向取代 ID。未知展示键失败关闭。 |
| 档案种类 | `profileKind`、`basicBindingKind` | `profileKind` 只能为 `basic`、`art`、`divine`。`basicBindingKind` 仅 `basic` 必填，值为 `main_equipment` 或 `unarmed_fallback`；其他种类必须为空。它不声明武器内容，也不替代装备所有者的外部引用。 |
| 内容可用性元数据 | `contentScope`、`sourceAffiliation`、`realmRequirementId`、`elementRequirementId` | 术法与神通必须保留现有的 `contentScope`、来源元数据和可用性门槛；`sourceAffiliation` 永远不是目标阵营。基础攻击没有已锁定的等价内容门槛时这些列必须显式为空。已声明的 requirement ID 必须在其现有注册表中存在；`element_none` 只能作为显式值，不能由空值推导。 |
| 效果与伤害 | `effectType`、`damageElementId`、`physicalDamageMultiplier`、`soulDamageMultiplier`、`healAmount`、`buffMultiplier`、`defensePenetration` | `effectType` 使用现有 `SpellType` 语义的文本值 `physical`、`magic`、`heal`、`buff`、`debuff`、`movement`、`hybrid`。物理、神魂和混合伤害分别只读取对应的一个或两个倍率列；治疗、增益等字段也必须由行显式给出或标为不适用，不能由默认值补齐。`damageElementId` 是现有 `element_*` 规范键，未知键失败关闭。`defensePenetration` 保留当前 `DivineConfig.DefPen` 的承载，只有有明确效果语义的行可填。 |
| 资源与冷却 | `resourceKind`、`resourceCost`、`cooldownTicks` | `resourceKind` 只能为 `none` 或 `mp`。基础攻击必须显式声明 `none`、资源值 `0` 和冷却 `0`；术法／神通的 MP 和冷却均来自其行。`cooldownTicks` 采用 Unity 现有“刻”单位；不得把 BattleSim 的当前回合计数或 Unity asset 默认值当作同义数值。 |
| 施放距离 | `minCastRange`、`maxCastRange` | 每行必填、非负且 `minCastRange <= maxCastRange`。单目标行对应现有 `AttackProfile`/`ArtConfig`/`DivineConfig` 与 Unity `minRange`/`maxRange`；范围行对应中心施放距离。`SpellRange` 只能在 Unity 侧从该区间派生为展示，不能成为反向输入。 |
| 范围与目标 | `targetingMode` 及所有 `area*` 列 | `targetingMode` 只能为 `single` 或 `area`。`single` 时全部 `area*` 字段必须为空；`area` 时由下节的完整配置承载中心、形状、阻断与目标资格。 |
| 神通特有描述 | `isDomain`、`isBloodline`、`specialEffectTextKey` | 仅 `divine` 可填写，保留当前神通 asset 的领域、血脉和显示描述边界。文本键不是执行规则；未知键失败关闭，不能把自然语言描述解释为新的效果。 |

`SpellData.range`/`DivineSkillData.range`、`GameData` 的 `Name`/`Type`/`Mult` 和 Unity 字段初始值均为旧载体字段；迁移后它们必须由本表映射或被删除，不能与本表双写。

## 范围配置与跨字段合法性

范围字段直接对应现有 `AreaTargetingConfig`、`AreaShapeConfig`、`AreaEffectBlocker`、`AreaTargetFaction` 与 `AreaTargetState`。它们定义“怎样请求已存在的空间查询”，不登记任何具体技能形状、尺寸、距离、阵营或阻断内容。

| 配置 | 合法值与约束 |
|---|---|
| `areaCenterKind` | `caster` 或 `target_cell`。`caster` 的中心距离恒为 0，因此当前契约要求其施法区间为 `0..0`；`target_cell` 使用本行的 `minCastRange`/`maxCastRange`。 |
| `areaShapeKind` | `circle`、`line`、`fan`。圆形要求 `areaRadius >= 0`、`areaLength=0`、`areaFanHalfAngleSteps=0`、`0 <= areaInnerRadius <= areaRadius`；直线要求 `areaRadius=0`、`areaLength>0`、扇角为 0、`0 <= areaInnerRadius < areaLength`；扇形要求 `areaRadius=0`、`areaLength>0`、扇角只能为 0 或 1、`0 <= areaInnerRadius < areaLength`。 |
| `areaFacing` | 直线和扇形必填，值为现有六角朝向的稳定文本。圆形必须为空：朝向对圆形没有语义，后续 BattleSim 投影不得为满足旧 `AreaShapeConfig` 构造器而偷偷填入任意方向。 |
| `areaEffectBlockers` | 逗号分隔、按字典序的集合，仅 `none` 或 `directed_edge`。它只声明效果传播是否受有向边阻断；普通视线、实体障碍和 `sight_blocked` 不得被写入该列或当作范围命中前置。 |
| `areaAllowedFactions` | 非空、逗号分隔、按字典序的 `enemy`、`ally`、`self` 子集。它独立于 `sourceAffiliation`。 |
| `areaAllowedStates` | 非空、逗号分隔、按字典序的 `alive`、`corpse` 子集。未声明 `corpse` 的档案不得作用尸体。 |

导入器和 BattleSim fixture 必须保持既有拒绝优先级：无效/越界目标格、施法距离不合法、已声明效果阻断、状态/尸体不符、阵营不符、范围内无合法目标。`effectiveRangeModifier` 仍是查询时修正，不能写回 CSV、asset 或基础距离。

## 导入边界与失败关闭

后续 `DataConfigImporter` 必须先读取并验证完整表，再创建或更新任一 asset。它不能像现有 `ImportSpells`/`ImportSkills` 一样跳过短行、记录警告后继续，亦不得留下部分新 asset。至少下列情况必须使导入非零失败并保持目标集合未变：

| 失败类别 | 必须失败的条件 |
|---|---|
| 表结构 | 文件缺失、没有表头、表头不精确、重复列、额外列、短行、重复 `attackProfileId`、非法 ID 或空的条件必填字段。 |
| 枚举/数值 | 未知 `profileKind`/`effectType`/资源种类/布尔值、非数值、负资源或冷却、非法距离区间，或“值存在但当前 effectType 不适用”。 |
| 引用 | 未知展示键、元素键、境界/元素需求键、内容范围，或装备所有者引用到不存在的 `attackProfileId`。 |
| 范围 | `single` 混入范围列、`area` 缺少任一必需列、形状参数不满足上表、未知朝向/阻断/阵营/状态、空目标资格，或把普通视线语义编码为范围配置。 |
| 投影 | asset ID 与行 ID 不一致、同 ID 映射多个 asset、Unity 或 BattleSim 无法承载一项已声明语义、或投影企图以旧字段/默认值填充本行空缺。 |

BattleSim 当前 `ArtConfig.Cooldown`/`DivineConfig.Cooldown` 注释为回合冷却，而 Unity `cooldownTicks` 是刻数；仓库尚无两者的权威换算。故本契约固定 CSV 的 `cooldownTicks` 为规范值，但不授权任意除法、乘法或截断。到存在独立的时间单位决定和实现前，BattleSim 投影遇到非零 `cooldownTicks` 必须报告 `battlesim_cooldown_unit_unresolved`，不能读取旧回合默认值或伪造一致结果。

## Unity 与 BattleSim 的消费投影

| 目标 | 后续允许的投影 | 禁止的来源/行为 |
|---|---|---|
| Unity `AttackProfileData` | 每个 CSV 行生成一个同 ID 的 typed asset；它承载本表的所有字段。现有 `SpellData`、`DivineSkillData` 只在受控迁移中读取旧行并被替换。 | 手工编辑 asset 覆盖 CSV、从 `SpellRange` 反写距离、保留字段初始化值作为缺列回退。 |
| BattleSim 基础攻击 | `profileKind=basic` 投影到 `AttackProfile`，再由主战装备/无装备装配点赋给 `Character.BasicAttackProfile`。 | 固定 `GameData.UnarmedBasicAttack`、门派/功法名称或测试构造器作为生产攻击来源。 |
| BattleSim 术法/神通 | `art`/`divine` 投影到各自配置载体；类型、元素、倍率、距离和范围目标字段逐项来自同一行。当前载体不能表达的已声明效果必须拒绝，而非丢列。 | `GameData` 静态表、`Character.AssignArts` 风格分支、旧数值/冷却默认值作为二次权威。 |
| 两端范围消费 | 两端共同使用本行的中心、形状、距离、阻断和目标资格；BattleSim 继续调用已有 `HexBattlefield` 空间根。 | 把 Unity 单目标 `SpellRange`、普通视线或角色来源阵营替代范围目标语义。 |

本表不声称 Unity 当前单目标控制器已经支持范围，也不把 BattleSim 当前“范围命中后仍只结算主要目标”改写为范围伤害。范围命中集合、真实伤害结算和结算候选仍分别服从既有所有者与时点。

## 迁移顺序、fixture 与差异检查

1. 本卡只锁定本文件；不产生 CSV 行、asset、导入器或消费者修改。
2. `U-COMBAT-01` 只能按本表建立 Unity asset/消费者所需的明确承载；不得另立 Unity 专属 schema。
3. 在 `D-COMBAT-01` 与 `U-COMBAT-01` 均完成后，`D-COMBAT-02` 建立没有生产内容的正反 fixture、整表导入检查和同 ID 的 Unity/BattleSim 投影差异检查。
4. 每个正 fixture 的最低输入是：稳定 ID、种类、显示键、全部适用的效果/资源/冷却/距离字段，以及当且仅当为范围时完整的范围配置。检查逐字段比较 ID、种类、效果类型、元素、两类倍率、资源、规范冷却、距离和所有范围目标字段；不按名称或 asset 路径配对。
5. 负 fixture 至少覆盖：重复 ID、缺必填列/短行、未知引用、非法数值/区间、错误的种类专属字段、三种非法形状、未知范围枚举、单目标混入范围列、asset ID 冲突、未支持效果语义以及未解决的 BattleSim 冷却单位。每个失败必须没有生成部分 asset，也不能落入旧默认值。
6. 只有 fixture 和消费者均证明同一 CSV 行的逐字段投影后，才可在独立生产迁移切片中切换具体内容。该切片必须同时移除该消费者对遗留 CSV/asset/`GameData` 硬编码值的读取；不能双读、双写或设置兼容回退。

若后续需要指定武器内容、攻击数值、范围形状尺寸、目标资格、阻断、BattleSim 冷却单位换算或某个消费者的实际所有者，而当时仍无直接事实，必须停止并转为 `pending_decision`。不得用本契约、空字符串、默认构造器或测试 fixture 补作生产决定。
