# 册界状态持久化契约设计

> 状态：`U-TZ-CHARTER-SAVE-01A` 已锁定后续实现的静态校验来源、schema 迁移和原子恢复边界。本文不实现存档字段、运行时静态数据、场景、UI、BattleSim 或册界规则事务。

## 目标与已核验现状

后续保存只能把 `CharterRuntimeStateData` 作为动态实例状态写入既有 `GameSessionSnapshot` 链。它不能把 `CharterRuleDefinitionData`、节点、授权目录或 shared 冲突对象写入存档，也不能由存档、场景或 UI 反向拥有这些静态事实。

- `GameSession` 是当前唯一跨场景会话所有者；`CaptureSaveData` 和 `RestoreSaveData` 都委托 `GameSessionSnapshot`，当前 schema 是 `3`。
- `GameSessionSnapshot.Restore` 已先构造 `GameSessionRestoredState`，`GameSession.RestoreSaveData` 在校验悬赏引用后才替换既有集合。该边界是册界恢复唯一可扩展位置。
- `ContentCatalogData` 是当前玩家运行时内容目录，但只持有据点、敌人、物品和悬赏。它尚未引用册界静态定义或静态引用目录。
- `CharterRuntimeStateData` 已是唯一动态承载：已登记条目、遗物／界印状态、节点、授权版本、覆盖、占用、现实供给、正负提交和当前地区规则均只以稳定 ID／状态保存。其 `CreateCopy` 与 `TryValidate` 是保存投影的直接边界。
- 当前唯一生产 `CharterRuleReferenceCatalog` 由 `DataConfigImporter.CreateProductionCharterRuleReferenceCatalog` 在 `Assets/Scripts/Editor` 构造；它只能用于导入，不可被玩家运行时或读档调用。唯一已导入规则 asset 是 `CharterRuleDefinition_charter_entry_suifu_diji.asset`。

## 方案选择

采用方案 A：在既有 `ContentCatalogData` 下增加一个只读的 `CharterRuleStaticCatalogData` 引用，并让该引用成为唯一玩家运行时静态校验来源。

| 方案 | 结论 | 原因 |
|---|---|---|
| A：`ContentCatalogData` 引用一个册界静态目录 asset | 采用 | 延续 `GameSession.RestoreSaveData(ContentCatalogData)` 的既有依赖方向；定义 asset 与批准引用目录各有一个可追溯运行时来源。 |
| B：由 `GameSession`、场景或静态单例直接序列化定义／节点／授权 | 拒绝 | 会让会话或场景变成第二个静态目录，并绕开既有内容目录。 |
| C：玩家运行时调用 `DataConfigImporter`，或重建当前 EditMode fixture | 拒绝 | Editor API 与 fixture 都不是玩家运行时事实源，且会复制或默认化静态事实。 |

## 唯一运行时静态目录

后续实现新增 `TianZhang.Content.CharterRuleStaticCatalogData`，唯一生产 asset 固定为：

```text
Assets/Data/CharterRuleStaticCatalog/CharterRuleStaticCatalog.asset
```

该 asset 只包含两类静态输入：

1. 已导入的 `CharterRuleDefinitionData[]` 直接 asset 引用；不能内嵌、复制或从存档重建定义。
2. 一个已批准的、可序列化 `CharterRuleReferenceCatalog`；它保存现有导入器已批准的遗物、授权版本、节点、边界、供给、提交、变量、冲突、事件和环境档案稳定 ID。它不是第二个节点／授权目录，而是取代 `CreateProductionCharterRuleReferenceCatalog` 中的唯一同一目录存放处。

`ContentCatalogData` 只保存这个 asset 的单一引用并暴露失败关闭的取得方法；它不复制条目、节点或授权数组。`CharterRuleStaticCatalogData` 必须同时验证：目录显式声明、目录内无重复稳定 ID、每个定义具有唯一 `ruleEntryId`、每个定义的十八字段及全部外部引用均由同一目录解析。零、缺失或不匹配的 `definitionCatalogVersion` 不得从默认值推断。

为保持一个静态目录而非两套实现，`CharterRuleReferenceCatalog` 改为可序列化数据类型，通用定义／目录校验移至 Runtime 可编译的 Content 代码；`DataConfigImporter` 和玩家运行时都调用同一校验。导入器只读取上述 canonical asset 的批准目录来验证 CSV，再更新已导入 definition asset 和其直接引用；它不得再持有第二份硬编码生产目录。`ImportAll` 的“先导入册界定义、再提交 `ContentCatalog` 引用”顺序保持不变。

EditMode 可以用瞬态 fixture 覆盖负例，但 fixture 不得被 `ContentCatalogData`、`GameSession`、任何 scene/prefab 或保存 DTO 引用。玩家运行时也不得调用 `AssetDatabase`、`DataConfigImporter`、`Resources` 搜索、显示文本／路径匹配或缺省权限补值。

## 状态所有权、schema 与迁移

`GameSession.CharterRuntimeState` 是会话中唯一的动态册界状态属性。它只在以下边界被整体替换：

- `BeginNewGame`、`ClearSession` 与 `SetPlayerProfile` 清除为明确的未接入状态（`null`），不创建遗物识别、授权、节点、覆盖、供给、占用或提交记录。
- 正常规则调用仍由 `CharterRuleRuntime` 在自身成功事务中构造 `CreateCopy` 后的状态；保存链只能捕获该状态的深复制，不能调用规则、重放事件或重新结算。
- `GameSessionSnapshot.Capture` 和 `Restore` 是唯一 DTO 投影。`GameSession` 只接受经过恢复校验的候选状态后一次替换。

下一版固定为 schema `4`。`GameSessionSaveData`／`GameSessionRestoredState` 新增成对字段：`hasCharterRuntimeState`、`charterDefinitionCatalogVersion` 和 `charterRuntimeState`。`charterDefinitionCatalogVersion` 是静态目录的显式版本快照，不属于 `CharterRuntimeStateData`；动态状态不得反向持有定义版本或定义对象。

| 保存版本 | 册界字段处理 | 恢复结果 |
|---|---|---|
| 0、1、2、3 | 不读取不存在的册界字段。 | 明确未接入：`HasCharterRuntimeState=false`、状态为 `null`、版本为 `0`；不得伪造任何识别、授权、节点、覆盖、供给、占用或提交。 |
| 4，`hasCharterRuntimeState=false` | 只接受空 payload 与版本 `0`。 | 明确未接入；不因当前生产定义存在而自动登记。 |
| 4，`hasCharterRuntimeState=true` | 要求非空状态和正的显式定义目录版本。 | 仅当保存版本等于唯一运行时静态目录版本且完整校验通过，才作为候选状态。 |

schema `4` 的缺字段、错误 presence 组合、零／未知定义目录版本、未知或重复稳定 ID、缺失正负提交、非法覆盖或半状态一律失败关闭；不提供 schema `3` 兼容读取层、版本默认值或局部恢复。将来定义目录版本改变时，必须先建立独立迁移契约；本卡不授权按 ID 猜测转换实例状态。

## 原子恢复顺序

`GameSession.RestoreSaveData` 必须在写入 `QuestStates`、`InventoryStates`、`NpcStates`、`BountyStates`、世界字段或 `CharterRuntimeState` 前完成下列顺序。任何步骤失败均保留恢复前的整个会话 JSON 不变。

1. `GameSessionSnapshot.Restore` 验证总体 schema、DTO 结构、现有世界／集合稳定 ID 和重复项，构造未应用的 `GameSessionRestoredState`。
2. 解析 `ContentCatalogData` 的唯一 `CharterRuleStaticCatalogData`。若 schema 为 4 且 payload 存在，先验证该目录、其定义数组、目录版本和定义／状态分层；不得进入 Editor、fixture 或替代目录。
3. 验证保存的定义目录版本与当前批准目录完全相等；再用该目录验证状态的条目、遗物／界印、节点、组织授权版本、覆盖、条目／节点占用、现实供给、正负提交与当前地区规则。
4. 验证每个已登记／当前地区条目与静态定义的完整关系：节点与覆盖属于其定义边界，授权与定义要求匹配，占用与供给的稳定 ID 唯一，正负提交成对且都能解析。读档只验证已保存的结果，不调用 `CharterRuleRuntime.Invoke`、`RuleConflictInstance.Decide`、环境事件、供给分配或任何账本写入。
5. 完成既有悬赏目录／目标进度校验及册界候选的全部深复制后，以一个 `GameSession` 应用点替换全部集合、世界字段、既有道基／紫府投影和册界状态。应用点本身不再可能抛出可验证错误。

因此失败读档既不会重复结算已分配现实供给、占用或正负提交，也不会留下旧集合与新册界状态混合的半恢复会话。成功读档只恢复已保存的 `allocated`／结果事实；它不再次调用规则事务或发射环境事件。

## 下游 `U-TZ-CHARTER-SAVE-01` 实施路径

实现卡只允许以下路径；未列路径（包括场景、UI、BattleSim、`CharterRuleRuntime.cs`、`CharterConflictRules.cs`、CSV 生产条目和环境档案）不得修改。

```text
src/Assets/Scripts/Content/CharterRuleDefinitionData.cs
src/Assets/Scripts/Content/CharterRuleStaticCatalogData.cs
src/Assets/Scripts/Content/CharterRuleStaticCatalogData.cs.meta
src/Assets/Scripts/Content/ContentCatalogData.cs
src/Assets/Scripts/World/CharterRuntimeStateData.cs
src/Assets/Scripts/Editor/DataConfigImporter.cs
src/Assets/Data/CharterRuleStaticCatalog.meta
src/Assets/Data/CharterRuleStaticCatalog/CharterRuleStaticCatalog.asset
src/Assets/Data/CharterRuleStaticCatalog/CharterRuleStaticCatalog.asset.meta
src/Assets/Data/ContentCatalog/ContentCatalog.asset
src/Assets/Scripts/Game/GameSession.cs
src/Assets/Scripts/Game/SessionStateSnapshots.cs
src/Assets/Tests/EditMode/CharterRuleDataTests.cs
src/Assets/Tests/EditMode/CharterRuleRuntimeTests.cs
src/Assets/Tests/EditMode/SessionStateSnapshotTests.cs
src/Assets/DataConfig/README.txt
开发管理/任务归档/验证记录/册界状态持久化验证记录.txt
```

直接 EditMode 用例至少覆盖：生产静态目录只解析已导入定义与批准目录、运行时无 Editor／fixture 回退、schema 4 往返深复制、schema 0～3 的明确未接入状态、目录版本不符、未知／重复条目、未知节点／授权／覆盖、占用或供给重复、缺失任一正负提交、当前地区条目不合法、非法档对完整旧会话的原子拒绝、已分配供给／占用／提交在重复读取中不重新结算。现有 `CharterRuleRuntimeTests` 必须改为从同一静态目录取得生产输入，不能继续调用 Editor factory。

实施者必须运行：

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/run-unity-editmode-tests.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-data-chain.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-review-text.ps1 -Paths 开发管理,docs,src/Assets/Scripts,src/Assets/Tests
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-task-cards.ps1 -TaskId U-TZ-CHARTER-SAVE-01 -Postcondition ExternalPendingReview
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-pending-whitespace.ps1
git diff --cached --check
```

`U-TZ-CHARTER-SAVE-01` 由 DeepSeek V4 Flash 执行，完成后转换为 `codex_review/codex/ready`，只能由 Codex 独立复审。若实现需要第二存档、第二静态目录、默认 ID／授权／供给、Editor 运行时读取，或无法在所有现有会话字段与册界状态之间一次原子替换，立即停止并转回 `pending_decision`，不提交兼容层或半恢复实现。
