# 后／侧腰挂袍片四视图

日期：2026-11-03。只生成单个独立袍片；装配时镜像得到一对。使用内置 image_gen，未调用 Tripo、未修改 Blender、未程序化绘画、未做裁剪或重采样。

## 参考与目标

原始风格参考：`D:/天章游戏开发/assets/generated-character-art/fuyuan-jointed-doll-concept-20261103/jointed-doll-assembly-concept-v2-unified-color.png`。

目标尺寸为提示词约束：总高 0.78m、上宽 0.20m、下宽 0.38m、实体边厚 8mm、横向浅弧深约 5cm。这些是制作意图，不是由图片验证的三维尺寸。外侧墨黑哑光金裂纹、无显眼象牙纵条；内侧完整暖灰象牙背衬；顶部实心挂耳与铜钉，无贯穿孔。

## 输出与实际像素验证

四图均为 1254 × 1254 RGBA PNG，alpha 范围 0–255。透明背景。主体边界按 alpha ≥ 128 测量，坐标为左、上、右、下（右下不含）。

|视图|主体边界|主体宽 × 高|来源|
|---|---|---|---|
|front|312,82,938,1186|626 × 1104|exec-abeda17b-6f7e-4274-be63-070ee0f34dbd.png|
|back|311,83,942,1185|631 × 1102|exec-82a9c5c9-b0eb-40a0-b2b2-6ef7c0f9ea83.png|
|left|584,74,643,1180|59 × 1106|exec-84498d85-a752-461f-ace2-c5e4c22d2d6b.png|
|right|604,72,669,1183|65 × 1111|exec-5b9ed238-081e-4b3b-b65f-2762a4fc854d.png|

原始生成路径根：`C:/Users/WINDOWS/.codex/generated_images/01a1a1f6-0bbe-7852-98c8-d2ea2aabacb9/`。已复制为本目录 `front.png`、`back.png`、`left.png`、`right.png`，原文件保留。

## 目检与限制

- 单物体完整，无身体、裤鞋或附带第二片；无字、尺寸线或场景。
- front 保持深色金裂纹，back 为完整浅色内衬，外侧没有正面衣片那种白色纵条。
- left 为黑金外侧朝画面左、象牙背衬朝画面右；right 相反，铜钉侧向同时反转。两侧均窄薄，未画成另一张正面。
- 正背主体高度差 2px；全部视图主体高差最大 9px（约 0.8%）。侧图顶部比正背高约 8–11px、底部高约 2–6px，轻微生成偏移尚在；没有进行程序化对齐。
- 正背可见宽高比约 0.568，较尺寸目标的 0.487 更宽；入模后必须按约 0.78m × 0.38m 做尺寸适配，不能把图像比例宣称为精密量测。
- 侧图显示浅弧和细薄边缘，但并非工程截面；双面曲率与8mm实体厚度仍须由 Tripo 结果检查。
- 非零 alpha 边界有低透明度远离主体的微弱噪点，因此以 alpha≥128 测主体，不把全 alpha 边界视为实际轮廓；图像未清理。
- 没有发现需要额外重生成的明显造型漂移；共生成4张，无修正轮。

## front 精确提示词

输入：原始概念参考。transparent_background=true。

```text
Use case: stylized-concept. Create ONE standalone strict orthographic FRONT view of a single detachable REAR/SIDE WAIST ROBE PANEL for the same Fu Yuan articulated doll in the reference sheet. The reference defines the black ink-painted material, restrained antique-gold branching cracks, broad deliberate folds and thin garment-panel construction. Do NOT reproduce the sheet or the character.

This single rear/side panel will later be mirrored to make a pair. It is a vertically hanging flared trapezoid: about 0.78 m tall including a short centered solid top mounting tab, 0.20 m wide at the top shoulder and 0.38 m wide at the hem. Thin but physically solid 8 mm edges. Shallow transverse curvature around the body, about 5 cm total depth across the lower width, no tube and no complete wraparound skirt. Three subtle broad longitudinal planar facets, not many wrinkles. The front means the OUTWARD painted face. Ink charcoal-black matte surface, fine sparse aged-gold branching crack lines in the reference style. The outer face is dark throughout with dark narrow binding: NO bright ivory longitudinal stripe on this rear panel. A warm gray-ivory backing exists behind the entire panel but is mostly occluded in this frontal view. A single short solid dark tab projects upward from the center of the top, with a small flat aged-brass pin cap bonded to it; no through-hole, ring or floating metal part. Hem is a restrained slightly angled shallow curve; no fringes, straps or extra side pieces.

One complete object centered upright on a SQUARE genuinely transparent canvas. Keep top of tab at about 8% and hem at about 92% of image height, with comfortable side margins. Strict frontal orthographic view looking squarely at the outward face, no three-quarter perspective. Even neutral soft lighting, no baked cast shadow, no floor, no background panel, no text, arrows, measurements or watermark. Absolutely no body, waist, torso, leg, pants, shoe, hand, second panel or exploded hardware. Keep the silhouette crisp and all edges uncut. This front image will anchor the same asset's back and side views.
```

## back 精确提示词

输入：front.png。transparent_background=true。

```text
Use case: identity-preserve. Render ONE strict orthographic BACK view of the EXACT single detached robe panel shown in the input. Rotate this same rigid panel precisely 180 degrees around its upright vertical axis. Do not redraw a new shape. The input locks its exact flared trapezoid silhouette, top tab, pin cap position, height, width, three longitudinal facets, edge binding, shallow transverse curvature, and 8 mm physical thickness. Same SQUARE canvas size and exact top-of-tab / hem heights as input. The tab stays centered at the top, hem stays down, do not turn the object upside down.

This BACK view shows the complete INNER BACKING face of that one same rear/side robe panel. The whole inner face is matte warm gray-ivory cloth backing with subtle hand-painted mottling and faint longitudinal fold shading, surrounded by the same narrow dark charcoal binding. It is NOT transparent, NOT a hollow void and NOT another outward black-gold face. On this view the same single solid dark top mounting tab is seen from its reverse, with a small muted bronze flat rear rivet cap attached to the tab; no through-hole. The reverse transverse curvature is gently concave, not a tube or bowl. The gold crack pattern belongs to the hidden exterior, so do not cover the lining with gold cracks. No additional garment pieces.

One object centered, strict rear view, no three-quarter perspective, no front face shown beside it. Truly transparent background, soft neutral illumination, no cast shadow, no floor, no text, labels, arrows, dimensions, frame or checkerboard. Absolutely no person, waist, limbs, pants, shoes, separate pin, detached hardware or extra panel. Keep the full silhouette and identical scale to the reference.
```

## left 精确提示词

输入：front.png, back.png。transparent_background=true。

```text
Use case: identity-preserve. Render ONE strict orthographic LEFT SIDE view of the EXACT single detached rigid robe panel in these two references: image 1 is the outward painted FRONT and image 2 is the lined BACK. Rotate this same object exactly 90 degrees around its upright vertical axis to see its left edge. Never redesign or unfold it. This is a very thin SHALLOW CURVED SHEET, not a sleeve, tube, boxed piece or broad side-facing slab. The 0.78 m overall height stays exactly the same as the references; the 0.38 m flared lower width is now strongly foreshortened edge-on. Actual material thickness is only 8 mm; the whole projected depth including transverse curvature is about 5 cm (about 1/15 of its height). Therefore the entire side silhouette should be a narrow upright strip only about 70 pixels wide for a roughly 1100-pixel-high object, centered on the same square transparent canvas. Do NOT enlarge it to fill the frame horizontally.

Show the full top tab and curved hem at identical image heights as the references. No longitudinal bending: upper and lower ends approximately vertically aligned; curvature runs across the sheet's width, not down its length. Show the slender physical dark edge and enough shallow transverse curve to make the double-sided solid construction clear. In this LEFT view the hidden black-gold OUTER surface lies on SCREEN LEFT and the warm gray-ivory INNER lining lies on SCREEN RIGHT. At most extremely narrow glimpses of these surfaces are visible beside the dark perimeter edge. No broad frontal trapezoid should be visible. The centered solid top tab is seen edge-on with the small bonded brass rivet cap, no hole or detached hardware. Exactly one piece.

Orthographic technical side view, no perspective or three-quarter rotation. Same style, material and neutral soft lighting as inputs. Truly transparent background, no text, floor, shadow, grid, measurements or extra object. Absolutely no waist, torso, leg, trousers or shoe. Keep complete object, exact same vertical scale and empty side margins.
```

## right 精确提示词

输入：front.png, back.png, left.png。transparent_background=true。

```text
Use case: identity-preserve. Produce ONE strict orthographic RIGHT SIDE view of the EXACT standalone robe panel shown in the supplied references. Image 1 is the FRONT exterior, image 2 is BACK lining, and image 3 is LEFT SIDE. View the SAME upright thin object from its opposite right edge. Reference 3 is the exact target for the side silhouette width, total height, tab height, narrowness and shallow curvature. This right view must be the opposite camera direction with the black-gold OUTER surface on SCREEN RIGHT and the warm gray-ivory INNER backing on SCREEN LEFT. The small flat antique brass pin cap on the top tab therefore projects toward SCREEN RIGHT. This left-versus-right orientation is essential.

Do not invent new form. Keep the 0.78m high hanging flared panel, 8mm physical material thickness and only about 5cm total transverse curvature. It is a thin sheet seen edge-on, NOT a rectangular column or tube. The side profile remains a very narrow upright strip about 70px wide in the same square canvas; do NOT zoom or enlarge its width. Only narrow strips of black-gold exterior and ivory lining can be glimpsed around the dark physical perimeter. No broad trapezoid face. Keep the tab top and hem bottom at precisely the same image heights as reference 3, with identical overall visual scale as the front/back. The upper tab is solid, with bonded brass rivet cap, no hole or separate hardware. One continuous complete object.

Transparent background, same subtle painted cloth material, neutral soft lighting, no floor or cast shadow. No text, labels, measurements, frame, grid or watermark. No person, torso, waist, legs, trousers, shoes, sleeve or second panel. Strict 90-degree edge-on orthographic right view with the full object uncut.
```
