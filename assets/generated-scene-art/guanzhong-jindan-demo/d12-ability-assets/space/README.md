# D12.4 定尺与封关空间资源包

## 2026-11-17 重做：空间波与能量膜

已按用户确认方向完成空间波、两层能量膜、受击与消散资源及Unity样例重做。新旧运行证据分开保存。

[新版默认镜头视频与关键帧](index.html) · [新版1080p记录](evidence/redesign-20261117/1920/runtime-proof.txt) · [新版768p记录](evidence/redesign-20261117/1366/runtime-proof.txt)

- 定尺保留地面短／长刻度，增加 0.9 米高透明波面：压缩波从两端向中心收拢，扩张波从中心向两端传播，波纹局部弯曲。既有格心和真实改距规则不动。
- 封关保留地面单向箭头，增加外紫／内金两张 1.1 米高的半透明多边形膜。薄膜是无碰撞的透明 VFX 载片，斜向跨过同一有向边，使固定镜头仍能看到膜面；不是新增实体墙或传送门。各膜保持淡底、明亮边沿和向外传播的多边形波纹。
- 展开与已提交命中写入实例材质 `_EventTime`／`_EventKind`；三层短促多边形波纹叠加反馈。外破只消散外膜，内膜继续阻挡表达；破尽消散内膜并保留断碑。定尺受损仍保留空间波，实际结束时才消散。
- `_Layer=0` 为原贴地带、`_Layer=1` 为能量载片；`_Shimmer=0` 关闭自身图案的局部扭曲，保留传播和静态用途纹样。**没有采样或扭曲背景**，未启用 Opaque Texture、Depth Texture 或 Renderer Feature。
- `_Opacity` 只控制视觉淡出；规则立即按真实命令生效。样例用 30 帧／0.5 秒消散，并在透明度归零时同帧比较隐藏前后像素以检查尾效残留。

新的默认镜头为俯角 38°、正交 size5，视频全程使用该尺寸；每个稳定状态另存 size3 的 `-detail.png` 近景。新取证路径为 `evidence/redesign-20261117/<1920|1366>/`，帧序列为 `src/Logs/d12-space/redesign-20261117/<宽度>/`。原 `evidence/1920`、`evidence/1366` 原地保留。

既有 Builder／Capture 入口保持。新增运行取证将记录每个地面带／能量膜的实际对象、场景、父级、网格、世界 bounds、`WorldToScreenPoint` 投影包围矩形、材质／shader、事件时间与 opacity；分别测完整效果和能量子层的默认镜头像素贡献。保留真实费用、受损不失效、两层封关、反向通行及144格不位移断言。可见像素只证明资源确实绘制；仍须逐图核对层次、人物与落点可读性。

运行 owner 仍是本包 `SpaceAssetSample`。生产 View、共享资源、规则、数据和渲染配置未改；正式 V3 HUD 接线留 D12 集成。

本轮验证：Unity 6000.3.18f1编译、重建保存重开、1920×1080与1366×768真实命令样例均PASS。定尺能量层默认镜头贡献6174–6239／2931–2970像素，封关两膜4929–5100／2491–2571像素；世界包围、屏幕投影和实际可见像素范围均记录。实际同边费用10.416／41.666AP；外层0、内层80时仍需2步绕行，破尽后直接1步，反向通行保留。四段消散归零后像素残留均0，144格格心位移0。已核对双层、受击、外破、破尽和压缩／扩张关键帧；默认镜头运动短片30fps、25.37秒。用户最终画面审阅与正式HUD集成仍待后续。

References loaded：runtime-owner-proof、unity-validation、ui-and-visual-assets、ai-workflows；Project maps loaded：UNITY_STRUCTURE.md、UNITY_STRUCTURE.content.md，结合当前源码和独立asmdef核实。未新增公共接口、运行管理器或真实背景折射。

## 旧版交付与验证记录（重做前历史）

2026-11-17。9个Prefab、材质与独立样例已完成，Unity构建、保存重开、1920×1080／1366×768真实命令驱动的资源样例均PASS。

[双分辨率原速短片与关键帧](index.html) · [1080p运行记录](evidence/1920/runtime-proof.txt) · [768p运行记录](evidence/1366/runtime-proof.txt)

Unity 路径：`src/Assets/Art/VFX/Guanzhong/Jindan/Space/`。正式 `JindanScaleView`、`JindanSealView` 与统一 V3 HUD 尚待 D12 集成绑定。

### 交付

| Prefab | 用途 |
|---|---|
| ScaleCompressed | 压缩短刻度；原边距离单位1 |
| ScaleExpanded | 扩张长刻度；原边距离单位4 |
| ScaleSupport / ScaleSupportDamaged | 原支点碑，受损灭微光但刻度继续生效 |
| SealOuter | 外层断续门沿纹样 |
| SealInner | 内层连续单向箭头；外破后仍显示 |
| SealSupport / SealSupportOuterBroken | 原碑微光／外破无微光两态 |
| SealFailed | 原无光断碑，固定在原支点保留 |

带网格本地 +X 指向目标格，长度1米、宽0.44米；样例按真实格心定位并把覆盖网格逐点贴到支承面 +0.035米。样例只使用正式上层平地两条边。更复杂坡阶上的正式接线应沿实际支承面适配，不把本样例当全地图贴面证明。

`SpaceBand.shader` 不动顶点、不读取背景；`_Pattern` 分别为0短刻度、1长刻度、2外层、3内层。`_Shimmer=0` 可关闭局部亮度微扰，形状仍在；真实背景折射仍留集成评估。`_Opacity` 可供表现层过渡使用，状态有效期始终读取真实规则。

支点模型保留原 `Intact/Body`、`Intact/Glow`、`Failed/Body`。定尺受损仅关闭 Glow；结束／耐久归零关闭刻度并恢复真实距离。封关外破关闭 Outer、灭碑微光，Inner保留；内破关闭 Inner，显示 Failed。不得用网格碰撞或移动格心实现规则。

### 样例与验证

`SpaceAssetSample` 在独立复制场景中创建第三段合法会话，通过生产 Preview/Execute 驱动：

1. 同一 `(5,14)→(6,14)` 边分别展开压缩、扩张，实际贯石击使支点200→170；低微光熄灭，刻度与真实距离仍在。
2. 两次都让正式棋子实际走过这条边，记录已提交行动点与位置；压缩用七次攻击击毁，扩张用正式关闭命令结束，恢复正常距离单位2。
3. 固定封关 `(8,12)→(9,12)` 展开，两层纹样并存；正式棋子绕行、反向穿过；四击后外0／内80仍阻挡，七击后开通，棋子实际穿过并保留无光断碑。
4. 冻结同帧隐藏各层做像素差，保存关闭微扰关键帧，核对全地图144格中心无位移；关键帧同时保存游戏内近看size3与默认size5镜头。

样例提供1920×1080／1366×768相机画面与30fps原速帧序列，证明资源可用和规则结果下的表达。不是正式 HUD 输入操作、正式 View 绑定或最终 Demo 验收。

两尺寸均取得真实跨边移动费用：压缩10416、扩张41666内部AP单位（1000单位=1AP，即10.416／41.666AP）；中心位置144格最大位移0。封关完整与外破状态的绕行路径均为2步，强移路径0步；内破后直接1步、强移1步。关闭微扰后短／长刻度及单向箭头仍可辨，外0／内80与破尽断碑关键帧已逐图检查。1080p压缩／扩张同帧可见差954／1756像素，768p为504／953；两封层分别1793／1076与876／596像素。无缺失材质，结束后边纹撤去、固定断碑保留。每个短片683帧、30fps、22.77秒。

### 复现入口

Unity 6000.3.18f1，关闭同一工程的其他编辑器。顺序批处理调用：

- `TianZhang.Art.Jindan.Space.Editor.SpaceAssetBuilder.Build`，带 `-quit`。
- `TianZhang.Art.Jindan.Space.Editor.SpaceAssetCapture.Capture1920`，不带 `-quit`。
- `TianZhang.Art.Jindan.Space.Editor.SpaceAssetCapture.Capture1366`，不带 `-quit`。

Builder 保存并重新打开 `Samples/SpaceAssetSample.unity` 验证引用；Capture 在 Play Mode 完成后退出。帧序列为 `src/Logs/d12-space/<宽度>/%04d.jpg`，截图和运行证明为本目录 `evidence/<宽度>/`。请以实际日志的 PASS 与画面检查记录作为完成证据。

[素材来源](source/README.md)。运行所有者为本包 `SpaceAssetSample`，仅复用生产命令和棋子 provider；没有修改战斗规则、CSV、共享材质、正式场景或生产 Views。已加载项目结构图与 `unity-agent-workflows` 的 runtime-owner-proof、asset-source-lock、visible-object-identity、unity-validation，并沿原 Water 包的保存与取证方式。
