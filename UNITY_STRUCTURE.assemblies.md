# Unity 程序集与模块边界

## 何时读取

新增／移动 C#、调整 namespace、asmdef／asmref、模块依赖或跨模块契约时读取。

## 主要路径

- Player 与 Editor 程序集：`src/Assets/Scripts/`
- EditMode／PlayMode 程序集：`src/Assets/Tests/`
- 边界闸门：`tools/check-unity-assembly-boundaries.ps1`
- 每个程序集旁的 `README.md` 是该模块的短路由。

## 实时程序集图

| 程序集 | 直接项目依赖 |
|---|---|
| `TianZhang.Foundation` | — |
| `TianZhang.Content` | Foundation |
| `TianZhang.Spatial` | Foundation |
| `TianZhang.Domain` | Foundation、Content、Spatial |
| `TianZhang.Character` | Foundation、Content、Spatial |
| `TianZhang.Cultivation` | Foundation、Content、Character |
| `TianZhang.Combat.Turns` | — |
| `TianZhang.Combat` | Foundation、Spatial、Combat.Turns |
| `TianZhang.World` | Foundation、Content |
| `TianZhang.Gameplay.Contracts` | Foundation |
| `TianZhang.Features.CharacterCreation` | Foundation、Domain、Content、Character、Cultivation、Gameplay.Contracts |
| `TianZhang.Features.WorldMap` | Foundation、Content、World、Gameplay.Contracts |
| `TianZhang.Features.Settlement` | Foundation、Content、Character、World、Gameplay.Contracts |
| `TianZhang.Features.Adventure` | Foundation、Content、Character、World、Combat、Gameplay.Contracts、Spatial、Infrastructure.UnityContent |
| `TianZhang.Features.CombatPresentation` | Foundation、Gameplay.Contracts |
| `TianZhang.Infrastructure.Persistence` | Foundation、Content、Character、Cultivation、World、Gameplay.Contracts |
| `TianZhang.Infrastructure.UnityContent` | Foundation、Content、Spatial |
| `TianZhang.Bootstrap` | Domain、Character、Content、Combat、Cultivation、World、Gameplay.Contracts、五个 Feature、两项 Infrastructure |
| `TianZhang.Editor` | 领域／Feature／Bootstrap 的编辑器入口；只在 Editor 平台 |

Unity UI/InputSystem 外部引用只出现在需要的 Feature、Editor 或测试程序集。`TianZhang.EditModeTests` 与 `TianZhang.PlayModeTests` 只用于验证，不进入 Player。

## asmref 与源所有者

- `src/Assets/Scripts/Cultivation/TianZhang.Domain.asmref` 将既有金丹道证纯规则纳入 `TianZhang.Domain`。
- `src/Assets/Scripts/Content/TianZhang.Domain.asmref` 保留该领域源目录的程序集归属；当前目录没有 C# 实现。
- Player 中不存在 `TianZhang.Runtime`、宽泛 `TianZhang.Gameplay` 或指向后者的 asmref。

## 跨模块路线

- Feature 之间不直接引用实现；导航、战斗命令、HUD 与棋子表现 DTO 经 `TianZhang.Gameplay.Contracts`。CombatPresentation 不引用 Combat，棋子载体只消费只读合同。
- Combat 只消费纯快照和 `ICombatSpatialQuery`；Character／Spatial／UI 实现不进入 Combat。
- `TianZhang.Bootstrap` 是唯一同时引用多个 Feature 的 Player 程序集。
- `TianZhang.Editor` 可编排导入和场景构建，但不进入 Player。

## 验证提示

- `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-unity-assembly-boundaries.ps1`
- 权威编译与测试：`pwsh -NoProfile -ExecutionPolicy Bypass -File tools/run-unity-editmode-tests.ps1`
- 仅当前 worktree 已有生成投影时快速编译：`dotnet build src/TianZhang.EditModeTests.csproj`
- `AssemblyBoundaryEditorTests`

## 禁止修改

- 不手改 Unity 生成的 `.csproj`／`.sln`。
- 不把 clean linked worktree 中缺失生成 csproj 视为程序集阻塞，也不跨 worktree 复制投影。
- 不向领域程序集添加 Feature、Bootstrap 或 Editor 引用。
- 不恢复宽泛 Gameplay 聚合、兄弟 Feature 直连或第二组合根。

## 开放边界

`TianZhang.Domain` 当前只承载既有金丹道证边界；若后续迁移它，必须另行证明引用和保存语义，不在普通功能任务中顺手调整。
