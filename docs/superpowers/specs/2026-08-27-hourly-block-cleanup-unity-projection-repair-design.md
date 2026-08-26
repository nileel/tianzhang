# 小时自动化 blocked 清理与 Unity 生成投影修复设计

## 状态

- 日期：2026-08-27
- 状态：用户已批准设计，待书面规格复核后实施
- 事故线程：`01a03edb-a0a8-7f60-a0b6-9265985f8dbc`
- 事故任务：`M-EXP-SEED-UNITY-ASMDEF-01`
- 事故 run：`de7ed3d3-3e93-4082-8e51-dd932a4f03db`
- 阻塞正式提交：`6fc20d6ead4d6bce482341c7b128db5688c57ab0`

## 已证实根因

1. 任务卡要求执行 `dotnet build src/TianZhang.EditModeTests.csproj`，但 Unity 生成的 `*.csproj` 被 Git 忽略，不会出现在新建的自动化 linked worktree。事故 worktree 中该文件不存在，构建实际返回 `MSB1009`，candidate 因而以 `unity_generated_projection_missing` 阻塞，没有产生业务修改。
2. blocked 状态已经正确进入 `master` 并关闭 schema 5 run；随后清理失败。`Remove-ExactSuccessfulWorktree` 会用 `taskId` 和 `baseCommit` 复核冻结输入，但 blocked 调用只传入 `runId`、worktree 和分支字段。PowerShell StrictMode 访问缺失属性后进入 catch，返回 `retained_cleanup_failed`。
3. 相同的残缺清理对象还出现在 `no_candidate` 和维护型直接决策路径，属于同一调用合同缺陷，不应只修事故分支。

## 目标

1. 让 `M-EXP-SEED-UNITY-ASMDEF-01` 在只含 Git 事实的新 worktree 中具备完整、可重复的验证入口，并重新成为合法 ready 任务。
2. 让所有成功关闭 run 后的清理调用继续携带完整 run 证据，保留现有冻结输入、分支、HEAD、主分支可达性和工作树清洁检查。
3. 在修复正式进入 `master` 后，按精确证据删除事故遗留 worktree 和对应临时分支。

## 非目标

- 不把 `.csproj`、`.sln`、`Library/`、`Temp/` 或 Bee 输出纳入 Git。
- 不从主工作区或其他 worktree 复制生成投影、`obj`、`bin` 或 Unity 缓存。
- 不新增 runtime 状态、重试、恢复队列、兼容分支、后台守护或第二清理入口。
- 不修改 asmdef、asmref、C# 业务源码、Unity 场景、Prefab、自动化配置或触发时间。
- 不改变 `M-EXP-SEED-UNITY-META-01` 及其他当前 run 的事实。

## 方案选择

### 采用：权威 Unity 验证独立于生成 csproj

命名程序集的权威编译与 EditMode 测试继续由 `tools/run-unity-editmode-tests.ps1` 执行。`TianZhang.EditModeTests.csproj` 只在 Unity 已生成该投影时提供快速 `dotnet build`，缺失本身不是业务 blocker，也不允许手工修补。

这使任务的必需验证只依赖 Git 中的 Unity 项目、asmdef／asmref、边界检查器和正式 Unity 入口，符合自动化 worktree 的隔离合同。

### 不采用：自动化入口统一生成 csproj

小时入口不增加 Unity 工程生成步骤。该方案会让所有任务承担 Unity 启动、项目导入和 IDE 投影生成的额外失败面，并把一次任务卡问题扩大成共享入口职责。

### 不采用：复制主工作区生成投影

主工作区生成物可能陈旧且受当前 Editor 状态影响。跨 worktree 复制会破坏输入冻结和缓存隔离，不具备可审核的 Git 来源。

## 修复设计

### 一、Unity 验证与事实说明

同步修改以下事实说明：

- `开发管理/开发-技术经验.txt#asmdef 分层后的 csproj 排查`
- `UNITY_STRUCTURE.md#验证入口`
- `UNITY_STRUCTURE.assemblies.md#验证提示`

统一合同为：

1. `*.csproj`／`*.sln` 是 Unity 生成的非源文件，不得手改或提交。
2. 权威验证是 `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/run-unity-editmode-tests.ps1`。
3. 只有生成投影已经存在时，才执行 `dotnet build src/TianZhang.EditModeTests.csproj --no-restore` 作为快速检查；缺失时直接走权威 Unity 验证，不从其他工作区借用投影。
4. `namespace does not exist`、`ProjectReference` 或程序集边界问题仍先以 asmdef／asmref 和边界检查器定位；普通无程序集语义的 C# 错误不得泛化命中该经验。

### 二、任务重新 ready

修改 `M-EXP-SEED-UNITY-ASMDEF-01` 的任务卡：

- `dispatchState` 从 `blocked` 改回 `ready`。
- `blockedBy` 保持空数组。
- `stateReason` 说明生成 csproj 在 clean worktree 中可缺失，权威 Unity 验证合同已经冻结，任务重新可执行。
- 必查范围继续要求理解生成投影用途，但不把文件存在性当作开工前置。
- 必需验证改为程序集边界检查、Unity EditMode、匹配器 fixture 和任务卡检查；快速 dotnet 构建仅在投影存在时执行，不作为完成门槛。
- 停止条件只保留真实语义冲突：权威 Unity 编译／测试失败、当前程序集所有者与经验描述冲突、`Assembly-CSharp` 恢复为活动所有者或没有稳定排除例。

同步更新：

- `开发管理/任务列表/管理与自动化任务.txt`
- `开发管理/当前任务队列.txt`

该卡按事故前等待顺序恢复为剩余 P1 种子任务中的首项，不重排其他既有行：若 `M-EXP-SEED-UNITY-META-01` 届时仍为 ready，则插在它之前；若该卡已经完成，则插在下一张仍存续的 P1 种子任务之前，不恢复已完成任务。只有实施 worktree 中的 Unity EditMode、程序集边界和任务投影检查全部通过，才允许把本卡写为 ready 并集成。

### 三、清理调用合同

不修改 `Remove-ExactSuccessfulWorktree` 的证据门槛。修改三个调用路径，使它们都传入原始完整 `$run`：

1. 维护型直接决策：将正式状态分支／HEAD 写回 `$run.canonicalBranch` 与 `$run.canonicalHead`，再传入 `$run`。
2. `no_candidate`：将 candidate branch 与 base commit 记录为本次清理的 canonical branch／HEAD，再传入完整 `$run`。`taskId=QUEUE-MAINTENANCE` 继续触发现有输入复核豁免。
3. `blocked`：将 state-block 分支／正式 HEAD 写回 `$run`，再传入完整 `$run`。

普通 completed／maintenance_completed 路径已经传入完整 `$run`，保持不变。不得通过允许缺失 `taskId`／`baseCommit`、跳过 StrictMode 或放宽输入复核来掩盖调用错误。

### 四、回归合同

在 `tools/check-automation-workflow.ps1` 增加静态合同：共享入口的 `Remove-ExactSuccessfulWorktree` 调用不得再用现场拼装的 `[pscustomobject]` 代替完整 run，并必须保留三条状态路径写回 canonical 证据的语句。

现有 `tools/test-hourly-task-input-materialization.ps1` 继续动态证明：完整 run 下会重新核验自动化输入并安全删除已集成、干净、精确匹配的 worktree。`tools/test-check-automation-workflow.ps1` 继续覆盖正式 checker，使调用合同缺失会令测试失败。

不新增第二套清理测试框架。

### 五、事故遗留清理

代码和任务投影正式进入 `master` 后，普通管理上下文重新执行 schema 5 `Show`，并只在以下条件全部满足时处理遗留现场：

1. 两个活动 run 均不引用事故 worktree。
2. 集成锁为空。
3. `master` 包含 `6fc20d6ead4d6bce482341c7b128db5688c57ab0`。
4. 遗留 worktree HEAD 精确等于该 SHA，当前分支精确等于 `codex/automation/codex/de7ed3d3-3e93-4082-8e51-dd932a4f03db/state-block-ff586416fde5`，且 staged、unstaged、untracked 集合均为空。
5. schema 5 中不存在 run `de7ed3d3-3e93-4082-8e51-dd932a4f03db`。

满足后，使用 Git worktree 的精确删除命令移除：

- `D:\天章游戏开发\.worktrees\automation\de7ed3d3-3e93-4082-8e51-dd932a4f03db\codex`
- 上述 state-block 分支
- 同 run 的 candidate 分支（仅在该精确 ref 仍存在时）

任一证据变化都停止清理并保留现场，不使用 `clean`、`reset`、stash、通配符或递归删除。

## 预期修改路径

- `tools/invoke-hourly-owner.ps1`
- `tools/check-automation-workflow.ps1`
- `开发管理/开发-技术经验.txt`
- `UNITY_STRUCTURE.md`
- `UNITY_STRUCTURE.assemblies.md`
- `开发管理/任务卡/M-EXP-SEED-UNITY-ASMDEF-01.txt`
- `开发管理/任务列表/管理与自动化任务.txt`
- `开发管理/当前任务队列.txt`
- 本规格文档

测试脚本只有在现有 fixture 无法覆盖新增 checker 断言时才允许修改 `tools/test-check-automation-workflow.ps1`；不得顺带改写其他测试。

## 验证矩阵

| 关注面 | 命令／证据 | 通过条件 |
|---|---|---|
| PowerShell 语法与共享合同 | `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-automation-workflow.ps1 -RequireLegacyRetired` | 输出 `check-automation-workflow: OK` |
| checker fixture | `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-check-automation-workflow.ps1` | 输出 `test-check-automation-workflow: OK` |
| 冻结输入与成功清理 | `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-hourly-task-input-materialization.ps1` | 输出 PASS，精确测试 worktree 被删除 |
| Unity 程序集边界 | `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-unity-assembly-boundaries.ps1` | 现行程序集／asmref 集合通过 |
| 权威 Unity 验证 | `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/run-unity-editmode-tests.ps1` | EditMode XML 完整通过；若已有 Unity Editor 阻止 batchmode，则先停止实施并报告，不先 requeue |
| 任务投影 | `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-task-cards.ps1` | 全局投影通过 |
| ready 后置条件 | `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-task-cards.ps1 -TaskId M-EXP-SEED-UNITY-ASMDEF-01 -Postcondition CodexDispatchReady -ExpectedRoute codex_execute` | 任务为 `codex_execute/codex/ready` 且三层投影一致 |
| 文本合同 | `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-review-text.ps1 -Paths AGENTS.md,CLAUDE.md,开发管理` | 通过 |
| 路径与空白 | `tools/check-pending-whitespace.ps1`、`git diff --cached --check` | 只包含预期路径且无非语义空白问题 |

## 集成与回滚

- 实施必须位于独立 task worktree，不触碰主工作区当前未提交修改或活动 automation worktree。
- 合并前从主工作区重新 `Show`，重读目标任务卡与队列，确认活动 run、集成锁和路径冲突均允许集成；随后通过 `tools/invoke-project-integration.ps1` 持有共享锁 fast-forward。
- 修复提交进入 `master` 前，可直接放弃未集成的实施 worktree；不得影响主工作区。
- 修复提交进入 `master` 后若需回滚，只回滚该正式修复提交并重新核对任务投影。若目标任务已经被新 run 领取或完成，不机械恢复旧 blocked 状态，转入普通管理判断。
- 遗留 worktree 清理是 Git 外部现场操作，不随提交回滚；执行前的精确证据记录必须保留在最终报告中。

## 完成条件

1. 三条受影响清理路径都传递完整 run，自动化 checker 与输入物化清理测试通过。
2. Unity 事实源不再把 clean worktree 中缺少生成 csproj 视为 blocker，权威 Unity EditMode 验证实际通过。
3. `M-EXP-SEED-UNITY-ASMDEF-01` 以完整 runnable 合同重新进入 ready 队列固定位置。
4. 修复正式提交通过共享锁进入 `master`，未包含任何主工作区用户改动。
5. 事故遗留 worktree／临时分支在精确证据满足时已清理；若证据不满足，则保留并报告具体不一致，而不宣称完成清理。
