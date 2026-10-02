# 关中正式战斗 UI 运行合同

2026-10-02由 D-GZ-PLAYTEST-UI-AUDIT-01 冻结。实测与原始证据见 `开发管理/关中战斗UI逐项排查记录.txt`。本合同是两个直接实施卡的输入，不是功能已完成声明。

## 1. 授权与单一所有者

2026-10-01用户反馈已明确要求行动轴、CT 和头顶血条；9月20日旧合同中把行动轴/移动排为 web fixture 的段落不能否定本次实现。保留 v6 的视觉语义，不把8/12单位 fixture、预计位置或静态截图当正式调度证据。

正式装配仍由 `AdventureSceneBuilder` 保存 `AdventureScene.unity`，由 `AdventureSceneInstaller` 注入已有组件，`EncounterCoordinator` 接唯一 CombatSession，`CombatHudPresenter` 消费只读 DTO。不另建 Canvas、会话、计时器、全局 UI 服务或配置存储。只覆盖本次正式玩家/石甲兽两个单位；不以本卡扩展通用多人战斗。

已导入资源：`src/Assets/Art/UI/Guanzhong/Guanzhong_UI_Atlas.png` 中的 Guanzhong_Panel_Dark、Guanzhong_Panel_Paper、Guanzhong_Button_Normal/Hover/Selected，以及 `Fonts/GuanzhongChinese.otf`、`FormalPlayer_Default_Portrait.png`。现有 atlas 只有上述五片，不能假称有专用兽头像/血条切片。数值条与阵营图形可用普通 UGUI Image/文字结合既有面板表达；不生成新原画，不修改 atlas/font/model/importer。新增脚本及.meta由各卡精确清单限定，无新资源目录或Prefab。

## 2. TIMELINE：真实状态的只读投影

### 数据与职责

- `CTBEngine` 在本文件内定义并提供不可变的只读调度快照：CurrentTick；每单位稳定 Id、Speed、ChargeTime、NextActionThreshold、IsReady；尚待分发的 actionQueue 原序。读取不得 Advance、Dequeue、Consume、Wait 或改变任何引擎状态；集合必须是副本，UI 不持有内部 UnitState。
- `CombatTurnScheduler` 在本文件内包装该快照，使用同一 CombatSession.Combatants 过滤死亡单位。Combat 层类型不依赖 Unity/UI/GameplayContracts；不为几个 DTO 新建程序集或服务。
- 当前行动者使用 `EncounterCoordinator.RunCombat` 的真实 advance.ActorId（移动规则交付后以该规则确定的会话行动阶段/当前行动者为准）；不能用 IsReady 推断“当前”，因为同 tick 可有多个 ready；不能解析 TurnText。
- `EncounterCoordinator` 构造 `CombatPresentationContracts.cs` 中不可变的 CombatTimelineSnapshot/CombatTimelineEntrySnapshot，并将其附入 CombatHudSnapshot。字段包括 CurrentTick、CurrentActorId、单位 Id、DisplayName、阵营、Speed、CurrentCt、NextActionThreshold、是否已 ready、队列位置/当前标记。阵营和名称由角色/现有内容投影得到，不让 view 访问会话或猜 id 前缀。所有单位用稳定 ID 关联，不靠数组固定下标辨认当前角色。
- 顺序分为“当前行动者 → 引擎已排队的 ready 项原序 → 尚在蓄力项”。待蓄力顺序由 CTB 层只读投影给出：离门槛所需整数 tick 更少者在前，同到达 tick 时用现引擎同一比较语义（速度、到达时 CT）。共用比较逻辑，不在 UI 复制一套排序；没有新业务 tie-break、浮点秒数或猜测之后会用哪种术法。不得将尚未发生的排序称作已排定队列。
- 当前单位后续 CT 门槛会随实际命令改变。本卡不制造 v6 fixture 的“自身预计下次位置”，也不扩建命令预览系统；可以显示其现有 CT，但不得在未知后续动作时显示确定的第二头像/预计名次。该项与查看目标联动保留为明确未实现的整屏交互边界，交规划。

### 显示与刷新

- `CombatHudPresenter` 将快照交给新 `CombatTimelineView`。view 只负责条目与焦点文本，不能调度、消费命令、改变当前行动者或设置命令 target。通过既有 Presenter/Installer 配置轴组件；避免破坏已有 Configure 调用者，新增独立的轴配置入口即可，无新兼容状态。
- 使用 v6 1920×1080 参考坐标：左52、顶279、轨宽76；普通条目52×46、当前66×66、间距5，轨道最大高605。默认常驻身份图形、序号及当前标记；悬停/键盘焦点显示中文身份、实际 CT/当前门槛，当前项也必须能查看。玩家可用已导入头像，敌方用可区分的阵营图形加名称；颜色不是唯一身份线索。
- 原 AdventurePanel 占同一左侧区域：仅在正式战斗 HUD 显示时隐藏该探索面板、Hide/退出时恢复，作为轴的直接布局依赖接入同一 Presenter，不修改 AdventureController 的返回/撤退规则。该视觉隐藏不等于修复审计记录中的返回状态门禁。
- 世界选中目标、正在行动者、键盘/鼠标查看焦点是不同语义。轴焦点只查看，不伪装为已实现世界目标选择。保留固定1v1命令目标，不自行新增战术规则。
- 首次 advance、成功命令/等待、下一次 advance、死亡、结束均以权威状态重建快照。MoveThenAction/ActionThenMove 内的 Move 不切走当前项、不消费 CT；行动后仍有移动阶段时也不提前显示下一单位，遵循移动合同。拒绝命令不推动轴。隐藏/离场清空，无旧战斗 ID、焦点或条目残留。
- 完成证据必须记录真实正式会话的 advance.ActorId、ticksElapsed/CurrentTick 和同次只读 CT/门槛/队列，与 UI 项对应。日志可只由定向测试采集；不得加入每帧生产日志。当前审计只有动作日志，不能代替这个数值验收。

### 冻结的精确改动面

沿用 U-GZ-PLAYTEST-TIMELINE-01.expectedPaths 全部字面量路径：CTBEngine、CombatTurnScheduler、CombatPresentationContracts、EncounterCoordinator、CombatHudPresenter、**新** CombatTimelineView、AdventureSceneInstaller、AdventureSceneBuilder、AdventureScene.unity、**新** GuanzhongCombatTimelinePlayModeTests，及已列.meta/本卡管理路径。纯调度快照类型放在已有两个 Combat 文件内；不新增其他脚本、依赖或资产。若移动规则交付改变这些入口，准备方先据真实结果核对原子性与预检再 ready。

### 最小充分验收

1. 不同速度（如100/50）及实际门槛（100/130）样本；同 tick 多 ready 保留队列原序，读取快照前后引擎状态与下一真实 Advance 结果不变。更快但已排在现队列后的新候选不能插队。对待蓄力投影使用独立会话后续真实 Advance 校验，不能让测试重抄 UI 排序公式。
2. Wait保留当前CT的50%，行动惩罚抬门槛；与移动规则交付后的 Move/行动/EndTurn 阶段及拒绝命令一致。当前项只能有一个；死亡待分发单位被跳过；关闭后再次进入没有残留。
3. 在现有 PlayMode 新文件中覆盖规则投影和正式场景装配，不为文档新增测试框架。正式入口 Game View 实测1920×1080和1366×768，截图可读 CT/门槛、当前标记，记录轴/探索面板实际 RectTransform 与遮挡情况；战场右侧整体布局问题仍按审计记录跟踪。

## 3. HP：稳定 ID、真实头部锚点与资源

### 数据与职责

- 复用现有 `CombatantHudSnapshot` 的 Id、CurrentHealth/MaximumHealth、CurrentSpirit/MaximumSpirit；由 `CombatHudPresenter.Present` 分别传给新 `CombatUnitHealthBarView`。不改 CombatSession、生命、治疗或费用规则，不因只有两名正式单位就按屏幕左右位置绑定。
- 每个稳定 ID 对应一个条实例，正式场景最多两个。Builder 创建同一 UICanvas 下专用覆盖容器/模板并保存场景，Installer 显式注入 Presenter、health view、provider、实际战场 Camera/Canvas。新增独立配置入口即可，不破坏既有 Presenter.Configure 调用者，不使用 FindObjectOfType/Camera.main/第二 Canvas 作为隐式兜底。
- `Static3DCombatUnitPresentationProvider` 是棋子Root/Body位置与生命周期唯一所有者；在该文件提供按稳定ID查询头部世界锚点的只读入口。锚点取实际 body Renderer 合并 bounds 的顶部中心，排除底座、地块、反馈粒子；缓存 body renderer 集合，按当前实例姿态/缩放取世界坐标，不在 view 猜 q/r→world 或写第二份模型尺寸。body 的1.4层和root临时动作倍率都必须计入。
- 条的底边中心投影到该顶部锚点，文字/条向上展开。像素间隙只是局部视图留白，先记录实际 bounds、world→screen、Canvas局部点再定值，不增加模型scale或多个世界offset补偿层。

### 显示与生命周期

- 每条显示可区分的身份、HP当前/上限、HP比例条，以及灵力当前/上限。使用数值加填充使低血可读，不新增会影响规则的“低血状态”；不需要闪烁状态机或新门槛数据。生命为0时随死亡隐藏/移除，无负值残留；上限异常视为输入问题并报告，不能默认填满掩盖。
- Presenter 收到快照时更新资源；LateUpdate 只将 provider 的当前世界锚点投影到已注入 Canvas，因此相机平移、人物移动/转向/受击缩放不需要再发送假资源更新。位置刷新与数值写入各有一个所有者。
- 相机后方或 viewport 外的锚点隐藏，不能钳在屏边冒充屏内位置；场景内遮挡采用屏幕覆盖式可读血条，不做新物理射线遮挡系统。头部投影在视口内而模型被地物遮挡时仍显示，这一表现选择不改变战斗可见性规则。
- 条/容器的Graphic关闭raycastTarget，不能抢移动/世界输入。Provider无该ID或root已inactive时隐藏；Hide、OnDisable、场景退出清空，重进按新快照重建，不保留失效引用。

### 冻结的精确改动面

沿用 U-GZ-PLAYTEST-HP-01.expectedPaths：**新** CombatUnitHealthBarView、Static3DCombatUnitPresentationProvider、CombatHudPresenter、AdventureSceneInstaller、AdventureSceneBuilder、AdventureScene.unity、**新** GuanzhongUnitHealthBarPlayModeTests，及已列.meta/本卡管理路径。现有资源DTO已足够，本卡无需改 GameplayContracts/EncounterCoordinator、模型/Prefab或新增其他资源路径。

### 最小充分验收

1. 两不同稳定ID分别伤害/治疗后，条的数值/填充只更新对应单位；与正式角色数值来源一致，不用 view 私有测试数值宣称正式接线完成。零血死亡、Hide/重入各无残留。
2. body=1.4、root的攻击/受击瞬时倍率及复位、六向与移动后的锚点来自实际renderer bounds；记录body/root尺度、bounds顶部、screen点、Canvas局部点和最终条Rect，保证条底边与锚点对应，不因屏幕缩放重复乘倍率。
3. 用定向 PlayMode 驱动现有相机/棋子位置验证远近、平移、屏外、后方及遮挡策略；不要求尚未完成 CAMERA 卡的用户输入。正式入口1920×1080和1366×768 Game View同时见两条、真实伤害刷新；不侵占动作按钮，不拦截世界点击。
4. 只运行该改动的编译、指定 PlayMode、新.meta及数据链/程序集直接检查。没有规则数值修改，不运行 BattleSim。若为获得锚点必须改模型/导出/全局规则，停止回报路径缺口。

## 4. 状态与剩余边界

- TIMELINE 已准备，仍 blockedBy U-GZ-PLAYTEST-MOVE-RULE-01；前置交付后核对当前行动阶段入口、重新预检才可 ready。
- HP 已消费 SCALE完成归档及本审计；路径、输入、视图与验收已冻结，可在本审计完成事件中转ready。其实现仍须在独立任务 worktree 中正式取证。
- 两术法、移动、相机按现有计划卡推进；整屏布局、通用本地化、完整目标查看联动及战斗返回门禁按审计记录交规划。本合同不偷偷把它们变成这两个叶子的扩展范围。
- 最终连续流程由 V-GZ-PLAYTEST-FLOW-01，最终用户实玩接受由 V-GZ-ART-SLICE-01；本合同不代签。
