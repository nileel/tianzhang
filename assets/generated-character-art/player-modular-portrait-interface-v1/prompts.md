# 默认男主标准接口模块化 UI 立绘 v1 · A 门生产提示合同

状态：2026-09-03，负责人已视觉批准并冻结 A 门标准头部。绿幕源图只作证据；后续生成注册只读取 `sources/native/base-head/head_young_refined_01.png`，正式组合只读取 `layers/base-head/head_young_refined_01.png`。A 门预算已消费完毕，B 门四次调用已获负责人明确授权；C1、C2 仍需各自授权。

## 固定参考输入

- 输入一：`assets/generated-character-art/dialogue-transparent/formal-player-default-male-v1.png`，仅作年轻脸身份、年龄气质和项目角色画风参考；不得反向抠取或逐像素继承。
- 输入二：无。标准头部必须直接生成，不使用旧四母图、旧遮罩或组合图作输入。
- 请求：`model=gpt-image-2`、`size=1024x1536`、纯 `#00ff00` 背景、PNG。
- 模型源输出：`evidence/gates/a-standard-head/attempt-03-green-source.png`。按 input contract 冻结参数确定性去绿后写入 `sources/native/base-head/head_young_refined_01.png`；确认合格后只执行一次固定规范化，写入 `layers/base-head/head_young_refined_01.png`。

## A 门唯一英文提示

```text
Create exactly one isolated full bald base-head component for the unnamed 16-year-old default male player character of the 2D Chinese xianxia game 《天章》. Use the supplied image only as a reference for his young refined identity, calm temperament, warm light skin tone, and polished Chinese xianxia anime illustration quality. Do not copy or extract pixels from it.

On a native 1024 by 1536 PNG canvas, place the entire subject fully inside the central x=128..895 safe area. Draw a complete clean bald skull, face, both ears, and a neck extending downward far enough to overlap an outfit collar. Keep a calm three-quarter-forward portrait orientation, centered full-canvas registration, refined narrow oval face, straight brows, clear eyes, and coherent soft directional lighting. The component must be independently usable as the frozen common head interface for later hair and outfit components.

Include no hair, hair cap, beard, moustache, clothing, shoulder, torso, arm, hand, jewelry, headwear, weapon, effect, text, seal, UI, watermark, ground, shadow, extra person, extra limb, photographic skin, realistic 3D rendering, or modern element. Place the subject on one perfectly flat solid #00ff00 chroma-key field with no gradient, texture, lighting variation, floor, cast shadow, contact shadow, reflection, border, vignette, checkerboard, or simulated transparency. Do not use #00ff00 or green spill anywhere in the skull, face, eyes, ears, neck, brows, linework, highlights, or shadows. Keep the silhouette clean and continuous. Do not crop, resize, rotate, translate, recenter, or make a complete character.
```

## 验收与失败处理

验收依次核对：纯绿幕背景均匀、主体不含键色污染、1024×1536 原生尺寸、确定性去绿后真实连续 Alpha、全部有效像素位于中央安全区、完整头骨／耳朵／颈部、无非 `base-head` 像素、身份与画风、以及固定规范化后 887×1774 注册。记录调用参数、实际调用次数、绿幕源图、原生及规范 PNG 解码哈希与接口遮罩。

任何一项失败即停止 A 门，保留输入、提示、输出、参数、哈希与具体失败原因。不得自动重试、增加预算、改动去绿参数、语义抠图、反向抠层、FLUX、KSampler、LoRA、扩散重画、组合专用补丁或第二套头部接口。只有负责人另行批准的新 checkpoint 才能再次调用。
