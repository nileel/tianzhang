# D12.5 阳照与藏真公开支点及范围特效资源包

**2026-11-17 最新状态：按追加反馈增强逐格地面覆盖，并加密日光。** 每个受影响格都有稳定地面光色与柔和中心光面；阳照叠加跨格日光、上浮光尘与流动地光，藏真叠加横向低雾与月光。原逐格描边已移除，两个领域均有0.8秒展开、持续运动及0.55秒消散。新版仍须用户审阅最终画面。

共8个Prefab：原日／月公开坛各三态，以及重做的 `YangDomainRange`／`YinDomainRange`。默认镜头size5，另存size3近景、全图与展开／消散中间帧。最新证据在 `evidence/coverage-20261117/`；旧19.43秒公开坛、26.43秒格框及 `evidence/motion-20261117/` 首版日光／低雾样例保留为历史记录，不代表当前资源效果。

Unity编译、保存重开、1920×1080／1366×768实机样例通过；视频均为973帧、30fps、32.43秒，完整解码通过。双分辨率默认镜头和全图截图均已实际查看，领域之间的0.5秒差帧确认持续变化；领域关闭／到期完成后根节点及粒子清空，隐者渲染贡献仍为0。独立只读复核检查状态来源、截图与运行记录。[媒体验证](evidence/coverage-20261117/media-validation.json)

[最新视频与关键帧](index.html) · [1920运行记录](evidence/coverage-20261117/1920/runtime-proof.txt) · [1366运行记录](evidence/coverage-20261117/1366/runtime-proof.txt) · [范围贴面量测](evidence/coverage-20261117/range-geometry-proof.txt) · [素材来源](source/README.md)

这是资源交付与独立样例。正式 `JindanVisibilityView`、`AdventureSceneInstaller`、V3 HUD及自建角色流程未接线，用户最终画面审阅仍待后续。

## 资源与使用

Unity目录：`src/Assets/Art/VFX/Guanzhong/Jindan/Visibility/`。

| Prefab | 状态 |
|---|---|
| YangDormant / YinDormant | 原完整碑，暗日纹／月牙可识别，微光关闭 |
| YangActive / YinActive | 原完整碑，暖金日芒／冷蓝月牙与克制局部微光 |
| YangDestroyed / YinDestroyed | 原失效断碑，无纹样微光 |
| YangDomainRange | 每格稳定暖金光面、加密流动日光束与上浮光尘 |
| YinDomainRange | 每格稳定冷蓝雾色、漂移低雾与月光薄幕 |

直接复用D12.4 `ScaleSupport.prefab` 的原石碑完整／失效网格及材质。Builder只在本包副本中替换 `Intact/Glow`，不修改Space、原Props或正式场景。纹样为原创局部HLSL，无新增位图、外部依赖或最终模型。

层级保留 `Intact/Body`、`Intact/Glow`、`Failed/Body`。新增 `Intact/Identity` 为未展开时的暗纹，`Intact/Glow/Glyph` 为日／月亮纹，`Intact/Glow/LocalGlow` 为局部微光。透明载片无碰撞、无投影、不采样背景、不改变原碑尺度或接地。纹样尺寸由原碑bounds取得：本样例0.1794米，沿原碑前面定位；默认1080p投影约19.38×15.27像素，768p按同镜头比例缩放。实际对象、世界bounds、屏幕矩形、材质与来源记录在运行证据中。

后续View继续从真实席位投影：未展开显示Intact并关闭Glow；有效实例打开Glow；`LastEndReason == "jindan_support_destroyed"` 时关Intact、开Failed。领域自然结束应回完整无光态，不能误用Destroyed。冲突输赢不会改变两坛的有效状态。所有微光附着公开坛，禁止转挂隐者或用于定位隐者。

初交付运行记录中的 `/Intact/Glyph` 是省略Glow父层的短标签，真实对象层级为 `/Intact/Glow/Glyph`；最新记录已使用完整层级。

## 全棋盘范围补充

两种领域均覆盖本演武地图全部144个基础拓扑支承格，包括不可停格。阳照照明适用于全部粗阵营；藏真保护仅适用于施术方粗阵营存活角色。范围纹表达领域空间覆盖，不等于观察者视野、实际可见者或当前隐匿者集合；阳照不加视距、不穿墙。依据为[日坛昭明](../../../../../docs/角色养成/演示原型/神通/散修/演武·日坛昭明.txt)和[朔坛藏真](../../../../../docs/角色养成/演示原型/神通/散修/演武·朔坛藏真.txt)原规则。旧V2“只在公开坛附近增加微光”的表现文字已同步纠正。

两个范围Prefab共用 `WholeBoardRange.asset`，按本张固定关中地图的 `CombatBattlefieldSurface` 烘焙，须以世界零位、单位缩放、无旋转装配；不挂在石碑或人物下随其移动。UV2记录固定q/r，样例用生产 `SceneObjects.Board.Cells` 核对集合完全相等。每域包含一个贴面Renderer与两个原生ParticleSystem，粒子也由同一覆盖网格发射。柔软雾缘可在棋盘外侧自然淡去，不作为可选格或命中判定。本资源仅适用本张固定地图；更换地形需重新烘焙。

`DomainRange.shader` 保留连续世界XZ噪声与滚动光带，增加每格局部UV0的柔和中心光面，不画六角边线。完全生效时阳照地面alpha下限由0.025提高到0.14，藏真由0.04提高到0.16；下限位于柔光遮罩之外，流动低谷也不会让某格失去地面光色，最后仍统一乘退场透明度。144格网格集合检查结合非零下限证明每个支承面都有输出；人物／障碍遮挡仍正常生效，不把整图像素差宣称为144格逐格像素统计。粒子复用仓库已有Kenney CC0烟雾／柔光纹理：阳照为纵向光束和小光尘，藏真为横向低雾和柔月光。暖冷颜色之外，也以运动方向、形态和公开坛日／月纹区分。所有运动只读取固定覆盖网格、粒子生命周期与表现秒数，不读取人物、观察结果、目标或施术者位置；角色隐匿不会导致范围挖空、移动、增强或追踪。未开启全局折射、Depth/Opaque Texture，不改变世界几何、碰撞、通行或距离规则。

样例唯一运行所有者仍为 `VisibilityAssetSample`，`Present()` 分别把本方 `seat.ActiveVisibility != null` 交给局部普通C#辅助类 `VisibilitySampleDomain.Set`；每帧 `Tick(Time.deltaTime)` 只驱动0.8秒渐入／0.55秒淡出。激活时播放原生粒子并记录地光展开时刻，结束时停止发射、淡出，随后清空粒子并停用根节点。材质使用PropertyBlock，Prefab初始地面透明度0、粒子不自动播放；后续正式View装配须遵循这套局部生命周期，不能只激活根节点。没有新增生产MonoBehaviour、全局管理器或战斗状态。

冲突阳胜／阴胜／中性、近距自然现形及退隐均保留两域；日域合法关闭只淡出日光，月域真实到期后淡出月雾；另起会话验证月域合法关闭及重置清理。公开碑失效的三态夹具单独标注，范围也随夹具退出，不据此宣称真实破坛完成。普通Play的最终断言等待淡出完成，避免高帧率时固定帧数早于0.55秒。

载片较原面抬高0.025米。坡面沿现有12等分算法，石阶按每个踏面条带裁剪；严格三角形内部采样251072点，最大高度误差0.00003817米，无跨踏面斜片。实际网格187450顶点、62768三角形，两个Prefab引用同一网格。运行记录包含原地图144格格心、范围实例、材质、世界bounds、屏幕投影及同帧关掉单层时的像素差，结合默认镜头和全图关键帧核对覆盖与可读性。

动效证据分别记录0.5秒间隔的 `motion-yang-a/b`、`motion-yin-a/b`。每个时刻先取领域启用／禁用Renderer的同帧差，再比较两次领域独立贡献，排除人物与坛纹变化；768p日光116042、月雾18628像素发生变化。运行记录另含每个粒子系统的存活数量、世界bounds、投影角点、材质与发射网格。展开0.2秒时透明度0.25，退场0.2秒时0.636364；最终根节点停用且粒子数0。隐藏人物基线仍为0像素。默认与全图关键帧用于核对角色、公开坛、地形与运动形态，数值本身不替代画面审阅。

日光束发射率7→21/s，光尘22→35/s，保留每系统180粒上限；双分辨率实测对应采样时刻存活数由33／99变为96／155。地面滚动光带也加密并放宽亮带，不修改粒子源、月雾密度、范围网格或真实领域生命周期。1080p半秒时差为日光229570、月雾37298像素；本轮双分辨率均重新捕获并通过，默认及全图画面已复核。

全图镜头按范围及粒子bounds调正交size，相机距离保持20并核对整体深度小于背景35。旧范围取证初轮拉远至40造成远景遮挡的问题未复发；未改正式相机、背景或地形。

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

纯规则诊断入口为 `VisibilityAssetCapture.ProbeDestruction`，带`-quit`。本轮原始JPG帧序列位于 `src/Logs/d12-visibility/coverage-20261117/`；运行记录、PNG和MP4在本包`evidence/coverage-20261117/`。未改动的破坛规则诊断不重复运行。

References loaded：unity-agent-workflows的runtime-owner-proof、ui-and-visual-assets、asset-source-lock、visible-object-identity、runtime-numeric-proof、project-structure-discovery、unity-validation。Project maps loaded：UNITY_STRUCTURE.md、UNITY_STRUCTURE.ui.md、UNITY_STRUCTURE.assemblies.md，结合当前源码及独立asmdef核实。新C#只属于本包Samples／Editor，无新增生产公共接口，无跨Feature生产依赖；最大脚本低于500行。

非请求系统改动：无。保留原有工作区改动；不修改战斗规则、CSV、共享渲染配置、旧资产、正式View或生产Builder。资源验收不代替统一Demo连续试玩与用户视觉验收。
