# 关中最小 ItemData 数据契约

> 状态：设计契约；不代表 ItemData CSV、Unity asset、导入器、只读目录、掉落、悬赏或背包授予已经实施。
>
> 范围：只固定 `item_lingshi_low` 与 `item_shijia_piece` 进入后续正式数据链所需的身份字段、引用边界与失败关闭语义。掉落概率、授予数量、`maxStack` 生产值、价格和经济结论均不在本契约内。

---

## 一、目的与单向边界

后续正式物品数据严格沿用下列单向链路：

```text
docs 中已审核的物品设计与内容事实
  -> DataConfig CSV 的结构化生产投影
  -> DataConfigImporter 的整表与跨表校验
  -> 保持 GUID 的 ItemData Unity ScriptableObject
  -> ContentCatalogData 的只读查找
  -> 掉落、悬赏和物品授予消费者
  -> GameSession 只保存实例进度
```

- `docs/` 是物品身份、类别和允许用途的唯一设计事实源；CSV 只承载已批准事实的机器可读投影。
- 导入器必须先验证整张物品表及其 Language、掉落和悬赏引用，再原位创建或更新 asset。同 ID asset 保持 GUID；CSV、asset、目录和消费者都不得成为第二物品事实源。
- `ContentCatalogData` 只提供按 `itemId` 的只读查找，不能保存库存、替换物品定义、生成奖励或授予物品。`GameSession` 与 `InventoryStateStore` 只保存实例状态，不能补出或覆写 ItemData。
- 当前 `Enemies.csv` 的候选掉落字符串、现有 `InventoryStateStore` 和两份物品档案均不证明任何物品已可掉落、进入背包或领取悬赏。它们不能被用作参数默认值或运行时回退。

## 二、ItemData 最小字段

所有稳定 ID 都是非空、小写 ASCII 键；显示名、描述文本、对象名、数组位置和 asset 路径均不得替代稳定 ID。

| 字段 | 约束与职责 |
|------|------------|
| `itemId` | 全表唯一稳定 ID。首批已审核身份只允许 `item_lingshi_low` 与 `item_shijia_piece`。 |
| `displayNameKey` | 非空 Language 键，导入时必须可解析。首批分别固定为同名键 `item_lingshi_low`、`item_shijia_piece`；键解析出的显示名分别为“劣质灵石”“石甲碎片”。 |
| `descriptionKey` | 非空 Language 键，导入时必须可解析；只保存键，不内嵌描述文本。当前没有获准的首批生产描述键，不能以显示键、名称或默认文案代替。 |
| `contentScope` | 明确内容范围。正式消费者只能查询生产范围 `content_scope_production`；范围标记不等于当前已有掉落、悬赏或库存实现。草稿、原型或未知范围不能被正式目录或消费者悄然升格。 |
| `itemCategory` | 只允许本契约定义的 `basic_resource`（基础资源）或 `monster_material`（妖兽材料）。类别表达身份，不声明价格、使用效果、装备、配方或经济用途。 |
| `maxStack` | 正整数堆叠上限，也是可堆叠语义的唯一生产表达；不得另设默认上限或把“可堆叠”转换为未经批准的数值。首批具体值只由 `N-GZ-REWARD-01` 决定。 |

`ItemData` 不包含价格、汇率、商店库存、装备槽、使用效果、炼器、合成、掉落概率、奖励数量或库存状态。不得为这些未授权概念增加字段、默认值、兼容回退或第二张物品表。

## 三、首批身份与参数闸门

| `itemId` | 已审核显示键与名称 | 已审核类别 | 可表达的当前边界 | 必须保持缺失的生产参数 |
|------|--------------------|------------|------------------|--------------------------|
| `item_lingshi_low` | `item_lingshi_low` / 劣质灵石 | `basic_resource` | 作为基础资源的可堆叠物品身份；未来可成为掉落或悬赏奖励库存单位。 | `descriptionKey` 生产值、`maxStack`、掉落概率、授予数量、价格及所有经济／使用参数。 |
| `item_shijia_piece` | `item_shijia_piece` / 石甲碎片 | `monster_material` | 作为妖兽材料的可堆叠物品身份；未来可成为掉落或悬赏奖励库存单位。 | `descriptionKey` 生产值、`maxStack`、掉落概率、授予数量、价格及所有炼器／使用参数。 |

- 现有 `enemy_shijiahou` 行对两个 ID 的候选引用，只能证明 ID 已被写入旧 CSV；在正式 ItemData、整表校验与运行时消费者尚未实施前，不能证明正式掉落或填充任何参数。
- `N-GZ-REWARD-01` 未决期间，`maxStack`、掉落概率和奖励数量必须在生产 ItemData、掉落表、悬赏表及其 fixture 中缺失；不得从候选 CSV、旧值、显示名或默认值推导。
- 任何正式 ItemData 生产行在 `descriptionKey` 或 `maxStack` 未获批准时均不可导入。此失败关闭不否定上表的已审核身份，只禁止把身份档案伪装为可运行内容。

## 四、引用、授予与失败关闭

### 4.1 整表与跨表校验

导入器必须在更新任一 ItemData asset 前完成物品表、Language 表和所有已接入引用表的整表校验。下列任一情况均整体失败，目标 asset 集合保持不变：

- 缺文件、错误表头、短行、额外未知列，或空、非法、重复的 `itemId`。
- 空或不可解析的 `displayNameKey`／`descriptionKey`，未知 `contentScope`，未知 `itemCategory`，或 `maxStack` 缺失、非整数、零或负数。
- 同 ID 多 asset、asset 路径与 ID 冲突、手工修改 ID，或更新时未能原位保持既有 GUID。
- 掉落或悬赏条目引用未知、非生产范围或未通过本表校验的 `itemId`；任何一个引用失败都不得静默删除该条目、改成默认物品或继续部分导入。

导入器和只读目录尚未实施时，以上是后续实现的强制契约，不代表当前 `DataConfigImporter.ImportEnemies` 已满足它们。

### 4.2 掉落、悬赏与原子授予

- 怪物掉落和悬赏奖励都只能保存正式 `itemId` 与由后续已批准数据提供的正整数数量；不能保存显示名、描述、asset 路径或候选 CSV 文本。
- 两类来源必须复用同一物品原子授予入口：先对全部物品 ID、数量和堆叠结果建立候选库存快照，全部合法后一次性替换状态。
- 物品不存在、数量非法、`maxStack` 未定义或超限、或任一条目校验失败时，掉落与悬赏奖励均失败关闭，背包与悬赏状态保持不变。不得部分授予、吞掉错误条目或回退为默认物品。
- 本契约不创建该入口，不把现有 `InventoryStateStore` 解释为该入口，也不声明悬赏已可领取。

## 五、fixture 规格

fixture 只验证字段、引用和失败关闭，不是生产 CSV、Language、asset、目录、掉落、奖励或库存数据。

| 类型 | 规格 |
|------|------|
| 正向身份 fixture | 分别包含 `item_lingshi_low`／`item_shijia_piece`、其已审核显示键和类别；验证 ID 唯一、小写 ASCII，显示键能解析到“劣质灵石”／“石甲碎片”，且类别分别为 `basic_resource`／`monster_material`。它明确不填写 `descriptionKey` 生产值、`maxStack`、掉落、奖励或数量，因此只证明身份，不能作为可导入生产 fixture。 |
| 正向生产形状 fixture | 在 `N-GZ-REWARD-01` 和描述键获得批准后，为每项加入可解析的描述键与该任务提供的批准 `maxStack`；不在本契约写入任何数值。fixture 还须证明对应生产范围，并只允许已通过 ItemData 校验的 ID 被掉落或悬赏引用。 |
| 非法字段 fixture | 空、非小写 ASCII 或重复 ID；未知显示／描述键、生产范围或类别；缺失、非整数、零或负数 `maxStack`；以显示名、路径或数组位置替代 `itemId`。 |
| 非法引用 fixture | 掉落或悬赏引用不存在、非生产范围或未通过物品表校验的 ID；把候选 CSV 文本、显示名或 asset 路径作为物品引用；数量缺失、零或负数。 |
| GUID 与原子失败 fixture | 同 ID 多 asset、路径／ID 冲突或更新后 GUID 变化必须拒绝；多物品授予中任一 ID、数量或堆叠结果非法时，验证库存和悬赏状态均不改变。 |

## 六、后续顺序与非目标

1. `D-ITEM-01` 只完成本契约。
2. `N-GZ-REWARD-01` 负责决定首批掉落概率、授予数量、`maxStack` 和悬赏奖励；在其未决前，本契约禁止填入参数。
3. `U-CONTENT-CATALOG-01` 在四类数据契约和首批参数齐备后，统一实现 CSV 导入、跨表校验、GUID 保持与只读目录。
4. `U-ITEM-GRANT-01` 在目录可用后，实现掉落与悬赏共用的原子物品授予入口。

本契约不修改 CSV、Language、asset、导入器、背包、掉落、悬赏、场景、Unity 测试或现有物品档案；不定义价格、商店、装备、使用效果、炼器、合成、经济循环或全局道具编辑器。

## 七、验证口径

- 两项身份逐字段映射到已审核物品档案、Language 显示键和 `enemy_shijiahou` 的候选 ID 边界；不把候选解释为已实现掉落。
- 生产链保持 docs 到消费者的单向关系，ItemData asset 保持 GUID、目录只读、导入整表原子性和跨表失败关闭语义明确。
- fixture 覆盖 ID、语言键、类别、生产范围、缺失参数、跨表引用、GUID 和原子授予失败；不填入掉落、奖励、数量或堆叠生产值。
- 直到后续 Unity 实施任务完成直接构建、导入和运行时验证前，本文件不声称任何物品已进入背包、掉落、领取或拥有可用经济／使用效果。
