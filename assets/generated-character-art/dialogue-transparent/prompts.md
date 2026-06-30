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

## 苻渊 / 坤元真君

```text
Use case: background-extraction
Asset type: transparent-background full-body dialogue character portrait for a 2D xianxia tactics RPG
Primary request: Generate one full-body standing portrait of 苻渊 / Fu Yuan / 坤元真君. Use the first no-title Fu Yuan portrait as the identity and style reference, but make this version a pure character cutout for dialogue use.
Scene/backdrop: The source image must have a perfectly flat solid #00ff00 chroma-key background for background removal. The background must be one uniform color with no paper texture, no shadows, no gradients, no ink wash, no floor, no reflections, no contact shadow, and no lighting variation.
Subject: A powerful older male xianxia cultivator, late seventies but imposing; broad shoulders, sturdy body, weathered calm face, white hair and beard after spiritual collapse, hair tied in a simple topknot. He wears one old black Daoist robe, worn and unornamented, with layered loose sleeves and a heavy grounded silhouette. No weapon and no external artifact. His robe may contain restrained cracked earth-vein ochre/gold lines as part of the clothing surface, not as background effects.
Style/medium: Chinese xianxia ink-wash character art, expressive dry-brush black linework, hand-drawn sketch quality, semi-transparent watercolor washes inside the figure, low-saturation palette, elegant rough line economy, matching the first no-title portrait's watercolor and ink texture.
Composition/framing: Tall vertical full-body portrait, centered calm three-quarter standing pose, feet visible, generous padding around the figure. Keep all visible marks attached to the character silhouette or clothing. No oversized ghost sketch behind him, no sweeping background brush arcs, no cracked ground plane, no smoke cloud, no halo.
Color palette: black robe, charcoal ink, warm stone gray, muted ochre, dull antique gold, small restrained earth-yellow highlights. Do not use #00ff00 anywhere in the subject.
Constraints: Pure character only for later transparent PNG cutout. Crisp readable silhouette. No readable text, no Chinese calligraphy, no title, no seal, no UI, no watermark, no logo, no modern elements, no photorealism, no 3D render, no elaborate armor, no sword, no staff, no background illustration.
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
