# 苻渊静态 3D 棋子自然战斗定姿四视图

用途：`A-CHAR-STATIC-CHESS-FUYUAN-FOURVIEW-01` 的唯一静态 3D 路线输入。四张 PNG 是同一次内置 ImageGen 调用产生的无文字 2×2 母图的无损分槽；只可在负责人另行明确授权唯一一次 Tripo credits 消费后，供 `A-CHAR-STATIC-CHESS-FUYUAN-TRIPO-01` 使用。

## 事实与参考边界

- 生产合同：`docs/superpowers/specs/2026-08-21-static-3d-chess-character-production-contract.md` §2、§3.1、§3.2、§7。
- 角色直接事实：`docs/剧情/重要NPC/苻渊.txt`。苻渊／含弘真君是沉稳、疲惫而自持的年长修士；不得写成帝王、反派或衰败伤者。
- 决定性身份参考：`assets/generated-character-art/dialogue-transparent/fu-yuan-hanhong-zhenjun-standing-black-gold-v1.png`，SHA-256 `082fd5bcd152d008f9285383bf9b233d1d190de9cccad9a793d4c1f64ca38a65`。只锁定成熟棱角脸、白灰发髻与后发、短尖须／八字胡、黑白金层叠道袍、宽袖、旧铜腰扣、淡玉坠和配色；不继承其站姿、背景或构图。
- 辅助脸型与材质参考：`assets/generated-character-art/profile-wide/fu-yuan-hanhong-zhenjun/fu-yuan-profile-preview-16x9-v1.png`，SHA-256 `afd4a4f22a83b569a22b75200f44bec1d0327fec90d67efaad3484fdd95b358c`。只补充脸型和布料／旧铜／淡玉材质，不继承题字、印章、背景、特效或姿势。
- 历史失败边界：`开发管理/苻渊Tripo严格轮四视图生成记录.txt` 记录首次四图约 6.2 头身而失败，也记录后续旧四图与唯一 Tripo 试验；本批仅吸收身份事实与“紧凑比例、同一结构”教训，不复用任何旧姿势、旧四图或 `strict-round-02` 模型输入。

## 内置 ImageGen 提示词

调用方式：内置 ImageGen；以以上两张已批准立绘为身份参考；未使用 CLI、API key、Tripo、Blender 或 Unity 资源路径。以下是本批四槽共用的完整提示词；槽位位置是该提示词中冻结的唯一顺序。

```text
Use case: stylized-concept
Asset type: project-bound four-view production turnaround source for one static 3D tactical chess-piece character in the 2D xianxia game 《天章》.

Input images: Image 1 and Image 2 are approved identity references for Fu Yuan only. Preserve their same elderly angular Chinese male face, white-gray single topknot, compact shoulder-length cohesive rear hair mass, short pointed white beard and moustache, charcoal-black layered Daoist robes with pale gray-white inner collar and sleeve lining, restrained old-bronze circular waist fastener, one tiny pale-jade pendant, and sparse antique-gold broken-covenant seams. Do not preserve either reference pose, background, text, seal, ornaments, composition, or effects.

Primary request: Create ONE clean, textless, perfectly aligned 2×2 character turnaround contact sheet for the SAME exact Fu Yuan model in a single locked natural combat pose. The four equally sized cells must be arranged in row-major order: FRONT (top left), FACE-LEFT-SIDE (top right, his nose visibly points toward the image's left edge), FACE-RIGHT-SIDE (bottom left, his nose visibly points toward the image's right edge), BACK (bottom right). Do not print any labels, letters, numbers, UI, borders, or dividers. Every cell shows the same person, same pose, same scale, same orthographic eye-level camera, same neutral soft light, same plain uniform warm-gray background, differing only in viewing direction.

Locked pose and proportions: a compact 4.5–5 heads-tall static chess-piece proportion, broad sturdy shoulders; upright and alert but restrained; feet fully visible and planted on the same invisible flat level, left foot half a step forward pointing forward, right foot half a step back, knees only subtly bent; torso and head face forward in the character's local frame without twist. His left arm hangs naturally with left hand visible in front of the left sleeve opening; his right forearm bends naturally before the waist, with the right hand half-closed and visible. Both hands are empty, attached correctly, readable, and must remain identical across every cell. Do not use an A-pose, T-pose, attack, spellcasting, running, throne, victory pose, or three-quarter portrait stance.

Back construction: in the BACK cell show no face at all; show complete cohesive shoulder-length rear hair, continuous back robe panels and rear sleeves. Do not repeat the front waist fastener or jade pendant on the rear. In the FRONT cell show exactly one circular old-bronze waist fastener and exactly one small pale-jade pendant. Side cells show only what their strict profile naturally reveals. The broad paired sleeves remain coherent, close to the torso, opaque and cloth-like: never wing-like, rigid plates, spikes, transparent panels, or a cross-back frame.

Style/medium: premium hand-painted stylized xianxia game 3D character turnaround concept for image-to-3D input, with simple readable chess-figurine large planes, rough matte cloth, low-saturation deep charcoal, warm gray, gray-white, muted old bronze and a tiny pale jade accent. Use 2–3 restrained toon-like shading levels, crisp production-friendly silhouette, no realistic skin pores, no glossy plastic or photoreal render. Full body in every cell with generous equal padding: topknot, rear hair, both hands, sleeve ends, robe hem and both shoes fully visible.

Constraints: exactly one identical elderly male character per cell and exactly two arms, two hands, two feet; one consistent model viewed from four directions. No floor line, cast shadow, environment, chair, pedestal, hex tile, weapon, staff, sword, armor, crown, imperial insignia, magic, aura, smoke, floating parts, detached strands, torn cloth, holes, transparent cloth, extra fingers, extra limbs, duplicated waist ornaments, text, calligraphy, title, seal, UI, logo, watermark, crop, or perspective distortion.
```

## 固定槽位与无损分槽

- `front`：母图左上；鼻尖正对镜头。
- `face-left-side`：母图右上；鼻尖朝图片左侧。
- `face-right-side`：母图左下；鼻尖朝图片右侧。
- `back`：母图右下；只见后发、袍背与袖背，不见脸。
- 分槽只移除了母图中心 4 px 的网格留白，并将四个原像素槽位按同一顺序拼为联系表；没有重画、翻转、镜像、修饰或改变任一人物结构。

## 人工视觉 QA

- 四槽均为完整单人全身：顶髻、后发、双手、双足、袖口与袍摆都未裁切；没有文字、印章、UI、水印、武器、地块、座椅、环境、烟雾或粒子。
- 正面与两侧保持同一紧凑宽肩比例、左脚前半步／右脚后撤的站立重心、左手下垂可见／右前臂腰前半握可见的空手轮廓；不是 A/T Pose、攻击、施法或奔跑姿势。
- 脸、顶髻、贴近肩背的一体后发、短尖须、炭黑袍、灰白内层、收束宽袖、鞋和断契金线在四槽中连贯；没有额外肢体、错误手指、破洞、透明布、独立飘丝或衣内烘焙手发。
- 正面仅见一套旧铜圆腰扣与淡玉坠；两张侧面按自然可见性显示；背面没有脸、正面腰饰或前襟复制，且显示连续后发、袍背与后袖。
- 四图使用同尺度、平视无透视人物构图、柔和中性光和低饱和暖灰背景。六向固定镜头表现属于下游 Blender／Unity 静态候选验证，四图阶段不以二维图像伪报该结果。

## SHA-256

| 槽位 | 文件 | 尺寸 | SHA-256 |
|------|------|------|---------|
| `front` | `fuyuan_static_chess_front.png` | 625×625 | `ABC4E9B9295C64CC1857E0A33A4E4E351FD9DCD95AC0CCB3785DBE37A5A93371` |
| `face-left-side` | `fuyuan_static_chess_face-left-side.png` | 625×625 | `638F5E8F67673C8BB11D44074A97A4D91F1F8B7AD841C5406D50EED4A99DB0F3` |
| `face-right-side` | `fuyuan_static_chess_face-right-side.png` | 625×625 | `690EB5F0BED8527AEC25EDBD64CBD87419A005520B90EA0C133F031336B96C11` |
| `back` | `fuyuan_static_chess_back.png` | 625×625 | `5655A745F7EC1118A5FED1D87BBDF514109E02BD30F3A39B07061AC27D7F24A7` |
| 联系表 | `fuyuan_static_chess_fourview.png` | 1250×1250 | `CE6B875C0110BE3945FEC3DF4674E7797E9701904EEDE58CCD4E3E988B858B1F` |
