# 关中正式战场与障碍格合同

## 冻结结果

`env_guanzhong_wild` 的正式战斗棋盘固定为 6×6 轴坐标平行四边形：

- 坐标全集：`{ (q,r) | q∈[0,5] 且 r∈[0,5] }`，共 36 格。
- 不可通行障碍：`(2,1)`、`(3,1)`、`(2,4)`、`(3,4)`，共 4 格；其余 32 格可地面进入。
- 玩家起点：`(0,0)`；石甲兽起点：`(1,0)`。两者均为可通行格，且保持现有正式流程首回合普攻距离。
- 双向邻接：任意两格都在坐标全集内且差值为 `(1,0)`、`(1,-1)`、`(0,-1)`、`(-1,0)`、`(-1,1)` 或 `(0,1)` 时，生成两个方向的边。36 格共 85 条无向邻接、170 条有向边。
- 从玩家到石甲兽的冻结路径为 `(0,0) → (1,0)`；该路径不经障碍。障碍格不从图中移除，因此仍能作为视觉和视线查询中的已定义格。

## 格型语义

障碍格投影到既有 `TacticalTileData` 时固定为：

| 字段 | 值 |
|---|---|
| `TerrainType` | `Obstacle` |
| `BlocksGroundMove` | `true` |
| `BlocksLanding` | `true` |
| `BlocksFlyingMove` | `false` |
| `BlocksLineOfSight` | `false` |
| `IsEntityObstacle` | `false` |

因此障碍格不能作为地面移动或落点，但首版不阻挡视线或效果路径；不得把“不可进入”默认扩大为“阻挡视线”。本合同不引入高差、可破坏物、飞行阻挡或新地形系统。

## 唯一数据映射

CSV 的 `EnvironmentProfiles.csv::env_guanzhong_wild` 新增唯一手工输入列 `battlefieldCells`。每项编码为 `q:r@blocksGroundMove@blocksLineOfSight`；36 项按 `q`、再 `r` 升序列出，四个障碍使用 `@1@0`，其余使用 `@0@0`。`queryLimits` 只保存 `unitsPerRange=2;maxQueryRange=16`。

导入器从 `battlefieldCells` 校验非空、坐标唯一和布尔值，并只从该列表生成 170 条 `directedEdges` 写入 `EnvironmentProfile_env_guanzhong_wild.asset`。边不是第二份手工地图。asset 的 `battlefieldCells` 保留原始格型；运行时 `CombatEntryAdapter` 将它逐项投影为 `TacticalTileData`，`SpatialQueryBoardFactory` 继续消费同一 asset 中导出的边和格位。`AdventureSceneBuilder` 同样逐项读取 `battlefieldCells`，不得再由边端点推断显示格。

这条链为：

`EnvironmentProfiles.csv.battlefieldCells → WorldContentImporter → EnvironmentProfileAsset.battlefieldCells/directedEdges → CombatEntryAdapter.TacticalTileData + AdventureSceneBuilder → SpatialQueryBoard / GuanzhongBattlefield`

其中 `directedEdges` 为导入时确定性投影，不是第二份手工地图。

## 正式显示

36 格均复用 `Assets/Art/Environments/Guanzhong/Tile.prefab`，位置继续使用 `AdventureSceneBuilder.HexToWorld` 和现有格子世界尺寸。四个障碍格在对应 tile 下实例化既有 `Stair.prefab`，命名为 `GuanzhongObstacle_<q>_<r>`；不创建、复制或修改美术资源及其 `.meta`。视觉 stair 仅表现已定义障碍格，不承担阻挡逻辑。

## 下游实施与验证

`U-GZ-PLAYTEST-MAP-01` 只可修改以下实现面：环境 schema/asset/importer、关中 CSV 与生成 asset、`CombatEntryAdapter`、`AdventureSceneBuilder`、正式场景和对应数据/场景测试、环境档案与运行时结构说明、数据链表头检查。它不修改移动行动阶段、相机、数值、角色表现或其他地区。

必须验证：CSV 与 asset 一致；36 格、4 障碍、170 有向边、双向邻接、起点合法；障碍不可地面进入且不挡视线；玩家到敌人路径可达；Builder 保存重开后 36 个地块与 4 个 stair 障碍一致；正式 PlayMode 入口仍能创建会话并展示同源坐标。最终用户流程验收仍由 `V-GZ-PLAYTEST-FLOW-01` 负责。
