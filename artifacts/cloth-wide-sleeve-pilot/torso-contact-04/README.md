# Torso Contact 04：纯躯干代理修复被真实体表／运动约束冲突阻断

日期：2026-09-07。用户批准沿“保留手臂跟随，修正躯干碰撞”方向修复重试。**本轮完成一次真实 Unity 取证运行，发现只调整躯干代理不能同时满足现有运动约束与体表避让，故在物理补丁前停止。没有实施代理候选，也没有宣称修复成功。**

冻结：免费 Standard 人体、v001 宽袖源／FBX／权重／袖幅、上臂／前臂代理、动作／相机／刚度／自碰撞集合。比较基准为 Isolation 03 的 Pin Release 01（68 固定点），不是默认的 96 点 Retest 02。

本轮执行方案限定在已有实验所有者内取证和修正躯干代理：先用 Unity Player 中非 Cloth 人体的 BakeMesh 世界表面与实际注册代理几何证明覆盖情况，有依据且不破坏冻结约束时才修正代理；不叠加软硬参数、蒙皮修补或替换系统。由于证据表明还需要调整贴身过渡区的运动约束或蒙皮目标，触发了扩大范围前停止的条件。

## 实际证据

- [8 秒真实 Unity 取证实录](unity-body-probe-baseline.gif)：**输入未修改的基准，不是修复效果演示**。yaw 150°，静置、抬放臂、挥臂、静置回落；同帧近景、代理线框、战棋投影。96 帧，960×540，12 fps。没有新增物理方案，因此没有重复之前的六方向 72 秒整套运行。
- [原始同帧人体／袖子／代理快照](body-surface-samples.json)：保留 t=0 放臂与 t=3 抬臂两帧；原始运行共 96 帧，截取说明在 JSON 的 `selectionNote`。附加的有效运动约束数组按冻结保存场景系数与已验证的 28 点释放掩码重建，不是运行时逐顶点直接采集；其固定点索引集合与每帧原始快照核对一致，来源见 `effectiveMaxDistancesSource`。
- [体表覆盖及约束冲突结果](body-constraint-proof.json)、[可重复计算脚本](analyze_body_coverage.py)。算法使用独立人体表面与 CPU 蒙皮目标，**没有用未全面校准的 Cloth 顶点来计算人体穿透**。

人体包含 `SuperHero_Male`、`Eyes`、`Eyebrows` 三个启用的非 Cloth 渲染网格。本次躯干结论只使用 `SuperHero_Male` 的 7,275 顶点／12,566 三角形，眼睛与眉毛不混入躯干覆盖统计。全部取证快照的非 Cloth 人体 BakeMesh 与四权重 CPU 蒙皮最大差约 0.609 微米。

## 躯干不是完全没有碰撞，但粗代理形状不贴体

以主导骨骼分区，放臂姿态的体表顶点统计如下；“未覆盖”指在躯干代理外超过 5 mm，**不是表面积比例或实测袖面穿透**。

| 区域 | 未覆盖顶点／区域顶点 | 最大体表—代理间隙 |
| --- | --- | --- |
| 腰部下／中段 `spine_01 / spine_02` | 0／152、0／172 | 两区都已被包住 |
| 胸上部 `spine_03` | 54／240 | 5.10 cm |
| 骨盆区域 `pelvis` | 96／248 | 7.19 cm（最远点在骨盆下缘） |

计入已有手臂代理后，胸上部仍有 41 点未覆盖，最大约 4.21 cm；不能把整片腰背描述为“无碰撞”。抬臂姿态的相应缺口仍在。左肩区域部分由上臂代理覆盖，右肩不是本轮单袖重点；分区原始结果保留在 JSON。

作为一次**离线几何检查、未应用到 Unity**，试算了用一对更长更粗的球对包住胸腰骨盆（人物局部端点 `(0.01,0.995,0.025)`／`(0.01,1.43,0.025)`，半径 0.18／0.225 m）。虽然选定体表覆盖增加，却重新压入 13 个固定目标，最大约 5.55 cm，因此没有将其作为物理候选或继续调半径。

## 决定停止的证据：蒙皮目标本身已经在真实人体内部

在 t=0 的实际放臂姿态，以下三点已足以证明约束不能全部满足：

| 袖子源顶点索引 | 到真实人体表面的最短距离（点在体内） | 允许运动半径 `maxDistance` | 至少还缺少的运动距离 |
| --- | --- | --- | --- |
| 415 | 1.1765 cm | 0（固定） | 1.1765 cm |
| 454 | 1.1799 cm | 0（固定） | 1.1799 cm |
| 359 | 11.1915 cm | 1.9475 cm | 9.2439 cm |

这不是估计碰撞球覆盖，而是 CPU 蒙皮目标到真实人体三角面的最近距离，且三个点的实体绕数均约为 1。415、454 属于当前保留的 68 个固定点；359 是自由点，但可移动范围不够离开人体。最近体表三角形属于胸背 `spine_03 / spine_02`，不是头部或眼睛。抬臂 t=3 时这三个点不在体内，因此问题具有姿态依赖性。

可信度检查：人体网格按**完全相同的世界坐标**合并导入产生的重复缝顶点后，有 6,285 顶点、18,849 边，所有边恰有两面且朝向相反；无边界、非流形边或退化面。独立只读复核还检查了单一连通组件、Euler 数 2，并用三个不平行方向的射线奇偶复核这三个点在内部；距离与主计算一致。

**结论边界：**这些数值是“蒙皮目标及运动半径与人体的冲突”，不是实际求解后袖面穿入身体的深度。没有做全部三角形的全局自交审核，也不据此宣称 Unity Cloth 无法做宽袖。局部调整代理仍可能改善画面，但不能在保持这些目标和运动范围不变时，同时满足完整体表避让；不能把局部改善当作已消除上述冲突。

## 实施、返工与验证

- 保留手臂跟随、袖幅、刚度、代理与 68 点基准的全部模拟输入，没有重建人体、改袖子源、购买插件或上传资产。最终保存场景仍为原默认 Retest 02，28 点释放开关仍默认关闭。
- 仅扩展既有只读 `ClothWideSleeveSkinningProbe`：通过 `--body-probe true` 保存同帧非 Cloth 人体表面；控制器只增加这个可选参数的传递。普通播放和既有模式不启用人体取证。
- 两次诊断播放器构建。首次取证错误假设只有一个人体渲染器，触发 `Sequence contains more than one matching element`，模拟未开始；改为记录同一人物下实际启用的三个身体部件后，成功运行 8 s／480 步／96 帧。没有物理参数重试。
- [首次 EditMode 结果](editmode-initial.xml)：既有 11 项通过，新增人体快照检查因 EditMode 不允许 `Destroy` 而失败。临时 Mesh 的销毁改为 Play 时延迟销毁、EditMode 时立即销毁后，[仅重跑新增检查 1／1 通过](editmode-body-final.xml)；没有重复已通过且输入未变的 11 项。
- 新检查验证三个人体部件、精确网格规模、BakeMesh／CPU 一致性、读取前后全部 Cloth 约束与代理引用不变。Unity 构建引起的渲染设置、导入 meta、材质空白和场景 fileID 重建差异不纳入交付。
- 这轮没有完成躯干修复，也未重新验收转向／急停／六方向。既有动作结论不因本次取证而升级为通过；单角色性能、多角色同屏与双袖／袍摆仍未覆盖。

下一步需要单独确认的最小范围：只处理造成体内目标／不可达的**贴身过渡区运动约束或蒙皮目标**，再校正躯干代理；保留手臂两对代理、袖幅、人体、软硬参数与同一动作。这里不保证仅放开上表三点就足够，也不自动继续实施或增加系统。

## 路径、隔离与复现

主工作区 `D:\天章游戏开发`，隔离 worktree `D:\天章游戏开发\.worktrees\cloth-wide-sleeve-pilot`，基准提交 `6406bbb56e1a8883a3a531074d3208b35a383596`。写入前主工作区 schema 5 两个 run 均空、集成锁空闲，worktree 干净。无新 taskId，不改队列。

精确路径（相对上述 worktree）：

- 取证所有者：`src/Assets/Tests/ClothWideSleevePilot/Runtime/ClothWideSleeveSkinningProbe.cs`。
- 运行入口与诊断开关：`src/Assets/Tests/ClothWideSleevePilot/Runtime/ClothWideSleevePilotController.cs`。
- 原计划代理修正文件 `Runtime/ClothWideSleeveTorsoProxy.cs` **未创建**；没有代理补丁。新增独立检查仍在 `EditorTests/ClothWideSleeveExperimentEditorTests.cs`。
- 现有场景：`src/Assets/Tests/Scenes/ClothWideSleeveExperimentScene.unity`，仍不在 BuildSettings；不接入正式角色／Adventure／战斗／存档。
- 本轮证据及分析脚本：本目录 `artifacts/cloth-wide-sleeve-pilot/torso-contact-04/`；完成后仅更新上一层入口 README。
- 可编辑源：`assets/source/characters/cloth-wide-sleeve-pilot/TZ_ClothWideSleevePilot_v001.blend`，本轮不改。
- 冻结导出：`src/Assets/Tests/ClothWideSleevePilot/Models/TZ_ClothWideSleevePilot_v001.fbx`，本轮不改。
- 临时构建与帧：`D:\Temp\TianZhang-Blender\cloth-wide-sleeve-torso-04-*`，不覆盖旧运行。

运行时所有者已通过控制器 GUID `a0a6924ec4081f943a5c82860f4b8cfc` 唯一场景引用与 C# 调用者搜索确认；全部代码属于独立 `TianZhang.ClothWideSleevePilot` 程序集（无项目依赖、autoReferenced=false）。新增人体快照只读，无物理或动画写入。人体 BakeMesh 独立性另以 CPU 蒙皮差值校验；不以尚未全面校准的 Cloth 顶点推断真实人体穿透深度。

构建命令沿用既有 `ClothWideSleeveExperimentSceneBuilder.BuildPlayerForBatchMode` 与 `-clothPilotBuildPath`。本次播放器参数为：

```text
--isolate-mode C --release-conflicting-pins true --body-probe true --capture-dir <新的绝对目录>
```

原始帧和完整 96 帧运行 JSON 位于 `D:\Temp\TianZhang-Blender\cloth-wide-sleeve-torso-04-probe-parts-capture\`；播放器位于 `cloth-wide-sleeve-torso-04-probe-player\TianZhangClothWideSleevePilot.exe`，同根目录保留对应 build／player／tests 日志。完整 JSON 约 68 MB，仅提交上述两个直接用于结论的原始帧快照。

在安装了 NumPy 的 Python 中执行 `python -B analyze_body_coverage.py body-surface-samples.json`，即可重算覆盖与三点约束结果；无 Unity 或项目资产写入。`make_probe_gif.py` 从既有 96 个 PNG 编码实录，不重跑模拟。GIF 已逐帧解码核验 96 帧／8,000 ms，SHA-256 `bfb6ad5c570883894758823e812be859a657e6c3ac052797d44d641ca5e8b89a`。

流程依据：本轮读取 `tianzhang-blender-pipeline` 与工具路由；`unity-agent-workflows` 的 runtime-owner-proof、runtime-visible-output、runtime-numeric-proof、ai-workflows、serialized-persistence、project-structure-discovery、modular-architecture、unity-validation、cleanup-and-git。项目索引为 `UNITY_STRUCTURE.md`、`UNITY_STRUCTURE.runtime.md`、`UNITY_STRUCTURE.assemblies.md`。没有正式系统改动或跨程序集依赖；源 Blender 与 FBX 保持不变。按项目“查明根因、越界先停止”规则结束此轮。
