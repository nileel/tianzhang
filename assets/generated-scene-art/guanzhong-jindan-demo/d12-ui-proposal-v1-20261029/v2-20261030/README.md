# D12 能力生效视觉补充 V2

版本：2026-10-30。沿用 [V1 方案包](../README.md)，基线提交 `78a734f8`。

2026-11-02 用户已确认 V3 统一 HUD。V2 的 8 组能力表现、合法构筑及历史实机来源用于五个资源包制作，本文的左右栏与分页仅供查阅旧分镜。[V3 制作方案](../v3-20261030/README.md#制作顺序与交付) · [开发计划](../../../../../开发计划.md)

## 直接审阅

[打开 V2 连续分镜](index.html)。顶部选择能力与状态，左右箭头逐帧查看；键盘左右键也可使用。每帧同时显示合法构筑、场景结果、规则边界和“已有／建议”区别。右侧的“原始实机”打开未经修改的历史 Unity 截图，另有原始方案组图入口。1366 下较长的来源详情可滚动，主要场景结果在上方常驻。

共 **8 组、42 个状态、84 张整屏稿**：每个状态同时覆盖 1920×1080、1366×768。浏览器原型是审阅工具，不执行战斗、费用、保存或 Unity 命令；底部行动区保留 V1 的位置与选择→预览→确认方向，本页不重做可玩战斗。

| 组别 | 连续镜头与分支 | 1920 关键稿 | 1366 关键稿 |
|---|---|---|---|
| [厚载·岩墙](index.html?sequence=earth-wall&frame=0) | 空边 → 土石聚合 → 实体挡路／挡作用线 → 破坏后恢复原边 | [墙生效](visuals/1920/earth-wall-03.png) | [墙解除](visuals/1366/earth-wall-04.png) |
| [厚载·窄坡](index.html?sequence=earth-ramp&frame=0) | 裸崖 → 坡面接通 → 单格角色走上高端 → 坡消失、角色留在已达位置 | [实走结果](visuals/1920/earth-ramp-03.png) | [坡形成](visuals/1366/earth-ramp-02.png) |
| [厚载·重力](index.html?sequence=gravity&frame=0) | A：双方离地，高空→低空→解除仍低空；B：施术者接地，高空→下压途中→合法接地 | [压落途中](visuals/1920/gravity-05.png) | [压低结果](visuals/1366/gravity-02.png) |
| [蓄澜](index.html?sequence=tide&frame=0) | 火格／浅水 → 首涨过程 → 火变湿泥 → 次涨成浅水／低植冲蚀 → 退为湿泥 → 再退为干地 | [首次灭火](visuals/1920/tide-03.png) | [再次退潮](visuals/1366/tide-06.png) |
| [长明](index.html?sequence=fire&frame=0) | 未展开 → 中心及全棋盘弱热 → 维护后局部火 → 中心受损仍有效 → 中心毁但余火在 → 独立水修灭余火 | [火域生效](visuals/1920/fire-03.png) | [毁中心后](visuals/1366/fire-05.png) |
| [定尺](index.html?sequence=scale&frame=0) | 两边不可及 → 压缩后跨格作用 → 受损仍改距 → 毁支点恢复；另次扩张：相邻不可及 → 关闭后恢复 | [跨格作用](visuals/1920/scale-02.png) | [扩张不可及](visuals/1366/scale-05.png) |
| [封关](index.html?sequence=seal&frame=0) | 连接可通 → 单向封住／旁边可绕 → 外层破但内层仍封 → 两层破尽、同边穿过 | [外层破](visuals/1920/seal-03.png) | [真正开通](visuals/1366/seal-04.png) |
| [阳照与藏真](index.html?sequence=visibility&frame=0) | 原可见 → 完全隐匿 → 阳胜／阴胜／中性三个并列结果 → 另次近距自然现形 | [阳胜复见](visuals/1920/visibility-03.png) | [阴胜仍隐](visuals/1366/visibility-04.png) |

“另次”“A／B”“分支”不是连续免费换装或重复结算。审阅顺序只组织画面，不代替正式行动、冷却与收费。

## 规则、实现、实机与建议的区别

本轮先核对 D6–D11 原文、当前实现及已有验收。原型文首的早期“未运行”状态不是当前事实；以下以当前源码与对应验收记录共同判断，不借新效果图扩大旧验收。

| 类别 | 当前规则／实现所有者 | 历史 Unity 实机 | V2 建议新增表达 |
|---|---|---|---|
| D6 飞行及 D7 重力 | [厚载原型](../../../../../docs/角色养成/演示原型/神通/散修/演武·厚载.txt)；[CombatFlight.Reconcile／Fail](../../../../../src/Assets/Scripts/Combat/CombatFlight.cs)；[CombatUnitFlightVisual.Position／Transition](../../../../../src/Assets/Scripts/Modules/Features/CombatPresentation/CombatUnitFlightVisual.cs) | [低空投影](../../d7-earth-20261026/AirborneSourceLowersFlyingAlly/01-pressure-lowered.png)、[合法压落](../../d7-earth-20261026/GroundSourceLandsFlyingAlly/01-pressure-landed.png)；[D6 验收](../../d6-flight-20261026/验收说明.txt) | 体外细下压痕、短促衣摆／落尘；不遮脚底与落点，不造悬浮石块或陨坑 |
| D7 显化墙坡 | [CombatEarthRules.Validate／Prepare](../../../../../src/Assets/Scripts/Combat/CombatEarthRules.cs)；[CombatEarthView.Present](../../../../../src/Assets/Scripts/Modules/Features/CombatPresentation/CombatEarthView.cs) | [墙 750](../../d7-earth-20261026/native/01-wall750.png)、[墙 0](../../d7-earth-20261026/native/03-wall0.png)、[真实走坡](../../d7-earth-20261026/ProductionRampChangesRouteAndSupportsActualTraversal/02-ramp-moving.png)；[D7 验收](../../d7-earth-20261026/验收说明.txt) | 沿边聚土成石、坡肩沉降、结束短尘；保留既有实体结构，结束无永久残留障碍 |
| D8 潮汐／地表 | [蓄澜原型](../../../../../docs/角色养成/演示原型/神通/散修/演武·蓄澜.txt)；[CombatTideRules.Preview／PrepareSettlement](../../../../../src/Assets/Scripts/Combat/CombatTideRules.cs)；[CombatSurfaces.Water／ClearSource](../../../../../src/Assets/Scripts/Combat/CombatSurfaces.cs)；[CombatSurfaceView.Present／ColorFor](../../../../../src/Assets/Scripts/Modules/Features/CombatPresentation/CombatSurfaceView.cs) | [冻结变化预览](../../d8-water-fire-20261027/ProductionTideMovesBurningSurfaceAndReturnsToShore/01-preview.png)、[真实涨潮结果](../../d8-water-fire-20261027/ProductionTideMovesBurningSurfaceAndReturnsToShore/02-tide-result.png)；[D8 验收](../../d8-water-fire-20261027/验收说明.txt) | 低潮头、水膜、湿泥反光、微量蒸汽、低植消失；只表现冻结变化格，不增加模拟水层 |
| D10 长明 | [长明原型](../../../../../docs/角色养成/演示原型/神通/散修/演武·长明.txt)；[CombatFireDomainRules.Deploy／EndMain／EndOwnTurn](../../../../../src/Assets/Scripts/Combat/CombatFireDomainRules.cs)；[JindanTidePresentation.Project](../../../../../src/Assets/Scripts/Modules/Features/Adventure/JindanTidePresentation.cs) | [维护后 1 格火](../../d10-fire-domain-20261027/player/03-maintained-fire.png)、[中心毁但余火在](../../d10-fire-domain-20261027/counter/02-center-broken.png)、[水灭余火](../../d10-fire-domain-20261027/counter/03-tide-extinguished.png)；[D10 验收](../../d10-fire-domain-20261027/验收说明.txt) | 全棋盘台缘弱暖光、中心暖纹、稀疏低火、余火变弱及水灭；不画全场烈火或局部圆形领域 |
| D9 定尺 | [定尺原型](../../../../../docs/角色养成/演示原型/神通/散修/演武·方位定尺.txt)；[CombatScaleRules.Validate](../../../../../src/Assets/Scripts/Combat/CombatScaleRules.cs)；[CombatJindanLoadout.Reconcile／PrepareProjection](../../../../../src/Assets/Scripts/Combat/CombatJindanLoadout.cs)；[JindanScaleView.Present](../../../../../src/Assets/Scripts/Modules/Features/CombatPresentation/JindanScaleView.cs) | [压缩](../../d9-space-20261027/scale-compressed.png)、[扩张](../../d9-space-20261027/scale-expanded.png)；[改距见证](../../d9-space-20261027/ProductionScaleChangesReachAndInvalidatesOldQuotes-proof.txt) | 受影响边低矮刻度与折光，加上作用成立／不成立的场景结果；不挪动地块，不把折光当实体墙 |
| D9 封关 | [封关原型](../../../../../docs/角色养成/演示原型/神通/散修/演武·门枢封关.txt)；[CombatSealRules](../../../../../src/Assets/Scripts/Combat/CombatSealRules.cs)；[JindanSealView.Present](../../../../../src/Assets/Scripts/Modules/Features/CombatPresentation/JindanSealView.cs) | [完整](../../d9-space-20261027/gate-intact.png)、[外层破](../../d9-space-20261027/gate-outer-broken.png)、[开缺口](../../d9-space-20261027/gate-open.png)；[D9 验收](../../d9-space-20261027/验收说明.txt) | 单向门沿低折光、两层支点状态；内层破后保留固定碑失效形态、门沿退出并可穿过 |
| D11 显隐 | [装配与冲突规则](../../../../../docs/基础设定/金丹基础效果装配与冲突规则.txt)；[藏真原型](../../../../../docs/角色养成/演示原型/神通/散修/演武·朔坛藏真.txt)；[CombatVisibility.IsVisible／NaturallyRevealed／CommitResult](../../../../../src/Assets/Scripts/Combat/CombatVisibility.cs)；[AdventureSceneInstaller.LateUpdate](../../../../../src/Assets/Scripts/Modules/Bootstrap/AdventureSceneInstaller.cs)；[JindanVisibilityView](../../../../../src/Assets/Scripts/Modules/Features/CombatPresentation/JindanVisibilityView.cs) | [阴胜隐藏](../../d11-visibility-20261028/concealment/01-hidden-after-release.png)、[阳胜复见](../../d11-visibility-20261028/manual/03-manual-result.png)；[D11 验收](../../d11-visibility-20261028/验收说明.txt) | 公开坛保留日／月纹与暖／冷微光，领域覆盖全部基础拓扑格，应以持续动效表达；2026-11-17用户确认跨格日光／光尘与低雾／月光方向，D12.5独立资源样例已完成展开／持续／消散，并按追加反馈保证每格有稳定地面光色、加密日光，正式View接线留集成。范围各随本方领域存续，不随观察胜负或隐者位置变化。人物按本次有权观察的结果出现或完全移除，绝不向隐者位置施光 |

现有墙坡、飞行高度、地表色片、贴地青金线／紫橙箭头、公开支点及人物显隐已经存在。V2 提议的成形动画、潮头、蒸汽、火苗、范围温光、折光与裂纹并未接入 Unity。

历史图片保持原文件不变，本页引用的 16 个原图入口均为 1366×768。D7／D8／D9 有部分原生输入证据，D10 为自动化 UI 事件与正式命令，D11 为图形 PlayMode 键鼠见证；这些不等于新增 D12 双分辨率完整试玩。V1 使用的 [R4 1920 实机参考](../../r4-low-cover-20261029/runtime/1920/mid_low_cover_a-state-0.png)仍是场景辨识和配色依据。

## 每类不能误读的条件

- **显化与重力二选一。** 岩墙、窄坡是显化场次；重力是另一场次。显化构件 750 耐久，大于 0 全效，归零无残留。坡限相邻高差 1、净空 1 的单格通路。战中切装按完整本人回合 AP 与下一本人回合才能发动的既有规则，不靠换图免费切换。
- **压低和压落分条件。** 双方离地的见证为档差 2，高→低，无坠落伤害；接地施术者的见证为档差 3，可压到合法地面。解除不免费回升，也不返还已发生伤害；地面失重轻跃仍不授持续飞行。
- **水量逐级。** 火／干／焦→湿→浅→深，退潮反向。首涨湿泥不冲蚀该株低植；第二涨变浅水后才体现冲蚀。第六帧原火格湿→干，原潮源浅→湿，集合外的水面保持。它不是点击“终止”后的回滚：终止本源会清除归属地表，已改写自然水面没有隐藏旧水量。瞬时推移、水击与冲蚀不撤销。正式预览继续显示水线、冻结格、推向和落点；图中的水纹不扩大结算集合。
- **长明主规则与余火分别结束。** 整棋盘弱高温，最多 3 个合法燃烧格；刚展开不立即跳维护周期。中心 600→225 仍生效，归零停止主规则，但已付余烬最多再活 2 个本人结束周期。独立水修可以灭火；火修本人不因演示需要获得水潮或飞行。画面只是低火与中心的状态差别，不承诺粒子规模与最终灯光开销。
- **定尺只改共同距离。** 两边各 0.5 可总距 1；另次单边扩张为 2 可让相邻近身攻击不可及。格心、六邻接与高度不变。方位标 200 耐久，受损灭微光但效果仍在，归零／合法结束才恢复；旧报价必须重取。
- **封关不是实体墙。** 只挡指定单向边的敌方地面／飞行及强移进入，反向、友方、其他边、攻击及视野不挡。外 100／内 100；第 4 次贯石击后的内层 80 仍阻挡，只缩期到最多 t+2，第 7 击破尽才开通。固定支点保留低矮失效碑；不把它与临时显化墙或火中心的清理方式混同。
- **显隐只影响本次观察。** 隐藏完成后没有模型、影子、血条、轮廓、脚圈、定位光、追踪粒子、坐标和隐者命中列表。阳胜本次可见，阴胜本次不可见，中性恢复正常观察基础值，本见证为可见。两个领域及独立减伤都继续存在。近距自然现形优先；阳照不加视距、不穿墙；藏真界版攻击／施法不自动现形。不同观察结果是并列分支，不是预先承诺玩家必胜。

构筑仍取自 [现有预设 CSV](../../../../../src/Assets/DataConfig/JindanDemoPresets.csv)：厚载、蓄澜、长明、尺度与阳照、封关与藏真。图中相同蓝袍沿用现有测试形象；双方／同伴为不同持有者，能力归属由每组左栏明示。火域反制帧的水修可位于镜头外，不能理解为火修施水。实际制作仍检查 positionId 与来源唯一性。

## 表现与遮挡约束

保留 V1 青黑浅金、左右信息区、底部行动和确认区。新增画面采用中央固定局部镜头，不靠把整张 144 支承格地图拉远解决遮挡。岩墙要能看见实体阻隔；坡要能看见上下接口；重力要能看见脚底离地距离；潮与火保持贴地、低矮，留出格心、人物轮廓、落点与支点。

作用过程建议短促，成形／下压约 0.25–0.4 秒，灭火蒸汽及时散去；这是待制作验证的节奏建议，不改变规则 tick、费用、动作完成时点或输入锁。全棋盘火域以台缘弱暖光表示范围，实际燃烧仍逐格。定尺青短刻度／金长刻度配合作用结果；封关完整／破外层配合两层支点形状，颜色不单独承担规则含义。

中央画面是 **Imagegen 参考重绘**，不是实机像素编辑的测量图。保留蓝灰六角阶地、土路、水岸、阶梯、石碑、岩堆与山雾的辨识，但细节格数、模型外形、镜头和精确边位置存在概念化重绘；不据此改地图、碰撞、连接、支点坐标或单位占位。正式地图仍由 D1–D11 的数据和实现决定。画面中的作用光痕不授权通用弹道等新机制。

## 资产与复现

- [storyboards.js](storyboards.js)：42 个状态、合法构筑、分支与规则说明。
- [review.js](review.js)、[review.css](review.css)：只做分镜审阅，引用 V1 的 `proposal.css`，没有复制游戏系统。
- [imagegen-prompts.json](imagegen-prompts.json)：内置 `image_gen.imagegen` 的完整提示词、真实参考图、保存文件和定向修正提示。未用 CLI／API 回退。
- `art/`：8 张 Imagegen 原始工作组图，用作浏览器裁切的源素材；**均是视觉方案，不是 Unity 原图**。成品审阅以有明确标识的 `visuals/` 整屏稿和浏览器页为准。
- [review-check.json](review-check.json)：浏览器检查结果；[render-check.cjs](render-check.cjs)：使用 Node、Playwright、Edge 渲染。从本目录执行 `node render-check.cjs`；可传逗号分隔组名只更新已变输入的组。依赖从本机 Codex 的 workspace dependencies 取得，不安装新项目依赖。

## 本轮验证与验收边界

84 张整屏稿的图片加载、页面错误、控件／标题溢出、场景与面板重叠检查通过；8 组前后切换与 16 个历史实机图入口通过。1366 的长来源详情按 V1 策略允许滚动，不挡场景、结果说明或底部操作。人工查看 8 张组图与双分辨率关键帧；两项独立只读复核核对规则和图文一致性，定向修正低植消失时点、固定封关碑失效表现后只重查改动组。

另做方案链接、定向空白和暂存差异检查。不运行 Unity／BattleSim／全项目数据链：本轮没有修改对应输入，也没有新增游戏运行验收。生成图中的角色、灯光和作用痕只供视觉选择，不能证明 D12 已制作或运行通过。

资源按本页已核对的场景约束制作，技能栏、目标与持续状态的同屏呈现按 V3 执行。D12.1 为下一项，正式接入后交付双分辨率 Unity 连续试玩证据。
