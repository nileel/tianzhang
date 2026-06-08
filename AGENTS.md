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
- 可调参数在 Program.cs 顶部 GameData 类中：BaseGainPerCycle(8.0)、CultivationCycles(100)、BreakthroughBaseRate(0.70)、根骨HP衰减指数(0.75)。
- 数值设计完整文档：`docs/基础设定/角色数值设计.txt`（v3.1版，含次线性HP公式+二级属性重映射）。

# 工作规则
- 思考时尽量使用中文进行推理和分析。
- 设计生成时，非规则描述类的设计内容完成品（如术法、神通、功法、角色等）每个都单独一个文件，不要合并。