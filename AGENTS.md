# 设定来源
- 讨论或修改任何设定时，设定原文默认来源于 docs/ 下各子文件夹中的 .txt 文件。

# 游戏类型
- 2D 沙盒世界，战棋玩法的修仙游戏。
- 参考游戏：《龙胤立志传》、《鬼谷八荒》、《觅长生》。

# 设计规范
- 设计功法、术法、神通等游戏内容时，必须先查阅对应的设计规范文档：
  - 功法：`docs/角色养成/功法设计规范.txt`（约束 + 检查清单）+ `docs/角色养成/功法/功法设计.txt`（模版字段说明）
  - 术法：`docs/角色养成/术法设计规范.txt`（约束 + 检查清单）+ `docs/角色养成/术法/术法设计.txt`（模版字段说明）
  - 神通：`docs/角色养成/神通设计规范.txt`（约束 + 检查清单）+ `docs/角色养成/神通/神通设计.txt`（模版字段说明）
# 数值模拟
- 讨论或验证角色数值平衡时，必须运行战斗模拟器：
  - 位置：`simulations/BattleSim/Program.cs`（.NET 10 项目，零依赖）
  - 运行：`dotnet run --no-build -c Release --project "D:\天章游戏开发\simulations\BattleSim"`
  - 首次或无变更时须先编译：`dotnet build -c Release --no-restore "D:\天章游戏开发\simulations\BattleSim"`
- 模拟器包含：修炼引擎（100轮从凡人出发）+ CTB战斗引擎（格挡/魂盾/闪避/暴击/抗性）+ 10Build胜率矩阵。
- 可调参数在 Program.cs 顶部 GameData 类中：BaseGainPerCycle(10.0)、CultivationCycles(200)、BreakthroughBaseRate(0.70)、根骨HP衰减指数(0.75)。
- 数值设计完整文档：`docs/基础设定/角色数值设计.txt`（v3.1版，含次线性HP公式+二级属性重映射）。

# 工作规则
- 思考时尽量使用中文进行推理和分析。
- 设计生成时，非规则描述类的设计内容完成品（如术法、神通、功法、角色等）每个都单独一个文件，不要合并。
# 技术经验

- **文件编码陷阱**：Program.cs 和多数 docs/*.txt 是 GBK 编码而非 UTF-8。编辑时不能直接 ReadAllText/WriteAllText，必须用 [System.Text.Encoding]::GetEncoding('gbk') 的字节级往返（读字节→GBK解码→编辑→GBK编码→写字节）。一旦用 UTF-8 写入，中文全部变 �，文件报废。
- **String.Replace 风险**：在 PowerShell 中对整个文件做字符串替换容易命中多处匹配，导致结构损坏。优先用行号定位或锚定唯一上下文。
- **模拟器即真理**：任何数值平衡讨论必须先跑 dotnet run -c Release --project simulations/BattleSim 看矩阵输出，不要凭感觉判断。
- **文档跟着模拟器走**：模拟器参数调完，必须同步更新对应的 docs 设计文档（术法/神通倍率等），否则下次对话会出现两边不一致。

# 当前状态

- **模拟器已接入术法/神通 AI**：神通(CD5)>术法(CD3,MP20)>平A，法系有远程优势（对方下轮伤害×0.35）
- **回合数**：金丹同境 6.8 回合（9 Build），筑基同境 3.0 回合，练气快照 4.4 回合（40轮快照，9 Build）
- **物/法平衡**：9×9矩阵，物均衡 vs 法均衡 EV 47%。新增水·散修（water_physical泳道）
- **SubGrowth 已对称化**：肉攻=神攻、肉防=神防，物法副属性比率接近一致
- **术法/神通已拆分单文件**、合规检查完毕并修复。散修新增川流劲（术法）+ 逝水千击（神通），与秋水游心经形成水系Build
- **神通设计规范**：已修正"本命神通无冷却"→"冷却5回合"以匹配模拟器
- **术法/神通参数**：已从 AssignArts() 提取到 GameData 顶部（ArtConfig/DivineConfig 记录）
- **练气对战**：已加入40轮快照模式，Cultivation.Simulate 支持 maxCycles 可选参数
- **上次提交**：495462f — 21 files, +601/-350

# 下一步建议

1. **引入丹药/天材地宝突破加成**：低资质Build（资质3/5）卡筑基需外部机制弥补，可在模拟器中为 Cultivation.Simulate 添加 treasureGrade 参数提升突破概率。
2. **战斗引擎接入水系机制**：川流之势减伤、逝水印记叠防、秋水回血护盾等特色机制尚未在 Combat 引擎中实现。