# 册界体系数据契约

> 状态：`D-TZ-CHARTER-SCHEMA-01` 只建立 schema。它不表示《册界天章》条目、节点、授权、地区长期规则、保存或 Unity 交互已经可用。

## 所有权与分层

- `CharterRuleDefinitions.csv` 是唯一的静态条目定义生产表；每一行是可完整调用的一个规则条目，不能把收益、世界代价、范围或冲突资格拆为独立词条。
- `CharterRuleDefinitionData` 是该表的 Unity 投影。它只保存稳定 ID、明确枚举和战场 `environmentProfile` 输出引用；它不拥有战场环境或地区当前状态。
- `CharterRuntimeStateData` 是独立的动态数据对象，只记录已登记条目、天章／界印状态、节点与授权版本、覆盖、占用、现实供给、正负提交结果和当前地区规则的稳定 ID／状态。它不包含定义对象、`GameSession`、存档版本或规则调用。
- `EnvironmentProfileData` 仍只拥有战场档案。册界定义只能在事件输出中单向引用它，战场档案不能反向保存节点、授权、覆盖、占用或长期地区状态。

## 静态定义表

生产表表头严格固定为以下十八列，当前仅允许这一个表头、没有生产行：

```text
ruleEntryId,displayName,ruleFamily,relationElement,compatiblePhenomena,positiveCommit,negativeCommit,requiredAuthority,requiredNodeTypes,scopeType,scopeTierCap,anchorNodeIds,propagationBoundaryProfileId,currentCoverageSet,affectedWorldVariables,conflictProfileId,failurePolicy,worldEventOutputs
```

字段含义与编码：

| 字段 | 契约 |
|---|---|
| `ruleEntryId` | 唯一稳定条目 ID。 |
| `displayName` | Language 的显示键，不填写显示文本。 |
| `ruleFamily`、`relationElement`、`compatiblePhenomena` | 外部类别、五行母属性与现象目录引用；现象以 `|` 分隔。 |
| `positiveCommit`、`negativeCommit` | 各一个外部提交档案 ID；两者都必须带已声明的现实供给，缺任一方即拒绝。 |
| `requiredAuthority` | 外部权限要求档案；该档案显式引用天章遗物权限与组织授权版本，不能由地点、关系或战力补齐。 |
| `requiredNodeTypes`、`anchorNodeIds` | 节点类型和实际锚点 ID，集合以 `|` 分隔。 |
| `scopeType`、`scopeTierCap` | 明确写入的范围和规模上限。允许值分别为 `SINGLE_NODE`／`CONNECTED_NODES`／`REGIONAL_HUB` 与 `NODE`／`AREA`／`REGION`，不会从空值默认。 |
| `propagationBoundaryProfileId`、`currentCoverageSet` | 传播边界档案与可枚举覆盖集合；覆盖以 `|` 分隔，任何成员越界即拒绝。 |
| `affectedWorldVariables` | 唯一允许影响的外部世界变量 ID，使用 `|` 分隔。 |
| `conflictProfileId` | 外部冲突档案 ID；档案可列出版本化的 `CrossTierChallengeGrant`，但条目不能借此获得未列变量或范围的资格。 |
| `failurePolicy` | 明确的 `REJECT`、`SUSPEND` 或 `SAFE_DOWNGRADE`。安全降级仍由外部档案定义完整正负提交。 |
| `worldEventOutputs` | `eventId~environmentProfileId` 记录，以 `|` 分隔；两个 ID 都必须由外部目录解析。环境档案只是输出引用。 |

`none`、显示文本、文件路径、未声明枚举、测试 fixture 和旧字段都不能充当引用。集合中的重复或空成员同样失败关闭。

## 外部引用与导入

导入器先完整解析并校验表头、每行的十八字段、外部目录、现实供给、范围边界和全部输出，再创建或更新任一 `CharterRuleDefinitionData` asset。当前尚无生产外部目录，因此非空生产表固定以 `CHARTER_REFERENCE_CATALOG_UNDECLARED` 拒绝；这避免把样例字面量误升格为生产内容。

稳定失败类别包括：未知遗物／授权／节点／边界／变量／冲突／环境档案引用、覆盖越界、正负提交不完整、未声明目录，以及未知或默认化的范围和失败策略。失败不创建半 asset，不保留半覆盖，也不借 `EnvironmentProfileData` 或 fixture 补值。

## 动态状态边界

动态状态校验只接受已有 `CharterRuleDefinitionData` 的 `ruleEntryId`。把 `stateId`、节点 ID 或其他状态 ID 填到条目字段，统一视为定义／状态 ID 混用并失败。动态对象还独立检查节点、组织授权版本、覆盖、占用、现实供给和提交结果的稳定引用。

后续 `D-TZ-CHARTER-SAMPLE-01` 才能提供一个门禁、公共设施、节点和水府地纪的外部目录与有效／非法数据；`U-TZ-CHARTER-MODEL-01` 才能执行规则事务、金丹冲突或元婴受锚。
