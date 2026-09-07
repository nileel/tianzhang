# Sleeve Range 06：单候选活动范围修正

## 实际结果：明显改善，但整体验收未通过

已完成唯一候选的一次 72 秒／六方向／四类动作真实 Unity Player 运行。**经历抬放臂后，袖腹可以落回袖臂一侧，转身和急停后的持续围腰明显改善；开场包腰与快速动作中的尖折／卷束仍未解决。** 不将它标为正式成功，不进入双袖／袍摆。

- [左 05／右 06 的 12 秒同帧对照](unity-range-06-comparison.gif)：完整抬放、挥臂、转身、移动、急停和恢复，开场失败也保留。
- [本次近景／碰撞代理／战棋尺度三视图](unity-range-06-yaw150.gif)、[完整 72 秒六方向实录](unity-range-06-full.gif)、[六方向关键帧索引](six-direction-contact-sheet.png)。仅由实际 Unity PNG 编码，非 AI 视频。
- [直接运行时输入证明](runtime-input-proof.json)、[864 帧与 4320 步记录](runtime-report.json)、[原始 t12／t15 人体与袖子快照](runtime-body-samples.json)、[14 项测试结果](editmode-results.xml)。原始 97,734,150 bytes JSON 和全部 PNG 保留在本机 capture 目录，压缩提交记录只去掉大体积 skinning 快照，保留全部帧、动作与 steps。

| 动作／检查 | 判定 | 实际观察与限制 |
| --- | --- | --- |
| 开场放臂静置 | 失败 | 六方向初始段仍横向围腰、出现硬折，未在静置阶段自行解开。 |
| 抬臂和放臂 | 回落子项改善；整组未通过 | 抬臂将袖腹带开后能向侧下方回落；抬起／下降过程仍有尖折、卷束与局部重叠感。不能宣布整个动作自然、无自交。 |
| 明显挥臂 | 跟随有效；自然度未通过 | 布面真实响应并在动作后回落，但近景快速动作折叠仍不自然。正常瞬时布褶与异常自交尚未逐面分离。 |
| 身体转向 | 持续围腰检查通过；衣料总体验收未通过 | 代表帧与临近帧中维持侧下垂，没有回到 05 的横撑腰背。其他近景缺陷仍在。 |
| 移动后突然停步 | 停后侧下垂／回落子项通过 | 速度输入仍为 1.25 m/s 后突然归零。各方向末段保持侧下垂；例如 yaw150 frame0284 已停约 2.67 秒，05 同帧仍横向围腰。未宣称全段完全无微抖或穿模。 |
| 袖根／穿插／拉尖／翻卷 | 未全面通过 | 附着未明显脱离，29 固定点、根带不改，但根部及快速动作仍有硬折／尖角。旧粒子坐标／索引校准不足，不用旧穿深统计声称无穿模。 |
| 战棋尺度与六方向 | 大轮廓改善；正式美术未通过 | 三相机同帧，固定俯角 38°、正交 6.2、六方向 90/150/210/270/330/30 全保留。侧下垂收益可见；战棋视图较小不能掩盖近景缺陷。 |

主线检查全方向索引及 90/150/30 代表实际帧，独立只读复核检查 210/270/330 的四类动作、同帧旧版与临近帧。抽帧判断不等于全部三角面无穿插的逐帧证明；完整动态文件留给用户查看。

## 已证实原因与仍未知部分

运行中实际提高 **249／456 点**，原袖根带与上侧布面均 **0 改动**，保留 29 固定点。最大活动半径为 **1.470018983 m**；源最低袖口为 40，邻近下缘 36 为约 1.445449 m。独立 FBX 几何计算与实际径向值最大差约 0.155 微米；实际 456 项系数均符合公式。此前横向不可达的 42 点全部属于本次修改集合。

15 个全量快照的 CPU 蒙皮目标与 05 逐分量相同；858 个启用帧的全部三组碰撞中心／半径完全相同；4320 个动作步骤完全相同；网格、权重、绑定、刚度、自碰撞集合未改。故可以确认：**原袖腹活动范围是持续围腰的重要限制，放宽它确有可见收益，但并不是全部问题的唯一原因。**

同一方向的 trial1 与 trial11，去除根节点平移后，蒙皮目标最大分量差约 0.147 微米，碰撞中心差约 0.067 微米，朝向相同；前者仍包腰，经历动作后的后者已经侧下垂。这证明结果受前面动作历程影响，支持继续检查开场初始化，但还没有分离初始相交、回挡、tether、自碰撞及折叠状态各自作用。不能据此保证改初始化就全部成功，也不能据此宣布 Unity Cloth 无法做宽袖。

本轮旧局部坐标标志仍是 822／858；它不是所有自由粒子的世界空间／索引证明，不引用旧 `freeVerticesInsideProxy` 或 `maximumProxyDepthMeters` 作为实际人体穿透结论。

## 实际成本与停止决定

- 新增的物理逻辑为已有局部修正类中的一次性几何约束计算，控制器只增加开关、调用及记录。没有新增运行时组件／程序集／生产机制，没有 Blender 或权重返工，没有缩袖幅。
- 一次 EditMode **14／14 通过**（0.858 秒测试执行），一次 Development Player 构建成功，一次有效 72 模拟秒运行，864 PNG、4320 步、15 全量人体快照。实际墙钟 83.472 秒包含三相机、CPU 取证、截图和写盘，**不是 Cloth 性能基准或制作人时**。
- 一次实现前的只读范围复核避免了误改上侧／根带；未测试其他物理参数组。实际制作投入包括代码、针对性测试、运行输入对账与媒体整理，不虚报未完整记录的人工工时。
- Unity 已退出；仅还原本轮自动产生的渲染配置迁移、材质／FBX meta 空白、场景重建 fileID 与 ProjectSettings 变动，保留 3 份 C# 修改与本候选证据。可编辑源 `.blend` 主区／worktree SHA-256 同为 `5773BEEFEFD1C4DE02BA80B3F3B12F39BDD084ADFE600477ADEB9730CA255F65`，FBX 不变。
- 本轮停止，不叠加初始化或权重第二候选。**这条方向有继续做单袖收尾验证的价值，但尚不值得直接投入双袖／袍摆或完整角色生产。**
- 单角色没有覆盖多角色同屏、双袖互碰、袍摆、多层衣物、目标低配硬件；录制耗时不能线性外推多人性能。较大活动范围的后续稳定性也不能从一个 72 秒试验保证。

## 重新打开与播放

在 Unity 6000.3.18f1 打开仓库 `src/`，打开 `Assets/Tests/Scenes/ClothWideSleeveExperimentScene.unity`，选 `ClothWideSleeveExperimentRoot`：

1. `Isolation Mode = Retest02`。
2. 勾选 **Correct Torso Transition** 和 **Expand Sleeve Range**；保持 **Release Diagnosed Torso Pins** 关闭。
3. 进入 Play；退出后再进入可重播。开关默认关闭，原基准保留；组合不合法会明确报错。

本机已经构建的 Player 为 `D:\Temp\TianZhang-Blender\cloth-wide-sleeve-range-06-player\TianZhangClothWideSleevePilot.exe`。复录传入新输出目录：

```text
--correct-torso-transition true --expand-sleeve-range true --body-probe true --capture-dir <新的绝对目录>
```

验证入口沿用 `ClothWideSleeveExperimentSceneBuilder.BuildPlayerForBatchMode` 与 `-clothPilotBuildPath`；EditMode 使用 `-runTests -testPlatform EditMode -testFilter TianZhang.ClothWideSleevePilot.EditorTests.ClothWideSleeveExperimentEditorTests`。`verify_runtime.py` 从原始记录与安装随附 FBX 二进制解析器复算，不启动 Unity／Blender；`encode_capture.py` 只从保存的 PNG 编码并逐帧解码核验 GIF 数量／时长。媒体 SHA-256 见 `media-manifest.json`。

## 冻结设计与授权

2026-09-07，用户在只读诊断后明确要求“那就尝试修改后看看”。本轮只做一次修改候选与一次完整 72 秒实际 Unity 运行，不进入双袖／袍摆，不购买、不上传、不换 Cloth 系统。

诊断已把冻结 FBX 的顶点、Cluster 权重／绑定矩阵与保存的 Unity t0/t3 骨姿态及 CPU 蒙皮逐点对应：456 个唯一映射相同，最大误差约 0.431／0.345 微米。19 圈每圈 24 点权重完全相同，源最低袖口对应 Unity 40；源下垂 0.735 m，放臂时变为横向约 0.697 m，原最大活动半径只有 0.38 m。Cloth OFF 实录已包腰，因此不能只调碰撞或刚度。

在 Torso Transition 05 基准上只提高非固定袖腹点的 `maxDistance`：沿用原 Builder 的轴向比例 t，完整保留 t<=0.17 袖根带；只选择 t>0.17 且位于绑定手臂轴下方的点。取源顶点到绑定姿态 upperarm_l→hand_l 无穷直线的垂距 d，新值为 max(05 原值, 2d)。2d 是该半径圆上最远两点的距离，解除绕此直线换向时的单点运动范围限制；它不是弯肘多骨蒙皮、重力、碰撞、拉伸、自碰撞和 tether 共同可满足的保证，也不保证从已相交的放臂初态解开。

保持 05 的 29 个固定点、完整袖根带、上侧布面、躯干及手臂双球、碰撞回挡距离、刚度、阻尼、tether、自碰撞集合、动作、时间步、三视图和六方向不变。独立只读复核在实现前发现，若不限定根带和上下侧，2d 会改变 65 个上侧点与 39 个根带过渡点，因此在物理代码写入前收窄此掩码；未运行另一参数候选。候选不改蒙皮权重、绑定、网格或袖幅，不加入动态投射或新的服装机制。若自然宽袖表现仍失败，保留证据，不追加初始化或权重第二候选。

## 路径与所有者

- 主工作区 `D:\天章游戏开发`；本轮 worktree `D:\天章游戏开发\.worktrees\cloth-wide-sleeve-pilot`，分支 `codex/cloth-wide-sleeve-pilot`，冻结基线 `d9daa7e33b33127f1713b0c78966cc2f815b0b56`。
- 写入前 schema 5 Show：Codex run 空；DeepSeek 的 `U-COMBAT-PROJECTION-01` 在 attention_required，未占用本小样；集成锁 none。未领取队列任务，不创建／修改任务卡或 runtime。主区无关改动保留。
- 允许修改 `src/Assets/Tests/ClothWideSleevePilot/Runtime/ClothWideSleeveTorsoCorrection.cs`、同目录 `ClothWideSleevePilotController.cs`，以及 `EditorTests/ClothWideSleeveExperimentEditorTests.cs`；证据与离线分析／编码在本目录，上一层 README 只增加入口。
- 场景：`src/Assets/Tests/Scenes/ClothWideSleeveExperimentScene.unity`。仍不在 BuildSettings，不接正式角色、Adventure、战斗或存档。通过 Inspector 的候选开关重播，原基准默认不变。
- 不修改 `assets/source/characters/cloth-wide-sleeve-pilot/TZ_ClothWideSleevePilot_v001.blend`、冻结导出和 `src/Assets/Tests/ClothWideSleevePilot/Models/TZ_ClothWideSleevePilot_v001.fbx`。FBX SHA-256 `5BD14D613A68881ED5202F03CE6A494535382DBB541C5BC4310CC6A30E0367A4`。
- 临时 Player、PNG 和日志只写 `D:\Temp\TianZhang-Blender\cloth-wide-sleeve-range-06-player`、`cloth-wide-sleeve-range-06-capture`、`cloth-wide-sleeve-range-06-validation`；不覆盖既有运行。
- 运行时链：唯一场景控制器 GUID `a0a6924ec4081f943a5c82860f4b8cfc` → 已绑定 `TZ_LeftWideSleeve_Cloth` → Start 先应用 05 再应用范围修正；其后仍由原 ApplyPose 写骨姿态。代码调用者仅实验 Builder／测试，程序集 `TianZhang.ClothWideSleevePilot` 无项目依赖、autoReferenced=false。三相机均观察同一实例，不替换其他角色所有者。

## 最小验证

1. EditMode：逐点公式、29 固定点、碰撞／刚度／mesh 不变，默认场景重开与非 BuildSettings 合同。
2. 一次 Development Player 构建及一次 72 秒运行：864 录制帧、4320 步；四类动作、六方向、近景／战棋尺度全保留。
3. 运行记录直接保存绑定轴、逐点垂距和实际系数；与 05 的同帧蒙皮目标、双球和动作输入比对。旧 Cloth 粒子坐标／索引未全面校准，不用旧穿深统计宣称无穿模。
4. 真实 PNG 编码为全程与对照动图，实际观察后填结果。输入未变的检查不重复；不把编译／布料在动当验收。

采用 `tianzhang-blender-pipeline` 的源路径、工具与停止边界；本候选无需修改 Blender 场景，不重启不可用会话、不切换 headless 写入。采用 `unity-agent-workflows` 的运行所有者、数值、可见输出、序列化与最小验证门禁。`brainstorming` 用于收敛已有批准方向，本次明确实施要求优先，不重复开题审批。

References loaded：Blender tool-routing；Unity runtime-owner-proof、runtime-visible-output、runtime-numeric-proof、ai-workflows、serialized-persistence、project-structure-discovery、modular-architecture、unity-validation、cleanup-and-git。Project maps loaded：UNITY_STRUCTURE.md、UNITY_STRUCTURE.runtime.md、UNITY_STRUCTURE.assemblies.md。独立复核只读，主线统一写入／运行／差异复核。
