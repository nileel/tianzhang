# 石甲兽原画 v1

- 日期：2026-09-08。
- 角色：石甲兽，稳定 ID `enemy_shijiahou`；文件沿用项目稳定 ID 的 `shijiahou` 拼写。
- 用途：用户明确要求先出角色原画；无字概念原画，视觉方向已获用户确认。未替换既有 3D source 或 Unity 资产，不代表 3D 生产或导入批准。
- 用户验收：2026-09-20，用户针对本概念稿明确回复“这个通过”；本版作为已通过的石甲兽概念原画保留。
- 图片：`shijiahou_concept_v1.png`，1536 × 1024 PNG，横向全身构图。
- 生成方式：内置 `image_gen`，1 次生成，未使用 CLI 或外部 3D 服务，未做后期修改。未向生成工具传入参考图。
- 关联任务：`开发管理/任务归档/A-CHAR-BATTLE-STATIC3D-SHIJIAHOU-ASSET-01.txt`（已完成的静态 3D 资产卡，仅作为既有方向的线索；本轮未修改任务状态）。
- 事实源：`docs/怪物/关陇玄域/石甲兽.txt#身份`、`#战斗识别`；`docs/superpowers/specs/2026-08-27-formal-static-3d-combat-piece-asset-contract.md#二、稳定身份、Profile 与视觉边界`。
- 风格来源：`assets/generated-character-art/README.md`、`assets/generated-character-art/no-title/prompts.md#通用风格关键词`。原画保留水墨、纸底、低饱和、哑光风格；人类服饰提示不适用于妖兽。
- 既有造型核对：生成前查看了 `assets/source/characters/combat-pieces/shijiahou-static-3d-v1/shijiahou_static3d_v1_contact-sheet.png`；只核对低伏四足与层叠石甲方向，没有将原低模网格形状视为本轮原画必须复制的细节。
- 本轮视觉细化：楔形头、板岩式层叠甲片、关节皮褶和分节尾属于原画提案，不新增剧情、物种能力或数值事实。
- 目视检查：单只四足妖兽；头、躯干、尾与四个足端可辨，远侧后足仅部分可见；全身未裁切，石甲和皮肤区分清楚；无文字、印章、武器、骑手、翼、发光裂缝或独立特效。背景为浅纸底并有接地笔刷，不是透明母版。

## 实际生成提示词

```text
Use case: stylized-concept
Asset type: original creature concept art for the Chinese xianxia tactical game 《天章》, first text-free character illustration, not a 3D asset.
Primary request: Create one polished, visually distinctive full-body original concept painting of 石甲兽 (Shi Jia Shou), stable project ID enemy_shijiahou: a low-tier qi-refining wild beast native to the dry loess-and-stone roads outside Guanzhong. Its identity is thick armor, a slow heavy body, close-range physical strength and an instinct to brace defensively.
Subject and anatomy: One low-slung, massive, compact quadrupedal living beast. A broad wedge-shaped animal head tucked protectively under heavy shoulder armor, a blunt powerful muzzle, small deep-set alert eyes beneath rocky brows, a short thick neck, a wide barrel-like trunk, exactly four short powerful weight-bearing legs with broad planted clawed feet, and one clearly visible substantial tapering segmented tail following the body's anatomy. The head, body, all four legs and feet, and tail must be independently readable. Keep a coherent organic animal skeleton and joints under the armor, with thick dark leathery skin visible in the neck folds, limb joints and protected lower belly. Calmly hostile, wary and stubborn, jaws almost closed, alive and feral rather than cute or mechanical.
Stone armor design: Broad overlapping, organically grown weathered stone plates form an integrated protective mantle over shoulders, back and flanks. Large flattened angular plates with layered shale-like edges, blunt broken corners, restrained small fractures and erosion; larger plates protect the shoulder and spine, smaller articulated plates allow the legs and tail to bend. A strong stepped back silhouette and shield-like forward shoulders express defensive weight. Armor must feel fused to a living creature, not loose rocks balanced on a body and not metal plate armor. The face remains a readable animal face, not a rock pile. Unlit crevices and cracks only.
Composition/framing: Landscape 3:2 concept illustration. One single creature dominates the page in a full-body, slightly elevated front three-quarter view, head oriented toward the left of the image and tail extending visibly to the right. The near and far limbs are naturally offset so all four feet are readable, all planted in a braced standing pose. Entire muzzle, claws and tail tip remain within the frame with breathing space around every edge. Clear readable silhouette at thumbnail size. A restrained faint dry-brush ground-contact wash anchors the weight. No scenic backdrop, no secondary views, no duplicate creature.
Style/medium: Refined hand-painted Chinese fantasy creature concept art with confident expressive dry-ink contours, elegant selective sketch lines, nuanced semi-transparent watercolor and gouache washes, tactile matte weathered stone, warm off-white xuan-paper ground, subtle soft paper grain, sophisticated restrained detail and large quiet negative space. Strong art direction and solid anatomy. This is a finished creature design illustration rather than a photorealistic animal or a polygonal model render. Keep the head and shoulder plates especially crisp; allow selected outer edges and contact marks to dissolve into dry brush. No oversized ghost portrait behind the creature.
Color and lighting: Low-saturation charcoal and warm dark gray stone armor, dusty earthen brown and muted ochre weathering, desaturated dark brown skin; softly lit from upper left, broad readable planes with gentle grounded shadows, no glossy highlights, no neon or luminous parts.
Constraints / Avoid: Exactly one original non-humanoid four-legged beast, one head and one tail; no extra limbs, no bipedal posture, no human rider, no saddle, no clothing, no jewelry, no weapons or magical artifacts, no wings, no horns, no giant spikes, no floating stones, no glowing eyes, no glowing cracks, no crystals, no halo or magical effects. Do not invent a spellcasting role or boss-tier divine aura. No chibi proportions, no cute pet expression, no turtle shell dome, no rigid robot joints, no stone golem assembled from separate blocks, no photorealism, no 3D render, no low-poly facets, no modern elements. No readable text of any kind, no calligraphy, no title, no letters, no numbers, no annotation arrows, no diagrams, no seal, no UI, no logo, no watermark.
```
