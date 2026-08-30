# 透明对话立绘提示词

用途：生成无题字、无背景、周围为透明图层的纯角色立绘，用于游戏对话界面。当前 image2 流程先生成纯绿幕源图，再本地抠图转成 RGBA PNG。

## 通用生成约束

```text
Create the character on a perfectly flat solid #00ff00 chroma-key background for background removal.
The background must be one uniform color with no paper texture, no shadows, no gradients, no ink wash, no floor, no reflections, no contact shadow, and no lighting variation.
Keep all visible marks attached to the character silhouette, clothing, hair, weapon, fan, or carried object.
No oversized ghost sketch, no background brush arcs, no floor effects, no halo, no text, no seal, no title, no watermark, no logo.
Do not use #00ff00 anywhere in the subject.
```

## 本地透明处理

```powershell
python 'C:\Users\WINDOWS\.codex\skills\.system\imagegen\scripts\remove_chroma_key.py' --input <green-source.png> --out <final-transparent.png> --auto-key border --soft-matte --transparent-threshold 12 --opaque-threshold 220 --despill --force
```

验收：输出必须为 RGBA PNG，四角 alpha 为 0；人物完整；没有背景、地面、投影或可见绿幕边。

## 苻渊 / 含弘真君（黑白金站立对话版）

```text
Use case: background-extraction
Asset type: transparent-background, full-body standing dialogue character portrait for the 2D xianxia tactics RPG 《天章》
Input images: The approved 16:9 Fu Yuan profile sample is the decisive style, face, hands, standing pose, and identity reference. Preserve its refined 2D guofeng anime rendering, illustrated face and hands, black-white-antique-gold palette, premium cloth/metal/jade separation, and restrained Chinese ink treatment. The previous transparent Fu Yuan portrait is an outfit, silhouette, proportion, and cutout-engineering reference only; do not preserve its more realistic skin rendering. Do not copy any throne, text, seal, background, UI, or unrelated reference identity.
Primary request: Generate one original full-body standing portrait of 苻渊 / Fu Yuan, current title 含弘真君, specifically engineered as a clean dialogue cutout. He is the failed pioneer of a Deeds-and-Obligations Nascent Soul path, secluded for fifteen years after incompatible promises and public duties rebounded through him. Express burden, containment, unresolved responsibility, quiet authority, and the possibility of trying again—not imperial triumph, simple evil, or physical defeat.
Scene/backdrop: The character stands on a perfectly flat solid #00ff00 chroma-key background for removal. The entire background must be one uniform exact green with no paper texture, shadow, gradient, ink wash, floor, reflection, contact shadow, lighting variation, border, or vignette. Never use green anywhere in the subject.
Subject/pose: One elderly Fu Yuan only, full body standing in a calm three-quarter stance with face and eyes directed toward the viewer. Upright with broad heavy shoulders. Feet planted naturally at slightly different depths. One hand rests beside the robe, the other open near the waist as if weighing an invisible obligation. Both hands fully visible, anatomically correct, relaxed, and readable at dialogue scale. Moderately narrow silhouette; sleeves controlled close to the body.
Cutout-safe silhouette: Keep the white hair mostly gathered into a simple topknot and one cohesive mass falling close behind the shoulders and back. At most two broad loose locks; absolutely no hair-thin flyaways, looping strands, detached wisps, or transparent gaps between many strands. Give every robe layer a clean, intact, continuous hem. No torn cloth, ragged holes, lace-like erosion, watercolor gaps, detached tassel threads, filaments, smoke-like edges, or isolated marks. The entire visible silhouette must be opaque, connected, compact, and easy to chroma-key cleanly.
Clothing: Layered old black Daoist robes with broad but controlled sleeves, long grounded silhouette, pale gray-white inner collar, rough matte cloth, subtle folds, worn but physically intact, unornamented rather than imperial. Add only sparse hair-thin antique-gold broken covenant seams printed or embroidered inside the robe surface; incomplete linked arcs and small knot junctions terminate inside the cloth. They represent mutually pursuing promises and backlash, not cracked earth. No detached effects.
Style/medium: High-end Chinese xianxia game character art; refined 2D guofeng anime illustration with controlled ink-like linework, softly cel-painted planes, elegant simplified facial volumes, slightly idealized illustrated hands, and subtle ink-wash texture strictly inside the opaque figure. Preserve mature age and authority without photographic skin: no pores, no realistic skin texture, no photo lighting, no oily highlight, no plastic skin, and no uncanny 3D volume. Keep clear rough-cloth, silk lining, old bronze fastener, and tiny pale-jade bead material separation with a strong dialogue-readable silhouette.
Composition/framing: Tall vertical full-body portrait, centered, generous uniform green padding on all sides. Topknot, cohesive hair mass, both hands, sleeves, intact robe hem, and shoes fully inside frame. No chair, throne, seat, floor, pedestal, scenery, halo, circular frame, ghost portrait, brushstroke, smoke, aura, orbit line, floating node, sphere, particle, or cast shadow.
Lighting/mood: Soft frontal diffused light with faint edge light confined to the opaque character; solemn, quiet, formidable, exhausted but controlled. No hard shadow.
Color palette: Deep charcoal black, warm gray, off-white, restrained antique gold, tiny pale jade accent; no saturated colors and absolutely no green in the subject.
Constraints: Pure character cutout source only; one original older male; preserve Fu Yuan's age and identity. No weapon, staff, sword, bottle, crown, imperial regalia, ornate jewelry, armor, exposed skin, female traits, youthful face, royal pose, villain grin, readable text, calligraphy, talisman characters, letters, numbers, title, seal, UI, logo, or watermark. No photorealism, 3D render, or modern elements. Exactly two arms, two hands, and two feet; five fingers per hand where visible; coherent robe layers. Nothing cropped.
```

本版深灰黑衣在绿幕反光下使用 `opaque-threshold=220` 会误删衣袍，最终透明处理使用：

```powershell
python 'C:\Users\WINDOWS\.codex\skills\.system\imagegen\scripts\remove_chroma_key.py' --input <green-source.png> --out <final-transparent.png> --auto-key border --soft-matte --transparent-threshold 12 --opaque-threshold 128 --despill --force
```

当前苻淵母版为避免细小绿边，最终处理追加 `--edge-contract 1`。人物介绍、对话半身和头像裁切默认引用同一输出；不再为普通对话另生成同姿势副本。

## 无名默认男主（正式原画 v1）

状态：2026-08-29 已由负责人逐字批准。本卡不调用 ImageGen；下列完整提示词仅记录已批准的唯一绿幕源图的上游生成合同。唯一过程输入是 `C:\Users\WINDOWS\.codex\generated_images\01a04d10-bade-7221-8a04-dbcde0b2a02d\exec-1b0f38ad-b898-47fd-927c-c8ce0c2443df.png`，尺寸 `887×1774`、`1621601` 字节、SHA-256 `a1aae3b8a8e0f90c428b7e8818a045d428306725318a9d530f7ee02779722133`。它不进入项目正式路径。

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

本卡实际本地透明处理（只执行一次，不修改共享脚本）：对每个源 RGB 像素取 `anchor=max(R,B)` 与 `dominance=G-anchor`；当 `G>=200` 且 `dominance>=160` 时设 `alpha=0`，当 `dominance<=0` 时设 `alpha=255`，其余使用 `round(255*(1-min(1, dominance/max(1,249-anchor))))`。仅在 `0<alpha<255` 的边缘像素将 `G` 限制为不高于 `anchor` 以去绿；所有 `alpha=255` 像素 RGB 保持源图同坐标字节一致。最终 RGBA 母版为 `formal-player-default-male-v1.png`；`titled/formal-player-default-male-v1-approval-preview.png` 仅以 `#d7d0c5` 暖灰底本地合成。负责人已于 2026-08-30 通过决策 `DEC-20260830-CD8702E62EB4B6` 选择 A，明确批准该透明母版与暖灰预览。

## 谢观微 / 观澜真君

```text
Use case: background-extraction
Asset type: transparent-background full-body dialogue character portrait for a 2D xianxia tactics RPG
Primary request: Generate one full-body standing portrait of 谢观微 / Xie Guanwei / 观澜真君. Use the first no-title Xie Guanwei portrait as the identity and style reference, but make this version a pure character cutout for dialogue use.
Scene/backdrop: The source image must have a perfectly flat solid #00ff00 chroma-key background for background removal. The background must be one uniform color with no paper texture, no shadows, no gradients, no ink wash, no floor, no reflections, no contact shadow, and no lighting variation.
Subject: A slender, tall older male xianxia cultivator, about eighty, elegant and visibly weakened but mentally sharp. Neat silver hair tied behind his head, pale calm face, restrained expression, long narrow posture. He wears a plain white Daoist robe, clean and almost colorless, with very subtle water-blue inner lining. In one hand he holds a folding fan; the fan surface has an unfinished abstract water-ripple pattern, not text. His other sleeve falls softly.
Style/medium: Chinese xianxia ink-wash character art, expressive dry-brush black linework, hand-drawn sketch quality, semi-transparent watercolor washes inside the figure, low-saturation palette, elegant character-design sheet feeling, matching the first no-title portrait's watercolor and ink texture.
Composition/framing: Tall vertical full-body portrait, centered calm three-quarter standing pose, feet visible, generous padding around the figure. Keep all visible marks attached to the character silhouette, robe, hair, or fan. No oversized ghost sketch behind him, no broad water arcs, no ripples on the ground, no ink splash background, no halo.
Color palette: white robe, pale water blue, muted cyan, ink gray, very light silver, small dark ink accents. Do not use #00ff00 anywhere in the subject.
Constraints: Pure character only for later transparent PNG cutout. Crisp readable silhouette. No readable text, no Chinese calligraphy, no title, no seal, no UI, no watermark, no logo, no modern elements, no photorealism, no 3D render, no sword, no staff, no heavy armor, no background illustration.
```
