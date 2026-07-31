# 册界跨端冲突／跨阶授权契约设计

> 状态：`D-TZ-CHARTER-CONFLICT-01` 已完成架构锁定。本设计只定义后续共享实现的唯一边界；不在本文件所对应的任务中创建模块或改动 Unity、BattleSim、存档、样例或数值。

## 已核验现状与术语校正

- `src/Assets/Scripts/World/TianZhang.Gameplay.asmref` 已把 `World/` 目录编入 `TianZhang.Gameplay`；`CharterRuntimeStateData` 与跨场景状态所有者 `GameSession` 当前都在该程序集。前者只保存动态稳定 ID／结果，后者当前只保存任务、背包、NPC、世界时间、地点与筑基紫府快照；二者都没有册界规则或跨阶授权结算入口。
- `simulations/BattleSim/BattleSim.csproj` 是 `net10.0` 可执行项目，当前以 linked source 的方式消费 `src/Assets/Scripts/Core/SpatialRules/*.cs`，没有对 Unity 工程的项目引用。Unity 的 `TianZhang.Gameplay` 生成项目为 C# 9／`netstandard2.1`，且现有 Unity 源码未使用 record；共享源码必须只依赖 BCL，不能引用 `UnityEngine`、`ScriptableObject`、场景、存档或 BattleSim 的 `Character`／账本类型。
- 原任务文字中的 `RuleConflictInstance` 在 `src/` 与 `simulations/` 当前均不存在，不能作为可迁移的既有类型。实际的同变量金丹结算是 `simulations/BattleSim/GameData.cs` 中的 `GoldenCoreConflictCandidateInput`、`GoldenCoreConflictCandidates` 与 `GoldenCoreConflictResolver`，通过 `simulations/BattleSim/Combat.cs::ResolveGoldenCoreConflict` 进入；`Character::PrepareGoldenCoreConflictCandidate` 是它的准备适配点。
- 实际跨阶资格是同一 `GameData.cs` 中的 `CrossTierChallengeSourceKind`、`CrossTierChallengeGrant`、`CrossTierChallengeRequest`、`CrossTierChallengeResolution` 与 `CrossTierChallengeArchive`，唯一直接入口为 `Combat.ResolveCrossTierChallenge`。仓库扫描到的消费者只有 BattleSim 自测；不存在 Unity 同名类型或第二个授权入口。
- 这个命名差异不改变已锁定规则：`RuleConflictInstance` 将作为新建的共享、无副作用的“冲突请求／决定”类型；既有 BattleSim 金丹结算和跨阶资格的可观察理由、优先序、资源扣除与冷却结果必须迁入或由它驱动，不能另写 Unity 副本。

## 方案选择

负责人已选择 A，以下仅记录已排除的边界：

| 方案 | 结果 | 结论 |
|---|---|---|
| A：一份无 Unity／BattleSim 依赖的 shared source，由两端编译消费 | 两端使用同一类型、资格重验和冲突决定；BattleSim 保留仅负责把结果写入自身账本的适配层。 | 采用。 |
| B：在 Unity 重新实现 BattleSim 的授权与冲突 | 会形成两套理由、优先序与资源结算。 | 拒绝。 |
| C：只把 Unity 路径默认拒绝 | 会把金丹冲突或元婴受锚错误缩小为默认拒绝。 | 拒绝。 |

## 唯一所有者与依赖方向

唯一规则所有者定为新文件 `src/Assets/Scripts/World/CharterConflictRules.cs`，命名空间为 `TianZhang.World`。该文件由现有 `World/TianZhang.Gameplay.asmref` 编入 `TianZhang.Gameplay`，并由 `simulations/BattleSim/BattleSim.csproj` 以一个 `<Compile Include="..\\..\\src\\Assets\\Scripts\\World\\CharterConflictRules.cs" Link="..." />` 链接编译；不新建 asmdef、NuGet 包、服务器或第二个项目。

```text
册界定义／状态校验（未来 U-TZ-CHARTER-MODEL-01 的实时扫描所有者）
                         │ 只构造完整输入；不结算
                         ▼
src/Assets/Scripts/World/CharterConflictRules.cs
  CrossTierChallengeArchive + RuleConflictInstance（唯一资格／决定）
             │                                     │
             │ linked source                        │ Unity 直接消费
             ▼                                     ▼
BattleSim 的金丹账本适配与写入                  册界纯规则事务
```

- 共享文件不持有 `GameSession`、`CharterRuntimeStateData`、存档 schema、静态目录、场景或全局单例；它只接收已验证的稳定 ID、枚举、整数和不可变候选值。
- `CharterRuntimeStateData` 仍只由未来册界规则／保存链管理稳定状态；它不得保存共享对象实例或复制 grant。`GameSession` 也不在 `D-TZ-CHARTER-CONFLICT-02` 接入，持久化仍是 `U-TZ-CHARTER-SAVE-01` 的边界。
- BattleSim 的 `GoldenCoreConflictCandidates` 继续校验金丹装配、席位、能力账本和成本档案，并把通过的候选映射为共享候选。`Combat.ResolveGoldenCoreConflict` 仅调用共享决定并原子应用其明确给出的账本扣除／冷却；它不得比较优先级、选择赢家或再算脉冲。
- Unity 的后续纯规则所有者在完成定义、节点、授权、覆盖与供给校验后直接调用共享文件；它不得声明本地 `CrossTierChallenge*`、`RuleConflictInstance` 或冲突 resolver。

## 契约 v1

### 跨阶资格（保留既有语义）

共享文件迁入现有 `CrossTierChallengeSourceKind`、`CrossTierChallengeGrant`、`CrossTierChallengeRequest`、`CrossTierChallengeResolution` 和 `CrossTierChallengeArchive`，字段与当前 BattleSim 顺序和含义保持不变：

- grant：`GrantId`、`DefinitionVersion`、`TargetVariableId`、`ChallengerId`、`QualificationSource`、`AllowedOperationId`、`TargetId`、`ScopeId`、`BeneficiaryId`、`RealityAnchorId`、`ResourceLedgerRef`、`CapacityLedgerRef`、`ChallengeRuleTier`、生效／失效 tick、撤销状态／理由和显示来源；
- request：`ChallengeEventId`、`GrantId`、期望定义版本、目标变量、挑战者和世界 tick；
- resolution：资格布尔值、稳定理由和只读 grant。

`CrossTierChallengeArchive.Resolve` 仍只做现有版本、撤销、时间、目标变量和挑战者重验；相同输入不扣资源、不写胜负。它必须原样保留 `JD_CHALLENGE_REQUEST_INVALID`、`JD_CHALLENGE_GRANT_UNKNOWN`、`JD_CHALLENGE_GRANT_INVALID`、`JD_CHALLENGE_VERSION_MISMATCH`、`JD_CHALLENGE_REVOKED`、`JD_CHALLENGE_NOT_YET_EFFECTIVE`、`JD_CHALLENGE_EXPIRED`、`JD_CHALLENGE_TARGET_MISMATCH`、`JD_CHALLENGE_CHALLENGER_MISMATCH`、`JD_CHALLENGE_AUTHORIZED`，以及空 archive 的 `JD_CHALLENGE_ARCHIVE_UNAVAILABLE`；迁移不得借机改变现有 BattleSim 输入的结果。

### 册界冲突实例与金丹决定

v1 新增的 `RuleConflictInstance` 是共享的、短生命周期的纯值；它不是存档 DTO，也不是赢家、资源或场景状态的第二所有者。它的完整字段为：

- `ContractVersion`（只接受显式 `1`）、`ConflictEventId`、`Kind`（`JindanSameVariable` 或 `YuanyingAnchored`）、`RuleEntryId`、`TargetVariableId`、`AllowedOperationId`、`TargetId`、`ScopeId`、`BeneficiaryId`、`RealityAnchorId`、`ResourceLedgerRef`、`CapacityLedgerRef` 与 `WorldTick`；
- 金丹分支的 `LeftCandidate`／`RightCandidate`（`RuleConflictCandidate`）：`CandidateId`、`TargetVariableId`、`TargetId`、`HasVariableAuthority`、`HasLegalTarget`、`PositionRank`、`RealityAnchorRank`、`AlreadyPaidCost`、`HasActiveContinuousCarrier`、`ConflictReserve`、`PulseCost` 与 `SettlementCooldown`；
- 可选的 `CrossTierChallengeRequest`。跨阶时，实例先调用同一文件的 archive；随后以授权 grant 的 `AllowedOperationId`、`TargetId`、`ScopeId`、`BeneficiaryId`、`RealityAnchorId`、资源账本和容量账本引用与实例逐项绑定，任一不符即失败关闭。

`RuleConflictInstance` 的金丹决定复用现有顺序：变量权限与合法目标 → 席位 → 现实锚 → 已付代价 → 持续载体 → 脉冲；同级才产生储备扣除和冷却建议。它返回赢家／中立／拒绝、稳定理由、两侧建议扣除、冷却和被拒候选数，但不自行写账本。BattleSim 再应用该结果；Unity 未来只把同一决定纳入自己的原子规则事务，不能重新排序或重算脉冲。

新字段只使用普通不可变 C# 类、字符串、布尔、整数和显式枚举，确保 C# 9／`netstandard2.1` 与 BattleSim 都可编译；不使用 record、`JsonUtility`、Unity 序列化、反射或隐式默认值。`ContractVersion` 缺失、零或非 `1` 统一为 `TZ_CHARTER_CONFLICT_CONTRACT_VERSION_UNSUPPORTED`，非法／空输入为 `TZ_CHARTER_CONFLICT_INPUT_INVALID`，grant 全字段绑定不符依次为 `TZ_CHARTER_CONFLICT_GRANT_OPERATION_MISMATCH`、`TZ_CHARTER_CONFLICT_GRANT_TARGET_MISMATCH`、`TZ_CHARTER_CONFLICT_GRANT_SCOPE_MISMATCH`、`TZ_CHARTER_CONFLICT_GRANT_BENEFICIARY_MISMATCH`、`TZ_CHARTER_CONFLICT_GRANT_ANCHOR_MISMATCH`、`TZ_CHARTER_CONFLICT_GRANT_RESOURCE_LEDGER_MISMATCH`、`TZ_CHARTER_CONFLICT_GRANT_CAPACITY_LEDGER_MISMATCH`。迁入的 `JD_CONFLICT_*` 拒绝理由也保持原值。v1 不提供未版本化构造、默认 grant、旧 DTO 转换器或兼容分支。

### 元婴受锚

静态目录、节点和道路锚点是否命中仍由未来册界规则层验证；共享文件不查询目录。命中已验证元婴道路锚点时，该层构造 `Kind=YuanyingAnchored` 的 v1 实例，唯一结果为 `TZ_CHARTER_CONFLICT_YUANYING_ANCHORED`。该结果不调用跨阶 archive、不降格到金丹脉冲结算、不提交覆盖或资源，也不自动成功／失败其他规则。之后是否持久化受锚状态仍由模型与保存链的既有原子边界决定。

## `D-TZ-CHARTER-CONFLICT-02` 迁移顺序与删除边界

1. 在 `src/Assets/Scripts/World/` 创建上述共享源码及 `.meta`，先以 Unity EditMode 直接测试证明 v1、金丹优先序、跨阶重验、全字段绑定和元婴受锚都是无副作用的。
2. 在 `simulations/BattleSim/BattleSim.csproj` 增加该源码的 linked compile 项；不得改生成的 `src/TianZhang.Gameplay.csproj`，不得添加 Unity 到 BattleSim 的项目引用。
3. 将 `GameData.cs` 的全部 `CrossTierChallenge*` 定义和 archive 解析移出到共享文件；BattleSim 改为 `using TianZhang.World`。先保持自测中每个已有输入的资格理由不变。
4. 将 `GoldenCoreConflictResolver` 的决定顺序迁为 shared `RuleConflictInstance`；`GoldenCoreConflictCandidates`／`Character.PrepareGoldenCoreConflictCandidate` 保留为 BattleSim 输入适配与静态账本校验，`Combat.ResolveGoldenCoreConflict` 只负责适配与一次性应用返回差额。
5. 删除 `Combat.ResolveCrossTierChallenge`、`GameData.cs` 内旧 `CrossTierChallenge*` 与旧 `GoldenCoreConflictResolver`。`BattleSimSelfTests` 直接调用 shared archive／实例；不得留下同名别名、转发 wrapper、DTO 镜像或第二个 resolver。
6. 只有两端构建和直接测试都通过后才关闭迁移。然后 `U-TZ-CHARTER-MODEL-01` 可按其自身实时扫描建立执行卡；它在此之前继续等待，不能绕过共享实现。

回滚／失败关闭：若 linked source 不能同时通过 Unity 的 C# 9／`netstandard2.1` 与 BattleSim 构建，或任一既有 BattleSim 跨阶／金丹理由、扣除、冷却、幂等结果变化，停止该实施卡，保留直接失败证据，不提交半迁移、兼容 shim、第二结算或存档迁移。若只缺未来 Unity 模型所有者，停在共享文件和直接测试，不提前命名或实现该模型。

## 直接测试矩阵

| 边界 | Unity EditMode 共享测试 | BattleSim 自测 |
|---|---|---|
| v1 与序列化边界 | 缺失／错误版本、空 ID、无 Unity 类型、无状态写入 | linked source 编译通过 |
| 金丹同变量 | 权限、目标、席位、锚、已付代价、持续载体、脉冲和中立决定 | `golden-core-conflict-n-jd-rule-01b` 的赢家、理由、储备和冷却保持 |
| 跨阶授权 | 有效、未知 grant、版本不符、撤销、过期、变量／挑战者不符，以及 grant 全字段绑定不符 | `golden-core-challenge-death-n-jd-rule-01c` 的既有授权理由与幂等结果保持 |
| 元婴受锚 | 只返回受锚理由，不触发 grant 或金丹结算 | 不影响金丹死亡与既有战斗路径 |
| 删除边界 | `rg` 只保留共享文件中的 `CrossTierChallenge*`／`RuleConflictInstance` 定义 | `GameData.cs` 和 `Combat.cs` 无旧 archive／跨阶 wrapper／旧 resolver 定义 |

实施卡必须运行：

```powershell
dotnet build -c Release --no-restore "D:\天章游戏开发\simulations\BattleSim"
dotnet run --no-build -c Release --project "D:\天章游戏开发\simulations\BattleSim"
dotnet build src/TianZhang.EditModeTests.csproj
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-review-text.ps1 -Paths 开发管理,docs,simulations,src
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-task-cards.ps1 -OutputJson
```
