# D12.2 蓄澜水与地表资源包

2026-11-16。交付八个 Prefab、四段材质动画、湿／浅／深与露干材质、低潮头和共用灭火蒸汽，以及正式阶地上的六帧 Unity 资源样例。

[双分辨率视频与六帧](index.html) · [1080p 运行记录](evidence/1920/runtime-proof.txt) · [768p 运行记录](evidence/1366/runtime-proof.txt) · [素材来源与许可](source/README.md)

本包尚未绑定正式 Adventure 的 `CombatSurfaceView`，也未进行 V3 HUD 同屏验收。样例使用现有战斗命令产生真实结果，证明的是这些资源在该结果下可播放、状态可辨；统一 Demo 接线仍属 D12 集成。

## 交付与用法

Unity 根目录：`src/Assets/Art/VFX/Guanzhong/Jindan/Water/`。

| Prefab | 用途 |
|---|---|
| `WetMud` | 薄湿泥、局部湿亮，复用 WetShore 基色 |
| `ShallowWater` | 浅青水面，低幅双层滚动法线和稀疏细纹 |
| `DeepWater` | 深蓝水面，与浅水保持同一支承面、不同状态材质 |
| `WetToDry` | MudBank 基色的退水残湿，0.4 秒淡出至露干 |
| `TideRise`／`TideEbb` | 原水片上的低潮头与尾纹，0.4 秒掠过并消失；两者共用同一 Shader／泡沫纹理 |
| `ExtinguishSteam` | 每个真实火→湿格一次7粒淡蒸汽，最大存活0.55秒，供 D12.3 直接引用 |
| `LowPlantErode` | 原 Living 网格短促消退，0.28 秒归零，无残骸模型 |

`WaterSurface.shader` 使用官方样例泡沫和法线位图；`TideFront.shader` 将潮头裁在原六角足迹内。无顶点波浪、屏幕折射、流体模拟或额外 Renderer Feature。水面以颜色、细纹和湿亮区分状态，不升高水体或改变支承／占位。动画不写对象位置、旋转或缩放。

四段 Legacy Clip 在实例上自动播放并停留末帧。需要表现层决定的时点仍由结算驱动，不能让粒子寿命决定地表期限。销毁视觉实例时一并释放调用方创建的 Mesh／Material 副本。

## 接入约定

- 水片根放到 `CombatBattlefieldSurface.Center(cell)`，显示面为实际支承高度＋0.018米；潮头用＋0.023米，蒸汽发射点＋0.04米。沿用原六角 XZ 足迹。
- 首先读取冻结 `Changes`，实际成功结算后使用 `CombatSurfaceSettlement.Changes` 的 Before／After 驱动过渡。无变化的格不生成潮头、蒸汽或额外命中表现。
- `_Flow.xy` 是世界 XZ 单位方向；涨潮从源向外、退潮指向源，源本格按实际方向约定设置。水面的 `_Flow.z` 是纹理速度，默认0.12；仅为方向表现，不是战斗推力或水量。
- 湿／浅／深切换时同时插值 `_Depth` 与前后 `_BaseColor`，露干使用 `_Opacity`；不能只切终态材质造成颜色硬跳。样例采用0.4秒视觉过渡，规则在实际提交时已生效。
- 只在 **Burning→Wet** 生成蒸汽。首涨后低植仍存在；收到真实冲蚀销毁结果时才用 `LowPlantErode` 替换对应可见原件，尾效结束清理，退潮不复生。
- 强制推移保持真实朝向，向现有单位表现提供 `CombatUnitPresentationMovementStep(..., isForced:true)`。样例首涨被崖边阻挡；首退将水修从 `(4,5)` 推到 `(5,5)`，没有编造向外落点。
- 终止只清当前归属地表。被水改写的旧自然水不隐藏保存、也不恢复；集合外天然水保持。样例关闭正式视图序列化引用的28个静态水／岸线层，之后统一按实际快照显示水片。
- 原 WaterHex 只有7个顶点。对台阶只改这些顶点的 Y，会形成跨多个踏面的斜三角；在相邻曲坡的三角内部也实测约5毫米误差。本包 `Samples/WaterSampleSupport.cs` 复用正式 D3.3 阶梯的条带裁剪算法，按 `riserZ/riserY` 将**原水片运行时副本**分成独立水平踏面；曲坡复用正式 D3.2 的每半径12等分精度。两者均保留原 XZ 足迹，不改源 FBX、不增加立壁或碰撞。后续集成应沿同一支承数据采用此适配，不能继续只改7个顶点。

当前生产 `CombatSurfacePresentationSnapshot` 只含当前状态与预览数据，没有完整提交过渡；生产低植损毁仍映射 `Charred`。这两处正式绑定留后续集成，本包未修改共享 DTO、View、Builder 或正式场景。

## 六帧样例

打开 `Samples/WaterAssetSample.unity` 并进入 Play。样例从现有 Adventure 场景复制出独立场景，保留144格阶地、灯光与背景。镜头沿用38°、游戏内 size=3 缩放，另保存每个结果的 size=5 默认缩放图。

`WaterSampleRules` 使用 `JindanRehearsalFactory` 的第二段合法配置，经 `CombatEntryAdapter` 创建真实会话；以 `CombatCommandService.Preview/Execute` 执行两涨、两退与终止。玩家使用原正式棋子，行动间通过正常 EndTurn／AdvanceUntilAction 推进。没有手填水状态表、修改耐久或直接调用内部结算方法。初始燃烧格沿用当前生产橙色地表作参照，低火资源留 D12.3。

| 关键帧 | 潮源 `(5,5)` | 原燃烧低植格 `(4,6)` | 低植 |
|---|---|---|---|
| ① 施放前 | 浅水 | 燃烧 | 耐久15、燃料3 |
| ② 首涨中 | 浅→深 | 火→湿，短蒸汽 | 保留 |
| ③ 首涨后 | 深水 | 湿泥 | 耐久15、燃料3 |
| ④ 次涨后 | 深水，不在变化集合 | 浅水 | 冲蚀至0、燃料0、消退 |
| ⑤ 首退后 | 浅水 | 湿泥 | 不复生 |
| ⑥ 次退后 | 湿泥 | 干地 | 不复生 |
| 结束补图 | 干地 | 干地 | 不恢复旧自然水或低植 |

规则结算分别在 tick 10／20／30／40，动画按60Hz推进、视频30fps原速；展示暂停不推进战斗时间。视频只包含资源样例的相机画面，没有正式操作 HUD。

## 验证与复现

2026-11-16，Unity 6000.3.18f1／URP 17.3.0 编译、构建保存重开及最终1920×1080／1366×768两次运行均 PASS，关键帧人工查看通过。视频均为30fps、10.27秒。

| 检查 | 最终实际结果 |
|---|---|
| 六帧、终止与低植 | 两尺寸均与上表一致；冻结变化集合与回执一致，首退真实推移到源格 |
| 蒸汽同帧开关像素差 | 1080p 2166像素；768p 1160像素 |
| 四次潮头同帧开关像素差 | 1080p 36977–43767；768p 18703–22087像素 |
| 潮头／植物／露干尾效 | 动画结束后与隐藏效果的同帧比较，变化像素均为0 |
| 原静态水面 | 28个已序列化水／岸线层均停用；结束时源格实际露干 |
| 原七顶点阶梯适配 | 三角内部实测最大偏离0.125米，证明只测顶点不足 |
| 本包支承适配 | 每尺寸64008个三角内部取样，最大偏离0.0000370722米（0.0371毫米） |

坡面采用原 D3.2 的 N=12；其数学插值误差界为0.057116毫米，样例使用含浮点余量的0.1毫米阈值。三角内部取样配合裁剪／细分算法核对，不将有限取样称为全三角穷尽证明。首轮样例发现的静态水引用、强制推移标记及颜色硬切已修正；贴面问题在本包样例修正，生产绑定仍按上文留待集成。

构建入口保存后重新打开样例，检查关键引用持久化。样例核对冻结集合与提交回执一致、首涨保留低植、次涨冲蚀、逐级退水、实际推移落点、终止归属和集合外天然水；通过同帧隐藏效果做像素差，检查蒸汽／潮头实际可见与消退后无残留。贴面量测包含三角形内部，不能用旧顶点通过代替。

```powershell
$unity = 'C:\Program Files\Unity\Hub\Editor\6000.3.18f1\Editor\Unity.exe'
Start-Process $unity -WindowStyle Hidden -Wait -ArgumentList '-batchmode -projectPath D:\天章游戏开发\src -executeMethod TianZhang.Art.Jindan.Water.Editor.WaterAssetBuilder.Build -quit -logFile D:\天章游戏开发\src\Logs\d12-water-build.log'
Start-Process $unity -WindowStyle Hidden -Wait -ArgumentList '-batchmode -projectPath D:\天章游戏开发\src -executeMethod TianZhang.Art.Jindan.Water.Editor.WaterAssetCapture.Capture1920 -logFile D:\天章游戏开发\src\Logs\d12-water-capture1920.log'
Start-Process $unity -WindowStyle Hidden -Wait -ArgumentList '-batchmode -projectPath D:\天章游戏开发\src -executeMethod TianZhang.Art.Jindan.Water.Editor.WaterAssetCapture.Capture1366 -logFile D:\天章游戏开发\src\Logs\d12-water-capture1366.log'
```

同一工程不能同时开另一个 Unity 编辑器。JPEG 序列在忽略目录 `src/Logs/d12-water/<分辨率>/`，交付 MP4、关键帧与运行记录在本包 `evidence/`。取证后运行本包 `export-video.ps1`，使用本机已有 imageio-ffmpeg 输出视频。

本次未改变战斗规则、CSV、共享材质、渲染配置、正式场景、RockCluster 或角色资源；样例与 Editor 工具均在本包非自动引用的独立程序集内。工作流参考：runtime-owner-proof、visible-object-identity、asset-source-lock、serialized-persistence、ui-and-visual-assets、project-structure-discovery、modular-architecture、ai-workflows、unity-validation；项目无 `UNITY_STRUCTURE*`，以当前源码、原资源和计划定向核实。
