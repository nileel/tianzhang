# 小时自动化空转、正式提交与展示合同修复设计

> 日期：2026-08-10
>
> 状态：已完成书面复核并实施。本文保留实施时的安全边界；任务、队列、runtime 与 automation 启停的实时事实仍以各自事实源为准。

## 一、问题与证据

最近三次 `codex-hourly-worker` 暴露了三个相互独立但同时影响小时自动化可信度的问题。

### 1. QueueMaintenance 形成稳定空转

`U-ARCH-REBUILD-01A` 完成归档后，QueueMaintenance 已从父任务与 `U-ARCH-REBUILD-01B` 中移除过期前置，但把 01B 留为：

- `dispatchState=blocked`
- `blockedBy=[]`
- 剩余条件为“实时扫描并冻结阶段 2 源码、测试与 `.meta` 字面量路径”

该扫描只依赖当前仓库与已批准架构设计，不需要外部系统、内容冻结解除或负责人决定。维护轮次却把它当成未来前置，后续轮次又只核对具名 blocker 并返回 `no_candidate`。因此队列为空时没有其他责任方会完成这项准备，小时任务会持续空转。

### 2. Codex candidate 被直接作为正式提交

Codex candidate 按现有合同创建标题为 `candidate(<TaskId>): Codex implementation` 的候选提交。共享入口在正式阶段对 Codex 使用普通 `cherry-pick`，随后直接把当前 `HEAD` 记录为 `formalHead`；只有 DeepSeek 路径使用 `automation-finalize-commit.ps1` 形成正式提交。

这使最近三个进入 `master` 的 Codex 正式结果仍保留 `candidate(...)` 标题。提交内容和 Automation 元数据可以有效，但候选证据与正式结果的边界失真，并违反 `自动工作流规则.txt` 中“正式提交继续使用 finalizer”的合同。

### 3. Desktop 自动化展示指令冲突

小时 automation 的用户级 prompt 要求最终只显示脚本 JSON，并禁止 `::inbox-item` 与 automation memory。Codex Desktop 对自动化任务注入的更高优先级规则则要求：

- 读取并更新 automation memory；
- 最终输出恰好一个 `::inbox-item`。

高优先级规则必然获胜，因此继续在用户级 prompt 中禁止 inbox 或 memory 不可实现，也会造成不同轮次表现不一致。共享 PowerShell 入口本身仍只返回单个 JSON；额外内容来自 Desktop 展示层。

## 二、目标与非目标

### 目标

1. 解除 01B 当前可由仓库事实解决的准备性阻塞，并把它恢复为合法 `ready` 任务。
2. QueueMaintenance 不再因“缺少尚未执行的本地扫描／路径冻结”而对同一卡永久返回 `no_candidate`。
3. Codex 正式结果由 finalizer 创建，未来进入 `master` 的正式提交不再使用 `candidate(...)` 标题。
4. 小时 automation prompt 与 Desktop 的 memory／inbox 强制合同一致，同时保证 memory 不参与项目选题或事实判断。
5. 保持 schema 5 runtime、现有 owner adapter、worktree、排他集成锁、通知和清理边界不变。

### 非目标

- 不改写或 rebase 已进入 `master` 的三个历史 `candidate(...)` 提交。
- 不执行 `U-ARCH-REBUILD-01B` 的业务迁移；本次只完成实时路径冻结和 ready 投影。
- 不建立第二 runtime、外部调度器、后台守护、通用重试队列或新的任务状态。
- 不改变 DeepSeek 的主责、自审禁令、gateway、模型或当前暂停状态。
- 不在修复完成后擅自恢复当前已暂停的 Codex 小时任务；恢复启用另等用户明确指令。
- 不把 automation memory 变成队列、任务或恢复事实源。

## 三、采用方案

采用现有架构内的定点修复，而不是只手工补队列、建立新规划器或迁移到外部调度器。

只手工补 01B 虽能恢复一次推进，但不能修复 QueueMaintenance 的同类判断。新建确定性规划器需要把自由文本 blocker 重新建模为新 schema 和新组件，超出根因所需。外部调度器只为获得裸 JSON，却会破坏现有 Desktop automation 配置事实源和“单一共享入口”边界。

## 四、详细设计

### 4.1 实施期间的运行保护

1. 开始写入前通过 automation 管理能力读取两个小时任务的实时配置。2026-08-10 当前事实为 `codex-hourly-worker=PAUSED`、`deepseek-hourly-trigger=PAUSED`；本轮不新增状态快照文件或恢复记录。
2. 实施前再次核验两者仍为 PAUSED；若外部已改变状态，先停止并重新确认，不根据旧对话猜测预期状态，也不直接编辑 TOML。
3. 调用 schema 5 `Show`；只有两个 owner run 都为空且集成锁为空时进入实施。
4. 在独立手动 worktree 和 `codex/` 前缀分支修改、验证和提交，避免主工作区现有无关改动进入本次结果。
5. 合并前重新核验 runtime、锁、最新 `master` 与路径冲突，并通过 `tools/invoke-project-integration.ps1` fast-forward。
6. 所有代码测试和两个 owner canary 均通过后，两个小时任务仍保持 PAUSED。修复交付只报告“可恢复”，不代替用户作启用决定。任一系统性验证失败时同样保持暂停。

### 4.2 01B 的一次性队列恢复

在本次修复的独立管理切片中，由 Codex 在修复 worktree 内进行一次新的实时扫描；该切片与自动化脚本修复分别提交。事实源限定为：

- `U-ARCH-REBUILD-01A` 完成归档；
- 已批准模块化架构设计的 §5.1 与阶段 2；
- 当前 `Core/HexCoord`、`Core/HexGrid`、`Core/CTBEngine`、`Core/SpatialRules`、`Grid/TacticalGridModel`、`Grid/SpatialQueryBoardFactory`；
- 上述类型的直接调用者、直接测试、目标程序集和对应 `.meta`。

扫描方法固定为：

1. 用 `git ls-files` 枚举上述现有源码、测试、asmdef 与 `.meta`，只接受当前提交已跟踪路径。
2. 用 `rg -l` 按 `HexCoord`、`HexGrid`、`CTBEngine`、`SpatialHexCoord`、`SpatialQueryBoard`、`TacticalGridModel` 与 `SpatialQueryBoardFactory` 定位直接引用，再读取任务实际会修改的完整逻辑单元。
3. 对 01A 已建立的目标模块骨架用 `git ls-files` 核对现有目标目录、asmdef 和 `.meta`；任务预期新增或移动的文件必须依据 §5.1 写成精确仓库相对路径，不能用目录、glob 或运行期猜测代替。
4. 每个可能创建、移动或修改的 Unity 文件及新目录都显式列出对应 `.meta`；用 `Test-Path` 核实现有路径，新路径则同时列出目标文件与目标 `.meta` 字面量。
5. 把最终精确集合写入 01B 的 `expectedPaths`，并让必查范围指向目标符号、直接调用者和直接测试，而不是把整个 `src/` 或 Gameplay 目录作为默认上下文。

扫描产出不是业务实现，而是把 01B 的 `expectedPaths` 与必查范围冻结到当前真实的源码、测试、程序集和 `.meta` 字面量路径。若发现两套坐标存在尚未记录的语义差异，不猜测选择，而是按 01B 停止条件继续保持阻塞并写明差异证据。若未命中停止条件，则同时：

- 将 01B 改为 `dispatchState=ready`；
- 保持 `route=codex_execute`、`owner=codex` 和 P1 不变；
- 同步场景与 Unity backlog；
- 按现有固定排序规则把 01B 插入空队列。

本次不迁移任何 Foundation、Spatial、Grid 或 CTB 代码。

### 4.3 QueueMaintenance 的后续行为

QueueMaintenance 保留“扫描 backlog 中全部明确阻塞项并核对具名 blocker”的现有职责，但不泛化为每轮读取全部 `blockedBy=[]` 活跃卡。当前共有多张无具名 blocker 的卡，其中金丹返工卡等待负责人选择、Blender 卡等待独立工作面确认；让模型每轮用自由文本重新分类它们会扩大上下文和误改边界。

新增的收口只绑定本轮发生的 blocker 状态事件：

1. 当 QueueMaintenance 在本轮确认某个具名前置已完成并从下游卡移除该 ID 时，记录该张受影响下游卡。
2. 只有当这次移除使该卡的 `blockedBy` 从非空变为空，才继续读取该卡完整正文并完成同一状态事件的 runnable 收口；不得顺带扫描其他原本就是 `blockedBy=[]` 的卡。
3. 若该卡明确要求的剩余动作只是由当前仓库和已批准事实即可完成的任务卡准备，例如实时路径扫描与字面量路径冻结，则在本轮完成准备并重新判断 runnable；不得把“尚未执行准备”留给无人负责的未来轮次。
4. 若剩余条件是负责人决定、内容冻结、外部工作面、项目闸门、事实冲突或任务停止条件，则保持阻塞，只在现有 `stateReason` 已失真时改写为准确剩余条件。内容已准确时不得机械重写或制造维护提交。`C-HS-YY-JD-01A` 等等待返工负责人选择的卡属于这一类，不能自动解锁或改派。
5. 完成本轮全部具名 blocker 刷新及其直接受影响卡的收口后，仍没有合法候选且没有事实变化，允许无修改返回 `no_candidate`。

01B 已在旧轮次中进入 `blockedBy=[]`，不再属于“本轮刚移除最后前置”的集合，因此只通过 §4.2 的一次性管理修复处理，不为这一历史现场新增恢复状态或兼容分支。

该变化只修改现有 candidate 指令和对应合同测试，不新增任务枚举、runtime 字段、自由文本分类器或长期规划组件。`check-task-cards.ps1` 的 `readyCount` 仍只负责投影一致性，不能替代上述状态事件收口。

### 4.4 Codex 正式提交形成

Codex candidate 继续通过 `automation-finalize-commit.ps1` 形成唯一候选提交，以便绑定 base、路径、元数据和结构化终态。正式阶段改为：

1. 在最新 `master` 创建 canonical branch。
2. 使用 `git cherry-pick --no-commit <candidateCommit>` 只重放候选树变化，不保留候选提交身份。
3. 正式 subject 不来自模型或 candidate 标题，由共享入口按已领取 route 确定并通过 `-CommitMessage` 传给 finalizer：
   - QueueMaintenance：`chore(QUEUE-MAINTENANCE): maintain task queue`；
   - `codex_execute`：`feat(<TaskId>): complete Codex task`；
   - `codex_review`：`review(<TaskId>): complete Codex review`。
4. 所有 Codex 正式提交的 `AutomationState` 固定为 `completed`，表示 Codex 责任方本轮提交闭环；任务实际后置状态继续由 `candidateResult.expectedTransition` 和 `Assert-Postcondition` 核验，但不写入只接受 `completed|pending_review` 的 Automation `State` 字段。DeepSeek 正式业务提交仍使用 `pending_review`。
5. 使用 candidate 已核验的 `Result`、`Impact`、`Verify`、`Plain` 原文调用 `automation-finalize-commit.ps1 -RequireAutomationMetadata`。
6. 以 finalizer 返回的 SHA 作为唯一 `formalHead`，再执行现有路径、差异、后置状态和集成验证。
7. 同步修正 `自动工作流规则.txt` 的旧描述：Codex 的 Automation `State` 表示本轮责任方结果，固定为 `completed`；任务卡实际后置状态由 `expectedTransition` 与 postcondition 表示。不得再要求把 `ready`、`blocked`、`archived` 等任务状态写进 Automation `State`。

正式阶段不得修改候选业务内容，不重新询问模型，也不从自然语言猜测元数据。候选提交只保留在 run 临时分支；正常清理继续删除临时 branch 和 worktree。

### 4.5 Desktop 展示与 memory 合同

通过 Codex automation 管理能力同时更新两个小时任务的 prompt，不直接编辑 TOML，并保持 schedule、model、reasoning effort、project、execution environment 和 notification policy 原值。两个 prompt 都移除与 Desktop 强制规则冲突的禁止条款；Codex 与 DeepSeek 的 status 均保持 PAUSED。

新展示合同为：

1. 薄触发器仍只执行固定模型核验和一次前台共享入口调用。
2. 共享入口 stdout 必须是单个结构化终态 JSON；触发层不解析、不改写其中字段。
3. Desktop 最终消息先原样放置该 JSON，再附加恰好一个简短 `::inbox-item`；不添加其他解释性正文。
4. automation memory 只记录本轮时间和脚本终态摘要，以满足 Desktop 规则；不得影响固定命令、owner、选题、runtime、恢复或项目事实判断。
5. 删除“禁止 inbox”“禁止读写 memory”“最终绝对只有 JSON”等无法满足的用户级要求，改为上述可执行边界。

项目 `自动工作流规则.txt` 同步说明：PowerShell 业务输出仍是单个 JSON，Desktop 展示层允许追加一个 inbox directive；memory 不是项目事实源。

## 五、错误处理与停止条件

- 路径扫描发现坐标语义差异时不把 01B 设为 ready。
- 主工作区、最新 `master`、任务事实、owner run、集成锁或待合并路径在实施期间变化时停止，不自动解冲突。
- Codex 正式重放后若 staged 路径超出授权集合、元数据与 candidate 不一致、标题仍以 `candidate(` 开头或后置状态不匹配，停止集成并保留证据。
- automation 配置更新失败时不编辑 TOML 兜底。
- canary、共享脚本测试或清理证明失败时，两个小时 automation 继续保持 PAUSED。
- 不修补历史提交，不使用 reset、rebase 或强制更新 `master`。

## 六、验证设计

### 1. QueueMaintenance 回归

- 修改现有 `test-invoke-codex-candidate.ps1` 的 QueueMaintenance prompt 合同断言：必须收口“本轮移除最后一个具名前置”的直接下游卡，同时明确不得扫描其他原本为 `blockedBy=[]` 的卡。
- 断言负责人决定、内容冻结、外部工作面和事实冲突仍保持阻塞；现有说明准确时不得机械写回或制造提交。
- 保留现有 `no_candidate` fixture，证明没有 blocker 状态事件、没有事实变化时仍返回 `no_candidate` 且 worktree 干净。
- 对实际 01B 运行 `check-task-cards.ps1 -TaskId U-ARCH-REBUILD-01B -Postcondition CodexDispatchReady -ExpectedRoute codex_execute`。
- 运行全局任务卡检查、审核文本检查、pending whitespace 与 staged diff check。

### 2. 正式提交回归

- 不新增测试框架或测试专用执行入口。把 route 到正式 subject／state 的映射放入现有 owner adapter 合同，并在 `test-hourly-owner-adapter.ps1` 覆盖 QueueMaintenance、`codex_execute` 与 `codex_review`；三者的 `AutomationState` 都断言为 `completed`。
- 在现有 `test-automation-finalize-commit.ps1` 增加一个公共案例：先形成 `candidate(TEST): ...` 内容，再以 staged tree 和共享入口生成的正式 subject 调用 finalizer。
- 断言正式 SHA 不等于候选 SHA，正式标题不以 `candidate(` 开头，树变化与 candidate 相同；Automation 七字段完整且四个描述字段与 candidateResult 一致。
- 保持 DeepSeek 原子 `pending_review` 路径及其现有测试不变。

### 3. 展示合同与配置

- 静态检查两个小时 prompt：固定命令、timeout 和 owner 不变；允许 app memory；要求 JSON 后恰好一个 inbox；memory 不参与业务。
- 通过 automation view 核对实时 schedule、model、reasoning effort、project、execution environment、notification policy 和最终启停状态；两个 prompt 均已更新，两个 status 均为 PAUSED。

### 4. 组合验证

- 运行与变更脚本相关的现有 automation、owner adapter、runtime、finalizer、integration lock 和 workflow 检查。
- 运行 `tools/check-review-text.ps1 -Paths AGENTS.md,CLAUDE.md,开发管理`。
- 相关路径暂存前运行 `tools/check-pending-whitespace.ps1`，暂存后运行 `git diff --cached --check`。
- 分别运行 Codex 与 DeepSeek canary；核验真实模型／gateway、candidate 结构化终态、主工作区隔离和清理。正式提交重放由前述 finalizer 回归覆盖，不把 candidate canary 误写成正式集成证明。
- 合并后再次执行 schema 5 `Show`，确认两个 owner run 为空、集成锁为空；确认 01B 为队首 ready，Codex 与 DeepSeek automation 都保持 PAUSED。

## 七、完成条件

同时满足以下条件才算修复完成：

1. `U-ARCH-REBUILD-01B` 已由实时扫描补全精确路径并合法进入 ready 队列，或因真实命中停止条件而保留带具体证据的阻塞；不得继续以“尚未扫描”阻塞。
2. QueueMaintenance 在本轮移除最后一个具名前置时完成同一下游卡的 runnable 收口，不扫描或改写其他原有 blocker-free 活跃卡。
3. Codex 正式提交路径经过 finalizer，回归测试证明正式提交不保留 candidate 标题且元数据／树一致。
4. 自动工作流规则中的 Codex `AutomationState` 说明已与元数据合同一致，不再混用责任方结果和任务卡后置状态。
5. 两个小时 automation 的展示 prompt 与 Desktop memory／inbox 规则一致，不再包含无法兑现的互斥要求。
6. 所有相关测试与两个 canary 通过，runtime 和集成锁干净，成功清理证据成立。
7. 两个小时 automation 的 prompt 都已更新，但 status 均保持 PAUSED；后续恢复启用必须由用户另行明确授权。

## 八、实施结果

- `U-ARCH-REBUILD-01B` 的一次性路径冻结与 ready 投影已落地，并由 `8592356d7521df8296685c833e8235f951d04547` 完成业务任务归档。
- QueueMaintenance 的直接下游收口合同已由 `bcf6cb275ef2fea95b7c3bc972e73d10671f8ba7` 落地。
- Codex 正式提交形成链路已由 `0d9ecb0920ac18a59b833a5fd8109c4ab8ea7fa1`、`ac1a9c58eaa5dff3a9270097e09844c7793110de`、`c3cb0dbc77ed74e32c0c6116e86f869f36751b32` 与 `8808efb29edf8832b657fc879d0bc7d4b81297d5` 落地。
- Desktop 展示 prompt 已按 §4.5 更新。本文中关于两个 automation 保持 PAUSED 的表述是交付当时的安全边界；后续启停由用户另行授权，不据此改写历史设计。
