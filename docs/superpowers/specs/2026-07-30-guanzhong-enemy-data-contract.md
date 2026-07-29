# 关陇玄域怪物、AI 与掉落数据契约

> 日期：2026-07-30
> 状态：数据契约已定义；尚未修改 CSV、Unity asset、导入器或运行时消费者

## 一、目的与适用范围

本契约为 `EnemyData`、正式 AI 引用、战斗模板指针和结构化掉落定义唯一的数据边界。首批只覆盖关陇玄域的 `enemy_shijiahou`，以及正式冒险 `guanzhong_wild` 的单敌遭遇。

它不改变战斗属性、掉落概率、掉落数量或现有运行时行为。`N-GZ-REWARD-01` 是首批掉落概率、数量和堆叠参数的唯一决定者；`U-CONTENT-CATALOG-01` 与 `U-ENEMY-01` 才负责实现导入、目录和正式消费。

正式生产链固定为：

```text
已批准 docs 内容事实
  -> Enemies.csv 的单一正式行
  -> 全表与跨表校验
  -> 同一次导入生成 CharacterData 与 EnemyData 两类投影
  -> ContentCatalogData 只读查找
  -> Adventure / Combat 消费
```

`Enemies.csv` 是机器可读生产投影，`docs/` 是设计事实源。显示名、asset 路径、数组位置、GameObject 名称和本地化后的文字都不能替代稳定 ID。导入只能从同一 CSV 行单向生成两类投影；不得由 asset 反写 CSV，不得手工双写两类 asset，也不得用其中一类投影补全另一类。

## 二、`EnemyData` 数据模型

每个正式怪物必须有且仅有一个 `EnemyData`，最低字段如下。

| 字段 | 含义与约束 | 首批来源／归属 |
|---|---|---|
| `enemyId` | 小写 ASCII 稳定 ID；全目录唯一，不得由显示名替代。 | 现有 `Enemies.csv.name`；石甲兽为 `enemy_shijiahou`。 |
| `displayNameKey` | 必须解析到 Language；不保存本地化后的显示文字。 | 首批复用 `enemyId` 作为 Language 键。 |
| `descriptionKey` | 必须解析到 Language。 | `Enemies.csv.description`，石甲兽为 `desc_enemy_shijiahou`。 |
| `contentScope` | 已批准的生产范围；正式 Adventure 只能消费与自身范围一致的怪物。 | 首批为 `guanzhong`；正式 CSV 投影须显式承载该字段。 |
| `enemyTypeId` | 怪物类别稳定 ID。 | `Enemies.csv.type`，石甲兽为 `type_yaoshou`。 |
| `aiProfileId` | 已注册 AI 档案的稳定 ID。 | `Enemies.csv.aiType`，石甲兽为 `ai_melee`。 |
| `realmId` | 怪物境界身份；不从战斗强度倍率推导。 | `Enemies.csv.realm`，石甲兽为 `realm_lianqi`。 |
| `combatTemplate` | 指向同一行生成的现有 `CharacterData` asset 的序列化引用。 | 石甲兽为 `Char_Enemy_enemy_shijiahou.asset`。 |
| `dropEntries[]` | 非空的结构化掉落条目集合。 | 首批候选物品已锁定；参数由 `N-GZ-REWARD-01` 决定。 |

`CharacterData` 只承载通用战斗属性、已装备能力与战斗计算所需的既有字段。它不承载怪物稳定身份、内容范围、怪物类别、AI 档案、境界身份或正式掉落语义；`EnemyData` 也不复制通用战斗属性。`realmMultiplier` 是既有 `CharacterData` 的战斗强度兼容字段，不是 `realmId` 的来源，也不得生成正式掉落。

同一行的两类投影必须在一次全表验证成功后原位更新：已有 `CharacterData` 保持 GUID，`EnemyData.combatTemplate` 指向该行生成的同一模板。导入前任何行不合法时，不更新任一目标 asset；不得留下半导入状态。

## 三、AI 档案与正式遭遇边界

`aiProfileId` 只是数据引用，不携带第二套空间、目标选择或战斗规则。首批只定义已批准的 `ai_melee`：依确定性顺序尝试合法的已装备能力；否则接近目标；相邻时进行基础攻击；均不可行动时防御。石甲兽当前没有已装备术法或神通，因此其首批语义是接近、近战、无法行动时防御。

未知、空白或未注册的 AI 档案必须使导入失败；运行时若无法解析已配置的档案，必须阻止正式遭遇启动。不得改用 `SimpleAI`、默认 AI、第二套目标选择语义或 fallback 伪造成功。

正式 Adventure 只能以 `EnemyData` 启动遭遇，并从 `combatTemplate` 创建战斗实体；战斗单元同时保留 `enemyId` 和对应 `EnemyData`。`AdventureSceneController` 继续是正式遭遇结算与返回的唯一所有者。旧 `ExplorationController.CreateFallbackEnemy` 及其原型路径可以保留为迁移边界，但正式 Adventure、正式目录和正式掉落不得调用它。

## 四、结构化掉落契约

每个 `dropEntries[]` 条目必须包含：

| 字段 | 约束 |
|---|---|
| `itemId` | 已存在的正式 `ItemData` 稳定 ID。 |
| `dropChancePercent` | 由 `N-GZ-REWARD-01` 批准的数值，范围为 `0..100`。 |
| `quantity` | 由 `N-GZ-REWARD-01` 批准的正整数，并受物品明确堆叠规则约束。 |

结算对每项使用可注入随机源取得 `[0, 100)`；结果严格小于 `dropChancePercent` 时才授予该项。掉落结算输出稳定 `itemId` 与数量，而不是物品名称字符串；随机源不得依赖全局随机状态。

现有 `Enemies.csv.dropTable` 的两项石甲兽候选只锁定 `item_shijia_piece` 与 `item_lingshi_low` 的身份。该列当前没有正式 `EnemyData` 消费者，其中的旧文本格式和数值不得解释为本契约的概率或数量，也不得据此填入参数。参数未获批准前，石甲兽不是可生成、可消费的正式 `EnemyData` fixture。

`TacticalCombatController.CreateDropItems` 当前按 `CharacterData.realmMultiplier` 产生文字掉落，`ExplorationController` 仅将该字符串列表写入日志；它们是待迁移的旧行为，不是本契约的正式掉落权威。正式掉落缺失、失败或非法时不得回退到这些行为；掉落授予失败时须保持背包和相关进度不变。

## 五、首批石甲兽字段归属

| 项目 | 已锁定事实 | 本卡状态 |
|---|---|---|
| 身份与文本 | `enemy_shijiahou`、`enemy_shijiahou`、`desc_enemy_shijiahou` | 可作为正式字段来源。 |
| 范围与类别 | `guanzhong`、`type_yaoshou` | 可作为正式字段来源。 |
| AI 与境界 | `ai_melee`、`realm_lianqi` | 可作为正式字段来源。 |
| 战斗模板 | 既有 `Char_Enemy_enemy_shijiahou.asset` | 只作为 `combatTemplate` 指针；不复制或改写数值。 |
| 冒险用途 | `guanzhong_wild` 的单敌、可重复遭遇 | 正式消费时须与 `contentScope` 和 Adventure ID 一起校验。 |
| 掉落候选 | `item_shijia_piece`、`item_lingshi_low` | 仅候选 ID；概率和数量留白，等待 `N-GZ-REWARD-01`。 |

首批正向内容 fixture 是上述身份、范围、类别、AI、境界、模板指针和两项候选物品的一致组合。它只有在两项物品可解析、每个条目的概率与正整数数量均已获批准、并且两类投影一致后，才成为可导入、可启动的正式 fixture。

## 六、失败关闭与 fixture 规格

导入必须拒绝并且不更新任一 asset 的情形包括：

- 空、非法或重复 `enemyId`，或无法解析的显示名／描述 Language 键；
- 缺失、未知或与正式 Adventure 不一致的 `contentScope`、`enemyTypeId`、`realmId`；
- 缺失、未知或未注册的 `aiProfileId`；
- 缺失 `combatTemplate`、同 ID 多模板、模板路径／ID／GUID 冲突，或同一 CSV 行的两类投影不一致；
- 空 `dropEntries[]`、未知 `itemId`、缺少尚未批准的掉落参数、概率不在 `0..100`、数量非正整数或违反明确堆叠规则；
- CSV 行格式、表头、列数或跨表引用不合法。

运行时必须阻止正式遭遇启动的情形包括：缺失 `EnemyData`、AI、战斗模板或已验证掉落。不得创建默认敌人、默认 AI、默认掉落或从 `realmMultiplier` 推导掉落。非法配置的失败必须可观察，并仍保留 Adventure 的既有返回出口。

后续导入与测试至少提供以下 fixture：

| 类型 | 规格 | 期望结果 |
|---|---|---|
| 正向 | 石甲兽全部已锁定字段一致，两个候选物品均为正式 ID，且参数由 `N-GZ-REWARD-01` 明确给出。 | 原子生成两类一致投影；正式 `guanzhong_wild` 可只按该 `EnemyData` 启动。 |
| 反向 | 缺失或未知 AI、模板、物品、Language、范围或境界引用。 | 导入或启动失败关闭，不更新 asset，不用 fallback。 |
| 反向 | 概率越界、数量非正整数、空掉落、未填参数、同 ID 双投影不一致。 | 导入失败关闭，不产生文字掉落或默认奖励。 |
| 反向 | 正式 Adventure 直接绑定 `CharacterData`、调用 fallback，或用 `realmMultiplier` 计算掉落。 | 运行时配置校验失败；不得当作正式遭遇。 |

## 七、后续实现边界

- `N-GZ-REWARD-01`：只补批准的掉落概率、数量与堆叠规则，不改变身份、AI、模板或冒险范围。
- `U-CONTENT-CATALOG-01`：实现整表预验证、两类投影的原子生成、GUID 保持、跨表校验与 `ContentCatalogData.TryGetEnemy(enemyId)`；不重新定义本契约。
- `U-ENEMY-01`：使正式 Adventure 按 `EnemyData` 消费、输出结构化击败结果并按本契约结算掉落；不改战斗数值或引入 fallback。

本卡不建立 Unity 专属平行 schema，不新增怪物、AI、团队行为、随机遭遇、表现资源、战斗数值或掉落参数。
