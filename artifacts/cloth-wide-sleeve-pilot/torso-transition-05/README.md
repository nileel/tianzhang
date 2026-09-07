# Torso Transition 05：授权后的最小局部修正

日期：2026-09-07。用户在 Torso Contact 04 证实蒙皮目标／运动约束与真实人体冲突后，明确允许：只调整贴身过渡区必要运动约束或蒙皮目标，再校正躯干代理；其余保持不变。

## 结论与真实结果

**已实施一次局部修正并完成一次 72 秒／六方向／四类动作的 Unity Player 实跑；候选仍未达到自然宽袖验收。** 真实注册的躯干覆盖缺口已缩小，旧三个目标的运动距离短缺已解除，但放臂、转身与急停回落仍出现明显横向包腰、外撑硬边／翻卷。没有继续叠加第二组参数或蒙皮修补，不进入双袖／袍摆。

- [12 秒左右对照](unity-transition-05-comparison.gif)：左为旧 Pin Release 01，右为本次 Torso Transition 05，均为独立真实 Unity 运行的同时间近景。取绝对 t12–24（yaw150），保留完整动作与回落，没有挑选静止美化结果。
- [本次 12 秒三视图实录](unity-transition-05-yaw150.gif)：同帧近景、代理线框和战棋视图。
- [完整 72 秒六方向实录](unity-transition-05-full.gif)、[六方向关键帧索引](six-direction-contact-sheet.png)。全部方向均保留，索引图不替代动态实录。
- [运行记录](runtime-report.json)、[直接运行时输入证明](runtime-input-proof.json)、[两帧原始人体／袖子快照](runtime-body-samples.json)。原始完整约 97.7 MB JSON 与全部 PNG 在临时运行目录，提交版运行记录只去掉大体积 skinning 快照，保留全部 864 帧时序、原 measurement、4320 steps；所引用 t12／t15 的原始快照独立保留。

| 动作／检查 | 结果 | 实际观察 |
| --- | --- | --- |
| 抬臂与放下 | 跟随与抬起时下垂部分仍有正面效果；整体失败 | 抬起时保留袖腹，放下后重新围腰、翻出硬边；不能据此判袖根稳定合格。 |
| 明显挥臂 | 整体失败 | 袖体会随手臂移动并下垂，但大折片、卷起边与非自然包身形状未消除。 |
| 身体转向 | 失败 | 围腰式横撑仍在，不能作为自然宽袖轮廓。 |
| 移动后突然停步 | 输入有效，回落形状失败 | 例如 yaw150 的 frame0284，停止已约 2.67 秒，仍有横向围腰与上翻硬边。 |
| 近景／战棋尺度／固定斜俯视六方向 | 未通过 | yaw150、270、30 等方向可见异常，近景清楚；战棋投影也保留腰部外撑轮廓。 |
| 穿插／拉尖／自交／持续抖动 | 没有全面排除 | 可见硬折与局部尖角、翻卷，不将其自动等同于某个精确三角面穿透深度，也不凭关键帧宣布全段无抖动或自交。 |

三相机观看同一人体／单袖实例；固定俯角 38°、战棋正交 6.2，原面板仅 640×360，不等同正式全屏像素或 UI 验收。移动是已冻结的 1.25 m/s 根节点平移／9 秒处速度突停，未覆盖完整走跑步态。

## 已改什么，已证实什么

候选同时包含一个静态局部约束掩码和一个躯干双球形状修正，**不是分离二者视觉因果的单变量试验**：

- 相对 68 固定点基准，仅提高 71／456 点的 maxDistance：32 个原自由点、39 个原固定点；其余 385 点不变，剩 29 个固定点。值来自 11 个旧真实快照的代理避让需求加 15 mm 工程余量，向上取整到 1 mm，不是反复调试得到的最优值。最大新需求约 0.215073 m；整体原最大值仍是 0.38 m。
- [可重复派生脚本](derive_constraints.py)、[精简原始输入](constraint-inputs.json)、[逐点数值与覆盖](constraint-derivation.json)保留全部 71 项，不只处理示例三个点。该推导证明单点运动球可达性，不保证布面全部约束同时可满足。
- 真实运行直接记录的 456 项系数与派生值最大差约 `1.46e-8 m`，只是 float 序列化差；15 个实际全量快照的单点躯干活动范围没有再出现不足。旧三个目标仍在人体内，**没有改蒙皮目标位置**：415／454／359 在放臂时距体表约 1.18／1.18／11.19 cm，现允许移动 9.5／8.1／21.1 cm，距离短缺归零；这不是“目标已经在体外”或“布料已无穿模”。
- 真实躯干中心与预测偏差不超过约 0.320 微米，半径确为约 0.18／0.225 m；独立复核旧、新全程 858 个启用采样，手臂两对的中心和半径逐项相同。上／前臂引用、自碰撞集合、源 mesh、动作、刚度不变。
- 旧 11 个姿态离线回放中，三对代理合并后胸部最大仍约 4.96 mm 体表在代理外；代理不是精确人体网格。人体、眼睛、眉毛均保存独立取证，人体结论不混入眼眉。
- 原局部坐标检查是 822／858 个启用采样通过，最大固定点候选误差约 1.963 cm，仍有 36 帧未过；而且固定点通过也不校准所有自由顶点。保留原数据但不引用其中 `freeVerticesInsideProxy`／`maximumProxyDepthMeters` 宣称实际人体穿透已消除。固定点集合已由 68 减少到 29，不能直接用通过率宣称质量提升。

剩余包身、硬边和翻卷，究竟主要由保守代理外形、局部放宽、蒙皮参考或布料约束的共同作用导致，本轮尚未分离。这里不下“Unity Cloth 做不了宽袖”或“换插件即能解决”的结论；由于自然形状仍失败，不再继续堆参数。

Unity 6.3 的[Cloth 手册](https://docs.unity3d.com/6000.3/Documentation/Manual/class-Cloth.html)明确要求手动注册受支持的简化碰撞体；[Cloth.vertices API](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Cloth-vertices.html)还提示粒子索引不一定等同源 mesh。本试验沿用真实注册双球，不以“挂一个任意模型防穿模脚本”代替上述证据。

## 制作、调试、验证与成本

- 本轮无建模或权重返工，无付费、无新资产提供商、无上传项目资产。新增一个实验局部修正类，控制器只接可选开关、初始化和记录；没有新系统或模块依赖。
- 一次 EditMode 运行 **13／13 通过**，包含局部约束／代理、手臂与源输入不变检查，见 [XML](editmode-results.xml)。随后只修正了完整运行的特殊取证帧索引（全局帧号改为试次内帧号），不改物理；构建编译通过，实际运行已采到 4.9167／5.5／7.9167 秒关键姿态，未重复已通过的同范围测试。
- 一次 Development Player 构建成功，一次有效 Player 运行，72 模拟秒、4320 步、864 帧、15 个全量人体快照。82.3749 秒墙钟包含三相机、CPU 取证、截图和写盘，**不是稳定帧率、单角色 Cloth 成本或制作人时**。
- [运行验证脚本](verify_runtime.py)确认实际候选数值、动作时序、源／手臂输入与三个目标可达性。GIF 逐帧解码验证与 SHA-256 见 [媒体清单](media-manifest.json)，来源是实际 Unity PNG，仅做缩放、对照裁切和编码，没有 AI 视频。
- Unity 已退出，仅清除本轮构建产生的渲染设置、材质空白、FBX meta 空白与场景 fileID 重建差异；源文件、旧证据和用户无关改动保留。
- 这次得到一份解释更清楚但仍失败的候选，不支持低成本角色量产，也不建议进入双袖／袍摆。单角色未覆盖多角色同屏、双袖互碰、袍摆、多层布料、目标低配硬件；录制耗时不能线性外推这些性能风险。

## 重新打开与重播本候选

在下述隔离工作区的 Unity `src/` 项目打开原实验场景，选 `ClothWideSleeveExperimentRoot`：`Isolation Mode = Retest02`，只勾选 **Correct Torso Transition**，保持 **Release Diagnosed Torso Pins** 关闭，进入 Play。退出再进入 Play 重播。候选失败，所以两个开关默认均不启用；未把失败修正写成正式默认，场景原字节保留。两开关同时启用会明确报错。

本机已构建 Player：`D:\Temp\TianZhang-Blender\cloth-wide-sleeve-transition-05-player\TianZhangClothWideSleevePilot.exe`。复录使用新的输出目录：

```text
--correct-torso-transition true --body-probe true --capture-dir <新的绝对目录>
```

构建沿用 `TianZhang.ClothWideSleevePilot.Editor.ClothWideSleeveExperimentSceneBuilder.BuildPlayerForBatchMode` 与 `-clothPilotBuildPath`。检查命令为 Unity `-batchmode -nographics -runTests -testPlatform EditMode -testFilter TianZhang.ClothWideSleevePilot.EditorTests.ClothWideSleeveExperimentEditorTests`。运行快照与日志在 `D:\Temp\TianZhang-Blender\cloth-wide-sleeve-transition-05-*`。`derive_constraints.py` 可直接从本目录输入重算；`verify_runtime.py`／`encode_capture.py` 使用上述本机原始记录，不启动第二次模拟。

References loaded：Blender `tool-routing`；Unity `runtime-owner-proof`、`runtime-visible-output`、`runtime-numeric-proof`、`ai-workflows`、`serialized-persistence`、`project-structure-discovery`、`modular-architecture`、`unity-validation`、`cleanup-and-git`。Project maps loaded：`UNITY_STRUCTURE.md`、`UNITY_STRUCTURE.runtime.md`、`UNITY_STRUCTURE.assemblies.md`。无缺失门禁；只读复核与主线统一核验，子智能体未写入。各改动 C# 均未过 500 行；不涉及正式模块图或跨模块改造。

## 冻结执行与路径

- 隔离工作区：`D:\天章游戏开发\.worktrees\cloth-wide-sleeve-pilot`，起点 `87de10d1cad947151b216184f92f28427260670e`。写入前工作区干净；从主工作区调用 schema 5 Show（显式 RepositoryRoot）得到两个 run 均空、集成锁 none。主工作区无关改动不碰。
- 原场景：`src/Assets/Tests/Scenes/ClothWideSleeveExperimentScene.unity`；不接入 BuildSettings、正式角色、Adventure、战斗或保存。
- 允许脚本：`src/Assets/Tests/ClothWideSleevePilot/Runtime/ClothWideSleevePilotController.cs`、新建同目录 `ClothWideSleeveTorsoCorrection.cs` 及 `.meta`、必要的同帧只读取证 `ClothWideSleeveSkinningProbe.cs`；测试在 `EditorTests/ClothWideSleeveExperimentEditorTests.cs`。实验场景仅保存可重播的候选开关，不替换默认基准，除非实际验收通过。
- 证据、最小离线计算与实录编码只在本目录；入口更新上一层 README。临时 Player、日志、PNG 只在 `D:\Temp\TianZhang-Blender\cloth-wide-sleeve-transition-05-*`，不覆盖旧运行。
- 不改 `assets/source/characters/cloth-wide-sleeve-pilot/TZ_ClothWideSleevePilot_v001.blend` 与 `src/Assets/Tests/ClothWideSleevePilot/Models/TZ_ClothWideSleevePilot_v001.fbx`。FBX SHA-256 仍为 `5BD14D613A68881ED5202F03CE6A494535382DBB541C5BC4310CC6A30E0367A4`。不从旧苻渊网格修补。

## 修正设计与验证门槛

唯一候选以 Pin Release 01 的 68 固定点为比较基准：使用 Torso Contact 04 已录制的 11 个同帧骨骼／人体快照，计算更贴近胸腰骨盆的双球代理对贴身点运动范围的要求。候选双球参考人物局部低端 `(0.01,0.995,0.025)`、半径 `0.18 m`，高端 `(0.01,1.43,0.025)`、半径 `0.225 m`，分别保留已有 spine_02 与 neck_01 骨骼父级。只给已证实范围不可达的点提高静态 maxDistance，保留其它点和已有 28 点释放值；不逐帧投射，不改蒙皮目标，不改手臂两对、袖幅、刚度、动作、相机、自碰撞集合或系统。

验证应先证明索引掩码、保留固定点及 unchanged 输入，再用一次完整的真实 Unity 六方向／四动作运行判断。场景可重新打开，候选通过 Inspector 开关重播。编译成功不等于通过；不得将候选 Cloth 坐标下未经完整校准的穿透数字当作实际袖面穿深。若仍出现无法解释的相同现象，保留本候选和证据，不叠加第二种物理方案。

运行时所有者：场景中控制器 GUID `a0a6924ec4081f943a5c82860f4b8cfc` 唯一引用，C# 调用者仅实验 Builder 与测试。源码属于独立 `TianZhang.ClothWideSleevePilot` 程序集，项目依赖为空、autoReferenced=false。原目标源和其它正式表现所有者均未替代。

流程采用 `tianzhang-blender-pipeline` 的源路径／工具通道规则，以及 `unity-agent-workflows` 的所有者、运行数值、可见输出、序列化、程序集与最小验证规则。这里是对已批准修复范围的执行记录，不另建生产管线或重新讨论是否开始。
