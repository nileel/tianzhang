# 苻渊左袖挂片四视图

2026-11-03。仅生成图像拆件参考；使用内置 `image_gen`，全部以 `transparent_background=true` 调用。没有操作 Tripo、Blender 或 Unity，没有提交。最终右袖将由主任务镜像复用，此处没有制作右袖副本。

## 设计与方向

依据 `../../fuyuan-jointed-doll-concept-20261103/jointed-doll-assembly-concept-v2-unified-color.png` 中栏的独立袖挂片：墨黑哑光、金裂纹、窄象牙边、完整象牙背衬；一个上端实心挂耳及同一枚铜钉。目标名义尺寸为高 0.48 m、宽 0.28 m、厚 8 mm。只含衣片，不含臂手或袖筒。

`front` 为安装后朝角色前方的外面；`back` 为其完整衬里面。`left` 是本人左侧相机，外面朝屏幕左；`right` 是本人右侧相机，外面朝屏幕右。两侧均保持挂耳在上。

## 采用输出与实测

| 文件 | 工具实际输出 | 像素 | alpha≥128 主轮廓宽×高 |
|---|---|---|---|
| front.png | C:/Users/WINDOWS/.codex/generated_images/01a1a1a5-1809-71b3-a15b-56ede9819575/exec-37ef4ea0-2304-4634-a03f-bd873712bdc8.png | 1024×1536 RGBA | 656×1391 |
| back.png | C:/Users/WINDOWS/.codex/generated_images/01a1a1a5-1809-71b3-a15b-56ede9819575/exec-18470e73-9d2a-42e1-96f4-147f9efc1bba.png | 1024×1536 RGBA | 665×1395 |
| left.png | C:/Users/WINDOWS/.codex/generated_images/01a1a1a5-1809-71b3-a15b-56ede9819575/exec-2e31f888-8c30-4329-8d54-866cf2573299.png | 1024×1536 RGBA | 34×1396 |
| right.png | C:/Users/WINDOWS/.codex/generated_images/01a1a1a5-1809-71b3-a15b-56ede9819575/exec-eaf2813d-f518-452b-a501-0f57f2c2fe87.png | 1024×1536 RGBA | 35×1399 |

四张真实包含 alpha；主轮廓顶端 y=69–73，最低端 y=1463–1468，纵向尺度接近。侧面中段实际宽约 24 px，正反色层及铜钉凸出方向相反，是薄片侧缘，没有冒充宽幅正面。完整数值与 SHA-256 见 `image-qa.json`。

限制：本次只测了二维像素，不能宣称已验证真实米制厚度或三维封闭性。正面宽高比约 0.472；若总高按 0.48 m，投影宽约 0.226 m，仍窄于目标 0.28 m。左右侧中段若按同一高度换算，投影厚度约 8.2 mm。折角较弱，主体接近平片；没有深曲率。图片存在少量低 alpha 半透明边缘／杂点，未另做后处理；透明背景不是完全无边缘杂点的二值遮罩。后续生成不能把外观图当作真实拓扑保证。

只用了一次定向修正：首张 `front-v1.png` 有明显双层折回且偏窄，修正为单一连续浅片；其余背、左、右分别生成一次，未扩大候选数。首张工具输出：C:/Users/WINDOWS/.codex/generated_images/01a1a1a5-1809-71b3-a15b-56ede9819575/exec-d1108f2f-721a-4527-9e0d-7fd101fba97d.png。

## 精确发送提示词

### front

初始正面；输入为统一色概念图（设计与材质参考），输出 front-v1.png，未采用。

```text
Use case: stylized-concept.
Create ONE isolated production orthographic FRONT view of a single hanging sleeve panel for the character's OWN LEFT forearm. This is a clothing plate accessory, not a sleeve tube and not a body part.

Image 1 is a DESIGN AND MATERIAL reference only. Use the dark gold-veined hanging sleeve plate in the UPPER MIDDLE group of the reference (the external dark face of the independent sleeve piece). Ignore all people, other pieces, assembly poses, diagram text, arrows and background.

Subject: one connected asymmetrical polygonal hanging cloth-shell plate, approximately 0.48 m total height, 0.28 m maximum width, 0.008 m solid thickness. Preserve the reference's elegant downward-hanging angular outline and broad planar folded surfaces. The plate has only very shallow curvature and one subtle angular fold; it is not a bowl, cone, closed sleeve, cuff, cylinder, arm guard or overlapping stack. Ink-charcoal matte outer face, fine restrained antique-gold branching crack veins, a narrow ivory lining strip visible along one long edge. The full reverse surface is pale ivory lining. Edges are solid and have believable thin thickness.

At the top, ONE short solid hanging tab is integral with the plate. ONE small round bronze rivet cap is embedded in this tab, with solid material behind it; no see-through hole, no ring, no separate floating hardware. The tab stays at the top. No additional rivets elsewhere.
The FRONT face means the broad decorated external surface that faces toward the character's forward direction after assembly. Camera looks squarely at that broad face, true orthographic front, no three-quarter camera. Keep the design slightly faceted like the reference, with readable material but no photorealistic cloth wrinkles.

Framing: portrait 1024 x 1536 canvas; the entire plate from tab to lowest corner is visible, upright, centered; top at about 8 percent and lowest corner at 92 percent canvas height. Neutral even studio illumination, no ground shadow or scene reflections.
Output ONE image with a genuinely transparent alpha background. No labels, letters, arrows, numbers, dimensions, checkerboard, pedestal or other objects. Absolutely no hand, arm, body, sleeve tube, mannequin or person.
```

### front_correction

唯一一次修正；输入 front-v1.png（编辑目标），采用为 front.png。

```text
Use case: precise-object-edit.
Image 1 is the edit target: the single left sleeve hanging plate. Correct only its geometry and clean transparent framing; preserve its ink-charcoal matte material, fine antique-gold crack veins, narrow pale ivory edge trim, top hanging tab and exactly one embedded round bronze rivet cap.

Make this ONE shallow solid plate, not two overlapping flaps. Remove the large diagonal overlapping front flap and the separate-looking dark lower rear flap. Merge the silhouette into a single unbroken angular polygonal sheet with a SINGLE uninterrupted decorated front surface and at most one gentle broad planar bend. No deep fold, curl, return flap, pocket, cavity, sleeve tube or rolled edge. Keep the long ivory border as a narrow flat inset strip along the left edge, not a deep lining wedge. The full back, unseen from this front, is ivory.

Correct proportions: overall height including tab 0.48 m and maximum width 0.28 m, so visible front width-to-height ratio about 0.58. Widen the main plate and reduce excessive height if needed. The material thickness is 8 mm, very thin relative to width, with shallow 2 cm total curvature maximum. Preserve a tasteful asymmetric sleeve-hanger outline: short narrow centered top tab, angular sloping shoulders to the broad sheet, one clipped side corner, and a long diagonal bottom edge. Do not add decorations, objects or hardware.
True orthographic FRONT camera normal to the broad outer face, no perspective or three-quarter turn. Keep upright with tab at top. Keep the whole object between 8% and 92% of the height of a 1024x1536 portrait canvas, centered with generous transparent margins.

Genuinely transparent background. All pixels outside the object must be transparent: no drop shadow, halo, glow, backdrop, floor, vignette or semi-transparent dark cloud. No text or dimensions. Absolutely no hand, arm, person, hollow sleeve or additional clothing piece.
```

### back

输入 front.png（已采用正面与尺度依据），输出 back.png。

```text
Use case: identity-preserve.
Create the strict orthographic BACK view of the EXACT SAME single left-forearm hanging sleeve plate in Image 1. Image 1 is the accepted FRONT and locks its geometry, overall size, canvas, finish, top tab, rivet, angular outline, shallow curvature and thin solid thickness.

Rotate the CAMERA exactly 180 degrees around the upright vertical axis to see the reverse. Do not turn the plate upside-down or tip it. As a result of looking from behind, the long straight outer edge and the lowest pointed corner that appear on the left of the front reference now appear on the RIGHT of this back view. Match the mirrored projection of every silhouette corner. Keep the single top tab centered at the same height, with the opposite flush bronze end of the SAME rivet in the same position; no extra pin, no hole, no ring.

The BACK is one complete pale ivory lining surface, uninterrupted across the entire main plate, with subtle matte textile/paper fiber and restrained warm-grey tonal facets, bounded by a very thin charcoal edge. No black gold-veined large front surface visible through the lining. No pocket, tube, open inner cavity, curl, double layer or overlapping lower flap. Keep the 8 mm solid sheet thickness and only a very shallow broad planar bend. The top hanging tab remains dark charcoal, with a small bronze rivet end, like the same physical tab viewed from the other side.

Exactly one accessory, no arm, hand, mannequin, body, cuff, sleeve cylinder or other object. Same portrait 1024x1536 canvas, same physical scale as Image 1, same top-tab and lowest-corner pixel heights and centerline. Orthographic camera normal to the broad reverse face, not three-quarter. Neutral even light. Genuinely transparent alpha background: outside silhouette is clear, no shadows, glow, backdrop or vignette. No words, arrows, dimensions, labels or watermark.
```

### left

输入 front.png、back.png（同一资产的两面），输出 left.png。

```text
Use case: identity-preserve.
Render the STRICT 90-DEGREE CHARACTER-LEFT SIDE VIEW of the exact isolated hanging sleeve plate shown in Image 1 (accepted front) and Image 2 (accepted back). Both inputs define the SAME one solid thin plate. This is an engineering multiview input, so do not make a more attractive three-quarter view.

Camera is on the character's own LEFT side. The installed plate's decorated FRONT surface points toward SCREEN LEFT; the ivory BACK surface points toward SCREEN RIGHT. Rotate the camera exactly 90 degrees from the accepted front, around the vertical axis only. The plate remains upright, tab at top and lowest tip at bottom. In this view the large 0.28 m broad face is foreshortened almost completely and MUST NOT be visible as a wide panel. The result is a tall, very NARROW edge-on profile, showing true 8 mm plate thickness and the very shallow broad angular bend (total curvature depth at most 20 mm). At the same scale as the accepted front this thin edge profile should occupy only about 30–80 pixels of the 1024-pixel canvas width, NOT hundreds of pixels. Keep its full 0.48 m height. Do not zoom in to enlarge the edge.

Show the charcoal front skin on the SCREEN-LEFT edge and ivory lining on the SCREEN-RIGHT edge, bonded into one solid sheet with a closed thin edge, no slit or hollow opening between them. The top short solid tab is also seen edge-on, with the existing bronze rivet cap in narrow side profile protruding slightly to screen left; it must not show a large round front-facing circle. No extra hook, ring, pin, arm or support. Include the broad subtle angular fold as a slight change in the narrow edge profile, not a folded-back doubled layer.

Use exactly the same 1024x1536 portrait canvas, scale, top-tab height (about y=74), lowest-point height (about y=1460), and vertical centering as the accepted views. Neutral even illumination; sharp readable silhouette. Genuinely transparent alpha background with no shadow, halo, cloud, floor or vignette. No text, view labels, measurement marks, people, limbs, hands, clothing tube, extra panels or exploded components. One upright edge-on object only.
```

### right

输入 front.png、back.png、left.png（同一资产的两面及侧面尺度依据），输出 right.png。

```text
Use case: identity-preserve.
Create the STRICT 90-DEGREE CHARACTER-RIGHT SIDE VIEW of the same single hanging sleeve plate. Image 1 is the accepted FRONT; Image 2 is the accepted BACK; Image 3 is the accepted LEFT SIDE and locks the very thin edge-on appearance, scale and height. Generate the opposite camera view of that same physical object, not a new object and not a duplicate sleeve for the other arm.

Camera is on the character's own RIGHT side. The plate's decorated FRONT face points toward SCREEN RIGHT; the ivory BACK points toward SCREEN LEFT. Rotate the CAMERA around the vertical axis to this opposite side. Keep the plate upright with tab on top. Show only its true narrow edge-on profile: the broad decorated or ivory faces are almost completely foreshortened and must NOT become a wide panel or a three-quarter view. At the same scale, the plate is only about 20–40 pixels thick at most of its height; preserve the approximately 8 mm solid thickness and very shallow faceted bend from the accepted views. Do not zoom in or enlarge this narrow profile to fill the canvas.

The charcoal front skin is on the SCREEN-RIGHT edge; the ivory lining on the SCREEN-LEFT edge, bonded together into one solid closed plate. The existing single bronze rivet cap protrudes slightly SCREEN RIGHT from the top short tab and is seen in narrow side profile, not as a large front-facing round circle. No added hardware or hole. Preserve every real edge depth change and small fold angle from Image 3 viewed from the opposite side.

Same portrait 1024x1536 canvas. Match the top at about y=74 and bottom at about y=1460, center the narrow profile at x=512, and preserve the same physical scale in all views. Neutral even lighting. Genuinely transparent alpha background, no grey halo or shadow cloud outside the object, no floor, gradient, vignette or display stand. No text, labels, dimension marks, arms, hands, bodies, sleeve tubes, extra sheets or exploded parts. One complete thin edge profile only.
```
