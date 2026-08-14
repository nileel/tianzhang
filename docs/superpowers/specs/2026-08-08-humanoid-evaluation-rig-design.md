# 《天章》统一 Humanoid 评估骨架与评估动作设计

> 日期：2026-08-08
>
> 状态：用户书面复核已通过；实施任务拆分见 `docs/superpowers/plans/2026-08-08-humanoid-evaluation-rig-implementation.md`。本文本身不批准 Blender 安装、资产制作、Meshy／Tripo 正式生成槽位、60 分钟计时或平台胜负判定。
>
> 上位事实源：`docs/superpowers/specs/2026-08-08-ai-3d-character-asset-platform-evaluation-design.md`、`docs/superpowers/specs/2026-08-05-character-art-and-modular-appearance-design.md`、`docs/资源管理/美术资源版本管理规范.md`、`UNITY_STRUCTURE.md`。

## 一、决策摘要

1. 采用项目自建的最小 Humanoid 评估骨架，不采用外部标准骨架作为项目源骨架，也不使用 Rigify 生成的控制／机制／变形多层骨架作为导出骨架。
2. 骨架只服务 Meshy、Tripo 候选的共同换绑基准和男女基础体型的共享骨架证明。它不是未来正式人形骨架、正式 Avatar、正式 Animator 或正式动画美术的批准结果。
3. Blender 源绑定姿势固定为 T Pose；源正面为 Blender `-Y`，经固定 FBX 轴转换后导入 Unity 时本地 `+Z` 为正面、本地 `+Y` 为上方。模型原点位于双脚底部中心，`1 unit = 1 m`。
4. 骨架包含 Unity Humanoid 主体映射所需及项目主动要求的通用身体骨，并固定 16 根角色无关的袖、下摆、头发和胡须二级骨。评估期间不得增加苻渊或其他角色专用骨。
5. 与骨架共同制作两种非正式校准体型和三条 30 FPS 评估动作：待机、原地移动、空手驱动外部兵器／法宝的攻击。动作只施加蒙皮压力，不定义正式动作风格、门派招式或运行时速度。
6. Meshy 与 Tripo 候选不翻译或继承平台权重。两边统一保留平台骨架作前置诊断，然后丢弃工作副本中的平台骨架与权重，使用项目骨架重新生成相同的自动权重基线并做计时内有限修正。
7. 武器与法宝不进入 Humanoid 蒙皮骨架。未来表现可以是手持独立对象、围绕角色表现根悬浮的独立对象或脚下独立承载法宝；本文不创建挂点、Prefab、轨迹、物理或运行时逻辑。
8. 骨架、动作、候选工作文件和证据保存在 `assets/source/characters/` 的原始工程边界，不进入正式 Unity 目录。Unity Humanoid 证明在一次性临时项目中完成。
9. Meshy 与 Tripo 只在一次 A/B 中并存；选型后正式固定人形生产只保留一个默认平台。苻渊整身 A/B 不能证明平台适合模块化身体、脸、发型和服装生产。
10. 下游正式共享资源池以长期成本为准：男、女各一套身体、每种体型三个脸型、六个共享发型、三个 Outfit ID 的男女六份适配网格、四个肤色、六个发色和每套 Outfit 三个明显可辨的表面预设。核心 NPC 只按需增加少量专属部件，不建立专用主骨架。

## 二、事实基线与当前阻塞

### 2.1 仓库事实

截至本文设计调查：

- 仓库没有受追踪的人形 3D 骨架、模型或动画文件；未发现 `.blend`、`.fbx`、`.glb`、`.bvh`、`.anim`、`.controller` 或 `.avatar`。
- `src/Assets/Art/Characters/` 当前只有 `PortraitPresentation/` 及其 `.meta`。
- `assets/source/characters/` 当前不存在；`assets/source/` 受 `.gitignore` 保护，是本地原始工程边界。
- Unity 版本为 `6000.3.18f1`。
- 本机 PATH、应用注册表、AppX、开始菜单及常见便携／包管理位置均未发现 Blender。

因此本文可以冻结设计，但在获得后续明确实施授权并准备可靠 Blender 运行时之前，不得制作骨架、动作或 FBX，也不得开始 Meshy／Tripo 正式槽位。

### 2.2 上位约束

- 人形采用约 4.5 至 5 头身，普通人形建议约 8,000 至 15,000 三角面；立足点统一为底部中心。
- 玩家、普通修士、魔修和人形 NPC 共享 Humanoid 骨架、Avatar 与 Animator 契约；男、女基础体型不改变骨名、绑定姿势、动画或规则状态。
- 所有 Unity 源模型以本地 `+Z` 为正面、本地 `+Y` 为上方；不得为场景或角色增加私有朝向偏移。
- 原始工程保存在 `assets/source/characters/`；只有通过正式资源闸门的导出物才进入 `src/Assets/Art/Characters/`。
- 本任务不得修改 CTB、六角坐标、AI、伤害、装备、外观数据、存档、正式场景或正式 Animator。

## 三、候选方案与取舍

### 3.1 方案 A：自建最小项目骨架与动作

采用本方案。

优点：

- 骨名、层级、姿势、比例、附加骨和 Unity 映射完全由项目冻结。
- 不依赖第三方骨架命名、版本、控制层或动作许可。
- 男女校准体型可以直接绑定同一 Armature；候选换绑后直接播放相同 Action，不需要动作重定向。
- 只有三条非正式动作，直接 FK 关键帧比引入控制系统更简单。

缺点：

- 三条动作需要项目自行制作。
- 在实际 FBX 导入前，Unity Humanoid 兼容性仍必须由 `6000.3.18f1` 实测，不能只凭命名推断。

Blender 的 GPL 只约束 Blender 软件，不约束使用 Blender 创作的美术输出：`https://docs.blender.org/manual/en/3.2/getting_started/about/license.html`。

### 3.2 方案 B：采用合法标准骨架和动作来源

已核对 Quaternius Universal Base Characters 和 Universal Animation Library。它们以 CC0 提供，声明支持男女、多比例和 Unity 重定向：

- `https://quaternius.com/packs/universalbasecharacters.html`
- `https://quaternius.itch.io/universal-animation-library`

未采用的原因：

- 可编辑 `.blend` 源版本需要额外取得，免费标准版以导出物为主。
- 上游骨架命名在 2026 年发生过更新，会增加版本与来源维护。
- “兼容 Unity”是来源声明，不是本项目 Unity `6000.3.18f1` 的实际 Avatar 映射证明。
- 现成动作并非为肩、肘、腕、髋、膝、袍袖、下摆、头发和胡须压力检查设计，仍需修改或重做。

Adobe Mixamo 允许角色和动作在商业游戏中免版税使用，但账户地区存在访问限制，且官方明确提醒大体积衣物和头发会影响自动绑定；它可以作为动作参考来源，不能成为本任务的可靠共同骨架或最终绑定所有者：`https://helpx.adobe.com/creative-cloud/faq/mixamo-faq.html`。

### 3.3 方案 C：Blender Rigify Basic Human

Rigify 合法、支持一米单位和不同体型共享元骨架，但当前工作流会生成控制、机制和 DEF 变形层，并要求保留元骨架以支持再生成和版本变化。将其缩减为本项目稳定导出骨架会增加裁剪、烘焙、命名和维护步骤。Unity 的 Rigify 专项指南停留在旧版本，不能替代 Unity 6 证明。

当前 Rigify 工作流参考：`https://docs.blender.org/manual/en/latest/addons/rigging/rigify/basics.html`。

## 四、源文件、空间与绑定姿势合同

### 4.1 固定源路径

```text
assets/source/characters/humanoid-evaluation/TZ_HumanoidEval_v001.blend
```

源文件包含：

- 唯一导出 Armature：`TZ_HumanoidEval`；
- 男、女两个非正式校准网格集合；
- 三个评估 Action；
- 只用于校准观察、不参与导出的辅助对象。

### 4.2 空间合同

- Blender 使用公制，`1 unit = 1 m`。
- 校准身高 `1.70 m`，约 `4.75` 头身；这是美术校准尺度，不进入规则数据。
- Armature 与网格的 Location、Rotation、Scale 全部应用，Scale 为 `(1,1,1)`。
- 世界原点与 `Root` 位于双脚底部中心 `(0,0,0)`。
- Blender 源正面为 `-Y`，上方为 `+Z`。
- FBX 固定使用 `Forward=-Z`、`Up=Y`；Unity 导入后本地正面为 `+Z`、上方为 `+Y`。
- 绑定姿势为水平双臂 T Pose：双脚平行朝前，膝肘伸直但不反关节，掌心向下，拇指朝前。
- 主要关节校准点：髋高约 `0.88 m`、膝高约 `0.48 m`、踝高约 `0.08 m`、肩高约 `1.36 m`、肘距中线约 `0.47 m`、腕距中线约 `0.72 m`、头顶 `1.70 m`。
- 最终精确 rest matrix、骨骼 roll 和骨长冻结在 `.blend` 中，不按 Meshy、Tripo、性别或角色候选调整。

Unity 当前手册要求 Humanoid 骨架具备必需骨并推荐使用 T Pose：`https://docs.unity3d.com/cn/current/Manual/ConfiguringtheAvatar.html`。

## 五、固定骨骼层级与 Avatar 映射

### 5.1 完整层级

```text
Root
└─ Hips
   ├─ Spine
   │  └─ Chest
   │     └─ UpperChest
   │        ├─ Neck
   │        │  └─ Head
   │        │     ├─ Sec_Hair_C_01
   │        │     │  └─ Sec_Hair_C_02
   │        │     └─ Sec_Beard_C_01
   │        │        └─ Sec_Beard_C_02
   │        ├─ LeftShoulder
   │        │  └─ LeftUpperArm
   │        │     ├─ LeftLowerArm
   │        │     │  └─ LeftHand
   │        │     └─ Sec_Sleeve_L_01
   │        │        └─ Sec_Sleeve_L_02
   │        └─ RightShoulder
   │           └─ RightUpperArm
   │              ├─ RightLowerArm
   │              │  └─ RightHand
   │              └─ Sec_Sleeve_R_01
   │                 └─ Sec_Sleeve_R_02
   ├─ LeftUpperLeg
   │  └─ LeftLowerLeg
   │     └─ LeftFoot
   │        └─ LeftToes
   ├─ RightUpperLeg
   │  └─ RightLowerLeg
   │     └─ RightFoot
   │        └─ RightToes
   ├─ Sec_Hem_FL_01
   │  └─ Sec_Hem_FL_02
   ├─ Sec_Hem_FR_01
   │  └─ Sec_Hem_FR_02
   ├─ Sec_Hem_BL_01
   │  └─ Sec_Hem_BL_02
   └─ Sec_Hem_BR_01
      └─ Sec_Hem_BR_02
```

### 5.2 Unity 映射合同

`Root` 不进入 Humanoid 映射。以下骨骼按同名目标映射，并全部作为项目必查项，即使 Unity 将其中部分视为可选：

- Hips、Spine、Chest、UpperChest、Neck、Head；
- 左右 Shoulder、UpperArm、LowerArm、Hand；
- 左右 UpperLeg、LowerLeg、Foot、Toes。

16 根 `Sec_` 骨不进入 Humanoid 映射，但必须保留在 Transform 层级，并且不得使 Avatar 无效。

### 5.3 附加骨边界

`v001` 只允许上述 16 根角色无关的二级骨：

- 左右袍袖各两根；
- 下摆前左、前右、后左、后右各两根；
- 中央头发链两根；
- 中央胡须链两根。

没有对应部件的模型保持相应骨骼权重为零。评估期间禁止增加：

- 苻渊或其他角色专用骨；
- 面部、眼球、手指、胸部或肌肉骨；
- Twist、IK、Pole、Rigify `ORG/MCH/DEF` 控制骨；
- 武器、法宝、飞行承载、物理或碰撞骨；
- 平台骨架兼容骨。

若未来正式人形需要这些能力，另立设计并升级正式骨架版本；不得回改 `TZ_HumanoidEval_v001`，也不得在 Meshy／Tripo A/B 中为单个候选破例。

## 六、校准体型

`.blend` 中建立两个独立显示集合，共同绑定唯一 `TZ_HumanoidEval`：

- `CAL_Male`：约 4.75 头身，肩胸体积略宽；
- `CAL_Female`：相同身高、骨长和关节位置，只改变肩、胸腰、骨盆及四肢表面体积。

每个校准体约 3,000 至 5,000 三角面，关节处保留足够环线；两者各带简化宽袖、长袍下摆和后发束压力测试外壳，男体额外显示短须测试片。只使用纯色材质，不制作脸、UV、贴图、正式服装或角色身份特征。

两套网格必须共享：

- 同一 Armature 对象；
- 同一骨名、层级、rest matrix 与绑定姿势；
- 同一 Avatar 映射；
- 同一三个 Action；
- 同一动作文件；
- 不复制 AnimatorController，不改变规则状态。

校准网格只证明不同表面体积可共用骨架，不能进入角色创建、Prefab 或正式资源批次。

## 七、三条评估动作

### 7.1 共同合同

- 场景与导出采样率固定为 30 FPS。该数值只服务评估烘焙，不是正式动画帧率或停格风格批准。
- `Root` 始终固定在底部中心，无位移、旋转或缩放。
- 不使用 Root Motion，不改变角色规则位置或朝向。
- 不制作 IK、控制约束、形态键、物理模拟或正式 AnimatorController。
- 所有骨骼只记录旋转；仅 `Hips` 允许不超过 `3 cm` 的上下和左右位移。
- 导出前逐帧烘焙；任何骨骼都不得记录缩放曲线。
- `Sec_` 骨只做轻微、明确的延迟摆动，用于暴露权重和穿模，不追求自然布料效果。

### 7.2 `TZ_Eval_Idle_v001`

- 帧范围：`1–61`，2 秒循环；第 1 与第 61 帧完全一致。
- 第 1 帧：中性站立，双臂自然下垂，膝盖不锁死。
- 第 16 帧：重心轻移左侧，胸腔轻微抬起。
- 第 31 帧：呼吸最高点，肩部略展开，腕部小幅外旋。
- 第 46 帧：重心轻移右侧。
- 第 61 帧：回到第 1 帧。

检查肩部静态塌陷、肘部内侧夹折、腕部扭转、袖口小幅摆动、下摆重心移动，以及后发和胡须是否错误吸附到胸、肩或手臂。

### 7.3 `TZ_Eval_Move_v001`

- 帧范围：`1–31`，1 秒原地移动循环；第 1 与第 31 帧一致。
- 第 1 帧：左脚接触、右脚后伸。
- 第 8 帧：左腿承重，右膝前摆。
- 第 16 帧：右脚接触、左脚后伸。
- 第 23 帧：右腿承重，左膝前摆。
- 第 31 帧：回到第 1 帧。
- 髋关节前后摆幅约 `±28°`，膝关节最大弯曲约 `50°`。
- 双臂反向摆动约 `±22°`，肘部保持轻度弯曲。
- `Hips` 最大上下浮动 `3 cm`、左右移动 `2 cm`。
- 袖、下摆、头发和胡须延迟摆动不超过约 `12°`。

该动作只检查髋沟、膝窝、脚踝、大步穿袍、宽袖撞身和二级部件权重，不定义正式六向移动速度、气质或步法。

### 7.4 `TZ_Eval_Attack_v001`

采用空手驱动外部兵器或法宝的中性施力动作：

- 帧范围：`1–46`，1.5 秒，非循环。
- 第 1 帧：中性战斗站姿。
- 第 8 帧：重心后移，髋与胸向右后扭转；右臂抬起蓄力，左臂横向护势。
- 第 16 帧：最大蓄力；右肩明显外展、肘部弯曲、腕部后翻。
- 第 23 帧：前跨释放；髋与胸快速反转，右臂斜向前导引，左臂反向展开。
- 第 30 帧：最大随动；右腕翻转、双肩明显不对称，前膝弯曲约 `45°`。
- 第 38 帧：收势。
- 第 46 帧：回到中性战斗站姿。
- 胸髋最大相对扭转约 `35°`，肩部最大抬举约 `100°`，腕部最大旋转约 `30°`。
- 袖、下摆、头发和胡须延迟摆动最大约 `20°`，随后回收。

该动作检查肩腋撕裂、上臂穿袖、肘部尖折、腕部糖纸式扭曲、前跨时髋膝和下摆穿模，以及快速扭转时头发或胡须错误拉扯。它不定义兵器用法、施法节奏或正式动画品质。

## 八、武器、法宝与飞行承载边界

所有器物都是独立于 Humanoid 蒙皮的视觉对象。下游可以采用三种表现模式：

1. `Handheld`：普通兵器或低阶使用方式，独立对象附着到 `LeftHand` 或 `RightHand` Transform；不增加武器骨。
2. `Orbiting`：保留飞剑、法轮、符箓等未来环绕角色的表现方向；首版只允许独立对象由角色表现根驱动并保持固定局部偏移的悬浮，不模拟随时间变化的复杂环绕轨迹。真实环绕轨迹如需实施，必须另立设计。
3. `Mounted`：脚下飞行法宝由独立承载锚点驱动，角色骨架只播放站立或御器姿势，不跟随左右脚骨。

本文只冻结“器物不进入评估骨架”的资产边界，不创建表现根、手持 Socket、承载锚点、悬浮轨迹、遮挡排序、物理、Animator 或运行时代码。正式器物表现必须继续服从模型本地 `+Z` 正面与项目六向朝向契约。

## 九、固定输入／输出路径

### 9.1 公共评估输入

```text
assets/source/characters/humanoid-evaluation/
├─ TZ_HumanoidEval_v001.blend
└─ exports/
   ├─ TZ_HumanoidEval_Rig_v001.fbx
   ├─ TZ_Eval_Idle_v001.fbx
   ├─ TZ_Eval_Move_v001.fbx
   └─ TZ_Eval_Attack_v001.fbx
```

- `.blend` 是骨架、绑定姿势、校准网格和三个 Action 的唯一源文件。
- Rig FBX 包含唯一骨架和两个校准网格。
- 三个动作 FBX 只包含相同骨架与对应动作，不携带正式角色模型。
- 目录只在实际制作时创建，不预建空目录。

### 9.2 后续平台候选路径

只允许平台名 `meshy`、`tripo` 和槽位 `slot-01`、`slot-02`、`slot-03`：

```text
assets/source/characters/platform-evaluation/
├─ meshy/
│  └─ slot-01|slot-02|slot-03/
└─ tripo/
   └─ slot-01|slot-02|slot-03/
```

每个实际生成成功的槽位使用同一结构：

```text
<platform>/<slot>/
├─ raw/source.fbx
├─ working/rebind.blend
└─ exports/evaluation.fbx
```

失败或未使用的槽位不创建目录。平台原始下载统一重命名为 `source.fbx`；平台任务 ID、原文件名、模型版本、生成日期和授权状态继续记录在既有试产结果中，不新增登记系统。

## 十、统一换绑与权重处理

### 10.1 为什么不转译平台权重

Meshy 与 Tripo 的骨骼数量、命名和自动权重不同。分别建立骨骼映射会形成两套平台专用流程，也会让平台比较受到不同映射质量影响。因此：

- 平台自动骨架和权重只用于前置诊断；
- 最终候选不继承、重命名或翻译平台顶点组；
- 两边统一对项目骨架重新生成权重基线；
- 相同 Action 直接作用于相同项目骨架，不执行动作重定向。

### 10.2 固定流程

1. 在平台原骨架上做前置诊断；结果只记录，不计作项目骨架通过。
2. 从 `TZ_HumanoidEval_v001.blend` 执行 Save As，生成槽位的 `working/rebind.blend`。
3. 导入 `raw/source.fbx`，把原模型和原骨架保存在不可导出的 `PLATFORM_DIAGNOSTIC` 集合。
4. 复制候选网格作为工作网格；移除平台 Armature Modifier、父级关系和全部旧顶点组。
5. 平台生成输入优先直接要求项目 T Pose。若原始候选仍是 A Pose，只允许在工作网格上使用 Blender Edit Mode 的 Rotate／Translate、X Mirror 和 Proportional Editing，围绕项目固定 Shoulder／Elbow／Wrist 校准点对称地把手臂、手和相连袖袍调整到 T Pose；这些校准点是候选关节的硬对齐目标，不只是旋转参考，正交正视与侧视叠加时必须落在候选对应关节的过渡体积内。`PLATFORM_DIAGNOSTIC` 中的平台骨架必须停用，不得用平台骨架或平台权重摆姿后 Apply 烘焙。该调整计入 60 分钟；若使用上述工具仍不能满足关节对齐，或必须雕刻、重拓扑、使用额外变形工具或移动项目骨骼才能达到 T Pose，该候选失败。
6. 临时关闭 `Sec_` 骨的 Deform，使用 Blender `Armature Deform > With Automatic Weights` 为核心 Humanoid 骨生成统一初始权重。
7. 恢复固定 `Sec_` 骨，并按同一规则处理二级部件：
   - 宽袖从上臂核心权重向 `Sec_Sleeve_*_01/02` 形成根部到袖口的线性渐变；
   - 下摆从 Hips／腿部权重向对应 `Sec_Hem_*_01/02` 渐变；
   - 头发、胡须根部保持 Head 权重，向末端过渡到对应 `Sec_` 链；
   - 单个顶点全部 `Sec_` 权重合计不得超过 `0.7`。
8. 刚性附件固定归属：发冠、发簪等归 Head；腰扣、固定腰饰归 Hips；悬浮武器和法宝不蒙皮。无法归类的附件停止并记录，不临时发明骨骼。
9. 所有权重归一化，每顶点最多四根影响骨；不得存在未加权顶点或负权重。
10. 直接播放公共 `.blend` 中的三个 Action，不做平台动作重定向。

### 10.3 允许的有限修正

60 分钟计时内只允许：

- Normalize、Smooth、Add、Subtract 权重；
- 修正肩、肘、腕、髋、膝及批准二级部件附近的局部权重；
- 对明显穿模、破面、非流形或悬浮部件做有限几何整理；为自动权重失败处理明确的重叠点、非流形或断裂部件后，只能重新执行同一个自动权重步骤；
- 对袖袍、下摆、头发、胡须、手部和主要饰物做达到评估硬门槛所必需的有限形体整理；
- 调整候选网格以符合固定 T Pose、单位、朝向和原点。
- 检查材质槽、贴图路径和 Base Color 可用性；只允许重连候选已有贴图或纠正已有材质槽归属，不新绘完整贴图、不借材质重做角色设计。

本节允许的几何或形体整理不得替代、扩展或规避第 10.2 节步骤 5 的 T Pose 姿态调整；达到 T Pose 的工具和关节对齐判据只以该步骤为准。

禁止：

- 改骨长、rest pose、名称或层级；
- 使用平台专用骨骼映射表；
- 从男／女校准网格复制权重来掩盖候选拓扑问题；
- 建立第三套绑定工具、外部自动绑定服务或兼容脚本；
- 完整手绘全身权重、全身重拓扑、增加纠正骨或形态键；
- 因某个平台失败而改用另一种权重算法。

若 Blender 自动权重在有限几何清理后仍失败，或必须完整手绘权重才能继续，该候选直接失败。

## 十一、长期正式生产成本与模块化资源池

### 11.1 不长期维持双平台

Meshy 与 Tripo 只在一次 A/B 中并存。正式生产同时维护两平台会重复提示词、参数、导出记录、授权核验、Blender 修整经验和平台版本验证，其隐藏人工成本高于月费差异。选型后只保留一个默认主平台；主平台价格、授权或质量显著变化时另立重新评估，不在日常生产中自动回退到第二平台。

正式单件成本按以下口径评估：

```text
单件合格资产成本
= 平台月费与 credits 的批次分摊
+ 失败生成分摊
+ Blender 换绑和修整时间
+ Unity 导入与返工时间
```

质量和商业授权先作为硬门槛。通过硬门槛后，平台选择重点比较每件合格资产所需生成次数、合格率、Blender 中位及最差合格修整时间、正式批次所需最低套餐和工作流稳定性，不以一次性 A/B 订阅费决定长期平台。

### 11.2 正式共享资源池

长期目标：先组合出男、女各 10 个明显可辨的普通角色；只有核心 NPC 按需增加少量专属部件。

| 资源 | 数量 | 几何成本 |
|---|---:|---:|
| 基础身体 | 男、女各 1 | 2 个模型 |
| 完整脸型 | 每种体型 3 | 6 个头部模型 |
| 发型 | 男女共享 6 | 6 个模型 |
| 发色 | 6 | 材质预设，不新增模型 |
| 肤色 | 4 | 材质预设，不新增模型 |
| Outfit | 3 个 ID | 男、女共 6 个适配网格 |
| Outfit 表面 | 每套 3 个 | 材质／贴图预设，不新增模型 |
| 基础头饰 | 0 | 核心 NPC 或后续批次按需制作 |
| 武器／法宝 | 独立池 | 不计入基础角色组合数 |

几何模块总数为 20：2 身体、6 脸型、6 发型、6 服装网格。

每种性别的理论组合数：

```text
3 脸型 × 4 肤色
× 6 发型 × 6 发色
× 3 服装 × 3 服装表面
= 3,888
```

男女合计 7,776 种档案组合。更适合战棋镜头判断的头部和服装表面组合为每种性别 `6 × 6 × 3 × 3 = 324`。首批每种性别选择 10 个时，尽量不重复“发型模型＋服装模型”组合；若重复，发色与服装表面必须同时明显变化，不能只靠细小纹样或近似颜色凑数。

### 11.3 低成本表面变体

每套 Outfit 使用固定 UV、一张低饱和基础 Base Color、一张颜色区域 Mask、可选的大块纹样 Overlay 和三个 `outfitPaletteId` 预设。三个表面必须通过大面积主色、衣襟／袖缘／腰带辅色、大块宗门纹样或金属／玉石材质关系形成固定镜头可辨差异；不能只改难以看清的细小刺绣。

每个发型使用一个网格和中性灰度／低饱和基础贴图，六个 `hairColorId` 通过材质参数变化，不复制模型。材质变体仍需要对应 2D 立绘配色、稳定目录 ID 和组合检查，不能视为零成本。

### 11.4 核心 NPC 专属边界

核心 NPC 先复用统一骨架、基础身体接口、动作、材质和可复用模块，只独立制作最能改变身份的少量部件：专属脸型、发型／发冠、整身服装表面或网格、独立悬浮法宝、必要的独立姿态和精绘立绘。不得默认增加身体或主骨架。

苻渊可以有专属脸、束发、短须、黑袍和青玉坠，但继续服从统一骨架与动作合同。苻渊整身 Meshy／Tripo A/B 只验证核心固定 NPC 路线；模块化身体、头、发型和服装必须在上位设计第 12.3 节的模块化人形技术样例中另行证明。

本章只记录下游成本与资源池边界，不批准创建 AppearanceCatalog 条目、修改槽位 schema、生产模块、生成 2D 图层或接入运行时。

## 十二、Blender 与 FBX 验收

### 12.1 Blender 源检查

使用 Blender 后台读取 `.blend`，必须证明：

- 场景帧率为 30 FPS；
- 只有一个导出 Armature `TZ_HumanoidEval`；
- 骨名、父子层级和数量与本文完全一致；
- `CAL_Male`、`CAL_Female` 的 Armature Modifier 指向同一对象；
- 两个体型共享骨骼数组、绑定姿势和 rest matrix；
- 世界原点、对象缩放和源正面符合第四章；
- 三个 Action 名称与范围分别为 `1–61`、`1–31`、`1–46`；
- Root 没有变化，动作没有缩放曲线；
- Idle、Move 首尾一致，Attack 非循环；
- 校准网格权重归一化、每顶点最多四骨、无未加权顶点；
- `Sec_` 骨只影响对应袖、下摆、头发和胡须区域。

### 12.2 FBX 导出合同

- Binary FBX；
- Scale `1.0`；
- Apply Scalings 固定为 `FBX Units Scale`；若实际采用的官方稳定 Blender 版本没有该选项或语义发生变化，停止并核对，不改用另一预设继续；
- Forward `-Z Forward`；
- Up `Y Up`；
- Apply Transform 关闭；轴转换只使用 `Forward=-Z`、`Up=Y`，不得在两个平台候选之间切换该设置；若关闭后不能满足 Unity 的单位、正面、原点或 Humanoid 映射要求，按停止条件报告，不试探第二套导出预设；
- Add Leaf Bones 关闭；
- Only Deform Bones 关闭，确保不参与蒙皮的 `Root` 仍随全部 FBX 导出；不得借此加入控制骨，因为源骨架本身不允许控制骨；
- 动画以 30 FPS 烘焙；
- 每个动作单独导出，关闭 `All Actions`；
- 不导出相机、灯光、校准辅助线或平台诊断集合。

## 十三、临时 Unity Humanoid 验收

不把 FBX 放入 `src/Assets/`。使用 Unity `6000.3.18f1` 创建一次性临时项目，验证完成后删除临时项目，只保留日志和截图证据。

必须证明：

1. `TZ_HumanoidEval_Rig_v001.fbx` 以 `Animation Type=Humanoid`、`Avatar Definition=Create From This Model` 导入；三个动作 FBX 均以 `Animation Type=Humanoid`、`Avatar Definition=Copy From Other Avatar` 指向该 Rig FBX 生成的 Avatar，不允许各自动生成 Avatar。
2. Avatar 同时满足 `Avatar.isValid == true` 与 `Avatar.isHuman == true`。
3. 第 5.2 节列出的全部项目映射存在。
4. Root 和 16 根 `Sec_` 骨不进入 Humanoid 映射，但导入后仍存在于 Transform 层级。
5. 加入全部 `Sec_` 骨后 Avatar 仍有效。
6. 三个动作均满足 `AnimationClip.isHumanMotion == true`，时长分别约 2 秒、1 秒和 1.5 秒。
7. 三个动作使用同一个 Avatar；不为男女分别创建 Avatar 或 AnimatorController。
8. 临时验证通过 Playables 或直接采样 AnimationClip，不创建正式 AnimatorController。
9. 男、女校准网格分别播放三动作时，SkinnedMeshRenderer 引用同一骨架路径。
10. Unity 中脚底中心落在模型原点，身高约 `1.70 m`，脚尖和角色正面朝本地 `+Z`。
11. 不创建或修改正式场景、Prefab、材质、角色数据或运行时代码。

## 十四、视觉证据与交付

### 14.1 固定截图

每种体型输出：

- Idle 左右重心和呼吸最高点；
- Move 左右接触和两次过渡姿势；
- Attack 蓄力、释放、最大随动和收势。

检查肩、肘、腕、髋、膝是否脱骨、翻面或明显塌陷；袖口是否穿身；下摆是否穿腿；头发和胡须是否被错误拉向胸、背或手臂；附加骨动作是否存在且不破坏 Humanoid 映射。

### 14.2 最小资产交付

```text
assets/source/characters/humanoid-evaluation/
├─ TZ_HumanoidEval_v001.blend
├─ exports/
│  ├─ TZ_HumanoidEval_Rig_v001.fbx
│  ├─ TZ_Eval_Idle_v001.fbx
│  ├─ TZ_Eval_Move_v001.fbx
│  └─ TZ_Eval_Attack_v001.fbx
└─ evidence/
   ├─ blender-validation.txt
   ├─ unity-humanoid-validation.txt
   └─ calibration-contact-sheet.png
```

证据记录精确 Blender／Unity 版本、骨架和动作文件 SHA-256、映射结果、动作时长、校准体型引用和未解决风险。原始工程按项目规范备份，不能用网盘替代 Git 中未来正式构建资源。

## 十五、停止条件

命中任一项即停止，不增加兼容层、自动重试或临时替代骨架：

1. Blender 不可用，或无法确认所使用的官方稳定版本。
2. FBX 导出后不能保持单位、原点、正面、层级或 rest pose。
3. Unity Avatar 无效、不是 Humanoid，或项目要求映射缺失。
4. 附加骨导致 Avatar 失效、Transform 丢失或动作无法复用。
5. 男、女校准体型需要不同骨长、rest pose、Action、Avatar 或 AnimatorController 才能成立。
6. 动作必须依赖 Root Motion、正式 Animator、IK 系统或运行时代码才能播放。
7. 为修复校准网格必须增加 Twist、纠正骨、角色专用骨或完整手绘全身权重。
8. 候选必须改变既定骨架、绑定姿势、骨名、Avatar 映射、复制 Animator 或修改运行时规则才能成立。
9. 必须把资产放入正式 Unity 目录才能完成证明。
10. 实施或集成时活动 run、进程持有型集成锁、任务占用或现有未提交路径发生冲突。
11. 平台或动作来源的商业授权、输入权利或私有性无法确认。

## 十六、明确非目标与后续授权门

本文不批准：

- 安装 Blender 或其他 DCC／绑定插件；
- 制作本文描述的骨架、动作、校准网格或证据；
- 创建正式 Unity Avatar、AnimatorController、Prefab、材质或场景；
- 修改 CTB、六角空间、AI、伤害、装备、外观数据、目录 schema 或存档；
- 生产模块化身体、脸型、发型、服装、立绘图层或核心 NPC 专属部件；
- 开始 Meshy／Tripo 正式槽位、60 分钟计时、评分或胜负判断。

用户完成本文书面复核后，仍需下一次明确要求才进入实施。实施前重新检查 `git status --short`、schema 5 活动 run、进程持有型集成锁、目标路径冲突和 Blender 可用性；任一条件不成立就按第十五章停止。
