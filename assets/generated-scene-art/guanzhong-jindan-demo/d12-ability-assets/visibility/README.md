# D12.5 阳照与藏真公开支点及范围特效资源包

2026-11-17 初交付日／月公开坛各三态，共6个Prefab，19.43秒旧样例仅表现公开坛，遗漏领域覆盖。按同日用户反馈补交 `YangDomainRange`／`YinDomainRange` 两个全棋盘范围Prefab，共8个Prefab。默认镜头size5，关键帧另存size3近景与全图镜头；最新证据单列 `evidence/range-20261117/`，原始证据保留。Unity编译、保存重开与双分辨率实机样例通过；最新视频各793帧、30fps、26.43秒，完整解码通过。

[最新视频与关键帧](index.html) · [1920运行记录](evidence/range-20261117/1920/runtime-proof.txt) · [1366运行记录](evidence/range-20261117/1366/runtime-proof.txt) · [范围贴面量测](evidence/range-20261117/range-geometry-proof.txt) · [素材来源](source/README.md)

这是资源交付与独立样例。正式 `JindanVisibilityView`、`AdventureSceneInstaller`、V3 HUD及自建角色流程未接线，用户最终画面审阅仍待后续。

## 资源与使用

Unity目录：`src/Assets/Art/VFX/Guanzhong/Jindan/Visibility/`。

| Prefab | 状态 |
|---|---|
| YangDormant / YinDormant | 原完整碑，暗日纹／月牙可识别，微光关闭 |
| YangActive / YinActive | 原完整碑，暖金日芒／冷蓝月牙与克制局部微光 |
| YangDestroyed / YinDestroyed | 原失效断碑，无纹样微光 |
| YangDomainRange | 全棋盘暖金连续线及轻微流光，标示阳照覆盖 |
| YinDomainRange | 全棋盘冷蓝内圈断续线及轻微流光，标示藏真覆盖 |

直接复用D12.4 `ScaleSupport.prefab` 的原石碑完整／失效网格及材质。Builder只在本包副本中替换 `Intact/Glow`，不修改Space、原Props或正式场景。纹样为原创局部HLSL，无新增位图、外部依赖或最终模型。

层级保留 `Intact/Body`、`Intact/Glow`、`Failed/Body`。新增 `Intact/Identity` 为未展开时的暗纹，`Intact/Glow/Glyph` 为日／月亮纹，`Intact/Glow/LocalGlow` 为局部微光。透明载片无碰撞、无投影、不采样背景、不改变原碑尺度或接地。纹样尺寸由原碑bounds取得：本样例0.1794米，沿原碑前面定位；默认1080p投影约19.38×15.27像素，768p按同镜头比例缩放。实际对象、世界bounds、屏幕矩形、材质与来源记录在运行证据中。

后续View继续从真实席位投影：未展开显示Intact并关闭Glow；有效实例打开Glow；`LastEndReason == "jindan_support_destroyed"` 时关Intact、开Failed。领域自然结束应回完整无光态，不能误用Destroyed。冲突输赢不会改变两坛的有效状态。所有微光附着公开坛，禁止转挂隐者或用于定位隐者。

初交付运行记录中的 `/Intact/Glyph` 是省略Glow父层的短标签，真实对象层级为 `/Intact/Glow/Glyph`；最新记录已使用完整层级。

## 全棋盘范围补充

两种领域均覆盖本演武地图全部144个基础拓扑支承格，包括不可停格。阳照照明适用于全部粗阵营；藏真保护仅适用于施术方粗阵营存活角色。范围纹表达领域空间覆盖，不等于观察者视野、实际可见者或当前隐匿者集合；阳照不加视距、不穿墙。依据为[日坛昭明](../../../../../docs/角色养成/演示原型/神通/散修/演武·日坛昭明.txt)和[朔坛藏真](../../../../../docs/角色养成/演示原型/神通/散修/演武·朔坛藏真.txt)原规则。旧V2“只在公开坛附近增加微光”的表现文字已同步纠正。

两个范围Prefab共用 `WholeBoardRange.asset`，按本张固定关中地图的 `CombatBattlefieldSurface` 烘焙，须以世界零位、单位缩放、无旋转装配；不挂在石碑或人物下随其移动。UV0是每格局部XZ，UV2记录固定q/r；样例用生产 `SceneObjects.Board.Cells` 核对集合完全相等。每域各一个Renderer、共用网格；本资源不宣称适配其他地图，更换地形需按实际支承面重新烘焙。

`DomainRange.shader` 用两条错开的连续／断续纹辅助暖冷颜色识别，范围内仅有低透明流光。流光只读取固定网格坐标与表现秒数，不读取人物、观察结果、目标或施术者位置；角色隐匿不会导致范围挖空、移动、增强或追踪。不开启全局折射、Depth/Opaque Texture，不改变世界几何、碰撞、通行或距离规则。

样例唯一运行所有者仍为 `VisibilityAssetSample`，`Present()` 分别按本方 `seat.ActiveVisibility != null` 开关范围。冲突阳胜／阴胜／中性、近距自然现形及退隐均保留两层；日域合法关闭只清日纹，月域真实到期后清月纹；另起会话验证月域合法关闭及重置清理。公开碑失效的三态夹具单独标注，范围也随夹具关闭，不据此宣称真实破坛完成。

载片较原面抬高0.025米。坡面沿现有12等分算法，石阶按每个踏面条带裁剪；严格三角形内部采样251072点，最大高度误差0.00003817米，无跨踏面斜片。实际网格187450顶点、62768三角形，两个Prefab引用同一网格。运行记录包含原地图144格格心、范围实例、材质、世界bounds、屏幕投影及同帧关掉单层时的像素差，结合默认镜头和全图关键帧核对覆盖与可读性。

双分辨率默认镜头下日／月范围均可辨，单层同帧开关分别影响362574／379662像素（1080p）、185374／193141像素（768p）；这仅证明可见贡献，结合关键帧确认角色、公开坛与地面仍可读。关闭／到期帧两层均无残留，隐藏人物基线仍为0像素。独立只读检查未发现范围集合、状态条件、贴面或隐藏位置输入问题。

全图取证初轮曾将正交相机拉远至40，而相机附着的远景在深度35，导致远景遮住地形。只恢复样例既有20距离并改变正交size取全图；最终地图相机深度13.6238–26.3762，小于背景35，未改正式相机、背景或地形。

## 已验证的真实命令分支

`VisibilityAssetSample` 是本包唯一运行所有者，场景为 `Samples/VisibilityAssetSample.unity`。使用生产 `JindanRehearsalFactory` 第三段、合法席位及 `CombatCommandService.Preview/Execute`；真实角色为 `demo_20261024_space_yang`／`demo_20261024_seal_yin`，支点固定在(4,15)／(7,15)。人物仅由既有provider呈现。

- 三分支各自从独立会话开始，先阴域、再阳域，同tick两边各付250AP／400MP。冲突通过 `Session.Visibility.Input/Skip` 提交观察回执，未直接写可见性结果或预算。
- 阳胜：双方各付100MP，目标可见；阴胜：双方各付50MP，目标隐藏；中性：双方各付200MP，恢复本场正常观察基础，可见。三个结果均保留两个有效领域与公开坛微光。
- 阴胜后，玩家从(4,14)合法移动至(6,14)，实付41.666AP，近距自然现形。真实贯石击造成非致死伤害，provider播放Cast／Hit通用粒子；0.1秒时 `IsPresenting=true`，等待其结束才允许后续移动。返回(4,14)再付41.666AP，按新观察回执重新隐藏。
- 每个隐藏检查中角色根及子渲染器不活动，可见bounds／头顶锚点查询失败；同帧关闭所有人物渲染器的基线差为0像素。实际Cast／Hit反馈已结束，全图144格格心位移0。双分辨率关键帧未见粉材质或隐者模型／影子残留。

样例没有创建正式血条、扫光或飞行提示，只验证隐藏根与血条所依赖的可见锚点。贯石击不在攻击VFX目录，本轮未产生飞星Head／Tail／Ribbon，不能把空集合检查宣称为其覆盖证据。正式入口的 `EncounterCoordinator.AcceptsPlayerCommand` 与敌方循环会等待provider的表现锁；本样例遵守同一等待条件，但不是通过正式HUD触发的输入验收。隐藏角色远程攻击、盲打隐藏目标以及完整HUD／扫光／飞行叠加仍需在D12集成时验证；本包未提前修改共享provider。

## 破坏态夹具与诊断边界

视频最后 `states-*` 三帧是**资源状态夹具**：单独切换原碑Intact／Glow／Failed来核对三态外观，不调用或伪造 `jindan_support_destroyed`，不作为正式命令破坛证明。

初轮曾尝试让固定第三段角色用贯石击打破600耐久日坛。实际Probe记录：tick10展开，tick16到期；10击后耐久300、剩余62.5AP，下一击需75AP；下一次玩家回合为tick20，真实结束原因为 `visibility_expired`。原因是现有行动周期与6tick期限，贯石击CT代价为0。未提高伤害／行动点／速度，未延长期限，未添加新角色或术法。

[实际诊断](evidence/destruction-probe.txt)与[初轮失败](evidence/diagnostics/initial-capture-error.txt)保留；初轮画面在diagnostics，不混入最终视频。正式完整支点的攻击破坏路径留后续对应验收，本轮只证明失效网格与材质状态可用，不提出角色平衡结论。

## 复现与验证

关闭占用同一工程的Unity编辑器，按顺序运行Unity 6000.3.18f1：

1. `-batchmode -projectPath D:\天章游戏开发\src -executeMethod TianZhang.Art.Jindan.Visibility.Editor.VisibilityAssetBuilder.Build -quit`。编译，生成8个Prefab和样例，保存并重开检查引用；检查范围shader编译与贴面量测。
2. `-batchmode -projectPath D:\天章游戏开发\src -executeMethod TianZhang.Art.Jindan.Visibility.Editor.VisibilityAssetCapture.Capture1920`；不加`-quit`，样例结束自行退出。
3. 同上调用 `VisibilityAssetCapture.Capture1366`。双分辨率日志均为 `D12_VISIBILITY_CAPTURE_PASS`。
4. `pwsh -NoProfile -ExecutionPolicy Bypass -File assets/generated-scene-art/guanzhong-jindan-demo/d12-ability-assets/visibility/export-video.ps1`。读取各自`capture-frames.txt`限制编码帧数，避免较早未完成采集的尾帧混入。

纯规则诊断入口为 `VisibilityAssetCapture.ProbeDestruction`，带`-quit`。范围补充的原始JPG帧序列位于 `src/Logs/d12-visibility/range-20261117/`；最新运行记录、PNG和MP4在本包`evidence/range-20261117/`。未改动的破坛规则诊断不重复运行。

References loaded：unity-agent-workflows的runtime-owner-proof、ui-and-visual-assets、asset-source-lock、visible-object-identity、runtime-numeric-proof、project-structure-discovery、unity-validation。Project maps loaded：UNITY_STRUCTURE.md、UNITY_STRUCTURE.ui.md、UNITY_STRUCTURE.assemblies.md，结合当前源码及独立asmdef核实。新C#只属于本包Samples／Editor，无新增生产公共接口，无跨Feature生产依赖；最大脚本低于500行。

非请求系统改动：无。保留原有工作区改动；不修改战斗规则、CSV、共享渲染配置、旧资产、正式View或生产Builder。资源验收不代替统一Demo连续试玩与用户视觉验收。
