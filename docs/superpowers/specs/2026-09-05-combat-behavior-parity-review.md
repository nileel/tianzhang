# 最小战斗输入与双端行为对照合同

状态：已定案。核验基线：`0ea9998640c09903626d7fcf2b30bd964a37aac5`（2026-09-05）。本文只冻结 `U-COMBAT-PROJECTION-01`、`U-COMBAT-LOADOUT-01`、`N-COMBAT-PARITY-01` 的实施边界；不修改业务源码，不把未覆盖行为宣称为双端一致。

## 1. 事实源与裁决方法

- `docs/基础设定/角色数值设计.txt` 把 BattleSim 标为现行数值实现基线，并明确战斗二级属性来自功法篇章、装备、丹药、术法／神通或状态，而不是由裸先天属性直接补算；暴击伤害字段是基础 `150%` 之上的附加百分比点。
- `docs/superpowers/specs/2026-07-25-combat-attack-profile-data-contract.md` 把 `AttackProfiles.csv` 行定为攻击参数唯一生产事实，以 `attackProfileId` 作为稳定身份；主战装备与显式徒手引用负责选择普攻，缺引用必须失败，不能回落到 BattleSim 默认值。该合同也明确 Unity 的刻与 BattleSim 的回合没有权威换算。
- Unity 当前真实输入从 `CharacterData` 经 `CharacterRuntimeProfile`、`CharacterStateSnapshot`、保存封套和 `AdventureUnitSpawner` 入战；当前玩家链丢失显式战斗字段，敌人链只消费其中一部分。BattleSim 当前角色成长会计算自己的二级属性，不能反过来充当 Unity 存档缺失字段的迁移规则。
- 当前两端的通用伤害形状、概率比较顺序、境界／抗性、取整与 CT 并不完全相同。因此本合同只把原文能够支持、且可用中性输入隔离的交集列为“必须一致”；其余分别列为“批准保留的运行时差异”或“尚不可比”。这不是认定任一端整体正确。

## 2. 角色显式战斗输入与旧档兼容

### 2.1 唯一传递对象

`U-COMBAT-PROJECTION-01` 在 Character 模块内建立一个不可变 `CharacterCombatModifiers` 值对象。它保存 `CharacterData` 已有十四个字段的原始 `float` 值，不新增成长、装备或状态来源：

| 类别 | 字段 | 单位／解释 |
|---|---|---|
| 一级派生显式加成 | `hpBonus`、`mpBonus`、`physAtkBonus`、`magAtkBonus`、`physDefBonus`、`magDefBonus` | 原始浮点加成；仅在调用现有 `CharacterAttributes.Derive` 时逐项 `Mathf.RoundToInt`，保持当前敌人派生口径。 |
| 战斗概率／减伤 | `blockRate`、`blockReduction`、`soulShieldRate`、`soulShieldReduction`、`dodgeRate`、`critRate`、`hitRateBonus` | 百分比点，原值透传；本卡不新增截断或合法区间。 |
| 暴击伤害 | `critDamage` | 基础暴击倍率 `1.50` 之上的附加百分比点；例如 `15` 表示 `1.65`。 |

传递链固定为：

`CharacterData -> CharacterRuntimeProfile -> CharacterStateSnapshot -> GameSaveEnvelope.CharacterRecord -> Restore -> AdventureUnitSpawner.CreatePlayer -> CombatantSnapshot`

敌人不经过存档，但必须先从同一个 `CharacterCombatModifiers.FromDefinition` 建值，再由 `CreateEnemy` 消费，禁止保留另一份手抄字段清单。

### 2.2 派生与资源边界

- 不修改 `CharacterAttributes.Derive`，也不把 BattleSim 的先天／功法成长公式移植进 Unity。
- `FromDefinition` 只用六项显式加成派生一次初始一级战斗值和初始 HP／MP 资源。
- `Capture`、保存和恢复只保存原始十四字段及已经存在的最终资源；恢复时不得重新累加 HP／MP。
- `CreatePlayer` 的最大／当前 HP、MP继续读取已保存的 `CharacterResources`；肉攻、神攻、肉防、神防由基础属性、境界倍率和保存下来的六项加成重新调用现有 `Derive`。这样六项加成不会丢失，HP／MP也不会双算。
- `CreateEnemy` 用相同六项转换调用现有 `Derive`，并把八项概率／减伤字段逐项投影到 `CombatantSnapshot`。

### 2.3 schema 2 与 legacy 1

当前 `GameSaveSerializer.SchemaVersion` 为 `2`，唯一旧版本为 `1`。十四字段采用 schema 2 的可选增量字段：

- JSON 缺字段时的唯一兼容值是 `0`；不提高 schema，不增加第二条迁移分支。
- schema 1 经既有 `MigrateSchemaOne` 后仍得到十四个 `0`；不得从功法、灵根、先天、显示名或当前内容资产反推。
- schema 2 旧档同样按缺字段为 `0`；这是当前玩家链“空显式加成／零概率字段”的已确认旧行为。
- `HasCharacterPayload` 必须把任一非零显式字段视为角色载荷，避免 `hasPlayer=false` 却携带新字段的封套绕过一致性检查。
- 非零十四字段须在定义、运行时、快照、JSON 往返和入战快照中逐项相等；只有六项进入 `Derive` 时允许上述确定性整数转换。

### 2.4 功法元素

`GongFaElement` 不来自可见灵根，也不从任意名称片段、默认元素或 `Resources` 搜索推断。仓库已有 `CombatElementFacts.ResolveGongFaElement`，其显式表与 BattleSim `GameData.GongFaElements` 覆盖同一组功法名称／稳定 ID；因此玩家和敌人都必须以保存或定义中的 `GongFaId` 做精确字典查询并写入 `CombatantSnapshot.GongFaElement`。未知、空值或表外 ID 得到空字符串；本卡不扩充映射表或内容目录。

## 3. 逐角色攻击授权

### 3.1 战斗快照表达

`CombatantSnapshot` 是战斗授权的唯一运行时输入，新增或补齐以下只读集合：

- `BasicAttackProfileId`：该角色唯一选中的基础攻击档案 ID；
- `AvailableArtProfileIds` 与 `EquippedArtProfileIds`：已经完成能力 ID 映射后的已知术法档案与当前装载档案；
- `AvailableDivineProfileIds` 与 `EquippedDivineProfileIds`：已经完成能力 ID 映射后的已知神通档案与当前装载档案。

空字符串和空集合表示该角色没有相应授权，不表示“使用全局第一项”或“所有全局档案均可用”。列表去空、去重的既有快照习惯保留；装载项即使出现在槽位中，也必须同时出现在相应 available 集合才有攻击授权，以关闭 legacy／篡改存档中“装载但未知”的输入。

### 3.2 普攻选择

玩家与敌人都只接受下面两个来源之一：

1. 非空 `mainEquipmentBasicAttackProfileId`；或
2. 非空 `unarmedBasicAttackProfileId`。

恰好一个非空才合法；两者都空或同时非空时，`AdventureUnitSpawner.TrySpawn` 在实例化棋子前失败，玩家返回 `adventure_player_basic_attack_binding_invalid`，敌人返回 `adventure_enemy_basic_attack_binding_invalid`。选中的值原样写入角色 `BasicAttackProfileId`；功法名和 `GameData.UnarmedBasicAttack` 均不能补位。

### 3.3 术法／神通映射的当前边界

`CharacterData` 与 `AbilityLoadout` 的四个列表保存的是 `spell_*`／`skill_*` 能力稳定 ID；`AttackProfiles.csv` 保存的是 `attackProfileId`。当前 `ContentCatalogData` 不含术法、神通或攻击档案目录，`SpellData`／`DivineSkillData` 也没有 `attackProfileId` 外键，生产攻击表目前只有 `basic_unarmed`。因此：

- 本卡不批准把能力 ID 与 `attackProfileId` 的字符串相等当作映射，也不批准显示名、数组位置或资源扫描。
- 正式 `AdventureUnitSpawner` 在没有显式映射输入时，把术法／神通 profile 授权投影为空；现有生产术法／神通仍属“未接入正式战斗”，不是被删除或取消学习。
- `U-COMBAT-LOADOUT-01` 的术法／神通正反例只使用直接构造 `CombatantSnapshot` 的内存夹具，夹具传入的是“已完成映射后的 profileId”。它只证明战斗边界，不建立生产内容映射。
- 将来接入生产能力时必须另立数据合同，提供唯一的 ability stable ID -> `attackProfileId` 外键或映射表，并扩展对应目录／导入验证；不属于本批三张实施卡。

### 3.4 唯一校验与拒绝优先级

`CombatSession.ValidateAttack` 是直接命令与 AI `AddIfValid` 共用的唯一所有权校验。顺序固定为：现有 actor／turn -> target -> 全局 profile 存在且 kind 匹配 -> 当前角色授权 -> cooldown -> spirit -> range。

授权条件：

| kind | 条件 |
|---|---|
| `basic` | `profile.Id == actor.BasicAttackProfileId` |
| `art` | profile ID 同时存在于 `AvailableArtProfileIds` 与 `EquippedArtProfileIds` |
| `divine` | profile ID 同时存在于 `AvailableDivineProfileIds` 与 `EquippedDivineProfileIds` |

全局 profile 缺失或 kind 不匹配继续返回既有 `attack_profile_unresolved`；profile 合法但当前角色不满足上述条件统一返回新码 `attack_profile_not_authorized`。拒绝不得消耗 HP、MP、CT、冷却、随机样本或换法次数。`CombatLegalActionService` 可以做无副作用遍历，但最终必须通过同一个 `ValidateAttack`，不得保留只过滤术法的平行规则。换法成功后仍以 available + 新 equipped 的交集授权。

## 4. Unity 与 BattleSim 最小对照

### 4.1 共用输入域

首批只比较“徒手、单目标、物理、正面、中性功法、中性境界”的一次攻击：

- profile：`basic_unarmed`，`physicalDamageMultiplier=1`，元素为空，距离合法；
- 攻防：正整数，`defensePenetration=0`、`physicalResistance=0`；双方境界倍率均为 `1`；
- 无功法叠层、守势、背击、范围、状态、资源或冷却效果；
- 概率样本是 U-COMBAT-RNG-01 已锁定的四个命名 `[0,100)` 值：hit、critical、block、soulShield；物理攻击不读取 soulShield 的判定结果。

在该输入域内，基础伤害的共同表达为：

`attack * (attack / (attack + defense)) * physicalDamageMultiplier`

测试值均选择整数结果和非 `.5` 中间值，不借本卡裁决全局 midpoint rounding、命中后最小伤害或非中性境界／抗性公式。

### 4.2 必须一致的概率语义

U-COMBAT-RNG-01 已锁定正式 Unity 的命名采样顺序和边界，本合同把同一窄语义用于对照：

1. `effectiveHitRate = Clamp(100 + hitRateBonus - dodgeRate, 5, 100)`；`hitPercent <= effectiveHitRate` 命中，未命中立即得到 `0`。
2. 命中后，`criticalPercent < critRate` 才暴击；倍率为 `1.50 + critDamage / 100`。
3. 物理攻击以 `blockPercent < blockRate` 判定格挡，命中伤害乘 `1 - blockReduction / 100`。
4. 对照事件顺序为 hit -> critical -> physical block。JSON 夹具的结果同时记录 hit／critical／blocked 与最终整数伤害；测试不得只比较均值。

这项裁决基于已完成前置的正式命名样本合同、docs 对命中／暴击／格挡百分比与暴击附加点的定义，以及两端共同的概率效果；它只规范上述最小输入域，不宣称 Unity 整个 resolver 为规范。

### 4.3 `combat-parity-cases.json`

`N-COMBAT-PARITY-01` 建立一个版本为 `combat-parity-v1` 的 JSON 根对象，固定默认输入 `attack=100`、`defense=100`、倍率 `1`、两境界 `1`、抗性／穿透／元素／功法／朝向修正为中性。至少包含以下逐行预期；未列字段取根对象默认值：

| caseId | 差异输入 | 预期 hit / critical / blocked / damage |
|---|---|---|
| `base` | hit=99，crit=0，critical=100，block=0，blockSample=100 | `true / false / false / 50` |
| `hit_equal` | dodge=60，hitSample=40 | `true / false / false / 50` |
| `hit_above` | dodge=60，hitSample=41 | `false / false / false / 0` |
| `crit_below` | crit=20，criticalSample=19 | `true / true / false / 75` |
| `crit_equal` | crit=20，criticalSample=20 | `true / false / false / 50` |
| `block_below` | block=30，blockReduction=20，blockSample=29 | `true / false / true / 40` |
| `block_equal` | block=30，blockReduction=20，blockSample=30 | `true / false / false / 50` |
| `crit_and_block` | crit=20，criticalSample=19，block=30，blockReduction=20，blockSample=29 | `true / true / true / 60` |

倍率／派生率中间断言容差为 `1e-4`；布尔与最终整数伤害必须精确相等。两端测试从同一个 JSON 文件读取，不能复制一份期望到代码。BattleSim 若增加确定性入口，该入口必须被现有 duel／group 的真实 `Dmg`／`ApplyDefenses` 调用链消费，不能成为测试专用平行公式。

### 4.4 批准保留的差异

以下差异有明确的运行时所有者，本批不换算、不修复：

- CT：Unity 每 tick 以 speed 充能至 `100`，普通行动归零，冷却提高下次门槛，等待保留一半；BattleSim duel 使用连续行动时点，group 以反应值充能并减 `100`。只共同断言“更高正反应值不晚于更低值获得首次行动”和各端自己的稳定同值顺序；`ticksElapsed`、回合数和累计 CT 不跨端比较。
- 非零冷却：Unity 使用刻；BattleSim 现有术法／神通使用回合。继续按攻击档案合同报告 `battlesim_cooldown_unit_unresolved`，不得换算。
- Unity 的朝向、守势、移动／CT 命令结算与 BattleSim 的 duel／group AI 循环是不同运行时编排；只要不改变本节共用攻击结果，可保留各自行为。

### 4.5 尚不可比

下列语义没有足够原文裁决，故不得被 `combat-parity-v1` 报告为通过，也不得在三张下游卡内顺带修复：

- 非 `1:1` 境界倍率、非零抗性、穿透与两端不同的境界／抗性缩放；
- `.5` midpoint、分步取整、命中后最小 `1` 伤害与攻击／防御异常值；
- 神魂攻击、魂盾、元素克制、具体功法叠层、范围／混合／治疗／状态效果；
- 正式术法／神通 ability ID 到 profileId 映射及非零冷却；
- 完整 duel／2v2 数值平衡和胜率。

这些项目若要统一，必须以独立决定补足预期值、单位和生产事实；不得从本合同的中性夹具外推。

## 5. 三张实施卡的停止边界

- `U-COMBAT-PROJECTION-01` 只做十四字段、旧档零值、相同派生调用和现有功法元素表接线；不得改 `CharacterAttributes.Derive`、schema 版本、内容目录或能力权限。
- `U-COMBAT-LOADOUT-01` 只做战斗快照授权、普攻二选一失败关闭、统一 ValidateAttack 及内存 profileId 夹具；不得建立能力映射、修改生产能力内容、保存格式或学习／装备 UI。
- `N-COMBAT-PARITY-01` 只消费上述 JSON，对齐本节八个中性用例和首次行动顺序；只允许修改 `Combat.cs` 的真实基础伤害／防御概率链及 `CombatActionResolver` 对应函数。若通过测试必须改境界、抗性、全局取整、CT、冷却、范围、GameData／Character 成长或生产数值，立即停止并返回本合同。

完成上述三卡也只表示：角色显式输入已接入、正式普攻按角色授权、内存术法／神通授权边界成立、八个中性物理用例双端一致。它不表示生产术法／神通、全部功法、所有战斗公式或数值平衡已经跨端接入。
