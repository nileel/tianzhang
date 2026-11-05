# 苻渊自然下垂袖筒四视图

本轮以内置 imagegen 直接从已确认三栏概念图中提取自然垂臂袖筒，先锁定正面，再分别生成背面与左右侧。输入图为[上一版装配概念图](../../fuyuan-jointed-doll-cuff-20261105/concept/jointed-doll-assembly-concept-v3.png)。原件为一个近肘小口朝上、袖口斜切向下、长白衬连续到低尖角的立体袖筒。

## 采用文件及方向

四张均为 1254 × 1254 RGBA。下表包围盒以 alpha ≥128 计算，右和下边界不含。相机有少量俯角以显示上口。

| 文件 | 观察方向 | 包围盒 | 高宽比 |
| --- | --- | --- | --- |
| front.png | 相机在 -Y，黑色前外面、前铜扣、内衬长带可见 | (237,24)–(1017,1231) | 1.547 |
| back.png | 相机在 +Y，完整黑色后外面，铜扣隐藏 | (251,18)–(995,1227) | 1.625 |
| left.png | 相机在 +X，腕口向画面左，铜扣在左轮廓 | (357,15)–(823,1234) | 2.616 |
| right.png | 相机在 -X，腕口向画面右，铜扣在右轮廓 | (415,25)–(879,1231) | 2.599 |

资产坐标按人物自然下垂臂定义。Z 竖直向上，小口朝 +Z，前侧大口朝 -Y 并向下，后方为 +Y。前臂穿过袖筒的路径从上方朝下方。长边与白衬均向 -Z 延伸。此方向用于直接匹配自然低臂姿态，绑定静止骨架的变换应以实际骨局部坐标计算。

正面短唇在全高约 0.50 处，两侧约 0.43 处。长角到达全高最低点。四张实际袖高差约 1%。正面提示词要求总高约 2.1 L、最大宽约 1.2 L，其中 L 是参考肘腕距离。生成结果横向比提示词更宽，整体横向缩至约 0.88 可到高宽比 1.75。这个比例是量测后的装配建议，图像本身未额外缩放。

front-candidate-narrow.png 为初次未采用候选，高宽比 2.72。随后以 imagegen 单次定向加宽，得到采用的 front.png。该候选只用于来源追溯。

## 核验范围

四向均已人工查看，铜扣方向、长角、白衬连续性、上下口朝向和原始透明背景已核对。顶口内可见的米白区域表示远侧内壁，图像不能证明成品网格实际贯通。Tripo 结果需从上口到大口作实际孔道与手掌通行检查，并将其自然垂臂截图与目标图并排比较。

## 实际提示词

### front_initial

```text
Use case: identity-preserve. Extract and refine ONLY ONE detached 3-dimensional bell sleeve from the approved Fu Yuan construction sheet. The reference's CENTER column, two sleeves under 袖筒×2, and the sleeves worn by the RIGHT assembled figure define the exact intended shape. We need a single standalone FRONT 3D modeling reference, looking at the garment's front exterior and its down/front-facing open hem. Preserve its lean asymmetrical long angular silhouette, matte ink-black cloth with sparse worn muted antique-gold branching crackle, fine dark piping, warm ivory woven jacquard lining, one small aged-brass circular snap sewn flush into upper cloth below the elbow rim. Use the narrow long elegant sleeve silhouette of this artwork. Do not reuse any sleeve from another trial.

POSE AND VOLUME: the invisible forearm is ALMOST VERTICAL, elbow above, wrist below. The sleeve hangs naturally in the same low-arm pose as the assembled character. Top small opening is an upright slightly tilted oval aimed UPWARD toward the elbow, with visible ivory inner rim. Sleeve axis runs from this top hole DOWNWARD toward the short wrist lip, with no horizontal bend. The top fitted collar is only a short segment. From the collar, fabric forms a slender flaring bell around the forearm. The sleeve's LOWER oblique opening runs from a short front lip at wrist height down to a much lower narrow pointed back-side corner; ivory lining is a long continuous strip inside this oblique open hem, reaching all the way to its low tip. This is a real empty wearable hollow tube, the small top hole communicates with the large lower opening. No arm, hand, mannequin or obstruction inside.

PROPORTIONS: if elbow-to-wrist length is L, total sleeve height from top rim to low pointed corner is 2.1 L; overall maximum width is 1.2 L. At 1.0 L below the top rim, the short lip is at wrist height, freeing the hand. Long corner reaches another 1.1 L below that. The fabric hangs as a few broad angular planes, slender and pointed with an elegant long taper, not a rounded ball, balloon, poofy cuff, bag, face mask, round bonnet or short flared bracelet. One continuous fine ivory lining surface, no crumpled nested rolls or extra circular holes. Overall height about 1.75 times overall maximum width; pinch into the fitted upper tube. The long corner may be slightly offset SCREEN-RIGHT, as the reference's assembled screen-right sleeve; show the sloping open hem and ivory as a long narrow inner face down that side. A modest frontal oblique peek is acceptable to reveal openings, preserve the near-VERTICAL forearm axis. No horizontal forearm presentation.

OUTPUT: ONE complete isolated sleeve, square image with generous 10% margins. Front view, neutral even soft light, clean stylized game-asset rendering faithful to the illustrated reference, no ground shadow, truly transparent background. No text, labels, arrows, diagram lines, scene, person, detached hardware, straps or protruding hanging tabs.
```

### front_width_correction

```text
Use case: precise-object-edit. This is a targeted proportion correction to the existing isolated upright bell sleeve. Preserve its silhouette topology, poses, exact hole positions, tilted top opening, short black front lip, one brass snap, long ivory-lined asymmetric lower corner, all colors and texture. Keep the overall object HEIGHT identical. WIDEN the whole sleeve horizontally by 50%, centered on its existing centerline, including the top oval, interior and lower flared hem. This is proportional fitting of the same design. The required full silhouette height/width ratio is approximately 1.75, not the current 2.7. Keep the forearm axis near vertical, top hole aimed up, oblique lower mouth down/front. Do not shorten the long pointed hem, turn it horizontally or make it puffy. Preserve square transparent canvas and margins. Output one complete updated sleeve.
```

### back

```text
Use case: identity-preserve. Image 1 is the ACCEPTED FRONT reference for this exact single detached garment sleeve. Image 2 is the approved construction sheet for material/style only. Create ONE orthographic view of that SAME exact sleeve by turning the camera around its VERTICAL axis. Preserve exact silhouette in three dimensions, scale and absolute total height, long oblique open hem, fold layout, single brass snap on front cloth immediately under upper collar, black silk and antique-gold crackle, continuous smooth ivory lining, top oval rim and low sharp corner. The invisible forearm is near VERTICAL, elbow at top, wrist halfway down, never horizontal. Small round elbow opening aims UP; large sharply oblique wrist opening faces FORWARD AND DOWN; a short front lip ends at wrist height about halfway down the full height, while back/side hem continues in a narrow descending point to the very lowest tip. The ivory lining forms a long uninterrupted tapering inner surface to the low tip. Hollow tube, openings connected, no cap plugging either end, no bunched white folds or extra circular holes. One garment only, no hand, arm or stand. Its rear exterior has the same cloth, with few broad straight elegant folds. Top rim y≈24, bottom tip y≈1231 on square 1254 canvas in the front; use identical height and margins. Transparent background, neutral even illumination, no text, diagram or cast ground shadow. The accepted front's long hem tip is slightly screen-left of center.
VIEW: BACK, camera has rotated 180 degrees around the vertical axis. Show rear BLACK EXTERIOR from collar to near full length. The brass front snap is hidden behind the upper collar. Show fine ivory trim only at turned oblique edges as geometrically visible; do not duplicate front-facing open white sheet. Upper oval opening remains at top, lower long tip now slightly SCREEN-RIGHT of center. Preserve open tubular geometry and volume.
```

### left

```text
Use case: identity-preserve. Image 1 is the ACCEPTED FRONT reference for this exact single detached garment sleeve. Image 2 is the approved construction sheet for material/style only. Create ONE orthographic view of that SAME exact sleeve by turning the camera around its VERTICAL axis. Preserve exact silhouette in three dimensions, scale and absolute total height, long oblique open hem, fold layout, single brass snap on front cloth immediately under upper collar, black silk and antique-gold crackle, continuous smooth ivory lining, top oval rim and low sharp corner. The invisible forearm is near VERTICAL, elbow at top, wrist halfway down, never horizontal. Small round elbow opening aims UP; large sharply oblique wrist opening faces FORWARD AND DOWN; a short front lip ends at wrist height about halfway down the full height, while back/side hem continues in a narrow descending point to the very lowest tip. The ivory lining forms a long uninterrupted tapering inner surface to the low tip. Hollow tube, openings connected, no cap plugging either end, no bunched white folds or extra circular holes. One garment only, no hand, arm or stand. Its rear exterior has the same cloth, with few broad straight elegant folds. Top rim y≈24, bottom tip y≈1231 on square 1254 canvas in the front; use identical height and margins. Transparent background, neutral even illumination, no text, diagram or cast ground shadow. The accepted front's long hem tip is slightly screen-left of center.
VIEW: sleeve's own LEFT SIDE, the side that appeared SCREEN-RIGHT in the front. True 90-degree side view. The short wrist lip and forward-facing large opening point SCREEN-LEFT, rear long descending pointed hem is at SCREEN-RIGHT. Upper forearm collar stays above, almost upright, no bend halfway. Show the slim lateral depth with an oblique lower edge extending downward from short front lip to long rear point. The long ivory lining is visible along the opening's near side as one straight/tapered band, no ball of folds. Front brass snap lies on the left profile edge and should be hidden or thin naturally. The sleeve retains the same total height, same upper-collar diameter and same matching short lip height around the image's middle as image 1.
```

### right

```text
Use case: identity-preserve. Image 1 is the ACCEPTED FRONT reference for this exact single detached garment sleeve. Image 2 is the approved construction sheet for material/style only. Create ONE orthographic view of that SAME exact sleeve by turning the camera around its VERTICAL axis. Preserve exact silhouette in three dimensions, scale and absolute total height, long oblique open hem, fold layout, single brass snap on front cloth immediately under upper collar, black silk and antique-gold crackle, continuous smooth ivory lining, top oval rim and low sharp corner. The invisible forearm is near VERTICAL, elbow at top, wrist halfway down, never horizontal. Small round elbow opening aims UP; large sharply oblique wrist opening faces FORWARD AND DOWN; a short front lip ends at wrist height about halfway down the full height, while back/side hem continues in a narrow descending point to the very lowest tip. The ivory lining forms a long uninterrupted tapering inner surface to the low tip. Hollow tube, openings connected, no cap plugging either end, no bunched white folds or extra circular holes. One garment only, no hand, arm or stand. Its rear exterior has the same cloth, with few broad straight elegant folds. Top rim y≈24, bottom tip y≈1231 on square 1254 canvas in the front; use identical height and margins. Transparent background, neutral even illumination, no text, diagram or cast ground shadow. The accepted front's long hem tip is slightly screen-left of center.
VIEW: sleeve's own RIGHT SIDE, the side that appeared SCREEN-LEFT in the front. True opposite 90-degree side view. The short wrist lip and forward-facing large opening point SCREEN-RIGHT, rear long descending pointed hem is at SCREEN-LEFT. Upper forearm collar stays above, almost upright, no elbow bend. Show the same slim lateral depth with the same oblique edge from middle-height short front lip to the much lower rear point. The long ivory lining follows the open near side as one continuous smooth taper, not a curled pocket. Front brass snap is on the right profile edge and should be thin/foreshortened naturally. Same total height, top collar diameter, same short lip height near middle, do not horizontally mirror the previous view; depict the true opposite face and corresponding fold geometry.
```
