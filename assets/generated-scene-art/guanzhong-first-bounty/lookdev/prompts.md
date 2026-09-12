# 关中首个悬赏流程整屏样板提示词

状态（2026-09-12）：负责人已对 DEC-20260912-CD1A9E90A58405 选择 C，拒绝本轮整屏方向。以下提示词和两张 PNG 仅保留为已查看的拒绝记录，不可作为生产输入，也不得据此继续生成、修订、解锁下游或发起外部交易。它们不是 Unity 截图或正式 source 资产。

## 共同输入与处理

| 输入 | 角色 | SHA-256 |
|---|---|---|
| `assets/generated-character-art/dialogue-transparent/formal-player-default-male-v1.png` | 已批准默认男主的身份、深靛服色、半束黑发与精致国风立绘完成度；不复制绿幕背景。 | `399175e1d5f2ca81fbd246c43a4cc02b2867721ad13df429340d9950698f4948` |
| `assets/source/characters/combat-pieces/shijiahou-static-3d-v1/shijiahou_static3d_v1_contact-sheet.png` | 已批准石甲兽的低伏重型四足、层叠石甲与哑光灰褐材质；不复制联系表文字。 | `39ac621d0d2229ee775fb1c70ce7db605e31353582a1207276ab31a57be2a067` |

工具：内置 ImageGen（默认路径）。调用数：`2`；定点修订数：`0`；本卡上限：`4`。每张原始输出为 RGB `1672×941`，用 Pillow LANCZOS 先缩放至 `1920×1081`，再居中裁去 1 行，得到无拉伸的 RGB `1920×1080` 交付图。没有透明抠图、外部平台、付费 credits、上传、下载或第三角色生成。

## 城镇与悬赏：`city-and-bounty-v1.png`

```text
Use case: stylized-concept
Asset type: a single 16:9, 1920×1080 art-direction look-development board for the first playable Guanzhong bounty screen of the 2D xianxia game Tian Zhang. It is a design illustration, not a Unity runtime screenshot.
Input images: Image 1 is the approved default male player portrait; use it only as the identity, deep-indigo robe palette, long black half-tied hair, youthful calm silhouette, and refined Chinese xianxia illustration finish. Do not reproduce its green background. Image 2 is the approved stone-armored beast contact sheet; use it only as the heavy, low four-legged layered weathered-stone silhouette reference, not as a contact sheet.
Primary request: show the Guanzhong City bounty-board direction with a clear wide city screen and restrained, readable UI framing.
Scene/backdrop: a quiet low-tier Guanzhong loess trading city: thick rammed-earth walls, gray tiled roofs, half-timbered market facades, a weathered wooden-and-iron bounty board on the east wall of the Changning Hall market, faint mineral trade cues, no story NPCs.
Subject: exactly one default young male cultivator, shown as a calm deep-indigo full-body portrait inside a left UI portrait bay; the central world space remains a city view. A small, non-living four-legged stone-beast silhouette appears only as an emblem on one blank bounty card.
Style/medium: polished refined Chinese xianxia anime game key art mixed with a practical desktop game UI mockup; controlled hand-painted shading, low-saturation materials, no photorealism, no 3D render.
Composition/framing: full 16:9 desktop screen, fixed orthographic design reference with 6.2-scale intent, pitch 38 degrees, yaw 0 degrees; city environment remains visible in the central field, portrait bay occupies the left edge, bounty board and its blank-card UI occupy the right third, clear safe central space. UI panels are deep ink-blue with subdued brass and gray-white edges, quiet center panels suited to editable text overlays later.
Lighting/mood: warm late-afternoon loess light, soft hard-edged shadows, restrained and low contrast.
Color palette: muted ochre loess, dry gray-green accents, slate-gray tile, deep indigo player robe, deep ink UI, weathered gray-brown stone.
Materials/textures: rammed earth, aged timber, ironwood board, gray tile, rough unpolished stone, woven robe fabric.
Constraints: exactly one human and no other people, no guards, merchants, crowds, NPCs, animals, monsters, or extra characters. No combat, weapon, magic glow, aura, particles, clan emblem, modern object, calligraphy, legible Chinese or English text, UI glyphs, watermark, logo, title, or mock device frame. Make all cards and panels blank/abstract so text stays editable outside the image. Preserve a restrained, coherent look; do not claim runtime measurement or create a screenshot.
```

## 野外战斗与 HUD：`battle-and-hud-v1.png`

```text
Use case: stylized-concept
Asset type: a single 16:9, 1920×1080 art-direction look-development board for the first Guanzhong wilderness tactical battle and HUD in the 2D xianxia game Tian Zhang. It is a design illustration, not a Unity runtime screenshot.
Input images: Image 1 is the approved default male player portrait; use it only as the identity and palette reference: youthful calm Chinese male, long black half-tied hair, deep-indigo outer robe, gray-white crossed collar, dark belt and boots. Do not include its green background. Image 2 is the approved stone-armored beast contact sheet; use it only as the silhouette and material reference: exactly one low heavy four-legged creature, layered weathered dark-gray and loess-brown stone armor, no bipedal body, no wings, no glow, no contact-sheet labels.
Primary request: show a complete fixed-camera Guanzhong wilderness encounter screen where the default player and the stone beast are readable at tactical pawn scale above grass-and-loess hex ground, with quiet HUD safe zones.
Scene/backdrop: dry Guanzhong outskirts, grassland and yellow loess soil, sparse low rocks and one or two small weathered wood props only; no city, no crowd, no additional creatures.
Subject: exactly two combatants: one deep-indigo male cultivator tactical pawn and one low four-legged stone-armored beast tactical pawn. Both stand grounded on separate hexes and face each other across several usable hex cells.
Style/medium: polished Chinese xianxia anime-game tactical key art, controlled hand-painted material treatment, readable 3D-pawn-like silhouettes, restrained low saturation; not photorealistic, not a real-time screenshot.
Composition/framing: 16:9 desktop screen design reference, fixed orthographic-camera intent with 6.2 scale, pitch 38 degrees and yaw 0 degrees. Terrain and hex grid occupy the clear center. Reserve a dark ink-blue adventure panel safe zone from roughly 2% to 28% width on the left; reserve a deep ink-blue combat HUD panel safe zone from roughly 62% to 98% width on the right, with status strips and action slots but no actual text. Keep the player and beast inside the unoccluded central field; each pawn should read at approximately one tenth of the screen height as a design target, not a measured runtime size.
Lighting/mood: dry afternoon light, clear ground contact, soft hard-edged shadows, no fog that obscures pawns.
Color palette: muted ochre loess, gray-green grass, charcoal and brown stone, deep indigo player, deep ink UI with restrained brass/gray-white dividers.
Materials/textures: matte earth, clipped grass, weathered stone plates, quiet woven fabric, unobtrusive UI surfaces.
Constraints: exactly two combatants and no other humans, NPCs, animals, monsters, portraits, banners with people, or silhouettes. Do not add a sword, magic glow, aura, particles, spell circle, attack impact, corpse, weapon, building, terrain blocker, modern object, text, calligraphy, readable Chinese or English labels, logo, watermark, title, UI glyph, or mock device frame. UI areas must be blank abstract editable art rather than baked copy. Do not invent Guard or Block indicators. Do not imply this is a running game or measured gameplay capture.
```
