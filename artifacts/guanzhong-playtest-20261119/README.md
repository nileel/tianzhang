# 《天章》首次玩家黑箱试玩 · 2026-11-19

实测确认，同名新建会覆盖人物，江左错误据点会写出无法读取的存档，自建长明无法施放已装配的飞星指。这三项优先修复。普通悬赏可打赢并领取奖励，但装备栏要到下次进城才显示青锋剑，列为第四项修复。长明展开后可以继续普攻。修复、五包接线和最终三段演武的先后顺序见[开发计划](../../开发计划.md#当前执行区)。

**版本与方法。** 主会话从工作区 `7c0a390b` 及当时已有的本地资源改动，用 Unity 6000.3.18f1 的四个正式 Build Settings 场景生成 Windows Player。试玩者从开始菜单操作，使用专用存档。三名子智能体均以空白对话开始，只获玩家目标与成品入口，无源码、计划、测试预期或历史过关步骤。普通路线先用默认选项。固定阵容演武与自建金丹分别记录。资源观察使用已交付的录像，以秒数定位。主会话随后复读实际画面、对照现行规则与实现。样本是 AI 试玩者，适于发现可复现问题，难度和审美仍需真人反馈。

## 与整份开发计划对照

| 已交付的部分 | 本次从成品能走到哪里 | 距统一 Demo 验收还缺什么 |
|---|---|---|
| D0–D11 的阶地、战斗规则、金丹账本，以及 R1–R4 的寻路、敌方行动和表现修订 | 普通人物走完两种悬赏。固定演武能在阶地移动、升空、观察显隐冲突，部分支点可点选。 | 逐项保留原有技术验收。本轮没有穷举全部地块、高差、压落、守方胜利及隐藏窗口的特效残留。 |
| D12.0 定稿的统一操作方案、已接入的长明建角与同屏行动栏 | 本人能入场、展开长明、施行普通攻击、撤离重进；普通练气能打出飞星指，自建长明被误拒。 | 补其余四种合法配置，统一锁定、报价、取消与提交，让玩家能在同一场交替使用普通术法和金丹能力。 |
| D12.1–D12.5 五包独立资源 | 观察八段成片，再用固定阵容和自建长明检查部分行动如何呈现在玩家镜头。 | 将五包接到真实状态，由玩家证明能看懂、能操作；三段每段用两种合法办法打出结局，在两个分辨率验收，并留用户最终画面审阅。 |

## 普通开局

**同名新建和江左读档是两个可分开复现的问题。** 试玩者先在专用 `fxqa` 创建关中角色“岑一”，保存后在菜单看到了该名字。再用同一编号新建“岑二”，点击创建直接进入江左，没有覆盖确认；保存回菜单后 `fxqa` 只列“岑二”。操作只涉及这个一次性槽。[第一次存档](slot-before.png) · [同名新建再保存](slot-after.png)

“岑二”从江左进入报错据点后仍能“保存并返回菜单”。点击读取则停在菜单，报 `save_navigation_target_unresolved`。保留的原档为 `jiangzuo_hub / taiyi_sect`。地图可把原型目的地写入导航，保存时未要求目标在正式目录可解析，读档时却要求据点存在。玩家遇到后可先返回世界地图再进关中；已经写成该状态的档需要专门验证恢复。修复验收应使用这份实际失败档。[读档错误](jiangzuo-reload.png) · [一次性 QA 原档](jiangzuo-qa-save.json) · [读取校验](../../src/Assets/Scripts/Modules/Bootstrap/StartMenuSceneInstaller.cs)

对照组沿用 `fxqa` 和“岑二”，改选关中小族，装备铁刀后保存并返回菜单，能直接读回关中城，主手铁刀仍在。第一位“岑一”使用的是铁剑。这既确认了同槽人物被替换，也把江左导航问题与同槽写入区分开来。

- 1366×768，首页已有角色。点击“新建角色”，表单第一项预填 `slot1`，填有值时看不到字段用途。试玩者手工改用自己的空槽。核对 [建角视图](../../src/Assets/Scripts/Modules/Features/CharacterCreation/CharacterCreationView.cs)、[新建入口](../../src/Assets/Scripts/Modules/Bootstrap/StartMenuSceneInstaller.cs)和[存档写入](../../src/Assets/Scripts/Modules/Infrastructure/Persistence/GameSaveSlotStore.cs)，新建和保存共用替换同名文件的操作。[新建表单](new-character.png)
- 默认寒门散修出生江左。地图提示可进入据点，点击后得到“据点不存在”，城池背景正常，悬赏与副本皆不可用。退回地图改选关陇玄域，关中城可以领裂石甲兽悬赏。地图用原型节点的非空 `settlementId` 决定可进入，江左指向 `taiyi_sect`，正式据点表只有 `guanzhong_city`。错误发生在进入前的可用性判断，返回世界地图是实际可走的绕路。[据点实拍](jiangzuo.png) · [世界地图](../../src/Assets/Scripts/Modules/Features/WorldMap/WorldMapController.cs) · [正式据点](../../src/Assets/DataConfig/Settlements.csv)

- 在关中，野外遭遇与悬赏同名，试玩者顺利开战。移动蓝框、Esc 取消和行动点都能读懂。取消后仍是 100 AP，移动后曾耗尽 AP，需要主动结束。相邻普攻从 100 扣至 80，战报能查到敌方伤害 15、本人伤害 20。击败两兽后回城，开悬赏板才确认已完成 1/1。领取、同一进程返回菜单读回专用槽后仍是已领取 1/1，青锋剑可见。凡人点击装备收到境界不足提示，与物品的练气要求一致。[实战与战报](ordinary-combat.png) · [读档后的奖励](ordinary-reload.png) · [装备数据](../../src/Assets/DataConfig/Items.csv)
- 战报按钮“录”的用途要点开才明白，亮地上的浅字费力。首战后半段主要是空手重复普攻。练气小族子弟用专用槽 `qasword` 复测，完成同一悬赏、领取、关榜后，左栏始终只有铁剑和铁刀；返回世界地图再进关中城，青锋剑按钮出现，能当场换到主手。领取成功只刷新悬赏榜，聚落载入时才重新绑定装备。修复只需要在成功领取后调用现有装备绑定。关榜后状态字还写“悬赏板已打开”，可在关闭时一并校正。练气角色与凡人的对照也说明装备门槛和列表漏刷是两件事。[同城关榜](sword-after-claim.png) · [重新进城](sword-after-reentry.png) · [装备成功](sword-equipped.png) · [领奖入口](../../src/Assets/Scripts/Modules/Features/Settlement/BountyBoardView.cs) · [主手显示](../../src/Assets/Scripts/Modules/Features/Settlement/SettlementController.cs)

- 同一练气角色在首场用贯石击打近敌、飞星指打远敌，战报记 66 伤／12 灵力和 81 伤／10 灵力，两击合计花去 100 AP。选中贯石击时，卡片写 50 AP，未点目标的浮条却写 0 AP／0 MP、预计 AP 100→100。现有浮条直接渲染候选费用；统一确认区要把尚未取到的价格明确显示为待选目标。第二场持青锋剑打赢石甲兽遭遇，一次近身普攻 40 AP、战报 172 伤。回城青锋剑仍在主手，两份悬赏均为已领取 1/1。两场的武器、敌人与行动不同，这些数字只作复现依据。[费用矛盾](ordinary-quote.png) · [费用呈现入口](../../src/Assets/Scripts/Modules/Features/CombatPresentation/CombatActionPreviewView.cs) · [持剑命中](sword-hit.png) · [两份悬赏](both-bounties.png)

## 自建长明

玩家经关中城、关中野外找到金丹演武，准备页明确可“执火守阵”，列出本人、对手和真实费用。进场后，右上目标仍是“地表、筑路与破火域”，与本人的守方任务相反。玩家点自己和邻格、移动到橙色火格旁，再打开长明，仍看到“距离过远”。账本能找到中心坐标，却很难从默认镜头把坐标与支点对应起来。[准备页](fire-preparation.png) · [棋盘与拒绝报价](fire-out-of-range.png)

核对发现，HUD 目标取的是段落名称；长明施放引用固定公开支点，在规则中检查距离、高差接触与视线。点橙色火格不会改换中心。原双尺寸用例直接以已知目的地 (7,12) 发出合法移动命令，然后点 HUD 展开，证明费用与效果正确，却不能回答新人能否找到该位置。应保留射程与费用，用守方目标、选中公开中心和可恢复的拒绝提示解决引导问题。[HUD 投影与准备](../../src/Assets/Scripts/Modules/Features/Adventure/JindanRehearsalPresenter.cs) · [长明规则](../../src/Assets/Scripts/Combat/CombatFireDomainRules.cs) · [原用例](../../src/Assets/Tests/PlayMode/D12PlayerStartPlayModeTests.cs)

**飞星指是实质阻塞。** 自建长明在同一场里对两个射程内敌人尝试飞星指，均被拒绝，未扣 AP／MP。飞星指的正式配置要求练气、无元素限制。新建把“金丹初结”、中品火灵根及飞星指正确写入人物，入场把小阶段原样传进战斗。资格表只接受“凡人／练气／筑基／金丹……”这些大境界名，故在检查灵根前已拒绝。固定金丹演员使用“金丹”，历史用例点出玩家飞星指只断言槽位及候选，未打出这击。将现有大境界解析用于资格判断即可保留人物小阶段、灵根门槛与旧档。需同一自建角色真正在棋盘提交一次，核对资源、反馈和后续长明报价。[实机拒绝](flying-star-rejected.png) · [开局赋值](../../src/Assets/Scripts/Modules/Bootstrap/JindanDemoNewGame.cs) · [入场投影](../../src/Assets/Scripts/Modules/Features/Adventure/AdventureUnitSpawner.cs) · [资格判断](../../src/Assets/Scripts/Modules/Content/CombatElementFacts.cs) · [已有境界解析](../../src/Assets/Scripts/Modules/Character/CharacterRuntimeProfile.cs)

**提示后可完成长明与普攻。** 在记录完独立尝试后，主会话告知场上有固定公开中心并给出坐标。试玩者缩小镜头、沿路走到上层石柱附近，下一回合从 1000 AP、6000 MP 展开，实际变为 500 AP、5000 MP。领域持续时对相邻敌人普攻，AP 再降至 250。费用和两个动作的共存成立，独立找点仍需改善。[长明生效后的普通行动](fire-and-attack.png)

主会话还直接调用了本次 Player 程序集中的 `AbilityRequirementPolicy.IsSatisfied`。固定中品火根、练气门槛和无元素限制，仅变更境界输入，凡人、金丹初结、金丹温养均返回 false，练气和金丹返回 true。已有解析能将金丹子阶段归入大境界；修复后仍应拒绝非法值。这种上位类别与具体状态分开表达的做法也见 Epic 的层级标签，项目可直接复用自己的解析方法。[Epic 层级标签](https://dev.epicgames.com/documentation/en-us/unreal-engine/using-gameplay-tags-in-unreal-engine)

三名棋子衣色和血条相近，试玩者靠打开详情辨认本人。准备页首屏还列出来源实例等调试文字。保留真实账本供展开查看，默认页先给角色、阵营、目标和入场；对可见棋子在现有血条旁加能区分本人／敌我的形状或字样。结算方面，25 AP 收势生效。撤离回城、保存读回、悬赏仍已接 0/1，再入演武回到初始站位及 3474 生命、6000 灵力。尚未见到本挑战胜负，也未观察到下一次实际维护扣费。

## 固定阵容演武

另一名试玩者用关中小族的普通角色进入固定演武。第一段操控的是厚载演武者。起飞确认后 AP 从 1000 降至 750、MP 从 6000 降至 5800，再升高空后 AP 降至 625，青色投线随高度变长。随后尝试极重与切换目标，点到对手并打开了详情，仍未取得可确认的压落报价。此次证明了起升与落点可读；压落应在下一次演武中检查“点中身体格→选定受力对象→报价或具体拒绝原因”各步。[第一段高空](fixed-flight.png)

第三段选择阳照，取得 250 AP／400 MP 报价并确认。游戏暂停普通战斗，进入昭明殿与藏真域比拼，试玩者选择“跳过本次”。返回棋盘后对手和血条出现，能单击打开敌方详情，两座坛的状态仍在。这个固定场景给出了“可见以后确实可选”的正例，自建角色三段、手动冲突的不同结局和每段两种解法仍待验。[第三段冲突后点选](fixed-visibility.png)

第二段选破阵方，战前切到厚载·造物，进场选“岩墙·选空边”。试玩者点可见的水面边界后取价，确认仍灰，只得到显化／重力的通用说明。现有交互实际依次收取两个相邻格心。直接点一条视觉边不会给出完整输入。下一步可在原确认区按选择状态分别提示“选第一个格／选相邻第二格／可确认”，高亮已选端点。用一个合法空边与一个已有障碍的边复测，本轮没有证据判定造墙或压落规则失效。

## 五包资源在画面里传达了什么

| 1366×768 样例 | 初见者看见的变化 | 正式棋盘需要回答的问题 |
|---|---|---|
| 土墙、土坡 | 约 0.60 秒开始成形、1.10 秒立起、1.77 秒退去。贴崖的棕色坡面在镜头里接近直板。 | 玩家能否先指出入口边，再实走到上层？选墙时能否预测哪条边会被阻断？[土与重力成片](../../assets/generated-scene-art/guanzhong-jindan-demo/d12-ability-assets/earth/index.html) |
| 压低、压落 | 压低片 0.43–0.73 秒角色下移后悬停，脚下投线连接落点。压落片约 0.53 秒投线消失，约 1.03 秒脚触地。 | 保留悬空投线。对照实际可输入时刻与落地姿态，确认玩家能区分悬停和落地。 |
| 潮汐 | 2.97–3.47 秒浪圈向外推，逐格水纹留下。5.17–5.57 秒退潮伴随一个角色移向中心。 | 展示施放者、受影响格和真实阻挡后的最终位置，目标选择时可先预测一次。[水成片](../../assets/generated-scene-art/guanzhong-jindan-demo/d12-ability-assets/water/index.html) |
| 长明 | 0.90 秒柱顶起火，2.50 秒三处地火，6.13 秒局部转湿仍有余火，8.17 秒余火消散。 | 让玩家在破中心后指出哪格仍危险，再选择绕开、等候或以合法潮汐处理。[火成片](../../assets/generated-scene-art/guanzhong-jindan-demo/d12-ability-assets/fire/index.html) |
| 定尺、封关 | 1.50 秒青色与 9.90 秒金色装置轮廓相近。约 17 秒立环易认，随后变色、消退。 | 分别询问定尺后目标是否可及、封锁后哪条边仍可通行。封关改变跨边资格；录像里的门形不赋予传送能力。[空间成片](../../assets/generated-scene-art/guanzhong-jindan-demo/d12-ability-assets/space/index.html) |
| 阳照、藏真 | 1.17 秒角色与长影一同消失，6.27–6.63 秒原位重现。26.67 秒远景可见全场覆盖，观察者也可能读成天气。 | 同一镜头询问哪座公开坛仍生效、按当前观察资格谁可被选中，然后实际点选；提示从公开信息取值。[最新显隐成片](../../assets/generated-scene-art/guanzhong-jindan-demo/d12-ability-assets/visibility/index.html) |

以上八段是样例动效的可读性观察。观察员提出的“门另一端落点”与正式封锁规则不符，改用“哪条边能过、目标能否攻击”作为验收问题。压落片中投线提早消失，也不足以推断输入提早解锁。

后置的苻渊长袖模型另抽看三动作六方向录像。白发、袖摆和衣摆有助识别朝向，侧向出手容易辨认。正面 15.57 秒施法与 29.07 秒攻击的峰值姿势较接近。后续若启用该角色动画，先在正式镜头中遮去动作标签测试起手和收势，再决定哪个片段需修。该样片使用独立播放器。[三动作原片](../fuyuan-jointed-doll-silhouette-20261105/runtime/three-actions-six-directions.mp4)

## 调研如何改变实施选择

| 需要解决的问题 | 查阅的成熟做法 | 本项目采用的最小做法与验证 |
|---|---|---|
| 新建会写入旧档 | Xbox XAG 115 的原则是让玩家能预判不可逆结果并恢复输入错误。.NET 两参数文件移动在目标已存在时失败，正好适配现有同目录临时文件。[XAG 115](https://learn.microsoft.com/en-us/xbox/accessibility/xbox-accessibility-guidelines/115) · [File.Move](https://learn.microsoft.com/en-us/dotnet/api/system.io.file.move) | 默认新名字并在碰名时就地提示。新建发布临时文件时禁止替换，已有槽位列表仅用于给建议名；发布失败保留原档并清理临时文件。显式保存仍使用原来的替换写入。换序列化库或只在按钮上问一次都解决不了写盘时的竞争。 |
| 默认入口、目标与奖励难理解 | XAG 114 要求操作前能推断按钮作用，标签贴近控件。XAG 109 要求随时查看当前目标和进度。Wesnoth 把每方胜败目标展示与实际结算事件分开。[界面上下文](https://learn.microsoft.com/en-us/xbox/accessibility/xbox-accessibility-guidelines/114) · [目标清晰度](https://learn.microsoft.com/en-us/xbox/accessibility/xbox-accessibility-guidelines/109) · [Wesnoth 实例](https://wiki.wesnoth.org/BuildingScenariosSimple) | 地图用可解析的正式地点控制按钮；战斗按当下攻守显示一句可执行目标。打完读原战果／悬赏状态，在回城时指出哪项已达成、去哪里领奖。练气要求显示在奖励或按钮旁。以“新人能指出下一步并独立做到”验收。沿现有页面使用短提示即可。 |
| 混用普通与金丹、拒绝后找不到目标 | Unity 的命令模式把选择与执行分离。Wesnoth 用棋盘格展示移动范围；Into the Breach 用公开预告让玩家先预测再行动。[Unity 6 命令示例](https://learn.unity.com/tutorial/65e0e048edbc2a23a5ee7442?version=6.0) · [Wesnoth 格位预览](https://wiki.wesnoth.org/FAQ) · [Subset Games](https://subsetgames.com/itb.html) | 复用现有冻结报价、命令重验、格位和支点投影。选长明时标出固定公开中心，说明“接近标记的中心再展开”，按真实距离和视线判断可否施放。只投影公开信息。从合法位置确认、非法位置取消和战况变化后重选三条路径检查 AP／MP；无需动作队列、战斗撤销栈或提前公开隐者位置。 |
| 五包效果叠加后仍能决策 | Riot 以命中范围与视觉层级控制噪声，无障碍指南建议用形状、纹样或文字补充颜色。[Riot 画面可读性](https://www.leagueoflegends.com/en-us/news/dev/clarity-in-league/) · [形状与颜色](https://gameaccessibilityguidelines.com/ensure-no-essential-information-is-conveyed-by-a-fixed-colour-alone/) | 保留水纹、悬空投线与局部余火。坡道优先试入口边和路径预览，封关只标真正禁入的方向，日月坛依公开状态显示。在默认镜头实际走坡、破阵、重选目标，先验证已有预览能否解决误读，再决定是否调材质。 |
| 自建角色、真实 AI 与三段两条路线 | Unity 说明稳定模板可与运行实例分开；Wesnoth 的 RCA 每步评估候选、按得分执行，目标展示本身不会触发胜败。[Unity 数据模板](https://unity.com/how-to/separate-game-data-logic-scriptable-objects) · [Wesnoth 候选行动](https://wiki.wesnoth.org/RCA_AI) · [胜败条件](https://wiki.wesnoth.org/BuildingScenariosSimple) | 复制预设到本人并显式映射守府、席位、能力与飞行来源；同伴保持各自账本。沿现有 AI 每步重取合法命令，先补本段缺的可打支点、绕行或对空候选。记录候选、实际报价、选择及下一局面。胜负由真实行动后状态判定。试跑同配置重试和两条路线，不引入整套 AI 框架。 |

这些来源能复用交互约定和实现方法。三段的攻守、席位唯一性、支点、余火和显隐可见权限来自《天章》自己的规则，外部样例不能直接给出合法搭配与数值。最终仍需本工程实走和用户画面审阅。

### 后置工作怎样使用本轮结果

- **F1、V8、自然双目标。** 这次看到了零 AP 后主动结束和两只怪同场。F1 仍需核对死亡／控制／重复结束时 CT 只结算一次；同时出现两敌也没有证明一个 AOE 自然命中两者。前者可复用固定初态验证，后者要保留真实敌方移动。V8 用现有录屏的亮地和暗底作为对照，实测“录”、拒绝原因、禁用文字的像素高度与对比，再改局部标签／底色。[Wesnoth 的场景测试](https://github.com/wesnoth/wesnoth/blob/master/data/test/scenarios/README.md) · [XAG 101 的实拍测字方法](https://learn.microsoft.com/en-us/xbox/accessibility/xbox-accessibility-guidelines/101)
- **FX2、FX3 与地块预算。** 剑弧方案可借静态环形网格配滚动遮罩，水墨方案可借带笔触纹理的网格和粒子参数。原案例分别使用商业工具及旧 Shader Forge，对照应在本项目现有 URP 材质中做，并先让合法飞星真正出手。若正式镜头出现卡顿，再对同一场景做目标 Player 采样，分清提交、几何与 GPU 耗时；本轮没有帧时测量。[剑弧作者拆解](https://realtimevfx.com/t/venom-slash-breakdown-of-the-effect-included/18903) · [水墨作者拆解](https://realtimevfx.com/t/unity-tutorial-ink-brush-effect/2500) · [Unity 目标设备性能采样](https://docs.unity3d.com/6000.3/Documentation/Manual/profiler-profiling-applications.html)
- **闭关和组合档案。** CDDA 的活动规则区分每回合与完成时触发。可借用完成一次才提交的边界，把实际耗材、成长、历时和一个世界变化放在同一合法周期验收；目前缺真实闭环。火系组合档案则只需按已批准字段整理，既有位格来源和资源账本已给出依据。两项都不影响眼下首次试玩。[CDDA 活动完成触发](https://docs.cataclysmdda.org/JSON/EFFECT_ON_CONDITION.html) · [本项目组合范围](../../docs/superpowers/specs/2026-09-20-jindan-combo-scope-design.md)
