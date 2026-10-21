# 敌人头像生成记录

使用内置 image_gen，未用 CLI。最终采用手绘修订稿，共两次首稿生成、两次针对用户反馈的风格修订。首稿过于写实，不在交付包中保留；原始工具输出仍在本机 Codex generated_images 中。

事实来源：docs/怪物/关陇玄域/石甲兽.txt、裂石甲兽.txt；现用 v2 模型联系表与独立材质。玩家头像直接复用，图标／面板延续 UI v5/v6 原生 SVG 做法，不由 imagegen 生成。

## 首稿输入

1. assets/generated-character-art/no-title/shijiahou_concept_v1.png（已确认概念身份）。
2. assets/source/characters/combat-pieces/shijiahou-static-3d-v2/shijiahou_static3d_v2_contact-sheet.png（现用模型轮廓）。

### 石甲兽首稿

```text
Use case: stylized-concept. Asset type: text-free square UI creature portrait for the Chinese xianxia tactics game Tianzhang. Reference image is the approved stone-armored beast identity, not a background to preserve. Create a newly framed head-and-shoulders portrait of this SAME low-slung quadrupedal beast, recognizable wedge head, layered overlapping rock plates and leathery jaw, no horns, wings, jewels, crystals, glowing cracks, added anatomy, letters, numbers, frame, scenery, ground or shadow. Refined matte hand-painted Chinese fantasy style, clear broad values, crisp readable silhouette at 64 pixels; upper-left soft lighting, face fills the central 75% with 8% transparent margins. Actual transparent background with native alpha, never checkerboard or green screen. Square 1024x1024. This is SHIJIAHOU, the warm earth-brown melee stone beast. Warm sandstone-taupe armor with ochre weathering and dark brown jaw. Three-quarter facing left, broad brow and closed mouth, head low with heavy upper shoulder plate. Preserve the reference species and restrained natural eyes. Crop upper torso only; no feet or tail. Image 2 shows the current model identity: keep its low wedge-shaped head and overlapping flat stone plates. Do not copy contact-sheet labels or layout.
```

### 裂石甲兽首稿

```text
Use case: stylized-concept. Asset type: text-free square UI creature portrait for the Chinese xianxia tactics game Tianzhang. Reference image is the approved stone-armored beast identity, not a background to preserve. Create a newly framed head-and-shoulders portrait of this SAME low-slung quadrupedal beast, recognizable wedge head, layered overlapping rock plates and leathery jaw, no horns, wings, jewels, crystals, glowing cracks, added anatomy, letters, numbers, frame, scenery, ground or shadow. Refined matte hand-painted Chinese fantasy style, clear broad values, crisp readable silhouette at 64 pixels; upper-left soft lighting, face fills the central 75% with 8% transparent margins. Actual transparent background with native alpha, never checkerboard or green screen. Square 1024x1024. This is LIESHIJIA, the cold slate-blue variant of the same approved mesh/species. Image 2 shows the current model shape. Cool desaturated blue-grey layered armor and dark slate jaw, midtone highlights readable on dark teal UI. Three-quarter facing right, neck slightly raised and jaw slightly open, distinguish from the closed-mouth left-facing warm-brown sibling without inventing different anatomy. No projectile or spell effect. Upper torso and head only, no feet or tail; keep all silhouette in frame with transparent margins. No contact-sheet labels.
```

## 最终手绘修订

用户指出怪物头像偏写实、可能与画风不符。以各自首稿为编辑对象，以现用 FormalPlayer_Default_Portrait.png 为风格权威，保留身份、朝向和配色，减少微纹理并概括明暗。transparent_background=true，未做后期抠色、绘制或风格滤镜。

- 石甲兽首稿输出：exec-53204b33-5d0d-4c3e-b14f-4c054059105b.png；最终输出：exec-27e4bb04-3f86-41b8-ae01-da645f1d86f8.png → portraits/enemy_shijiahou.png。
- 裂石甲兽首稿输出：exec-f5db0d8b-191e-4a5d-87eb-af3352ffdc9f.png；最终输出：exec-d0ab4bec-a8f8-4c7e-a8d3-13ea8f59d5e6.png → portraits/enemy_lieshijia.png。
- 输出原尺寸均 1254×1254，工具未严格采用提示词中的 1024。最终文件原样复制，实际尺寸及 Alpha 以 evidence/validation.json 为准。

### 石甲兽最终提示词

```text
Use case: style-transfer. Image 1 is the edit target monster portrait. Image 2 is our EXISTING APPROVED PLAYER PORTRAIT, the stylistic authority. Restyle Image 1 to fit Image 2's elegant illustrated Chinese fantasy anime game portrait style. The current monster image is MUCH TOO REALISTIC. Remove the granular rock veins, pores, reptile scale microdetail and photographic rendering. Draw smooth broad stone armor planes with restrained ink outlines and just TWO OR THREE flat/cel-shaded tonal steps, simplified leathery jaw with only a few drawn folds, soft low-saturation watercolor shadows, quiet interiors, selective thin brush accents. At 48px it must read immediately as a graphic painted character, not a photograph or realistic 3D render. Match the refined 2D drawn line weight and surface simplification of the player portrait. Do not change species or invent anatomy: keep low wedge-shaped head and layered flat rock plates, no horns/wings/crystals/magic. Keep the existing pose and color identity. Composition: head-and-shoulders square avatar, keep entire HEAD inside with 8% transparent margins, shoulders may crop at bottom, no frame/background/circle/text/ground/shadow. Real native alpha transparency; ignore any green RGB in transparent pixels of reference 2. This should be a distinctly simplified illustrated repaint, not merely a color filter. Warm brown stone beast, three-quarter facing left, closed mouth, warm taupe armor.
```

### 裂石甲兽最终提示词

```text
Use case: style-transfer. Image 1 is the edit target monster portrait. Image 2 is our EXISTING APPROVED PLAYER PORTRAIT, the stylistic authority. Restyle Image 1 to fit Image 2's elegant illustrated Chinese fantasy anime game portrait style. The current monster image is MUCH TOO REALISTIC. Remove the granular rock veins, pores, reptile scale microdetail and photographic rendering. Draw smooth broad stone armor planes with restrained ink outlines and just TWO OR THREE flat/cel-shaded tonal steps, simplified leathery jaw with only a few drawn folds, soft low-saturation watercolor shadows, quiet interiors, selective thin brush accents. At 48px it must read immediately as a graphic painted character, not a photograph or realistic 3D render. Match the refined 2D drawn line weight and surface simplification of the player portrait. Do not change species or invent anatomy: keep low wedge-shaped head and layered flat rock plates, no horns/wings/crystals/magic. Keep the existing pose and color identity. Composition: head-and-shoulders square avatar, keep entire HEAD inside with 8% transparent margins, shoulders may crop at bottom, no frame/background/circle/text/ground/shadow. Real native alpha transparency; ignore any green RGB in transparent pixels of reference 2. This should be a distinctly simplified illustrated repaint, not merely a color filter. Cold slate blue stone beast, three-quarter facing right, slightly open mouth, cool blue-grey armor. Eye natural, no glow.
```
