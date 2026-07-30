# 数据管线说明 (v2 — 文本 ID 化)

## 设计理念

所有游戏数据以 CSV 表格为唯一数据源。**显示文本统一通过 Language.csv 管理**，其他 CSV 表中仅引用文本 ID。Excel 编辑 → 导出 CSV → Unity 菜单 `天章/导入全部配置` → 生成 .asset

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
| `Enemies.csv` | 敌人模板（ID化） | `Data/Characters/Char_Enemy_*.asset` |
| `Settlements.csv` | 正式据点投影 | `Data/Settlements/Settlement_*.asset` |
| `Items.csv` | 正式物品投影 | `Data/Items/Item_*.asset` |
| `Bounties.csv` | 正式悬赏投影 | `Data/Bounties/Bounty_*.asset` |
| `EnvironmentProfiles.csv` | 环境档案纯数据契约 | `Data/EnvironmentProfiles/EnvironmentProfile_*.asset` |
| `FoundationPurpleMansionStates.csv` | 道基、紫府与修炼根状态（当前仅 schema） | `Data/FoundationPurpleMansionStates/FoundationPurpleMansionState_*.asset` |
| `JindanStaticStates.csv` | 金丹静态根（当前仅 schema） | `Data/JindanStaticStates/JindanStaticState_*.asset` |

## CSV 格式规则

- 首行为字段名，后续每行为一条数据
- `#` 开头为注释，导入时跳过
- 字段分隔：`,` | 数组分隔：`|` | 键值分隔：`:`
- **文本字段填 ID**（不是中文），ID 在 Language.csv 中定义

## 关中首批正式内容目录

`Settlements.csv`、`Items.csv`、`Bounties.csv` 与 `Enemies.csv` 中唯一带有
`contentScope` 的石甲兽行共同构成首批正式内容投影。导入器会先读取并验证四张表、
Language 引用和跨表稳定 ID，再原位更新 asset，并生成唯一的
`Data/ContentCatalog/ContentCatalog.asset`。

- 首批只允许 `guanzhong_city`、`enemy_shijiahou`、`item_lingshi_low`、
  `item_shijia_piece` 与 `bounty_guanzhong_shijiahou`。其他 `Enemies.csv` 行保持
  既有战斗模板输入，不能因导入而升格进正式内容目录。
- `Enemies.csv.dropTable` 是旧候选文本；正式石甲兽掉落只由 `dropEntries` 的
  `itemId@dropChancePercent@quantity` 表达。
- `features` 使用 `featureId~displayNameKey~availability~disabledReasonKey`，多个条目用
  `|` 分隔；`adventureEntranceIds` 用 `|` 分隔。
- `Bounties.csv.rewardEntries` 使用 `itemId@quantity`，多个条目用 `|` 分隔。
- 任一表头、行数、Language、稳定 ID、参数、路径或跨表引用不合法时，导入在写入任何
  正式内容 asset 前失败；目录只提供查找，不接入场景、战斗、掉落、背包、悬赏或存档。

## EnvironmentProfiles.csv 契约

本表定义环境档案结构；当前唯一生产行为 `env_guanzhong_wild`。表头固定为：

`profileId,directedEdges,surfacePrototypeRefs,phenomenonChannels,phenomenonPairs,elementRelationRefs`

- `directedEdges`：完整格式为 `unitsPerRange=<正整数>;maxQueryRange=<正整数>;edges=<边列表>`。边列表以 `|` 分隔，每项为 `fromQ:fromR>toQ:toR@metricDistanceUnits@allowsMovement@allowsEffects`；两个许可字段只接受 `0`/`1`。缺少查询上限、非拓扑邻格、非正边长、非法许可值或重复有向边均拒绝导入。
- `surfacePrototypeRefs`：以 `|` 分隔的地表原型 ID 引用。
- `phenomenonChannels`：以 `;` 分隔的六个通道声明，格式为 `channel=typeA+typeB`。通道必须恰为 `airflow`、`visibility`、`temperature`、`precipitation`、`suspendedHazard`、`cloudDischarge` 各一次。
- `phenomenonPairs`：以 `|` 分隔的同通道无序配对，格式为 `channel:typeA+typeB>resultType`。三个类型引用必须已在对应通道声明；翻转的同一对视为冲突并拒绝导入。
- `elementRelationRefs`：以 `|` 分隔，且必须恰含 `element_wood`、`element_fire`、`element_earth`、`element_metal`、`element_water` 各一次。

导入器会先验证整张表；任何缺字段、未知引用、非法通道、非相邻边、重复或顺序冲突配对都会在创建或更新 `.asset` 前失败。

## FoundationPurpleMansionStates.csv 契约

本表只消费 `docs/superpowers/specs/2026-07-25-foundation-purple-mansion-data-contract.md` 的
`foundationPurpleMansionState` 根对象；当前没有生产角色行，内容迁移由
`D-FPD-MIGRATE-01` 单独授权。表头固定，未知列（包括旧
`developedMansions`、`mansionBindings`、`realmStage` 与 `legacyDanJiType`）会失败关闭。

- `schemaId` 固定为 `foundationPurpleMansionState`，`schemaVersion` 固定为 `1`。
- 复合列不用 CSV 内嵌逗号：集合使用 `|`，记录字段使用 `~`，记录内列表使用 `+`；
  空集合写 `none`。`mansionStates` 始终显式给出五府，不得省略未建府。
- `fixtureId`、`expect` 和 `fixtureOnlyNumericProfile` 仅供 EditMode fixture 使用；生产
  导入拒绝它们，故测试字面量不能生成生产 asset。
- `phaseBoundarySetId`、功法／术法／神通／行动和数值档案仍是外部权威 ID；本表只保存
  稳定引用，不从旧角色字段或运行时槽位推断任何状态。

## CharterRuleDefinitions.csv 契约

本表只消费 `docs/superpowers/specs/2026-07-22-tianzhang-charter-data-contract.md` 的
十八字段静态条目定义；当前只固定表头，`D-TZ-CHARTER-SAMPLE-01` 才能迁入首个生产条目。
它与 `CharterRuntimeStateData` 严格分离，不能把地区长期状态写入
`EnvironmentProfileData`。

- 表头固定为 `ruleEntryId,displayName,ruleFamily,relationElement,compatiblePhenomena,positiveCommit,negativeCommit,requiredAuthority,requiredNodeTypes,scopeType,scopeTierCap,anchorNodeIds,propagationBoundaryProfileId,currentCoverageSet,affectedWorldVariables,conflictProfileId,failurePolicy,worldEventOutputs`；未知列、缺列和空的生产字段失败关闭。
- 集合以 `|` 分隔；`worldEventOutputs` 的单项格式为
  `eventId~environmentProfileId`。`displayName` 必须是 Language 键而非显示文本。
- 非空生产行必须通过显式外部目录解析遗物权限、组织授权、节点、边界、现实供给、提交、变量、冲突／跨阶资格、事件和环境档案。当前没有生产目录，导入器拒绝所有非空生产行，不能用 fixture 或路径补齐。
- `scopeType` 只接受 `SINGLE_NODE`、`CONNECTED_NODES`、`REGIONAL_HUB`；
  `scopeTierCap` 只接受 `NODE`、`AREA`、`REGION`；`failurePolicy` 只接受
  `REJECT`、`SUSPEND`、`SAFE_DOWNGRADE`，均不得依靠默认值。

## JindanStaticStates.csv 契约

本表只消费 `docs/superpowers/specs/2026-07-25-jindan-static-data-contract.md` 的
`jindanStaticState` 根对象。当前没有生产角色行；`D-JD-SAMPLE-01` 才能授权实际样例。
表头固定，未知列以及旧 `developedMansions`、`mansionBindings`、`realmStage`、
`legacyDanJiType` 和显示文本字段均会失败关闭。

- `schemaId` 固定为 `jindanStaticState`，`schemaVersion` 固定为 `1`。集合用 `|`，记录字段用
  `~`，辅助承载列表用 `+`；无可选引用写 `none`。
- `mansionInputs` 必须明确包含命、魂、识、悟、运五行；已建府完整镜像同角色、`FORMED` 的
  `foundationPurpleMansionState` 冻结输入。稳定实位为一至三项 `SOURCE`／`TRANSFORMATION`／`DOMAIN`，
  每项一个不重复主承载。
- 道路、效果、位格、道证、兼容档案、丹相、账本和冲突费用只保存稳定引用；导入器在 asset
  写入前要求它们全部由同一批次或声明的外部权威目录解析。当前没有正式权威目录，因此非空生产
  行会失败关闭，不能把 fixture ID 当成生产内容。
- `fixtureId`、`expect` 和 `fixtureOnlyNumericProfile` 仅供 EditMode 直接 fixture；生产导入拒绝它们，
  因而 fixture 字面量不能生成生产 asset。

## 添加多语言

1. 在 Language.csv 中追加列：`id, zh_CN, en_US, ja_JP`
2. 修改 DataConfigImporter 的 `T()` 方法，按当前语言选择对应列
3. 重新导入即可

## 工作流

1. 在对应 CSV 中编辑数据（文本字段用 ID）
2. 如需新增文本，先在 Language.csv 中添加 ID→中文映射
3. Unity 菜单 → `天章/导入全部配置`
4. 生成的 .asset 中显示为已解析的中文
