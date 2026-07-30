# Unity 2v2 会话与基础攻击绑定所有者设计

状态：`U-COMBAT-01B0` 的已锁定实施设计。本文只为 `U-COMBAT-01B` 明确 Unity 的会话、部署、目标资格和基础攻击绑定边界；不修改运行时、CSV、asset、场景、预制体、攻击参数、冷却单位或 BattleSim。

## 一、事实与结论

当前 Unity 的正式遭遇生产链是 `ExplorationController` 创建角色、分配 CTB 单位 ID 与 `HexGrid` 占格，随后用同一格盘生成 `SpatialQuerySnapshot`，再由 `ExplorationController.StartBattle` 调用 `TacticalCombatController.BeginCombat`。`TacticalCombatController` 是现有的单场战斗调度者；`TacticalCombatSession` 当前只保存 `Player`、`Enemy`，因此它而非空间快照是缺失 2v2 阵营、存活和行动所有权的唯一扩展点。

`SpatialQuerySnapshot.UnitAnchors` 只从 `TacticalGridModel` 的占格投影出“单位 ID → 格位”。它不拥有阵营、存活、候选或范围命中资格，不能被扩展为第二个会话。`CombatResolver.CanTarget` 的 `SpatialQueryKind.Attack` 加普通视线，是既有单目标查询；范围必须改由攻击档案声明的 `SpatialQueryKind.Area`、效果阻断与无普通视线前置的同一 `SpatialQueryBoard` 调用。

`CharacterData` 是当前唯一实际角色装配源，`Character.FromData` 是其唯一运行时复制点。仓库尚无独立主战装备对象；因此基础攻击的外键应附在这条既有角色装配链，而不能以功法名、显示名、`GameData.UnarmedBasicAttack`、构造器常量或调用方默认值伪造装备系统。

## 二、唯一所有者

| 事实 | 唯一运行时／装配所有者 | 输入与输出 | 明确不拥有 |
|---|---|---|---|
| 会话成员、阵营、存活、输入顺序、行动推进与胜负 | `TacticalCombatController.currentSession` 所持的扩展 `TacticalCombatSession` | 接收两侧有序成员；输出存活行动者、同阵营／敌对候选和全灭结果。 | `ExplorationController.currentCombatTarget`、`SpatialQuerySnapshot`、UI、`CombatResolver`。 |
| 部署、稳定 CTB ID、格位和快照一致性 | `ExplorationController` 的遭遇组装与现有 `HexGrid` / `TacticalGridModel → SpatialQueryBoardFactory` 链 | 在建立会话前以实际占格重新生成当前遭遇快照；提交显式的双方成员与同一快照。 | 第二份格位表、按名称找单位、按最近敌人补足队员、技术空战场默认值。 |
| 单目标／范围目标资格 | `TacticalCombatSession` 提供阵营与存活候选，`AttackProfileData` 提供攻击声明，`CombatResolver` 适配同一 `SpatialQueryBoard` | 单目标只接受会话内的显式目标单位 ID；范围按档案中心、形状、距离、声明阻断、阵营与状态过滤候选。 | `sourceAffiliation`、显示名、`SpellRange`、普通视线、`UnitAnchors` 字典和调用方默认目标。 |
| 基础攻击 `attackProfileId` 绑定 | `CharacterData` 的角色装配字段，复制到 `Character` 的运行时绑定；`AttackProfileData` 只提供被引用档案 | 装配点给出一个明确的基础攻击外键；行动前解析、校验并只读消费对应档案。 | `CombatResolver` 硬编码 `1..1`、功法名称、无装备默认档案、旧术法／神通 asset。 |

这四项之间只有输入关系：遭遇生产者组装会话，会话把当前合法成员交给行动，行动把档案声明和成员锚点交给共享空间根。没有新的全局会话、阵营表、占格表或空间查询服务。

## 三、会话模型与 1v1 迁移

### 3.1 会话输入

`TacticalCombatController` 应以一个显式 `TacticalCombatSetup` 取代现有二参数 `BeginCombat(Character player, Character enemy, ...)` 入口；这不是兼容重载。该输入只包含：

- 两个有序侧（`Player` 与 `Enemy`），每侧只能为 1 人或 2 人，且双方人数必须相同；因此本切片仅接受 1v1 或 2v2。
- 每名成员的既有 `Character`、已分配的 `CTBUnit.Id` 和由角色装配解析出的基础攻击档案；顺序是调用方提交的稳定输入顺序，不按显示名、HP 或格位重排。
- 由当前 `HexGrid` 和环境档案新建的一个 `SpatialQuerySnapshot`。控制器将其 `Board` 交给现有 `CombatResolver`，但会话保留快照以核验成员锚点。

现有正式单敌遭遇同样构造这个模型：`ExplorationController.StartBattle` 先组装 `[player]` 与 `[enemy.character]`，再传给新 `BeginCombat(setup, grid, snapshot)`。它不是“旧接口回退”，也不为 2v2 按最近敌人、默认队友或单目标属性补齐输入。

### 3.2 `TacticalCombatSession` 的职责

扩展后的会话保存有序成员记录（角色、阵营、稳定输入顺序、已解析基础攻击档案）及同一快照，并提供下列唯一事实：

1. `CreateActiveUnitList` 仅按稳定输入顺序返回存活成员的 CTB 单位；每次推进前同步 `Character.IsAlive` 到既有 `CTBUnit.IsAlive`。
2. `GetEligibleSingleTargets(actor, requestedTargetUnitId)` 只从会话另一侧的存活成员中匹配显式 ID；缺失、己方、死亡或不在会话内均失败，不选择“当前敌人”或首个候选。
3. `GetMembersForArea(actor, profile)` 提供全部会话成员给范围资格过滤；`self`、`ally`、`enemy` 与 `alive`、`corpse` 均以会话记录为准。
4. `EvaluateEnd` 在任一侧没有存活成员时给出胜负；结束结算只清除实际被击败成员的既有格位。掉落如何由多个敌人模板生成仍由 `ExplorationController` 的遭遇数据负责，不能用第一名敌人的 `CharacterData` 代替整个敌队。

`AdvanceUntilAction`、冷却推进与行动消耗仍留在 `TacticalCombatController`。玩家命令和 AI 都先向会话请求行动者／目标资格，再调用 `CombatResolver`；`CombatResolver` 不再推断阵营或会话状态。

## 四、部署与快照失败关闭

### 4.1 生产顺序

1. `ExplorationController` 继续为参与者调用既有 CTB 注册并赋予稳定 `CTBUnit.Id`，再写入同一个 `HexGrid.SetOccupied`；四名成员必须全部完成这一步后才能建立会话。
2. 进入遭遇前，`ExplorationController` 从当前 `HexGrid` 创建 `TacticalGridModel`，并用当前环境档案调用 `SpatialQueryBoardFactory.TryCreate`。新快照替换旧字段，而非并存；这避免探索移动后旧 `UnitAnchors` 与实际格位脱节。
3. 遭遇组装逐名比较成员的 CTB ID、`Character.Position`、`HexGrid.GetOccupant(position)` 与 `snapshot.UnitAnchors[id]`。通过后才调用会话入口。

这仍是当前的 `TacticalGridModel → SpatialQueryBoardFactory → SpatialQuerySnapshot` 空间根。会话只读取其结果；不会保存另一份坐标、占格或距离缓存。

### 4.2 固定拒绝结果

会话组装必须在改变 CT、冷却、朝向或行动队列前失败，并保留现有战斗状态。最小稳定原因如下：

| 条件 | 结果 |
|---|---|
| 任一侧不是 1 人或 2 人，或双方人数不同 | `combat_session_side_cardinality_invalid` |
| 空成员、重复角色引用或重复 CTB 单位 ID | `combat_session_participant_invalid` |
| 成员没有已注册 CTB ID，或 ID 不在 `UnitAnchors` | `combat_session_unit_anchor_missing` |
| 成员当前位置、格盘占用与锚点三者不一致 | `combat_session_unit_anchor_mismatch` |
| 当前环境／格盘不能生成快照 | 沿用 `SpatialQueryBoardFactory` 的既有失败原因 |
| 基础攻击绑定未能解析或校验 | 见第六节；同样拒绝建立可行动会话 |

## 五、目标资格与共享空间边界

### 5.1 单目标

单目标行动接口迁为“行动者单位 ID + 显式目标单位 ID + 已解析档案”。`ExplorationController` 的 1v1 命令把其遭遇中已知的敌方单位 ID 显式传入；它不把 `currentCombatTarget` 当作会话事实。`SimpleAI` 同样从会话获得候选后提交一个 ID。现有攻击距离仍由 `CombatResolver.CanTarget` 经 `SpatialQueryBoard.QueryRangeEntry(... Attack, requireLineOfSight: true)` 判断。

### 5.2 范围

范围行动先以档案 `areaCenterKind` 接收施法者格或调用方显式选择的目标格，再由同一 `SpatialQueryBoard` 以 `SpatialQueryKind.Area` 检查中心距离和已声明效果阻断；不得调用普通视线。形状覆盖只解释档案字段，空间可达性、边阻断和距离仍全部由该 Board 返回。

对每名会话成员的范围资格按下列顺序关闭，第一项失败即为可观察拒绝原因：目标格无效／越界 → 中心不在施法距离 → 已声明效果阻断 → 状态不符 → 阵营不符 → 范围内无合法目标。`sourceAffiliation` 仅为内容来源元数据，显示键、`SpellRange`、`QueryLineOfSight`、字典枚举顺序或调用者没有资格替代该判断。范围命中集合不等于范围伤害结算；本设计不增加治疗、尸体系统、协同走位或团队 AI。

## 六、基础攻击绑定

### 6.1 角色装配外键

`CharacterData` 增加且仅增加两个可空的外键槽：`mainEquipmentBasicAttackProfileId` 与 `unarmedBasicAttackProfileId`。它们不是攻击参数、武器定义或 Unity 专属攻击 schema；每个非空值都只是指向导入器生成的 `AttackProfileData.attackProfileId`。两槽必须恰有一项非空：

- 主战装备槽只能引用 `profileKind=basic`、`basicBindingKind=main_equipment` 的档案；它表示该角色现有装配入口已明确选择的主战绑定，不创建武器内容、武器 asset 或默认主战装备。
- 无装备槽只能引用 `profileKind=basic`、`basicBindingKind=unarmed_fallback` 的档案；它必须由角色数据显式填写，空字符串不是无装备决定。

`Character.FromData` 复制已选择的 ID 与绑定种类到运行时 `Character`。它不复制倍率、距离、资源、冷却或范围字段。`DataConfigImporter.ImportAttackProfiles` 按统一 CSV 整表生成 asset 后，验证每个已填写的角色装配外键存在且种类匹配；运行时的遭遇组装再解析同 ID asset 并复验。这样 `AttackProfiles.csv → AttackProfileData → CharacterData 外键 → Character → 行动` 仍是单向读取链。

### 6.2 行动失败关闭

`CombatResolver.BasicAttack` 改为接收已解析的 `AttackProfileData`，并只读取该档案的效果、距离、资源与冷却投影；它不再选择物理／神魂数值，也不保留固定 `1..1`。行动前必须返回以下稳定失败，绝不落回旧普攻：

| 条件 | 结果 |
|---|---|
| 两个绑定槽均空或同时非空 | `basic_attack_binding_missing_or_ambiguous` |
| 外键没有对应 asset | `basic_attack_profile_not_found` |
| 引用档案不是 `basic`，或 `basicBindingKind` 与槽不符 | `basic_attack_profile_binding_kind_invalid` |
| 会话参与者没有通过上述校验的基础攻击档案 | `basic_attack_profile_unresolved` |

本卡没有、也不登记任何生产 `attackProfileId`、无装备档案 ID、攻击数值、武器内容、范围形状／尺寸或冷却换算。

## 七、U-COMBAT-01B 精确实施顺序

1. 在 `AttackProfileData.cs` 与 `DataConfigImporter.cs` 建立契约字段的 typed asset、严格整表导入及 ID 查找；任何缺字段、冲突或部分写入均关闭失败。
2. 在 `CharacterData.cs` 增加上述两个外键，在 `Character.cs::FromData` 建立唯一运行时绑定；不修改功法、术法或神通名称装载来充当基础攻击来源。
3. 在 `TacticalCombatController.cs` 将 `TacticalCombatSession` 扩展为双方有序成员会话，并把 `BeginCombat`、推进、玩家行动、AI 调用与 `ResolveBattleEnd` 迁到显式 setup／目标 ID／会话存活集合。旧二人 `BeginCombat` 不保留重载。
4. 在 `ExplorationController.cs` 用当前格盘重建快照，组装 1v1 的同一 setup，并为将来的显式 2v2 调用消费四名已部署成员；不修改场景、预制体、目标选择 UI 或正式冒险入口。
5. 在 `CombatResolver.cs` 和 `EnemyAI.cs` 只消费会话给出的行动者、目标和基础攻击档案；单目标与范围分别调用共享 Board 的 `Attack`／`Area` 路径，不能双读旧 `SpellData`／`DivineSkillData`。
6. 在 `SectSelectionManager.cs` 将玩家术法／神通装载替换为稳定档案 ID 解析；与 `ExplorationController` 的敌方装配同样拒绝临时术法、显示名映射和构造值回填。

直接测试路径为 `AttackProfileDataTests.cs`（导入、绑定缺失／冲突与逐字段投影）、`TacticalGridModelTests.cs`（1v1 与 2v2 setup、稳定推进、全灭、显式单目标、基础攻击失败关闭）和 `SpatialQueryBoardTests.cs`（锚点一致性、共享 Board 的效果路径与范围拒绝优先级）。验证先运行 `dotnet build src/TianZhang.EditModeTests.csproj`，可用时再运行直接 Unity EditMode 测试。

## 八、明确未涉及范围

- 不新建战斗会话、空间查询、格位／占格、阵营或存活的平行所有者。
- 不实现目标选择 UI、协同走位、让路、预留格、完整团队 AI、治疗、尸体系统、范围伤害或新冒险内容。
- 不把普通视线作为范围前置，不用 `sourceAffiliation`、名称、槽位序号或默认敌人代替攻击档案／会话输入。
- 不确定 BattleSim 回合到 Unity 刻的换算，不创建生产攻击档案、武器或无装备档案。

## 九、解除前置的理由

现有调用链只有 `ExplorationController → TacticalCombatController → CombatResolver` 这一条正式遭遇链；没有第二个会话、部署或装配所有者。上述设计把 1v1 归入同一会话模型，限定了 2v2 的显式输入、稳定顺序、锚点核验、目标资格和基础攻击失败结果。`U-COMBAT-01B` 因而无需猜测默认值、生产参数或第二套空间查询即可开始实现；若实现发现不存在的装备系统、生产档案、冷却换算或新的正式遭遇入口仍是必需输入，必须按其原停止条件转为 `pending_decision`。
