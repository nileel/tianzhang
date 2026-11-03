# 苻渊左前腰挂袍片四视图

2026-11-03。独立生成单位：一个角色本人左前腰挂袍片；后续镜像装配右前袍。使用内置 image_gen，不调用 Tripo，不制作三维网格。

## 采用文件与方向

- `front.png`：面向角色前方的黑金外表面，窄象牙白衬内缘在屏幕左。
- `back.png`：绕竖轴旋转 180°，完整浅象牙色背面，内缘转到屏幕右。
- `left.png`：从角色本人左侧看附件，黑色外表面朝屏幕左、象牙内表面朝屏幕右；铜钉向屏幕左微凸。
- `right.png`：从角色本人右侧看附件，黑色外表面朝屏幕右、象牙内表面朝屏幕左；铜钉向屏幕右微凸。独立生成，未镜像伪造侧图。

设计参考：`D:/天章游戏开发/assets/generated-character-art/fuyuan-jointed-doll-concept-20261103/jointed-doll-assembly-concept-v2-unified-color.png`。仅作为衣片设计／材质参考，不把人物和整页部件陈列当作一个三维生成输入。

## 实际 QA

四张均为 **1254×1254 RGBA**，alpha 范围 **0–255**。下表采用 alpha≥128 的实际包围框，右／下边界不含在框内。

| 图 | 包围框 [左,上,右,下] | 宽×高像素 |
|---|---|---|
| front | [351,33,903,1221] | 552×1188 |
| back | [347,32,905,1223] | 558×1191 |
| left | [604,29,643,1227] | 39×1198 |
| right | [608,29,649,1227] | 41×1198 |

- 四图顶部最大差 4 像素、底部最大差 6 像素；侧图比正面高度多 10 像素（约 0.84%）。无主体裁断，但实际上下余白约 2.2%–2.6%，没有达到初始提示词 8%。
- 正面在 y=130／1170 的宽度分别为 305／551 像素，背面分别为 306／558 像素；上口／下口比例约 0.55，接近目标 0.16／0.30≈0.53。
- **物理尺寸仍是设计目标，未由图像验证。** 若按正面总高 0.78 米等比解释，图中上口约 0.20 米、下口约 0.36 米，仍宽于目标 0.16／0.30 米。三维返件应按目标宽度适配，不得宣称此位图已精确满足尺寸。侧面投影约 2.5–2.7 厘米；8 毫米实体厚度与 3 厘米横向弧度尚须三维实测。
- 只画一个完整衣片，未混入人体、手脚、裤腿；上端实心挂耳及铜钉不是贯穿孔。正背白边位置随旋转正确改变，背面为连续浅象牙面，侧面保持薄片轮廓。
- 侧面边缘存在少量半透明细碎色边；未进行程序抠图或改写 alpha。金裂纹下部仍比“更疏”目标密，保留为外观限制。
- 首次正面上口过窄，执行一次针对性的上口比例修正后采用；之后背、左、右各一次独立生成，无其它修正。没有用镜像生成四视图。

## 输出关联

| 调用 | 实际输出文件 | 用途 |
|---|---|---|
| front initial | C:/Users/WINDOWS/.codex/generated_images/01a1a1d4-7770-7d50-8f20-0ce78b1d8274/exec-933883c8-ceab-43e7-8f4d-ee60e0799ac6.png | 上口过窄，未采用 |
| front correction | C:/Users/WINDOWS/.codex/generated_images/01a1a1d4-7770-7d50-8f20-0ce78b1d8274/exec-a53a731f-4b64-48b1-bc50-8dfa3534b5d6.png | 复制为 front.png |
| back | C:/Users/WINDOWS/.codex/generated_images/01a1a1d4-7770-7d50-8f20-0ce78b1d8274/exec-8204245f-7134-4027-a546-ba95dafe08da.png | 复制为 back.png |
| left | C:/Users/WINDOWS/.codex/generated_images/01a1a1d4-7770-7d50-8f20-0ce78b1d8274/exec-2c5583ff-67e3-4639-97fd-9206d8b88a8c.png | 复制为 left.png |
| right | C:/Users/WINDOWS/.codex/generated_images/01a1a1d4-7770-7d50-8f20-0ce78b1d8274/exec-ce50b8d9-ef86-4ba2-9659-8d3606879827.png | 复制为 right.png |

## 实际发送提示词

### front initial

输入：统一配色结构示意图（设计参考）。所有调用 `transparent_background=true`。

```text
Use case: stylized-concept.
Asset type: one standalone modeling reference for a detachable LEFT-FRONT waist robe panel from the pictured Fu Yuan articulated doll.
Input image 1 is a DESIGN/STYLE reference only: use the long black-gold hanging waist panel with a narrow ivory inner edge, not the person, layout, labels or other accessories.

Create exactly ONE upright complete garment panel in strict orthographic FRONT view, looking squarely at its OUTER painted face. This is the character's own LEFT FRONT waist panel; in this front image its medial ivory lining edge is on SCREEN LEFT.
Shape: a single long slightly tapered trapezoid, approximately 0.78 m total height including a short top tab, main panel top width 0.16 m, lower hem width 0.30 m. A thin rigid paper-and-wood clothing panel, about 8 mm true material thickness, a shallow transverse bow of only 3 cm depth across its width, no lengthwise curling. Keep straight calm sides and a slightly angled faceted lower hem as in the reference. Deliberate broad flat folded planes, not fabric ruffles, not armor.
At the top center: one short attached solid charcoal hanging tab, integrated into the panel, with one small solid round aged-brass rivet. The rivet is NOT a hole. No cut-through openings, loops, separate floating hardware or strings.
The outer FRONT is matte deep ink-charcoal with restrained thin antique-gold branching crack accents, sparse enough to retain broad dark areas. A narrow ivory turned lining strip runs down the SCREEN LEFT inner edge only. The reverse surface is continuous pale ivory lining; only the narrow turned edge is visible in this front view. Keep the crisp outlined hand-painted miniature/paper puppet finish of the design reference, neutral local colors and soft even illumination.
Square canvas. Center the single panel vertically, complete tab and hem visible with about 8% clear margin at top and bottom, no perspective or three-quarter turn. This front defines the exact silhouette, height, baseline and design for later back and side views.
Genuinely transparent background. No ground, cast shadow, pedestal, stand, human body, arms, hands, pants, legs, feet, duplicate pieces, labels, text, numbers, dimensions, arrows, border, watermark or checkerboard.
```

### front correction

输入：首次 front 输出（编辑目标）。所有调用 `transparent_background=true`。

```text
Use case: precise-object-edit.
Edit this SINGLE front-view hanging robe panel only. Preserve its exact ink-charcoal painted surface, restrained antique gold cracks, SCREEN LEFT ivory lining strip, solid top tab with one brass rivet, lower hem, front-facing orthographic view, transparency and single-piece construction.
Make exactly one shape correction: widen the TOP EDGE OF THE MAIN TRAPEZOID PANEL (where it meets the tab) to about 53 percent of the bottom hem width. The current top is much too narrow. Target proportions: overall height including the tab 0.78 m, main panel top width 0.16 m, main panel bottom width 0.30 m. Keep the side edges gently tapering outward; retain thin 8 mm material with only shallow transverse curvature. Center the short top tab over the newly widened upper edge. Do not turn this into a narrow long triangular blade.
Keep the same image canvas, vertical height and base level, no added pieces or decoration. Only one exact front view. No text, labels, dimensions, person, background, floor, cast shadow, stand or checkerboard. Genuinely transparent background.
```

### back

输入：采用的 front.png（同件正面）。所有调用 `transparent_background=true`。

```text
Use case: identity-preserve.
Create exactly ONE strict orthographic BACK view of the SAME single detachable left-front waist robe panel shown in input image 1. Image 1 is the accepted FRONT and locks the silhouette, tab, proportions, vertical height, lower hem, subtle 3D curvature and material thickness. Rotate this exact object 180 degrees around its upright vertical axis, do not redesign it.
The reverse is one continuous pale ivory lining face with restrained warm-gray paper/fabric variation and a very subtle traditional woven floral trace. The front's ink-charcoal gold-cracked surface is on the far side and is NOT painted onto the back. The narrow ivory turned inner edge that is on SCREEN LEFT in the front now lies on SCREEN RIGHT in this rear view. Preserve the exact long trapezoid, main upper edge about 53 percent of the bottom width, short solid centered top tab and small solid aged-brass rivet back; the back of that rivet can be a flat closed brass disk, never a cut-through hole.
Keep a thin approximately 8 mm rigid clothing panel and only shallow transverse curvature, not a hollow sleeve or a thick block. Preserve the front image's exact canvas, scale, top-tab height, hem baseline and centerline. Strict rear orthographic view, no perspective or three-quarter rotation. One complete connected piece.
Genuinely transparent background. Soft even neutral illumination and the same hand-painted paper-puppet rendering. No body, hands, legs, pants, shoes, extra pieces, floating hardware, holes, text, labels, dimensions, arrows, checkerboard, stand, floor, cast shadow or scenery.
```

### left

输入：采用的 front.png、back.png（同件正背面）。所有调用 `transparent_background=true`。

```text
Use case: identity-preserve.
Create exactly ONE strict 90-degree orthographic LEFT-SIDE view of the SAME single hanging waist robe panel. Input 1 is its accepted front and locks exact height, tab, hem, gold-black exterior and ivory medial edge. Input 2 is its accepted back and locks the continuous ivory reverse. This object is the character's own LEFT FRONT waist panel; this view looks from the character's own LEFT toward its right, at the outer lateral edge of the panel. The outward black FRONT surface points SCREEN LEFT, and the inward ivory BACK surface points SCREEN RIGHT.

This is a TRUE EDGE-ON PROFILE of a THIN panel. Do NOT show a broad front or back face, do NOT use a three-quarter view, do NOT enlarge the horizontal scale. At the same vertical scale as the inputs, its narrow projected depth is only about 3.8 cm versus 78 cm height: approximately one-twentieth as wide as it is tall. Actual material thickness is about 8 mm; the remaining depth comes from its shallow transverse 3 cm curvature. The panel is almost straight along its upright length; no large hook, curl, blade, wedge, cylinder or complete sleeve. Its silhouette is a narrow mostly vertical side profile, with a small shallow thickness change at the hem and the solid top tab. The top tab and closed brass rivet are attached, not floating; the rivet is only a small edge bump in this profile, not a front-facing round disk. No through-hole.
Preserve the exact total height, tab-to-panel junction height and bottom baseline of the accepted front/back on the SAME square canvas. Keep the slim profile centered in the canvas. Neutral soft illumination, thin dark painted exterior edge and pale ivory reverse edge, hand-painted crisp paper-puppet finish.
Genuinely transparent background. No labels, numbers, arrows, dimensions, text, body, pants, hands, legs, shoes, extra parts, stand, background, floor, shadow or checkerboard. One single complete edge-on profile only.
```

### right

输入：采用的 front.png、back.png、left.png（同件正背与另一侧）。所有调用 `transparent_background=true`。

```text
Use case: identity-preserve.
Create exactly ONE strict 90-degree orthographic RIGHT-SIDE view of the SAME single detachable waist robe panel. Input 1 accepted FRONT locks shape, tab and painted exterior. Input 2 accepted BACK locks the continuous ivory lining. Input 3 accepted LEFT profile locks the exact narrow depth, height and scale. Render the actual opposite side of this same object, not a new part and not a broad three-quarter view.
This object is the character's own LEFT FRONT waist panel. Here the camera looks from the character's own RIGHT toward its left, so the OUTWARD BLACK FRONT surface points SCREEN RIGHT and the INWARD IVORY BACK surface points SCREEN LEFT. The medial side with the turned ivory lining edge is now the near edge; show this ivory edge correctly. The front protruding brass rivet at the tab makes only a small bump toward SCREEN RIGHT, while the flat reverse rivet side lies to SCREEN LEFT. No hole.
Strict edge-on profile, as slender as input 3. Keep actual material around 8 mm thick with only the same shallow transverse curvature; no enlarged horizontal scale, thick block, hook, folded-open second panel, cylinder or cloth ruffle. Same complete one-piece solid tab, long taper and lower hem. The profile can show the thin ivory medial edge with the adjacent thin charcoal edge, never a wide front or rear face. The full black-gold exterior exists on the far surface and must not become a second attached strip.
Use the SAME square canvas, top height, tab-to-panel height, baseline, central position and neutral hand-painted paper-puppet finish as the three accepted views. Genuinely transparent background. No person, hands, arms, pants, legs, shoes, extra pieces, floating hardware, text, labels, dimensions, arrows, checkerboard, stand, ground or cast shadow. One complete isolated thin RIGHT profile only.
```
