# 默认男主正式原画到静态 3D v2 生产链规格

日期：2026-08-29

状态：用户已复核通过的批准设计；角色定义、生产链、合同裁决和改用项目绿幕抠图合同后的完整原画提示词均已批准。用户于 2026-08-29 另行明确要求建立任务卡，现只授权按 §6 形成管理投影；仍未授权生成图片、调用外部平台、消费 credits、操作 Blender 或继续 Profile 实现。

## 1. 目标与边界

本生产链为《天章》建立一个固定的通用默认男主，从正式对话立绘母版依次生产严格四视图、AI 平台原始 3D 包、Blender 清理资产和 Unity 可验证 FBX。

本链只解决默认男主视觉身份及可导入静态 3D 资产，不继续实现 Profile 映射，不修复、重导出或替换既有 `formal-player-static-3d-v1`，也不覆盖既有失败证据。既有任务 `U-CHAR-BATTLE-STATIC3D-PROFILES-01` 保持 blocked，直至本链最终任务通过 Unity importer 数值验收。

稳定运行时身份保持：

- `player -> combat_player_default_v1`

现有粗模及冻结证据保持原路径和历史不变；新资产使用 v2 路径和独立事实记录。

### 1.1 与 2026-08-27 冻结合同的关系

`docs/superpowers/specs/2026-08-27-formal-static-3d-combat-piece-asset-contract.md` 是 v1 正式玩家与石甲兽的冻结生产合同，并在正文末尾明确只给 `A-CHAR-BATTLE-STATIC3D-PLAYER-ASSET-01` 与 `A-CHAR-BATTLE-STATIC3D-SHIJIAHOU-ASSET-01` 提供共同输入。该文件继续作为 v1 和石甲兽历史事实，不回写、不覆盖。

本规格依据 2026-08-29 用户对新默认男主的逐项明确决定，构成正式玩家 v2 的后续修订事实。只对玩家侧作以下替换：

| 旧合同玩家条款 | v2 裁决 |
| --- | --- |
| 「未命名的成年修士棋偶，而非任何固定主角、肖像或可选外观」 | 改为无名、16 岁、固定默认男主视觉身份；仍不是命名 NPC、真人肖像或可选外观。该身份同时服务对话立绘和默认战斗棋偶。 |
| 脸部、发型和服饰保持中性 | 改为本规格第 2 节的具体脸型、半束长发与深靛蓝旅行道袍；仍不得识别为苻渊、古天乐、杨过或其他现有角色、真人。 |
| 少量旧铜 | 改为极少量旧银色扣件；仍保持低饱和、哑光和材质可区分。 |
| `sourceRoute=blender_native`、零外部输入 | 改为「批准原画 -> 批准四视图 -> 一次获批 AI 平台原包 -> Blender 清理」；外部交易仍必须先通过最大 credits、输入哈希和资产 ID 的人工授权门。 |
| v1 source 文件 | 改由 `formal-player-static-3d-v2` source 文件供应；v1 文件只保留为历史和失败证据。 |

下列合同继续原样约束 v2：`player`、`combat_player_default_v1`、不读取未建立的外观档案、单一不透明非发光 BaseColor、一个命名材质、静态网格、无运行时骨骼动画、`Y=0` 接地、最高点 `Y=1.03m`、水平包络 `X=±0.30m` 与 `Z=±0.30m`、局部 `+Z` 正面、单位缩放以及六向 `90/150/210/270/330/30`。稳定 Unity 目标名继续复用，但正式玩家 FBX/BaseColor 的来源改为本规格 v2。

若本规格与旧合同其他玩家条款发生未列出的冲突，必须停止并先取得用户裁决；不得由执行者自行扩大替换范围。石甲兽条款全部不受本规格影响。

## 2. 已批准角色定义

### 2.1 身份与年龄

- 无名、固定的通用默认男主，不是苻渊或其他命名 NPC。
- 16 岁，中国男性修士，默认出身关中小族。
- 气质沉静、自持、认真，带轻微警觉；不虚弱、不傲慢、不阴郁、不冷酷、不显得饱经沧桑。
- 不提供外貌自定义分支。

### 2.2 体态与面部

- 约七头身，身形清瘦挺拔，肩宽适中，四肢自然修长，有轻度训练感但不壮硕。
- 保留可信的 16 岁柔和感；不幼童化、不脆弱、不女性化、不 Q 版。
- 窄椭圆脸，下颌清楚但不尖锐。
- 肤色自然白皙、带健康暖调。
- 眼睛纯黑，目光清澈、平静、专注。
- 自然平直黑眉，眉尾略扬，不做凌厉剑眉。
- 鼻梁清楚、挺直但不过高，鼻头自然小巧。
- 唇形自然偏薄、颜色克制，嘴角放松中性。
- 无妆容、胡须、伤疤、纹身、额印、发光眼或异色瞳。

### 2.3 发型

- 黑色长发，上半部在头顶简单束起，其余分成连贯发束自然垂落至肩胛骨下方，绝不到腰。
- 脸两侧各保留一束自然长鬓发。
- 只借鉴古装武侠半束长发的结构思路；不得复制古天乐、杨过或任何真人、命名角色的脸和形象。
- 不使用冠、华丽发饰、珠宝、散乱飞丝、过量碎发或孤立细发带。

### 2.4 服装与配色

- 关中小族年轻修士的实用旅行道袍。
- 深靛蓝外袍、灰白交领内衫、深棕皮革腰带、极少量旧银色扣件。
- 袖口收束；下摆分层并明确开片；不使用夸张宽袖。
- 深棕近黑轻便布靴，窄靴口、软底，无甲片和装饰。
- 显示清楚的领口结构、滚边、缝线和少量低对比织纹。
- 不出现族徽、宗门标识、大面积刺绣、玉佩、身份牌、首饰、兵器、法器、符箓、盔甲、披风或魔法配件。

### 2.5 对话立绘姿态

- 正式原画是透明全身对话立绘母版，同时作为后续身份事实源；暖灰底图只用于审批预览。
- 角色以温和的三分之四侧身朝画面右侧，适合放在对话 UI 左侧。
- 身体自然挺立、重心稳定，双脚完整着地；不采用大跨步、扭身、战斗、施法、胜利、A-pose 或 T-pose。
- 右前臂适度抬至下胸前，右手作单手剑指：食指与中指并拢伸直，其余手指自然收拢。
- 右肘靠近躯干但保持可读间隙，剑指手位于近镜头一侧，不遮挡脸、领口或主要服装结构。
- 左臂自然下垂，左手完整可见。
- 手印是日常修行习惯动作，不产生法术；不得出现光效、气场、粒子、烟雾、符文、能量或风效。
- 目光投向右前方的对话对象，不直视观众；不微笑、不皱眉、不低头、不仰头。

### 2.6 项目统一画风

画风必须继承已批准苻渊立绘的项目级视觉语言。参考图：

`assets/generated-character-art/dialogue-transparent/fu-yuan-hanhong-zhenjun-standing-black-gold-v1.png`

该图只提供精致中式修仙动漫插画品质、手绘完成度、线条精度、克制光影层级、面部刻画、布料质感与整体制作标准。不得继承苻渊的身份、年龄、脸、白发、胡须、神态、黑金服装、饰品、姿态、地位或经历。

### 2.7 解剖与完整构图

- 画面只出现一人、一头、双臂、两只结构正确的手和两只完整可见的脚；每只可见手必须为五指。
- 不允许重复或融合手指、额外或缺失肢体、断开发束、透明衣服、断裂衣片或身体裁切。
- 正式 3D 棋偶继承角色身份和七头身内部比例，但整体高度必须缩放并冻结为本地 `Y=1.03m`；七头身不表示 1.7m 世界尺度。

## 3. 已批准的正式原画完整提示词

每次 ImageGen 调用必须使用一份用户逐字批准的完整提示词。下列文本保留已批准的角色定义，并把输出合同改为项目既有的纯绿幕源图与本地抠图流程；用户已于 2026-08-29 明确通过该完整文本：

```text
Use case: character-concept
Asset type: chroma-key source for the canonical transparent full-body dialogue portrait and identity source of the unnamed default male player character in the 2D xianxia game 《天章》.

Reference usage:
Image 1 is the approved Fu Yuan dialogue portrait. Use it ONLY as the project-wide visual-style reference: inherit the same refined Chinese xianxia anime illustration quality, hand-painted finish, line precision, restrained light-and-shadow hierarchy, facial rendering quality, cloth material treatment, and overall production polish.

Do NOT inherit Fu Yuan’s identity, age, face, white hair, beard, expression, black-and-gold robes, ornaments, pose, social status, or character history. The new character must not resemble Fu Yuan, Gu Tianle, Yang Guo, or any existing named character or real person.

Character identity:
Create one unnamed 16-year-old Chinese male cultivator who serves as the fixed default player protagonist. He comes from a minor clan in the Guanzhong region. He is young, composed, earnest, and quietly alert, without looking weak, arrogant, gloomy, cruel, or world-weary.

Body and proportions:
Approximately seven heads tall. Slim, upright, and lightly trained, with moderate shoulder width and naturally long limbs. He should retain a believable sixteen-year-old softness and youthfulness without appearing childlike, frail, muscular, bulky, feminine, or chibi.

Face:
A narrow oval face with a clear but not sharp jawline. Naturally fair skin with a healthy warm undertone. Pure black eyes with a clear, calm, focused gaze. Natural straight black eyebrows with slightly raised outer ends. A clear, straight but not overly high nose bridge with a naturally small nose tip. Naturally thin lips with restrained color and a relaxed neutral mouth. No makeup, facial hair, scars, tattoos, forehead marks, glowing eyes, colored pupils, or supernatural symbols.

Hair:
Long black hair with the upper section tied simply at the crown and the remaining hair falling in several cohesive locks to slightly below the shoulder blades, never reaching the waist. Leave one natural long face-framing lock on each side. The silhouette should feel free and elegant, inspired only by the structural idea of a classic half-tied wuxia hairstyle. Do not reproduce any actor’s or named character’s likeness. No crown, ornate hairpiece, jewelry, loose floating strands, excessive flyaway hair, tangled hair, or isolated thin ribbons of hair.

Clothing:
A practical travel Daoist robe suitable for a young cultivator from a minor Guanzhong clan:
- deep indigo outer robe
- gray-white crossed-collar inner garment
- dark brown leather waist belt
- a very small amount of aged-silver fastening hardware
- fitted wrist cuffs
- clearly layered and split lower robe panels
- dark brown, nearly black lightweight cloth boots with narrow openings and soft soles

Show clear collar construction, edging, seams, and a small amount of low-contrast woven pattern. Keep the clothing refined and production-quality but restrained. No clan emblem, sect symbol, large embroidery, jade pendant, identity tablet, jewelry, weapon, sword, staff, talisman, armor, crown, shoulder plates, cape, or magical accessory.

Pose and composition:
Create a complete full-body source illustration for the transparent mother portrait in a gentle three-quarter side view, with the character facing toward the right side of the image as if looking calmly at a dialogue partner.

His body remains naturally upright and stable. Both feet are fully visible and grounded in a simple natural stance. Do not use a dramatic step, torso twist, wind-blown pose, combat stance, spellcasting stance, victory pose, A-pose, or T-pose.

His right forearm is raised modestly before the lower chest. His right hand forms a clear single-handed sword-finger seal: index and middle fingers extended together, remaining fingers folded naturally. Keep the elbow close to, but visibly separated from, the torso. His left arm hangs naturally with the complete left hand visible.

The hand seal is a quiet habitual cultivation gesture, not an active spell. Do not add magic light, aura, particles, smoke, runes, energy, or wind. The sword-finger hand must not cover the face, collar, or major clothing construction.

Expression and gaze:
Calm, self-contained, earnest, and slightly vigilant. His gaze points toward the right-front, naturally observing a dialogue partner. He does not stare at the viewer, smile, frown, lower his head, raise his chin, or appear cold and bitter.

Rendering:
Match the approved project-wide Fu Yuan art style: refined Chinese xianxia anime character illustration, normal human proportions, controlled hand-painted shading, clear silhouette, readable fabric construction, and polished face and hands. Not photorealistic, not a photograph, not a 3D render, not western comic art, not ink-wash abstraction, and not chibi.

Output:
One complete full-body character, centered with generous uniform green padding around the hair, hands, robe hem, and boots. The entire background must be one perfectly flat solid #00ff00 chroma-key field with no paper texture, gradient, lighting variation, environment, floor, cast shadow, contact shadow, reflection, frame, decorative border, title, calligraphy, seal, UI, logo, watermark, or readable text. Do not use #00ff00 anywhere in the character, hair, skin, eyes, clothing, seams, hardware, or boots.

Anatomy hard constraints:
Exactly one person, one head, two arms, two anatomically correct hands, five fingers per hand, and two fully visible feet. No duplicated fingers, fused fingers, hidden sword-finger hand, extra limbs, missing limbs, detached hair, transparent clothing, broken garment panels, or cropped body parts.
```

生成后使用项目既有本地脚本把绿幕源图转换为正式 RGBA 母版：

```powershell
python 'C:\Users\WINDOWS\.codex\skills\.system\imagegen\scripts\remove_chroma_key.py' --input <green-source.png> --out <final-transparent.png> --auto-key border --soft-matte --transparent-threshold 12 --opaque-threshold 128 --despill --force --edge-contract 1
```

绿幕源图属于过程文件，完成透明边缘、四角 alpha、衣袍保真和无绿边验收后不进入项目正式路径。若首次生成或本地抠图结果未获用户批准，任务立即停止。下一次调用前必须先提交修订后的完整提示词并再次取得用户明确批准；禁止隐式改词、自动重试或批量生成变体。

## 4. 生产链与依赖

生产链只包含四张原子任务卡：

1. `A-CHAR-BATTLE-STATIC3D-PLAYER-CONCEPT-01`：正式原画与对话立绘母版。
2. `A-CHAR-BATTLE-STATIC3D-PLAYER-FOURVIEW-01`：基于唯一批准原画建立严格前、左、右、后视图。
3. `A-CHAR-BATTLE-STATIC3D-PLAYER-AI-RAW-01`：经用户另行批准平台与 credits 后，只执行一次 AI 3D 平台生成并冻结原包。
4. `A-CHAR-BATTLE-STATIC3D-PLAYER-ASSET-04`：只清理已批准原包，输出 Blender/FBX/BaseColor，并完成 Unity importer 数值验收。

依赖关系：

`CONCEPT-01 -> FOURVIEW-01 -> AI-RAW-01 -> ASSET-04 -> U-CHAR-BATTLE-STATIC3D-PROFILES-01`

只有 `CONCEPT-01` 在任务卡建立后进入 ready；其余三张按直接依赖保持 blocked。现有 Profile 任务继续 blocked，并将其直接 `blockedBy` 投影为 `A-CHAR-BATTLE-STATIC3D-PLAYER-ASSET-04`。

## 5. 任务合同

### 5.1 `A-CHAR-BATTLE-STATIC3D-PLAYER-CONCEPT-01`

目标：生成并冻结唯一批准的默认男主透明全身对话立绘母版。

预期产物：

- `assets/generated-character-art/dialogue-transparent/formal-player-default-male-v1.png`
- `assets/generated-character-art/dialogue-transparent/formal-player-default-male-v1.manifest.json`
- `assets/generated-character-art/dialogue-transparent/prompts.md`
- `assets/generated-character-art/titled/formal-player-default-male-v1-approval-preview.png`
- `开发管理/默认男主正式原画生成记录.txt`

执行合同：

- 只使用第 3 节经用户重新批准后的完整提示词和苻渊参考图执行一次 ImageGen。
- ImageGen 只生成临时纯绿幕源图；按第 3 节固定本地命令抠成 RGBA 后，透明母版才是正式事实。暖灰审批图只用于看清透明边界和全身细节。
- 正式母版、共享提示词入口和审批预览分别落在 `dialogue-transparent/` 与 `titled/` 既有用途目录；不新建脱离 `assets/generated-character-art/README.md` 的顶层分类。
- 保存输入参考、完整提示词、生成时间、生成工具记录、输出尺寸、SHA-256 和用户批准结论。
- 审批必须核对：身份非苻渊/真人、16 岁、发型结构、七头身、配色、衣服构造、单手剑指、完整双手双脚、透明背景和统一画风。
- 未获用户明确批准时不得完成，不得放行四视图任务。

停止条件：生成失败、参考图不可读取、结果出现身份漂移、解剖错误、裁切、非透明背景、未批准改词，或用户拒绝结果。

### 5.2 `A-CHAR-BATTLE-STATIC3D-PLAYER-FOURVIEW-01`

目标：从唯一批准的原画母版生产严格建模四视图，不重新设计角色。

预期产物：

- `assets/source/characters/combat-pieces/formal-player-static-3d-v2/input/formal_player_default_male_front.png`
- `assets/source/characters/combat-pieces/formal-player-static-3d-v2/input/formal_player_default_male_face-left-side.png`
- `assets/source/characters/combat-pieces/formal-player-static-3d-v2/input/formal_player_default_male_face-right-side.png`
- `assets/source/characters/combat-pieces/formal-player-static-3d-v2/input/formal_player_default_male_back.png`
- `assets/source/characters/combat-pieces/formal-player-static-3d-v2/input/formal_player_default_male_fourview.png`
- `assets/source/characters/combat-pieces/formal-player-static-3d-v2/input/prompts.md`
- `assets/source/characters/combat-pieces/formal-player-static-3d-v2/input/manifest.json`
- `开发管理/默认男主正式多视图生成记录.txt`

执行合同：

- 开始前先向用户展示四视图完整提示词，只有逐字批准后才允许一次 ImageGen 调用。
- 唯一调用产出一张含前、角色左、角色右、背四个独立全身面板的母图；只允许用本地确定性分槽裁切形成四张独立 PNG 和联系表，不得用第二次生成或 AI 补画填槽。
- 四视图采用严格正交含义：正面、角色面向左侧、角色面向右侧、背面；全身比例、相机高度、尺寸和落脚线一致。
- 建模视图使用自然对称放松站姿与清楚分离的手臂，不沿用对话剑指姿态。
- 前后左右必须保持同一脸型、头身比、发长、发束、衣片、领口、腰带、袖口、靴子和色彩。
- 左右语义按角色自身确定：角色左侧图鼻尖朝画面右，角色右侧图鼻尖朝画面左；不得以文件名或肉眼直觉颠倒槽位。
- 左右侧不得用镜像冒充；每侧可见的发束、衣片与腰带结构必须符合真实空间关系。虽然本角色无腰饰，仍须核对侧面没有凭空生成扣件、挂绳或其他远侧结构。
- 背面不得出现脸、三分之四转身或复制正面细节；只允许从批准母版最小推断后发、后袍分片、中央接缝和腰带背面，不得添加原画不存在的饰物、纹样或开口。
- 四张都必须完整包含发顶、双手、袍摆和双脚，使用相同近正交镜头、同色背景、均匀中性光和一致的七头身比例；不得因生成把任一视图拉长、缩短或改成纸偶比例。
- 保存四张独立视图及一张联络表，并记录完整提示词、参考图哈希、输出哈希和用户批准结论。
- 未获用户明确批准时不得放行 AI 原始模型任务。

停止条件：提示词未批准、身份或服装漂移、左右镜像、背面伪造、比例/相机不一致、解剖错误、视图裁切或用户拒绝结果。

### 5.3 `A-CHAR-BATTLE-STATIC3D-PLAYER-AI-RAW-01`

目标：使用批准的四视图，在用户另行明确批准的平台和 credits 范围内生成一次 AI 原始 3D 包，并原样冻结。

预期产物：

- `assets/source/characters/combat-pieces/formal-player-static-3d-v2/raw/formal_player_static3d_v2_platform_raw.zip`
- `assets/source/characters/combat-pieces/formal-player-static-3d-v2/raw/formal_player_static3d_v2_platform_manifest.json`
- `assets/source/characters/combat-pieces/formal-player-static-3d-v2/raw/formal_player_static3d_v2_platform_contact_sheet.png`
- `开发管理/默认男主AI模型原包生成记录.txt`

执行合同：

- 平台和一次调用授权必须在交易前由用户另行明确批准。预授权记录必须先写明负责人、批准日期时间、平台、模型或服务、允许的最大 credits、唯一输入四视图哈希和本资产 ID `combat_player_default_v1`；缺一项都不得上传或发起交易。
- 每次授权只对应一个平台任务和一次生成；不得同时投递多个平台或自动重试。
- 交易完成后追加记录平台任务 ID、模型设置、纹理设置、生成时间、实际 credits/费用、剩余额度、下载清单和每个文件 SHA-256；实际消耗不得超过已批准最大值。
- 原始下载内容不改名拆散，统一原样封装为规范 ZIP；manifest 记录 ZIP 内部真实文件名和格式。
- manifest 固定 `sourceRoute=approved_multiview_external_ai_then_blender_cleanup`、`externalSourceUsed=true`，并同时保存预授权与交易后记录；不得沿用 v1 的 `sourceRoute=blender_native`。
- 联络表只用于确认几何非空、正反侧身份可辨、服装层次存在和无明显缺肢；不在本卡使用 Blender 修复。
- 用户批准唯一原包后，才允许下游清理任务读取其精确哈希。

停止条件：未获平台/credits 批准、平台要求额外付费、生成失败、下载不完整、文件损坏、模型为空、明显身份错误、明显缺肢或用户拒绝原包。

### 5.4 `A-CHAR-BATTLE-STATIC3D-PLAYER-ASSET-04`

目标：只对唯一批准且哈希冻结的 AI 原始包做 Blender 清理、静态资产导出和 Unity importer 验收，不重新设计或再生成角色。

预期源资产：

- `assets/source/characters/combat-pieces/formal-player-static-3d-v2/formal_player_static3d_v2.blend`
- `assets/source/characters/combat-pieces/formal-player-static-3d-v2/formal_player_static3d_v2.fbx`
- `assets/source/characters/combat-pieces/formal-player-static-3d-v2/formal_player_static3d_v2_basecolor.png`
- `assets/source/characters/combat-pieces/formal-player-static-3d-v2/formal_player_static3d_v2_manifest.json`
- `assets/source/characters/combat-pieces/formal-player-static-3d-v2/formal_player_static3d_v2_contact-sheet.png`
- `开发管理/默认男主静态3D模型与Unity导入QA记录.txt`

预期 Unity importer 资产：

- `src/Assets/Art/Characters/CombatPieces/Static3D/FormalPlayer/FormalPlayer_Static3D.fbx`
- `src/Assets/Art/Characters/CombatPieces/Static3D/FormalPlayer/FormalPlayer_Static3D.fbx.meta`
- `src/Assets/Art/Characters/CombatPieces/Static3D/FormalPlayer/FormalPlayer_Static3D_BaseColor.png`
- `src/Assets/Art/Characters/CombatPieces/Static3D/FormalPlayer/FormalPlayer_Static3D_BaseColor.png.meta`
- `src/Assets/Art/Characters/CombatPieces.meta`
- `src/Assets/Art/Characters/CombatPieces/Static3D.meta`
- `src/Assets/Art/Characters/CombatPieces/Static3D/FormalPlayer.meta`
- `src/Assets/Tests/EditMode/FormalPlayerStatic3DImportEditorTests.cs`
- `src/Assets/Tests/EditMode/FormalPlayerStatic3DImportEditorTests.cs.meta`

执行合同：

- `automationInputs` 只登记 schema 5 允许的 `assets/source/` 输入：批准原始 ZIP、raw manifest 和四视图文件的精确字节数与 SHA-256。透明原画母版位于 Git 跟踪的 `assets/generated-character-art/`，由卡内字面量路径、任务卡 digest 和 manifest SHA-256 固定，不得伪装成 `automationInputs`。
- Blender 清理只允许修复拓扑、法线、UV、材质槽、静态姿态、比例、原点、命名和导出兼容性；不得改脸、发型、服装设计、配色、体型或添加配件。
- 输出单个静态可见角色网格、一个命名材质和一张不透明非发光 BaseColor；不要求骨骼、动画或动态布料。3D 材质把批准立绘身份解释为低饱和、哑光、手绘大色块和二至三档 Toon 明暗，不把立绘的完整光影烘成第二套视觉方向。
- 坐标与尺度合同：双脚最低承重面位于 `Y=0`，根在占地投影中心，本地 `+Z` 朝前，单位缩放冻结为 1；含发顶最高点精确为本地 `Y=1.03m`，水平包络不超过 `X=±0.30m`、`Z=±0.30m`。七头身只约束内部造型比例，不改变棋偶世界尺度。
- 联系表精确为十格：固定正、角色左、角色右、背面，以及表现根 Y yaw `90/150/210/270/330/30`；全部使用同一固定相机与光照，不增加顶部、底部或私有角度，不得用任一单视图替代。
- Blender 现场必须 factory-empty 可重复打开并回读冻结 FBX；回读网格顶点和三轴 bounds 必须非零。
- 本卡把 v2 FBX 与 BaseColor 复制到旧合同冻结的稳定 Unity 文件名，并由持久化 EditMode 导入测试读取 Unity 实际导入网格；只接管正式玩家 FBX、BaseColor、对应 `.meta` 与 importer 测试，不创建材质、Prefab、catalog 或 Profile。
- Unity 硬门：mesh 数量大于 0、总顶点数大于 0、三角形数大于 0、所有顶点位置不能全为零、X/Y/Z 三轴 bounds 均大于 0；任一失败即 blocked。
- 记录 Blender 版本、导出设置、源包哈希、blend/FBX/纹理哈希、Blender 回读数值、Unity importer 数值、测试日志和十视图审批结论。
- 只有该卡完成且通过 Unity importer 硬门，现有 Profile 任务才可重新评估为 ready。Profile 卡随后只读消费已导入的正式玩家 FBX/BaseColor，并继续负责玩家材质/Prefab、石甲兽正式 Unity 导入、双 catalog/Profile 映射和双输入综合测试。

停止条件：原始包哈希不符、需要重新生成或重新设计、无法在允许清理范围内修复、FBX 回读为空、Unity importer 顶点位置全零、任一 bounds 轴为零、Unity 测试失败或需要替换已批准输入。

## 6. 管理投影方案

本规格经用户复核后，后续独立写入轮才允许执行以下最小管理投影：

- 新建上述四张任务卡及各自的 active backlog 投影。
- 只将 `A-CHAR-BATTLE-STATIC3D-PLAYER-CONCEPT-01` 放入当前 ready 队列；其余三张保持 blocked，不提前排队。
- 原画卡按用户明确顺序作为该系列首卡；不顺带预分析或拆分其他 backlog。
- 保留 `A-CHAR-BATTLE-STATIC3D-PLAYER-ASSET-04` 编号：它是同一正式玩家资产在 01、02 失败和 03 Unity importer 失败之后的第四次独立资产卡；v2 写在 source 路径与合同里，不另起一个容易割裂失败链的编号序列。
- 将 `U-CHAR-BATTLE-STATIC3D-PROFILES-01` 的直接 `blockedBy` 更新为 `A-CHAR-BATTLE-STATIC3D-PLAYER-ASSET-04`，并把玩家事实源从旧合同/v1 source 改为本规格/v2 source。
- 同步从 Profile 卡的 `expectedPaths` 与写入职责中移除本卡已独占的正式玩家 FBX、BaseColor、对应 `.meta`、三个父目录 `.meta` 和 importer 测试路径；Profile 只读验证这些玩家输入，再负责玩家材质/Prefab、石甲兽 FBX/BaseColor/材质/Prefab、catalog 与 Profile 映射。两卡不得拥有重叠写路径。
- 石甲兽 source/FBX 已通过既有 Blender 与十视图 QA，但尚未在当前 Unity importer 中落地。它没有已知 importer 失败，因此不另建或预分析石甲兽任务；Profile 重新进入执行后仍须原样导入并验证石甲兽，若届时出现实际 importer 失败，再按该任务停止条件 blocked，不用推测性新卡提前掩盖风险。
- Profile 的任务 blocker 必须保留为管理事实，不得用自动化 runtime 的 `recoveryReason` 代替。
- 新卡 route/owner、字段语法、归档投影、队列排序和变更提交必须在真正写卡时重新依据届时的管理规则与 schema 5 Show 核验，不从本设计草案推断活动 runtime 状态。

## 7. 审批、失败与重试状态规则

- 原画和四视图各自遵守“一份已批准完整提示词对应一次生成调用”。
- 任何生成结果被拒绝后，该卡保持未完成并停止；必须先形成新完整提示词，再经用户明确批准后才能再次调用。
- AI 原始模型遵守“一次平台/credits 授权对应一个平台任务和一次生成”。失败后不得自动换平台、追加 credits 或重试。
- Blender/Unity 卡不拥有生成权；需要重生成、换原包或改设计时必须 blocked，并回到相应上游审批，而不是在下游叠加修补。
- 每个阶段只放行其唯一直接下游；不得跳过审批、哈希冻结或 Unity importer 硬门。

## 8. 完成定义

本系列只有在以下条件全部成立时完成：

1. 用户批准唯一透明全身对话立绘母版。
2. 用户批准与母版身份一致的严格四视图。
3. 用户批准平台、credits 和唯一 AI 原始 3D 包，且原包完整冻结。
4. v2 Blender、FBX、BaseColor、manifest 和「正/左/右/背 + 六个冻结 yaw」十视图齐全，哈希可追溯。
5. Blender 回读和 Unity importer 都证明几何非空、顶点位置非全零、三轴 bounds 非零，并满足 `Y=1.03m`、水平 `±0.30m`、`Y=0` 接地、`+Z` 正面与单位缩放合同。
6. `A-CHAR-BATTLE-STATIC3D-PLAYER-ASSET-04` 完成后，才解除 `U-CHAR-BATTLE-STATIC3D-PROFILES-01` 的当前资产 blocker；是否置为 ready 仍须按届时任务卡、队列、活动 run、集成锁与路径冲突重新核验。

## 9. 明确非目标

- 不修复、重导出、覆盖或删除 v1 FBX、blend、纹理、证据、branch 或 worktree。
- 不在本规格阶段生成图片、四视图或 3D 模型。
- 不继续实现 Profile 映射、材质、prefab 或 catalog。
- 不创建额外角色、女性主角、自定义系统、武器、法器、特效、骨骼或动画。
- 不预选 AI 3D 平台，不推定 credits 授权，不批量生成候选。
- 不创建新工作流机制，不绕过 schema 5、活动 run、集成锁、路径冲突检查或项目正式集成入口。
