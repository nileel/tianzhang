# 2D 战斗动画生产流程详细方案

日期：2026-08-25
任务：`D-CHAR-2D-BATTLE-ANIM-PIPELINE-01`
状态：负责人已选择 A；书面方案完成，苻渊隔离 2D 战斗动画 pilot 已按本方案解锁。未生产资产、未消费外部 credits、未修改 Unity。

## 一、授权、证据与非结论

本方案只定义《天章》斜俯视六角战斗的苻渊 2D 动画 pilot 如何生产、测量和接入隔离比较入口。剧情／UI 立绘仍是剧情与界面资产；闭关中的苻渊仍不是正式单位、遭遇、规则或存档对象。

《龙胤立志传》是负责人指定的实战表现下限。复核入口为研究中的 S7（官方 Steam 产品页），证据等级 E3：它只给出产品画面的可观察目标。不能由它推断原作的逐帧、剪纸／骨骼、3D 预渲染或有限帧／棋子化技术，也不能推断方向数、帧数、制作周期、工时或成本。其方格地形不能替代本项目固定正交斜俯视六角矩阵。

现有 `assets/generated-character-art/fuyuan-2d-tactical-sprite/` 的六张静态方向图是已冻结的旧技术样张：可读取其中的身份、六向、`768×768`、`512 PPU`、Custom pivot `(0.5,0.18)` 和接地合同，但不得覆盖、拆改、扩帧或把它们冒充本 pilot 的动作资产。`dialogue-transparent/` 与 `profile-wide/` 的苻渊图只提供身份、年龄、服饰、配色和材质参考，不能裁切、变形或逐帧修补为战斗动画。

## 二、质量下限与人工验收合同

下列是《天章》自己的验收合同，而不是对 S7 内部实现的猜测。所有人工判定都在既有 `AdventureScene/VisualBaselineBoard`、主相机位置 `(0,8,-10)`、欧拉角 `(38°,0°,0)`、`orthographicSize=6.2`、同一光照、同一地块和 `1920×1080` 输出条件下进行。

| 维度 | 每次都必须人工确认 | 失败即止 |
|------|------------------|----------|
| 固定镜头角色清晰度 | 苻渊仍可辨认为成熟棱角脸、白灰顶髻与后发、短须、炭黑／灰白／旧铜金／淡玉；大轮廓不被帧间抖动吞没。 | 只能靠放大镜头、私有缩放或名字／UI 才能识别。 |
| 六向与移动 | 方向 `0..5` 仍精确对应 `HexCoord.Directions` 与 `90/150/210/270/330/30°`；移动前后脚底在格心接地，不镜像、不重排、不以各向偏移补偿。 | 缺方向、方向错配、脚底漂浮／穿格或方格案例替代六角验证。 |
| 攻击、受击、死亡 | 三者是首批高风险语义：攻击有可读的蓄势—命中—回收，受击不丢身份且冲击来源可辨，死亡有可判定的终态且不伪装为待机。 | 只用色闪、静态截图或根节点晃动而没有可辨动作／终态。 |
| 施法与特效分层 | 角色动作、一次性法术特效、投射／范围指示和命中反馈层级分明；特效不遮住苻渊身份和事件时点。 | 把特效烘焙进角色帧、用特效覆盖角色动作，或用角色帧承担规则状态。 |
| 宽袖、武器与遮挡 | 宽袖、可选武器轮廓和双手保持连通可读；前景遮挡真实覆盖应被遮部分，前后层不以 always-on-top 排序伪造。 | 宽袖／武器跨躯干硬折、穿模状剪影、手消失，或人物错误盖住地形／遮挡物。 |
| 多单位战场可读性 | 同镜头下与既有隔离探针并置时，角色、地块、遮挡、范围覆盖和事件起止仍可分辨。 | 单角色特写通过而九格矩阵、遮挡或多单位场景不可读。 |

每个状态的人工检查必须同时看起始帧、事件帧和结束帧；静态截图只可作为矩阵证据的一个格子，不能替代动作回放。宽袖、武器的“可选”仅指实际被该次 pilot 选择的角色动作可含或不含武器：若含武器，必须覆盖六向和攻击／施法；若不含，日志须记录“不含武器”及其没有被当作批量成本结论的原因。

## 三、技术方法选择门

本方案不预选逐帧、剪纸／骨骼、3D 预渲染或有限帧／棋子化，也不把“静态载体＋有限帧”当作已批准的共享哲学。所有方法在运行时都必须导出同一份 raster Sprite atlas 合同；制作源可以不同，不能把源技术差异泄漏成不同规则、镜头或场景条件。

pilot 的第一阶段写入 `source-selection.md`，以四个方法作为待审候选：逐帧、剪纸／骨骼、3D 预渲染、有限帧／棋子化。每行必须记录：调研支持／缺口、可实际使用的工具与版本、需要的外部许可或 credits、一次小型高风险试片的实际人工分钟和实际费用、攻击／受击／死亡是否通过第二节人工门、已知返工原因，以及“选定／未选定／未尝试”的真实状态。未尝试不是失败，缺少费用或工具授权不是可猜测的零成本。

试片按攻击、受击、死亡的顺序进行；每一项都必须同时覆盖六向，并保持固定镜头、格心、宽袖、遮挡与事件条件。失败只允许把失败原因和消耗写入日志后停止该候选；不得通过逐方向补画、减少方向、镜像、私有锚点或追加规则／fallback 使其过关。只有某候选三项均通过，且其实际分钟、外部费用／credits、制作次数与返工次数已可复核，才可以在 `source-selection.md` 选定它继续完成 idle、move、cast 和完整 atlas。没有候选满足该门时，pilot 停止并报告，不创建第三条混合路线或虚构成本。

## 四、版本化资产单位与导出形状

一个可交付 pilot 资产单位是“苻渊 × 六向 × 六个状态”的单一版本；六个状态固定为 `idle`、`move`、`attack`、`hit`、`cast`、`death`。每个状态恰有一个 RGBA PNG atlas，方向为六行 `0..5`，每行由同一状态的连续帧组成。每个 cell 固定 `768×768 px`；一行的帧数由选定方法的实际结果决定，不能在本方案中虚构帧数。所有方向同状态必须拥有相同 cell 数；`manifest.json` 必须记录帧数、每格源矩形、每个事件帧及每个方向／帧的脚底锚点。

| 文件 | 固定用途 |
|------|----------|
| `assets/generated-character-art/fuyuan-2d-battle-animation-pilot/source-selection.md` | 技术候选、试片证据、选择和未选择原因。 |
| `assets/generated-character-art/fuyuan-2d-battle-animation-pilot/manifest.json` | 版本、输入 SHA-256、实际源文件、atlas 格网、帧／事件／锚点、导出 SHA-256 与自动检查结果。 |
| `assets/generated-character-art/fuyuan-2d-battle-animation-pilot/production-log.md` | 每次制作、人工修订、失败、时间、工具、credits／费用和 QA 的逐项账本。 |
| `fuyuan_battle_idle.png`、`fuyuan_battle_move.png`、`fuyuan_battle_attack.png`、`fuyuan_battle_hit.png`、`fuyuan_battle_cast.png`、`fuyuan_battle_death.png` | 唯一的六个运行时 source atlas；不含地面、阴影、UI、文字或常驻特效。 |
| `source/fuyuan_battle_animation.aseprite`、`.kra`、`.spine` 或 `.blend` | 仅选定方法对应的一份可编辑母源，其他三项不得伪造空文件。其路径和 SHA-256 由 manifest 固定。 |

所有 atlas 的 alpha 四角为透明；人物的脚底锚点保持既有 `(0.5,0.18)` 语义，任意方向／帧相对该点的落地高度不允许以 Unity 私有位置或缩放补偿。角色帧不烘焙地块、接触阴影、法阵、投射物、命中效果、文字或遮挡物。角色相关动作可改变剪影；特效与规则事件仍属 Unity 表现层，必须有独立的所有者。

## 五、生产、修订与成本记录流程

1. 读取苻渊直接事实、既有六向样张合同、批准的本方案和 `assets/generated-character-art/README.md`；记录全部输入的路径和 SHA-256。确认工具授权、实际 credits／费用和可保存的源文件；不具备任一条件即停止。
2. 建立 source-selection 账本，按第三节进行攻击、受击、死亡的六向小试片。逐次记录开始／结束时间、操作者、方法、工具版本、实际费用／credits、结果和失败／返工原因。
3. 只在一项方法通过选择门后完成一个可编辑母源和六个状态 atlas。每个修订都增加 production log 记录；不得覆盖掉失败条目，不计算不存在的自动化折扣。
4. 导出前锁定所有 atlas、母源和输入哈希。写入 manifest 的方向顺序、frame count、source rect、事件帧、锚点、文件尺寸、格式与 SHA-256；没有这些字段即不导出。
5. 自动检查文件名、六状态、六向、相同帧数、`768×768` cell、RGBA、透明角、atlas／manifest SHA、可读事件次序、锚点覆盖和不存在镜像／私有缩放数据。自动检查只判结构确定性。
6. 在第二节固定镜头矩阵执行人工视觉检查，并逐行写入“通过／失败／证据位置／人工判定者”。人工检查才判定冲击力、身份保持、宽袖／武器、真实遮挡和多单位可读性。
7. 通过后将 source atlas 原样交给 Unity integration；pilot 本身不得复制到 `src/`、不得改 Unity、不得接入正式单位。将实际的生产分钟、工具费用、生成／制作次数、返工次数和失败原因写入 `开发管理/苻渊2D战斗动画样例生产记录.txt`，供后续成本证据卡读取。

换装、武器、批量角色与新增动作只记录计量口径，不在苻渊 pilot 推算金额：每一项分别计“可复用母源／必须重做部件／新增 atlas 状态／新增六向帧／人工分钟／外部费用／返工次数／QA 分钟”。没有该项实际记录时写“未知”，不外推成单价或规模折扣。

## 六、Unity 消费合同

Unity 只消费 pilot 已冻结的六个 atlas，把它们原样复制为 `src/Assets/Art/Characters/TacticalSprites/FuYuanBattle/FuYuan_Battle_<State>.png`，并保存其 `.meta`。导入固定为 Sprite、多 sprite grid、`512 PPU`、Custom pivot `(0.5,0.18)`、无方向私有缩放／平移／镜像；atlas 格网和帧数必须由 manifest 驱动并由 importer 验证，缺状态、缺方向、缺帧或不一致即失败关闭。

当前 `TacticalSpritePresentationController` 与 `TacticalSpriteProbeMatrix` 是旧静态六向样张的所有者，不能被改写为动作资产的隐式 fallback。动态 pilot 必须使用受限的新 `BattleAnimationSpritePresentationController`，只消费既有 `StaticChessPresentationEvent` 和批准的 atlas；它只在角色根节点播放选定状态、在结束时复位，并只暴露既有一次性施法效果信号。不得写入 `Character.Facing`、格位、路径、伤害、状态、死亡结算、装备或存档。

`VisualBaselineBuilder`、`AdventureSceneBuilder` 与 `SceneArchitectureValidator` 只允许为新 `FuYuanBattle` Prefab 和隔离 `BattleAnimationSpriteProbeGroup` 提供确定性导入、六向矩阵和验证。现有 `TacticalSpriteProbeGroup`、静态 3D `FacingProbe_*`、相机、光照、九格地块和正式 `AdventureUnitSpawner` 保持原所有者与行为。新组默认关闭；测试显式启用它，且同一格位的静态 3D 与旧静态 2D 样张均不参与渲染。

运行时自动验证须证明：每一个批准状态都映射到六方向、事件的开始／命中（若有）／结束索引均与 manifest 一致、角色根节点在结束复位、施法只触发一次效果信号、活动 sprite 与 atlas 一一对应、规则快照不变。固定镜头人工矩阵须另行确认第二节全部画面标准；两类结果不得互相替代。

## 七、下游卡冻结

`A-CHAR-2D-BATTLE-ANIM-PILOT-01` 只负责选型试片、唯一苻渊 source atlas、来源／成本日志和人工 QA；它不触碰 `src/`。它的完成是六个 atlas 与完整记录通过，不是“生成过图片”。它失败或缺少工具／费用授权时保持阻塞，不能用旧静态精灵或 UI 立绘顶替。

`U-CHAR-2D-BATTLE-ANIM-INTEGRATION-01` 只负责把 pilot 已冻结 atlas 导入隔离的 Unity 比较入口、构建受限 controller／Prefab、建立可重复自动与人工证据并解除两个下游 blocker；它不修源图，不重新选技术，不扩大到正式单位或战斗规则。两张卡的精确允许路径、验证、完成与停止条件已写回各自任务卡。

## 八、自审与批准结果

- 占位符：没有把帧数、方向数、技术方法、工具权限、credits 或成本伪装为已知；实际值由 manifest 和 production log 记录。
- 一致性：六向、镜头、锚点、隔离比较入口和规则不变边界与既有合同一致；动态需求不再用旧静态样张代替。
- 范围：只覆盖苻渊 2D pilot 与其隔离接入，不生产资产、不改 Unity，也不触及剧情立绘、正式单位、规则或存档。
- 歧义：自动检查与人工视觉矩阵分别列出；攻击、受击、死亡是先行高风险语义；技术选择和批量成本均以真实记录为准。
- 2026-08-25，负责人已选择 A“批准方案并解锁 pilot”。`A-CHAR-2D-BATTLE-ANIM-PILOT-01` 现可按本方案开始其隔离样例生产；该批准不替代 pilot 对工具／credits 的实际授权门，也不授权 Unity integration。

### 已实施 pilot 事实

- 2026-08-25，`A-CHAR-2D-BATTLE-ANIM-PILOT-01` 按本合同完成隔离苻渊样例：有限帧／棋子化通过攻击、受击、死亡六向三帧选择门，留下唯一 `.spine` 时间线母源和六份三帧、六方向 `768×768` cell RGBA atlas。实际 18 分钟、0 元／0 credits、7 次生成（含五行攻击失败稿 1 次）和全部哈希记录在 pilot 的 source-selection、manifest 与生产记录；没有导入 `src/`。
- 该事实不替代下游 Unity 的真实 `VisualBaselineBoard` 遮挡、多单位、事件、活动 atlas、根节点复位或规则快照验证；这些仍属于 `U-CHAR-2D-BATTLE-ANIM-INTEGRATION-01`。

### 已完成 Unity 接入事实

- 2026-08-25，`U-CHAR-2D-BATTLE-ANIM-INTEGRATION-01` 已只消费上述冻结 atlas，原样复制为 `FuYuanBattle/FuYuan_Battle_<State>.png`，并以 manifest SHA-256、`6×3` grid、`512 PPU`、Custom pivot `(0.5,0.18)` 和事件帧验证导入。
- 新 `BattleAnimationSpritePresentationController` 与 `FuYuan_BattleAnimationSprite` Prefab 只消费 `StaticChessPresentationEvent`；它选择同一状态的六方向三帧 atlas、仅在 cast release frame 发出一次效果信号并在结束复位根节点，不拥有方向、格位、伤害、状态、结算、装备或存档。
- `AdventureScene/VisualBaselineBoard` 已建立默认关闭的 `BattleAnimationSpriteProbeGroup`。其显式 route 关闭旧静态 2D 与静态 3D probe；旧静态样张、相机、光照、地块和 `AdventureUnitSpawner` 不变。EditMode、PlayMode、资产版本、数据链和程序集边界通过；人工矩阵仍由后续用户可玩比较而非自动分数判定。

### 后续成本证据事实

- 2026-08-26，`D-CHAR-BATTLE-VISUAL-COST-EVIDENCE-01` 将本路线的 18 分钟、0 元／0 credits、7 次生成和 1 次失败返工，以及 Unity 接入来源记录的约 3 分钟单列为实际记录；约 3 分钟不与 source pilot 合计为路线总价。新角色、动作、方向、武器、服饰、批量和工具维护仍以本方案“生产、修订与成本记录流程”的计量口径记录，未试项目均为未知；该整理不决定视觉方向。
