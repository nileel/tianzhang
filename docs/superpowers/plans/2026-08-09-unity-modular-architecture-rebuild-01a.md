# Unity 模块化架构重建 01A 文件级执行记录

> 基线：2026-08-09 实时扫描 `e155f6d038dbfe7bc6479bd4808de7a77a02de75`。本文记录批准父设计阶段 0/1 的实施，不重新设计父项，不包含 01B～01H。

## 实时结构证明

- Unity：`6000.3.18f1`；正式 Build Settings 入口仍为 StartMenu、World、Settlement、Adventure 四个场景。
- 活动 Player 程序集：Foundation -> Domain -> Combat -> Gameplay；Gameplay／Domain 的跨目录归属由 7 个 `.asmref` 声明。
- `TianZhang.Editor` 仅含 `includePlatforms: Editor`，合法引用现行 Player 程序集；Player 不引用 Editor。
- `src/Assets/Scripts/TianZhang.Runtime.asmdef` 无直接 C#，不作为新架构入口。
- 现有 `CharacterPresentationPrototypeBootstrap` 只属于旧表现原型；目标组合根只有 `TianZhang.Bootstrap/GameBootstrap`。
- schema 5 扫描时 `runs.codex=null`、`runs.deepseek=null`、`activeTaskIds=[]`、`integrationLockStatus=none`；主工作区用户脏路径留在主工作区，实施 worktree 从上述基线提交创建。

## 文件级实施

| 文件／目录 | 处理 | 边界证明 |
|---|---|---|
| `开发管理/架构重建行为基线-U-ARCH-REBUILD-01A.txt` | 新建五项有限行为基线 | 只记录输入、稳定 ID、状态变化、结果和直接测试；不冻结 UI／视觉 |
| `src/Assets/Scripts/Modules/{Spatial,Content,Character,Cultivation,World,GameplayContracts}` | 新建空 asmdef 与 README | 只形成目标 Domain／Contracts 边界，不迁移规则类 |
| `src/Assets/Scripts/Modules/Features/*` | 新建五个空 Feature asmdef 与 README | 兄弟 Feature 无直接依赖 |
| `src/Assets/Scripts/Modules/Infrastructure/*` | 新建 Persistence／UnityContent 空 asmdef 与 README | 只依赖批准的低层模块与契约 |
| `src/Assets/Scripts/Modules/Bootstrap/*` | 新建唯一 asmdef、README 与 `GameBootstrap` | 壳类型无字段、无声明方法、无业务逻辑 |
| `src/Assets/Scripts/{Core,Combat,Editor}/README.md` | 为继续复用的程序集补短边界说明 | 不复制 Foundation、Combat、Editor 程序集 |
| `tools/check-unity-assembly-boundaries.ps1` | 新建机械依赖检查器 | 检查解析、重复名、未解析内部引用、循环、Domain→Feature、兄弟 Feature、非 Bootstrap 多 Feature、Player→Editor |
| `AssemblyBoundaryEditorTests.cs` | 扩展目标图直接断言 | 保留现行程序集与 asmref 断言，新增目标依赖图和唯一 Bootstrap 断言 |
| `ArchitectureBaselineEditorTests.cs` | 新建基线、Bootstrap 与非法负例测试 | 运行故意非法循环、Domain→Feature、兄弟 Feature、Editor 进入 Player 四类负例 |
| `TianZhang.EditModeTests.asmdef` | 引用 `TianZhang.Bootstrap` | 让直接测试编译目标组合壳 |
| `UNITY_STRUCTURE.md` | 收敛为根索引 | 只保存版本、正式场景、路由、验证入口、不可触碰区和核验日期 |

所有新 Unity 文件和目录同时建立稳定 `.meta`；不修改场景、Prefab、CSV、导入器、asset、渲染或美术路径。

## 依赖边

迁移前边保持不变：Foundation -> 无，Domain -> Foundation，Combat -> Foundation + Domain，Gameplay -> Foundation + Domain + Combat，Editor -> 现行 Player 程序集。

本阶段删除依赖边：无。新增的目标骨架边由各 asmdef 的 `references` 精确声明；Domain／Contracts 只向低层，Feature 向 Domain／Contracts，Infrastructure 向 Domain／Contracts，Bootstrap 是唯一同时引用五个 Feature 实现的程序集。没有运行时调用边或状态写入者变化。

## 结构与范围门禁

- 不新增第二 Runtime、第二目标 Bootstrap、adapter、双写或 fallback。
- 不迁移领域规则或运行时实现；空模块没有临时 C# 占位。
- `U-ARCH-REBUILD-01H -> U-URP-PREFLIGHT-01 -> U-URP-MIGRATE-01 -> U-URP-VISUAL-BASELINE-01 -> U-CHAR-3D-PROTO-01` 的任务卡实时保持 blocked，本实施不改这些任务卡。
- 最大新增手写文件为 `ArchitectureBaselineEditorTests.cs` 191 行；边界检查器 178 行，均未触发 500 行职责门禁。

## 验证记录

- `tools/check-unity-assembly-boundaries.ps1`：通过，扫描 20 个脚本程序集与 8 个 asmref；直接负例确认循环、Domain→Feature、兄弟 Feature、Player→Editor 均被拒绝。
- `dotnet build src/TianZhang.EditModeTests.csproj`：Unity 正常生成投影后通过，0 error、2 个既有 `CS0649` warning；没有手改生成文件。
- `tools/run-unity-editmode-tests.ps1`：命令已完整运行，436/438；01A 结构用例 5/5、行为基线相关直接用例 88/88 通过。两个失败均在 `e155f6d` 已存在且不属于本卡路径：
  - `CharterConflictRulesTests.AuthorizedGrantBindsEveryConflictIdentityField`：默认左右候选储备同为 6，生产规则返回 `PULSE_NEUTRAL`，旧断言期待 `PULSE_ADVANTAGE`。
  - `DataConfigImporterContentScopeTests.LianShenReservedAbilitiesRemainExcludedAtPlayerLoad`：现行加载器只查询 `Assets/Data/AttackProfiles/AttackProfile_<id>.asset`，旧测试加载的四项 Spell／Skill asset 没有对应 AttackProfile 文件，实际为 `Attack profile not found`。
- `tools/check-review-text.ps1`：通过，426 files checked。
- `tools/check-pending-whitespace.ps1`：对当前 92 个 changed expectedPaths 文件通过；`.meta` 语义空值尾随空格被正确接受。
- 独立复审状态转换：用户随后明确授权把 `开发管理/未通过审核清单.txt` 加入本卡 `expectedPaths`，仅用于合法进入独立架构复审；本次不写该清单正文。
- 任务投影转换为 `codex_review/codex/ready` 并通过 `check-task-cards.ps1` 全局及定向检查；01B 及 URP／美术链保持 blocked，本实施不宣称独立复审完成。
