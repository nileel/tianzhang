# PC 宽屏人物展示分层提示词

用途：统一《天章》重要角色在 PC 人物介绍、角色资料与对话界面的 2D 立绘风格。16:9 是最终 UI 容器；透明人物母版仍采用竖向全身构图，并默认供人物介绍、对话半身和头像裁切共同使用。

## 全角色统一风格核心

```text
Use case: stylized-concept
Asset type: layered PC character-profile art for the 2D xianxia tactics RPG 《天章》
Primary request: Create an original Chinese xianxia character presentation in a refined 2D guofeng anime illustration style, designed for runtime layer composition rather than a single baked poster.
Style/medium: decisive ink-like linework, softly cel-painted planes, elegant simplified facial volumes, slightly idealized illustrated hands, restrained Chinese ink-wash texture, delicate dry-brush edges, pale watercolor atmosphere, clean premium game-character finish.
Face and hands: clearly illustrated and stylized, anatomically readable but never photographic; no skin pores, no realistic skin texture, no photo lighting, no oily highlights, no plastic skin, no uncanny 3D volume.
Materials: clear separation between rough cloth, silk lining, old bronze, jade, lacquer, ink, paper and restrained metallic accents.
Lighting/mood: soft frontal diffused light, faint controlled rim light, no harsh cast shadow.
Composition: one complete standing character with head, both hands, robe hem and feet fully visible; clean silhouette at dialogue scale; no throne unless the character facts explicitly require one.
Runtime layers: textless 16:9 background; optional symbolic FX overlay; transparent full-body character master; optional foreground FX; separate correct Traditional Chinese name, Dao title and seal; static 16:9 preview only as a fallback.
Constraints: original Chinese fantasy identity; correct anatomy; exactly two arms, two hands and two feet; no modern clothing, European throne, Japanese kimono structure, cyberpunk, sci-fi weapons, neon, photorealism, 3D render, UI panels, random glyphs, watermark or logo.
```

角色专属提示词只替换身份事实、年龄、体型、发型、服装、势力、道途象征、主配色和标志物；不得改变上述绘制方式、脸手风格、分层职责与验收边界。

## 通用背景层

```text
Use case: stylized-concept
Asset type: textless exact-16:9 background layer for a PC xianxia character-profile UI
Primary request: Create a warm ivory xuan-paper background with a restrained low-opacity Chinese ink landscape derived from the character's region and faction. Keep the center and intended character side quiet enough for a separate full-body character layer; keep the title side clear for separate UI text.
Style/medium: refined 2D Chinese ink-wash game UI background, delicate dry-brush edges, pale watercolor atmosphere, soft paper grain, premium and uncluttered.
Composition/framing: exact 16:9 landscape; important silhouettes stay near outer edges and lower third; generous negative space; background only.
Constraints: no person, readable text, letters, numbers, seal, emblem, UI panel, halo, symbolic FX that should animate, watermark, photorealism or 3D render.
```

## 通用透明人物母版

```text
Use case: background-extraction
Asset type: reusable transparent full-body character master for PC character-profile, dialogue crop and portrait crop
Primary request: Create one complete full-body standing character on a perfectly flat solid #00ff00 chroma-key background for later alpha removal.
Style/medium: refined 2D guofeng anime illustration with controlled ink-like linework, softly cel-painted planes, elegant simplified facial volumes, slightly idealized illustrated hands and premium game-character finish; clearly illustrated rather than photographic.
Cutout-safe silhouette: compact connected opaque silhouette; cohesive broad hair locks; intact continuous hems; no hair-thin flyaways, detached wisps, transparent holes, smoke, aura, particles, orbit lines or isolated marks.
Scene/backdrop: perfectly flat uniform exact #00ff00 only; no texture, gradient, shadow, floor, reflection, paper, scenery, halo, contact shadow, border or vignette; never use green in the subject.
Composition/framing: tall full-body portrait, centered, generous uniform green padding, nothing cropped.
Constraints: no text, calligraphy, title, seal, UI, watermark, logo, throne, modern elements, photorealism or 3D render; exactly two arms, two hands and two feet.
```

默认抠图：

```powershell
python 'C:\Users\WINDOWS\.codex\skills\.system\imagegen\scripts\remove_chroma_key.py' --input <green-source.png> --out <character-full-rgba.png> --auto-key border --soft-matte --transparent-threshold 12 --opaque-threshold 128 --despill --edge-contract 1 --force
```

## 通用法相 / 墨迹 FX 层

```text
Use case: stylized-concept
Asset type: removable-background exact-16:9 symbolic FX overlay for a PC xianxia character UI
Primary request: Create only the character's approved symbolic energy, order diagram, ink motion or elemental ornament on a perfectly flat solid #00ff00 chroma-key background.
Style/medium: refined 2D Chinese ink and restrained metallic ornament, sparse elegant geometry, controlled dry brush, no sci-fi machinery.
Composition/framing: exact 16:9; keep the title safe area mostly empty; place the principal symbol behind the future character rather than in front of the face or hands.
Constraints: effects only; no person, landscape, paper, architecture, readable text, pseudo-text, talisman glyphs, seal, emblem, UI panel, watermark or logo; avoid translucent smoke and detached hair-thin specks.
```

## 文字与印章

- 姓名和道号以正确繁体文本数据为准。
- 优先从已人工验字的批准样张中逐像素提取透明书法层；不得为了“更像书法”重新交给模型改写复杂字。
- 印章单独导出 RGBA PNG，文字逐字验收；普通 NPC 没有道号或印章时直接隐藏对应层。
- 运行时可对姓名、道号做遮罩揭示，对印章做短促落印与朱砂晕染。

## 苻淵 / 含弘真君分层设定

```text
Character identity: 苻淵, Dao title 含弘真君; elderly Chinese male, broad sturdy shoulders, stern restrained gaze, white-gray topknot and cohesive shoulder-length hair, short pointed white beard and moustache.
Pose: calm three-quarter standing pose facing the viewer; left hand open near the waist as if weighing an invisible obligation; right hand relaxed beside the robe.
Clothing: layered charcoal-black old Daoist robes, pale inner collar, restrained antique-gold broken covenant seams, old-bronze waist fastener and tiny pale-jade pendant; worn but intact, unimperial, no weapon.
Narrative mood: burden, containment, unresolved responsibility, collective consequence and the possibility of trying again after a failed Deeds-and-Obligations Nascent Soul attempt.
Region/background: Guanlong ink mountains, sparse old pines, low fog and a subtle ruined pass.
Symbolic FX: one visibly incomplete covenant/order circle, sparse antique-gold commitment orbits and nodes, broad restrained charcoal ink arcs; never a perfect halo.
Palette: warm xuan-paper white, deep charcoal black, warm gray, restrained antique gold, tiny pale jade and one vermilion seal.
Text data (verbatim): "苻淵"; use Traditional 淵 U+6DF5, never simplified 渊.
Dao title data (verbatim): "含弘真君".
Seal data (verbatim): "含弘".
```

当前苻淵成品层：`fu-yuan-hanhong-zhenjun/`。静态预览为 `fu-yuan-profile-preview-16x9-v1.png`；运行时人物、背景、FX、姓名、道号与印章均使用独立文件。
