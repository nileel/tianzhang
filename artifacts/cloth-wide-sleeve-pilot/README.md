# Unity 自带 Cloth 单侧宽袖试验

## 当前结论：Retest 02（2026-09-05）

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
4. 双球配对本身构成连续锥形胶囊；不能因 `capsuleColliders` 数组为空就认定躯干／肘部有碰撞空隙。当前代理体覆盖真实人体的程度仍未量化。
5. 旧“急停通过／失败”、按六方向转动展示得出的动作全方向通过结论，均由 Retest 02 重新判断，不再作为有效验收。
6. Retune 01 有效报告实际为 `54.4360619 fps`、最大 `2705.13745 ms`。此前 README 中的 `55.02 fps / 2728.62 ms` 来自无效黑屏录制，现更正。旧帧率混入截图开销，不是 Cloth 性能基准。

基线 GIF SHA-256：`C3541AF0126D1C8E3067C2AFC062854EADB6251912E7C87B7D411CA958A94D8A`。Retune 01 GIF SHA-256：`082CDCB404AFB1D2A596A1D49C56CD35535E3942FDD7FF9E534F562CFCF84BF8`。有效原始帧分别保存在本机 `D:\Temp\TianZhang-Blender\cloth-wide-sleeve-pilot-capture-final2\`、`D:\Temp\TianZhang-Blender\cloth-wide-sleeve-retune-02-capture\`。

## 实际制作、返工与未覆盖项

既有制作包括免费绑定人体导入、新建单袖、蒙皮／约束／代理体配置、独立场景和实际播放器。曾返工 Unity 6 的 `useVirtualParticles` API 类型、FBX 单位导出（改为 FBX Units Scale，Unity 节点恢复 1 倍缩放）、相机跟随以避免裁切、可见窗口以排除全黑录制。之后有一次两变量加硬尝试。本次又修正了测试动作与取证驱动，没有再调物理或重新制作模型。

本次只做一次有效的 72 秒全方向运行，没有反复参数搜索；新增的数值探针未通过验证也如实保留。这说明目前制作成本还包括测试与碰撞／蒙皮诊断返工，**不能据此承诺低成本角色量产**；此前没有完整人工工时记录，不虚报总人时。

暂不建议进入双袖或袍摆。未覆盖同屏多角色、双袖互碰、多层衣物和目标硬件性能。本次固定步长、三相机、截图及诊断计算的墙钟耗时不代表正常游戏帧率；不能按单角色录制数字线性外推多人规模。
