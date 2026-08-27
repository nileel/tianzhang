# 2D 战斗动画纯帧／整体动效切换设计

状态：2026-08-27 已按用户批准的推荐方案实施并通过 Unity EditMode 428/428、PlayMode 14/14 验证。

## 一、问题与目标

现有 `BattleAnimationSpritePresentationController.Tick()` 在推进三帧 Sprite 的同时，无条件调用 `ApplyRootPresentation()`。因此 2D 动态样例同时包含两种视觉来源：atlas 帧变化，以及表现控制器施加的根节点位移／旋转。用户需要在同一个可玩比较入口中分别观察这两种结果。

本次只为 `AdventureScene/VisualBaselineBoard` 的隔离 2D 比较路线增加两个明确选项：

- `纯帧动画`：默认。正常推进 Sprite 帧和表现事件，但不施加 `ApplyRootPresentation()` 的根节点位移／旋转。
- `整体动效`：保留当前全部 2D 行为，即帧动画与 `ApplyRootPresentation()` 同时播放。

切换模式时立即复位当前动作和根节点，防止上一模式的中间姿态残留。静态 3D、旧静态 2D 样张、正式单位、战斗规则、方向、格位、寻路、伤害、结算和存档均不改变。

## 二、方案选择

采用两个显式按钮，而不是单按钮循环切换或拆成两条 2D 路线：

1. **两个显式按钮（采用）**：当前意图直接可见，沿用现有比较面板的按钮模式，不把表现模式伪装成新的资产路线。
2. **单按钮循环切换（不采用）**：控件较少，但按钮本身不能稳定表达当前模式，容易在截图和重复测试中误判。
3. **`2D 纯帧`／`2D 整体` 两条路线（不采用）**：会把同一套 atlas 的播放选项错误扩张成两条路线，并重复现有 route 切换逻辑。

## 三、运行时所有者与数据流

运行时链固定为：

`AdventureScene/BattleVisualComparisonPanel/Comparison2DMotionButtons`
→ `Comparison2DPureFramesButton`／`Comparison2DOverallMotionButton`
→ 持久化 `Button.onClick`
→ `VisualBaselineBoard.BattleVisualComparisonController`
→ 六个 `BattleAnimationSpriteProbe_<0..5>` 上的 `BattleAnimationSpritePresentationController`
→ `Tick()` 推进帧，并按模式跳过或执行 `ApplyRootPresentation()`
→ `FuYuan_BattleAnimationSprite/SpriteBody` 的活动 Sprite 与可选根节点动效。

`AdventureSceneBuilder.BuildBattleVisualComparisonPanel()` 是按钮、标签和持久监听器的唯一创建者；`BattleVisualComparisonController.UpdateStatus()` 是状态文字的唯一运行时刷新者。现有 `SceneBuildSupport.CreateButton()` 只作为已证明的创建工具使用，不改变其共享行为。

`BattleVisualComparisonController` 继续拥有比较会话的选择状态，并新增默认值为 `false` 的 `is2DOverallMotionEnabled` 运行时状态及只读属性 `Is2DOverallMotionEnabled`。它以 `SelectPureFrame2DMode()`、`SelectOverall2DMotionMode()` 接收两个按钮事件，并向六个 2D probe 同步状态。该状态不进入存档，也不成为正式战斗配置。

`BattleAnimationSpritePresentationController` 只新增 `SetRootPresentationEnabled(bool)` 和只读属性 `RootPresentationEnabled`；它不读取 UI，也不拥有路线选择。`Awake()` 仍完成帧校验和 rest pose 捕获；比较控制器在自身 `Awake()` 校验全部 probe 后，先把六个 2D 控制器显式设为 `false`，再保持现有静态 3D 默认路线。缺少任一 probe、SpriteRenderer 或批准帧时继续按现有合同抛出明确异常，不增加静默 fallback。

## 四、状态行为

### 4.1 默认与持久期

- 场景进入 Play Mode 后，静态 3D 仍是默认路线。
- 本次 Play 会话内的 2D 模式默认是 `纯帧动画`。
- 在同一次 Play 会话中切换到静态 3D 再切回 2D，保留用户最近选择的 2D 模式。
- 退出 Play Mode 后不持久化模式；下次进入仍回到 `纯帧动画`。

### 4.2 模式切换

- 点击任一 2D 模式按钮时，先对静态 3D 与六个 2D probe 执行现有复位，再写入新模式。
- 复位后全部 2D probe 回到 idle 第 0 帧、原始位置与原始旋转。
- 重复点击当前模式允许执行同一幂等复位，不新增 toggle 状态或兼容分支。

### 4.3 纯帧动画

- `idle`、`move`、`attack`、`hit`、`cast`、`death` 仍按现有三帧时序播放。
- 事件帧、`CastEffectRequested` 单次信号、播放结束与 idle 复位保持不变。
- `Tick()` 不调用 `ApplyRootPresentation()`；根节点从动作开始到结束保持 `restPosition/restRotation`。
- `approvedWorldPosition` 仍由现有比较控制器传入，但在纯帧模式下不消费，不添加第二条位置算法或 fallback。

### 4.4 整体动效

- 完整保留现有 `ApplyRootPresentation()`：move 的前进／竖直弧线、attack 的前冲／俯仰、hit 的横向震动、cast 的上浮、death 的下沉／旋转。
- 动作结束仍由现有 `RestoreRoot()` 恢复原始位置、旋转和 idle 帧。

### 4.5 静态 3D

- 两个 2D 模式按钮只更新 2D probe；不改变 `StaticChessPresentationController` 的动效。
- 路线互斥、六方向和六事件按钮行为保持现状。

## 五、UI 与可见文字

`AdventureSceneBuilder.BuildBattleVisualComparisonPanel()` 在路线按钮下增加一行两列网格：

- 容器：`Comparison2DMotionButtons`
- 左按钮：`Comparison2DPureFramesButton`，可见文字 `纯帧动画`，唯一持久监听器为 `BattleVisualComparisonController.SelectPureFrame2DMode()`
- 右按钮：`Comparison2DOverallMotionButton`，可见文字 `整体动效`，唯一持久监听器为 `BattleVisualComparisonController.SelectOverall2DMotionMode()`

面板仍位于左下角，横向范围保持 `0.02..0.38`；纵向上边界由 `0.42` 调整为 `0.48`，为新增 40 px 按钮行和既有 5 px 网格间距留出空间，不覆盖 Game 视图中央比较对象。按钮继续使用现有 `112×40` cell 和既有样式，不增加新图片、字体、颜色或共享 UI helper 行为。

状态文字新增独立一行：

`2D 模式：纯帧动画` 或 `2D 模式：整体动效`

该行在静态 3D 路线也保留，用于说明再次切换到 2D 时将采用的模式。路线、方向、事件和“仅供用户实机比较”文字继续由同一个 `UpdateStatus()` 刷新。

## 六、持久化与允许路径

场景对象继续由 `AdventureSceneBuilder` 确定性重建并保存到 `src/Assets/Scenes/AdventureScene.unity`。两个新按钮必须各有且只有一个持久监听器，目标均为同一个 `BattleVisualComparisonController`；不手工维护场景 YAML。

实施允许修改：

- `src/Assets/Scripts/Modules/Features/CombatPresentation/BattleAnimationSpritePresentationController.cs`
- `src/Assets/Scripts/Modules/Features/CombatPresentation/BattleVisualComparisonController.cs`
- `src/Assets/Scripts/Editor/AdventureSceneBuilder.cs`
- `src/Assets/Scenes/AdventureScene.unity`（只由 Builder 保存）
- `src/Assets/Tests/EditMode/VisualBaselineEditorTests.cs`
- `src/Assets/Tests/PlayMode/BattleVisualComparisonPlayModeTests.cs`
- 必要时扩充现有 `BattleAnimationSpritePresentationPlayModeTests.cs`
- `开发管理/战场角色视觉方向可玩比较操作说明.txt`
- `开发管理/战场角色2D与静态3D可玩比较验证记录.txt`

明确不修改：六份 2D atlas 及 `.meta`、pilot manifest／母源、`FuYuan_BattleAnimationSprite.prefab`、静态 3D Prefab／材质／控制器、旧 `TacticalSpritePresentationController`、Bootstrap、正式 Adventure／Combat 规则和任何存档数据。

## 七、验证与完成条件

### 7.1 EditMode

- `AdventureScene` 恰有一个 `Comparison2DMotionButtons`。
- 两个按钮对象、可见文字和唯一持久监听器分别指向批准的方法。
- 比较控制器、路线／方向／事件／复位按钮和静态 3D 默认路线保持完整。
- 场景保存后重新打开，按钮与监听器不丢失。

### 7.2 PlayMode

- 进入场景后点击 `2D 动态`，默认模式为 `纯帧动画`。
- 纯帧模式下播放非 idle 事件：活动帧按 0→1→2 推进，根节点位置／旋转在每个采样点都等于 rest pose，cast 仍只发出一次信号，结束回到 idle。
- 切换 `整体动效` 后播放同一事件：活动帧仍一致，至少一个批准采样点的根节点位移或旋转与 rest pose 不同，结束后精确复位。
- 动作播放中切换模式会立即停止动作、恢复 idle 第 0 帧和 rest pose。
- 切换到静态 3D 再返回 2D，最近的 2D 模式保持；静态 3D 动作结果不受两个 2D 模式按钮影响。
- 状态文字精确显示当前 2D 模式。

### 7.3 项目门禁

- `dotnet build src/TianZhang.EditModeTests.csproj`
- `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/run-unity-editmode-tests.ps1`
- `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/run-unity-playmode-tests.ps1`
- `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-data-chain.ps1 -FailOnMissingAssets`
- `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-unity-assembly-boundaries.ps1`
- 预期路径的 whitespace 与 `git diff --check`

完成必须同时满足：两个模式可由用户明确选择；默认纯帧；模式切换无位置残留；纯帧与整体动效的根节点数值证据可区分；现有帧、事件、cast 信号、复位、3D 路线和规则隔离回归全部通过。任何实现若需要修改 atlas、Prefab 根位置、正式战斗移动或增加第二套动画控制器，应停止并回到本设计复核。
