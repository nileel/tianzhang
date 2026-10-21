# V1 · 关中战场环境分层素材

2026-10-21。沿用[已接受参考](../interaction-preview-20261021/reference-approved-layout.png)的山谷、山雾、左上暖光与青灰冷影。交付范围是可导入 PNG 与摆放起点；Unity 场景接入和真实运行验收属于 V2，实机光色统一属于 V3。

以下 V1 推导保留为素材制作依据；V2/V3 已完成，当前正式摆放与参数以文末 V3 节及 Unity 资源为准。

## 素材与导入

| 文件 | 实际尺寸／通道 | 用途与边缘 |
|---|---|---|
| [01-distant-canyon.png](01-distant-canyon.png) | 1672×941，RGB | 全幅远山／峡谷，中心较淡，四边填满；没有棋盘、角色、近景角片或 UI |
| [02-foreground-left.png](02-foreground-left.png) | 1536×1024，RGBA | 左侧松枝／岩石；内缘透明，左边和底边有意截出画布 |
| [03-foreground-right.png](03-foreground-right.png) | 1536×1024，RGBA | 右侧松枝／岩石；内缘透明，右边和底边有意截出画布；不是左图镜像 |
| [04-valley-mist.png](04-valley-mist.png) | 1536×1024，RGBA | 独立崖底雾，四边近全透明，实际雾芯较浓，材质 Alpha 从 0.30 起调 |

使用内置 `image_gen`；完整输入见 [PROMPTS.md](PROMPTS.md)。PNG 为原始输出，未插值放大；远景不是 4K。检查现有 `generated-scene-art/guanzhong-first-bounty` 与 `src/Assets/Art/Environments/Guanzhong` 后，没有发现独立可复用的山雾／山谷／松枝图层。现有 Tile、RockCluster、角色和攻击特效继续复用，不从效果图提取棋盘或重做模型。

V2 可将这四张 PNG 导入 `src/Assets/Art/Environments/Guanzhong/` 的独立环境素材子目录，由 Unity 正常生成 `.meta`。建议初始导入：Texture Type=Default、sRGB 开、NPOT=None、Max Size=2048、Wrap=Clamp、Filter=Bilinear、Mip Maps 开；RGBA 使用输入 Alpha 且 Alpha Is Transparency 开，先用无压缩核对边缘，再决定压缩。采用现有支持纹理的 Unlit 材质：背景 Opaque，其余 Transparent／普通 Alpha Blend、ZTest LEqual、ZWrite Off，不使用 Additive，不对颜色再次预乘 Alpha。装饰不投射阴影、无 Collider，不添加可接收点击的 UI Graphic。

## 分层构图与摆放起点

单独建立 `GuanzhongEnvironment` 根物体，与 `GuanzhongBattlefield` 平级。当前 `FitCameraToBattlefield` 和 `AdventureCameraController.ReadBattlefieldBounds` 都汇总战场子树的 Renderer；装饰挂入该子树会扩大自动取景和平移范围。保持已禁用的旧 `VisualBackdrop` 不启用，其旧平面会切穿崖体。

以下数值来自当前保存场景、源码与既有地砖测量的推导，**不是本轮 Unity 运行实测**。镜头正交、俯角 38°、无偏航，S=6.3617754，初始位置 C0=(6.7289762,8,-4.889916)，forward=(0,-sin38°,cos38°)，远裁面 60。100 格格心 `(q+0.5r,0.34,0.8660254r+1)`，q/r=0…9；Renderer X 约 [-0.84457,14.30252]，Z 约 [0.221975,9.606903]，地砖顶约 0.43394、崖底约 -1.54311。

| 层 | 摆放起点（Unity 世界单位） | 合成与覆盖 |
|---|---|---|
| 远景 | 中心 `C0+35*forward`，约 `(6.729,-13.548,22.690)`；宽 40、高 22.51196 | 与相机成像面平行、正面朝镜头，按原图比例；固定在世界中，处于整个棋盘后方 |
| 左近景 | 与相机成像面平行；宽 18、高 12；中心 X=-4、Z=0.5，Y≈-1.152 | 可见内缘 X≤-2.055，与整个棋盘 X 最小边至少隔 1.21；左侧裁切边 X=-13，置于全 pan 可视域之外 |
| 右近景 | 与相机成像面平行；宽 17、高 11.33333；中心 X=18、Z=0.5，Y≈-1.575 | 可见内缘 X≥15.078，与棋盘 X 最大边至少隔 0.776；右侧裁切边 X=26.5，置于全 pan 可视域之外 |
| 崖底雾 | 水平 XZ 面，朝上；中心约 `(6.729,-1.30,7.014)`；宽 40、深 33 | 位于地面以下；由不透明地砖的深度挡住顶面，不作屏幕全覆盖雾。先单层、Alpha=0.30，远景负责雾透明区域的底色 |

卡片 UV 向右对应世界 +X，图像向上对应相机 up；不要左右镜像，否则受光方向会反转。上述宽高是最终世界尺寸，不能直接套给 Unity 默认 10×10 Plane 的 Transform scale。两张近景底边的投影高度统一取 U=-6.6，其中 `U=cos38°*y+sin38°*z`；精确中心 Y 可由 `(U底+卡片高/2-sin38°*Z中心)/cos38°` 计算。近景矩形的透明部分可以伸入视野，可见枝叶不进入战场投影。

覆盖依据：两种分辨率中较宽的比例为 1366/768。全 pan 的 X 可视并集约 [-12.160,25.618]、最低投影 U≈-5.957，近景的外侧和底部裁切边均留在并集外至少 0.5，避免平移后露出硬切边。近景内缘按原图 Alpha>8/255 的轮廓计算；透明区极低 Alpha 残余不作为实体边缘。当前镜头方向固定，平移不会改变近景与棋盘的相对 X 投影间隔。

远景最低覆盖公式：`W≥2*S*aspect+ΔBx+2m`、`H≥2*S+sin38°*ΔBz+2m`，取 m=0.5，1920×1080 需要 38.767×19.501，1366×768 需要 38.778×19.501；40×22.512 均覆盖。若 V2 的实际 bounds 或 Fit 结果变化，按同一公式重算，不扩大相机边界来迁就装饰。水平雾中心 Z 为 `Bcenter.z+(0.34-yFog)*cot38°`，所需全视域深度约 31.051；雾图透明边缘不负责填满背景。

## 光色与 UI 留白

主光从画面左上来，山脊／松针亮边偏淡金，右侧岩面偏青灰；背景远处低对比、近景最深，战场主体应比近景清楚。可沿用当前主光旋转 `(48,-32,0)`、颜色 `(1,0.93,0.82)`、强度 1.1 作为 V2 起点；这些不是 V3 的最终调色值。图片已含绘制光色，Unlit 层不再重复叠加主光；V3 调整现有地砖、岩石、角色材质，使其与图片一致。

近景固定在棋盘外侧，不固定在屏幕角上，避免极端平移时盖住地格。中央留给 100 格棋盘；顶部保留地点／悬赏，左侧保留资源卡／行动轴，底部保留动作区，右侧保留详情与预览。所有装饰在场景层，现有 Screen Space Overlay HUD 保持最上层。不要将 UI 或角色烘入环境图，也不新增天气、后处理或完整三维山谷系统。

## 本项检查与后续验收

- 四张 PNG 解码／完整性通过；三张 RGBA 存在真实透明像素。左右图顶部／内侧边 Alpha 最高为 0／1；雾四边最高为 2/255。浅底、深底和山谷底三组目视检查未见黑色底块或明显硬边，雾以 0.30 透明度检查。
- 双分辨率的全 pan 背景覆盖、近景与棋盘间隔、近景裁切边不进入全 pan 可视域的数值核对通过。检查原始记录在仓库本机 `Logs/v1-environment-assets/validation.json`、`alpha-preview.png`；这些是素材／推导证据，不是 Game View。
- 已读取 `UNITY_STRUCTURE.md`、`UNITY_STRUCTURE.ui.md` 及 Unity 技能的 `ui-and-visual-assets.md`、`ai-workflows.md`；实际镜头入口核对 `AdventureSceneBuilder.ConfigureAdventureCamera/FitCameraToBattlefield`、`AdventureCameraController` 和 `AdventureScene.unity`。本项没有修改 `src/`、战斗规则或其他系统。
- V2 仍须用真实 Game View 检查 1920×1080／1366×768 初始及 pan 四角：无露底、穿插、关键格遮挡、点击拦截，背景采样清晰度可接受；Builder 重建与保存重开一致。近景雾的材质排序、深度效果与上下文亮度必须以实机为准，不把本素材检查当作 V2/V3 完成。

## V3 实机光色定稿（2026-10-21）

实际场景为 `src/Assets/Scenes/AdventureScene.unity`。V2 已按试玩反馈改为相机子树 `Main Camera/GuanzhongEnvironment`，四层随镜头固定；远景 z=35，左右近景 z=4，雾 z=3.5、中心 y=-4.2、尺寸 34×7.5。V3 保持这些摆放、100 格棋盘、正交尺寸 6.3617754、网格、碰撞和 HUD。

颜色为 Unity 材质／光照属性的输入 RGB；没有改全局 URP、换 Shader、加体积雾或后处理。

| 项目 | V2 基线 | V3 最终值 |
|---|---|---|
| 主光 Euler | (48,-32,0) | **(42,132,0)**，实际屏幕来光左上，投影朝右下 |
| 主光 RGB／强度 | (1,.93,.82)／1.1 | **(1,.89,.72)／1.15** |
| 阴影 | Hard，强度 .85，实际采用全局 bias 1／normal 1 | **Hard，强度 .78，局部 bias .12／normal .18**；`UniversalAdditionalLightData.usePipelineSettings=false` |
| Trilight sky | (.30,.38,.48) | **(.58,.63,.72)** |
| Trilight equator | (.20,.25,.31) | **(.50,.55,.64)** |
| Trilight ground | (.09,.10,.12) | **(.25,.28,.34)** |
| Tile `_BaseColor`／`_Smoothness` | (1,1,1)／.15 | **(1,.90,.72)／.08**；顶面和崖壁仍共用原材质与贴图 |
| RockCluster `_BaseColor`／`_Smoothness` | (1,1,1)／1 | **(.90,.93,.98)／.35**；保留原法线与金属／光滑度贴图 |
| 左右近景 `_Color` | (1,1,1,1) | **(.78,.82,.88,1)** |
| 雾 `_Color` | (1,1,1,.50) | **(1,1,1,.42)** |

远景保持白色 tint。玩家、石甲兽、裂石甲兽沿用原 Lit 材质和纹理，裂石甲兽原蓝灰染色保留；同一暖主光与冷补光作用于三者。`ambientIntensity=.9` 作为原序列化值保留，Trilight 实际亮度由上述三色控制。实测水平 ambient SH（linear）为 (.2047,.2509,.3486)，修正光向后的背光面保留可辨细节；没有手写 SH 或添加额外灯。

持久化 owner：`GuanzhongEnvironmentBuilder.ConfigureLighting/EnsureMaterial` 负责灯光与装饰 tint；`GuanzhongTerrainAssetBuilder.BuildAsset/BuildRockClusterAsset` 负责地块与岩石材质默认值。对应 `.mat`、正式场景和 Builder 已同步，重建不会恢复旧色。

### 实机对照与验收

本机 [双分辨率对照页](../../../../src/Logs/v3-lighting/compare.html) 使用完整 `ScreenCapture` 输出，含真实 HUD；截图没有重绘、调色或裁切。

| 分辨率 | 调整前 | 调整后 | 移动后 |
|---|---|---|---|
| 1920×1080 | [V2 基线](../../../../src/Logs/v3-lighting/before-1920.png) | [V3 初始](../../../../src/Logs/v3-lighting/initial-1920.png) | [第三段落点](../../../../src/Logs/v3-lighting/move-3-1920.png) |
| 1366×768 | [V2 基线](../../../../src/Logs/v3-lighting/before-1366.png) | [V3 初始](../../../../src/Logs/v3-lighting/initial-1366.png) | [第三段落点](../../../../src/Logs/v3-lighting/move-3-1366.png) |

- Unity 编译与 `tools/check-unity-assembly-boundaries.ps1` 通过；`GuanzhongEnvironmentTests` 3/3，验证局部阴影、材质、环境重建、保存重开与完整光色快照。最终保留原环境对象 ID 后，两项只读场景检查通过。
- 两项 PlayMode 通过：1920×1080／1366×768 初始与平移四角；正式生产移动三段 `(0,0)→(2,0)→(1,2)→(2,3)`，跨回合，每段实际报价及结算均 50 AP，模型根落点误差小于 .001m。保留每段截图、移动中截图及 `runtime-{1920,1366}.txt`，不注入位置／AP，不把测试业务入口称为物理鼠标操作。
- 目视检查草面纹理、岩石接触阴影、角色脚下、两敌冷蓝／暖褐区别、深冷崖壁和淡远景；全棋盘初始可读，近景不压住初始关键格。平移截图仍沿用 V2 的完整屏幕 HUD 与相机边界，未做 V4–V8 UI 改造。
- XML 位于仓库 `Logs/v3-environment-editmode.xml`、`v3-lighting-final-playmode.xml`、`v3-final-serialized.xml`。图片与临时测量资料位于本机忽略的 `src/Logs/v3-lighting/`，不是重新生成的美术原图；未运行全量测试，最终体验待用户试玩。
