# 《天章》统一 Humanoid 评估骨架与评估动作实施计划

> 日期：2026-08-08
>
> 状态：实施计划待用户复核；本计划尚未批准安装 Blender、制作资产或启动 Meshy／Tripo A/B。
>
> 设计事实源：`docs/superpowers/specs/2026-08-08-humanoid-evaluation-rig-design.md`。

## 一、目标与实施形态

在不接入正式 Unity 项目的前提下，完成以下本地评估资产与证据：

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

`.blend` 是唯一资产源文件。源文件内部允许保留一个不参与导出的 Blender Text 数据块 `TZ_Validate`，用于后台重复检查骨架、动作与权重合同；不得因此在仓库新增绑定框架、兼容脚本或正式运行时代码。

本计划的 `HUM-EVAL-01`～`HUM-EVAL-07` 是计划内切片 ID，不是 `开发管理/任务卡` ID，不自动进入近期队列。正式排队时再依据当时事实源、活动 run 和执行器能力分配 owner。

## 二、串行依赖与成本估计

```text
HUM-EVAL-01 工具与写入闸门
  → HUM-EVAL-02 骨架源文件
  → HUM-EVAL-03 男女校准体型与蒙皮
  → HUM-EVAL-04 三条评估动作
  → HUM-EVAL-05 FBX 导出与 Blender 证明
  → HUM-EVAL-06 临时 Unity Humanoid 证明
  → HUM-EVAL-07 证据签收与停止
```

所有任务修改同一个源 `.blend` 或依赖其导出结果，禁止并行编辑。若使用多个 AI，任一时刻只能有一个资产编辑 owner；独立 reviewer 只能读取已关闭切片的产物。

| 任务 | 规划人工时间 | 说明 |
|---|---:|---|
| HUM-EVAL-01 | 0.5～1 小时 | 不含下载安装等待时间 |
| HUM-EVAL-02 | 2～4 小时 | 最小 39 骨 Armature、空间合同和内嵌验证 |
| HUM-EVAL-03 | 5～10 小时 | 两个简化体型、压力部件与有限权重 |
| HUM-EVAL-04 | 3～6 小时 | 三条非正式 FK 动作 |
| HUM-EVAL-05 | 1～2 小时 | 四个 FBX、回读和 Blender 证据 |
| HUM-EVAL-06 | 2～4 小时 | 临时 Unity 项目、Avatar 与动作证明 |
| HUM-EVAL-07 | 0.5～1 小时 | 汇总证据、备份核对和最终停止 |
| 合计 | 14～28 小时 | 规划值；实施时记录实际时间，不包含任何平台 credits |

## 三、全局路径与禁止项

### 3.1 允许写入

- `assets/source/characters/humanoid-evaluation/`：仅在 HUM-EVAL-01 工具闸门通过后按实际需要创建。
- 操作系统临时目录下唯一的 `tzg-humanoid-eval-unity-<guid>`：只供 HUM-EVAL-06 使用，验证后安全删除。
- 本计划与对应设计文档：只记录批准状态、实施结果或明确阻塞；不把二进制资产强行加入 Git。

### 3.2 明确不触碰

- `src/Assets/`、`src/ProjectSettings/`、正式场景、Prefab、Avatar、AnimatorController 和运行时代码；
- CTB、六角坐标、AI、伤害、装备、外观数据、目录 schema 与存档；
- `assets/source/characters/platform-evaluation/`、Meshy／Tripo 槽位、60 分钟平台计时、评分与胜负结论；
- 正式模块化身体、脸型、发型、服装、贴图变体、武器和法宝生产；
- Rigify、Mixamo、第三方自动绑定服务、平台骨架映射表、第二套权重算法或兼容层。

### 3.3 全局停止规则

任一任务失败时停止依赖链，保留已有证据并报告根因；不得跳过失败切片继续，不得增加重试层或替代骨架。已经通过且输入未变化的检查不在后续切片重复运行。

## 四、任务明细

### HUM-EVAL-01：工具链、授权与写入闸门

**目标：** 只在可靠 DCC 和项目写入条件成立后创建源资产目录。

**前置事实：** Unity `6000.3.18f1` 已安装于 `C:\Program Files\Unity\Hub\Editor\6000.3.18f1\Editor\Unity.exe`；当前未发现 Blender。

**步骤：**

1. 重新运行 `git status --short`、schema 5 `Show` 和集成锁检查；存在活动 run 时只在独立 worktree 处理受追踪文档，忽略目录中的资产不得与其他任务共用编辑 owner。
2. 再次检查 PATH、官方安装位置与已批准便携位置是否已有 Blender；不得凭历史调查直接断言仍不存在。
3. 若仍不存在，停止并取得用户对下载／安装官方稳定 Blender 的明确授权。不得自动改用非官方镜像、在线 DCC、Rigify 或平台骨架。
4. 获得授权后，只从 Blender 官方来源取得当前稳定版本；记录下载 URL、精确版本、安装包或便携包 SHA-256、许可说明和最终 `blender.exe` 绝对路径。实施批次内不升级版本。
5. 使用 factory startup 做后台探针，证明 Blender 可无界面启动、Python API 可用、FBX 导出操作存在；不创建资产文件作为探针。
6. 只有全部探针通过后才创建 `assets/source/characters/humanoid-evaluation/` 及当前任务实际需要的子目录，不预建平台目录或其他角色目录；把工具来源、版本、SHA-256、可执行路径和探针输出写入 `evidence/blender-validation.txt` 的工具链区段，HUM-EVAL-05 在同一文件中补齐资产验证结果。

**最小验证：**

```powershell
& '<validated-blender.exe>' --background --factory-startup --version
& '<validated-blender.exe>' --background --factory-startup --python-expr "import bpy; print(bpy.app.version_string); print(bpy.ops.export_scene.fbx.get_rna_type().identifier)"
```

**通过证据：** 精确 Blender 版本、来源、SHA-256、可执行路径和两条探针输出均已记录；Unity 路径仍指向 `6000.3.18f1`。

**停止条件：** 无可靠 Blender、来源或许可无法确认、FBX 导出不可用、活动 run／锁／路径冲突无法隔离、必须改用非批准工具。

### HUM-EVAL-02：建立唯一 Armature 与空间合同

**依赖：** HUM-EVAL-01 通过。

**目标输出：** `assets/source/characters/humanoid-evaluation/TZ_HumanoidEval_v001.blend` 的骨架基线。

**步骤：**

1. 从 factory startup 新建 Blender 工程，使用公制与 `1 unit = 1 m`，场景 30 FPS。
2. 创建唯一导出 Armature `TZ_HumanoidEval`，严格建立设计文档第 5.1 节的 39 骨层级：22 根 Humanoid 映射骨、16 根 `Sec_` 骨和不映射的 `Root`。
3. 固定 T Pose、Blender `-Y` 正面、`+Z` 上方、`Root=(0,0,0)` 和 1.70 m／约 4.75 头身校准点；应用对象变换，使 Location／Rotation 为零、Scale 为一。
4. 一次性确定 bone roll、骨长和全部 rest matrix；不得为性别或后续候选调整。
5. 在 `.blend` 内建立 `TZ_Validate` Text 数据块，至少断言单位、FPS、Armature 数量与名称、39 骨名称／父级、`Root` 位置、对象变换、骨骼 rest matrix 快照和禁止骨名前缀。验证逻辑不参与导出。
6. 保存 `.blend` 后关闭并以后台模式重开一次，排除只存在于未保存会话的状态。

**验证命令形态：**

```powershell
& '<validated-blender.exe>' --background '<absolute-path>\TZ_HumanoidEval_v001.blend' --python-expr "import bpy; exec(bpy.data.texts['TZ_Validate'].as_string())"
```

**通过条件：** 单一 Armature、39 骨、层级／命名／rest matrix／空间合同全部通过；没有 IK、控制、Twist、手指、面部、器物、角色专用或兼容骨。

**停止条件：** 需要 Rigify 或控制层才能建立；Unity 必查骨无法按固定层级表达；无法冻结一致 bone roll／rest matrix；需要改变已批准比例或空间合同。

### HUM-EVAL-03：制作男女校准体型与统一蒙皮

**依赖：** HUM-EVAL-02 通过且骨架 rest matrix 未变化。

**目标输出：** 同一 `.blend` 中的 `CAL_Male`、`CAL_Female` 与压力测试部件。

**步骤：**

1. 创建男、女两个独立显示集合；每个集合总计约 3,000～5,000 三角面，身高同为 1.70 m，骨长和关节位置完全相同，只改变表面体积。
2. 两体型均提供简化宽袖、长袍下摆和后发束；男体增加短须测试片。只使用纯色材质，不制作身份化脸、UV、贴图或正式服装。
3. 两体型的所有 Skinned Mesh 只引用 `TZ_HumanoidEval`；不得复制 Armature。
4. 临时关闭 `Sec_` Deform，以 Blender `With Automatic Weights` 建立核心骨基线，再按设计固定渐变处理袖、下摆、发和须。
5. 权重归一化，每顶点最多四骨、无未加权或负权重；没有对应部件时相应 `Sec_` 权重为零，全部 `Sec_` 权重合计不超过 `0.7`。
6. 更新 `TZ_Validate`，加入集合、三角面范围、Armature Modifier 目标、权重上限、未加权点和 `Sec_` 影响区域检查；不得改变 HUM-EVAL-02 的骨架快照。

**通过条件：** 两种表面体型共享同一 Armature 对象和完全相同的 rest skeleton；后台验证全部通过，正交正／侧视可确认脚底、关节和 T Pose 对齐。

**停止条件：** 任一体型需要不同骨长／rest pose；必须增加 Twist／纠正／角色专用骨；必须完整手绘全身权重或引入形态键才能达到校准用途。

### HUM-EVAL-04：制作三条 30 FPS 非正式评估动作

**依赖：** HUM-EVAL-03 通过。

**目标输出：** 同一 `.blend` 中的 `TZ_Eval_Idle_v001`、`TZ_Eval_Move_v001`、`TZ_Eval_Attack_v001`。

**步骤：**

1. 只用固定骨架 FK 关键帧制作动作，不增加 IK、约束、控制骨、Root Motion、物理、形态键或 AnimatorController。
2. Idle 固定 `1–61` 帧并循环；Move 固定 `1–31` 帧并原地循环；Attack 固定 `1–46` 帧且非循环。关键姿势、角度上限和检查意图逐项按设计第七章执行。
3. `Root` 不建立变化曲线；除 `Hips` 最多 3 cm 平移外只记录旋转，不记录缩放曲线。
4. `Sec_` 骨只添加设计允许的轻微延迟摆动，不追求正式布料或发须动画。
5. 在男、女校准体型上逐条播放，检查肩、肘、腕、髋、膝、袖、下摆、发和须；只修局部权重或动作关键帧，不改变骨架合同。
6. 更新 `TZ_Validate`，断言 Action 名称、帧范围、循环首尾、Root 曲线、缩放曲线、Hips 位移和关键角度上限。

**通过条件：** 两体型可直接播放相同三个 Action；Idle／Move 首尾一致，Attack 非循环；不存在第二套动作、Armature、Avatar 或性别分支。

**停止条件：** 任何动作必须依赖正式 Animator、IK、Root Motion、运行时代码或不同性别动作才能成立；需要改骨架或完整重绘权重。

### HUM-EVAL-05：确定性 FBX 导出与 Blender 侧证据

**依赖：** HUM-EVAL-04 通过且 `.blend` 输入冻结。

**目标输出：** `exports/` 下四个固定 FBX，以及 `evidence/blender-validation.txt`。

**步骤：**

1. Rig FBX 只导出唯一 Armature 和两个校准体型；三个动作 FBX 各只导出相同 Armature 与当前 Action，不携带校准网格。
2. 四次导出全部使用 Binary、Scale 1.0、Apply Scalings=`FBX Units Scale`、Forward=`-Z`、Up=`Y`、Apply Transform 关闭、Add Leaf Bones 关闭、Only Deform Bones 关闭；动画 30 FPS 烘焙并关闭 All Actions。
3. 不导出相机、灯光、辅助对象、Text 数据块或任何平台诊断集合；不试探第二套导出预设。
4. 将四个 FBX 分别重新导入 factory-startup 的空 Blender 会话，验证骨名、层级、骨数、动作范围、对象缩放、原点和正面；失败时先判定是源文件还是固定导出预设根因。
5. 运行 `.blend` 内 `TZ_Validate` 最终检查；记录 Blender 精确版本、四文件 SHA-256、骨架摘要、动作摘要、回读结果和未解决风险到 `blender-validation.txt`。

**通过条件：** 四个文件均存在且非空；回读后保持 39 骨与固定层级，Rig 含两体型，动作 FBX 各只含一个正确 Action；单位／原点／朝向可进入 Unity 验证。

**停止条件：** 固定预设不能保持任一合同；必须开启 Apply Transform、切换 Apply Scalings、改变骨架或使用第二个导出流程才能通过。

### HUM-EVAL-06：一次性 Unity 6000.3.18f1 Humanoid 验证

**依赖：** HUM-EVAL-05 通过；四个 FBX SHA-256 已冻结。

**目标输出：** `evidence/unity-humanoid-validation.txt` 与 `evidence/calibration-contact-sheet.png`；不保留临时 Unity 项目。

**步骤：**

1. 执行前确认 `C:\Program Files\Unity\Hub\Editor\6000.3.18f1\Editor\Unity.exe` 仍存在且没有会阻塞 batchmode 的同项目 Unity 会话。
2. 在操作系统临时根目录创建唯一 `tzg-humanoid-eval-unity-<guid>`，确认其解析后绝对路径位于系统临时目录内且末级名称匹配该前缀；不得把临时项目建在仓库、用户主目录根或 `src/` 内。
3. 用 Unity `6000.3.18f1` 创建临时项目，将冻结的四个 FBX 复制到临时 `Assets/Evaluation/`。只在临时项目创建 Editor 验证脚本和临时验证场景。
4. Rig FBX 设置为 Humanoid／Create From This Model；三个动作设置为 Humanoid／Copy From Other Avatar，并全部指向 Rig Avatar。
5. Editor 验证脚本断言：`Avatar.isValid`、`Avatar.isHuman`、22 项项目必查映射、Root 与 16 根 `Sec_` Transform 保留但未映射、三个 `AnimationClip.isHumanMotion`、时长约 2／1／1.5 秒、同一 Avatar、两体型同一骨架路径、身高约 1.70 m、脚底中心原点与本地 `+Z` 正面。
6. 通过 Playables 或直接采样 Clip 为男女各输出设计第 14.1 节的固定姿势：Idle 左／右重心和呼吸最高点，Move 左／右接触与两次过渡，Attack 蓄力／释放／最大随动／收势；合成为 contact sheet，不得创建 AnimatorController。
7. 将批处理日志、映射表、四个输入 FBX 的 SHA-256、截图帧位和结果写回源目录 evidence，并与 HUM-EVAL-05 冻结的哈希逐一相等；证据成功复制且可读取后，按第 2 步验证过的绝对路径安全删除临时项目。

**批处理命令形态：**

```powershell
& 'C:\Program Files\Unity\Hub\Editor\6000.3.18f1\Editor\Unity.exe' -batchmode -nographics -quit -projectPath '<validated-temp-project>' -executeMethod '<temporary-editor-validator-method>' -logFile '<validated-temp-log>'
```

**通过条件：** 全部 Humanoid、骨骼、动作、体型、原点、正面和 contact sheet 检查通过；`src/` 的 Git 状态在任务前后完全一致；临时项目已安全删除。

**停止条件：** Avatar 无效／非 Human、任一映射缺失、`Sec_` 导致失效或丢失、动作不能复用同一 Avatar、男女需要不同 Avatar／Animator、必须写入正式 Unity 项目才能证明。

### HUM-EVAL-07：证据签收、备份核对与实施停止

**依赖：** HUM-EVAL-06 通过，且四个 FBX 与 `.blend` 自验证后未变化。

**目标：** 确认评估骨架前置任务完成，但不把完成自动解释为平台 A/B 授权。

**步骤：**

1. 核对最小交付树，只允许设计批准的 `.blend`、四个 FBX 和三份 evidence；不为“以后可能需要”创建目录或占位资源。
2. 对 `.blend`、四个 FBX 和 contact sheet 计算 SHA-256，核对 Blender 与 Unity 证据引用的是同一批输入。
3. 只读取既有通过证据，不重复运行输入未变化的 Blender／Unity 全套检查；若哈希变化则回到实际受影响任务，不直接重跑所有步骤。
4. 按美术资源版本管理规范完成 `assets/source/` 外部备份核对，记录备份批次和文件大小／SHA-256；不把原始目录强行加入 Git。
5. 最终报告明确区分“评估骨架／评估动作”与未来正式骨架、动画、Animator、模块化角色和器物表现。
6. 停止。只有用户随后明确批准，才另按平台评估设计开始 Meshy／Tripo 正式槽位、60 分钟计时与胜负判断。

**通过条件：** 设计第十二至十四章的全部验收有可重复证据；没有未说明风险；正式 Unity 和游戏规则路径零改动；A/B 未启动。

**停止条件：** 证据哈希不一致、临时项目残留、正式 Unity 路径发生变化、备份无法核对、任一上游任务实际未通过。

## 五、任务领取与复审规则

1. 每次只领取依赖已通过的第一项任务；同一 `.blend` 不允许多 owner 并行编辑。
2. 每项开始前重读设计中的对应完整章节、检查 Git／schema 5／集成锁和实际工具状态。
3. 执行者只能修改任务允许路径；发现需要改设计、增加骨骼、改变算法或写入正式 Unity 时立即停止并交回用户。
4. 资产任务的完成证据是实际 `.blend`／FBX／Unity 批处理结果，不是文本声明或 DeepSeek 审核意见。
5. 若由 DeepSeek 或其他外部执行器实施，完成后必须由 Codex 独立复审；执行者不得自审。
6. 本计划不创建或修改 `开发管理/任务卡`、当前队列或 backlog。需要正式调度时另行建立与当时状态一致的任务卡，不从本计划推导“已经获得队列主责”。

## 六、计划自检

- 七个任务覆盖工具、骨架、双体型、三动作、FBX、Unity Humanoid 和最终证据，没有把 Meshy／Tripo A/B 混入前置实施。
- 所有资产输出都位于 `assets/source/characters/humanoid-evaluation/`；正式 `src/Assets/` 保持零写入。
- Blender 与 Unity 各有独立机器可执行证明；实际视觉截图不能被静态文本检查替代。
- 男女体型、三个 Action 和四个 FBX 都依赖唯一 `TZ_HumanoidEval`，不复制 Avatar、Animator 或规则状态。
- 每个任务都有前置、通过条件与停止条件；失败不会自动切换工具、骨架、导出预设或权重算法。
- 本计划没有占位任务、未定义目录、平台 credits 或长期双平台生产步骤。
