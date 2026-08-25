# 战场角色 2D／静态 3D 表现方向重新评估设计

日期：2026-08-24
状态：本文根据 2026-08-24 手动只读审核修订并由用户明确批准；同日已建立完整任务图和首批 ready 投影，尚未开始业务实施

## 一、重新评估原因

- 归档任务 `A-CHAR-BATTLE-VISUAL-COMPARE-01` 以 **2D:静态 3D = 42:30** 得出 2D 胜出，但该分数不能继续作为方向决策依据。
- 旧比较要求两条路线保持同一角色身份、服饰和主色，并规定证据条件不对称时停止；实际静态 3D Unity 材质 `src/Assets/Art/Characters/StaticChess/FuYuan/FuYuan_StaticChess.mat` 的 `_BaseMap`、`_MainTex` 均为空，而来源包存在 `traditional_robes_3d_model_basecolor.JPEG`。
- `VisualBaselineBuilder.GetOrCreateStaticChessMaterial` 只在导入材质已经暴露贴图时设置 `_BaseMap`，空贴图不会触发失败；既有 EditMode 测试只验证材质使用 URP Lit，没有验证 BaseMap 已绑定。因此旧比较把 Unity 贴图消费缺陷造成的灰白模型，与完整着色的 2D 样例进行了比较。
- 旧评分还以静态样例推断长期生产成本，没有先分别冻结 2D 战斗动画生产流程和静态 3D 战斗动态表现流程，无法可靠覆盖移动、攻击、受击、施法、死亡等后续成本。
- 旧文档和归档记录保留为历史证据，不删除、不改写为“当时已公平比较”；只有新的实机比较和用户决定完成后，才能产生替代方向结论。

## 二、决策范围与优先级

### 2.1 范围

- 本轮只决定战斗中的角色表现方向。
- 剧情演出继续使用立绘，不计入两条战斗路线的生产量。
- 其他玩法未来可能复用战斗角色表现，但只作为低权重扩展信息，不得压过当前战斗效果与战斗生产成本。
- 固定样例角色继续使用苻渊；两条路线必须保持相同身份、服饰、主色、占格、世界尺度、相机、光照、地块、方向语义和战斗事件时点。

### 2.2 决策优先级

1. **战斗内实际表现效果**是第一判定标准，由用户在可运行游戏中直接操作和观察。
2. **后续战斗制作成本**是第二判定标准，必须建立在两条已批准生产流程和真实样例生产记录上。
3. 运行稳定性、维护风险和其他玩法复用作为硬门或辅助信息；不得用等权总分替代前两项的先后关系。

最终不再由自动评分锁定胜者。自动检查只证明输入对称、运行时行为未变和比较入口可运行；路线选择由用户作出。

## 三、初步参考游戏与继续调研边界

本轮只完成了足以建立任务图的初步搜索，以下项目是后续参考调研卡的起点，不是《天章》生产方案：

| 候选 | 可参考内容 | 当前证据边界 |
|------|------------|--------------|
| [Battle Brothers 官方开发日志](https://battlebrothersgame.com/dev-blog-5-concept-art-explaining-battle-brothers-character-art-style/) | 小团队在多方向、装备与完整动画成本之间取舍，最终采用棋子化半身角色 | 可作为 2D 抽象程度与成本取舍的一手资料，不代表适合直接照搬其无步行动画方案 |
| [Card Hunter 官方美术文章](https://www.cardhunter.com/2011/10/the-evolving-art-style-of-card-hunter/) | 主动放弃传统动画并采用立式棋子表现的设计原因 | 可作为低动画棋子风格的决策参考，不是六向战斗动画生产流程 |
| [Into the Breach 官方论坛制作案例](https://www.subsetgames.com/forum/viewtopic.php?t=35529) | Blender 渲染、像素清理、Sprite Sheet 与状态变体的可复核案例 | 目前只确认发布于官方论坛；正式引用生产结论前必须核实作者身份与适用范围 |
| [Wartile 官方页面](https://spotlight.deck13.com/games/wartile/) | 立体战场、微缩棋子和完整动态表现 | 是静态 3D 棋子的视觉邻近参考，但官方页面明确为 fully animated figurines，不是多套静态 Pose 换模证据 |
| [Armello 官方页面](https://armello.com/) | 3D 棋盘、角色镜头尺度和战斗演出语法 | 可参考棋盘角色可读性，不足以证明其资产生产方法适合《天章》 |
| [Moonbreaker 官方产品页](https://unknownworlds.com/en/games) | 数字微缩模型与回合制战术结合 | 可参考微缩模型视觉方向，不是静态 Pose 模型工作流证明 |

初步搜索尚未找到可信且完全等价于“用多套独立静态 3D Pose 模型换模模拟整套战斗动画”的成熟案例。静态 3D 调研任务必须首先验证这种做法是否有可迁移先例；如果没有，应明确标记为项目自研路线及其风险，而不是把 Pose 换模预设成既定方案。

参考调研的来源优先级为：开发者／发行方官方制作资料、可确认作者的演讲或复盘、官方产品资料、可复核的社区生产案例。普通攻略、百科和宣传视频只能作为画面线索，不能独立支持生产成本结论。

## 四、完整任务图

拟议父项为 `A-CHAR-BATTLE-VISUAL-REEVALUATE-02`，只汇总依赖与最终决定，不直接生产资产。

```text
A-CHAR-BATTLE-VISUAL-REEVALUATE-02
│
├─ 现存技术缺陷修正
│  └─ U-CHAR-STATIC3D-MATERIAL-CORRECT-01
│
├─ 2D 调研与方案
│  ├─ D-CHAR-2D-BATTLE-ANIM-REFERENCE-01
│  └─ D-CHAR-2D-BATTLE-ANIM-PIPELINE-01
│
├─ 静态 3D 调研与方案
│  ├─ D-CHAR-STATIC3D-MOTION-REFERENCE-01
│  └─ D-CHAR-STATIC3D-MOTION-PIPELINE-01
│
├─ 方案批准后的样例生产
│  ├─ A-CHAR-2D-BATTLE-ANIM-PILOT-01
│  ├─ U-CHAR-2D-BATTLE-ANIM-INTEGRATION-01
│  ├─ A-CHAR-STATIC3D-MOTION-PILOT-01
│  └─ U-CHAR-STATIC3D-MOTION-INTEGRATION-01
│
├─ 共同证据
│  ├─ U-CHAR-BATTLE-VISUAL-PLAYABLE-COMPARE-02
│  └─ D-CHAR-BATTLE-VISUAL-COST-EVIDENCE-01
│
└─ A-CHAR-BATTLE-VISUAL-USER-DECISION-02
```

首批 ready 叶子固定为两张参考调研卡和一张独立材质修正卡。材质修正处理的是 master 上已经存在且会被现有测试静默放行的缺陷，不依赖最终是否批准静态 3D 动态表现方案；静态 3D pilot 和 integration 依赖它完成。两张详细方案卡分别依赖本路线参考调研完成和用户复核；全部样例生产、Unity 接入、成本证据和可玩比较在对应方案批准前保持非 ready。

样例生产与 Unity 接入节点是已知的后续原子结果，但在详细方案批准前不冻结帧数、Pose 数、文件形状或具体工具链，也不提前建立为可执行任务。详细方案卡负责把本路线后续卡的资产单位、允许路径、验证入口和停止条件冻结到可执行程度。

明确依赖如下：

| 任务 | 直接依赖／解锁条件 |
|------|-------------------|
| `D-CHAR-2D-BATTLE-ANIM-REFERENCE-01` | 无；首批 ready |
| `D-CHAR-STATIC3D-MOTION-REFERENCE-01` | 无；首批 ready |
| `U-CHAR-STATIC3D-MATERIAL-CORRECT-01` | 无；首批 ready；不改变视觉方向 |
| `D-CHAR-2D-BATTLE-ANIM-PIPELINE-01` | 2D reference 完成；用户复核调研结果并明确允许进入详细方案 |
| `D-CHAR-STATIC3D-MOTION-PIPELINE-01` | 静态 3D reference 完成；用户复核调研结果并明确允许进入详细方案 |
| `A-CHAR-2D-BATTLE-ANIM-PILOT-01` | 2D pipeline 书面方案由用户批准 |
| `U-CHAR-2D-BATTLE-ANIM-INTEGRATION-01` | 2D pilot 完成 |
| `A-CHAR-STATIC3D-MOTION-PILOT-01` | 静态 3D pipeline 书面方案由用户批准，且 material correction 完成；涉及付费生成时另有当次 credits 授权 |
| `U-CHAR-STATIC3D-MOTION-INTEGRATION-01` | 静态 3D pilot 完成 |
| `U-CHAR-BATTLE-VISUAL-PLAYABLE-COMPARE-02` | 两条 Unity integration 完成；由 Codex 完成共同条件技术复核 |
| `D-CHAR-BATTLE-VISUAL-COST-EVIDENCE-01` | 两条 pilot 与 integration 的实际制作记录完整 |
| `A-CHAR-BATTLE-VISUAL-USER-DECISION-02` | playable compare 已通过 Codex 技术复核且 cost evidence 完成；等待用户实机判断 |
| `A-CHAR-BATTLE-VISUAL-REEVALUATE-02` | user decision 选择 2D 或静态 3D 后汇总关闭；两者均不接受时不关闭 |

### 4.1 五个人工门的机器状态

用户门不能只写在任务正文中。建卡时按以下状态合同建立，并在卡片 `stateReason`、完成条件和停止条件中同时写明：

1. 两张 pipeline 卡建卡时即为 `dispatchState=frozen`，`blockedBy` 分别包含对应 reference。reference 完成、具名前置被移除后，pipeline 卡继续以 `blockedBy=[]`、`dispatchState=frozen` 保持非 ready，只有用户复核调研结果并明确回复“进入详细方案”才能转 `ready`；维护轮不得把“前置已清空”解释为已获授权。
2. 两张 pipeline 卡各自写完并自审书面方案后，不直接完成，而是在同一 owner run 中建立合法 `automationCheckpoint` 并转为 `waiting_reply`。选项固定为 A“批准方案并解锁 pilot”、B“按用户意见只修订方案后再次等待”、C“冻结本路线且不解锁 pilot”。只有 A 才允许原 run 完成 pipeline；B 不扩大到实施，C 不创建候选资产。
3. `A-CHAR-BATTLE-VISUAL-USER-DECISION-02` 在技术与成本前置完成后运行，向用户提供实机入口和成本证据，再以合法 `automationCheckpoint` 转为 `waiting_reply`。选项固定为 A“2D”、B“静态 3D”、C“两者均不接受”；任务恢复后只记录用户选择，不自行改选。

这里没有把所有人工门机械写成裸 `pending_decision`：现行 `tools/check-task-cards.ps1` 要求 `pending_decision`／`waiting_reply` 必须恰好携带 `automationDecision` 或 `automationCheckpoint`；单纯“是否允许开始”的批准门也不天然存在维护型 A／B 两条可执行路线。因此 pipeline 进入门使用 `frozen`，运行中书面方案与最终实机决定使用 `waiting_reply + automationCheckpoint`。如果某个门届时确实形成两条可确定性准备的 A／B 路线，才允许按维护规则生成 `pending_decision + automationDecision`，不得伪造选项绕过检查器。

以上五个人工门分别是：2D pipeline 进入、静态 3D pipeline 进入、2D 书面方案批准并解锁 pilot、静态 3D 书面方案批准并解锁 pilot、最终实机方向决定。

### 4.2 建卡同轮暂停旧方向效力

建立父项和全部子卡的同一规划提交必须同步修改 `开发管理/任务列表/场景与Unity任务.txt` 的现行事实：

- 第 13 行的“2D 胜出／正式方向已固定”保留为 2026-08-23 历史结论，同时追加“因静态 3D BaseColor 证据不对称及完整战斗生产成本未建立，该结论在重新评估结束前暂停作为新增任务的方向依据”。
- 第 70 行改为“真实战场角色方向正在重新评估”；重评结束前不得以旧 2D 结论新建正式 2D 批次，也不得解冻正式 3D 替换。
- 第 71 行移除 `D-CHAR-APPEARANCE-01` 必须按正式 2D 合同展开的预设，改为依赖本父项最终用户决定。

这一步不修改 `开发管理/任务归档/` 中的历史卡。所有新卡的 `expectedPaths` 都必须包含其 source backlog、`开发管理/当前任务队列.txt`、自身 active／archive 路径；父项以及会改变上述现行效力的管理切片必须把 `开发管理/任务列表/场景与Unity任务.txt` 列入 `expectedPaths`。建卡提交未完成这三处中和时，首批三张叶子不得进入队列。

### 4.3 `-02` 命名说明

`A-CHAR-BATTLE-VISUAL-REEVALUATE-02`、`U-CHAR-BATTLE-VISUAL-PLAYABLE-COMPARE-02` 与 `A-CHAR-BATTLE-VISUAL-USER-DECISION-02` 中的 `-02` 表示对 2026-08-23 视觉方向比较的第二轮，不表示仓库中已经存在同名 `-01` 卡。父卡的来源与当前边界必须显式回指旧 `A-CHAR-BATTLE-VISUAL-COMPARE-01` 和 `A-CHAR-BATTLE-VISUAL-DIRECTION-01`，避免把后缀误当成具名前置。

## 五、调研与详细方案任务合同

### 5.1 2D 参考调研

`D-CHAR-2D-BATTLE-ANIM-REFERENCE-01` 只回答以下问题：

- 战棋／回合制游戏如何用 2D 角色表达方向、移动、攻击、受击、技能和死亡。
- 逐帧、骨骼／网格、3D 预渲染转 Sprite、有限帧配合位移特效等方法分别在什么画面目标下成立。
- 换装、武器、角色批量、动作扩展和修改反馈如何影响长期成本。
- 哪些资料是开发者一手生产证据，哪些只是画面观察或社区案例。
- 哪些经验可迁移到《天章》，哪些因镜头、体量、美术规格或团队构成不同而不可迁移。

本卡不得决定《天章》使用哪种 2D 动画技术，不得生产苻渊动画，不得预设方向数、帧数或动作帧表。

### 5.2 2D 详细方案

`D-CHAR-2D-BATTLE-ANIM-PIPELINE-01` 在调研结果上制定独立、可审核的 2D 战斗动画生产方案，至少冻结：

- 目标画面质量和战斗可读性标准。
- 动作、方向与状态的内容边界及选取理由。
- 单个可版本化资产单位、来源文件、导出物和 Unity 消费形状。
- 从角色事实、制作、修订、导出、导入到运行时验证的完整步骤。
- 换装、武器、批量角色和新增动作如何计量成本，不用未验证的自动化折扣成本。
- 人工时间、外部工具／credits、返工次数、Unity 接入与 QA 的记录方法。
- 苻渊样例卡的确切范围、允许路径、失败标准和用户可见结果。

方案必须单独交给用户批准。批准前不得进入 2D 样例生产。

`2026-08-25` 的书面方案固定为
`docs/superpowers/specs/2026-08-24-2d-battle-animation-production-pipeline-design.md`。它把负责人指定的《龙胤立志传》实战完成度只拆为固定镜头下的可见验收下限；该来源是 E3 产品画面，不承担技术、方向数、帧数、工时或成本结论。方案必须把逐帧、剪纸／骨骼、3D 预渲染和有限帧／棋子化列为待由 pilot 记录选定的制作方法，不能把任一方法或“静态载体＋有限帧”写成既获批准的事实。

### 5.3 静态 3D 参考调研

`D-CHAR-STATIC3D-MOTION-REFERENCE-01` 只回答以下问题：

- 数字棋盘、微缩模型或低动画战棋如何在 3D 中表达移动、攻击、受击、技能和死亡。
- 多套静态 Pose 换模、单模型骨骼动画、刚体／根节点运动、材质与特效驱动、定格动画式关键 Pose 等方法是否存在可信先例。
- 各方法在身份保持、宽袖与武器、方向变化、接地、遮挡、批量生产和 Unity 维护上的代价。
- 相邻参考与完全等价参考必须分开标记；没有找到等价案例时必须明确报告，不得用宣传画面推断生产管线。

本卡不得预设最终一定使用固定 Pose 模型，也不得发起付费生成、制作新模型或修改 Unity。

### 5.4 静态 3D 详细方案

`D-CHAR-STATIC3D-MOTION-PIPELINE-01` 在调研结果上制定独立、可审核的 3D 战斗动态表现方案，至少冻结：

- 采用的动态表现机制及其参考依据；若属于项目自研路线，必须列明验证假设和淘汰条件。
- 模型／Pose／动画／材质／特效的资产单位和角色身份一致性要求。
- 从角色事实、生成或建模、Blender 处理、导出、Unity 导入到运行时表现的完整步骤。
- 方向、宽袖、武器、换装、批量角色和新增动作的成本模型。
- 人工时间、平台 credits、失败生成、返工、Blender／Unity QA 的记录方法。
- 苻渊样例卡的确切范围、允许路径、外部消费授权点、失败标准和用户可见结果。

方案必须单独交给用户批准。批准前不得进入静态 3D 样例生产；任何会消费平台 credits 的操作仍需当次单独授权。

`2026-08-25` 的书面方案固定为
`docs/superpowers/specs/2026-08-24-static-3d-battle-motion-production-pipeline-design.md`。它把炉石传说、游戏王与万智牌式数字载体严格限于 E3 画面语法，冻结苻渊单一静态模型的整体根节点、一次性特效与短促音效分工；不采用多 Pose 换模、模型内部动画、蒙皮或统一骨架。攻击、受击和死亡必须先在项目 pilot 的六向矩阵中证明，P1 既有根节点样例不承担视觉质量、批量生产或成本结论。

## 六、样例生产与技术修正边界

### 6.1 静态 3D 材质修正

`U-CHAR-STATIC3D-MATERIAL-CORRECT-01` 固定为 `route=codex_execute`、`owner=codex`，只修复既有苻渊静态模型在 Unity 中没有消费来源 BaseColor 的根因，并补充能够阻止空 BaseMap 再次通过的测试。不得借此改变模型、镜头、灯光、Pose、战斗规则或制作第三条路线。

根因边界固定为：`FuYuan_StaticChess.fbx.meta` 当前使用内嵌材质且 `externalObjects={}`，Unity 目标目录没有任何贴图资源；现有独立 `FuYuan_StaticChess.mat` 即使被外部材质重映射，也没有可引用的 Unity Texture。因此本卡采用唯一最小路线：

1. 把来源文件
   `assets/source/characters/platform-evaluation/tripo/static-chess-fuyuan/raw/tripo_convert_c00fa18b-823d-45bf-bebb-7f2c6dc5463b.fbm/traditional_robes_3d_model_basecolor.JPEG`
   按字节复制为
   `src/Assets/Art/Characters/StaticChess/FuYuan/FuYuan_StaticChess_BaseColor.JPEG`。
2. 来源文件作为任务卡 `automationInputs` 锁定：字节数 `1,381,322`，SHA-256 `F40FA051F3450103DD261DC6738C4A987113945D90FFB025EDAFBD0A1D109611`。不得换用相近贴图、重新压缩或从 FBX 猜测提取。
3. `VisualBaselineBuilder` 以固定 Unity 资产路径加载该 Texture；缺失、导入失败或加载为空时立即失败，不再从 FBX 导入材质是否偶然暴露 `_BaseMap` 推断。
4. Builder 确定性把该 Texture 设置到 `FuYuan_StaticChess.mat` 的 `_BaseMap`；EditMode 测试同时断言路径存在、材质引用精确等于该 Texture、重建后仍保持引用。

不采用 `ModelImporter.externalObjects` 重映射：当前缺失的是 Texture 资产而不是 `.mat` 资产，重映射不能单独补出 BaseColor，反而会新增一层导入映射所有权。

该卡业务路径固定为：

- `src/Assets/Art/Characters/StaticChess/FuYuan/FuYuan_StaticChess_BaseColor.JPEG`
- `src/Assets/Art/Characters/StaticChess/FuYuan/FuYuan_StaticChess_BaseColor.JPEG.meta`
- `src/Assets/Art/Characters/StaticChess/FuYuan/FuYuan_StaticChess.mat`
- `src/Assets/Scripts/Editor/VisualBaselineBuilder.cs`
- `src/Assets/Tests/EditMode/VisualBaselineEditorTests.cs`
- `开发管理/苻渊静态棋子Unity材质修正验证记录.txt`
- 该卡自身 active／archive、source backlog 与当前队列路径

`FuYuan_StaticChess.fbx`、其 `.meta`、Prefab、场景、来源 JPEG 和其他四张 PBR 贴图均为只读禁止修改路径。详细 3D pipeline 可在后续决定最终样例是否还需要 normal／metallic／roughness，但本缺陷卡不借机扩大材质规格。

完成硬门是：重建后 URP 材质实际引用上述 Unity BaseColor，固定镜头内可辨认苻渊的黑、灰、金服饰主色，确定性 Builder 再次运行不会丢失贴图，空引用会由测试失败关闭。

2026-08-24 已完成 `U-CHAR-STATIC3D-MATERIAL-CORRECT-01`：唯一批准 JPEG 已按字节导入，Builder 只加载其固定 Unity 路径并在缺失或空贴图时失败关闭；URP 材质 `_BaseMap` 的实际引用和重建保持由 EditMode 测试覆盖。来源哈希、导入结果与验证边界见 `开发管理/苻渊静态棋子Unity材质修正验证记录.txt`。

### 6.2 两条样例路线

- `A-CHAR-2D-BATTLE-ANIM-PILOT-01` 只按已批准 2D 方案生产苻渊战斗动画样例资产。
- `U-CHAR-2D-BATTLE-ANIM-INTEGRATION-01` 只把该样例接入隔离比较入口并证明运行时事件，不修改规则结果。
- `A-CHAR-STATIC3D-MOTION-PILOT-01` 只按已批准静态 3D 方案生产苻渊战斗动态表现所需资产。
- `U-CHAR-STATIC3D-MOTION-INTEGRATION-01` 只把该样例接入同一隔离比较入口并证明运行时事件，不修改规则结果。

两条路线不得互相借用只对一边有利的镜头、时序、占格或特效条件；可以采用不同动画技术，但必须表达详细方案最终批准的同一组战斗语义。

## 七、可运行游戏比较合同

`U-CHAR-BATTLE-VISUAL-PLAYABLE-COMPARE-02` 提供一个用户可直接运行的游戏内比较入口，而不是截图包或自动评分表。

该入口必须满足：

- 在同一场景状态下切换 2D 与静态 3D 路线，不通过换镜头、换光照或换地块隐藏差异。
- 用户可以查看详细方案共同冻结的全部方向、动作和状态，并可重复触发、复位和切换路线。
- 两边使用同一苻渊身份、服饰主色、世界尺度、脚底／底座接地、占格、事件时点和战斗结果。
- 静态 3D 必须使用已修正的实际 BaseColor；2D 必须使用为战斗重新生产的资产，不能缩小旧立绘代替。
- 自动测试只验证路线互斥、输入对称、动作触发、复位和规则不变，不输出视觉胜者。
- **Codex 是共同条件的技术复核方**：复核材质实际引用、活动路线对象、相机／光照／尺度参数、方向与动作覆盖、事件触发和复位。若本卡由外部执行者完成，必须转 `codex_review`；若由 Codex 执行，上述项目仍须作为本卡验证硬门记录。
- **用户是唯一的视觉方向决策方**：用户验收交付只有可运行入口、简短操作说明和成本证据，不要求逐动作截图组、录屏或联系表，也不允许复审方用静态证据代替用户实机判断。
- 为使 `codex_review` 可以核对可见结果，技术复审记录可以保留最小运行截图或短录屏，范围只限证明两条路线确实使用同一条件、材质已绑定和动作确实触发；这些材料不是方向评分输入，也不作为给用户选择胜者的替代品。

如果某一边缺动作、方向、正确材质或运行条件不同，比较任务停止，不允许通过评分惩罚缺失的一边后继续得出结论。

## 八、后续制作成本证据合同

`D-CHAR-BATTLE-VISUAL-COST-EVIDENCE-01` 不评判画面胜负，只在两条样例完成后整理同口径成本证据：

- 生产管线建立的一次性成本。
- 苻渊样例各阶段实际人工时间、外部工具或 credits、生成／制作次数、返工次数和失败原因。
- 单个新战斗角色、单个新增动作、方向扩展、武器或服饰变化的增量成本及其假设。
- Blender、2D 工具、导出、Unity 导入、运行时接入、测试和人工 QA 的维护成本。
- 批量角色规模下可复用部分与必须逐角色重做部分。
- 对尚未真实生产的批量成本使用范围和假设，不制造虚假精确数字。

剧情立绘成本排除；其他玩法复用只列为低权重附注。成本证据必须与批准方案和真实制作日志一致，不能继续沿用旧比较中未经完整动画流程验证的 **2D:静态 3D = 4:2** 或类似主观分数。

## 九、用户决定与父项关闭

`A-CHAR-BATTLE-VISUAL-USER-DECISION-02` 的输入只有：

1. 用户实际运行并操作可玩比较入口后的视觉判断；
2. 同口径的后续战斗制作成本证据；
3. 已通过的共同硬门和已知风险。

输出由用户选择“2D”“静态 3D”或“两者均不接受”。系统不得把维度等权相加后替用户选择。

用户选择 2D 或静态 3D 时，决定写入新的任务事实，`A-CHAR-BATTLE-VISUAL-REEVALUATE-02` 才能关闭，并据此调整正式角色生产、外观和 Adventure 接入的后续依赖。旧归档任务继续保留，但新事实必须说明旧 **2D:静态 3D = 42:30** 因材质证据不对称和生产成本模型不完整而不再具有决策效力。

用户选择“两者均不接受”时，`A-CHAR-BATTLE-VISUAL-USER-DECISION-02` 记录该结果后完成，但父项不得关闭，也不得自动建立第三条视觉路线。父项转为合法的 `pending_decision + automationDecision`，固定三项：

- A：只重新规划 2D 方案；目标状态 `ready`。
- B：只重新规划静态 3D 方案；目标状态 `ready`。
- C：维持两者均不接受并让父项 `blocked`。

该后继决定必须引用本轮实机和成本证据；A／B 把父项转为 `ready` 时，只授权一次管理规划切片建立被选路线的新方案子项，随后父项重新以该子项为 blocker，不授权资产或 Unity 实施。没有用户回复时不推进、不重复投递，也不恢复旧 2D 结论。

## 十、停止条件

- 建卡同轮没有同步暂停 `开发管理/任务列表/场景与Unity任务.txt` 中旧 2D 方向的现行效力，却把首批叶子入队。
- 任一人工门只写在散文中，没有用 `blocked`／`frozen`／`waiting_reply + automationCheckpoint` 或合法维护型 `pending_decision + automationDecision` 形成机器不可领取状态。
- 任一详细方案在对应参考调研完成或用户批准前进入实施。
- 参考资料不能区分官方生产证据、画面观察和社区推测，却据此冻结技术路线。
- 静态 3D 没有等价参考时仍把 Pose 换模写成已验证的成熟方案；正确处理是列明自研假设并请求用户决定是否继续验证。
- 任一平台 credits 消费没有取得当次明确授权。
- 两条路线使用不同身份、主色、镜头、灯光、尺度、地块、事件时点或动作集合。
- 静态 3D BaseColor 未修复、材质修正改走未批准的 importer 重映射／相近贴图／重新压缩来源，或任何一边需要用缺失证据的扣分代替重新生产。
- 可玩入口不能由用户直接运行，或最终又退回截图／自动分数决定方向。
- 实施开始跨越已批准方案边界、连续叠加补丁或需要修改战斗规则、方向、占格、装备、AI、结算或存档所有权。

## 十一、本文复核与下一步

- 本文已由用户明确批准；批准范围只包括重新评估的任务拆分、现行事实中和和比较合同，不等于批准 2D 或静态 3D 的具体动画制作方案。
- 2026-08-24 独立规划已在专用 worktree 中一次创建父项、全部已知子项和依赖图，修改现行 backlog 三处方向事实，并只把两张 reference 与独立 material correction 共三张叶子设为 ready；本轮没有顺带调研、生产资产或修改 Unity 业务文件。
- 2026-08-24，`D-CHAR-2D-BATTLE-ANIM-REFERENCE-01` 已完成并产出[2D 战斗动画参考游戏调研](2026-08-24-2d-battle-animation-reference-research.md)。用户复核后指定《龙胤立志传》的实战表现为 2D 方向可接受下限，并明确批准两个方向进入详细方案；`D-CHAR-2D-BATTLE-ANIM-PIPELINE-01` 已转 `ready`，但仍只授权书面方案。
- 2026-08-24，`D-CHAR-STATIC3D-MOTION-REFERENCE-01` 已完成并产出[静态 3D 战斗动态表现参考游戏调研](2026-08-24-static-3d-battle-motion-reference-research.md)。用户复核后把 3D 方向收敛为炉石传说、游戏王、万智牌式的单一静态角色载体整体动效与特效，不采用角色模型内部动画或多套独立 Pose 换模，并明确批准两个方向进入详细方案；`D-CHAR-STATIC3D-MOTION-PIPELINE-01` 已转 `ready`，但仍只授权书面方案。
- 两张 reference 卡的调研和用户授权门均已满足；两张 pipeline 卡按既有固定顺序进入 ready 队列。本次状态变化不授权图片、Sprite Sheet、模型、credits 或 Unity 修改。
- 两张 pipeline 卡写出并自审方案后分别停在 `waiting_reply`；用户批准书面方案后才完成该卡并解锁对应 pilot。
- 2026-08-25，负责人已对 `D-CHAR-2D-BATTLE-ANIM-PIPELINE-01` 选择 A。该卡的书面方案完成归档，`A-CHAR-2D-BATTLE-ANIM-PILOT-01` 依冻结合同转为 `ready`；这仍只授权隔离苻渊样例的生产选择门，不授权正式角色、战斗规则、存档或 Unity 接入。
- 2026-08-25，`A-CHAR-2D-BATTLE-ANIM-PILOT-01` 已完成隔离苻渊 2D 样例：有限帧／棋子化通过高风险三动作的六向选择门，冻结唯一可编辑时间线母源、六状态三帧六向 atlas、真实分钟／费用／次数／返工和 source QA 记录。`U-CHAR-2D-BATTLE-ANIM-INTEGRATION-01` 已将该冻结输入原样接入默认关闭的 `BattleAnimationSpriteProbeGroup`：活动 atlas、六方向六状态、manifest 事件帧、施法单次信号、根节点复位、真实遮挡和规则隔离由 EditMode／PlayMode 验证，未接入正式单位或改变规则。可玩比较与成本证据各只剩静态 3D 动态接入 blocker。
- 2026-08-25，负责人已对 `D-CHAR-STATIC3D-MOTION-PIPELINE-01` 选择 A。该卡的单一静态载体整体动效方案完成归档，`A-CHAR-STATIC3D-MOTION-PILOT-01` 在 material correction 已完成的前提下转为 `ready`；这仍只授权隔离苻渊样例的生产选择门，不授权正式角色、战斗规则、存档或 Unity 接入。
- 2026-08-25，`A-CHAR-STATIC3D-MOTION-PILOT-01` 已完成隔离 source：唯一静态模型／BaseColor 哈希、六方向／六事件／根节点时点、内置 VFX 配方、五条原创本地 PCM cue WAV、成本记录与 source QA 矩阵均已冻结。没有改动 Blender、模型、材质、Prefab、场景、Unity、正式单位或规则；`U-CHAR-STATIC3D-MOTION-INTEGRATION-01` 只解除本卡 blocker，仍独立承担真实运行时矩阵与接入边界。
- 2026-08-25，`U-CHAR-STATIC3D-MOTION-INTEGRATION-01` 已在同一隔离 `VisualBaselineBoard` 接入静态 3D 的根节点动态、唯一内置 VFX 与五条冻结 cue。固定 `1920×1080` 的六方向六事件开始／关键／结束矩阵、manifest 时点、单次效果／cue、cast 单次信号、根节点复位、模型／底座不变和规则隔离均已由 Unity EditMode／PlayMode 覆盖；未接入 `AdventureUnitSpawner`、正式单位、战斗规则或存档。可玩比较与成本证据已各自转为 ready，仍不产生视觉方向结论。
- 最终 user decision 卡必须让用户运行游戏后回复；本文、参考调研、pipeline 方案或 Codex 技术复核均不能代替该决定。
