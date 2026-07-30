# 关中石甲兽 BountyData 数据契约

> 状态：设计契约；不代表 BountyData CSV、Unity asset、导入器、只读目录、悬赏运行时、背包授予或存档迁移已经实施。
>
> 范围：只为已审核的 `bounty_guanzhong_shijiahou` 固定字段、跨表引用、状态与失败关闭语义。奖励物品、授予数量、掉落概率和物品 `maxStack` 仍须由 `N-GZ-REWARD-01` 的负责人决定；本文件不填生产值或 fixture 值。

---

## 一、目的与单向边界

后续正式悬赏数据严格沿用下列单向链路：

```text
docs 中已审核的悬赏设计与内容事实
  -> DataConfig CSV 的结构化生产投影
  -> DataConfigImporter 的整表与跨表校验
  -> 保持 GUID 的 BountyData Unity ScriptableObject
  -> ContentCatalogData 的只读查找
  -> Settlement / Adventure / Bounty 消费者
  -> GameSession 只保存悬赏实例进度
```

- `docs/` 是悬赏身份、目标、地点、一次性规则与奖励类别的唯一设计事实源；CSV 只承载已批准事实的机器可读投影。
- 导入器必须先完成所有悬赏行及其 Language、SettlementData、EnemyData、AdventureData 和 ItemData 引用的整表校验，再原位创建或更新 asset。同 ID asset 必须保持 GUID；CSV、asset、目录和消费者都不得成为第二事实源。
- `ContentCatalogData` 只提供 `TryGetBounty(bountyId)` 和按 `issuerSettlementId` 查询悬赏的只读入口；它不保存玩家状态、不接取任务、不结算战斗，也不授予奖励。
- `GameSession` 只保存实例状态。当前 `QuestStateStore` 是通用七步布尔快照，`InventoryStateStore` 是通用库存快照，当前存档 schema 为 2；它们都不是现有悬赏系统或奖励原子提交。后续 `BountyStateStore`、schema 3 和会话原子替换由 `U-BOUNTY-01A`、`U-BOUNTY-01B` 实施。

本契约不修改当前 `guanzhong_city -> guanzhong_wild -> guanzhong_city` 的冒险与战斗通路。它不表示悬赏已可接取、计数、领奖或保存。

## 二、`BountyData` 最小字段

所有稳定 ID 都是非空、小写 ASCII 键；显示文本、物品名称、对象名、数组位置、asset 路径和本地化后的文字均不得替代稳定 ID。

| 字段 | 约束与职责 |
|------|------------|
| `bountyId` | 全目录唯一稳定 ID。首批已审核身份为 `bounty_guanzhong_shijiahou`。 |
| `titleKey` | 非空、可解析的 Language 键；只保存键，不内嵌标题。当前没有批准的生产值，不能以叙事标题、显示名或默认键代替。 |
| `descriptionKey` | 非空、可解析的 Language 键；只保存键，不内嵌描述。当前没有批准的生产值，不能以摘要、显示名或默认键代替。 |
| `contentScope` | 明确生产范围；首批正式悬赏只能使用 `content_scope_production`。草稿、原型或未知范围不得被正式目录或消费者悄然升格。 |
| `issuerSettlementId` | 已通过 SettlementData 校验的生产范围城市稳定 ID；首批为 `guanzhong_city`。 |
| `objectiveType` | 目标类型稳定 ID；首轮只允许 `defeat_enemy`。 |
| `targetEnemyId` | 已通过 EnemyData 校验的生产范围敌人稳定 ID；首批为 `enemy_shijiahou`。 |
| `requiredCount` | 正整数目标数量；首批单敌除害令为 `1`，不得为零、负数或以默认值补齐。 |
| `allowedAdventureId` | 已注册、生产范围一致的冒险稳定 ID；首批为 `guanzhong_wild`。 |
| `rewardEntries[]` | 非空奖励条目集合；每项只包含正式 `itemId` 与正整数 `quantity`。完整条目等待 `N-GZ-REWARD-01`，未决期间不得创建生产行或填入 fixture 数值。 |
| `repeatPolicy` | 首批固定为 `one_time`；它不引入每日刷新、随机任务池或其他重复规则。 |

`BountyData` 不包含 NPC、声望、价格、库存、掉落概率、战斗数值、场景路径、按钮名、通用条件表达式或可执行处理器。发布方“关陇坊市联盟”只是悬赏板的文本署名，不是接取或提交 NPC 引用。

## 三、首批已审核身份与参数闸门

| 项目 | 已审核、可表达的事实 | 当前不可填入的生产值 |
|------|------------------------|------------------------|
| 悬赏身份 | `bounty_guanzhong_shijiahou`；叙事任务为 `quest_guanzhong_bounty_shijiahou`。 | `titleKey` 与 `descriptionKey` 的具体生产键。 |
| 发布位置 | `issuerSettlementId=guanzhong_city`；城市唯一启用入口为 `bounty_board`。 | 额外城市、NPC、服务处理器或 UI 内容。 |
| 目标 | `objectiveType=defeat_enemy`、`targetEnemyId=enemy_shijiahou`、`requiredCount=1`。 | 其他目标类型、替代敌人或随机目标。 |
| 地点 | `allowedAdventureId=guanzhong_wild`；其为可重复的单敌遭遇，胜败均返回关中城。 | 场景路径、其他地点或地点 fallback。 |
| 重复规则 | `repeatPolicy=one_time`；领取后永久终态。 | 每日刷新、任务池、重置或第二套互斥规则。 |
| 奖励 | 奖励类别仅为低阶基础资源。 | 正式 `itemId`、数量、掉落概率、`maxStack`、价格和任何经济强度。 |

`C-GZ-BOUNTY-01` 已锁定奖励类别与候选边界，但没有锁定奖励条目。`N-GZ-REWARD-01` 是首批正式奖励 `itemId`、每项正整数数量、石甲兽掉落参数及物品 `maxStack` 的唯一参数闸门。当前 `Enemies.csv` 候选和现有库存状态都不能作为默认值、回退或正式奖励事实。

## 四、状态、计数与原子领奖契约

后续悬赏实例由独立 `BountyStateStore` 保存，至少包含 `bountyId`、`status` 与 `progress`。它不能复用或重新解释现有 `QuestStateStore`。

```text
无实例 = Available
Available -> Accepted
Accepted -> ObjectiveCompleted
ObjectiveCompleted -> Claimed
```

- `Accept` 只接受可解析的生产范围 BountyData、玩家位于 `issuerSettlementId` 的悬赏入口且状态为 `Available`。成功时创建进度为 0 的 `Accepted` 实例；`Accepted`、`ObjectiveCompleted` 或 `Claimed` 的重复接取均稳定失败且不改状态。
- `RecordDefeat` 只接受 Adventure 所有者在胜利结算时提交的结构化 `adventureId + enemyId`。只有状态为 `Accepted`、`objectiveType=defeat_enemy`、两个 ID 分别等于 `allowedAdventureId` 与 `targetEnemyId` 时才可将进度增加；达到 `requiredCount` 时同次转换为 `ObjectiveCompleted`。
- Adventure 从 Combat 消费同一场胜负结果至多一次，因此同一场战斗至多提交一次悬赏计数。败北、主动退出、错误地点、错误敌人、非胜利结果、未知引用、非法状态、负进度、超过目标或逆向转换均拒绝且保持实例不变。
- `Claim` 只接受 `ObjectiveCompleted`。它必须先验证全部奖励 `itemId`、正整数数量和对应 ItemData 的已批准 `maxStack`，构造完整的新库存快照；全部合法后才与 `Claimed` 状态由 `GameSession` 在同一次提交替换。任一校验失败时，库存与悬赏状态均保持不变，禁止部分授予、半领取或重复领取。

当前没有 `BountyRuntime`、`BountyStateStore`、领奖处理器或 schema 3。后续实现必须在保存与恢复时验证 schema、悬赏 ID、状态、进度、重复项和内容引用；未知版本、未知生产 ID、重复状态或非法进度失败关闭，不能静默删除玩家进度。

## 五、整表、跨表与运行时失败关闭

导入器必须整体拒绝并保持所有目标 asset 不变的情形包括：

- 缺文件、错误表头、短行、额外未知列、空/非法/重复 `bountyId`，或不可解析的标题、描述 Language 键；
- 未知、草稿、原型或范围不一致的城市、冒险、敌人、物品或内容范围引用；
- 不支持的 `objectiveType`、非正 `requiredCount`、缺失或非 `one_time` 的首批 `repeatPolicy`；
- 空奖励集合、未知奖励物品、缺失的待批准奖励参数、非正奖励数量、未定义/非法/超限的 `maxStack`；
- 同 ID 多 asset、asset 路径与 ID 冲突、手工修改 ID，或更新时未能原位保持 GUID。

运行时缺失或失效的单项悬赏、城市入口、目标、地点或奖励时，只禁用或拒绝该悬赏：不得伪造默认目标、默认奖励、成功日志或状态转换。冒险配置缺失时必须拒绝启动遭遇，同时保留既有返回来源出口；奖励校验失败时不得改变背包或悬赏状态。

## 六、fixture 规格

fixture 只验证字段、引用与失败关闭，不是生产 CSV、Language、asset、奖励、掉落或库存数据。

| 类型 | 规格与期望结果 |
|------|----------------|
| 已审核身份 fixture | 组合 `bounty_guanzhong_shijiahou`、`guanzhong_city`、`bounty_board`、`defeat_enemy`、`enemy_shijiahou`、`requiredCount=1`、`guanzhong_wild` 与 `one_time`；不含标题/描述生产键或奖励条目，因此只证明已审核身份，不能导入或运行。 |
| 正向生产形状 fixture | 仅在 `N-GZ-REWARD-01` 已批准奖励与物品 `maxStack`、且 Language 键已获批准后，加入可解析文本键、生产范围、非空正式奖励条目及所有跨表引用；导入后才允许建立正式 BountyData。具体参数由该决定提供，不在本契约写入。 |
| 非法字段 fixture | 空、非小写 ASCII 或重复 ID；未知文本键或范围；不支持目标类型；零/负目标数量；空或非 `one_time` 重复规则。应整体失败关闭。 |
| 非法引用与参数 fixture | 城市、敌人、冒险或物品未知/非生产范围；空奖励、未填参数、非正奖励数量、未定义/超限堆叠。应整体失败关闭，不生成默认奖励。 |
| 状态与计数 fixture | 验证四个合法转换、重复接取/领奖、逆向转换、错误地点/敌人、败北/退出、单场重复提交、负进度和超目标均拒绝且不改状态。 |
| 原子领奖与存档 fixture | 多奖励中任一物品、数量或堆叠校验失败时，库存与悬赏状态均不变；schema 3 往返保存合法状态，未知版本、未知 ID、重复项和非法状态/进度拒绝恢复。 |

## 七、后续顺序与非目标

1. `D-BOUNTY-01` 只完成本契约。
2. `N-GZ-REWARD-01` 在负责人提供完整参数与验证目标后，只补奖励条目、石甲兽掉落参数及物品 `maxStack`，不改变本契约的身份、地点、目标或状态语义。
3. `U-CONTENT-CATALOG-01` 在四份数据契约与参数闸门齐备后，实现整表验证、GUID 保持、跨表校验和 `ContentCatalogData` 的只读查询。
4. `U-BOUNTY-01A` 实现悬赏状态机与一次性计数；`U-BOUNTY-01B` 实现 schema 3 与迁移；`U-BOUNTY-01C` 接入悬赏板界面。

本契约不创建 CSV、Language、asset、导入器、目录、悬赏运行时、背包授予、存档迁移、UI、场景或测试；不新增其他悬赏、目标类型、随机池、每日刷新、通用条件表达式、NPC、价格、经济机制或兼容 fallback。

## 八、验证口径

- 已审核身份逐项对应关中城、悬赏板、关中野外、石甲兽与一次性除害令；奖励只保留类别及 `N-GZ-REWARD-01` 参数闸门。
- 生产链保持 docs 到消费者的单向边界；导入原子性、GUID 保持、目录只读与实例状态所有者明确。
- 正反 fixture 覆盖字段、所有跨表引用、缺失参数、四状态、单场计数、原子领奖与 schema 3 的失败关闭；不填入奖励、掉落或堆叠生产值。
- 在后续参数、目录与运行时任务完成直接验证前，本文件不声称悬赏已经可接取、计数、领奖或保存。
