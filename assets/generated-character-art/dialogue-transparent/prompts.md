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
Input images: The user-provided character-card image is a style and material reference only: use its refined semi-realistic painterly character rendering, crisp face and hands, black-white-antique-gold palette, premium cloth, metal, and jade separation, and restrained Chinese ink treatment. Do not copy its woman, costume, throne, bottle, text, panels, pose, or identity. The first transparent Fu Yuan portrait is a standing identity reference: preserve the imposing older Chinese male, broad sturdy build, weathered angular face, white topknot, short white beard and moustache, stern restrained gaze, and old black Daoist robes. Do not preserve its old earth-vein meaning or ragged watercolor silhouette. The seated black-gold Fu Yuan portrait is a rendering reference only: preserve the polished face, charcoal and antique-gold robe treatment, rough matte cloth, and solemn burdened mood, but remove the seat, halo, scenery, nodes, and every background element.
Primary request: Generate one original full-body standing portrait of 苻渊 / Fu Yuan, current title 含弘真君, specifically engineered as a clean dialogue cutout. He is the failed pioneer of a Deeds-and-Obligations Nascent Soul path, secluded for fifteen years after incompatible promises and public duties rebounded through him. Express burden, containment, unresolved responsibility, quiet authority, and the possibility of trying again—not imperial triumph, simple evil, or physical defeat.
Scene/backdrop: The character stands on a perfectly flat solid #00ff00 chroma-key background for removal. The entire background must be one uniform exact green with no paper texture, shadow, gradient, ink wash, floor, reflection, contact shadow, lighting variation, border, or vignette. Never use green anywhere in the subject.
Subject/pose: One elderly Fu Yuan only, full body standing in a calm three-quarter stance with face and eyes directed toward the viewer. Upright with broad heavy shoulders. Feet planted naturally at slightly different depths. One hand rests beside the robe, the other open near the waist as if weighing an invisible obligation. Both hands fully visible, anatomically correct, relaxed, and readable at dialogue scale. Moderately narrow silhouette; sleeves controlled close to the body.
Cutout-safe silhouette: Keep the white hair mostly gathered into a simple topknot and one cohesive mass falling close behind the shoulders and back. At most two broad loose locks; absolutely no hair-thin flyaways, looping strands, detached wisps, or transparent gaps between many strands. Give every robe layer a clean, intact, continuous hem. No torn cloth, ragged holes, lace-like erosion, watercolor gaps, detached tassel threads, filaments, smoke-like edges, or isolated marks. The entire visible silhouette must be opaque, connected, compact, and easy to chroma-key cleanly.
Clothing: Layered old black Daoist robes with broad but controlled sleeves, long grounded silhouette, pale gray-white inner collar, rough matte cloth, subtle folds, worn but physically intact, unornamented rather than imperial. Add only sparse hair-thin antique-gold broken covenant seams printed or embroidered inside the robe surface; incomplete linked arcs and small knot junctions terminate inside the cloth. They represent mutually pursuing promises and backlash, not cracked earth. No detached effects.
Style/medium: High-end Chinese xianxia game character art; refined semi-realistic painterly anime rendering with subtle controlled ink-wash texture strictly inside the opaque figure; crisp facial anatomy and hands; nuanced aging; clear rough-cloth, silk lining, old bronze fastener, and tiny pale-jade bead material separation; smooth tonal transitions; premium collectible-character finish; strong clean dialogue-readable silhouette.
Composition/framing: Tall vertical full-body portrait, centered, generous uniform green padding on all sides. Topknot, cohesive hair mass, both hands, sleeves, intact robe hem, and shoes fully inside frame. No chair, throne, seat, floor, pedestal, scenery, halo, circular frame, ghost portrait, brushstroke, smoke, aura, orbit line, floating node, sphere, particle, or cast shadow.
Lighting/mood: Soft frontal diffused light with faint edge light confined to the opaque character; solemn, quiet, formidable, exhausted but controlled. No hard shadow.
Color palette: Deep charcoal black, warm gray, off-white, restrained antique gold, tiny pale jade accent; no saturated colors and absolutely no green in the subject.
Constraints: Pure character cutout source only; one original older male; preserve Fu Yuan's age and identity. No weapon, staff, sword, bottle, crown, imperial regalia, ornate jewelry, armor, exposed skin, female traits, youthful face, royal pose, villain grin, readable text, calligraphy, talisman characters, letters, numbers, title, seal, UI, logo, or watermark. No photorealism, 3D render, or modern elements. Exactly two arms, two hands, and two feet; five fingers per hand where visible; coherent robe layers. Nothing cropped.
```

本版深灰黑衣在绿幕反光下使用 `opaque-threshold=220` 会误删衣袍，最终透明处理使用：

```powershell
python 'C:\Users\WINDOWS\.codex\skills\.system\imagegen\scripts\remove_chroma_key.py' --input <green-source.png> --out <final-transparent.png> --auto-key border --soft-matte --transparent-threshold 12 --opaque-threshold 128 --despill --force
```

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
