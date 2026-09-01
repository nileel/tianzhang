# 默认男主正式静态 3D v2 严格四视图新提示词

任务：`A-CHAR-BATTLE-STATIC3D-PLAYER-FOURVIEW-01`

状态：负责人已于 2026-08-31 在当时对话批准下方唯一完整提示词代码块；批准来源文件 SHA-256 为 `ab26b47b0576aa7adbb34034f1b0acb6cb57991b44da6ee47d889a185d29fbe0`，代码块规范化 UTF-8 为 `8174` 字节、SHA-256 为 `fbc9b5e0f99e6e2c13d3d3835d9b6ebf264f58f1393c19a364e2e5a6f3946ee0`。该批准和对应调用均已消费；后续背视修复严格受两份 2026-08-31 修复设计约束。负责人于 2026-09-01 对固定候选 SHA-256 `2cc813319737a2e56d54a7936b24228e860ea50d5f960f9fb143695aeea477c1` 明确回复“这张是对的”，候选已晋升为规范母图并完成确定性四象限裁切；不得再复用本提示词、旧批准或任何已消费调用。

## 冻结参考

- `assets/generated-character-art/dialogue-transparent/formal-player-default-male-v1.png`：唯一决定性身份与姿势参考；RGBA `887×1774`、`1806843` 字节、SHA-256 `399175e1d5f2ca81fbd246c43a4cc02b2867721ad13df429340d9950698f4948`。它决定无名 16 岁默认男主的脸、半束长发、衣装、配色、七头身比例、右手剑指和自然不对称重心；四视图不继承其三分之四相机角度。
- `assets/generated-character-art/dialogue-transparent/fu-yuan-hanhong-zhenjun-standing-black-gold-v1.png`：只作项目级插画完成度参照；不得借用苻渊的身份、年龄、脸、白发、服装、姿势或饰物。

## 已批准且已消费的唯一完整提示词（历史）

```text
Use case: identity-and-pose-preserve
Asset type: one project-bound, no-text four-panel turnaround mother image for static 3D geometry input for the unnamed default male player character in the 2D xianxia game 《天章》.

Input image:
Image 1 is the sole decisive approved identity and pose reference: the canonical full-body dialogue portrait of the unnamed default male player character. Preserve his exact individual identity, narrow oval youthful face, natural warm-fair skin, pure black eyes, straight black eyebrows, modest straight nose, restrained thin lips, black half-tied long hair, practical indigo travel Daoist robe, collar, cuffs, robe panels, dark brown belt, sparse aged-silver fasteners, nearly black cloth boots, restrained colors, right-hand sword-finger gesture, raised right forearm, lowered left arm, and mild natural asymmetric weight. Do not inherit Image 1's three-quarter camera angle; translate the same character and pose into the strict orthographic views specified below.

Primary request:
Create exactly one square 2×2 four-panel character-turnaround mother image of this same unnamed sixteen-year-old Chinese male cultivator. The four equally sized panels have no labels, text, borders, decorative dividers, or icons. Their acceptance-critical fixed positions are: upper-left strict FRONT; upper-right strict CHARACTER-LEFT 90-degree profile with the nose pointing image-right; lower-left strict CHARACTER-RIGHT 90-degree profile with the nose pointing image-left; lower-right strict BACK. Do not swap or reorder the two physical side-view slots. Every panel contains the same single complete full-body character in one identical frozen pose, at the same scale, camera height, framing, and foot baseline.

Identity and proportion invariants:
Keep one unnamed sixteen-year-old Chinese male cultivator from a minor Guanzhong clan, calm, self-contained, earnest, and slightly alert. He is slim and upright with moderate shoulders and naturally long limbs, visibly youthful but neither childlike, frail, feminine, muscular, bulky, chibi, aged, gloomy, arrogant, nor cold. Preserve about seven heads from the top of the simple crown tie to the soles. Preserve the narrow oval face with a clear but not pointed jaw. His long black hair is half tied simply at the crown; the remaining cohesive locks fall just below the shoulder blades, never to the waist; retain one natural face-framing lock on each side. No crown, jewelry, loose flyaway hair, tangled hair, thin detached strands, facial hair, makeup, scars, tattoo, forehead mark, glowing eye, or colored pupil.

Clothing invariants:
Preserve the same practical travel Daoist robe: deep indigo outer robe, gray-white crossed-collar inner garment, dark brown leather waist belt, only a very small amount of aged-silver fastening hardware, fitted wrist cuffs, clearly layered and split lower robe panels, and dark brown nearly black lightweight cloth boots with narrow openings and soft soles. Preserve clear collar construction, edging, seams, and only subtle low-contrast woven texture. Do not add or remove any clothing structure between panels. No clan emblem, sect symbol, large embroidery, jade pendant, identity tablet, jewelry, weapon, sword, staff, talisman, armor, cape, shoulder plate, crown, magical accessory, effect, or new clasp, cord, hanging ornament, pattern, opening, or object.

Frozen pose and modeling clearance:
Preserve the approved reference's recognizable asymmetric daily-cultivation pose in every panel. Keep the right forearm moderately raised to the lower chest. The right hand forms one clear single-handed sword-finger gesture: index and middle fingers joined and extended, the other three fingers naturally folded. Keep the right elbow close to the torso but not touching it. Maintain clear readable air gaps between the right elbow, forearm, hand, fingers, sleeve, chest, and robe so the anatomy and clothing do not merge. The sword-finger hand must not cover the face, crossed collar, belt, or main robe construction. Keep the left arm naturally lowered and the entire left hand visible. Preserve a mild natural asymmetric weight distribution while keeping both feet fully grounded and the shoulders and pelvis untwisted. This is a calm habitual cultivation gesture with no spell being cast.

The pose is one frozen three-dimensional pose shared by all four panels. Only the orthographic camera direction changes. Do not lower or neutralize the right arm in either side profile or the back view. Do not turn the character into a symmetric mannequin with both arms hanging down. Do not use a combat, casting, victory, walking, stepping, twisting, A-pose, or T-pose. Do not add light, aura, particles, smoke, runes, energy, wind, or other effects.

Camera and background:
Use the same eye-level near-orthographic camera in all four panels: no perspective drama, foreshortening, zoom change, tilt, crop, or body-length distortion. Use one flat uniform warm light-gray studio background and even neutral diffuse lighting, with no floor line, cast shadow, contact shadow, gradient, texture, scenery, reflection, halo, smoke, particle, or lighting variation.

Exact panel directions:
- Upper-left: true strict FRONT. He faces the camera squarely while keeping the frozen right-hand sword-finger pose and natural asymmetric weight. Do not use the reference's three-quarter camera angle.
- Upper-right: true strict CHARACTER-LEFT 90-degree profile. The camera sees the character's own left ear, left shoulder, left hip, left hand, and left boot nearest. His nose points horizontally toward image-right. This is the character's left side, not a mirrored substitute and not a three-quarter view. The same raised right forearm and sword-finger remain physically present and consistent from this view.
- Lower-left: true strict CHARACTER-RIGHT 90-degree profile. The camera sees the character's own right ear, right shoulder, right hip, right hand, and right boot nearest. His nose points horizontally toward image-left. This is the character's right side, not a mirrored substitute and not a three-quarter view. The same raised right forearm and sword-finger remain clearly readable from this view.
- Lower-right: true strict BACK. The camera is directly behind him; show no face and no three-quarter turn. Preserve the same raised-right-arm pose as seen from behind. Infer only the minimum rear structure supported by Image 1: the cohesive rear hair mass, continuous rear robe panels, a restrained centered rear seam, the back of the same belt, cuffs, hems, and boots. Do not invent any rear emblem, ornament, dangling object, decorative opening, or front-facing detail.

Continuity, anatomy, and layout hard constraints:
The same face, head size, seven-head proportion, hair length and lock structure, collar, robe layers, belt, cuffs, boot shape, colors, frozen right-arm pose, left-arm pose, weight distribution, camera height, light, background, foot baseline, and panel scale must remain continuous in all four panels. Left and right profiles must retain authentic one-sided spatial structure rather than mirroring each other. Exactly one complete person per panel: one head, two arms, two anatomically correct hands with five fingers each, two complete feet, intact hair, opaque connected clothing, and no cropped head, hands, hem, or boots. The right hand must have exactly two joined extended fingers and three naturally folded fingers; no fused, missing, duplicated, or extra fingers. Keep generous but equal empty padding around every full figure. Do not make a paper-doll body, a photograph, a 3D render, western comic art, a different named character, a real-person likeness, text, calligraphy, seal, UI, logo, watermark, background object, or extra limb.

Reject the result if both arms hang down, the pose becomes symmetric or mannequin-like, the right-hand sword-finger is missing or at the wrong height, the raised right arm changes between panels, the gesture hides key costume structure, the hand or sleeve merges into the torso, the side-view slots are swapped, or any identity, clothing, direction, anatomy, framing, or continuity constraint fails.
```

## 批准后的唯一执行边界

1. 只有负责人逐字批准上方完整代码块后，才允许用已核验的 `Image 1` 调用一次内置 ImageGen；对设计方向或本文件存在的普通认可不等于逐字批准。
2. 唯一无文字四面板母图只按其实际像素面板边界本地确定性裁切；不缩放、翻转、镜像、重绘、补画或逐槽 AI 修补。
3. 生成后才允许写入四张 PNG、无文字联系表和 `manifest.json`，并记录实际母图／输出 SHA-256、字节数、尺寸、裁切矩形、姿势与身份连续性以及负责人视觉批准结论。
4. 若唯一结果出现身份、比例、发型、服装、姿势、左右语义、背面、解剖或裁切失败，立即停止；不得自动改词、重试、调用第二次 ImageGen 或以 AI 补画修复。

## 最终批准结果

- 最终背视遮挡修复合同：`docs/superpowers/specs/2026-08-31-formal-player-default-male-fourview-back-occlusion-repair-design.md`；它只允许修正右下背视格的右臂投影，并冻结前三格及其他身份、服装和布局事实。
- 获批候选：`assets/source/characters/combat-pieces/formal-player-static-3d-v2/evidence/candidates/formal_player_default_male_fourview-back-occlusion-candidate-20260831.png`，RGB `1254×1254`，`1598278` 字节，SHA-256 `2cc813319737a2e56d54a7936b24228e860ea50d5f960f9fb143695aeea477c1`。
- 负责人于 2026-09-01 在当前 Codex 对话明确回复“这张是对的”；该 UTF-8 文本的 SHA-256 为 `7a204e834c29f397f970607e606083c66596586066e74456666b362ad376ffe6`。
- 规范母图与获批候选逐字节相同。四张正式输入仅按 `(0,0,627,627)`、`(627,0,627,627)`、`(0,627,627,627)`、`(627,627,627,627)` 确定性裁切；未缩放、旋转、镜像、翻转、重绘或 AI 修补。精确字节数、SHA-256、槽位方向和像素回读结论见同目录 `manifest.json`。
