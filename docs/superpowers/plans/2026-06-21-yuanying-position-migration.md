# Yuanying Position Migration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` to execute this plan task-by-task, with review checkpoints after each task.

**Goal:** 将已确认的“五根系、十七元婴分支、每分支源/化/界三席”落实为金丹位格唯一事实源，并按可验证切片迁移核心规则、重要 NPC、批量生产接口与工程数据契约。

**Architecture:** 化神只统摄五根系；元婴只统摄一条分支；金丹只占据该分支的一席源、化或界。源、化、界是并列功能席位；夺取、继承、敕封、暂寄、自辟只是占据方式或状态。通用效果只写入矩阵，人物差异只写入丹枢；任何门派不得完整垄断一条分支。

**Tech Stack:** UTF-8 TXT 设定文档、PowerShell、ripgrep、既有文本/数据链路检查脚本、.NET BattleSim、Unity C# 数据模型。

---

## 任务边界与依赖

| 任务 | 队列 ID | 主责 | 前置 | 交付物 |
|------|---------|------|------|--------|
| 1. 锁定效果事实源 | TQ-015A | Codex / ChatGPT5.5 | 用户确认 | 17 分支 × 51 席位效果矩阵与入口说明 |
| 2. 核心规则迁移 | TQ-015B-1 | Codex / ChatGPT5.5 | 任务 1 | 境界、槽位、设计规范与模板不再使用正/辅/敕/寄层级 |
| 3. 重要 NPC 迁移 | TQ-015B-2 | DeepSeek V4 Pro 起草，Codex / ChatGPT5.5 复审 | 任务 1、2 | 13 份 NPC 独立档案按源化界状态和丹枢表达 |
| 4. 批量生产包 | TQ-015B-3 | DeepSeek V4 Pro | 任务 1、3 | 可机械扩展的角色/丹枢/位格模板与交接说明 |
| 5. 数据模型与高阶清理方案 | TQ-015C | Codex / ChatGPT5.5 | 任务 2、4 | BattleSim/Unity/CSV 字段差异表与炼虚迁移清单 |

正式名称尚未批准。任务 1—5 都只能使用 `根系·分支·源/化/界` 临时代号，不得为金丹席位、元婴分支或化神根系自行取正式名。

## 任务 1：锁定效果事实源（TQ-015A）

**文件：**
- 新增：`docs/基础设定/元婴锚点与金丹位格矩阵.txt`
- 修改：`docs/基础设定/元婴锚点与金丹位格设定.txt`
- 修改：`docs/剧情/背景与重要NPC设计规范.txt`
- 修改：`开发管理/境界体系重构锁口径决策表.txt`
- 修改：`开发管理/设计-当前状态.txt`

**实施：**

1. 固定五根系：五行、阴阳、宇宙、五蕴、因果；固定十七条元婴分支。
2. 为每一分支写满源、化、界三席：唯一世界变量、作用对象、无效边界、持续代价、协同/不可替代项、允许丹枢个性化的字段。
3. 在入口说明中固定“源=能改什么、化=怎么改、界=改完留下什么”，并废止正籍、辅籍、敕籍、寄丹作为层级的旧口径。
4. 将人物规范改为“分支 + 源/化/界席位 + 占据方式 + 丹枢”，禁止以门派名称或旧丹籍层级代替位格。

**验证：**

```powershell
rg -n "^### (木|火|土|金|水|阴|阳|宇|宙|色|受|想|行|识|业|缘|轮回)$|源位：|化位：|界位：" "docs/基础设定/元婴锚点与金丹位格矩阵.txt"
powershell -ExecutionPolicy Bypass -File tools/check-review-text.ps1 -Paths docs/基础设定/元婴锚点与金丹位格矩阵.txt,docs/基础设定/元婴锚点与金丹位格设定.txt,docs/剧情/背景与重要NPC设计规范.txt
git diff --check
```

**完成条件：** 矩阵可数出 17 个分支、每分支恰有源/化/界三席；所有席位具备六类落地字段；入口文档和 NPC 规范无“一正二辅”正向规则。

## 任务 2：核心规则迁移（TQ-015B-1）

**文件：**
- 修改：`docs/基础设定/修行境界.txt`
- 修改：`docs/基础设定/境界特性.txt`
- 修改：`docs/角色养成/术法槽位设计.txt`
- 修改：`docs/角色养成/功法设计规范.txt`
- 修改：`docs/角色养成/术法设计规范.txt`
- 修改：`docs/角色养成/神通设计规范.txt`
- 修改：`docs/角色养成/功法/功法设计.txt`
- 修改：`docs/角色养成/术法/术法设计.txt`
- 修改：`docs/角色养成/神通/神通设计.txt`

**实施：**

1. 将金丹核心表达统一为“位格 + 丹名 + 丹性”；位格只允许指向具体分支的源、化、界席位。
2. 将旧正籍/辅籍/敕籍/寄丹替换为席位或占据状态；只有在“旧口径已废止”的历史说明中保留旧词。
3. 将槽位、功法、术法、神通模板的适配字段统一改为“元婴锚点 / 目标源化界席位 / 丹枢接口 / 根本神通闭环”。
4. 不擅自确定目标席位的成丹阈值、夺取数值或本命神通数量；这些数值要由 BattleSim 与 TQ-015C 共同锁定。

**验证：**

```powershell
rg -n "正籍|辅籍|敕籍|寄丹|一正二辅" "docs/基础设定/修行境界.txt" "docs/基础设定/境界特性.txt" "docs/角色养成/术法槽位设计.txt" "docs/角色养成/功法设计规范.txt" "docs/角色养成/术法设计规范.txt" "docs/角色养成/神通设计规范.txt" "docs/角色养成/功法/功法设计.txt" "docs/角色养成/术法/术法设计.txt" "docs/角色养成/神通/神通设计.txt"
powershell -ExecutionPolicy Bypass -File tools/check-review-text.ps1 -Paths docs/基础设定,docs/角色养成
git diff --check
```

**完成条件：** 上述九份文档不存在将旧词作为现行层级或数值规则的表述；全部引用矩阵为席位效果事实源；未锁定的数值明确保留给数据迁移任务。

## 任务 3：重要 NPC 迁移（TQ-015B-2）

**文件：**
- 修改：`docs/剧情/世界背景故事.txt`
- 修改：`docs/剧情/重要NPC/韩惊蛰.txt`
- 修改：`docs/剧情/重要NPC/钟离幽.txt`
- 修改：`docs/剧情/重要NPC/谢观微.txt`
- 修改：`docs/剧情/重要NPC/谢凌沧.txt`
- 修改：`docs/剧情/重要NPC/苻渊.txt`
- 修改：`docs/剧情/重要NPC/祝融烈.txt`
- 修改：`docs/剧情/重要NPC/王玄略.txt`
- 修改：`docs/剧情/重要NPC/拓跋烈.txt`
- 修改：`docs/剧情/重要NPC/慕容朔.txt`
- 修改：`docs/剧情/重要NPC/姚观寂.txt`
- 修改：`docs/剧情/重要NPC/吕星枢.txt`
- 修改：`docs/剧情/重要NPC/司马承景.txt`
- 修改：`docs/剧情/重要NPC/卫长庚.txt`

**实施：**

1. 保留南北朝／淝水之战的结构参考，不复制真实历史人物、地名、事件文本或结局。
2. 每份 NPC 档案补齐：历史锚点与非复制说明、叙事层级、当前目标/失败代价/时间压力、已知/误判/未知、分支与源化界席位、占据方式、丹枢六字段、至多三条强关系。
3. 对既有“二辅空缺”等冲突全部消除；任何妖血、法箓、血盟若未授予真实席位使用权，只能表述为契约或注入。
4. 太一道庭只拥有传承、敕封与局部资源网络，不完整控制任何丹系；司马承景以高成本外显方式展示真实元婴存在，不能作为正常战斗单位。

**验证：**

```powershell
rg -n "一正二辅|正籍|辅籍|敕籍|寄丹|炼虚" docs/剧情/世界背景故事.txt docs/剧情/重要NPC
rg -L "历史锚点|叙事层级|当前目标|已知|误判|未知|位格状态|丹枢" docs/剧情/重要NPC/*.txt
powershell -ExecutionPolicy Bypass -File tools/check-review-text.ps1 -Paths docs/剧情
git diff --check
```

**完成条件：** 13 份 NPC 互不共享旧层级语义；其通用位格效果均可回指矩阵；角色差异均能回指自身丹枢与叙事信息；背景时间线一致。

## 任务 4：批量生产包与 DeepSeek 交接（TQ-015B-3）

**文件：**
- 新增：`docs/基础设定/金丹位格批量生产模板.txt`
- 修改：`开发管理/DeepSeek工作提示词.txt`
- 修改：`开发管理/AI合作沟通.txt`

**实施：**

1. 建立角色、丹枢、功法、术法、神通、洞府、事件六类模板；每份模板强制填入目标席位、矩阵回指、不可越界项、代价、丹枢接口。
2. 明确 DeepSeek 只能扩展已有 17 分支与 51 席位，不能创建正式名称、第四席位、全丹系门派垄断或新的化神根系。
3. 每个批量包提供逐项自检表，并将仍需 Codex / ChatGPT5.5 判断的边界写入交接记录。

**验证：**

```powershell
rg -n "目标席位|矩阵回指|不可越界|代价|丹枢" "docs/基础设定/金丹位格批量生产模板.txt"
powershell -ExecutionPolicy Bypass -File tools/check-review-text.ps1 -Paths docs/基础设定/金丹位格批量生产模板.txt,开发管理/DeepSeek工作提示词.txt,开发管理/AI合作沟通.txt
git diff --check
```

**完成条件：** DeepSeek 接收模板即可批量生成而不接触体系命名与位格定义；所有产物可由矩阵和 NPC 规范机械验收。

## 任务 5：数据模型与炼虚迁移方案（TQ-015C）

**文件：**
- 新增：`开发管理/源化界数据模型迁移差异表.txt`
- 修改：`开发管理/当前任务队列.txt`
- 修改：`开发管理/设计-当前状态.txt`
- 后续实施范围：`simulations/BattleSim/Program.cs`、`src/Assets/Scripts/Cultivation/CultivationEngine.cs`、`src/Assets/Scripts/Data/CharacterData.cs`、`src/Assets/DataConfig/GongFa.csv`、`src/Assets/DataConfig/Spells.csv`、`src/Assets/DataConfig/Skills.csv`

**实施：**

1. 先对旧金丹字段、席位字段、暂寄状态、名称显示字段与丹枢字段建立一对一迁移表；未经表格审核不得改 BattleSim、Unity 或 CSV。
2. 全仓检索炼虚及 `realm_lianxu`，逐项标为删除、改为化神 NPC、改为背景传承、资料片保留或 ID 兼容；不得保留炼虚为可用境界。
3. 成丹阈值、席位竞争和本命神通数量必须在 BattleSim 中提出可运行方案并以模拟结果锁定，不能仅以文案决定数值。

**验证：**

```powershell
rg -n "炼虚|realm_lianxu" docs src simulations 开发管理
powershell -ExecutionPolicy Bypass -File tools/check-data-chain.ps1
dotnet build -c Release --no-restore "D:\天章游戏开发\simulations\BattleSim"
dotnet run --no-build -c Release --project "D:\天章游戏开发\simulations\BattleSim"
git diff --check
```

**完成条件：** 形成经复审的迁移差异表；炼虚不存在于最终境界链；所有具体数值修改均附带 BattleSim 证据；Unity/CSV 迁移作为独立可回滚切片进入队列。

## 复审与提交顺序

1. 每个任务只提交本任务文件，持续标注 `⚠️ 已修改/未审核`。
2. 先由 Codex / ChatGPT5.5 对事实源和核心规则复审，再允许 DeepSeek 批量扩展 NPC 与模板。
3. 每次任务完成前运行对应验证命令、`git diff --check`，并检查文本中没有控制字符。
4. 通过复审后，更新 `开发管理/当前任务队列.txt`，再将可长期留存的事实更新到 `开发管理/设计-当前状态.txt`。
