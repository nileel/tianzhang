# 苻渊六向 2D 战术精灵样张

用途：`A-CHAR-2D-FUYUAN-SPRITE-01` 的唯一六向战术样张。生产边界、姿态、画布、方向、锚点、遮挡和有限帧规则以 `docs/superpowers/specs/2026-08-21-2d-tactical-character-production-contract.md` §2～§5 为准。该目录不是 UI 立绘或 Unity 正式资源。

## 批次与身份参考

- 生成方式：内置 ImageGen；未使用 CLI、API key、Tripo、Blender 或 Unity 资源路径。
- 决定性身份参考：`assets/generated-character-art/dialogue-transparent/fu-yuan-hanhong-zhenjun-standing-black-gold-v1.png`，SHA-256 `082FD5BCD152D008F9285383BF9B233D1D190DE9CCCAD9A793D4C1F64CA38A65`。只锁定成熟棱角脸、白灰顶髻与后发、短尖须／八字胡、炭黑／灰白／旧铜／淡玉服饰身份；不继承其站姿、背景或构图。
- 辅助身份与材质参考：`assets/generated-character-art/profile-wide/fu-yuan-hanhong-zhenjun/fu-yuan-profile-preview-16x9-v1.png`，SHA-256 `AFD4A4F22A83B569A22B75200F44BEC1D0327FEC90D67EFAAD3484FDD95B358C`。只补充脸型与粗糙哑光布料、旧铜和淡玉的材质层次；不继承题字、印章、背景、特效或姿势。
- 角色直接事实：`docs/剧情/重要NPC/苻渊.txt`。苻渊／含弘真君是承受未竟责任、沉稳疲惫却仍可行动的年长修士；不可画成帝王、反派、重伤垂败者或年轻主角。

## 实际完整提示词

以下为选定批次的实际完整提示词。输入图 1、2 为上列批准身份参考；输入图 3 只作为上一稿版式问题的纠正参照，不继承其尺寸或姿态。

```text
Use case: stylized-concept
Asset type: strict six-direction 2D tactical sprite SOURCE contact sheet. The first two input images are the approved Fu Yuan identity references. The third image is a draft layout only: do NOT preserve its size or pose; correct the scale and the anchor alignment described below.
Primary request: Generate a clean TEXTLESS 3 by 2 contact sheet of the SAME exact elderly Chinese male cultivator Fu Yuan, six equally sized cells with no borders, gaps, labels, letters, numbers, or UI. Landscape 3:2 canvas. Row-major cell order 0,1,2 / 3,4,5; across cells he is shown at six evenly spaced 60-degree rotations around the vertical axis, with consistent orthographic elevated tactical camera; cells differ ONLY by viewing angle.
Identity: mature angular face, stern but controlled expression, white-gray single topknot, one compact cohesive shoulder-length rear hair mass, short pointed white beard and moustache; broad shoulder compact chess-piece body. Thick matte charcoal-black layered Daoist robe, gray-white inner collar and sleeve lining, sparse subdued antique-gold broken seams printed in cloth, at most one old-bronze circular waist clasp and one tiny pale jade pendant when front naturally shows them. Not imperial, young, villainous, injured, or photorealistic.
LOCKED POSE: compact 4.5-to-5-heads tall tabletop figurine. Upright alert stance, left foot half step local-forward, right foot half step local-back, knees subtly bent. Torso and head straight local-forward, no twist. Left arm straight down close to left side and left hand visibly emerges before sleeve opening. Right forearm bends gently at waist and right hand visibly half-clenched. Hands empty. Wide sleeves opaque, connected, close to torso. Identical pose, silhouette, relative scale, hands, hair, sleeves, robe panels and ornament count in every cell.
STRICT CELL GEOMETRY: This image will be enlarged uniformly from 1536×1024 to a 2304×1536 sheet, making each cell exactly 768×768. Compose each figure SMALL and fully inside its own cell: after enlargement, the entire opaque character must be exactly approximately 528 pixels high, which means in this 1536×1024 draft image it must visibly be ONLY about 352 pixels high in each 512×512 cell. Do not make figures tall; each figure must occupy about 69 percent of its cell height. In each 512×512 cell, align the lowest visible opaque shoe pixels at exactly 64 pixels above the cell bottom (a shared horizontal line; it becomes y=96 in a 768×768 cell). Therefore top-row shoes sit at y=448 in the full image and bottom-row shoes at y=960. With 352-pixel figure height, each topknot reaches y=96 for top row and y=608 for bottom row. Give green padding around each figure. This anchor geometry matters more than dramatic framing.
Backdrop: perfectly flat solid #00ff00 green from edge to edge. No floor, no ground line, no cast/contact shadow, no gradient, texture, environment, halo, smoke, glow, particles, text, marks, or extra objects. Do not use green in figure.
Style: refined hand-painted Chinese guofeng 2D game sprite, restrained 2-3 cel shading, simple readable tactical-scale planes, crisp solid connected silhouette. Avoid photographic skin, 3D render, A/T pose, attack, cast, run, sit, victory stance, crop, weapon, staff, sword, fan, external artifact, detached hair, torn/transparent cloth, extra limbs/fingers, repeated ornaments.
```

## 固定来源、分槽与方向

- ImageGen 选定 1536×1024 无文字绿幕六格稿，经统一抠图、统一边缘去绿和单一固定网格编排为本目录唯一 `2304×1536` 来源图；没有逐方向重画、镜像、翻转、重排或结构修图。
- `fuyuan_tactical_six_direction_source.png` 为纯 `#00ff00` 绿幕 RGB 来源，按行优先无间隙排列。六张方向输出均从该来源的原像素 `768×768` 槽位转为 RGBA；`fuyuan_tactical_six_direction.png` 只按同顺序无缩放拼合这六张最终输出。

| 方向 | 来源槽坐标 `(x,y,w,h)` | 规则邻格 `(q,r)` | 根节点 Y | 输出 |
|------|-----------------------|------------------|-----------|------|
| `0` | `(0,0,768,768)` | `(1,0)` | `90°` | `fuyuan_tactical_direction_0.png` |
| `1` | `(768,0,768,768)` | `(1,-1)` | `150°` | `fuyuan_tactical_direction_1.png` |
| `2` | `(1536,0,768,768)` | `(0,-1)` | `210°` | `fuyuan_tactical_direction_2.png` |
| `3` | `(0,768,768,768)` | `(-1,0)` | `270°` | `fuyuan_tactical_direction_3.png` |
| `4` | `(768,768,768,768)` | `(-1,1)` | `330°` | `fuyuan_tactical_direction_4.png` |
| `5` | `(1536,768,768,768)` | `(0,1)` | `30°` | `fuyuan_tactical_direction_5.png` |

- 每张最终图严格为 `768×768` RGBA；Unity 导入合同为 `512 PPU`，pivot 为 `(0.5, 0.125)`，即左下原点 `(384,96)` px。
- 六张图的非透明包围盒均为 `y=96..623`（含端点），可见高度 `528 px`；四角 alpha 都是 `0`。根锚点位于两足之间的画布中线，未以 Unity 私有缩放或偏移补偿。

## SHA-256

| 文件 | 尺寸／格式 | SHA-256 |
|------|-----------|---------|
| `fuyuan_tactical_six_direction_source.png` | 2304×1536 RGB | `8A413E1374CC4889DFDE89D35932D496274074AA27F28BEC755BC35F68BE5407` |
| `fuyuan_tactical_direction_0.png` | 768×768 RGBA | `66B3734DFB1DB56B78920FD37FDA5F072FBFE4BF470A4A1FD4E61F9A9BDA526A` |
| `fuyuan_tactical_direction_1.png` | 768×768 RGBA | `C586821EE4D8A1D1E7924EF7787EBE1D208EE149AF5A78740647A4C9DC7B790B` |
| `fuyuan_tactical_direction_2.png` | 768×768 RGBA | `F690FC98710620E8E240CEFAABF430AF8EAFD6308225E89E68F30F8094E7C6F8` |
| `fuyuan_tactical_direction_3.png` | 768×768 RGBA | `F8AB6F4B53EE2DFA913105E8D75C8CF125751674AEE427A0F17610CAA412EBDD` |
| `fuyuan_tactical_direction_4.png` | 768×768 RGBA | `E12DC8F10B16FEB2A5E97FA908A32759EFFA3EC15198BC263B582CAF10366597` |
| `fuyuan_tactical_direction_5.png` | 768×768 RGBA | `5A8E97D92FCD9FC87E0FB11BB6D42111A3624B80E344BBB484E5D5102066F4D8` |
| `fuyuan_tactical_six_direction.png` | 2304×1536 RGBA | `681E40EF0C3030A2A5136A1D72447CC7A6946B94811A2270801AE4DECE275594` |

## 人工 QA

- 六个槽位都是同一位年长苻渊：成熟棱角脸、白灰单顶髻与贴肩后发、短尖白须／八字胡、炭黑袍、灰白内层、旧铜圆腰扣、淡玉坠和克制断契金线在正面与自然可见的侧面保持连贯；背向只见连续后发、袍背和后袖，不复制前脸、前襟或腰饰。
- 姿态统一为直立警觉的空手轮廓：左脚前、右脚后、双膝轻屈，左手在左袖前可见，右前臂腰前半握；宽袖不透明、连贯且贴近躯干。没有武器、法器、额外肢体、破洞、透明衣片、独立飘丝、地面、投影、环境、文字、UI、水印或 logo。
- 六向只由观测方向变化；固定联系表、透明边缘、脚底锚点和来源追溯均已在本卡范围复核。下游 Unity 的六向选择、遮挡与事件时点证据仍属于 `U-CHAR-2D-TACTICAL-PROTO-01`，本样张不伪报。
