# Initialization 07：开场初始化修复

## 结论：开场围腰达到本次修复目标，整件宽袖仍未全面通过

同一候选完成 **96 秒、六方向、1152 张真实 Unity PNG**。从保存的展开姿态启用 Cloth 后连续慢放，原先“放臂开场已经围腰、必须抬臂活动才能解开”的现象在本次所看六向片段中没有复发。初始慢放、转向与急停后均能保持侧下垂。

**快速抬臂／挥臂仍有硬折、尖角及卷束；没有证明全程无自交、遮挡处穿体或持续微抖。** 本次只接受开场围腰这一可见子目标，不把它升级为自然宽袖或正式角色成功，不进入双袖／袍摆。

- [16 秒近景＋代理＋战棋同帧实录](unity-initialization-07-yaw150.gif)：包含完整 4 秒初始化与原 12 秒四动作，没有裁掉开场。
- [完整 96 秒六方向实录](unity-initialization-07-full.gif)、[六方向关键帧索引](six-direction-contact-sheet.png)、[媒体校验](media-manifest.json)。实际解码核对 192／1152 帧与 16／96 秒，非 AI 视频。
- [原始输入对账](runtime-input-proof.json)、[全 5760 步／1152 帧记录](runtime-report.json)、[四份未改动人体快照](runtime-body-samples.json)、[展开姿态取证](open-pose-proof.json)与[原始 EditMode 快照](editmode-open-pose.json)。

| 动作／检查 | 本轮判定 | 真实观察 |
| --- | --- | --- |
| 开场展开、慢放、静置 | 围腰子目标通过 | yaw150 frame0192 展开、frame0252 原动作前静置已侧挂；其余代表 0060／0444／0636／0828／1020 未复现横跨腰背的初态。 |
| 抬臂和放臂 | 跟随／回落有效，自然度未通过 | 如 0276 能提起袖腹；0467～0469 仍有连续硬褶和折团。 |
| 明显挥臂 | 跟随与恢复有效，自然度未通过 | 0299、0491、0683 仍团聚；0874～0876 在 0.167 秒内明显尖折／翻卷，0886 再展开，不是永久卡住。 |
| 身体转向 | 未复发围腰，回落子项通过 | 0130／0322／0514／0706／0898／1090 的袖幅与侧挂保留；不据此声称逐面无穿插。 |
| 移动后急停 | 停后侧下垂子项通过 | 原 1.25 m/s→0 输入保留，末段 0188／0380／0572／0764／0956／1148 未回到旧横向包腰。 |
| 袖根、贴身边、近景 | 未全面通过 | 观察中未见整体脱开；0815／0828 等贴身边仍有锯齿式硬折。初态根部 3 个可动点浅交见下文，不隐瞒。 |
| 战棋尺度／六向 | 大轮廓可读，细节未验收 | 固定斜俯角 38°、正交 6.2 的同帧视图保留；小尺度削弱细折，不可替代近景质量结论。 |

主线检查六向索引以及 90／150／30 的代表实帧；独立只读复核检查 210／270／330 各 11 帧和问题邻帧。以上是有界动态采样，不是逐三角面无穿插证明。

## 冻结设计与边界

2026-09-08，用户在只读开场诊断后明确要求“修复”。只验证一个初始化候选，不改变 Range 06 的网格、绑定、权重、袖幅、约束系数、碰撞体、刚度、阻尼、自碰撞或原四类动作。

旧录制已经证明：Cloth OFF 的放臂蒙皮初态包腰；1～2 秒坏形状基本维持；经历动作后回到同一姿态却能侧下垂。`ClearTransformMotion` 不是解穿插接口。旧动作峰值 t3 的 CPU 蒙皮仍有 3 个顶点在真实人体内，最深 0.017609 m，因此不把峰值冒称无穿体初态。

唯一候选采用保存场景的展开 HumanPose：先在原放臂姿态计算原 05／06 输入，再恢复保存的三项左臂 muscle 值。Cloth 在此启用，完整录制 1 秒保持、2 秒平滑放臂、1 秒静置，再原样执行原 12 秒四动作。六方向各自重建，合计 96 秒；不跳过初始化、不隐藏角度。先用 Unity EditMode 快照检查展开初态，再进行完整 Player 验证；若仍失败，保留证据，不叠加物理或资产修补。实际另有一次黑帧采集失败，仅重录同一二进制，见成本记录。

## 路径与所有者

- 主工作区：`D:\天章游戏开发`。worktree：`D:\天章游戏开发\.worktrees\cloth-wide-sleeve-pilot`，分支 `codex/cloth-wide-sleeve-pilot`，基线 `edf5d653`。写入前 schema 5 Show 两个 run 均空，集成锁 none。没有领取队列任务，没有任务卡／自动化写入；主区 3 项无关 dirty 保留。
- 主线独占修改 `src/Assets/Tests/ClothWideSleevePilot/Runtime/ClothWideSleevePilotController.cs`、同目录 `ClothWideSleeveMotion.cs`、`EditorTests/ClothWideSleeveExperimentEditorTests.cs`。本目录保存说明、离线检验／编码脚本与证据，父 README 仅添加结果入口。
- 唯一场景 `src/Assets/Tests/Scenes/ClothWideSleeveExperimentScene.unity`，GUID `a0a6924ec4081f943a5c82860f4b8cfc` 的 Controller 引用 `TZ_LeftWideSleeve_Cloth`。Start 拥有输入准备与 Cloth 开关，Update／ApplyPose 为唯一姿态写入者，Motion 为纯采样。调用者仅实验 Builder／测试；程序集 `TianZhang.ClothWideSleevePilot` 无项目依赖、autoReferenced=false。三视图观察同一实例，固定斜俯视 38°、战棋正交 6.2。
- 冻结源 `assets/source/characters/cloth-wide-sleeve-pilot/TZ_ClothWideSleevePilot_v001.blend` 与导出、Unity 模型 `src/Assets/Tests/ClothWideSleevePilot/Models/TZ_ClothWideSleevePilot_v001.fbx` 均不改；不启动 Blender，不补旧苻渊模型，不购买、不上传、不接正式角色／Adventure／战斗／存档／BuildSettings。
- 临时运行只写 `D:\Temp\TianZhang-Blender\cloth-wide-sleeve-initialization-07-validation`、`cloth-wide-sleeve-initialization-07-player`、`cloth-wide-sleeve-initialization-07-capture`。有效视觉复录在 `cloth-wide-sleeve-initialization-07-visible-capture`，不覆盖黑帧或其他旧运行。

## 验证计划

1. EditMode：初态真实快照、插值连续性、初始化后 720 个原动作采样在浮点时间量化误差内相同、原场景与物理合同保持。
2. 一次 Development Player：96 秒／六方向／1152 帧，记录每步 Cloth 启停和实际姿态输入；同原动作时间比较 06 的系数、蒙皮目标与碰撞体。
3. 实际近景／战棋六方向观察开场、抬放、挥臂、转向、急停；完整 PNG 编码为真实动图。旧粒子坐标和索引未全量校准，不引用旧穿深指标声称无穿体。
4. 主线统一复核差异，子智能体只读检查生命周期与真实帧；不写文件、不启动引擎、不操作任务／Git／锁。只保留一个 candidate。

采用 `tianzhang-blender-pipeline` 的源路径、隔离和停止边界；采用 `unity-agent-workflows` 的运行所有者、数值证明、持久化及最小验证门禁。已批准方向按 `brainstorming` 收敛为本设计，用户明确实施要求优先，不重复开题审批。

References loaded：Blender tool-routing；Unity runtime-owner-proof、runtime-visible-output、runtime-numeric-proof、serialized-persistence、asset-source-lock、ai-workflows、unity-validation、cleanup-and-git、project-structure-discovery、modular-architecture。Project maps loaded：UNITY_STRUCTURE.md、UNITY_STRUCTURE.runtime.md、UNITY_STRUCTURE.assemblies.md。

## 已证实与仍未知

展开姿态的实际三肌肉值由 Player 每步直接记录：armUp=0.434035718、armForward=0.222648785、forearm=0.961705625。Cloth OFF 的首帧与 EditMode 展开姿态转到角色局部空间后最大差约 **0.510 微米**。非袖根带顶点全部位于真实人体外，最近约 **4.64 厘米**；袖根 453／454／455 仍分别浅交 **5.15／8.68／11.36 mm**，它们均可动，活动半径分别 0.38／0.081／0.38 m。这是 CPU 顶点到闭合、定向人体网格的距离，不是模拟布面穿深，也不等于所有边／面无相交。

相同原动作时间对账：**456 项 maxDistance、范围计算记录、29 固定点及主要物理配置完全相同**；15 组 CPU 目标最大误差 0.588 微米，所有同动作代理中心最大分量差 10.334 微米，世界半径差 0.149 微米，根位置差 4.805 微米。后一组微差来自浮点时间平移与变换／缩放取值，不能表述成逐比特相同；原 FBX、骨权重、碰撞几何配置与动作公式没有改变。

5760 步中只有 0／960／1920／2880／3840／4800 六个周期首步 Cloth OFF；各自第二步启用。第一秒保持中实际 ON 约 0.983 秒。第 4 秒进入原动作时没有第二次 reset。证明本次改善来自初态／启动历程，而非缩袖、改碰撞或再次调软硬。

仍未知：快速动作折团中正常褶皱、自交、回挡／tether 等约束各自影响未分离；没有对全部模拟粒子做索引和世界空间校准。本轮不据旧 `freeVerticesInsideProxy` 等字段判断实际穿深，不追加第二候选。

## 实际制作、调试与返工

- 仅改现有三份 C#，没有新组件、asmdef、模型或 Blender 返工。Controller 426 行、Motion 71 行、测试 417 行。正式系统没有修改。
- 第一次 EditMode [16 项结果](editmode-initial.xml)：15 通过、1 因严格浮点相等失败（差 7.45e-9）；首次[单项修正复测](editmode-float-retry.xml)仍因 1.09e-6 误差超过过严容差失败。按 4 秒平移造成的时间量化与动作最大导数确定误差边界后，[该项复测通过](editmode-motion.xml)。最终 16 项覆盖均有通过证据，**不是一次“16／16 全套重跑通过”**；物理输入未因断言返工改变。
- 一次 Development Player 构建成功；第一次隐藏启动得到 1152 黑帧且报告图形 ring buffer 警告，未作为视觉验收。保持同一二进制与参数，正常窗口复录；`runInBackground=0` 导致失焦暂停，通过 Computer Use 激活唯一实验窗口后完成，未改项目设置。有效运行 96 模拟秒、1152 PNG、5760 steps，墙钟 **109.166 秒**包含三视图、取证与截图写盘，不能当 Cloth 性能基准或制作人时。两次采集都保留，只有后一份有有效画面。
- 录制后清除一个被 JsonUtility 忽略的冗余顶层 Sample 字段，初态 muscle 始终由 step0 及后续所有 steps 直接记录；未改变任何模拟逻辑或已输出 JSON。最终现有 Unity Bee 响应文件＋Unity 自带 Roslyn 编译退出 0、无输出错误。常规 `dotnet build --no-restore` 因临时 `project.assets.json` 缺失报 NETSDK1004，只是生成投影缺还原，未修改 csproj／缓存作为源码修补。
- `verify_runtime.py --runtime` 对账通过；GIF 均完整解码核对。正常窗口 Player 日志没有异常／ring-buffer 警告。引擎与 Player 均已退出，只还原本轮 Unity 自动重建场景 fileID、材质／FBX meta 空白及渲染／ProjectSettings 迁移，源模型和场景基准保持不变。
- 实际投入包括初始化实现、一次初态几何取证、浮点断言返工、一次无效视觉采集与同候选复录、输入对账和媒体整理；没有完整人工工时计时，不虚报人时。

FBX SHA-256：`5BD14D613A68881ED5202F03CE6A494535382DBB541C5BC4310CC6A30E0367A4`。可编辑 `.blend`：`5773BEEFEFD1C4DE02BA80B3F3B12F39BDD084ADFE600477ADEB9730CA255F65`。本轮未上传项目资产。

## 重开与复现

用 Unity 6000.3.18f1 打开主工作区 `src/`，打开 `Assets/Tests/Scenes/ClothWideSleeveExperimentScene.unity`，选择 `ClothWideSleeveExperimentRoot`：

1. `Isolation Mode = Retest02`。
2. 勾选 **Correct Torso Transition**、**Expand Sleeve Range**、**Initialize From Open Pose**；关闭 **Release Diagnosed Torso Pins**。
3. 进入 Play；退出再进入重播。开关默认关闭以保留历史基准，不把尚未全验收的小样推广成正式默认角色。

已构建 Player：`D:\Temp\TianZhang-Blender\cloth-wide-sleeve-initialization-07-player\TianZhangClothWideSleevePilot.exe`。正常显示并保持窗口激活，复录传入新的绝对输出目录：

```text
--correct-torso-transition true --expand-sleeve-range true --initialize-from-open-pose true --body-probe true --capture-dir <新的绝对目录>
```

构建入口仍为 `ClothWideSleeveExperimentSceneBuilder.BuildPlayerForBatchMode`＋`-clothPilotBuildPath`，只构建实验场景，不加入正式 BuildSettings。针对性 EditMode filter 为 `TianZhang.ClothWideSleevePilot.EditorTests.ClothWideSleeveExperimentEditorTests`；`-clothPilotInitializationProbePath <绝对文件>` 可让初态测试保存真实快照。

有效 125,104,576 bytes 原始 JSON 与全部 PNG 保留在本机 visible-capture 目录。提交的 compact report 仅移除逐帧大体积 skinning 数据，四份原始快照另存；离线脚本读取原路径可重新校验，不启动引擎或 Blender。

## 后续投入边界

开场方向值得保留，单袖已有可见进展；但本轮停止，不进入双袖／袍摆。下一步如获批准，应先单独判定快速动作折团的成因与制作代价。没有验证双袖互碰、袍摆、多层衣物、目标低配硬件或同屏多角色性能；当前录制耗时不可线性外推人数与正式帧率。
