# 实际内置 ImageGen 提示词

共两次调用。首稿是 RGB 棋盘背景，不能当透明资产；第二次背景改为绿幕，以确定性 alpha 提取输出。第二次仍未去掉 torso 下方玉坠，因此 torso 仅保留颈至腰带区域，完整下摆由独立 lower robe 层承担；不是补画或逐帧动画。

## 首稿

```text
Use case: stylized-concept
Asset type: a production cutout character PARTS ATLAS, NOT animation frames. Generate one 2048x1536 transparent PNG, four columns by three rows, twelve separated painted pieces of the SAME elderly Chinese cultivator Fu Yuan. Real transparent alpha background, no checkerboard, no labels or grid lines. Leave generous transparent margins in every 512x512 cell, no part crosses a cell boundary.
References: image 1 approved identity, image 2 material and face only, image 3 six-direction tactical style. Use the FRONT THREE-QUARTER direction of TOP MIDDLE figure in image 3: face points toward screen LEFT, elevated orthographic tactical camera about 38 degrees. Fixed direction for ALL pieces, no alternate views. Mature angular weathered face, white-gray topknot, short pointed white beard/moustache, charcoal matte Daoist robe, light gray lining, sparse fine antique gold broken seams, bronze belt clasp and a tiny pale jade pendant. Painterly Chinese guofeng 2D game art, readable restrained shading, not photoreal or 3D. Do not include captions, glow, shadows or effects.
This is for skeletal puppet assembly and strong casting motion, so draw COMPLETE hidden surfaces with rounded overlap tabs. No baked arms/hands/head in torso or sleeve pieces. Keep solid opaque cloth and rounded broad heavy sleeves.
Exact row-major contents:
row 1 col 1: complete UPPER TORSO only from neck to below belt, armless and headless. Include full closed shoulders, both armpit side surfaces, collar, crossed lapels, waist sash and bronze clasp. Rounded cloth shoulder caps for overlap. No dangling hair or hands. Extend below belt for skirt overlap.
row 1 col 2: complete BACK LOWER ROBE skirt from waist to ankle, broad dark draping silhouette, gray inner hem. No feet, head, arms or belt. Entire hidden upper waist painted.
row 1 col 3: FRONT LOWER ROBE center panel from waist to ankle, long charcoal front lapel panel with subtle antique-gold broken seams and one tiny jade hanging ornament. No belt, feet, arms or torso. Entire top overlap painted.
row 1 col 4: HEAD with face, ears, short beard, topknot and crown hair, and short neck overlap, looking three quarters toward LEFT. NO long rear hair, shoulders or clothing.
row 2 col 1: separate REAR HAIR mass, white-gray cohesive shoulder-length strands with painted continuous hidden top behind head. No face/head/neck/clothes.
row 2 col 2: NEAR SIDE WIDE SLEEVE, separate whole closed cloth from rounded shoulder overlap at upper-left, through elbow at center, to empty gray-lined wrist opening at mid-right. Arm direction diagonal down-right 40 degrees, sleeve root at approximately (100,75), elbow (235,150), cuff at (390,230) in its 512 cell. Very broad rounded hanging sleeve BELLY beneath arm extending to approximately y450. Heavy wide U-shaped drape, visibly capacious, NOT a narrow tube. Subdued gold seams. NO hand or skin.
row 2 col 3: FAR SIDE WIDE SLEEVE, separate complete cloth, shoulder overlap upper-right, elbow center, empty cuff mid-left; broad U shaped hanging belly below, no hand/skin.
row 2 col 4: NEAR HAND and long wrist overlap emerging toward lower-right, relaxed open palm with slightly curled fingers, anatomically clear five digits. Empty, no effects, no sleeve fabric. Same elderly man's hand.
row 3 col 1: FAR HAND and long wrist overlap angled lower-left, relaxed empty gently curled hand. No sleeve.
row 3 col 2: NEAR FOOT, one black cloth shoe in elevated three-quarter left direction, complete heel/ankle overlap; no robe.
row 3 col 3: FAR FOOT, one black cloth shoe in same elevated three-quarter left direction, complete heel/ankle; no robe.
row 3 col 4: NEAR SLEEVE INNER LINING, a small curved gray inner cuff crescent matching the sleeve cuff at row2col2, with deep dark inner opening, no hand, no full sleeve.
Scale each piece to comfortably fill its own cell for high-quality cutting. These are exploded modular parts, not a complete assembled person. Each cell contains ONLY its specified isolated piece. All edges must be clean alpha.
```

## 单次修正

```text
Edit this parts atlas only. Keep all twelve pieces in the same exact positions, sizes, camera direction, colors, drawing style and silhouettes. Replace the entire fake gray-white checkerboard background with perfectly flat pure CHROMA GREEN #00FF00 edge to edge, including all holes/gaps. Do NOT draw transparency, checkerboards, lighting, shadows or green inside the painted pieces.
Small structural corrections only: remove brown armor caps at the torso shoulders and both sleeve shoulder roots, replacing with continuous soft charcoal cloth. Remove the jade pendant from upper torso (keep it on front skirt only). Remove the duplicate topknot from the separate rear-hair piece (row2 left), make its top a simple continuous gray hair mass that will fit behind the head. Keep topknot on head piece. No new parts, no labels, no full character. Preserve large wide hanging sleeve bellies.
```
