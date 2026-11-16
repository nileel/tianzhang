# D12.1 厚载显化与重力资源包

2026-11-16。交付六个可播放 Prefab、四段显化动画、五份材质、同遮罩四 Pass Shader、两张 CC0 纹理及 Unity 资源样例。仅制作本包；正式 Adventure 接线、战斗命令触发和 V3 HUD 共存验收仍属 D12 集成。

[视频预览](index.html) · [1920 运行记录](evidence/1920/runtime-proof.txt) · [1366 运行记录](evidence/1366/runtime-proof.txt) · [源资源量测](evidence/source-inspection.txt)

## 交付物

Unity 根目录：`src/Assets/Art/VFX/Guanzhong/Jindan/Earth/`。

| 预制体 | 行为和用法 |
|---|---|
| `Prefabs/WallForm.prefab` | 原高墙完整网格，0.35 秒自下向上显现，短尘，保持完整状态 |
| `Prefabs/WallDissolve.prefab` | 同网格反向消退，0.35 秒归零，尘散后没有碎障 |
| `Prefabs/RampForm.prefab` | 原单格临时窄坡，0.35 秒显现；不改变顶点、根变换或接口 |
| `Prefabs/RampDissolve.prefab` | 同坡反向消退及四处沿原坡面的短尘 |
| `Prefabs/GravityPressure.prefab` | 四个体外细痕发射点，世界方向向下；挂在真实移动的单位根下 |
| `Prefabs/LandingDust.prefab` | 实际接地后，在真实脚底位置播放一次低尘 |

`Animations/` 四个 Legacy Animation Clip 仅写 `MeshRenderer.material._Progress`，0→1 成形、1→0 消退；实例自动播放并保持末帧。无位置、旋转、缩放曲线。每个资源的源网格仍直接引用原 FBX，未复制／改造最终模型。

材质为 `WallStone0`、`WallStone1`、`RampEarth`、`SoilDust`、`PressureTrace`。`EarthManifest.shader` 的 Forward、ShadowCaster、DepthOnly、DepthNormals 共用对象空间高度＋噪声遮罩，无顶点位移。坡沿用原 RampGround 的土色与碎砾算法，墙引用原贴图并降低色度。项目现有土石材质由 HLSL 实现，故直接在本包派生；没有为此导入 Shader Graph 样例依赖、NOVA 或修改渲染配置。

## 必须保留的接入约定

- 墙根：相邻两格格心中点，Y 取较低格；forward 为 A→B。效果放在 `Intact` 下，兼容 `CombatEarthView.Present` 只启用该直接子节点的现有约定。
- 坡根：低端格心，forward 为低→高；沿现有表现适配 `scale=(1,实际高差/0.75,1)`。局部宽 0.5 米、z=0.1→0.5，顶面 `0.75*(1-cos(pi*t))/2`，无额外缩放动画。
- 显化解除时，后续集成方在原实例的最终变换播放对应 Dissolve 预制，再清理视觉对象。规则对象立即按真实结果解除；尾效不应继续提供碰撞、通行或拾取。尘全部结束后销毁视觉实例，本包不另设管理器。
- 重力只观察正式单位运动，不写单位位置。开始下降时播放 Pressure；实际运动结束时 `Stop(false, StopEmitting)`，保留约 0.14 秒细痕尾部后清理。不要因粒子时长改变输入锁或下落速度。
- 只有真实结果为接地才播放 LandingDust；高→低不播放落地尘。解除不自动回升。若单位或会话被隐藏／退出，集成方同步清理附属效果。
- 显化和重力属于二选一合法构筑。样例逐段展示，未把二者同时装给同一角色。

## 样例与验证

打开 `Samples/EarthAssetSample.unity` 并进入 Play：依次播放墙、坡、高→低、空→地。样例从现有 Adventure 场景保存为独立资源演示，保留 144 格阶地、现有灯光和背景；采用原 38° 镜头、游戏已有的 size=3 缩放，另保存墙坡 size=5 的默认缩放关键帧。

样例使用现有 `Static3DCombatUnitPresentationProvider` 创建正式默认棋子。高→低／地面通过 `Present → CombatUnitFlightVisual.Transition`，速度仍为 4 米／秒；不是另一套角色位移动画。墙边取已验收的 `(4,1)→(4,2)`，坡取 `(7,8)→(7,9)`；重力源／目标使用第二段演武的 `(4,4)/(4,5)` 支承格。

2026-11-16 在 Unity 6000.3.18f1、URP 17.3.0 中编译、构建、保存后重开并运行，1920×1080／1366×768 两次均 PASS：

| 检查 | 实际结果 |
|---|---|
| 原坡导入网格 | 692 个导入顶点（含法线／UV 分裂），644 三角；共享原 Mesh |
| 六向 × 0.375/0.75 米坡高 | 4,368 个顶面样本，最大误差 0.000000328 米 |
| 成形／消退 | 各 0.35 秒；根及网格变换前后一致 |
| 消退后颜色画面 | 墙、坡与相同空场平均 RGB 差均为 0，未留可见表面或阴影 |
| 粒子确实被渲染 | 同帧开关粒子的像素差：1080p 墙250／坡518／压低841／压落802；768p 为131／270／423／396 |
| 高→低 | 固定 60Hz 实际12帧结束，最终脚底离地0.750000米；保留离地投影 |
| 高→合法地面 | 实际23帧结束，最终脚底偏差0；关闭离地投影并播放短尘 |
| 清理与解除 | 尘／细痕均结束，解除后高度未回升 |

深度／法线 Pass 共用遮罩由源码核对；颜色对照不冒充深度缓冲实测。原图为相机渲染的资源样例，未包含正式 HUD、战斗操作或新资源的生产命令绑定，因此不宣称 D12 集成或 V3 HUD 遮挡验收完成。当前细痕刻意克制，最终同屏观感仍在集成阶段核对。

取证入口（同一工程不能同时开另一个编辑器）：

```powershell
$unity = 'C:\Program Files\Unity\Hub\Editor\6000.3.18f1\Editor\Unity.exe'
Start-Process $unity -WindowStyle Hidden -Wait -ArgumentList '-batchmode -projectPath D:\天章游戏开发\src -executeMethod TianZhang.Art.Jindan.Earth.Editor.EarthAssetBuilder.Build -quit -logFile D:\天章游戏开发\src\Logs\d12-earth-build.log'
Start-Process $unity -WindowStyle Hidden -Wait -ArgumentList '-batchmode -projectPath D:\天章游戏开发\src -executeMethod TianZhang.Art.Jindan.Earth.Editor.EarthAssetCapture.Capture1920 -logFile D:\天章游戏开发\src\Logs\d12-earth-capture1920.log'
Start-Process $unity -WindowStyle Hidden -Wait -ArgumentList '-batchmode -projectPath D:\天章游戏开发\src -executeMethod TianZhang.Art.Jindan.Earth.Editor.EarthAssetCapture.Capture1366 -logFile D:\天章游戏开发\src\Logs\d12-earth-capture1366.log'
```

样例60Hz推进，视频30fps；原始JPEG序列在忽略目录 `src/Logs/d12-earth/<分辨率>/<序列>/`，导出的MP4与关键帧在本包 `evidence/`。构建工具只写本包，不覆盖 AdventureScene；样例代码、Editor工具均使用独立且不自动引用的程序集。

## 素材与边界

- 墙：`src/Assets/Art/Environments/Guanzhong/Props/HighWall.prefab` 的 `Intact/Body`，原 `WeatheredStone/StoneEdge` 材质和源贴图。
- 坡：`src/Assets/Art/Environments/Guanzhong/EarthManifest/EarthManifestRamp.prefab` 及其 FBX，原 `Ramp/Earth.mat`。未使用24格常驻长坡替代。
- 粒子：仓库已保存的 Kenney Particle Pack 1.1，`PNG (Transparent)/smoke_03.png` 和 `trace_01.png`；原文件在 [source](source/)，许可见 [CC0](source/Kenney-CC0.txt)。原ZIP路径与来源仍见 `assets/combat-vfx/kenney-particle-pack/README.md`。未生成新位图，无 Imagegen 提示词；未使用 Tripo、未新增消费。
- 正式规则、共享材质、共享 Builder/View、正式场景及已有 RockCluster／角色改动均未修改。地图、灯光、角色只作为样例引用。
- 事实源：开发计划 D12.1、V2 能力分镜、V3 制作分工、`CombatEarthView`、`CombatUnitFlightVisual`、`CombatBattlefieldSurface`、D7 已有取证。
- 工作流参考已加载：runtime-owner-proof、visible-object-identity、asset-source-lock、serialized-persistence、ui-and-visual-assets、project-structure-discovery、modular-architecture、ai-workflows、unity-validation。项目无 `UNITY_STRUCTURE*`，以当前代码／场景和本计划定向核实；无共享运行模块职责增加。
