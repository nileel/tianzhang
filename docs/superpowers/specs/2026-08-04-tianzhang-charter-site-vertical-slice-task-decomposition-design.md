# 《天章》册界单据点纵向切片任务拆分设计

状态：✅ 设计已通过（2026-08-04；用户授权 DeepSeek 补充，Codex 对照生产契约复核确认）。

日期：2026-08-04

## 一、目标与授权边界

本设计把既有父项 `U-TZ-CHARTER-SLICE-01` 拆成一张数据卡与四张 Unity 叶子卡，在一个既有据点内完成可验证的《开阖九章》通行、太玄界印管理、册界节点接通、水府地纪调用、战场环境引用、金丹介入、元婴受锚和保存读取闭环。

本轮只使用已经批准的旧水驿、水府地纪和 `env_guanzhong_wild`。不新增第二据点、第二条册界条目、完整 M7、九域模拟、动态经济、规则编辑器、上界地图、战斗数值或表现资源精修。

既有依赖均已完成并通过对应复审：

- `D-TZ-CHARTER-SAMPLE-01`：水府地纪唯一生产样例及静态引用目录。
- `U-TZ-CHARTER-MODEL-01`：纯规则事务、金丹冲突与元婴受锚。
- `U-TZ-CHARTER-SAVE-01`：schema 4、唯一静态目录和原子恢复。
- `U-ENV-RULE-01A`：`env_guanzhong_wild` 到 `TacticalGridModel` 的运行时投影。
- `U-ENV-RULE-01B`：正式 AdventureScene 的环境表现与反馈；该卡完成时关闭了 backlog 父项 `U-ENV-RULE-01`。

## 二、现状根因与采用方案

当前生产数据已经声明条目、遗物、组织授权版本、节点、覆盖、现实供给、正负提交、冲突档案 ID 和环境档案 ID；但旧门禁目标、操作者、设施管理者、受益者，以及金丹／元婴演示请求的完整身份仍只存在于 EditMode 测试字面量。直接实施场景会把测试 fixture 或控制器硬编码误升格为生产事实。

另一个阻断性根因是前置状态没有生产入口。`CharterRuleRuntime.Invoke` 只接受一次完整事务输入：调用前的 `CharterRuntimeStateData` 必须已经含天章识别、界印识别、授权版本、连通节点、当前覆盖和已登记供给；生产代码目前只会在保存恢复时替换该状态，合法前置状态的构造只存在于测试 helper。仅把父项拆成更多卡不能解决这一点。

因此先建立一个最小 `CharterSiteData` 生产契约，再依次接入交互运行时、Settlement UI、Adventure 环境投影和端到端验证。该方案比把运行时与 UI 合并到同一卡更容易独立复审，也避免把通行、界印、节点拆成反复修改同一场景的碎片卡。

未采用方案：

- 不把全部流程合并为一张场景卡；它会同时修改数据、候选状态生产、长期状态提交、UI、环境消费和 E2E。拆卡本身不解决前置状态生产，采用方案另在 01A 明确建立唯一候选状态构造入口。
- 不把通行、界印和节点分别拆卡；它们会连续触碰相同控制器、视图与场景，形成叠加补丁。
- 不继续使用测试字面量或 SceneBuilder 内的隐藏默认值作为生产站点数据。

## 三、站点数据契约

新增唯一生产链：

```text
CharterSites.csv
  -> DataConfigImporter 整表及跨表校验
  -> CharterSiteData asset
  -> ContentCatalogData 只读查询
```

首批只允许一个站点：`charter_site_old_water_station`。它归属现有 `guanzhong_city`，只引用现有水府地纪和 `env_guanzhong_wild`。

字段按以下语义分组；任务实施时必须冻结为明确表头，不允许用自由 JSON 或无约束键值串代替：

1. 站点身份：`siteId`、`displayNameKey`、`settlementId`。
2. 通行：`passageCapabilityId`、`passageOperatorId`、`passageTargetId`、协议兼容、结构、供能与 `interactionTimeProfileId` 的稳定引用或明确状态。
3. 管理：`facilityId`、太玄界印 ID、`sealManagerId`、`sealBeneficiaryId`、组织授权版本。
4. 册界：`ruleEntryId`、规则条目占用 ID、节点占用 ID；锚点、覆盖、正负提交和现实供给优先由既有定义与参考目录解析，不在站点行重复抄写。
5. 金丹样例：版本化 `CrossTierChallengeGrant` 的完整边界、左右候选和册界候选 ID。grant 列必须完整承载 `grantId`、定义版本、目标变量、挑战者、来源、允许操作、对象、范围、受益者、现实锚点、资源／容量账本、挑战层级、生效／失效 tick、撤销状态／原因和显示来源。左右候选分别以前缀列完整承载 `candidateId`、目标变量、对象、变量权限、目标合法性、位别、现实支点、已支付代价、持续承载、冲突储备、脉冲消耗和结算冷却；不得由运行时补默认值。
6. 元婴样例：冲突事件、目标变量、对象、范围与道路锚点的稳定 ID；结果固定为受锚且不提交状态。

站点契约同时拥有少量尚无外部生产目录的切片身份，但不能把它们伪装成既有战斗术法或角色档案：`passageCapabilityId` 固定为 `capability_kaihe_jiuzhang_v1`，只映射已审核的功能术法《开阖九章》，不写入无法表达功能术法的现有 `Spells.csv`；`interactionTimeProfileId` 固定为 `interaction_time_old_water_station_gate_v1`，同一行必须显式声明“识别瞬时、操作持续引导、取消不提交”；操作者、管理者和受益者是本站点拥有的职责 ID，不声称是全局角色 ID。跨契约字段必须由现有静态目录解析；上述站点自有 ID 必须在本表内唯一、非空并满足固定语义。

金丹样例中的完整 grant 由本站点静态数据拥有，但 `grantId` 必须已经列在水府地纪的 `conflictProfileId` 目录中；运行时只把该只读记录构造成 shared `CrossTierChallengeGrant`，不另建授权来源。导入验证必须用同一 shared 决定证明本站点左右候选的确定性赢家不是 `charterCandidateId`；若候选字段变化后不能稳定得到“册界侧未获胜”，整行拒绝，而不是在 UI 或运行时强制返回结果。

静态站点契约不保存“玩家已经完成某动作”的动态结果。它声明每个动作允许消费的真实身份和预期映射；实际完成度由 01A 的短生命周期 `CharterSiteInteractionProgress` 承载，映射关系见第四节。这样静态内容、临时操作证明与长期地区状态不会混为一体。

导入器必须在写入任何 asset 前整表失败关闭：

- 空、非法或重复站点 ID；非唯一生产站点。
- 未知据点、条目、遗物、授权版本、节点、覆盖、现实供给、提交、冲突 grant、世界变量、环境档案或显示键。
- 门禁协议不兼容、结构损坏、供能不可用却声明为可操作。
- 管理者或受益者缺失，或把通行资格当作管理资格。
- 金丹 grant 缺版本、变量、操作、对象、范围、受益者、现实支点、资源／容量账本、持续、失效条件或来源。
- 元婴样例携带金丹候选、grant 或可直接覆盖结果。

## 四、所有权与运行时边界

- `ContentCatalogData` 只持有和查询唯一生产 `CharterSiteData`；不保存玩家进度、不执行规则。
- `CharterSiteInteractionRuntime` 是无 Unity 场景依赖的前置状态生产者。它拥有短生命周期 `CharterSiteInteractionProgress`，逐步记录本次打开站点后的通行、管理、节点、条目登记和供给准备证明；正式提交前离开场景可丢弃这些临时步骤。
- `CharterSiteInteractionRuntime` 只有在五类证明全部成立后才能构造不可直接持久化的 `CharterInvocationPreparation`。该结果固定 `CandidateState`、站点静态引用、临时 `CrossTierChallengeArchive` 和静态目录版本，不携带单一 `InvocationRequest`，也不会赋值给 `GameSession.CharterRuntimeState`。金丹样例、元婴受锚和正式调用三次评估各自从同一 candidate 派生自身形态的 `InvocationRequest`：金丹带左右候选与 `crossTierChallengeRequest`，元婴置 `YuanyingAnchored` 且不得带候选／请求，正式调用不带冲突介入。
- `GameSession` 继续是唯一长期 `CharterRuntimeStateData` 所有者。首次调用时，它把 `CharterInvocationPreparation.CandidateState` 作为现有 `CharterRuleRuntime.Invoke` 的 `currentState` 输入；只有纯规则返回完整、可校验的 `NextState` 且静态目录版本一致时才原子替换 `CharterRuntimeState`，并在首次替换时把 `CharterDefinitionCatalogVersion` 从 0 原子置为当前生产目录版本，保证随后 schema 4 保存／恢复的版本校验通过。已有长期状态时禁止重新构造一份全新 registered 候选绕过消耗，必须使用当前长期状态再次调用，从而保留 allocated 供给的重复消费拒绝。
- `CharterRuleRuntime` 与 `CharterConflictRules` 保持唯一纯规则实现；本切片只构造经过站点数据校验的请求，不复制合法性、冲突排序或结算。
- `CrossTierChallengeArchive` 只根据站点中的版本化 grant 临时构造；不新增持久 archive、第二账本或保存格式。
- `EnvironmentProfileData` 仍只拥有战场档案。Adventure 从已生效条目解析输出引用并匹配现有序列化 asset，不反向写长期地区状态。
- 操作者与管理者使用本切片已声明的角色／职责 ID；本轮不增加全局 `CharacterData` 稳定角色 ID，也不序列化角色档案。

### 4.1 前置动作到候选状态的唯一映射

| 临时动作证明 | `CharterSiteInteractionProgress` | `CharterInvocationPreparation.CandidateState`／请求映射 |
|---|---|---|
| 《开阖九章》通行成功 | passage 已验证，记录站点声明的 operator／target | 不写长期状态；只允许把同一 `passageOperatorId`／`passageTargetId` 写入最终请求。缺证明时不能构造 preparation。 |
| 界印管理与受益确认 | management 已验证 | `worldSealState = recognized`；站点声明的 manager／beneficiary 写入最终请求。 |
| 节点接通 | connected node ID 集合 | 每个实际节点写为 `nodeStates[*].state = connected`；覆盖只能取定义和传播边界共同允许的集合。 |
| 条目登记与授权确认 | rule entry、天章实例、授权版本已验证 | `charterRelicState = recognized`、`organizationAuthorizationVersions[*].state = recognized`；不提前写 `registeredRuleEntryIds`，该字段仍由成功事务唯一追加。 |
| 现实供给准备 | registered supply ID 集合 | 每个供给写为 `realitySupplyStates[*].state = registered`；正负提交、占用 ID 和结果状态写入最终请求。 |

候选状态固定使用本站点稳定 `stateId`，其 `registeredRuleEntryIds`、占用、提交结果和 `currentRegionRuleEntryIds` 在调用前为空；这些长期结果只能由 `CharterRuleRuntime.Succeed` 一次构造。01A 不修改 `CharterRuleRuntime`，但必须直接测试上述完整映射，而不是在测试中继续调用旧 `BuildValidState` helper 冒充生产入口。

## 五、玩家交互与数据流

固定流程如下：

1. 在 `guanzhong_city` 打开旧水驿入口；控制器先从 `ContentCatalogData` 精确取得本站点和册界静态目录。
2. 使用《开阖九章》识别并开启已声明门禁。协议、结构、供能或目标不合法时停在本步；成功只推进 `CharterSiteInteractionProgress` 的 passage 证明。
3. 使用太玄界印或已登记组织授权确认设施管理者与受益者。通行成功不能自动获得管理权；成功只推进 management 证明。
4. 接通册界、水工、河道／湿地节点。任一节点缺失、断开或越界时不推进；成功记录实际 connected node ID 集合。
5. 登记水府地纪、确认天章实例与授权版本，并准备三个既有现实供给。条目定义、正负提交或供给解析不完整时不推进；成功后 01A 才能按第四节映射构造一份 `CharterInvocationPreparation`。
6. 使用同一 preparation 执行金丹介入样例。完整候选字段必须进入现有 shared 冲突决定并返回 `charter_conflict_not_won`；`NextState` 为空，`GameSession` 不替换长期状态。
7. 使用同一 preparation 执行元婴受锚样例。它返回 `TZ_CHARTER_CONFLICT_YUANYING_ANCHORED`，不降格金丹冲突；`NextState` 为空，`GameSession` 不替换长期状态。
8. 使用同一 preparation 执行无冲突的正式调用。`GameSession` 把 candidate 交给现有纯规则；全部检查通过后，正负提交和占用一次原子形成 `NextState`，再由 `GameSession` 唯一替换。
9. 进入 `guanzhong_wild`。Adventure 根据 `currentRegionRuleEntryIds` 解析水府地纪事件输出，并只匹配现有 `env_guanzhong_wild` asset。
10. 保存、退出并读取。恢复后水府地纪仍登记且生效；相同现实供给不可再次消费。

临时步骤和 candidate 都不是第二套长期状态。尚未正式调用时离开 Settlement 可重置；candidate 只活到一次评估／提交返回，不能直接保存或被 UI 赋值；正式调用后的长期结果只从 `GameSession.CharterRuntimeState` 重建显示，不伪造门禁、授权或供给。

## 六、UI 与场景边界

- 使用现有 `SettlementScene`、`SettlementSceneController`、`SettlementSceneView` 和唯一 `UICanvas`。
- 新增独立 `CharterSiteController` 与 `CharterSiteView`；Settlement 控制器只负责打开面板和提供现有目录／会话引用，不承载步骤规则。
- UI 显示站点、当前步骤、真实稳定 ID、授权缺口、节点、供给、冲突决定、环境引用和失败原因；它只提交动作并刷新结果。
- 不创建第二 Canvas、第二 Settlement 场景、全局 UI 单例、完整册界编辑器、九域地图或动态经济面板。
- Adventure 继续使用现有 `contentCatalog` 和 `guanzhongWildEnvironmentProfile` 序列化引用；环境投影适配器只解析和匹配，不新增环境状态所有者。

## 七、失败关闭与原子性

- 动作越序、目录缺失、站点不属于当前据点、未知引用或被禁用站点：返回稳定原因，不打开或不推进面板。
- 通行、管理、节点、登记和供给任一步失败：只保留本次交互中此前已完成的临时步骤，不写长期状态。
- 任一前置证明缺失、站点静态身份与实际动作结果不一致，或映射得到的 candidate 不能通过现有静态目录／动态状态校验：不能构造 `CharterInvocationPreparation`，不得以测试 helper、默认 recognized／connected／registered 或 UI 布尔值补齐。
- 金丹失败和元婴受锚：`NextState` 必须为空，事件输出为空，长期状态不变。
- 正式调用：candidate 永远不直接赋值给会话；只有 `NextState.TryValidate` 通过且目录版本等于当前生产目录时才替换。首次提交失败时 `GameSession.CharterRuntimeState` 仍为空，`CharterDefinitionCatalogVersion` 保持 0；首次提交成功时两者原子替换，目录版本随之置为当前生产目录版本；已有长期状态的重复调用失败时保持原实例内容不变。
- Adventure 环境 ID 缺失、重复或与序列化 asset 不匹配：显示稳定错误，不使用 fallback，不反写册界状态。
- 成功后的相同供给、提交或占用再次调用：保持现有失败关闭，不重复结算。
- schema 4 当前字段足以持久化正式调用结果；若实现必须升级 schema 才能完成本设计，立即停止并重新设计，不在叶子卡中顺手迁移。

## 八、任务拆分与依赖

父项 `U-TZ-CHARTER-SLICE-01` 不直接执行。叶子依赖固定为：

```text
D-TZ-CHARTER-SITE-01
  -> U-TZ-CHARTER-SLICE-01A
  -> U-TZ-CHARTER-SLICE-01B
  -> U-TZ-CHARTER-SLICE-01C
  -> U-TZ-CHARTER-SLICE-01D
```

所有业务叶子卡使用 `external_execute/deepseek`，完成后转 `codex_review/codex`。创建卡时，第一张合法叶子按现有固定队列规则插入；不得越过当时已有的更早 ready 复审或用户明确顺序。后续卡保持 active blocked，只阻塞于直接前序。

### 8.1 `D-TZ-CHARTER-SITE-01` · 旧水驿最小站点数据契约

职责：建立站点 CSV、数据对象、唯一生产 asset、只读目录查询和完整正反校验；不实施运行时、UI 或场景。

依赖：`D-TZ-CHARTER-SAMPLE-01`、`U-TZ-CHARTER-MODEL-01`、`U-TZ-CHARTER-SAVE-01`、`U-ENV-RULE-01A`、`U-ENV-RULE-01B` 均完成。

预期路径：

- `src/Assets/DataConfig/CharterSites.csv`
- `src/Assets/DataConfig/Language.csv`
- `src/Assets/DataConfig/README.txt`
- `src/Assets/Scripts/Content/CharterSiteData.cs` 及 `.meta`
- `src/Assets/Scripts/Content/ContentCatalogData.cs`
- `src/Assets/Scripts/Editor/DataConfigImporter.cs`
- `src/Assets/Data/CharterSites.meta`
- `src/Assets/Data/CharterSites/CharterSite_charter_site_old_water_station.asset` 及 `.meta`
- `src/Assets/Data/ContentCatalog/ContentCatalog.asset`
- `src/Assets/Tests/EditMode/CharterSiteDataTests.cs` 及 `.meta`
- `tools/check-data-chain.ps1`
- `tools/tests/check-data-chain-tests.ps1`
- `开发管理/册界单据点站点数据验证记录.txt`
- 对应任务卡、归档、backlog 与队列路径

完成条件：生产目录可精确取得唯一旧水驿站点；合法行完整投影；grant、请求与左右候选能经同一 shared 决定稳定得到册界侧未获胜；每类非法引用在任何 asset 写入前失败关闭；没有测试 fixture、场景硬编码、候选默认值或默认站点。

### 8.2 `U-TZ-CHARTER-SLICE-01A` · 会话交互桥与原子提交

职责：实现无场景依赖的临时交互状态机、从五类动作证明到完整未持久化 candidate 的唯一生产入口，以及 `GameSession` 对正式规则结果的唯一原子提交入口；不实施 UI 或环境表现。

依赖：`D-TZ-CHARTER-SITE-01`。

预期路径：

- `src/Assets/Scripts/World/CharterSiteInteractionRuntime.cs` 及 `.meta`
- `src/Assets/Scripts/Game/GameSession.cs`
- `src/Assets/Tests/EditMode/CharterSiteInteractionRuntimeTests.cs` 及 `.meta`
- `src/Assets/Tests/EditMode/SessionStateSnapshotTests.cs`
- `开发管理/册界单据点运行时交互验证记录.txt`
- 对应任务卡、归档、backlog 与队列路径

完成条件：从 `GameSession.CharterRuntimeState == null` 的未接入状态开始，合法动作能构造字段完整且通过现有校验的 candidate；candidate 的 recognized／connected／registered 字段逐项由真实动作证明映射，长期结果字段在调用前为空；越序与失败不推进；金丹未获胜和元婴受锚不提交；正常调用只提交一次且首次提交同时把 `CharterDefinitionCatalogVersion` 置为当前生产目录版本；已有长期状态不能重新自举 registered 候选绕过重复供给拒绝；schema 4 保存读取保持长期结果。

禁止修改 `CharterRuleRuntime`、`CharterConflictRules` 或保存 schema。若现有纯规则缺口使本卡必须修改它们，停止并建立独立根因任务。

### 8.3 `U-TZ-CHARTER-SLICE-01B` · Settlement 单据点交互与最小 UI

职责：在现有 Settlement 场景和唯一 Canvas 上接入旧水驿面板；只消费 01A 的动作与结果。

依赖：`U-TZ-CHARTER-SLICE-01A`。

预期路径：

- `src/Assets/Scripts/Settlement/CharterSiteController.cs` 及 `.meta`
- `src/Assets/Scripts/Settlement/CharterSiteView.cs` 及 `.meta`
- `src/Assets/Scripts/Settlement/SettlementSceneController.cs`
- `src/Assets/Scripts/Settlement/SettlementSceneView.cs`
- `src/Assets/Scripts/Editor/SceneBuilder.cs`
- `src/Assets/Scenes/SettlementScene.unity`
- `src/Assets/Tests/EditMode/CharterSiteViewTests.cs` 及 `.meta`
- `src/Assets/Tests/EditMode/SceneArchitectureEditorTests.cs`
- `开发管理/册界单据点交互与UI验证记录.txt`
- 对应任务卡、归档、backlog 与队列路径

完成条件：当前正式据点可打开唯一站点；按钮按顺序提交并显示真实结果；非法／未知站点不打开；重新生成场景后所有序列化引用完整；没有第二 Canvas 或规则复制。

### 8.4 `U-TZ-CHARTER-SLICE-01C` · Adventure 环境引用与反馈

职责：把已生效地区条目单向解析为现有 Adventure 环境档案，并显示条目事件、档案 ID 和稳定错误；不修改册界长期状态或环境规则。

依赖：`U-TZ-CHARTER-SLICE-01B`。

预期路径：

- `src/Assets/Scripts/Adventure/CharterEnvironmentProjection.cs` 及 `.meta`
- `src/Assets/Scripts/Adventure/AdventureSceneController.cs`
- `src/Assets/Tests/EditMode/CharterEnvironmentProjectionTests.cs` 及 `.meta`
- `src/Assets/Tests/EditMode/SceneArchitectureEditorTests.cs`
- `开发管理/册界单据点环境引用验证记录.txt`
- 对应任务卡、归档、backlog 与队列路径

完成条件：只有当前地区已生效的水府地纪能解析为定义声明的 `env_guanzhong_wild`；缺失、重复、越界或 asset ID 不匹配时失败关闭；环境消费者不反写册界状态。

### 8.5 `U-TZ-CHARTER-SLICE-01D` · 单据点端到端闸门

职责：只补端到端验证入口和状态收口，不新增业务规则。

依赖：`U-TZ-CHARTER-SLICE-01C`。

预期路径：

- `src/Assets/Tests/PlayMode.meta`
- `src/Assets/Tests/PlayMode/TianZhang.PlayModeTests.asmdef` 及 `.meta`
- `src/Assets/Tests/PlayMode/CharterVerticalSlicePlayModeTests.cs` 及 `.meta`
- `tools/run-unity-playmode-tests.ps1`
- `tools/test-run-unity-playmode-tests.ps1`
- `开发管理/册界单据点端到端验证记录.txt`
- `开发管理/设计-当前状态.txt`
- `开发管理/任务列表/场景与Unity任务.txt`
- 对应任务卡、归档与队列路径

完成条件：PlayMode 在正式场景连续执行通行、管理、节点、登记、两类冲突、正式调用、Adventure 环境反馈、保存和读取；读档后长期状态一致且重复消费失败；第一章、关中悬赏和正式冒险所有者不被改写。01D 通过后关闭父项 `U-TZ-CHARTER-SLICE-01`。

## 九、验证矩阵

| 层级 | 最小充分验证 |
|---|---|
| 站点数据 | 表头、唯一生产行、字段投影、跨表解析、正反 fixture、原位 asset 与目录引用。 |
| 前置状态自举 | 从空 `GameSession.CharterRuntimeState` 依次完成五类动作证明；每步只推进临时 progress；全部完成后 candidate 的天章／界印、授权、节点、覆盖和供给逐项正确，长期结果字段仍为空；缺任一步不能构造 candidate。 |
| 交互运行时 | 正确顺序、每种越序、失败不变、金丹失败、元婴受锚、正常唯一提交、重复消费。 |
| Settlement UI | 正确据点打开、各步骤刷新、真实失败原因、未知站点不打开、场景序列化引用。 |
| Adventure 投影 | 当前地区条目解析、精确环境匹配、缺失／重复／错配失败、不反写。 |
| 保存读取 | schema 4 往返、目录版本、原子恢复、长期结果稳定、未访问旧档保持未接入。 |
| E2E | 正式场景连续流程和失败节点可观察；读档后仍生效；相同供给不可重复。 |

各卡按实际影响运行：

- `dotnet build src/TianZhang.EditModeTests.csproj`
- `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/run-unity-editmode-tests.ps1`
- 数据路径变化时运行 `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/tests/check-data-chain-tests.ps1` 与 `tools/check-data-chain.ps1`
- 01D 运行 `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/run-unity-playmode-tests.ps1`
- `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-review-text.ps1 -Paths 开发管理,docs,src/Assets/Scripts,src/Assets/Tests`
- 提交前运行 `tools/check-pending-whitespace.ps1`，暂存后运行 `git diff --cached --check`

本设计不改变数值、倍率、概率、产量或战斗规则，不运行 BattleSim。任一叶子若需要修改 shared 冲突语义或得出数值结论，停止并另立对应规则／数值任务。

## 十、统一停止条件

- 需要第二据点、第二 Settlement／Adventure 场景、第二 Canvas、第二会话／存档／冲突所有者。
- 需要新增册界条目、完整 M7、完整角色身份系统、九域天气、经济模拟、任意规则编辑器或上界地图。
- 需要修改 CTB、伤害、防御、冷却、寻路、技能范围、掉落或奖励数值。
- 站点契约不能完整承载版本化金丹 grant，而必须使用测试 fixture、代码硬编码或默认 archive。
- 正式调用结果不能由现有 schema 4 表达，或恢复必须增加兼容分支、静默默认或第二保存格式。
- 实施开始连续叠加补丁、跨越相邻叶子职责或修改未批准路径；停止并重新判断根因，不继续扩卡。
