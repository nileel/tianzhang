# 运行时 3D 模型效果演示提示词

用途：依据批准立绘生成非正式的 3D 模型方向演示。输出只用于评审角色比例、轮廓、材质和多视角结构，不作为 Unity 正式模型资源。

## 苻渊 / 含弘真君（新立绘低模三视角）

```text
Use case: stylized-concept
Asset type: project art-direction preview for a production runtime 3D character model in the 2.5D hex-grid xianxia tactics RPG 《天章》

Input images:
- Image 1 is the decisive new Fu Yuan full-body identity, face, hair, clothing, silhouette, palette, and accessory reference. Preserve this redesigned character.
- Image 2 is supporting identity and material reference for the same new Fu Yuan.
- Image 3 is only the earlier approved presentation format and runtime-style boundary: a small true low-poly 3D unit on a raised hex tile under an orthographic tactical camera. Do not preserve the old Fu Yuan costume, loose torn hems, old hair treatment, or old facial design.

Primary request: Create one clean landscape model-effect demonstration sheet with exactly three equal vertical panels. Show the same final production-style low-poly 3D Fu Yuan model, consistent across all panels, standing on the same simple raised stone-and-earth hex tile. Panel 1: front-left three-quarter tactical view. Panel 2: front-right three-quarter tactical view. Panel 3: rear three-quarter tactical view, clearly showing the hair mass and robe construction. Same orthographic camera height, same character screen height, same neutral lighting, same model proportions. This is a model look-dev sheet, not portrait art.

Subject: 苻渊 / Fu Yuan, current title 含弘真君. An imposing elderly Chinese male cultivator with broad sturdy shoulders, stern restrained gaze, mature angular face, white-gray hair in one simple topknot with a cohesive shoulder-length back hair mass, short pointed white beard and moustache. Use a compact readable silhouette at about 4.5–5 heads tall for the distant tactical camera. His pose is a calm grounded idle stance, arms relaxed, no weapon.

Clothing and identifiers: layered charcoal-black old Daoist robes with broad controlled sleeves and a long intact grounded silhouette; pale gray-white inner collar and inner sleeve lining; old-bronze circular waist fastener; one tiny pale-jade pendant. Preserve only sparse simplified antique-gold broken covenant seams across the outer robe as large readable surface accents; they terminate inside the cloth and never become detached effects. Reduce the portrait's tiny woven ornament into low-frequency hand-painted texture. No throne, no halo, no orbit diagrams.

Style/medium: unmistakably a real low-poly 3D game model, not a flat drawing or sprite. Production-feasible faceted geometry, roughly the visual density of an 8,000–15,000-triangle humanoid; deliberate planar hair, beard, face, sleeves, robe panels, hands and shoes. Hand-painted low-saturation ink-wash texture, two-to-three-band Toon lighting, restrained dark brush-like contour, soft desaturated shadows, matte rough cloth, subtle old bronze and pale jade material separation. The model should feel like a “水墨棋偶”: dignified, solid, sober, readable, and clearly three-dimensional.

Scene/backdrop: identical neutral warm-gray studio/gameplay background behind each panel, same raised hex tile, no extra scenery.
Composition/framing: wide 16:9 landscape comparison sheet, three equal panels divided only by thin neutral separators, generous padding, all topknot, robe hem, shoes, and tile fully visible.
Lighting/mood: soft neutral orthographic gameplay lighting; quiet authority, burden, containment, unresolved responsibility.
Color palette: deep charcoal black, warm gray, off-white, restrained antique gold, tiny pale jade, muted stone and earth.
Constraints: exactly one Fu Yuan per panel and the same model in every panel. No text, labels, calligraphy, seal, UI, logo, watermark, spell effects, smoke, particles, floor shadow beyond a soft contact shadow, photorealism, glossy mobile-game rendering, painterly full-body illustration, sprite, paper-doll, crown, imperial regalia, armor, staff, sword, exposed skin, extra limbs, cropped body, detached hair strands, ragged/torn robe holes, transparent cloth, or busy embroidery.
```

输入图顺序：

1. `dialogue-transparent/fu-yuan-hanhong-zhenjun-standing-black-gold-v1.png`：新立绘身份与服装主参考。
2. `profile-wide/fu-yuan-hanhong-zhenjun/fu-yuan-profile-preview-16x9-v1.png`：新立绘脸型与材质辅助参考。
3. 旧会话批准的运行时角色路线对比图：本次生成时只参考六角地块、正交镜头和低模边界。原图未在仓库落盘，当前无法按文件哈希复现；后续重新生成前必须先补齐受控路径与 SHA-256，本条不能作为可复现输入证明。
