# 默认男主标准接口模块化 UI 立绘 v1 · 生产与注册提示合同

状态：2026-09-04，A 门标准头部的原生 RGBA 和 B 门四个恢复 RGBA 已注册到共同的 `1024×1536` 全身画布。绿幕源图始终只作证据；原生 RGBA 只作不可变来源审计；后续生成注册与正式组合都读取 `layers/base-head/head_young_refined_01.png`。A 门预算已消费完毕，B 门四次调用已消费完毕；本注册包仅待 B 门书面签收，C1、C2 仍需各自授权。

## 固定参考输入

- 输入一：`assets/generated-character-art/dialogue-transparent/formal-player-default-male-v1.png`，仅作年轻脸身份、年龄气质和项目角色画风参考；不得反向抠取或逐像素继承。
- 输入二：无。标准头部必须直接生成，不使用旧四母图、旧遮罩或组合图作输入。
- 请求：`model=gpt-image-2`、`size=1024x1536`、纯 `#00ff00` 背景、PNG。
- 模型源输出：`evidence/gates/a-standard-head/attempt-03-green-source.png`。按 input contract 冻结参数确定性去绿后写入 `sources/native/base-head/head_young_refined_01.png`；该原生近景只保留为来源。现行注册以 `registration.json` 的固定等比缩放与平移从该来源一次重建 `layers/base-head/head_young_refined_01.png`。

## A 门唯一英文提示

```text
Create exactly one isolated full bald base-head component for the unnamed 16-year-old default male player character of the 2D Chinese xianxia game 《天章》. Use the supplied image only as a reference for his young refined identity, calm temperament, warm light skin tone, and polished Chinese xianxia anime illustration quality. Do not copy or extract pixels from it.

On a native 1024 by 1536 PNG canvas, place the entire subject fully inside the central x=128..895 safe area. Draw a complete clean bald skull, face, both ears, and a neck extending downward far enough to overlap an outfit collar. Keep a calm three-quarter-forward portrait orientation, centered full-canvas registration, refined narrow oval face, straight brows, clear eyes, and coherent soft directional lighting. The component must be independently usable as the frozen common head interface for later hair and outfit components.

Include no hair, hair cap, beard, moustache, clothing, shoulder, torso, arm, hand, jewelry, headwear, weapon, effect, text, seal, UI, watermark, ground, shadow, extra person, extra limb, photographic skin, realistic 3D rendering, or modern element. Place the subject on one perfectly flat solid #00ff00 chroma-key field with no gradient, texture, lighting variation, floor, cast shadow, contact shadow, reflection, border, vignette, checkerboard, or simulated transparency. Do not use #00ff00 or green spill anywhere in the skull, face, eyes, ears, neck, brows, linework, highlights, or shadows. Keep the silhouette clean and continuous. Do not crop, resize, rotate, translate, recenter, or make a complete character.
```

## 验收与失败处理

验收依次核对：纯绿幕背景均匀、主体不含键色污染、1024×1536 原生尺寸、确定性去绿后真实连续 Alpha、全部有效像素位于中央安全区、完整头骨／耳朵／颈部、无非 `base-head` 像素、身份与画风、以及固定规范化后 887×1774 注册。记录调用参数、实际调用次数、绿幕源图、原生及规范 PNG 解码哈希与接口遮罩。

任何一项失败即停止 A 门，保留输入、提示、输出、参数、哈希与具体失败原因。不得自动重试、增加预算、改动去绿参数、语义抠图、反向抠层、FLUX、KSampler、LoRA、扩散重画、组合专用补丁或第二套头部接口。只有负责人另行批准的新 checkpoint 才能再次调用。

## 当前 B 门注册参考

- 五个既有组件的共同组合画布为 `1024×1536` RGBA；禁止在注册判断前裁掉旧中央安全区。`887×1774` 仅是未来展示导出议题，不决定本包是否可用。
- `layers/base-head/head_young_refined_01.png` 是当前标准头部全身注册参考。`sources/native/base-head/head_young_refined_01.png` 与四个 B 门 `sources/native/` 文件只能用于完整来源哈希审计和一次重建。
- 每个具名组件只可按 `registration.json` 的固定等比缩放、固定平移和预乘 Alpha Lanczos 从原生输入重建一次；组合阶段严格按 `hair-back → base-head → outfit-body → hair-front` 原位 Alpha 合成，不允许平移、缩放、旋转、变形、重新采样、局部遮罩或按头部切换参数。
- 所有新的 B/C 门生成提示必须把已注册的标准头部全身画布作为注册、尺度、耳根、后颈和衣领参考；不得把 A 门原生特写、绿幕源图、旧 `887×1774` 产物或失败组合当作当前注册参考。
- B 门书面验收仍由 `A-CHAR-PORTRAIT-INTERFACE-01C` 完成。本文件和本包不授权新的图片调用，也不代表 B 门已经批准。
