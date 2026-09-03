# 默认男主标准接口模块化 UI 立绘 v1 · A 门生产提示合同

状态：A 门 checkpoint 已冻结。本文件本身不调用图片模型。A 门只在 `A-CHAR-PORTRAIT-INTERFACE-01B` 持有的负责人书面授权下执行一次 GPT Image 2 调用；不包含 B、C1 或 C2 的任何预算。

## 固定参考输入

- 输入一：`assets/generated-character-art/dialogue-transparent/formal-player-default-male-v1.png`，仅作年轻脸身份、年龄气质和项目角色画风参考；不得反向抠取或逐像素继承。
- 输入二：无。标准头部必须直接生成，不使用旧四母图、旧遮罩或组合图作输入。
- 请求：`model=gpt-image-2`、`size=1024x1536`、`background=transparent`、`output_format=png`。
- 输出：`sources/native/base-head/head_young_refined_01.png`；确认合格后只按 input contract 进行一次固定规范化，写入 `layers/base-head/head_young_refined_01.png`。

## A 门唯一英文提示

```text
Create exactly one isolated full bald base-head component for the unnamed 16-year-old default male player character of the 2D Chinese xianxia game 《天章》. Use the supplied image only as a reference for his young refined identity, calm temperament, warm light skin tone, and polished Chinese xianxia anime illustration quality. Do not copy or extract pixels from it.

On a native 1024 by 1536 transparent PNG canvas, place every non-transparent pixel fully inside the central x=128..895 safe area. Draw a complete clean bald skull, face, both ears, and a neck extending downward far enough to overlap an outfit collar. Keep a calm three-quarter-forward portrait orientation, centered full-canvas registration, refined narrow oval face, straight brows, clear eyes, and coherent soft directional lighting. The component must be independently usable as the frozen common head interface for later hair and outfit components.

Include no hair, hair cap, beard, moustache, clothing, shoulder, torso, arm, hand, jewelry, headwear, weapon, effect, text, seal, UI, watermark, ground, shadow, background, extra person, extra limb, photographic skin, realistic 3D rendering, or modern element. The background must be genuinely transparent with alpha, not white, black, green screen, or simulated transparency. Do not crop, resize, rotate, translate, recenter, or make a complete character.
```

## 验收与失败处理

验收依次核对：真实 Alpha、1024×1536 原生尺寸、全部有效像素位于中央安全区、完整头骨／耳朵／颈部、无非 `base-head` 像素、身份与画风、以及固定规范化后 887×1774 注册。记录调用参数、实际调用次数、原生及规范 PNG 解码 RGBA 哈希与接口遮罩。

任何一项失败即停止 A 门，保留输入、提示、输出、参数、哈希与具体失败原因。不得自动重试、增加预算、改用绿幕、语义抠图、反向抠层、FLUX、KSampler、LoRA、扩散重画、组合专用补丁或第二套头部接口。只有负责人另行批准的新 checkpoint 才能再次调用。
