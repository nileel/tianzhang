# 关中薄切片后美术生产主线与任务优先级设计

> 日期：2026-08-09
>
> 状态：设计对话已批准，并按 2026-08-09 审核意见及更高优先级的 Unity 模块化架构重建设计修订；任务卡、backlog 与 ready 队列已按本文完成管理迁移，但不代表已经开始架构重建、安装 Blender、安装或迁移 URP、生成模型或导入正式资源。
>
> 上位约束：`docs/superpowers/specs/2026-08-09-unity-modular-architecture-rebuild-design.md`、`docs/superpowers/specs/2026-08-05-character-art-and-modular-appearance-design.md`、`docs/superpowers/specs/2026-08-08-ai-3d-character-asset-platform-evaluation-design.md`、`docs/资源管理/美术资源版本管理规范.md`、`开发管理/状态与建议维护规则.txt`。

## 一、当前事实

1. `U-GZ-FORMAL-E2E-01`、`D-COMBAT-PROD-01` 与 `U-GZ-UI-TEXT-01` 均已完成并归档；关中正式薄切片已有新建角色、世界、据点、悬赏、关中野外、战斗、结算、返回、基础攻击生产绑定和玩家可见文本的直接证据。
2. `2026-08-09-unity-modular-architecture-rebuild-design.md` 已被批准为新的最高顺序 P1 Unity 前置工程；它冻结架构重建期间的 Unity Runtime、场景、CSV、导入器和相关 asset 功能开发，并明确前置于 `U-URP-PREFLIGHT-01`。
3. 架构重建允许删除旧内部 API、旧序列化字段、旧场景／Prefab 绑定和旧 Built-in 视觉，只恢复功能完整、视觉简陋的正式薄切片；美术主线不得继续把旧 `ExplorationController`、旧 BattleUIManager、旧场景壳或临时 Built-in 材质当作未来接入事实。
4. 当前项目仍使用 Built-in Render Pipeline，`src/Packages/manifest.json` 尚未包含 URP。
5. 当前电脑尚未安装 Blender；仓库也没有已冻结的项目统一 Humanoid 评估骨架和与之匹配的待机、移动、攻击三条评估动作。
6. 已批准的 AI 3D 平台评估要求以苻渊四视图对 Meshy 与 Tripo 3D 各进行三个正式生成槽位的 A/B 试产，并把候选统一换绑项目骨架后评估；平台自动骨架不能替代项目骨架。
7. 运行时角色方向已批准为固定正交斜俯视的 2.5D 低模 3D“水墨棋偶”。正式角色资源进入 Unity 前，必须先通过架构重建、URP、URP 技术视觉基础、2.5D 测试台和对应技术样例闸门。
8. 现有金丹位格内容生产和筑基／金丹后续实现仍有价值，但在关中薄切片已闭环后，它们不再高于架构重建与首条正式美术生产链。

## 二、优先级决定

### 2.1 优先级口径

- `P0` 只保留给阻断构建、破坏数据／存档、不可逆资源损坏或安全隔离失效等必须立即处理的问题；计划性美术、内容与剧情生产不使用 P0。
- `P1` 首先承载 Unity 模块化架构重建；随后承载美术工具与平台准备、重构后的 URP／技术视觉关键路径，以及首批真实资源触发的资源版本闭环。
- `P2` 保存未完成的金丹内容、筑基／紫府／丹相、世界主线、册界内容和其他非阻断功能；不取消既有成果，也不在 P1 美术链形成前继续抢占 ready 队首。
- `P3` 用于季度／阈值触发、远期批量扩展和当前版本不需要的内容。

优先级表达业务重要性，命名依赖和固定队列顺序表达执行先后。架构重建和美术关键链中的下游任务可以同为 P1，但 `U-ARCH-REBUILD-01A`～`01H` 在 Unity 路径上拥有更高执行顺序；前置未完成的任务必须保持 blocked，不得用 P2/P1 的反复升降代替依赖关系。

### 2.2 新建或提升为 P1 的任务

最高顺序的 Unity 架构链：

1. 父项 `U-ARCH-REBUILD-01`。
2. `U-ARCH-REBUILD-01A`、`U-ARCH-REBUILD-01B`、`U-ARCH-REBUILD-01C`、`U-ARCH-REBUILD-01D`、`U-ARCH-REBUILD-01E`、`U-ARCH-REBUILD-01F`、`U-ARCH-REBUILD-01G`、`U-ARCH-REBUILD-01H`。

可在路径与责任方独立时并行的美术工具链：

1. `A-CHAR-BLENDER-SETUP-01`。
2. `A-CHAR-HUMANOID-RIG-01`。
3. `A-CHAR-AI-PLATFORM-EVAL-01`。
4. 条件回退 `A-CHAR-BLENDER-TEMPLATE-01`；只有平台路线不成立时建立为 P1。

被架构重建阻塞的 Unity／正式美术链：

1. `U-URP-PREFLIGHT-01`。
2. `U-URP-MIGRATE-01`。
3. `U-URP-VISUAL-BASELINE-01`。
4. `U-CHAR-3D-PROTO-01`。
5. `U-CHAR-HUMANOID-PROTO-01`。
6. `U-SHIJIAHOU-3D-PROTO-01`。
7. `D-CHAR-APPEARANCE-01`。
8. `A-CHAR-PORTRAIT-STYLE-01`。
9. `U-CHAR-3D-FORMAL-01`。
10. `U-CHAR-CREATION-VISUAL-01`。
11. `A-GZ-PRESENT-01`，按第四章冻结后的去重边界执行。

`AVM-03 / TQ-074` 已经是 P1，不属于本轮提升对象；它继续保持 P1，并在首批真实资源产生时触发。

### 2.3 从 P0／P1 调整为 P2 的明确清单

只调整当前未完成的活跃卡或 backlog 投影；已完成归档不改写。管理迁移开始时 `C-HS-YY-JD-01K` 的原执行 run 已达到合法终态并转换为 `codex_review` ready；本轮保留其复审路线与队列行，只把优先级同步调整为 P2，不在 run 中途改卡或取消复审。

| 分组 | 调整为 P2 的稳定 ID |
|---|---|
| 金丹位格批次 | `C-HS-YY-JD-01`、`C-HS-YY-JD-01A`、`C-HS-YY-JD-01B`、`C-HS-YY-JD-01C`、`C-HS-YY-JD-01D`、`C-HS-YY-JD-01E`、`C-HS-YY-JD-01F`、`C-HS-YY-JD-01G`、`C-HS-YY-JD-01H`、`C-HS-YY-JD-01I`、`C-HS-YY-JD-01J`、`C-HS-YY-JD-01K`、`C-HS-YY-JD-01O`、`C-HS-YY-JD-01P`、`C-HS-YY-JD-01Q` |
| 筑基／紫府内容 | `C-FPD-NAME-01`、`C-FPD-GONGFA-01`、`C-FPD-GONGFA-01A`、`C-FPD-GONGFA-01B`、`C-FPD-GONGFA-01C`、`C-FPD-GONGFA-01D`、`C-FPD-GONGFA-01E`、`C-FPD-GONGFA-01F`、`C-FPD-SPELL-01`、`C-FPD-SPELL-01A`、`C-FPD-SPELL-01B`、`C-FPD-SPELL-01C`、`C-FPD-SPELL-01D`、`C-FPD-SHENTONG-01` |
| 筑基／紫府数据与数值 | `D-FPD-MIGRATE-01`、`D-FPD-MIGRATE-01A`、`D-FPD-MIGRATE-01B`、`D-FPD-MIGRATE-01C`、`D-FPD-MIGRATE-01D`、`D-FPD-MIGRATE-01E`、`N-FPD-REGRESSION-01` |
| 金丹旧叙事迁移 | `C-JD-LORE-MIGRATE-01`、`C-JD-LORE-MIGRATE-01A`、`C-JD-LORE-MIGRATE-01B`、`C-JD-LORE-MIGRATE-01C` |
| 世界主线 | `C-STORY-WM-P0`、`C-STORY-WM-L1`、`C-STORY-WM-L1B`、`C-STORY-WM-L1C`、`C-STORY-WM-L1D`、`C-STORY-WM-L1E`、`C-STORY-WM-L1F` |
| 册界条目 | `C-TZ-CHARTER-ENTRIES-01`、`C-TZ-CHARTER-ENTRIES-01A`、`C-TZ-CHARTER-ENTRIES-01A1`、`C-TZ-CHARTER-ENTRIES-01A2`、`C-TZ-CHARTER-ENTRIES-01B`、`C-TZ-CHARTER-ENTRIES-01B1`、`C-TZ-CHARTER-ENTRIES-01B2`、`C-TZ-CHARTER-ENTRIES-01B3`、`C-TZ-CHARTER-ENTRIES-01B4`、`C-TZ-CHARTER-ENTRIES-01B5`、`C-TZ-CHARTER-ENTRIES-01B7`、`C-TZ-CHARTER-ENTRIES-01B8` |

未列入本表的既有 P2／P3 任务保持原优先级。若实施迁移时发现新的未完成 P0／P1 backlog 行，且它不属于安全、构建、数据完整性或本设计 P1 美术链，则停止迁移并补充本清单，不以“其他任务”概括处理。

## 三、架构前置、工具链与 Unity 视觉链

美术工具与平台链不依赖 Unity Runtime 重构；资源责任方在路径独立时可以并行安装 Blender、建立源骨架和执行平台评估，但不得向 Unity 正式路径导入资产。Unity 渲染、技术视觉、测试台和正式美术链全部阻塞于架构重建的对应下游。

### 3.1 美术工具与平台链

#### A-CHAR-BLENDER-SETUP-01 · Blender 工作站环境

- 选择并安装与当前生产流程相容的 Blender 正式版本，记录版本与可执行文件路径。
- 验证 Blender 可启动、可保存源工程、可导入／导出 FBX、可查看材质槽并统计三角面。
- 只安装满足已批准流程所需的最小环境；不预装自动修模、批处理、平台桥接或第三方重拓扑工作流。
- 本任务不生成正式角色、不建立项目骨架、不开始平台正式生成槽位。

#### A-CHAR-HUMANOID-RIG-01 · 项目统一评估骨架与三条动作

- 冻结项目统一 Humanoid 评估骨架的源文件、骨骼名称、绑定姿势、本地 `+Z` 正面、单位、底部中心原点与 Unity Humanoid Avatar 映射要求。
- 提供与该骨架匹配的待机、移动、攻击三条评估动作，并冻结两平台候选共同使用的换绑与权重转移方法。
- 该任务只提供平台公平比较所需的最小骨架基线，不生产男／女完整身体、脸型、发型、服装、武器或正式 Animator。

#### A-CHAR-AI-PLATFORM-EVAL-01 · Meshy／Tripo 3D 一次性选型

- 完整消费已批准的 `2026-08-08-ai-3d-character-asset-platform-evaluation-design.md`，以同一组苻渊四视图、相同目标预算和相同项目骨架／动作执行 A/B。
- Blender 修整计时包含移除或停用平台骨架、换绑项目骨架、权重转移和有限权重修正。
- 结束时只保留一个默认固定人形主平台，或按停止条件得出通用平台路线不成立；不得长期维护双平台流程。
- 评估候选只进入原始工程与评估证据，不因胜出直接进入 Unity 正式资源目录。

平台路线不成立时，`U-CHAR-HUMANOID-PROTO-01` 保持 blocked；不得在评估任务内自动换第三个平台、放宽门槛或改用平台自动骨架。此状态事件建立并提升 `A-CHAR-BLENDER-TEMPLATE-01` 为 P1，按 2026-08-08 设计的上一级方案由 Blender 制作者建立可控人形模板。该模板通过统一骨架、三条动作、面数、修整和导出验收后，替代“胜出平台”成为 `U-CHAR-HUMANOID-PROTO-01` 的合法资源前置；任务卡在同一状态事件中把 blocker 从平台胜出结果改为 `A-CHAR-BLENDER-TEMPLATE-01`，不得同时保留两条生产路线。

### 3.2 Unity 架构、渲染与测试台链

#### U-ARCH-REBUILD-01 · Unity 模块化架构重建父项

- 父项不直接执行；`01A`～`01H` 依次完成冻结与基线、程序集骨架、Foundation／Spatial、Content 导入、Character／Cultivation／World、Combat、GameRuntime／保存／应用用例、Feature／场景／最小 UI／遗留删除／总验收与 URP 交接。
- 八个阶段均为 Codex 主责，并在专用架构 worktree 中形成独立提交、验证和架构复审；前一项未通过时下一项保持 blocked。
- 重构期间冻结 Unity Runtime、正式场景、CSV、DataConfig 导入器和相关 asset 的功能开发；美术工具链只能修改独立源资产与评估证据，不能接入 Unity 正式路径。
- 架构阶段只恢复新建角色、世界、据点、悬赏、Adventure、战斗、结算、返回和保存的最小功能视觉，不恢复旧 Built-in 样式。
- `01H` 必须确认新的 Adventure、Combat、Presentation、Content、Bootstrap、正式场景和资产所有者，并为 URP 预检提供稳定扫描基线。

#### U-URP-PREFLIGHT-01 · URP 迁移预检

- 显式前置为 `U-ARCH-REBUILD-01H`；架构父项未关闭时本任务保持 blocked。
- 读取实际 Unity 版本并通过当前官方包信息确认兼容 URP 版本；不得凭旧平台评估记录猜包版本。
- 只扫描架构重建后的 Packages、Graphics／Quality 设置、正式 Build Settings 场景、保留材质、Shader、相机、Tilemap、Sprite、UI、TextMesh Pro、透明高亮、选格射线和直接测试。
- 冻结 `U-URP-MIGRATE-01` 的精确 expectedPaths、材质迁移清单、回滚边界、人工视觉检查和 EditMode／PlayMode 回归。
- 本任务不安装 URP，不修改项目渲染行为。

#### U-URP-MIGRATE-01 · URP 安装、绑定与完整迁移

- 安装预检批准的 URP 包，创建并绑定 Pipeline Asset 与各 Quality 等级配置，只迁移架构重建后确认保留的材质与表现。
- 安装包、绑定管线、迁移材质和正式场景回归属于同一个迁移闭环；不得建立只安装包后停留在半迁移状态的独立任务。
- 重构后的正式场景入口、相机、最小 UI、Tilemap、Sprite、TextMesh Pro、透明排序、高亮、选格和功能验证必须保持成立；不为架构阶段已删除的 Built-in 临时视觉建立兼容。
- 不新增角色模型、外观字段、换装、动画或正式美术资源；不得保留两套渲染管线、复制场景或增加静默材质回退。

#### U-URP-VISUAL-BASELINE-01 · URP 正式薄切片视觉基础重建

- 显式前置为 `U-URP-MIGRATE-01`。
- 重建四个正式场景的相机、灯光、背景和基础构图，并建立 Sprite、Tilemap、透明物、占位 3D 模型和 TMP／按钮状态的统一 URP 技术视觉规范。
- 六角地面按上位美术设计锁定为标准化、模块化的低模 3D 网格：以可复用顶面与侧壁／裙边表现规则高度，以材质、色彩和贴花区分基础地貌，并把场景物、地表状态及战棋反馈保持为独立表现层；不把纯平面伪 3D Tilemap 或逐格独立模型作为正式路线。
- 重建六角地块、可达范围、选中、高亮、遮挡、单位占位和 World／Settlement／Adventure／Combat HUD 的基础视觉。
- 只提供后续 Toon、正式场景资源和 3D 角色共同消费的技术底座；不生产正式角色、模块化外观、石甲兽、关中最终装修、音效或批量资源。

#### U-CHAR-3D-PROTO-01 · 2.5D 几何占位测试台

- 显式前置为 `U-URP-VISUAL-BASELINE-01`；技术视觉基础未通过时本任务保持 blocked。
- 在既有 `HybridTacticalPrototype.unity` 中用正面明确的几何占位体验证模型缩放、六向旋转、地块高度、阴影、选中反馈和相机裁切。
- 六个 `Character.Facing` 索引与规则邻格中心方向逐项对齐；测试台不接入 CTB、AI、伤害或正式遭遇，不成为第二战斗所有者。

## 四、工具链与重构后 Unity 视觉链汇合的 P1 顺序

### 4.1 U-CHAR-HUMANOID-PROTO-01 · 玩家基础人形技术样例

前置为 `U-CHAR-3D-PROTO-01`，以及“`A-CHAR-AI-PLATFORM-EVAL-01` 选出胜出平台”或平台失败分支的 `A-CHAR-BLENDER-TEMPLATE-01` 完成。只允许其中一条资源路线成立。随后生产男、女基础体型、每种体型一个脸型、两种发型、一套双体型服装和一件主手器物；证明完整批准动作、六向映射、Toon 材质、蒙皮和接缝稳定。固定人形平台评估不能替代玩家模块化人形的独立验证。

### 4.2 U-SHIJIAHOU-3D-PROTO-01 · 石甲兽重型四足技术样例

在玩家基础人形样例后生产石甲兽模型、重型四足骨架、模型材质、技术 Prefab，以及待机、移动、受击、倒地、基础攻击、防御动作；直接消费既有 `enemy_shijiahou`、`EnemyData.combatTemplate` 与 AI 结果，不重建规则、数值、占格或 AI。该任务不拥有角色级攻击／受击／防御音效，也不拥有命中、受击、格挡等战斗 VFX；固定人形平台选型结果不能直接外推为四足平台结论。

### 4.3 A-GZ-PRESENT-01 · 关中正式场景与角色级表现资源

- 直接前置为 `U-URP-VISUAL-BASELINE-01` 与 `U-SHIJIAHOU-3D-PROTO-01`；在两项均通过前保持 blocked。
- 负责关中城、悬赏板、关中野外环境的场景级视觉／环境音效，以及石甲兽攻击、受击、防御的角色级音效和命中、受击、格挡等战斗 VFX。
- 不生成、导入或验收石甲兽模型、四足骨架、模型材质、技术 Prefab 和角色动画；这些只属于 `U-SHIJIAHOU-3D-PROTO-01`。
- 概念参考、声音参考和资源清单可以提前准备，但不得写成正式 Unity 接入、任务完成或前置解除。
- 实施优先级位于石甲兽技术样例之后，可与 `D-CHAR-APPEARANCE-01` 并列推进；backlog 摘要必须同步改为“关中城、悬赏板、野外环境视觉／音效与石甲兽角色级音效／战斗 VFX”。

### 4.4 D-CHAR-APPEARANCE-01 · 外观数据与存档

建立 `AppearanceProfile`、`AppearanceCatalog`、`CharacterVisualAssembler` 与 `PortraitComposer` 的最小数据闭环，冻结稳定 ID、合法 `none`、版本迁移、保存读取与缓存重建；不复制装备、战斗或存档所有权。

### 4.5 A-CHAR-PORTRAIT-STYLE-01 · Toon 与模块化立绘风格样张

分别评审运行时 Toon 材质和玩家全身立绘样张；立绘最低覆盖两个脸型、两种发型、两套服装的八种组合，并与现有 NPC 立绘并排比较。未通过前不批量生产完整创角内容。

### 4.6 U-CHAR-3D-FORMAL-01 · 正式 Adventure 3D 替换

以已验证的玩家与石甲兽 3D 表现替换重构后正式 Adventure 的临时单位占位；只消费 `U-ARCH-REBUILD-01H` 确认的新 Adventure、Combat 和 CombatPresentation 契约，不继续锁定旧 `ExplorationController`、旧 `TacticalCombatController` 或旧 BattleUIManager。接入前后 CTB、AI、伤害、结算、返回和空间规则结果一致。

### 4.7 U-CHAR-CREATION-VISUAL-01 · 完整首版创角外观批次

技术、风格、数据与存档闸门全部通过后，补齐两种体型各三个脸型、四档肤色、六种发型、六种发色、三套双体型初始服装和少量辅色，并接入正式角色创建 UI；不扩成完整捏脸、身体滑杆或任意散件换装。

## 五、资源版本边界

1. `AVM-03 / TQ-074` 继续保持 P1，在首批真实资源产生时触发，验证原始路径登记、LFS、干净工作区恢复和 Unity 构建闭环；AVM 消费真实资源，但不拥有或重做资源内容。
2. 原始模型、Blender 工程和生成记录进入 `assets/source/characters/`；只有通过对应闸门的 FBX、贴图、材质、动画和 Prefab 才进入 `src/Assets/Art/Characters/`。
3. `A-GZ-PRESENT-01` 的正式资源同样遵守美术资源登记与 LFS 边界；角色级音效／VFX 的归属不改变其战斗规则所有者。

## 六、队列与任务卡迁移规则

1. 本设计获书面复核后，再同步修改相关分线 backlog、活跃任务卡和 `开发管理/当前任务队列.txt`；本文不直接替代任务卡。
2. 新任务只有在前置满足、主责授权、没有待决策且能够给出完整 expectedPaths、验证、完成条件和停止条件时才建立为 ready 卡。
3. `C-HS-YY-JD-01K` 的原执行 run 达到合法终态并转换为 `codex_review` ready 后，将第二章明确清单中的未完成任务调整为 P2，并同步 backlog、活跃卡和队列；保留该复审行，已完成归档不改写。
4. 随后只把 `U-ARCH-REBUILD-01A` 建立为 P1 `codex_execute` ready 卡并插入队首；`01B`～`01H` 依次 blocked，只在前一项通过独立架构复审后逐项解锁。
5. 架构重建期间冻结 Unity Runtime、场景、CSV、导入器和相关 asset 功能开发；`A-CHAR-BLENDER-SETUP-01`、`A-CHAR-HUMANOID-RIG-01` 与 `A-CHAR-AI-PLATFORM-EVAL-01` 只有在路径和责任方独立时才可并行，且不得向 Unity 正式路径接入资产。
6. `U-URP-PREFLIGHT-01` 显式阻塞于 `U-ARCH-REBUILD-01H`；之后按 `U-URP-MIGRATE-01 -> U-URP-VISUAL-BASELINE-01 -> U-CHAR-3D-PROTO-01` 顺序逐项解锁。
7. 未满足依赖的 P1 项保留为 blocked，不为显示主线而制造虚假 ready 卡。任务首次进入 ready 队列时按用户批准的本设计顺序固定插入；普通轮次不重新计算或改变顺序。

## 七、验证与停止条件

### 7.1 管理迁移验证

- `tools/check-task-cards.ps1 -OutputJson`。
- `tools/check-review-text.ps1 -Paths AGENTS.md,CLAUDE.md,开发管理,docs/superpowers/specs`。
- 对修改的管理和设计路径运行 `tools/check-pending-whitespace.ps1`，暂存后运行 `git diff --cached --check`。
- 管理迁移不运行 Unity 或 BattleSim；实际环境、迁移和资源任务分别使用各自任务卡的直接验证。

### 7.2 停止条件

1. Blender 安装需要未批准的第三方插件、额外自动修补层或无法确认的授权。
2. 平台正式槽位开始前仍缺项目统一骨架、三条项目动作、四视图或付费私有生成授权。
3. URP 兼容版本、精确路径、材质迁移清单、回滚或正式场景回归无法冻结。
4. 架构阶段开始混入 URP、3D 角色、正式美术或为旧 API／序列化／Built-in 视觉增加长期 adapter、双写或 fallback。
5. URP 迁移需要修改重构后已验证的 CTB、六角空间、伤害、AI、存档或场景流转规则。
6. 平台评估、玩家人形、石甲兽、关中场景表现或 AVM 开始重复拥有同一资源或验收结果。
7. 为赶进度必须把平台原始结果直接导入正式 Unity 路径、保留两套渲染管线、长期维护双平台或用静默资源回退掩盖失败。

命中停止条件时回到对应根因，不追加平台、重试层、平行管线、兼容分支或未批准资源范围。
