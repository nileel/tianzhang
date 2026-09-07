# Unity 自带 Cloth 单侧宽袖试验

## 当前结论：Sleeve Range 06（2026-09-07）

**放宽源几何确需的袖腹活动范围后，抬放、转身、急停后的持续围腰明显改善，袖腹能够侧下垂；但开场仍包腰，快速动作近景仍有硬折／卷束，整体验收未通过。** 仅提高 249 个非固定袖腹点的 maxDistance，原袖根带、上侧、29 固定点、网格、权重、碰撞和刚度保持不变。实际 15 快照蒙皮目标、858 帧碰撞代理、4320 动作步骤与 05 相同，确认本次修改真正生效且有可见收益。

见 [本次结果与重播说明](sleeve-range-06/README.md)、[左 05／右 06 的 12 秒真实对照](sleeve-range-06/unity-range-06-comparison.gif)、[完整 72 秒实录](sleeve-range-06/unity-range-06-full.gif)。14／14 EditMode 通过，一次构建、一次有效运行；没有追加第二候选。可继续评估单袖初始化与动态折叠，但暂不进入双袖／袍摆，也不推断同屏多角色性能。

重播时在原实验控制器勾选 **Correct Torso Transition** 和 **Expand Sleeve Range**，其余保持原基准设置；默认均关闭，可编辑源和原场景保留。

## 上阶段结论：Torso Transition 05（2026-09-07）

**已按授权实施贴身约束＋躯干代理的局部修正并重跑完整六方向，但自然宽袖仍未通过。** 相对 68 点基准，仅提高 71 个贴身点的活动范围，保留 29 个固定点；手臂两对、袖幅、源 mesh、动作和软硬不变。实际运行确认新躯干生效，旧三个目标的活动距离短缺归零；放臂、转身和停后回落仍有横向包腰与上翻硬边，不能宣称穿模已全面消除。

见 [本轮结论、数值证据与重播说明](torso-transition-05/README.md)、[左旧右新的 12 秒真实对照](torso-transition-05/unity-transition-05-comparison.gif)、[完整 72 秒六方向实录](torso-transition-05/unity-transition-05-full.gif)。13／13 EditMode 通过，一次构建与一次完整候选运行；不再叠加参数，不进入双袖／袍摆，单角色性能仍不能外推。

候选通过实验控制器 **Correct Torso Transition** 开关重播，默认关闭，与旧 **Release Diagnosed Torso Pins** 不同时启用。原实验场景与可编辑源保持不变，没有接入正式链路。

## 上阶段结论：Torso Contact 04（2026-09-07）

**手臂跟随已有正面效果，但不能仅靠补躯干代理完成修复。** 本轮真实 Unity 人体表面取证查明：两个仍固定的蒙皮目标位于真实人体内约 1.2 cm；另一个目标在体内约 11.2 cm，却只允许移动约 2 cm。粗代理确有胸上部／骨盆下缘覆盖缺口，但补齐体表碰撞会与现有运动范围冲突。没有应用代理候选或继续调软硬；需要确认贴身过渡区约束／蒙皮目标的最小扩围后才能继续。

见 [Torso Contact 04 报告与数值证据](torso-contact-04/README.md) 和 [未改模拟输入的 8 秒取证实录](torso-contact-04/unity-body-probe-baseline.gif)。本轮只新增只读人体快照及检查；原场景、模型、碰撞与约束设置不变，**尚未完成修复，也不升级已有动作验收结论**。

## 上阶段结论：Isolation 03（2026-09-05）

**查明了 28 个固定蒙皮目标与躯干碰撞代理的初始几何冲突，但只释放这些点后，完整六方向复验仍有包腰、硬折与回落失败，宽袖未通过。** A 纯蒙皮本身也有包腰背的参考形状，不能把所有问题归因于碰撞或 Unity Cloth；本轮停止，不再调参，不进入双袖／袍摆。

见 [Isolation 03 完整报告](isolation-03/README.md)、[A／B／C 并排真实实录](isolation-03/unity-abc-comparison.gif)、[一次修正后的 12 秒完整动作片段](isolation-03/unity-pin-release-01-yaw150.gif) 与 [修正后全部六方向 72 秒实录](isolation-03/unity-pin-release-01-full.gif)。新增诊断与唯一修正检查共 11／11 通过，不替代视觉失败判定。

实验场景默认仍播放原 Retest 02；本次修正通过控制器的 `Release Diagnosed Torso Pins` 开关重播，默认关闭，详见报告。它只把原 96 固定点中的 28 点最大位移改为既有的 0.38 m，保留 68 固定点和其他全部输入；没有把失败候选推广为默认方案。新增成本为三组隔离、一次代理几何取证和一次全动作复验，没有模型／源文件返工或第二次参数修正。

## 上阶段结论：Retest 02（2026-09-05）

**修正动作输入后仍未达到进入双袖／袍摆的门槛，但还不能把失败归因于 Unity Cloth 本身，或认定某个防穿模脚本就能解决。** 六个朝向的实际运行仍显示包腰背、鼓包、硬折与回落形状失败。碰撞深度探针没有通过整轮坐标验证，因此本轮不以它的深度值宣称已经证明人体穿透或碰撞失效。

最新证据、逐项判定、验证限制与复现方法见 [Retest 02 报告](retest-02/README.md)。

- [完整 72 秒 Unity 实录](retest-02/unity-cloth-retest-02-full.gif)：六方向均包含抬放臂、挥臂、转向、移动与骤停；同帧近景、代理体线框、战棋投影视图。
- [150° 的 12 秒实录片段](retest-02/unity-cloth-retest-02-yaw150.gif)：包含完整动作链和停后回落，未跳过失败段。
- [六方向关键帧](retest-02/six-direction-contact-sheet.png)、[逐帧运行数据](retest-02/runtime-report.json)、[9 项 EditMode 检查](retest-02/editmode-results.xml)。

## 冻结结构与重开入口

- 免费人体：Quaternius Universal Base Characters **Standard** 的 `Superhero_Male_FullBody.fbx`，65 骨有效 Humanoid。使用已核验的免费 CC0 来源，不是付费 Source 包。
- 一只从零建立的左侧宽袖；没有读取或继续修补旧苻渊坏网格。456 顶点、432 四边面／864 三角面；开放袖根与袖口，明显下垂袖腹。既有拓扑检查记录的额外非流形边、退化边、零面积面均为 0。
- 一份 Cloth：96 固定点、360 自碰撞点、3 组躯干／上臂／前臂双球碰撞代理、180 Hz solver。
- 当前冻结 Retune 01 物理：弯曲刚度 0.70、拉伸刚度 0.88、最大位移梯度 `0 → 0.10 → 0.24 → 0.38 m`。Retest 02 没有调这些参数，也没有缩袖幅或修改权重。
- 实验场景：`src/Assets/Tests/Scenes/ClothWideSleeveExperimentScene.unity`。打开后进入 Play 即可播放 72 秒，退出再进入 Play 重播。没有加入 BuildSettings，没有接入正式角色、Adventure、战斗或存档。
- 可编辑源：本机 `D:\天章游戏开发\assets\source\characters\cloth-wide-sleeve-pilot\TZ_ClothWideSleevePilot_v001.blend`。此目录按项目规则被 Git 忽略，原文件保留不变。
- 冻结 FBX：`src/Assets/Tests/ClothWideSleevePilot/Models/TZ_ClothWideSleevePilot_v001.fbx`，SHA-256 `5BD14D613A68881ED5202F03CE6A494535382DBB541C5BC4310CC6A30E0367A4`。

## 旧证据保留，但撤回过度结论

基线 `unity-cloth-wide-sleeve-pilot.gif` 与 `runtime-report.json`、Retune 01 的同名后缀 GIF／JSON 均保留原字节。它们是真实 Unity 运行，不是生成视频，但旧测试驱动和抓帧时序不足以支持原报告的全部判定：

1. 旧动作段之间存在姿态／根节点跳变；旧“急停”用 SmoothStep 在到达端点前已减速到零，不是真正的速度突停。
2. 旧抓帧基于墙钟时间并有补采样，无法把 GIF 帧号可靠映射成动作时间。旧报告按第 36–40 帧等作出的精确动作归属撤回。
3. Retune 01 **同时改变弯曲刚度和最大位移**，不是单变量试验；撤回“已证实基线太软”和已分离加硬因果的表述。
4. 双球配对本身构成连续锥形胶囊；不能因 `capsuleColliders` 数组为空就认定躯干／肘部有碰撞空隙。代理体覆盖真实人体的程度在当时未量化，后续 Torso Contact 04 已针对胸腰骨盆做同帧体表取证，见最新报告。
5. 旧“急停通过／失败”、按六方向转动展示得出的动作全方向通过结论，均由 Retest 02 重新判断，不再作为有效验收。
6. Retune 01 有效报告实际为 `54.4360619 fps`、最大 `2705.13745 ms`。此前 README 中的 `55.02 fps / 2728.62 ms` 来自无效黑屏录制，现更正。旧帧率混入截图开销，不是 Cloth 性能基准。

基线 GIF SHA-256：`C3541AF0126D1C8E3067C2AFC062854EADB6251912E7C87B7D411CA958A94D8A`。Retune 01 GIF SHA-256：`082CDCB404AFB1D2A596A1D49C56CD35535E3942FDD7FF9E534F562CFCF84BF8`。有效原始帧分别保存在本机 `D:\Temp\TianZhang-Blender\cloth-wide-sleeve-pilot-capture-final2\`、`D:\Temp\TianZhang-Blender\cloth-wide-sleeve-retune-02-capture\`。

## 实际制作、返工与未覆盖项

既有制作包括免费绑定人体导入、新建单袖、蒙皮／约束／代理体配置、独立场景和实际播放器。曾返工 Unity 6 的 `useVirtualParticles` API 类型、FBX 单位导出（改为 FBX Units Scale，Unity 节点恢复 1 倍缩放）、相机跟随以避免裁切、可见窗口以排除全黑录制。之后有一次两变量加硬尝试。本次又修正了测试动作与取证驱动，没有再调物理或重新制作模型。

Retest 02 只做一次有效的 72 秒全方向运行，没有反复参数搜索；当时新增的数值探针未通过验证也如实保留，后续 Isolation 03 的校准、几何确证和一次修正另见最新报告。这说明目前制作成本还包括测试与碰撞／蒙皮诊断返工，**不能据此承诺低成本角色量产**；此前没有完整人工工时记录，不虚报总人时。

暂不建议进入双袖或袍摆。未覆盖同屏多角色、双袖互碰、多层衣物和目标硬件性能。本次固定步长、三相机、截图及诊断计算的墙钟耗时不代表正常游戏帧率；不能按单角色录制数字线性外推多人规模。
