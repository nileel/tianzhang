# 裸颈隐藏覆盖不足的局部修正

输入：`assembly-corrections-atlas-v3.png`。只采用输出 `neck-extension-atlas-v3.png` 的头层，其他部分沿用首轮输出。内置 ImageGen 工具调用约50.3秒。

```text
Use case: precise-object-edit. Edit this atlas with ONE local change only. Preserve the exact 1536x1024 canvas and all existing pixels/positions of the face, hair, beard, torso and hand as closely as possible. Keep pure green background #00FF00.
Only the BARE NECK of the left HEAD piece needs a longer hidden overlap tab for skeletal assembly: extend the existing skin downwards by about 130 pixels, to y=985, from the existing lower neck edge around y=780-850. The neck extension should be broad enough from x=175 to x=390, smoothly shaded with the same skin tones, and join seamlessly to the existing neck. Its bottom may be a rounded flat edge; it will be hidden behind the separate torso collar. This is intentional hidden skin coverage, not a change to visible head anatomy. Do not elongate or move the face, jaw, beard, ear or hair, do not resize or move the existing head. No clothes, collars, shoulders or accessories on this head piece. Keep the torso's green U/V neck opening empty and keep all other atlas parts unchanged. No labels, no checkerboard, no shadow.
```
