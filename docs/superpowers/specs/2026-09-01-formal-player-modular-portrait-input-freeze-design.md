# 默认男主模块化 UI 立绘输入冻结设计

## 1. 背景与根因

`A-CHAR-PORTRAIT-STYLE-01` 当前同时写有 Toon 材质、模块化 3D 配对、八组合 UI 立绘与运行时路径冻结，已经不符合当前事实：

- `U-CHAR-HUMANOID-PROTO-01` 及原模块化 3D 路线已冻结，不能再作为本卡输入前提；
- `D-CHAR-APPEARANCE-01` 只建立了整体 `appearanceProfileId=none` 的目录、存档和空表现消费边界，尚未建立脸型、发型、服装组件目录；
- 已批准的默认男主透明全身母版是扁平身份／风格基准，不是可拆分图层；
- 原卡在没有稳定组件 ID、八组合矩阵、统一画布、锚点、图层、字面量路径与审批源时不能原子进入生产。

本设计不恢复旧 3D 路线，也不扩展 Unity 数据结构。先建立一个独立的输入冻结前置，只把后续八组合 UI 立绘生产所需的输入合同定死。

## 2. 决策

建立 P1 Codex 前置卡：

`A-CHAR-PORTRAIT-STYLE-INPUT-FREEZE-01`

该卡的唯一结果是：形成并获得负责人批准的默认男主模块化 UI 立绘输入合同。合同同时存在为本设计的人类可读事实与后续机器可读投影：

`assets/generated-character-art/player-modular-portrait-style-v1/input-contract.json`

为原样张卡增加具名前置、收窄其职责并写入已冻结字面量路径，是该合同的任务管理投影，不构成第二个业务结果。

前置卡不调用 ImageGen、不生成样图、不修改 Unity／CSV／`AppearanceProfileData`／存档／运行时。完成后，`A-CHAR-PORTRAIT-STYLE-01` 只负责生产、确定性组合和评审八张默认男主 UI 立绘样张。本设计不建立 3D Toon 后续任务；只有用户另行选定实际 3D 路线后，才能在该路线自己的任务中处理材质样张。

### 2.1 非目标

- 不覆盖女性体型，不建立完整创角批次。
- 不把内容组件 ID 提前写入当前只有整体 ID 的 Unity 数据结构。
- 不从批准扁平母版反向抠出并冒充正式可换装图层。
- 不生成八张独立扁平成图冒充模块化组合。
- 不增加自动对齐、运行时缩放、随机替代、组合专用覆盖图或逐图修补机制。
- 不修改战场角色方向、装备、规则、存档所有权或已冻结的模块化 3D 任务。

## 3. 批准来源与用途

默认男主身份、年龄、体型、姿势与基准组合读取：

- `assets/generated-character-art/dialogue-transparent/formal-player-default-male-v1.png`
- `assets/generated-character-art/dialogue-transparent/formal-player-default-male-v1.manifest.json`
- `docs/superpowers/specs/2026-08-29-formal-player-default-male-v2-production-chain.md`

项目立绘画风只参考：

- `assets/generated-character-art/README.md`
- `assets/generated-character-art/dialogue-transparent/fu-yuan-hanhong-zhenjun-standing-black-gold-v1.png`
- 现有已批准 NPC 透明立绘及其直接角色事实。

NPC 资产不提供玩家身份、脸型、发型、服装、姿势或可复制图层。默认男主批准母版固定为八组合中的 `f01/h01/o01` 基准；后续新生产的基准 composite 必须保持其 16 岁、体型、姿势、身份、主配色与画风，但不要求逐像素复制扁平母版。

## 4. 稳定组件与组合矩阵

### 4.1 固定公共项

| 字段 | 稳定 ID | 约束 |
|---|---|---|
| `bodyTypeId` | `body_male_young_standard_01` | 默认男主单体型；八组合不变 |
| `skinToneId` | `skin_warm_light_01` | 八组合不变 |
| `hairColorId` | `hair_color_ink_black_01` | 八组合不变 |
| `handPoseId` | `hands_sword_finger_01` | 两只裸露手与胸前剑指；八组合不变 |
| `headwearVisualId` | `none` | 显式空槽 |
| `mainHandVisualId` | `none` | 显式空槽；胸前剑指不是器物 |
| `artifactVisualId` | `none` | 显式空槽 |

### 4.2 可变组件

| 维度 | 01 基准 | 02 保守替代 |
|---|---|---|
| `facePresetId` | `face_young_refined_01`：批准母版的窄椭圆脸、平直眉与沉静眼型 | `face_young_defined_01`：稍宽下颌、较明确眉峰；年龄、头骨、发际线和体型不变 |
| `hairStyleId` | `hair_half_up_long_01`：批准母版半束长发 | `hair_high_ponytail_01`：高束马尾；保持黑发、前额清晰与共同头骨包络 |
| `outfitVisualId` | `outfit_practical_long_robe_01`：批准母版实用长袍 | `outfit_narrow_sleeve_travel_robe_01`：灰褐窄袖行装；不改变身体、姿势或手位 |
| `outfitPaletteId` | `palette_indigo_cream_01`，固定配对 `outfit_practical_long_robe_01` | `palette_gray_brown_01`，固定配对 `outfit_narrow_sleeve_travel_robe_01` |

首批不允许跨配服装与调色板；调色板 ID 不构成第九种组合维度。

### 4.3 八组合 ID

| `portraitSampleId` | 脸型 | 发型 | 服装 |
|---|---|---|---|
| `portrait_style_male_f01_h01_o01` | `face_young_refined_01` | `hair_half_up_long_01` | `outfit_practical_long_robe_01` |
| `portrait_style_male_f01_h01_o02` | `face_young_refined_01` | `hair_half_up_long_01` | `outfit_narrow_sleeve_travel_robe_01` |
| `portrait_style_male_f01_h02_o01` | `face_young_refined_01` | `hair_high_ponytail_01` | `outfit_practical_long_robe_01` |
| `portrait_style_male_f01_h02_o02` | `face_young_refined_01` | `hair_high_ponytail_01` | `outfit_narrow_sleeve_travel_robe_01` |
| `portrait_style_male_f02_h01_o01` | `face_young_defined_01` | `hair_half_up_long_01` | `outfit_practical_long_robe_01` |
| `portrait_style_male_f02_h01_o02` | `face_young_defined_01` | `hair_half_up_long_01` | `outfit_narrow_sleeve_travel_robe_01` |
| `portrait_style_male_f02_h02_o01` | `face_young_defined_01` | `hair_high_ponytail_01` | `outfit_practical_long_robe_01` |
| `portrait_style_male_f02_h02_o02` | `face_young_defined_01` | `hair_high_ponytail_01` | `outfit_narrow_sleeve_travel_robe_01` |

这些是内容稳定 ID。未来数据卡必须显式映射，不能根据 `f01/h01/o01` 编号、数组位置或文件名解析运行时身份。

## 5. 画布、姿势与锚点合同

### 5.1 硬注册

- 所有组件均为 `887×1774` RGBA PNG，坐标原点为左上角。
- `hair-back`、`base-face`、`outfit`、`hands-front`、`hair-front` 合成时全部放置在 `(0,0)`，不得平移、缩放、旋转或自动纠偏。
- 身体中心轴为 `x=443`，落脚基线锚点为 `(443,1695)`。
- 保持批准母版的直立轻微三分之四姿势、胸前右手剑指与左臂下垂。
- 组件层不得裁小；只有最终 composite 可以按第 7 节固定矩形裁切。

完整画布注册是实际拼合所有者；下列语义锚点用于生产约束、接缝核验和未来显式目录映射，不替代 `(0,0)` 注册。

### 5.2 语义锚点

| 锚点 | 坐标 `(x,y)` | 消费者 |
|---|---:|---|
| `crown` | `(421,110)` | `base-face`、前后发层 |
| `hairlineCenter` | `(414,185)` | `base-face`、前后发层 |
| `templeScreenLeft` | `(369,222)` | `base-face`、前后发层 |
| `templeScreenRight` | `(488,226)` | `base-face`、前后发层 |
| `headCenter` | `(428,252)` | `base-face`、前后发层 |
| `eyeScreenLeft` | `(382,229)` | `base-face`、`hair-front` 眼部净空 |
| `eyeScreenRight` | `(423,225)` | `base-face`、`hair-front` 眼部净空 |
| `earRootScreenLeft` | `(361,255)` | `base-face`、前后发层绕耳接缝 |
| `earRootScreenRight` | `(476,252)` | `base-face`、前后发层绕耳接缝 |
| `chinCenter` | `(411,322)` | 两个 `base-face` |
| `napeCenter` | `(441,349)` | `base-face`、前后发层、服装领口 |
| `neckScreenLeft` | `(395,367)` | `base-face`、服装 |
| `neckScreenRight` | `(485,373)` | `base-face`、服装 |
| `shoulderScreenLeft` | `(318,410)` | `base-face`、服装 |
| `shoulderScreenRight` | `(574,423)` | `base-face`、服装 |
| `raisedHandWrist` | `(337,553)` | `base-face`、服装袖口 |
| `loweredHandWrist` | `(545,805)` | `base-face`、服装袖口 |
| `waistCenter` | `(443,655)` | `base-face`、服装 |
| `ground` | `(443,1695)` | 所有 composite |

两个脸型另有各自的下颌目标，允许在共同头骨内产生已批准差异：

| `facePresetId` | `jawScreenLeft` | `jawScreenRight` | 容差 |
|---|---:|---:|---:|
| `face_young_refined_01` | `(372,293)` | `(451,299)` | 2 px |
| `face_young_defined_01` | `(365,293)` | `(458,299)` | 2 px |

头顶、发际线、眼位、耳根、头部中心、下巴和脸型各自的下颌目标最大偏差为 2 px。`napeCenter` 对前后发层使用 2 px 容差，对服装领口使用 3 px 容差；颈口、肩、腰与腕口其余接缝最大偏差为 3 px。

两个 `base-face` 必须在 `faceVariationRect=(340,165,155,190)` 之外逐像素相同；该矩形内只允许脸部、下颌、眉眼和相应头部轮廓变化。`base-face` 拥有固定身体、肤色、头颈与脸，但不拥有任何头发像素或裸露手部像素。

## 6. 固定图层与文件路径

### 6.1 图层顺序

1. `hair-back`
2. `base-face`
3. `outfit`
4. `hands-front`
5. `hair-front`

不得增加组合专用层、修补层、偏移层或 UI 专用身份层。

图层所有权固定如下：

- `hair-back`：头骨、后颈和躯干之后的披发、马尾尾段及后侧发束；不得包含头皮、脸或服装像素。
- `base-face`：固定身体、肤色、头颈和对应脸型；不包含头发、服装或裸露手部。
- `outfit`：服装、护腕和袖子直到两个腕口锚点；不得重画手指、手掌或脸。
- `hands-front`：从两个腕口锚点向外的两只裸露手、手指与胸前剑指，固定为 `hands_sword_finger_01`，覆盖在服装之上。
- `hair-front`：头皮发帽、头顶发团／发冠、发际线、刘海、太阳穴与绕耳侧发、马尾根，以及所有位于脸或服装之前的发丝。额头、眼睛、耳根和脸部非发丝区域必须透明。

因此头顶／冠部与马尾根明确归 `hair-front`，后披发与马尾尾段明确归 `hair-back`；两层不得重复同一非透明发丝像素。

### 6.2 目录

```text
assets/generated-character-art/player-modular-portrait-style-v1/
├─ input-contract.json
├─ prompts.md
├─ manifest.json
├─ layers/
│  ├─ hair-back/
│  ├─ base-face/
│  ├─ outfit/
│  ├─ hands-front/
│  └─ hair-front/
├─ composites/
├─ crops/
│  ├─ bust/
│  └─ avatar/
└─ evidence/contact-sheet.png
```

### 6.3 组件字面量文件

- `layers/hair-back/hair_half_up_long_01.png`
- `layers/hair-back/hair_high_ponytail_01.png`
- `layers/base-face/face_young_refined_01.png`
- `layers/base-face/face_young_defined_01.png`
- `layers/outfit/outfit_practical_long_robe_01__palette_indigo_cream_01.png`
- `layers/outfit/outfit_narrow_sleeve_travel_robe_01__palette_gray_brown_01.png`
- `layers/hands-front/hands_sword_finger_01.png`
- `layers/hair-front/hair_half_up_long_01.png`
- `layers/hair-front/hair_high_ponytail_01.png`

### 6.4 组合与裁切字面量文件

组合文件：

- `composites/portrait_style_male_f01_h01_o01.png`
- `composites/portrait_style_male_f01_h01_o02.png`
- `composites/portrait_style_male_f01_h02_o01.png`
- `composites/portrait_style_male_f01_h02_o02.png`
- `composites/portrait_style_male_f02_h01_o01.png`
- `composites/portrait_style_male_f02_h01_o02.png`
- `composites/portrait_style_male_f02_h02_o01.png`
- `composites/portrait_style_male_f02_h02_o02.png`

半身文件：

- `crops/bust/portrait_style_male_f01_h01_o01.png`
- `crops/bust/portrait_style_male_f01_h01_o02.png`
- `crops/bust/portrait_style_male_f01_h02_o01.png`
- `crops/bust/portrait_style_male_f01_h02_o02.png`
- `crops/bust/portrait_style_male_f02_h01_o01.png`
- `crops/bust/portrait_style_male_f02_h01_o02.png`
- `crops/bust/portrait_style_male_f02_h02_o01.png`
- `crops/bust/portrait_style_male_f02_h02_o02.png`

头像文件：

- `crops/avatar/portrait_style_male_f01_h01_o01.png`
- `crops/avatar/portrait_style_male_f01_h01_o02.png`
- `crops/avatar/portrait_style_male_f01_h02_o01.png`
- `crops/avatar/portrait_style_male_f01_h02_o02.png`
- `crops/avatar/portrait_style_male_f02_h01_o01.png`
- `crops/avatar/portrait_style_male_f02_h01_o02.png`
- `crops/avatar/portrait_style_male_f02_h02_o01.png`
- `crops/avatar/portrait_style_male_f02_h02_o02.png`

任务卡进入 ready 前必须逐条包含上述 24 条完整字面量路径，不能改回通配符或目录占位。

`manifest.json` 显式记录每个组件与组合的稳定 ID、完整相对路径、尺寸、SHA-256、图层顺序、锚点、容差、裁切矩形及负责人批准证据。运行时和后续任务不得根据文件名或数组顺序猜测关系。

### 6.5 `input-contract.json` 字段集合

机器可读投影固定包含且只包含以下顶层字段：

- `schemaVersion`：首版固定为整数 `1`；
- `contractId`：固定为 `formal_player_modular_portrait_style_v1`；
- `approvedSources`：基准母版、基准 manifest、风格参考的路径与 SHA-256；
- `canvas`：尺寸、原点、身体中心轴、落脚基线和姿势 ID；
- `fixedComponents`：第 4.1 节固定 ID，包括 `handPoseId=hands_sword_finger_01`；
- `facePresets`、`hairStyles`、`outfits`：第 4.2 节稳定 ID 与批准描述；
- `samples`：八个对象，每个对象显式写入 `portraitSampleId`、四个可变组件／调色板 ID、组合／半身／头像完整路径；
- `layerOrder`：固定五项数组；
- `layerPaths`：九个组件文件的稳定 ID 与完整路径映射；
- `anchors`：第 5.2 节全部共同锚点、各脸型下颌目标和整数坐标；
- `tolerances`：头发／脸 2 px、`napeCenter` 分消费者容差、服装接缝 3 px；
- `faceVariationRect`：固定为 `340,165,155,190`；
- `compositing`：像素格式、色彩空间、alpha 规则、blend ID、透明像素归一化与解码像素哈希规则；
- `crops`：第 7 节三个固定矩形；
- `requiredOutputs`：对后续生产卡的前向声明，固定为 `prompts.md`、`manifest.json` 和联系表完整路径；这些文件不是前置卡产物；
- `approval`：`status`、`approvedOn`、`source`、`evidenceRef`。

不得添加 Unity asset、Prefab、材质、存档、3D 模块或运行时缓存引用。设计文档与 JSON 任一字段不一致即视为前置未完成。

## 7. 确定性组合与裁切

每个唯一组件只生产一次，共九个组件文件：两个 `base-face`、两种发型各一对前后层、两个服装层和一个固定 `hands-front`。后续任务以固定图层顺序本地合成八种组合；不能让 AI 分别生成八张完整扁平图作为最终组合。

所有生产图层和 composite 使用 sRGB、8-bit straight/unassociated RGBA。透明像素必须规范化为 `RGBA=(0,0,0,0)`；合成使用固定层序的标准 Porter-Duff source-over，不使用预乘 alpha、颜色混合模式、滤镜或重采样。`input-contract.json` 将该合同标识为 `rgba8_straight_source_over_v1`。

确定性验证使用解码像素哈希：对 `uint32be(width) + uint32be(height) + 自上而下逐行 RGBA 字节` 计算 SHA-256。文件 SHA-256 仍单独记录交付文件，但不以 PNG 编码器产生的压缩字节差异代替像素一致性。

裁切只读取对应 composite：

| 用途 | 矩形 `x,y,w,h` |
|---|---|
| 全身 | `0,0,887,1774` |
| 半身 | `128,80,631,968` |
| 头像 | `256,80,375,500` |

相同输入文件重复组合和裁切必须产生相同解码像素哈希。全身、半身和头像不得由不同母版或不同 AI 结果生成。

## 8. 后续生产与审批流

1. 前置卡建立本设计和 `input-contract.json` 的一致投影，记录批准来源、稳定 ID、矩阵、锚点、路径及用户批准证据。
2. 前置卡归档后，由正常 QueueMaintenance 移除 `A-CHAR-PORTRAIT-STYLE-01` 的具名 blocker；不得在前置卡顺带生成图像。
3. 原样张卡先形成完整组件提示合同并取得负责人批准，再生产八个唯一组件；每个批准生成／编辑输入的次数与失败停止条件必须在该卡内冻结。
4. 本地确定性合成八组合并裁切；形成 `evidence/contact-sheet.png`。
5. 联系表同时展示八组合、批准默认男主基准和现有 NPC 立绘风格参考；负责人只批准 UI 立绘风格与模块兼容性，不批准 3D Toon 路线。
6. 风格批准后，后续独立数据／Unity 卡才能把已批准内容 ID 显式映射到正式目录和 `PortraitComposer`；本卡不做该接入。

## 9. 任务事实与修改边界

前置卡指定与原卡相同的 `route=codex_execute`、`owner=codex`、`priority=P1`。新卡元数据的 `schemaVersion` 必须为 `2`，并携带由现有预检入口实时计算的 `riskPreflight`。其预期路径上界为：

- 本设计文档；
- `assets/generated-character-art/player-modular-portrait-style-v1/input-contract.json`；
- `开发管理/任务列表/场景与Unity任务.txt`；
- `开发管理/当前任务队列.txt`；
- `开发管理/任务卡/A-CHAR-PORTRAIT-STYLE-INPUT-FREEZE-01.txt`；
- `开发管理/任务归档/A-CHAR-PORTRAIT-STYLE-INPUT-FREEZE-01.txt`；
- `开发管理/任务卡/A-CHAR-PORTRAIT-STYLE-01.txt`。

前置卡只为原样张卡增加具名前置、收窄标题／职责并冻结未来字面量路径；不修改原卡 ID、route、owner、priority 或 sourceBacklog。原卡不因设计批准被手工直接置为 ready，仍走正常 QueueMaintenance。

若实施证明必须修改 Unity、CSV、存档 schema、运行时组合器、3D 资产或额外任务所有者，立即停止并重新判断原子性。

## 10. 验收与失败关闭

### 10.1 前置卡验收

- 本设计与 `input-contract.json` 的稳定 ID、八组合、坐标、容差、路径和裁切矩形完全一致。
- JSON 只保存合同，不引用不存在的 Unity asset、Prefab、材质或运行时目录结果。
- 基准母版路径、尺寸、SHA-256 与批准证据可由现有 manifest 复核。
- 原样张卡明确排除 3D Toon，并冻结前置 ID 与未来字面量路径。
- 用户对书面合同给出明确批准证据。

### 10.2 后续样张验收

- 所有组件均为 `887×1774` RGBA，透明四角、无背景／地面／投影／文字／UI。
- 图层顺序、硬注册、语义锚点、脸型变化矩形与分消费者容差全部通过。
- 每个可变组件在所有引用它的组合中逐像素相同；固定 `hands-front` 在八个组合中逐像素相同。
- 八个 composite 重复生成字节一致；裁切可从 composite 确定性复现。
- `f01/h01/o01` 保持批准母版的年龄、体型、姿势、身份、主配色与画风。
- 其他七组只发生矩阵允许的脸型、发型与服装变化。
- 脸、发际线、后颈、领口、肩、袖口和手腕无破损、穿帮、遮挡错误或接缝。
- 八组合与 NPC 参考的联系表获得负责人书面批准。

### 10.3 停止条件

出现以下任一情况即停止，不追加兼容逻辑：

- 某组件只能在某一组合中使用；
- 需要组合专用补丁、覆盖图、逐图修补或第二身份母版；
- 需要平移、缩放、换姿势、改变画布或放宽锚点容差才能拼合；
- 必须把八张完整 AI 图当作模块化输出；
- 需要随机替代、相近 ID、默认资源或根据文件名猜关系；
- 需要修改 Unity／存档／装备／规则所有权或恢复已冻结 3D 路线；
- 用户否决输入合同或八组合联系表。

## 11. 完成条件

- `A-CHAR-PORTRAIT-STYLE-INPUT-FREEZE-01` 以批准合同为唯一结果归档；
- 原 `A-CHAR-PORTRAIT-STYLE-01` 只保留默认男主模块化 UI 立绘八组合生产与评审职责；
- 后续任务可以仅凭稳定 ID、批准来源、完整画布、锚点表、图层顺序和字面量路径开始生产，不需要再次猜测输入；
- 不产生图片、Unity、存档、3D 或运行时副作用。

## 12. 实施投影记录

- 2026-09-02：负责人已批准本书面输入合同；`A-CHAR-PORTRAIT-STYLE-INPUT-FREEZE-01` 已将其逐字段投影到 `assets/generated-character-art/player-modular-portrait-style-v1/input-contract.json`。
- 投影固定记录批准母版、其 manifest 与项目画风参考的仓库路径及 SHA-256；其中批准母版为 `887×1774` RGBA，基准组合为 `f01/h01/o01`。
- 本次只冻结输入，不生成图片、不建立 Unity／存档／3D／运行时引用。原样张卡仍保留具名前置，待正式结果进入 master 后由 QueueMaintenance 处理。
