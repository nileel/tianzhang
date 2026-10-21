# V1 环境分层素材生成提示词

2026-10-21；使用内置 `image_gen`，未使用 CLI/API 回退。源输出复制到本目录，未做像素裁切、抠图或缩放；保留生成的 Alpha。图像尺寸以实际 PNG 为准，提示词中的目标尺寸和透明度不是工具保证。

## 01-distant-canyon.png

编辑输入：`../interaction-preview-20261021/reference-approved-layout.png`，移除棋盘、角色、UI 与近景，仅保留山谷画风。

- 工具参数：`transparent_background=false`。
- 原始输出：`exec-b747f2cc-c9cf-4f0e-84a9-8a72ab4b43ff.png`，位于本次 Codex generated_images 会话目录。

```text
Use case: background-extraction. Asset type: production background plate for a Chinese xianxia orthographic tactical game. Edit the provided approved reference into ONLY its distant environment background, full-bleed landscape 16:9, ideally 3840x2160.
Remove ALL interface, lettering, portraits, buttons, health bars, characters, beasts, hexagonal board, grassy platform, rocks on that platform and all close foreground branches. Reconstruct a continuous naturally painted mountain ravine behind these removed objects. There must be NO hole or empty rectangle where the board used to be, and NO floating island or playable ledge.
Preserve the reference's restrained high quality stylized 3D/painterly game look, blue-gray vertical rocky canyon pillars with sparse small pine trees, layered pale distant peaks, soft atmospheric mountain mist in the valley. Mountains on the left and right edges frame a quiet open hazy ravine in the middle 70% of image. Upper left warm pale golden daylight, shaded right faces cool desaturated slate blue; low contrast and atmospheric perspective. Warm light area near upper left but no visible sun disc, no bright lens flare. Bottom half continues down into pale blue-gray fog-filled valley, detailed side cliffs only at outer edges. Keep center and lower center visually calm to sit behind a large live 100-hex board. No near foreground branches, no dark frame, no temple, no buildings, no waterfalls, no animals, no text, no UI, no grid. The painting extends to all four edges without borders. Opaque background.
```

## 02-foreground-left.png

独立生成，无输入图片。

- 工具参数：`transparent_background=true`。
- 原始输出：`exec-7d5ac57c-2f75-4ce2-8952-f53479c96c48.png`，位于本次 Codex generated_images 会话目录。

```text
Use case: stylized-concept. Asset type: transparent isolated LEFT foreground pine-and-rock sprite for a Chinese xianxia orthographic tactical game, landscape 1536x1024.
Create one single connected composition: a dark weathered blue-gray rock outcrop in the lower-left corner with a short gnarled pine trunk and sparse elegant needle boughs reaching gently upwards and to the right. Matches a polished painterly/stylized 3D Chinese mountain valley, natural rough granite and clustered pine needles, readable restrained detail. Warm golden daylight from upper left grazes the upper-left edges, deep desaturated cool slate shadows, darkest foreground values.
Composition: most opaque mass confined to left 45% and bottom 35%; the right 35% and top 12% completely transparent. Pine tips can extend into central third only in the lower half. Do not create a large full tree canopy. Rock and trunk intentionally continue off the left and bottom canvas boundaries as a corner framing piece. Inward tips end naturally with clean antialiased alpha, no matte fringe, no drop shadow. Modest fine detail, no baked depth-of-field blur.
Actual transparent RGBA background. No sky, no backdrop, no ground plane, no mountain panorama, no fog, no board, no hexes, no characters, no interface, no text, no checkerboard painted into image. This is one standalone reusable decorative sprite, not a mockup.
```

## 03-foreground-right.png

独立生成，无输入图片。

- 工具参数：`transparent_background=true`。
- 原始输出：`exec-a8aaf8dc-255a-4a41-bde9-423d38bf6340.png`，位于本次 Codex generated_images 会话目录。

```text
Use case: stylized-concept. Asset type: transparent isolated RIGHT foreground pine-and-rock sprite for a Chinese xianxia orthographic tactical game, landscape 1536x1024.
One single connected asymmetric composition rooted in the BOTTOM RIGHT: a dark rugged cool slate-gray rock outcrop with a small bent pine trunk and sparse horizontal pine-needle boughs reaching LEFT from the right border. This is the right-hand partner to a left corner framing sprite, but not a mirror: shorter trunk, two broad sparse needle clusters, jagged lower rock shapes. Polished painterly/stylized 3D Chinese mountain game asset, natural rough granite and pine needles, restrained detail and crisp antialiasing.
Lighting: warm golden sunlight always from UPPER LEFT of image, grazing upward and left-facing rock edges and pine tips. Deep desaturated cool blue-gray shadows on right-facing surfaces. Dark quiet foreground values.
Most mass in right 40% and bottom 30%; leftmost 35% and top 12% totally transparent. Thin bough tips may reach center only in lower half. No huge tree canopy. Rock and trunk intentionally continue beyond right and bottom canvas boundaries as a corner framing piece. Inward contour has natural cutout tips with NO surrounding yellow glow, NO aura, NO color cloud, NO shadow matte, NO atmospheric backdrop, NO blur.
Output actual RGBA with alpha=0 everywhere except the physical needles, wood and rock. Do not paint black or green background. No sky, landscape, fog, board, hexes, people, creatures, text, interface or watermark. One clean separate decorative sprite.
```

## 04-valley-mist.png

独立生成，无输入图片；实际中心 Alpha 接近不透明，使用时以材质 Alpha 0.30 起调。

- 工具参数：`transparent_background=true`。
- 原始输出：`exec-dfcc76f3-b84b-4355-ac80-f7cd226c361f.png`，位于本次 Codex generated_images 会话目录。

```text
Use case: stylized-concept. Asset type: one reusable horizontal mountain MIST texture for a Chinese xianxia tactical game, landscape 1536x1024, actual transparent RGBA.
Create only a soft, low-density bank of atmospheric blue-gray mountain mist seen from a downward camera. A few broad flattened wisps and gently rolling veils in the middle of the image. Delicate cool slate blue-gray and pale ivory wisps, very slight warm upper-left highlights, subdued painterly natural fog. The fog is translucent throughout, peak opacity about 45%, never solid opaque white. Its dominant horizontal band occupies the middle 55% of height, and softly fades out in every direction to fully transparent on ALL four boundaries, with at least 8% completely transparent margin. Different-sized wisps with irregular silhouettes, no distinct repetitive circles, no hard rectangular boundary.
Empty alpha is the background, not black paint, not a painted checkerboard. Every pixel outside the mist has alpha 0. No sky, no mountains, no rocks, no water, no trees, no horizon, no smoke source, no VFX glow, no dramatic storm, no interface, no text, no grid, no board, no people or creatures. This texture will be placed on a horizontal alpha-blended plane BELOW live terrain to soften the base of cliffs. Do not include any terrain within this texture.
```
