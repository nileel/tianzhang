# 关中首个悬赏流程美术首包生产合同

日期：2026-09-28
状态：**`DEC-20260923-GZLOOKDEVV2` 的 A 回执已接受两张 v2 整屏方向；`A-GZ-ART-LOOKDEV-01` 仅据此完成本合同和批准记录的收口。两图仍是设计示意，不是 Unity 实机、正式资源验收、玩法批准或外部平台交易授权。**

## 1. 目的、批准门与固定输入

本合同只服务既有的 `guanzhong_city`、`guanzhong_wild`、默认男主和 `enemy_shijiahou` 首流程；不新增 NPC、地区、怪物、玩法、数值、角色身份或通用美术系统。

- 已核验的整屏方向决定为 `DEC-20260923-GZLOOKDEVV2 = A`，回复时间为 2026-09-23 10:29:37（香港／北京时间），`evidenceHash=95f369820a032c12da76c54702249f1dea0a6b259f2b19c35c82c92ccc537fc9`。
- 已实际查看的新版样板是 `assets/generated-scene-art/guanzhong-first-bounty/lookdev-ui-v6-20260923/city-and-bounty-v2.png`（1672×941，SHA-256 `154666a8b55dd4647f34c86c40af91aff3b2913c1a31dc07798e95fb0a56a3ca`）和 `battle-and-hud-v2.png`（1672×941，SHA-256 `f9b7af157404ef118d14124d6894112cda5c5b72faef106ca56b8a159387079c`）；完整生成提示词位于同目录 `prompts.md`，提交为 `b290cae0000c4bea839b58158ddad9a3f0978821`。
- 城镇只采用 `docs/剧情/据点/关中城.txt` 的黄土城、夯土墙、灰瓦、木构集市、灵矿商号和唯一已启用的 `bounty_board`。坊市、客栈、情报不得因样板中的标签变成可操作功能。
- 野外只采用 `docs/基础设定/关中野外最小环境档案.txt` 的 `env_guanzhong_wild`、草地／黄土地表和六格空间。已完成的正式地块仍是青绿草顶、自然边缘和青蓝岩壁；示意图中的山峰、瀑布、岩壁延展或怪物细节不增加环境或角色事实。
- 默认男主唯一视觉身份来自 `assets/generated-character-art/dialogue-transparent/formal-player-default-male-v1.png`（SHA-256 `399175e1d5f2ca81fbd246c43a4cc02b2867721ad13df429340d9950698f4948`）：半束黑长发、深靛外袍、灰白内衫、深色腰封和克制剑指姿态。石甲兽在整屏图中只作一只低伏四足轮廓；旧 `shijiahou_static3d_v1_contact-sheet.png` 已查看但成品品质批准已撤销，不是本合同的正式资产或品质基准。

## 2. 画面与运行时边界

- 样板构图参考 `SceneBuildSupport.BeginScene` 的正交镜头、`orthographicSize=6.2`、位置 `(0,8,-10)`、pitch `38°`、yaw `0°` 与 `CreateCanvas` 的 `1920×1080` 参考分辨率。两枚棋子约占画面高度十分之一是构图意图，不是运行时测量、碰撞、格位或镜头常量。
- `SettlementSceneBuilder.Build` 已有左侧据点面板与右侧悬赏板；`AdventureSceneBuilder.Build` 已有左侧 Adventure 节点面板和右侧 Combat HUD；`AdventureHudPresenter.Present` 动态创建节点按钮。合同只要求后续 UI 工作面在这些既有表面接线，不能从示意图重做布局或新增 UI 系统。
- `1920×1080` 参考下的既有安全区来自 Builder anchor：据点主面板约 `8%..55% × 8%..92%`、悬赏板 `57%..95% × 10%..90%`、Adventure 节点面板 `2%..28% × 45%..96%`、Combat HUD `62%..98% × 5%..95%`。它们是后续接线的现有表面依据，不是示意图测量值。
- 当前战斗表现合同只有 `Idle`、`Move`、`Attack`、`Hit`、`Cast`、`Death` 六事件。示意图的招式、数值、奖励、文字、行动栏、怪物造型和图标都是不可采信的示例；不得推导 Guard／Block、额外事件、奖励或操作。
- 共同视觉基准为低饱和黄土／灰绿草地、深靛男主、灰褐石甲兽轮廓、深蓝绿面板、灰白／旧黄铜分隔线，以及夯土、灰瓦、旧木、粗石、哑光织物。UI 从下至上固定为世界／背景、深靛半透明底板、灰白／旧黄铜边框、图标、独立的运行时文字与按钮；复杂文字绝不烘焙到资源图。

## 3. 五个既有生产工作面

| 工作面与所有者 | 冻结数量与字面量路径 | 来源与消费者 | 轮廓、材质、层级与返工上限 |
|---|---|---|---|
| 整屏基准 `A-GZ-ART-LOOKDEV-01`（已完成） | 2 张 PNG、1 份提示词、1 份说明、1 份批准记录：`assets/generated-scene-art/guanzhong-first-bounty/lookdev-ui-v6-20260923/city-and-bounty-v2.png`、`battle-and-hud-v2.png`、`prompts.md`、`README.md`、`开发管理/关中整屏美术基准与生产批准记录.txt`。 | 消费已批准男主身份和当前地块视觉词汇；为 `A-GZ-UI-CITY-ASSET-01`、`U-GZ-ART-UI-01`、角色链和最终验收提供方向，不直接提供 Unity 资源。 | 已消费 2 次内置生成，现不再追加生成或修订。城镇保持关中主题；战斗保持两单位与同高六格；两图只冻结方向。 |
| 默认男主唯一原包 `A-CHAR-BATTLE-STATIC3D-PLAYER-AI-RAW-01` → `A-CHAR-BATTLE-STATIC3D-PLAYER-ASSET-04` | RAW 恰 1 ZIP、1 manifest、1 联系表、1 记录：`assets/source/characters/combat-pieces/formal-player-static-3d-v2/raw/formal_player_static3d_v2_platform_raw.zip`、`formal_player_static3d_v2_platform_manifest.json`、`formal_player_static3d_v2_platform_contact_sheet.png`、`开发管理/默认男主AI模型原包生成记录.txt`；清理阶段恰 1 blend、1 FBX、1 BaseColor、1 manifest、1 联系表和既有 Unity FBX／BaseColor 路径。 | 只消费四视图和冻结身份；RAW 消费者是 ASSET-04，清理资产消费者是 `U-CHAR-BATTLE-STATIC3D-PROFILES-01`。 | 深靛／灰白／深棕、七头身内部比例、`Y=0`、最高 `Y=1.03m`、`±0.30m` 包络、`+Z` 正面和六向合同不变。原包只允许一次获批平台任务；清理仅一次初始导出加至多两次已查明根因的本地修正。 |
| 正式战场 `U-GZ-ART-BATTLEFIELD-01`（已完成） | 2 个原 ZIP、1 份规范化、1 份 manifest 与已归档 Unity 导入：`assets/source/environments/guanzhong-first-bounty/initial-v1/terrain-source.zip`、`stairs-source.zip`、`normalization.json`、`manifest.json`、`src/Assets/Art/Environments/Guanzhong/Tile.fbx`、`Tile_basecolor.JPEG`、`Tile.mat`、`Tile.prefab`、`Stair.fbx`、`Stair_basecolor.JPEG`、`Stair.mat`、`Stair.prefab`。 | 消费 `env_guanzhong_wild` 的草地／黄土和既有六格；`AdventureSceneBuilder`、正式 Adventure 与 FORMAL-WIRING 消费同一 Tile Prefab。 | 只使用已批准同高六格平地；石阶仅备用，不宣称高差通行。不得以改镜头、格位、碰撞或地图掩盖资源问题。 |
| 关中城与 UI `A-GZ-UI-CITY-ASSET-01`／`U-GZ-ART-UI-01` | 恰 1 张城镇背景、1 张 UI atlas、1 张确定性头像、1 个汇总 manifest：`assets/source/ui/guanzhong-first-bounty/guanzhong_city_background.png`、`guanzhong_ui_atlas.png`、`formal_player_default_portrait.png`、`manifest.json`；对应 Unity PNG 为 `src/Assets/Art/UI/Guanzhong/Guanzhong_City_Background.png`、`Guanzhong_UI_Atlas.png`、`FormalPlayer_Default_Portrait.png`。 | 背景取关中城主题，头像只从批准母版确定性裁切；既有 Settlement／Adventure／HUD 表面消费。 | 图标至多石甲兽目标、矿料／奖励、通用状态圆点 3 枚；文字和按钮独立。城市背景 1 批加至多 1 次有明确原因的局部修订；不得更换全局 UI 主题。 |
| 六事件反馈 `U-GZ-ART-FEEDBACK-01` | 1 个 source manifest、5 条 cue、1 个有限 VFX Prefab、1 个材质：`assets/source/combat-feedback/guanzhong-first-bounty/manifest.json`、`move.wav`、`attack.wav`、`hit.wav`、`cast.wav`、`death.wav`、`src/Assets/Art/VFX/Guanzhong/Guanzhong_CombatFeedback.prefab`、`Guanzhong_CombatFeedback.mat`。 | 只消费 FORMAL-WIRING 的六事件端口；两个正式 profile 消费结果。 | `Idle` 静默；其余 cue 短促、不遮棋子／格位。每项 1 次初制、至多 1 次同事件证据定位的局部修正；不新增 Guard／Block。 |

`U-CHAR-BATTLE-STATIC3D-PROFILES-01`、`U-CHAR-BATTLE-STATIC3D-FORMAL-WIRING-01` 和 `V-GZ-ART-SLICE-01` 只消费、接线或验收上述产物，不是第六个生产工作面。

## 4. 默认男主唯一交易的费用边界

- 资产 ID 固定为 `combat_player_default_v1`，路线固定为 `approved_multiview_external_ai_then_blender_cleanup`。批准的四视图 SHA-256 依次为：正面 `2a8313b6e7312198c8be5cbd62651339c8bb8f5945639f2dcab768c3b2c65801`；鼻尖朝左 `506578eca5e7cd04ca5c713ebac34998016d3d550c90344275028fc848e96c79`；鼻尖朝右 `c4f0b8a3b12d908fdd62e938643cadeae5e3562c4090b5052328ed748cf09f31`；背面 `4497577b3fcea41b9ff71dc639916381d169245c312acb0e05f1a446a33ea5e9`；输入 manifest `112c971b2282efb03b2a7abb3b9c75ff873a540bfa1c0228f077eef4741d71d9`。
- `DEC-20260923-PLAYERRAW45 = A` 已授权 Tripo Studio 最高质量图生静态 3D 的同一方案、一次、无绑定／付费附加项／第二次生成，最高 45 credits；回执证据为 `4eecf5926baf9074786b506f435f4b343955495d2d996cb2ece5514d10227a47`。这是上限，不是已核实价格、余额或当日可用额度。
- RAW 执行时必须在上传前现场核实同一四视图支持、会话、服务设置、报价和余额；当前实际报价、余额与日额度均未知。任一条件不符即停止，不换服务、不提价、不上传、不消费。LOOKDEV 的整屏 A 回执不能替代这项费用门。

## 5. 验收、成本与停止条件

- 两张 v2 整屏图均已查看，图文、哈希、用途和接受证据可追溯。A 回执只解除视觉方向门；城市背景、角色原包、UI 接线、反馈、正式角色、连续流程和整段品质仍分别由原所有者验收。
- 各生产卡分别记录实际制作／返工次数、等待、外部费用、Unity 接入和验证时间；未知一律记“未知”，不得倒填推测值。本卡没有上传、采购、credits 消耗、Blender、Unity 或正式资源导入。
- 发现需要第三角色、额外图标库、地图／碰撞／镜头规则、第二表现控制器、第二平台、额外费用或不同视觉方向时，停止并返回准确所有者；不得把样板、历史投入或本合同当作扩范围授权。
- 最终仅由 `V-GZ-ART-SLICE-01` 提供同版本可操作正式入口与跑测步骤，并由用户本人完成悬赏→战斗→回城领奖、重复领奖和保存读取核对后接受整体品质。截图、录像、概念图、AI 代跑或单卡通过都不能替代该验收。
