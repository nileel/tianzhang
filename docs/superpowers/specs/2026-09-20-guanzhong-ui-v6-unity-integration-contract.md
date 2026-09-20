# 关中 UI v6 → 正式 Unity 接入合同

日期：2026-09-20
任务：`D-GZ-UI-V6-CONTRACT-01`
状态：已冻结接入边界；不构成 Unity 实施、最终美术或实机验收通过。

## 1. 冻结结论与边界

用户已确认 `assets/generated-scene-art/guanzhong-first-bounty/ui-visual-v6/` 的 v6 原型、共享模板和复用规格为当前设计基线。该确认只冻结设计输入：它没有创建正式 PNG/SVG/字体、没有修改 `src/`、Prefab、场景或运行时数据，也没有把固定 fixture 的交互当作玩法完成。

本合同只为 `U-GZ-ART-UI-01` 冻结既有正式表面的接入目标、数据边界、资源语义槽和验收入口。正式范围仍限于 `SettlementScene` 的关中城／悬赏，以及 `AdventureScene` 的关中野外导航与战斗 HUD；`SceneBuildSupport` 的全局默认值、StartMenu、World、册界、业务规则、存档、Combat 合同和场景外 UI 均不在范围内。

现有源资产均未物化：`assets/source/ui/guanzhong-first-bounty/` 的城市背景、UI atlas、头像裁切和 manifest，以及三个既定 Unity PNG 导入路径均不存在。`RESOURCE-MANIFEST.json` 的状态为 `prototype-assets-not-final-art`，`fontsBundled=false`；ui-kit 中只有十张 64×64、无烘焙文字的原型 SVG。因此 `U-GZ-ART-UI-01` 保持 blocked，不能把本合同或原型 SVG 当作最终资源证据。

## 2. 资源语义与不可混淆的尺寸

| 稳定语义槽 | 原型输入 | 正式 Unity 的允许用途 | 不得推导的内容 |
|---|---|---|---|
| `panel.dark` | 深底板 | 城镇、悬赏、Adventure/HUD 的可缩放底板 | 为每个窗口尺寸另画一张图或改全局主题 |
| `panel.paper` | 纸面底板 | 后续确有既有对象承载的阅读／内容区 | 新窗口管理器、全游戏面板 |
| `button.normal` / `button.hover` | 一般可用按钮状态 | 既有功能／行动按钮的正常与悬停视觉 | 以文字或状态图标替换稳定业务 ID |
| `button.selected` / `primary` | 选中与关键确认可共用底图 | 已有选中／确认意图的视觉状态 | 隐式确认、额外业务步骤 |
| 图标 | 石甲兽目标、矿料／奖励、通用状态圆点三枚上限 | 已有首流程目标、奖励、状态信息的独立图标层 | Guard／Block、额外图标库或新战斗语义 |
| 文字与头像 | 原型未烘焙文字；头像为待产出的确定性裁切 | Runtime `Text` 与独立头像控件 | 把悬赏、日志、数值或按钮名烘焙进背景 |

九宫格源像素和网页显示边框是两层数据：原型资源为 64×64，源切片四边均为 16 像素；若后续交付 2× PNG，则源图为 128×128、源切片为 32 像素。网页的 panel=8、button=5 是 `border-image` 的显示宽度，不是 Unity `Sprite.border` 的源切片值；Unity 导入、Canvas 缩放和实际九宫格渲染须在实机中独立核验。原型系统楷体／宋体／微软雅黑回退不是可批准的字体资产，现有 TMP LiberationSans 也不能被擅自宣称为最终中文字体。

## 3. 正式表面逐行映射

下表的“实施路径”都是当前字面量所有者；它们说明以后应在哪里接入，不授权本任务改写任何 C#、场景或合同。`原型限定` 表示 v6 有展示夹具而正式数据链没有对应字段，U 卡不得借此新增字段或交互。

| v6 稳定 key／语义 | 场景对象与 creator | 刷新 writer／事件链 | 当前数据合同 | 资源语义与实施路径 | 原型限定与验收 |
|---|---|---|---|---|---|
| `settlement_guanzhong_city`、地区与返回信息 | `SettlementNameText`、`SettlementDetailText`、`SettlementStatusText`；`SettlementSceneBuilder.Build` 创建 `SettlementPanel` | `SettlementController.RefreshSettlementUi` → `SettlementView.ShowSettlement` | `SettlementData.displayNameKey`、`regionId`、返回 world node | 城市背景位于世界／背景层；既有 `SettlementPanel` 使用 `panel.dark`，文本继续独立。实施：`src/Assets/Scripts/Editor/SettlementSceneBuilder.cs`、`.../Settlement/SettlementView.cs` | EditMode 的 `ProductionSceneTextsRemainChineseAfterRebuild` 必须仍显示中文；不得在背景中烘焙这些文本。 |
| `bounty_board` 功能入口 | `SettlementFeature_bounty_board`，由 `SettlementSceneBuilder.Build` 创建 | `SettlementController.BindFeature` → `SettlementView.BindFeature` → `DispatchFeature` | 单个 `SettlementFeatureData` 的 `displayNameKey`、`availability`、`disabledReasonKey`；只允许既有 enabled 的悬赏入口 | `button.normal`／disabled 状态与独立标签；不得把坊市、客栈、情报画成可用入口。实施：`SettlementView.cs` | `BountyBoardViewTests.UnknownAndDisabledFeaturesDoNotOpenBoard` 是保留门。 |
| `adventure_guanzhong_wild` 入口 | `SettlementAdventure_guanzhong_wild`，由 `SettlementSceneBuilder.Build` 创建 | `SettlementController.BindAdventure` → `SettlementView.BindAdventure` → `EnterAdventure` | 唯一 `adventureEntranceIds[0]`；文字经 `UiText.ResolveId("adventure_", adventureId)` | 既有按钮的 normal／disabled 皮肤与独立文字；实施：`SettlementView.cs` | 不新增 Adventure id、地图规则或跳转。 |
| `bounty_guanzhong_shijiahou` 的列表、接取、领取、关闭 | `BountyBoardPanel`、`BountyTitle`、`BountyEntries`、`BountyResult`、`AcceptBountyButton`、`ClaimBountyButton`、`CloseBountyButton`；`SettlementSceneBuilder.BuildBountyPanel` 创建 | `SettlementView.OpenBountyBoard` → `BountyBoardView.Show`／`Refresh`；按钮监听到 `SubmitAccept`／`SubmitClaim`，两者请求后再次 `Refresh` | `ContentCatalogData.GetBountiesByIssuer`、`BountyData`、`BountyUseCase.GetState/Accept/Claim`、`BountyStatus`；失败只经 `UiText.ReasonDisplay` 显示，稳定原因保留 | `panel.dark` + `button.*` + 独立奖励／目标／状态图标；实施：`SettlementSceneBuilder.cs`、`SettlementView.cs`、`BountyBoardView.cs` | `BountyBoardView` 不拥有规则、默认条目、目标、奖励或成功日志；接取／领取／重复领取的 EditMode 用例必须保持。 |
| 城镇背景与默认男主头像 | 关中城既有世界／Canvas 表面，最终对象由上述 Builder 创建的层级承载 | 无现有资源加载器或正式 Sprite writer；只能在以后已证明的 surface-local 引用点接线 | 资源合同固定为 1 张背景、1 张 atlas、1 张从批准透明母版确定性裁切的头像、1 个 manifest | source：`assets/source/ui/guanzhong-first-bounty/{guanzhong_city_background.png,guanzhong_ui_atlas.png,formal_player_default_portrait.png,manifest.json}`；Unity：`src/Assets/Art/UI/Guanzhong/{Guanzhong_City_Background.png,Guanzhong_UI_Atlas.png,FormalPlayer_Default_Portrait.png}` | 三个路径当前不存在。不得以 ui-kit SVG、组件头像或替代素材接线；确定引用点前不新增 loader。 |
| Adventure 地图标题、状态与动态节点 | `AdventurePanel`、`AdventureText`、`AdventureStatus`、`AdventureNodeContainer`，由 `AdventureSceneBuilder.Build` 创建 | `AdventureController.Start`／失败路径及 `AdventureInputController.SelectNode` 调用 `AdventureHudPresenter.Present`；该方法只在容器为空时创建 `AdventureNode_<nodeId>`／`Label` 与按钮 | `AdventureSession.Map.displayNameKey`、`session.Status`、`failureReason`、`AdventureNodeData.nodeId/q/r` | `panel.dark` 与既有节点按钮的主题层；实施：`AdventureSceneBuilder.cs`、`AdventureHudPresenter.cs` | `AdventureHudPresenter` 不拥有 Combat HUD，PlayMode `AdventureHudShowsMapWithoutOwningCombatHud` 必须保持；不新增节点／地图字段。 |
| Combat HUD：轮次、玩家／敌人生命灵力 | `CombatHudRoot`、`CombatTurnText`、`CombatPlayerText`、`CombatEnemyText`，由 `AdventureSceneBuilder.Build` 创建 | `EncounterCoordinator.Present` 构造快照 → `CombatHudPresenter.Present` → `CombatHudView.Present` | `CombatHudSnapshot` 仅含 Player、Enemy、`TurnText`、`AcceptsCommands`、Art／Divine profile id 列表；单位仅含 id、显示名、HP／灵力当前和上限 | `panel.dark`、独立文字与头像／状态槽。实施：`AdventureSceneBuilder.cs`、`CombatHudPresenter.cs`、`CombatHudView.cs` | 只能表现两名当前单位的已有快照；未知目标、状态列表、额外单位详情都不是此合同的数据。 |
| Combat 行动栏与日志 | `CombatActionBar`、`BasicAttackButton`、`ArtButton`、`DivineButton`、`GuardButton`、`WaitButton`、`CombatLogText`，由 `AdventureSceneBuilder.Build` 创建 | Installer 配置 `CombatCommandInput`；`CombatHudPresenter.Present` → `CombatCommandInput.SetContext` → `CombatActionBarView.Present`；`EncounterCoordinator` 经 `ICombatPresentationSink.ClearLog/AppendLog` 更新 `CombatLogView` | 已有 `ICombatCommandHandler` 的普攻／术法／神通／防御／待机；profile id、`AcceptsCommands` 和日志消息 | 现有行动按钮可套 `button.*`，日志保留可编辑文本。实施：`CombatActionBarView.cs`、`CombatCommandInput.cs`、`CombatLogView.cs` | 不增指令、确认步骤或日志翻译层；`CombatLogPreservesCustomNamesAndTechnicalLogsVerbatim` 保留。`Guard` 是既有命令，不能因 v6 限制而新增 Guard／Block 图标或语义。 |
| v6 76 宽行动轴、8／12 人、当前行动／查看目标／自身下次位置分离 | 仅 `ui-visual-v6/app.js::renderAxis` 的 fixture DOM | `renderAxis`／`pick` 的 web fixture | fixture `units`、CT、`activeOrder`、`nextState` | 无当前正式 Scene object、snapshot 字段或 writer | 原型专用。`CombatHudSnapshot` 不含行动轴、CT、选择目标或预测位置；若正式化需要新增 Combat／GameplayContracts 字段或交互，停止并另行定卡。 |
| v6 目标详情、未知信息、状态／已知能力、行动预览、物品／移动／确认 | 仅 `app.js::renderDetail`、`renderPreview`、`chooseAction`、`confirm` 的固定 web fixture | web `pick`、`chooseAction`、`confirm` | fixture 中的固定效果、未知值和临时状态 | 无当前正式 C# 接入点 | 原型专用。不得把固定伤害、物品数、移动结果、未知属性或确认行为写成运行时字段。 |
| v6 fullscreen／large／small 通用窗口与换肤 | 仅 `ui-kit/templates.js::{panelContent,buttonTemplate,listTemplate,windowTemplate,applyButtonSkin}` | 浏览器模板函数；无 Unity Prefab 或 window manager | `key/title/subtitle/items/disabled/hint` 是网页样板数据 | 可作为未来结构与资源语义参考，不建立第二套 Unity UI 框架 | 原型专用。正式 U 卡只改已证明的既有对象，不创建通用窗口／资源加载器。 |

## 4. 已证明的正式事件与数据闭环

1. 城镇刷新仅经 `SettlementController.RefreshSettlementUi` 写入城镇标题、功能入口和副本入口；`DispatchFeature` 只在 dispatcher 返回 `BountyBoardEntryOpenedReason` 时打开悬赏板。
2. `BountyBoardView.Refresh` 从当前据点的 `ContentCatalogData.GetBountiesByIssuer` 枚举，并在每次接取／领取后重新读取 `BountyUseCase.GetState`。战斗胜利的悬赏进度由 `AdventureController.ResolveEncounter` 在 Victory 时调用 `BountyUseCase.RecordDefeat`；它不是 HUD 的悬赏追踪 writer。
3. `AdventureHudPresenter.Present` 只创建导航节点按钮，不创建或拥有 Combat HUD；Adventure 的 HUD 和 Combat 表面由 `AdventureSceneInstaller` 分别序列化并在 Awake 中接线。
4. `EncounterCoordinator.Present` 是 Combat HUD 的快照生产者，`CombatHudPresenter` 是 `ICombatPresentationSink` 的唯一现有 UI 适配器，`CombatHudView`／`CombatActionBarView`／`CombatLogView` 是其各自的显示 writer。`CombatPresentationContracts.cs` 仍为只读边界，U 卡不得新增字段。

## 5. U-GZ-ART-UI-01 的冻结实施合同

U 卡可在其具名前置正式进入 master 后，且仅在下列全部真实条件满足时重新判断 ready：

- 1 张城市背景、1 张 UI atlas、1 张确定性头像裁切及 manifest 已按第 3 节的 source／Unity 路径实际交付；每个 Unity 导入物和新目录的 `.meta` 证据齐全。
- 最终字体的授权、字符覆盖和中文回退已获实际证据；不得以 `fontsBundled=false` 的原型或当前系统回退替代。
- 实施只在第 3 节列出的 `SettlementSceneBuilder`、`SettlementView`、`BountyBoardView`、`AdventureSceneBuilder`、`AdventureHudPresenter`、Combat HUD／action／log 视图和两个正式场景的既有对象上完成。引用点、Sprite 切片、Canvas 缩放及动态 writer 必须在写前再用当时 src／场景证明。
- 不需要增加 `GameplayContracts`／Combat／World／存档字段、悬赏规则、行动指令、全局主题、图标库或采购。发现任一项即排除该原型行，并按其真实所有者另行处理。

实施后最低验收为：1920×1080 正式入口从关中城打开／关闭悬赏、接取、进入野外、战斗、返回、领取；文本与按钮保持独立、动态 Adventure 节点和 Combat action／log 同主题可读、HUD 不遮战场，且一次性奖励与现有保存读取未回归。原型截图、静态网页检查或单个资源导入都不能代替连续实机证据。

## 6. 后续验证入口与停止条件

- 文字／重建：`src/Assets/Tests/EditMode/GuanzhongFormalUiTextTests.cs` 的 `ProductionEntitiesResolveToApprovedChineseNames`、`CombatLogPreservesCustomNamesAndTechnicalLogsVerbatim`、`ProductionSceneTextsRemainChineseAfterRebuild`。
- 城镇／悬赏：`src/Assets/Tests/EditMode/BountyBoardViewTests.cs` 的 `ProductionGuanzhongBountyOpensBoardAndAcceptsFromBoard`、`ObjectiveCompletedBountyCanBeClaimedFromBoard`、`ClaimedBountyCannotBeClaimedAgainFromBoard` 与禁用入口用例。
- Adventure 表面隔离：`src/Assets/Tests/PlayMode/GuanzhongFormalUiTextPlayModeTests.cs::AdventureHudShowsMapWithoutOwningCombatHud`。
- 首流程业务回归：`src/Assets/Tests/EditMode/GuanzhongFormalEndToEndTests.cs::FormalFeatureChainConsumesVictoryOnlyOnceBeforeClaimAndRestore`。
- 实施时按影响面运行 Unity EditMode／PlayMode、数据链、任务卡／队列和 whitespace 检查；本合同本身只做文本和结构化管理验证，不运行 Unity 或 BattleSim。

停止而非加补丁的条件：最终资源或字体未获证据；任何一行要求新业务字段／交互、全局 `SceneBuildSupport` 改动、StartMenu／World／册界改造、第二 UI 框架或资源加载器、额外图标库或未经批准费用；或无法证明两张正式场景的 creator、刷新 writer 和资源引用点。
