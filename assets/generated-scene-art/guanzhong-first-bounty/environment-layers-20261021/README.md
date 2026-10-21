# V1 · 关中战场环境分层素材

2026-10-21。沿用[已接受参考](../interaction-preview-20261021/reference-approved-layout.png)的山谷、山雾、左上暖光与青灰冷影。交付范围是可导入 PNG 与摆放起点；Unity 场景接入和真实运行验收属于 V2，实机光色统一属于 V3。

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
