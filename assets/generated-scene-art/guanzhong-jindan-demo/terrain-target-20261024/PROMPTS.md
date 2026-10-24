# 山谷阶地目标效果图：生成提示词

日期：2026-10-24。工具：内置 `image_gen`；新图生成与参考图重绘；不透明背景。依据根目录《开发计划》本轮特色演示目标。当前交付 V3：视觉沿用此前山雾、暖光冷影和斜俯六角战棋方向，参考原图的独立模块结构，不沿用旧 UI 数值及平面地图布局。


## V3 独立地块重绘提示词

用户确认：独立六角地块，同高度自然拼接，高低差、坡道和台阶清楚；后续再按图拆分资产。

输入：[原独立地块参考图](../../guanzhong-first-bounty/interaction-preview-20261021/reference-approved-layout.png)，作为结构重绘输入；保留模块形态和山雾光色，移除原 HUD／数值，将平面布局改为三档高差。V2 不作为本次几何输入。工具：内置 `image_gen`；不透明背景；输出 `terrain-target-v3.png`（1672×941）。

```text
Use case: stylized-concept / environment target redesign.
Asset type: one final target image for the Chinese cultivation hex tactics game 《天章》, suitable as a reference for later modular environment asset breakdown.

EDIT the provided flat hex-tile battlefield reference into a polished THREE-ELEVATION mountain-valley battlefield. Use the reference's individually recognizable HEXAGONAL TERRAIN MODULES as the structural foundation. Completely remove all HUD, portraits, lettering, numbers, health bars, monster, interface panels and selection outlines. Preserve the elegant Chinese mountain atmosphere, tactical oblique camera, warm sunlight, cool mist and stylized stone material. This is ONE unified finished game scene, no inset asset sheets, no exploded diagrams or annotations.

CRITICAL STRUCTURE:
The entire playable surface must visibly be assembled from consistent, reusable regular hexagonal terrain blocks, with the SAME cell size and orientation everywhere. Make the construction unmistakable: a regular honeycomb of grass-and-earth-topped stone hex modules; their seams continue down the exposed outside walls as individual faceted hexagonal rock columns. This is NOT a huge smooth sculpted mountain with hexagons drawn on top. The hex units should look deliberately designed and repeatable, with subtle natural material variation.
At the SAME elevation, neighboring hex tops TOUCH EDGE TO EDGE and form contiguous natural grassy/earthen walking surfaces: fine shallow seams only, NO deep cracks between every tile, NO air gaps and NO individually floating stepping stones. Expose the chunky stone side faces only at the perimeter or where a neighboring cell is lower. Keep the upper surface faces flat and walkable. Grass and soil blend in color across adjacent modules while their six-sided boundaries remain easy to read.
Exactly three broad LEVEL terrace bands made of these cells: low foreground valley, spacious central middle fighting terrace, upper rear-right command terrace. Height difference per band about one standing human, clearly visible and comfortably readable. Around 70–100 regular cells visually, each large enough for one human, with generous open areas on all three bands. Do not draw random irregular polygon paving or square tiles.

CLEAR ROUTES:
Keep all low-middle-high ground connections in view. At LEFT-CENTER, include a broad, unmistakably SLOPING earthen ramp with continuous inclined surface, visibly built as a small sequence of reusable wedge/transition modules. It connects flush to the low ground at its foot and the middle level at its upper landing; slope is distinct from flat tiers and stairs.
At RIGHT-CENTER, include a broad stone STAIRCASE with clearly visible short treads and risers, joining the central middle terrace directly to the upper rear-right terrace. Show its foot on a spacious middle-height landing and its top landing flush with the high terrace. The whole middle-to-high route is dry connected land with solid stone beneath, not crossing a river gap.
Add a modest second stepped connection from the low level to the middle terrace near the front-right, a readable alternative route.
All route connectors are about two hex cells wide, grounded solidly, with no tunnels, bridges, archways, hollow undersides or playable floors under another floor. Every horizontal hex column has exactly one playable surface. Heights alone do not make routes: a bare exposed cliff edge remains visibly unwalkable.
Leave one exposed central cliff edge without a ramp as a clear potential flight shortcut; do not surround the entire battlefield with impassable water.

SETTING:
Low foreground includes a modest shallow turquoise water pocket occupying a few adjoining low hex cells, thin muddy banks and clear dry access. Water belongs to the SAME hex board; its cell outlines are softly visible through it, and the shoreline follows module edges.
Middle terrace has a few clustered low broken stone walls for cover, one visibly taller short wall, and small patches of grass. Keep most cells open; avoid hiding tile boundaries behind excessive vegetation or rubble.
High terrace has one modest upright engraved stone anchor with restrained pale-gold light and room around it. Two small robed cultivators only as scale cues: one at low entry and one on the high level, on separate visible hex cells, no bases or UI. Do not add a flying character or magic dome; keep terrain geometry the focus.

CAMERA / FINISH:
Wide landscape 16:9. Elevated oblique orthographic tactical camera, around 40 degrees downward, enough elevation to see tile top faces, staircase treads and the ramp surface. The assembled map occupies about 80% of the frame and fits entirely inside it. Clear, crisp geometry, gentle painterly material detail, muted sage grass and warm earth rather than neon teal. Warm late-afternoon sunlight from upper left, cool slate-blue rock shadows, distant misty Chinese mountain peaks and a few atmospheric pines outside the playable surface. Fine module joints, subtle bevels on exposed hex edges, rock strata and moss, refined production-concept quality. Ensure three terrace heights, natural same-height connections, exposed modular cliffs, the continuous ramp and stepped staircase are all legible in this single image. No text, no UI, no watermark.
```

## V1 原始提示词

```text
Use case: stylized-concept.
Asset type: final target environment visualization for the Chinese cultivation hex-tactics game 《天章》, used to plan a real playable level.

Create ONE polished wide 16:9 game-environment target image of a mountain valley terraced tactical battlefield. This is a new environment concept, not an infographic, asset sheet, collage or cinematic poster. The main subject is the complete readable battlefield, with terrain and routes large enough to inspect. High-end stylized 3D diorama with subtly painterly hand-finished rock and vegetation, refined Chinese mountain atmosphere.

Camera and composition: fixed oblique orthographic tactical view, roughly 38–42 degrees downward, no fisheye, no extreme top-down view. Show the complete terrain comfortably inside the frame, with open space near the very bottom for a future HUD, but DO NOT draw any interface, text, numbers, logos or watermark. Battlefield occupies about 75% of image, distant layered peaks and luminous valley mist behind, dark out-of-focus pine framing only at outer corners.

Terrain design: roughly one hundred coherent regular flat-topped hex cells form a connected natural mountain landform, not a flat rectangular floating checkerboard. Exactly three broad readable elevation bands: a low valley floor with a shallow turquoise stream and muddy banks; a generous middle terrace for fighting; a higher rear terrace and commanding ridge. Each terrace contains substantial contiguous level ground. Short exposed stratified cliff faces clearly show the height change. Steps between bands are roughly a human body's height. Terrain is grounded into the mountain, with natural rocky edges, not hundreds of separate spindly floating pillars.

Two obvious ground connections: a wide gently winding stone-and-earth ramp climbs from the low valley into the middle terrace; a second readable stepped/ramped route climbs around the flank to the high terrace. Both routes have room for a creature occupying two adjacent hexes. Show an open cliff boundary where adjacent high and low hex columns have no ground connector, creating an obvious flight shortcut. Keep the valley water on its own exposed low cells; no bridges, no tunnels, no archways, no overlapping floors, no roof-like overhang above playable cells. Every horizontal hex column has one single surface.

Tactical details: small clusters of warm-gray broken stone waist-high cover on the middle terrace, one short thicker upright ruined wall for blocking a firing line, a few sparse shrubs or low plants along the water, dry earth and yellow-green grasses. Distinguish low cover from a full wall and from impassable cliff edges. One modest upright engraved stone anchor on the high terrace with a restrained pale-gold glimmer makes a future breakable rule source legible; no giant magical dome, no enormous circular sigil. Stone and water dominate, plants are secondary.

Hex readability: draw regular subtle six-sided cell seams on the walkable top surfaces, consistently aligned across the three height levels and faintly visible through shallow water. They should read like a coherent tactical grid over natural terrain, not bright neon tiles. Ramps retain the same hex layout. Never draw square paving as the gameplay grid, scattered giant hexagons, hidden floors or independently floating islands.

Scale figures: only three small elegant robed cultivators, full bodies visible, without health bars or pedestal bases. A dark-blue ground cultivator near the lower entry, a warm-gray opponent on a separate high terrace cell, and one lightly hovering cultivator by the cliff shortcut above an otherwise EMPTY mid-level hex column, with a subtle vertical ground-projection cue. No person stands beneath the flying figure. Figures are secondary scale cues, the terrain is the hero.

Light and color: warm soft afternoon sun from upper left, amber rock edges, cool blue-green shadows, desaturated sage grass, cyan shallow water, layered mist with distant mountains less contrasted than the battlefield. Beautiful material detail, crisp readable foreground landform and gentle atmospheric depth. The final feeling is an attainable elegant Chinese fantasy tactical-game scene with meaningful routes and elevation, not photorealistic open-world scenery and not toy-bright mobile terrain.
```

## V2 定向修订提示词

输入：`terrain-target-v1.png`，作为编辑目标。原因：右侧楼梯与中台的陆路连接不够清楚，补接一段有实体基底的坡颈，保留断崖捷径。

```text
Use case: precise-object-edit.
Edit this mountain-valley hex tactical environment target image with ONE local functional correction: make the ground route from the large central fighting terrace to the right-side staircase and upper command terrace unmistakably continuous.

Connect the RIGHT-REAR EDGE of the large CENTRAL TERRACE to the LOWER TURNING LANDING of the EXISTING RIGHT-HAND STAIRCASE with a short, wide, grounded earthen-and-stone saddle/ramp. The connector must have a visible continuous walkable top with subtle hex cell seams and be wide enough for a two-cell creature. It is a solid land neck all the way down to the terrain, NOT a bridge, NOT an arch, NOT a suspended walkway. There is no playable surface beneath it. Shape the nearby cliff/water edge only as much as needed so the full path can be visually traced: lower entry -> left ramp -> central terrace -> new right-rear connection -> existing stairs -> upper terrace. This land neck is further back than the hovering figure and its small cliff projection; keep that foreground cliff shortcut and empty-column flying figure intact.

Preserve the original camera, overall composition, all three main terraces, terrain art style, subtle hex grid, warm sun and cool mist, low water valley, central broken cover, upper engraved anchor, three cultivators, and clear flight shortcut. No new floors, no vertical stacking, no new figures, no HUD, no text, no arrow diagram, no decorative magical effects. Keep the result a beautiful polished 16:9 game-level target illustration.
```
