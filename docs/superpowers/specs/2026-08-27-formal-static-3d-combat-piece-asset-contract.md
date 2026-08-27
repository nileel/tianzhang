# 正式玩家与石甲兽静态 3D 战斗棋子资产合同

状态：2026-08-27 冻结。本文是首批两项正式静态 3D 战斗棋子资产的共同生产与批准输入；它不生产资产、不启动 Blender、不导入 Unity，也不建立正式场景接线。

## 一、范围、当前事实与唯一生产路线

本合同只覆盖首次正式关中遭遇的两项静态 3D 棋子：稳定来源身份 `player` 与 `enemy_shijiahou`。现行 Adventure 在玩家快照上固定生成 `player`，并从 `guanzhong_wild` encounter 的内容 ID 解析唯一石甲兽；该遭遇必须同时具备两项表现资料，不能用对象名或颜色推断身份。

当前生产内容中的 `AppearanceProfileData` 只有合法的 `none`，且下列两项 source 目录和后续 Unity 目录均不存在。因此 `combat_player_default_v1` 与 `combat_enemy_shijiahou_v1` 是本合同冻结的载体中立战斗表现 ID，不是现有外观档案、GameObject 名称、阵营颜色或 Prefab 名称。

唯一生产路线为：每项资产在本地 Blender 中原生建模和材质整理，导出一次冻结 FBX，再以 factory-empty Blender FBX 回读作 QA。factory-empty 回读是同一 Blender 路线的验证步骤，不是另一条资产来源或失败替代路线。模型生产、Blender 操作和 FBX 验证均遵守 `tianzhang-blender-pipeline`：写入前确认精确绝对路径，未通过时停止，不叠加重试、第二工具路线或外部资产来源。

本卡不授权任何外部平台、上传、下载或 credits 消费。若后续负责人要求以外部 credits 取得任一原始输入，必须先在该资产的 manifest 中记录一次独立人工授权：负责人、日期时间、平台、模型或服务、允许的最大 credits、唯一输入哈希和对应资产 ID。该记录必须在交易前获得批准；没有完整记录即不得发起交易，也不得以免费重试、相近模型或旧文件代替。

## 二、稳定身份、Profile 与视觉边界

| 资产 | 稳定来源身份与事实 | 固定 `presentationProfileId` | 视觉边界 |
| --- | --- | --- | --- |
| 正式玩家 | `player`；来源为当前玩家只读角色快照，不读取未建立的外观档案，也不指向任何既有命名 NPC。关中薄切片只以 `origin_minor_clan` 验证玩家进入 `guanzhong_wild` 的既有通路。 | `combat_player_default_v1` | 未命名的成年修士棋偶，代表关中小族出身的默认玩家，而非任何固定主角、肖像或可选外观。轮廓是紧凑直立人形、宽而连贯的简朴旅行袍、可见双手和双足；不得持武器、佩戴王冠、盔甲、翼状附件、漂浮器物、光环或脱离身体的能量效果。脸部、发型和服饰保持中性、克制且不可识别为苻渊或其他既有角色。 |
| 石甲兽 | `enemy_shijiahou`；关中野外唯一正式敌人，练气期低阶妖兽，厚甲、迟缓、近战、格挡倾向，`ai_melee`，无已装备术法或神通。 | `combat_enemy_shijiahou_v1` | 低伏、重型的四足妖兽棋偶。必须具有可独立辨识的头部、躯干、尾部、四条承重足和成层石甲；其厚重、迟缓、近战与防御识别来自轮廓和材质，而非阵营色。不得成为人形、双足怪、机械、骑乘单位或带人形统一骨架的模型；不得加入武器、法器、翼、浮空部件、发光甲缝或独立特效。 |

两项资产共享低饱和、哑光、手绘大色块的棋偶风格，并只使用二至三档 Toon 明暗。玩家使用布料、皮革与少量旧铜的可区分粗糙度；石甲兽使用深灰、土褐与风化石面的可区分粗糙度。颜色只服务材质和轮廓，不承载玩家／敌人身份、规则状态或 profile 选择。每项资产只允许一个不透明、非发光的 BaseColor 贴图和一个命名材质；透明、镜面、复杂粒子、光环和额外材质图不在本合同范围内。

## 三、冻结文件与命名

每个资产卡只能写入下表所属的 source 文件、对应 QA 记录以及自己的任务管理路径。`manifest` 必须记录所有输入与输出的 SHA-256、实际 Blender 版本、`sourceRoute=blender_native`、BaseColor 与 FBX 哈希、导出参数、坐标测量、回读结果和人工批准证据。除非先按本合同的 credits 门取得记录，`creditsUsed=0`、`externalSourceUsed=false` 和 `generationAttempts=0`。

| 资产 | 冻结 source 目录与文件 | 后续 Unity 原样导入目标 |
| --- | --- | --- |
| 正式玩家 | `assets/source/characters/combat-pieces/formal-player-static-3d-v1/formal_player_static3d_v1.blend`；`assets/source/characters/combat-pieces/formal-player-static-3d-v1/formal_player_static3d_v1.fbx`；`assets/source/characters/combat-pieces/formal-player-static-3d-v1/formal_player_static3d_v1_basecolor.png`；`assets/source/characters/combat-pieces/formal-player-static-3d-v1/formal_player_static3d_v1_manifest.json`；`assets/source/characters/combat-pieces/formal-player-static-3d-v1/formal_player_static3d_v1_contact-sheet.png`；`开发管理/正式玩家静态3D战斗棋子资产QA记录.txt` | `src/Assets/Art/Characters/CombatPieces/Static3D/FormalPlayer/FormalPlayer_Static3D.fbx`；`src/Assets/Art/Characters/CombatPieces/Static3D/FormalPlayer/FormalPlayer_Static3D_BaseColor.png`；`src/Assets/Art/Characters/CombatPieces/Static3D/FormalPlayer/FormalPlayer_Static3D.mat`；`src/Assets/Art/Characters/CombatPieces/Static3D/FormalPlayer/FormalPlayer_Static3D.prefab` |
| 石甲兽 | `assets/source/characters/combat-pieces/shijiahou-static-3d-v1/shijiahou_static3d_v1.blend`；`assets/source/characters/combat-pieces/shijiahou-static-3d-v1/shijiahou_static3d_v1.fbx`；`assets/source/characters/combat-pieces/shijiahou-static-3d-v1/shijiahou_static3d_v1_basecolor.png`；`assets/source/characters/combat-pieces/shijiahou-static-3d-v1/shijiahou_static3d_v1_manifest.json`；`assets/source/characters/combat-pieces/shijiahou-static-3d-v1/shijiahou_static3d_v1_contact-sheet.png`；`开发管理/石甲兽静态3D战斗棋子资产QA记录.txt` | `src/Assets/Art/Characters/CombatPieces/Static3D/Shijiahou/Shijiahou_Static3D.fbx`；`src/Assets/Art/Characters/CombatPieces/Static3D/Shijiahou/Shijiahou_Static3D_BaseColor.png`；`src/Assets/Art/Characters/CombatPieces/Static3D/Shijiahou/Shijiahou_Static3D.mat`；`src/Assets/Art/Characters/CombatPieces/Static3D/Shijiahou/Shijiahou_Static3D.prefab` |

文件名、目录名、Profile ID 和来源身份均为固定 ASCII 标识。后续 `U-CHAR-BATTLE-STATIC3D-PROFILES-01` 只可建立 `player -> combat_player_default_v1` 与 `enemy_shijiahou -> combat_enemy_shijiahou_v1` 两条内容映射；它只能引用本表的两项已批准 Prefab，且不在本卡提前创建 Unity 文件。

## 四、静态网格、材质与坐标合同

### 4.1 网格和材质

- Blender 文件各有一个根节点：`FormalPlayer_Static3D_Root` 或 `Shijiahou_Static3D_Root`；FBX 只导出该根及其静态网格。根下不得有 Armature、Avatar、Animator、动作、形态键、骨骼权重、碰撞体、导航、脚本、游戏规则字段、底座、特效或角色状态。
- 正式玩家使用唯一材质 `FormalPlayer_Static3D_Mat`，只读取 `formal_player_static3d_v1_basecolor.png`；石甲兽使用唯一材质 `Shijiahou_Static3D_Mat`，只读取 `shijiahou_static3d_v1_basecolor.png`。材质必须为不透明、非发光、无透明裁切的静态材质，不能以未贴图颜色、相近贴图或 Shader 特效替代。
- 玩家最高点为本地 `Y=1.03 m`，水平包络不超过 `X=±0.30 m`、`Z=±0.30 m`。石甲兽的足底到最高甲片为本地 `Y=0.78 m`，水平包络不超过 `X=±0.42 m`、`Z=-0.48..0.58 m`。所有数值以 root 的局部米制坐标测量，不得通过 Unity 缩放或场景偏移修正。
- 玩家双足和石甲兽四足的最低承重面均在局部 `Y=0`；根节点位于其占地投影中心 `(0,0,0)`。人物足底或四足中任一足悬空、网格穿过 `Y=0`、以底座垫高或以地块下沉补偿，均为失败。

### 4.2 导出、Unity 轴与六向

- 冻结的资产坐标为 `+Y` 上方、局部 `+Z` 正面，FBX 根节点位置 `(0,0,0)`、旋转 `(0,0,0)`、缩放 `(1,1,1)`。Blender 的作者视图可使用其原生轴，但导出和 factory-empty 回读必须证明上述资产坐标；不得把作者视图轴当作验收坐标。
- FBX 只可在冻结源通过后导出一次。Unity 后续导入的 Prefab 根与模型子节点均保持 Position `(0,0,0)`、Rotation `(0,0,0)`、Scale `(1,1,1)`；不复用任何历史资产的导入轴补偿、按角色补偿、镜像、缩放或地块偏移。
- 六个规则朝向只旋转表现根节点的 Y 轴，索引 `0–5` 严格为 `90/150/210/270/330/30` 度。每个角度的本地 `+Z` 对准对应规则邻格中心；模型、BaseColor、底座、相机、地块和每个实例都不得加私有 yaw、镜像或重排。
- 棋子根只消费已经确定的格位、Facing 和表现事件；源模型保持静态。允许的运行时移动、攻击、受击、施法和死亡表现只由后续表现根和一次性特效承担，且不得回写位置、朝向、伤害、AI、结算、存档或占格。模型内部动画、Root Motion、多套 Pose 换模和统一骨架不被授权。

## 五、来源、回读与人工批准矩阵

每个资产必须在其 QA 记录中逐项登记实际文件路径、SHA-256、工具版本、检查者、日期时间、命令或截图证据和结论。哈希、文件名和来源的不一致不能用口头说明修复；应停止该资产卡，保留失败层证据，并且双 Profile 总门保持阻塞。

| 硬门 | 正式玩家必须证明 | 石甲兽必须证明 |
| --- | --- | --- |
| 来源与哈希 | `player` 来源、`combat_player_default_v1`、全部 source 文件、BaseColor、FBX 和 manifest 哈希一致；未使用命名角色、肖像或外部输入。 | `enemy_shijiahou` 来源、`combat_enemy_shijiahou_v1`、全部 source 文件、BaseColor、FBX 和 manifest 哈希一致；未使用相近怪物或人形输入。 |
| Blender／FBX | 唯一 Blender 原生路线，静态网格、唯一材质、坐标、单位缩放和 factory-empty 回读全部通过。 | 唯一 Blender 原生路线，静态四足网格、唯一材质、坐标、单位缩放和 factory-empty 回读全部通过。 |
| 接地与朝向 | 双足 `Y=0`、根居中、局部 `+Z` 正面，以及六个固定 yaw 均通过。 | 四足 `Y=0`、根居中、局部 `+Z` 正面，以及六个固定 yaw 均通过。 |
| 轮廓与材质 | 固定正、左、右、背和六向联系表显示中性修士棋偶、可见手足、连贯袍体和哑光材质。 | 固定正、左、右、背和六向联系表显示重型四足、四个承重足、成层石甲和哑光材质。 |
| 负责人视觉批准 | 负责人对身份、轮廓、材质、尺度、接地和六向可读性签署明确通过。 | 负责人对石甲兽身份、重型四足轮廓、材质、尺度、接地和六向可读性签署明确通过。 |

联系表必须在相同固定相机和光照下包含正、左、右、背、以及全部六个指定 yaw；它不能以单帧、命名、颜色、文字注释或自动脚本替代视觉批准。自动检查只证明文件、哈希、网格、材质和坐标；负责人视觉结论仍不可替代。

两项资产的所有硬门和人工签署都通过前，`U-CHAR-BATTLE-STATIC3D-PROFILES-01` 不得开始、不得部分创建 profile 映射、不得导入单项 Prefab。该总门只在两个 profile 同时可解析、两项来源和 QA 同时通过后才可解除；任意一方缺失、拒绝或哈希变化时保持阻塞并报告具体来源身份、profile ID 和失败原因。

## 六、禁止项、失败关闭与后续边界

- `FuYuan_StaticChess` 仅是隔离比较样例，绝不被授权为本合同的来源、网格、材质、贴图、FBX、Prefab、profile、输入或 fallback。
- `U-CHAR-3D-FORMAL-01` 是冻结的旧动画 3D 路线，绝不被授权为本合同的资产输入、验证依据或后续接线依据。
- 技术 Marker、`UnitMarker.prefab`、对象名、阵营颜色、临时敌人名、正式 2D fallback、相近模型、旧动画 3D、统一骨架和第二正式提供器均不得替代任一正式 profile 或资产。
- 不在本合同中修改 `src/Assets/`、Adventure、Combat、GameplayContracts、provider、正式场景、数值、AI、存档或 Build Settings；正式 Unity 导入和两层 profile 映射只属于双资产 QA 完成后的后续卡。
- 身份、视觉边界、来源授权、材质、尺度、接地、导出轴、六向、哈希、factory-empty 回读或负责人视觉结论任一不明确或失败时，停止该资产生产。不得以补丁、二次生成、私有旋转、额外底座、技术对象、另一表现路线或战斗规则改动掩盖问题。

本合同只给 `A-CHAR-BATTLE-STATIC3D-PLAYER-ASSET-01` 与 `A-CHAR-BATTLE-STATIC3D-SHIJIAHOU-ASSET-01` 提供共同冻结输入。两卡各自完成并取得负责人批准后，唯一后继是双输入 `U-CHAR-BATTLE-STATIC3D-PROFILES-01`；正式 provider 与 Adventure 接线仍须等待该总门和载体中立合同，不在此处提前发生。
