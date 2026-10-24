# 山谷阶地目标效果图：生成提示词

日期：2026-10-24。工具：内置 `image_gen`；新图生成；不透明背景。依据根目录《开发计划》本轮特色演示目标。视觉沿用此前山雾、暖光冷影和斜俯六角战棋方向；旧 UI 费用与旧地图布局不作为本图输入。

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
