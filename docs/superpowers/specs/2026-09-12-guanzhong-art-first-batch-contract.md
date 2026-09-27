# 关中首个悬赏流程美术首包生产合同

日期：2026-09-12
状态：**负责人已对 DEC-20260912-CD1A9E90A58405 选择 C，拒绝本轮整屏方向。本合同和两张样板仅保留为拒绝证据，不可作为生产输入；没有 Unity 实机、外部交易或费用批准。**

## 已消费视觉决定

- 有效回复为 option C，来源为 feishu_card，证据哈希为 6aa1f2bee1e31e15b9589443bf867302a338e6a852e43aa638178f734bf03f11。
- C 的语义仅为拒绝本轮城镇/悬赏与野外战斗/HUD 方向，并保持 LOOKDEV 阻塞；它没有提供可推导的替代方向或修订要求。
- 不解锁下游，不追加 ImageGen 调用，不上传或消费外部 3D 平台、credits，也不运行 Blender、Unity 或正式资源导入。

下列章节保留为被拒绝方案的历史快照，不能被后续任务当作当前生产合同、视觉批准或费用批准。

## 1. 目的、批准门与事实来源

本合同把“关中城接悬赏 → 关中野外对石甲兽战斗 → 返回关中城领奖”的第一包美术拆为五个既有生产工作面。它只服务现有 `guanzhong_city`、`guanzhong_wild`、默认男主和 `enemy_shijiahou`，不新增 NPC、地区、怪物、玩法、数值、角色外观或通用美术系统。

两张整屏图是设计示意，不能用作 Unity 运行截图、棋子实际占屏测量、Unity UI 像素证据，或外部平台原包的品质批准。只有负责人明确接受两张图的方向，`A-GZ-ART-LOOKDEV-01` 才可完成；之后仍要分别满足环境/UI 的实现验证和男主外部交易的单独批准门。

| 输入 | 作为本合同的用途 |
|---|---|
| `docs/剧情/据点/关中城.txt` | 黄土城镇、夯土墙/灰瓦/木构集市、灵矿贸易与唯一已启用的 `bounty_board`；不引入主线或 NPC。 |
| `docs/基础设定/关中野外最小环境档案.txt` | `env_guanzhong_wild` 的草地/黄土地表、唯一 `guanzhong_wild` 入口与既有六角空间事实。 |
| `assets/generated-character-art/dialogue-transparent/formal-player-default-male-v1.png` | 唯一默认男主身份、深靛外袍/灰白内衫、半束黑长发与克制修行姿态的视觉参考。 |
| `assets/source/characters/combat-pieces/shijiahou-static-3d-v1/shijiahou_static3d_v1_contact-sheet.png` | 石甲兽的低伏重型四足、层叠风化石甲与哑光灰褐材质轮廓；不是 Unity 导入结果。 |
| `SceneBuildSupport.BeginScene`、`AdventureSceneBuilder.Build`、`SettlementSceneBuilder.Build`、`AdventureHudPresenter.Present` | 现有固定镜头、1920×1080 Canvas、关中城/战斗两个表面的真实 UI 创建位置和动态节点按钮事实。 |
| `CombatPresentationContracts.cs` | 仅有 `Idle`、`Move`、`Attack`、`Hit`、`Cast`、`Death` 六个表现事件；不推导 Guard/Block。 |

## 2. 画面基准与非实机尺度说明

- 镜头设计基准来自 `SceneBuildSupport.BeginScene`：正交、`orthographicSize=6.2`、位置 `(0,8,-10)`、`pitch=38°`、`yaw=0°`；两个样板均以此作为构图意图，不声称从运行时测得尺寸。
- UI 参考基准为 Canvas `1920×1080`。现有据点主面板约为屏幕 `8%..55% × 8%..92%`，悬赏面板约为 `57%..95% × 10%..90%`；Adventure 左侧节点面板约为 `2%..28% × 45%..96%`，右侧 Combat HUD 约为 `62%..98% × 5%..95%`。
- 战斗板中两枚棋子各约为画面高度的十分之一，是后续美术审阅的设计目标，不是屏幕测量、碰撞范围、格位尺寸或镜头补偿常量。中间六角地面及其上方是必须保持可读的无 UI 区。
- 共同色彩：低饱和黄土/灰绿草地、深靛默认男主、灰褐石甲兽、深靛墨色 UI，灰白/旧黄铜只用于层级分隔。共同材质：夯土、灰瓦、旧木、铁木、粗石、哑光织物；不得以高亮魔法材质或霓虹替代。

## 3. 两张可审阅样板

| 样板 | 文件 | SHA-256 | 画面承诺 |
|---|---|---|---|
| 关中城与悬赏 | `assets/generated-scene-art/guanzhong-first-bounty/lookdev/city-and-bounty-v1.png` | `4597716836798662ff01ebfc073f06b78ddcc3c1ddb950173fb40ffbd6775bf3` | 关中夯土城、灰瓦木构、无 NPC 的悬赏板；左侧唯一男主肖像区、右侧空白可编辑悬赏卡及一枚石甲兽目标轮廓。 |
| 野外战斗与 HUD | `assets/generated-scene-art/guanzhong-first-bounty/lookdev/battle-and-hud-v1.png` | `7ef61ba3da4ce54d68b4f8eed7402d9f990de219e930a04b09483f504406c3b3` | 草地/黄土六角地面，中央仅一名深靛男主棋子与一只重型四足石甲兽；两侧为空白、可编辑 HUD 安全区。 |

两图均为 RGB `1920×1080` PNG。生成输入、完整提示词与确定性缩放元数据记录在 `assets/generated-scene-art/guanzhong-first-bounty/lookdev/prompts.md` 和同目录 `manifest.json`；原始工具输出没有带入仓库。两图已经实际查看：没有可读文字、第三名角色、额外怪物、武器、法术光效、Guard/Block 标志或水印。

## 4. 五个既有生产工作面

| 工作面与所有者 | 冻结产物数量与字面量路径 | 来源与消费者 | 轮廓、材质、层级与返工上限 |
|---|---|---|---|
| 整屏基准 `A-GZ-ART-LOOKDEV-01` | 2 张整屏 PNG、1 份提示词、1 份 manifest、1 份批准记录：`assets/generated-scene-art/guanzhong-first-bounty/lookdev/city-and-bounty-v1.png`、`battle-and-hud-v1.png`、`prompts.md`、`manifest.json`、`开发管理/关中整屏美术基准与生产批准记录.txt` | 消费既有男主母版和石甲兽联系表；消费者是后续受影响的生产、接线与验收任务。 | 候选时已消费 2 次内置生成；历史上限剩余 2 次，但 C 已否决方向，剩余额度不构成继续或定点修订授权。 |
| 默认男主唯一原包 `A-CHAR-BATTLE-STATIC3D-PLAYER-AI-RAW-01` → `A-CHAR-BATTLE-STATIC3D-PLAYER-ASSET-04` | 原包阶段恰 1 ZIP、1 manifest、1 联系表、1 记录：`assets/source/characters/combat-pieces/formal-player-static-3d-v2/raw/formal_player_static3d_v2_platform_raw.zip`、`formal_player_static3d_v2_platform_manifest.json`、`formal_player_static3d_v2_platform_contact_sheet.png`、`开发管理/默认男主AI模型原包生成记录.txt`。清理阶段恰 1 blend、1 FBX、1 BaseColor、1 manifest、1 联系表和既有稳定 Unity FBX/BaseColor 路径。 | 只消费获批的四视图和固定身份；原包消费者为 ASSET-04，清理资产消费者为 `U-CHAR-BATTLE-STATIC3D-PROFILES-01`。 | 棋偶必须保留深靛/灰白/深棕/极少旧银、七头身内部比例、`Y=0` 接地、`Y=1.03m` 最高点、`±0.30m` 水平包络、`+Z` 正面和六向既有合同。原包只允许 1 次获批外部平台任务；清理允许 1 次初始导出加最多 2 次已经查明根因的本地修正/复导出，不重设设计。 |
| 正式战场 `U-GZ-ART-BATTLEFIELD-01` | 1 个源 `.blend`、1 个源 FBX、1 张 BaseColor、1 个源 manifest：`assets/source/environments/guanzhong-first-bounty/guanzhong_terrain_modules.blend`、`guanzhong_terrain_modules.fbx`、`guanzhong_terrain_basecolor.png`、`manifest.json`；各 1 个 Unity FBX/BaseColor/材质/Prefab 对应既有任务卡路径。 | 消费 `env_guanzhong_wild` 的草地/黄土和现有合法格位；消费者为 `AdventureSceneBuilder`、正式 Adventure 场景和 FORMAL-WIRING。 | 一个共享六角顶面/侧壁组与最多三项无碰撞语义的少量石木装饰，所有装饰低于棋子读数层，不增加阻挡/掩体/高度规则。1 次源生产，最多 1 次已定位的本地导出/导入修正；失败不能通过改镜头或地图掩盖。 |
| 关中城与 UI `U-GZ-ART-UI-01` | 1 张城镇背景、1 张 UI atlas、1 张男主确定性头像裁切、1 个 manifest：`assets/source/ui/guanzhong-first-bounty/guanzhong_city_background.png`、`guanzhong_ui_atlas.png`、`formal_player_default_portrait.png`、`manifest.json`；各 1 个对应 Unity PNG 与 `.meta`。 | 背景取关中城主题，头像只从获批透明母版裁切；消费者为 `SettlementSceneBuilder`、`AdventureSceneBuilder` 和 `AdventureHudPresenter` 的既有对象。 | 层级从下至上：世界/背景、深靛半透明底板、灰白/旧黄铜边框、图标，再由可编辑运行时文本/按钮覆盖。只可用 3 枚最小功能图标：石甲兽目标、矿料/奖励、通用状态圆点；现有行动按钮继续用独立文本，不新增 Guard/Block 图标。1 批源图，最多 1 次同主题的已定位局部修订；不得更换全局 UI 主题。 |
| 六事件反馈 `U-GZ-ART-FEEDBACK-01` | 1 个 source manifest、5 条 cue：`assets/source/combat-feedback/guanzhong-first-bounty/manifest.json`、`move.wav`、`attack.wav`、`hit.wav`、`cast.wav`、`death.wav`；1 个有限 VFX Prefab/材质和 5 个对应 Unity 音频路径。 | 消费 FORMAL-WIRING 已存在的六事件端口；消费者是两个正式 profile 的既有 provider。 | `Idle` 默认为静默无持续 FX；其余 cue 均须短促、可读、不遮棋子/格位。攻击、受击、施法、死亡只有已存在事件时才表现；不新增 Guard/Block。每个 cue/Prefab 1 次初制，最多 1 次由同一事件证据定位的局部修正；规则或根动作问题返回原所有者。 |

`U-CHAR-BATTLE-STATIC3D-PROFILES-01`、`U-CHAR-BATTLE-STATIC3D-FORMAL-WIRING-01` 和 `V-GZ-ART-SLICE-01` 是上述产物的消费者、接线与验收门，不是第六个美术生产工作面：它们不得额外生成角色、环境、UI 图库或反馈来源。

### 4.1 后续正式资产的字面量路径

下表补全第 4 节的 Unity 导入/消费者路径；它是数量和归属的合同，不授权提前创建、导入或改动任一路径。

| 工作面 | source 产物（数量） | Unity 导入或直接消费者（数量） |
|---|---|---|
| 默认男主 RAW → ASSET | 1 个 `.blend`、1 个 FBX、1 张 BaseColor、1 个 manifest、1 张联系表：`assets/source/characters/combat-pieces/formal-player-static-3d-v2/formal_player_static3d_v2.blend`、`assets/source/characters/combat-pieces/formal-player-static-3d-v2/formal_player_static3d_v2.fbx`、`assets/source/characters/combat-pieces/formal-player-static-3d-v2/formal_player_static3d_v2_basecolor.png`、`assets/source/characters/combat-pieces/formal-player-static-3d-v2/formal_player_static3d_v2_manifest.json`、`assets/source/characters/combat-pieces/formal-player-static-3d-v2/formal_player_static3d_v2_contact-sheet.png` | 1 个 Unity FBX、1 张 Unity BaseColor：`src/Assets/Art/Characters/CombatPieces/Static3D/FormalPlayer/FormalPlayer_Static3D.fbx`、`src/Assets/Art/Characters/CombatPieces/Static3D/FormalPlayer/FormalPlayer_Static3D_BaseColor.png`；后续 Profile 消费时才建立 `src/Assets/Art/Characters/CombatPieces/Static3D/FormalPlayer/FormalPlayer_Static3D.mat` 和 `src/Assets/Art/Characters/CombatPieces/Static3D/FormalPlayer/FormalPlayer_Static3D.prefab`。 |
| 正式战场 | 第 4 节已列的 1 个 `.blend`、1 个 FBX、1 张 BaseColor、1 个 manifest | 1 个 Unity FBX、1 张 BaseColor、1 个材质、1 个 Prefab：`src/Assets/Art/Environments/Guanzhong/Guanzhong_TerrainModules.fbx`、`src/Assets/Art/Environments/Guanzhong/Guanzhong_Terrain_BaseColor.png`、`src/Assets/Art/Environments/Guanzhong/Guanzhong_Terrain.mat`、`src/Assets/Art/Environments/Guanzhong/Guanzhong_Battlefield.prefab`。 |
| 关中城与 UI | 第 4 节已列的 1 张城镇背景、1 张 UI atlas、1 张头像、1 个 manifest | 3 张 Unity PNG：`src/Assets/Art/UI/Guanzhong/Guanzhong_City_Background.png`、`src/Assets/Art/UI/Guanzhong/Guanzhong_UI_Atlas.png`、`src/Assets/Art/UI/Guanzhong/FormalPlayer_Default_Portrait.png`；只由既有 `SettlementSceneBuilder`、`AdventureSceneBuilder`、`AdventureHudPresenter` 消费。 |
| 六事件反馈 | 第 4 节已列的 1 个 manifest 与 5 条 cue | 5 条 Unity 音频、1 个 VFX Prefab、1 个 VFX 材质：`src/Assets/Art/Audio/Guanzhong/move.wav`、`src/Assets/Art/Audio/Guanzhong/attack.wav`、`src/Assets/Art/Audio/Guanzhong/hit.wav`、`src/Assets/Art/Audio/Guanzhong/cast.wav`、`src/Assets/Art/Audio/Guanzhong/death.wav`、`src/Assets/Art/VFX/Guanzhong/Guanzhong_CombatFeedback.prefab`、`src/Assets/Art/VFX/Guanzhong/Guanzhong_CombatFeedback.mat`。 |

## 5. 现有交互、UI 与事件边界

- 关中城仅围绕已启用的 `bounty_board` 生产视觉。坊市、客栈、情报仍是禁用功能；不把它们画成可用入口。
- 头像只使用默认男主透明母版的确定性裁切；不得恢复组件头像、模块化换装或再生成角色。
- 战场只表现当前两名单位和草地/黄土。石木只做空间材质，不写入地块、碰撞或规则状态。
- 城镇/战斗所有复杂文字、悬赏内容、按钮名称、数值和日志都在运行时可编辑层；生成图中不烘焙文本。
- `AdventureHudPresenter.Present` 创建的动态节点按钮与 `AdventureSceneBuilder.Build` 创建的既有战斗动作条都需使用同一底板/边框层级，不能只美化静态 Builder。
- 反馈仅消费 `Idle/Move/Attack/Hit/Cast/Death`。`CombatUnitPresentationTargetResult` 的伤害或死亡字段不授权新增防御、格挡或新战斗语义。

## 6. 默认男主原包：一次具体交易批准材料

### 已冻结输入

| 输入 | SHA-256 |
|---|---|
| 四视图母图 `formal_player_default_male_fourview.png` | `2cc813319737a2e56d54a7936b24228e860ea50d5f960f9fb143695aeea477c1` |
| 正面 `formal_player_default_male_front.png` | `2a8313b6e7312198c8be5cbd62651339c8bb8f5945639f2dcab768c3b2c65801` |
| `face-left-side` | `506578eca5e7cd04ca5c713ebac34998016d3d550c90344275028fc848e96c79` |
| `face-right-side` | `c4f0b8a3b12d908fdd62e938643cadeae5e3562c4090b5052328ed748cf09f31` |
| 背面 `formal_player_default_male_back.png` | `4497577b3fcea41b9ff71dc639916381d169245c312acb0e05f1a446a33ea5e9` |

资产 ID 固定为 `combat_player_default_v1`，路线固定为 `approved_multiview_external_ai_then_blender_cleanup`。这些哈希取自已经归档的四视图完成记录；本 worktree 未物化或上传四视图文件。

### 2026-09-12 可用性与额度事实

- 只读打开 Tripo Studio 公开工作台，页面显示“输入图片，一键生成 3D 模型”、`最高质量` 与 `干净拓扑`选项，因此它是可被提出交易批准的图生 3D 服务候选。
- 同一页面可见 `注册/登录`，没有已验证的登录会话、余额、套餐、当日额度、当前报价或模型设置。故 **本日可用 credits=未知，费用=未知，不能把历史会员或历史消耗当成当日额度**。
- 任务列表只保留历史参照：2026-08 的独立 Tripo 任务曾出现 40 与 45 credits；它不是本次价格承诺。为一次具体批准材料，建议硬上限为 **45 Tripo credits**、一次 `最高质量` 图生 3D 任务、无付费附加项、无第二平台、无自动重试。若当日页面显示更高费用、不同条件或无法核验余额，此建议自动失效而非提高上限。

负责人若愿意另行批准交易，必须在提交前同时明确：负责人、日期时间、Tripo Studio、具体服务/模型设置、`45` credits 或更低的最大额度、上表五项输入哈希、资产 ID `combat_player_default_v1`。缺任一字段不得上传、生成、下载或消费。LOOKDEV 整屏方向的批准不能替代这份交易批准。

## 7. 验收、成本与停止条件

- 本轮的无费用产出只有两张样板与文档；内置 ImageGen 调用为 2/4，外部 3D 平台调用、上传、下载、购买、credits 消耗、Blender、Unity 和正式资产导入均为 0。
- 后续每卡保存：实际源/生成/返工次数、等待时间、外部费用、Unity 接入时间和验证时间；未知值写“未知”，不能倒填推测值。
- 任一实际实现发现需要第三角色、额外图标库、地图规则、镜头私有偏移、第二表现控制器、第二平台或未批准付费，停止并返回准确所有者；不得以此合同自动扩大范围。
- 2026-09-27 用户明确最终验收方式：首包实现完成后，由 `V-GZ-ART-SLICE-01` 交付同版本的可操作正式入口、启动/操作说明与跑测步骤；用户本人完成关中悬赏→战斗→回城领奖及重复领奖/保存读取核对，并明确接受整体品质，才可裁决整段里程碑。用户跑测反馈、版本与决定记入既有验收记录；截图和连续录像只作辅助，概念图、AI代跑、技术导入或单卡通过均不能替代本人实际跑测。
