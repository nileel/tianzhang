# 关中城 SettlementData 数据契约

> 状态：设计契约；不代表 CSV、Unity asset、导入器、目录或场景消费者已经实施。
>
> 范围：只为已审核的 `guanzhong_city`、`bounty_board` 与 `guanzhong_wild` 固定后续数据链所需的字段、引用和失败关闭语义。语言键在本契约中只定义字段约束，不填入生产值。

---

## 一、目的与单向边界

后续正式城市数据严格沿用下列单向链路：

```text
docs 中的已审核设计与内容事实
  -> DataConfig CSV 的结构化生产投影
  -> DataConfigImporter 的整表与跨表校验
  -> 保持 GUID 的 SettlementData Unity ScriptableObject
  -> ContentCatalogData 的只读查找
  -> Settlement、Adventure 等消费者
```

- `docs/` 是城市身份、功能可用性和冒险入口的唯一设计事实源；CSV 只承载已批准事实的机器可读投影。
- 导入器只能整表校验通过后原位创建或更新 asset；同 ID 资产必须保留 GUID。CSV、asset、目录和消费者均不得成为第二事实源。
- `ContentCatalogData` 只提供 `TryGetSettlement(settlementId)` 等只读查找；不保存玩家状态、不加载场景、不执行悬赏、奖励或战斗。
- 场景通过序列化引用同一目录 asset。`GameSession` 仅保存当前实例进度与场景上下文，不能保存或覆写城市定义。

本卡不改动当前原型：`SettlementSceneController` 仍从 `PrototypeSettlements` 查找，功能点击仍会进入 placeholder 日志；`WorldSceneController` 仍从世界节点传入 `settlementId`。这些均是后续实施任务的迁移证据，不能被描述为正式数据消费能力。

---

## 二、SettlementData 字段

`SettlementData` 的最低字段如下。所有稳定 ID 都使用非空、小写 ASCII 键；显示文本、GameObject 名、数组位置、asset 路径和本地化文本均不得替代稳定 ID。

| 字段 | 约束与职责 |
|------|------------|
| `settlementId` | 城市唯一稳定 ID；首批只允许引用已审核的 `guanzhong_city`。 |
| `displayNameKey` | 非空本地化键；导入时必须能在 Language 数据中解析，显示名本身不写入此字段。 |
| `contentScope` | 生产范围标识；首批城市为 `content_scope_production`，只有生产范围内容可由正式场景和启用功能引用。 |
| `settlementType` | 据点类型稳定键；首批城市为 `settlement_type_city`。 |
| `regionId` | 已注册地区稳定 ID；首批城市为 `guanzhong`。 |
| `ownerFactionId` | 已注册所属方稳定 ID；首批城市为 `faction_neutral`。 |
| `visualThemeId` | 已审核视觉主题稳定 ID；首批城市为 `visual_theme_loess_city`。 |
| `features[]` | 城市功能条目集合，按 `featureId` 唯一。 |
| `adventureEntranceIds[]` | 冒险入口稳定 ID 集合，城市内不得重复；首批只允许 `guanzhong_wild`。 |

`SettlementData` 不包含商店库存、价格、NPC、奖励、声望、世界状态、场景路径或可执行处理器。上述内容只能由各自后续数据与运行时所有者提供。

---

## 三、功能条目与分发约束

每个 `features[]` 条目包含：

| 字段 | 约束 |
|------|------|
| `featureId` | 非空、小写 ASCII 稳定 ID；同一城市内唯一。 |
| `displayNameKey` | 非空且可解析的本地化键。 |
| `availability` | 只能为 `enabled` 或 `disabled`。 |
| `disabledReasonKey` | `disabled` 时必须为非空且可解析的本地化键；`enabled` 时必须为空。 |

- 首批唯一启用的功能是 `bounty_board`。它只有在 `SettlementFeatureDispatcher` 能解析到已注册处理器时才可保持 `enabled`。
- 禁用功能不得调用处理器；视图只能显示其禁用原因，不能输出“已解锁”或其他 placeholder 完成日志。
- 任何未知 `featureId`、重复功能条目、不可解析显示键或禁用原因键、以及启用却没有处理器的功能都使导入失败关闭；不得静默删除、改为默认功能或自动禁用后继续导入。
- 处理器注册不改变数据可用性定义：后续内容任务先补齐已审核功能事实与语言键，再由对应实施任务注册处理器。

---

## 四、冒险入口与运行时边界

- `adventureEntranceIds[]` 只保存稳定 `adventureId`，不保存显示名、场景路径或按钮名称。
- 首批有效引用是 `guanzhong_wild`；其进入前提为当前据点为 `guanzhong_city`，胜利或败北均以 `Settlement(guanzhong_city)` 作为返回目标。
- 导入阶段必须拒绝空、未知或重复的冒险入口引用；不得以原型数组、目录顺序或默认入口补齐。
- 后续 `SettlementSceneController` 只读取 `GameSession.CurrentSettlementId`，经只读目录取得城市定义，再把功能点击交给 `SettlementFeatureDispatcher`，并保留现有 World / Adventure 进入和返回上下文。
- 运行时找不到城市、入口或其必要引用时必须失败关闭：不进入默认城市、不伪造冒险或完成结果，并始终保留返回世界入口。未知或禁用功能同样不得调用处理器。

---

## 五、首批 fixture 规格

fixture 只验证结构和引用，不是生产 CSV、Language、asset 或处理器配置，也不新增生产值。

| 类型 | 规格 |
|------|------|
| 合法城市 fixture | 一个生产范围的城市定义：`settlementId=guanzhong_city`，类型、地区、所属和视觉主题使用本契约第二节的已审核稳定键；包含可解析的显示键、唯一启用功能 `bounty_board`（禁用原因为空）和唯一冒险入口 `guanzhong_wild`。同时提供该功能已注册处理器与全部引用可解析的测试环境。 |
| 非法 ID fixture | 空、非小写 ASCII、重复 `settlementId`，或使用显示名、路径、数组位置代替稳定 ID。 |
| 非法字段 fixture | 不可解析的显示键、未知生产范围／类型／地区／所属／视觉主题，或把未审核内容作为首批生产引用。 |
| 非法功能 fixture | 重复或未知功能、`disabled` 缺少禁用原因、`enabled` 带禁用原因、启用功能没有已注册处理器。 |
| 非法入口 fixture | 空、未知或重复入口，或将显示名、场景路径作为入口引用。 |
| 运行时失败 fixture | 目录找不到 `guanzhong_city`，或其功能／入口引用在运行时失效；验证城市不回退到默认值，并保留世界返回出口。 |

---

## 六、后续实现顺序与非目标

1. `D-SETTLEMENT-01` 只完成本契约。
2. `U-CONTENT-CATALOG-01` 在四类数据契约和首批参数闸门齐备后，统一实现 CSV 导入、GUID 保持、跨表校验与只读目录。
3. `U-SETTLEMENT-01` 只在目录可用后，将城市场景由原型定义迁移为数据驱动的功能分发。

本契约不创建 CSV、Language、asset、导入器、目录、场景消费者、功能处理器、其他城市、服务、NPC、价格、奖励、UI 或运行时规则；也不改变现有世界、冒险或石甲兽的进入与返回行为。

## 七、验证口径

- 字段逐项对应已审核的关中城身份、生产范围、唯一启用功能和唯一冒险入口。
- 生产链保持 docs 到消费者的单向边界，且 asset GUID、目录只读语义与导入原子性明确。
- 正反 fixture 覆盖稳定 ID、语言键、功能可用性、处理器注册、入口引用和未知城市的失败关闭；未填写任何生产数据值。
- 直到后续 Unity 实施任务完成直接构建和场景验证前，本文件不声称当前场景已经数据化。
