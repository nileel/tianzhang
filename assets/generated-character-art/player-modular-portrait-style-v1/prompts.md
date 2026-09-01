# 默认男主模块化 UI 立绘 v2 · 四母图生产提示合同

状态：四母图设计已由负责人于 2026-09-02 书面批准；本提示合同仍需在正式生产前连同三个编辑遮罩和调用预算另行批准。不得把旧九次独立组件 checkpoint 作为本合同授权。

## 1. 固定输入与输出

- 身份、身体、姿势与基准组合：`assets/generated-character-art/dialogue-transparent/formal-player-default-male-v1.png`。
- 项目画风参考：`assets/generated-character-art/dialogue-transparent/fu-yuan-hanhong-zhenjun-standing-black-gold-v1.png`；只提供画风，不提供身份、年龄、脸、头发、服装、姿势或物件。
- 模型路线：GPT Image 2 的具名遮罩单变量编辑；不使用本地 FLUX 生成正式可见人物像素。
- 画布：`887×1774`，人物中心轴、落脚点、镜头、姿势、双手和光照方向与批准母版完全一致。
- 背景：纯 `#00ff00`，无渐变、纹理、阴影、地面或反射；随后按项目既有确定性流程转为 RGBA。
- 每张编辑源只改变一个维度；正式源图由“批准母版遮罩外像素 + 编辑输出遮罩内像素”本地确定性合成，模型在遮罩外的任何变化全部丢弃。

## 2. 共同编辑提示

以下英文文本逐字追加到三个单变量提示之前：

```text
Use the first input image as the canonical identity, body, pose, framing, hand pose, canvas registration, and lighting source for the unnamed 16-year-old default male player character in the 2D xianxia game 《天章》. Use the second input image only as a reference for the project's polished Chinese xianxia anime illustration quality; do not copy that character's identity, age, face, hair, clothing, pose, accessories, status, or expression.

Perform a masked single-variable edit. Change only the property explicitly named below and only inside the supplied edit mask. Preserve the canonical character's identity, age, seven-head internal proportion, warm light skin tone, calm expression, three-quarter standing pose, body center, foot placement, right-hand sword-finger gesture, lowered left hand, camera, line quality, material rendering, and lighting direction. Keep both hands anatomically complete. Add no weapon, artifact, headwear, jewelry, text, seal, UI, watermark, ground, shadow, effect, extra person, extra limb, extra finger, modern element, photographic skin, or realistic 3D rendering.

The background must be flat pure #00ff00 with no gradient, texture, shadow, floor, reflection, or color variation. Produce one full-body character on the original 887×1774 canvas. Do not crop, resize, rotate, translate, recenter, or change the pose.
```

## 3. 三个单变量编辑

### 3.1 只换脸型

- 输出：`sources/source_f02_h01_o01.png`
- 遮罩：`masks/edit/face_young_defined_01.png`
- 冻结组合：`f02/h01/o01`

追加文本：

```text
Change only the face preset from face_young_refined_01 to face_young_defined_01: keep the same 16-year-old identity and shared skull, but give him a slightly wider jaw and a more defined eyebrow peak. Keep the same eyes, gaze direction, nose, mouth, ears, hairline, hairstyle, neck, skin tone, body, hands, and indigo-and-cream practical long robe. The edit must remain inside the supplied face mask and must join the unchanged hairline, ears, and neck without a double contour or broken edge.
```

### 3.2 只换发型

- 输出：`sources/source_f01_h02_o01.png`
- 遮罩：`masks/edit/hair_high_ponytail_01.png`
- 冻结组合：`f01/h02/o01`

追加文本：

```text
Change only the hairstyle from hair_half_up_long_01 to hair_high_ponytail_01. Keep ink-black hair, the same shared skull envelope, clear forehead, canonical hairline, temples, ears, nape, face, age, body, hands, and indigo-and-cream practical long robe. Create a readable high-tied ponytail with its root and tail fully contained in the supplied union-envelope mask. Preserve clean front-hair and back-hair depth relationships around the face, neck, shoulders, and robe. Add no hair ornament or headwear.
```

### 3.3 只换服装

- 输出：`sources/source_f01_h01_o02.png`
- 遮罩：`masks/edit/outfit_narrow_sleeve_travel_robe_01.png`
- 冻结组合：`f01/h01/o02`

追加文本：

```text
Change only the outfit from outfit_practical_long_robe_01 with palette_indigo_cream_01 to outfit_narrow_sleeve_travel_robe_01 with palette_gray_brown_01. Create a restrained gray-brown narrow-sleeve travel robe for a young Guanzhong cultivator, with a clear crossed collar, fitted cuffs, practical layered hem, dark soft boots, minimal low-contrast stitching, and no emblem or ornament. Keep the canonical face, hair, age, body center, shoulder positions, waist, wrists, both bare hands, sword-finger gesture, foot placement, pose, and silhouette registration. Sleeves must meet the unchanged wrists cleanly inside the supplied union-envelope mask.
```

## 4. ComfyUI 分层合同

ComfyUI 不执行上述人物编辑，只在四张完整母图形成后读取冻结输入和遮罩：

1. 从四张来源图提取两个 `base-face`、两套前后发、两套 `outfit` 和一个 `hands-front`；
2. `base-face` 只含脸、头、耳、颈和联合包络下安全重叠，不生成被服装遮住的完整身体；
3. `outfit` 拥有完整着装身体轮廓和靴子，不含裸露手、脸、头颈或头发；
4. 保留来源可见像素，不用本地 FLUX 或其他扩散节点重画；
5. 先重组四张来源，再生成八种交叉组合；来源重组未通过时不得继续；
6. 透明洞或缺失遮挡区只形成精确失败遮罩，未经负责人另行批准不得调用 GPT Image 2 补全。

## 5. 停止条件

- 单变量编辑无法保持遮罩外的批准身份、姿势或结构；
- 需要自动重试、随机替代或纯文本重新生成人物；
- 需要本地 FLUX 重画正式可见区域；
- 需要按组合偏移、缩放、旋转或补画；
- 需要组合专用层、覆盖图或第二身份母版；
- 隐藏区域补全会改变已经批准的可见像素。

出现任一条件即停止并保留来源图、遮罩、参数与失败组合，不继续叠加补丁。
