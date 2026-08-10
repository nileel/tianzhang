# Codex 候选路径合同与 01C 失败现场修复设计

> 日期：2026-08-10
>
> 状态：用户已批准方案与书面设计；2026-08-10 按本设计实施。

## 一、问题与已核验证据

### 1. 候选终态漏报移动源路径

最新失败 run 为 `271205d2-c7bb-4f26-84fd-e470b479142d`，任务为 `U-ARCH-REBUILD-01C`，候选提交为 `274142e67645a006a0bb4fa30c951bc049c445b6`。schema 5 runtime 当前把它记录为 `attention_required`，稳定原因为 `codex_candidate_path_mismatch`。

实施前，候选提交的真实改动集合由共享校验器使用以下口径生成：

```powershell
git -c core.quotepath=false diff --name-only --no-renames "${baseCommit}..HEAD"
```

该集合共有 137 条路径。候选模型最终使用 `git show --name-only HEAD` 重新生成 `changedPaths`，Git 的 rename detection 把移动压缩为目标路径，因此只报告 98 条，漏掉 39 条移动源路径。模型没有报告任何 Git 中不存在的路径；137 条真实路径也全部属于任务卡的 646 条 `expectedPaths`。本次失败是生产者与校验器的路径表示不一致，不是越权修改、主工作区冲突或集成锁失败。

### 2. 01C 必需的程序集断言未随阶段 3 更新

候选最终 Unity EditMode 共执行 442 项，保留 4 项失败。其中两项是既有无关基线；另外两项直接由本次阶段 3 依赖变化触发：

- `AssemblyBoundaryEditorTests.ProjectAssembliesFollowTheRuntimeDependencyDirection`
- `AssemblyBoundaryEditorTests.TargetSkeletonUsesTheApprovedOneWayDependencies`

已批准的模块化架构设计确认：

- 阶段 3 把不可变定义迁入 Content，阶段 4 完成前的遗留 `TianZhang.Domain` 因消费已迁移定义而允许增加 `TianZhang.Content` 依赖；
- `SpatialQueryBoardFactory` 的 EnvironmentProfile 转换适配明确迁入 `Infrastructure.UnityContent`，所以该程序集必须允许依赖 `TianZhang.Spatial`；
- `Spatial` 本身仍只消费显式格位、边与查询限制输入，不反向依赖 Unity Content。

因此两项失败是阶段 1/2 断言未更新，不应通过删除正确依赖边来迎合旧断言。但 `src/Assets/Tests/EditMode/AssemblyBoundaryEditorTests.cs` 及其 `.meta` 没有进入 01C 当前 `expectedPaths`，导致候选无法在授权内完成必需验证。

### 3. 旧候选不能直接补写后接纳

旧候选同时存在结构化终态路径不一致和相关 EditMode 失败。项目恢复规则禁止普通失败恢复旧模型会话、自动重放或把普通失败伪装成 checkpoint。修复必须释放旧 run 的占用，从最新 `master` 建立全新 run 与 worktree；旧现场只作为证据保留。

## 二、目标与非目标

### 目标

1. 固定 Codex candidate 的移动路径表示：删除、源路径和目标路径均按 `--no-renames` 的仓库相对路径报告。
2. 用回归测试证明含文件移动的候选能返回完整路径，rename 压缩的终态继续失败关闭。
3. 给 01C 补齐更新程序集边界断言所需的精确路径授权。
4. 通过精确证据关闭当前失败 run，不删除其 worktree、branch 或候选提交。
5. 从最新 `master` 新建 run，重新执行并验证 01C；不恢复旧会话，不吸收旧候选提交。
6. 修复期间防止小时定时器抢占同一任务；交付后保持小时入口暂停，等待负责人另行决定是否恢复。

### 非目标

- 不改写、amend、rebase 或强制更新旧候选提交。
- 不降低共享校验器对 `changedPaths` 的精确相等检查。
- 不新增兼容路径、第二 runtime、自动重试队列或恢复状态。
- 不顺带修复两项已登记且与 01C 无关的 EditMode 基线失败。
- 不修改 DeepSeek 的候选合同、gateway、模型或任务路由。
- 不改动主工作区现有的美术、飞书、总结或其他未提交文件。
- 不删除失败现场；清理需要后续独立、精确授权和证明。

## 三、方案选择

### 采用：保留严格合同，固定唯一生成命令并全新重跑

共享校验器继续以 Git 真实集合为准并要求模型终态逐项一致。候选 prompt 明确给出唯一命令和禁止项，模型在提交后必须使用 `baseCommit..HEAD` 与 `--no-renames` 生成最终 `changedPaths`，不得使用 `git show --name-only`、`--find-renames` 或其他会压缩移动源路径的输出。

该方案保持已有失败关闭边界，只修复造成误报的生产步骤。

### 拒绝：忽略模型上报路径

虽然共享校验器已能自行计算真实路径，但忽略终态字段会削弱结构化结果绑定，并连带改变 decision checkpoint 合同。该变化超出本次根因所需。

### 拒绝：人工修补旧候选后直接集成

旧候选没有通过必需的 EditMode 验证，且普通失败不具备 checkpoint 恢复合同。直接接纳会绕过任务卡停止条件和人工恢复规则。

## 四、实施设计

### 4.1 运行保护与旧 run 精确关闭

1. 通过 automation 管理能力读取 `codex-hourly-worker` 当前配置；若仍为 ACTIVE，先只把该入口设为 PAUSED，并保持 schedule、model、reasoning、project、execution environment 与通知配置不变。不得编辑 TOML。DeepSeek 入口不因本次单 owner 问题被改动。
2. 调用 schema 5 `Show -RepositoryRoot`，重新核对：
   - owner=`codex`；
   - taskId=`U-ARCH-REBUILD-01C`；
   - runId=`271205d2-c7bb-4f26-84fd-e470b479142d`；
   - state=`attention_required`；
   - recoveryReason 精确不变；
   - integration lock=`none`。
3. 核对旧 worktree 的 branch、HEAD、工作树清洁状态和候选提交唯一父链；确认候选 SHA 尚未被 `master` 包含。
4. 该失败发生在共享入口把 candidate 写入 runtime 前，所以 runtime 的 `candidateCommit`／`candidateResult` 均为空；先在 Git 中独立核对旧 worktree、branch、HEAD 和清洁状态，再使用 `hourly-automation-lease.ps1 -Action CompleteRun` 的 empty attention 精确关闭合同并提供 `ExpectedRecoveryReason`。不得伪造 runtime 中不存在的 candidate 字段来换取 `evidenceRetained` 标记。
5. 只接受 `RUN_COMPLETED`；关闭后再次 `Show`，要求 `runs.codex=null`，并再次核对旧 worktree、branch 与 HEAD 仍原样存在，以此证明证据保留。
6. 不删除旧 worktree、branch、session JSONL 或候选提交。

任一证据不一致时停止，不关闭 run，不尝试 reset、stash、checkout、clean 或泛化恢复。

### 4.2 候选路径合同修复

修改 `tools/invoke-codex-candidate.ps1` 的正常完成 prompt：

1. 显式向候选提供 `BaseCommit`。
2. 固定最终路径生成命令：

   ```powershell
   $changedPaths = @(git -c core.quotepath=false diff --name-only --no-renames "${baseCommit}..HEAD" | Where-Object { $_ } | Sort-Object -Unique)
   ```

3. 说明源路径、目标路径、删除路径和新增路径都必须出现；不得用 rename-compressed 输出替代。
4. 最终 JSON 的 `changedPaths` 必须直接使用该数组，不得在提交后改用另一条 Git 命令重算。
5. 继续保留共享校验器现有的真实集合计算、精确相等检查和 `expectedPaths` 授权检查。

`开发管理/自动工作流规则.txt` 同步加入这一条稳定合同。该规则只描述现有真实所有者，不新增组件或状态。

### 4.3 回归测试

在 `tools/test-invoke-codex-candidate.ps1` 的现有 fixture 中增加移动文件场景：

- 正例：候选提交移动一个已跟踪文件，终态按 `--no-renames` 同时报告源路径和目标路径；wrapper 返回 `completed`，其 `candidateResult.changedPaths` 精确包含两者。
- 负例：同一提交只报告 rename-compressed 目标路径；wrapper 返回 `failed/codex_candidate_path_mismatch`，runtime 不推进为 `candidate_ready`。
- prompt 合同断言：必须包含 `BaseCommit`、`--no-renames` 和禁止 `git show --name-only` 的说明。

测试复用现有临时仓库、fake runner 和断言工具，不新增测试框架或第二 candidate 入口。

### 4.4 01C 任务卡授权修正

在 `开发管理/任务卡/U-ARCH-REBUILD-01C.txt` 的 `expectedPaths` 中补入：

```text
src/Assets/Tests/EditMode/AssemblyBoundaryEditorTests.cs
src/Assets/Tests/EditMode/AssemblyBoundaryEditorTests.cs.meta
```

任务卡正文增加已核验的阶段 3 依赖证据，明确：

- `TianZhang.Domain -> TianZhang.Content` 是阶段 4 接管前的过渡依赖；
- `TianZhang.Infrastructure.UnityContent -> TianZhang.Spatial` 是 `SpatialQueryBoardFactory` 转换适配的批准依赖；
- 测试只更新批准依赖图，不放宽 Feature 兄弟引用、Editor 进入 Player 或 Spatial 反向依赖 Unity Content 等禁止项。

任务保持 `codex_execute/codex/ready`、P1、原队列位置和既有业务范围不变。只更新任务卡 digest 所依赖的事实，不人工实现 01C 业务变化。

### 4.5 隔离、提交与集成

项目修改在独立手动 worktree 中完成，预期形成两个路径限定提交：

1. 自动化合同提交：candidate prompt、candidate 回归测试、自动工作流规则与本设计文档。
2. 任务准备提交：只修改 01C 任务卡的授权路径和阶段 3 证据。

暂存前对各提交预期路径运行 `tools/check-pending-whitespace.ps1`，暂存后运行 `git diff --cached --check`。不得 stage 主工作区或 worktree 中的无关文件。

合并前重新执行 schema 5 `Show`、读取同一任务卡和队列，核对集成锁、最新 `master`、待合并 taskId 未被占用以及主工作区相关路径无 staged、unstaged、untracked 冲突。只通过 `tools/invoke-project-integration.ps1` 取得共享锁并 fast-forward；不直接在主工作区 merge。

### 4.6 全新执行 01C

项目修复集成后，在 `codex-hourly-worker` 保持 PAUSED 的情况下手动调用一次：

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/invoke-hourly-owner.ps1 `
  -Owner codex -Action RunOnce -RepositoryRoot "D:\天章游戏开发" -Model $actualModel
```

`$actualModel` 必须是执行前通过项目规定的 request metadata 通道取得的实际字符串，不静态猜测。共享入口应创建新 runId、新 worktree、新 candidate branch 和新模型会话；不得 cherry-pick 旧候选或恢复旧 session。

新候选负责重新实施 01C，并在授权内更新 `AssemblyBoundaryEditorTests`。共享入口继续负责 candidate 核验、最新 `master` 重放、正式验证、finalizer、排他 fast-forward、通知和成功清理。

若新调用返回 `existing_run`、`attention_required`、验证失败、路径冲突或任何非成功终态，立即停止并保留现场；本设计不授权自动重试。

## 五、验证设计

### 5.1 自动化合同

- `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-invoke-codex-candidate.ps1`
- 与候选 prompt、结构化终态和 runtime 相关的现有 workflow 检查。
- `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/invoke-hourly-owner.ps1 -Owner codex -Action Canary ...`

Canary 只在 runtime 和 worktree 隔离前提满足时运行；它不代替真实 01C 的移动路径 fixture。

### 5.2 任务事实与文本

- `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-task-cards.ps1 -TaskId U-ARCH-REBUILD-01C -Postcondition CodexDispatchReady -ExpectedRoute codex_execute`
- `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-review-text.ps1 -Paths AGENTS.md,CLAUDE.md,开发管理`
- 任务卡 JSON 解析、路径唯一性与队列投影检查。

### 5.3 重新执行后的 01C

新候选必须按任务卡运行并记录：

- `tools/check-unity-assembly-boundaries.ps1`；
- `tools/check-data-chain.ps1`；
- `.NET` 编译入口；
- Unity EditMode；
- 任务卡后置状态检查；
- pending whitespace 与 cached diff 检查。

Unity EditMode 允许继续存在的失败只能是任务开始前已登记且输入未变化的无关基线；两项 `AssemblyBoundaryEditorTests` 必须通过。若 .NET 入口因 Unity 生成文件缺失不可运行，必须按任务卡验证合同报告且不得用它掩盖 Unity EditMode 相关失败。

### 5.4 最终状态

- 新 01C 正式提交可由 `master` 到达，旧候选 SHA 不被直接集成。
- `U-ARCH-REBUILD-01C` 达到任务卡规定的关闭或非 ready 后置状态。
- schema 5 `runs.codex=null`、`runs.deepseek` 保持原事实、`activeTaskIds` 不再包含 01C、集成锁为空。
- 新 run 的成功 worktree 按精确合同清理；旧失败 worktree继续保留证据。
- `codex-hourly-worker` 保持 PAUSED；不自动恢复定时执行。

## 六、停止条件

- 无法通过 automation 管理能力确认或暂停 Codex 小时入口。
- 旧 run 的 runtime、branch、HEAD、worktree、候选 SHA 或 recoveryReason 任一不一致。
- 主工作区相关路径已有人工改动，或 `master` 在合并核验后变化。
- 修复需要删除旧证据、改写历史提交、恢复旧 session 或新增兼容状态。
- 01C 重新实施发现任务卡未授权的新路径、架构事实冲突、跨领域半提交、静默默认 ID、新旧资产双写或未批准内容/数值创作。
- 两条阶段 3 依赖无法同时满足程序集检查和 Unity EditMode。
- Canary、路径 fixture、任务卡检查或共享集成失败。

命中任一停止条件时保留 PAUSED、runtime、worktree、branch 和验证证据，并向负责人报告；不得通过重试、自动解冲突或额外状态绕过。

## 七、完成条件

同时满足以下条件才算修复完成：

1. 旧失败 run 已按精确合同关闭且证据仍保留。
2. 移动候选正例返回完整源/目标路径，rename-compressed 负例稳定失败关闭。
3. 候选 prompt、共享校验器和自动工作流规则使用同一 `--no-renames` 口径。
4. 01C 已获批准的程序集测试路径授权，没有扩大其他业务范围。
5. 全新 run 从最新 `master` 重新执行并完成 01C；两项相关程序集测试通过。
6. 正式提交、任务后置状态、runtime、集成锁和成功清理证据全部一致。
7. 主工作区无关改动未被修改、暂存或提交。
8. Codex 小时入口保持 PAUSED，等待负责人单独决定是否恢复。
