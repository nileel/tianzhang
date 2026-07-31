# 天章自动化多通道并行开发设计

> 日期：2026-07-31
> 状态：用户已批准对话设计，待书面规格复核
> 当前启用通道：Codex、DeepSeek V4 Flash
> 当前最大自动并发：2
> 继承：`docs/superpowers/specs/2026-07-15-hourly-automation-controller-v3-design.md`、`docs/superpowers/specs/2026-07-15-hourly-automation-controller-v3-1-candidate-discovery-design.md`、`docs/superpowers/specs/2026-07-28-external-terminal-contract-repair-design.md`
> 基线：`8d27e31`（切换 DeepSeek V4 Flash 并重分配 AI 工作）

## 1. 背景与问题

现有 `tzg-hourly-controller` 使用一个覆盖整个仓库写入期的全局单写入租约。每轮只选择一个任务，责任方直接在主工作区当前分支实施、验证并提交。该设计能可靠保护人工改动、Git index、任务队列和恢复状态，但不能让 Codex 与 DeepSeek V4 Flash 同时开发两个互不依赖的任务。

当前任务卡已经提供 `expectedPaths`，队列也保存固定顺序、route、owner、依赖和 dispatchState，具备并行候选判定的基础。真正的共享冲突来自：

- 两个责任方共同写主工作区和 Git index；
- `开发管理/当前任务队列.txt`、source backlog、任务卡和 `开发管理/AI合作沟通.txt` 是收尾热点；
- 当前租约、恢复和 wrapper 都假设只有一个 taskId、一个 RepositoryRoot 和一个终态；
- 手动开发可能在自动 Worker 运行期间产生新提交或未提交路径。

本设计采用“并行 Worker、串行集成”的结构：业务实施在隔离 worktree 中并行，主分支状态变更只由固定集成器按全局队列顺序串行完成。

## 2. 已批准的产品决策

1. 当前固定启用两个自动通道：Codex 与 DeepSeek V4 Flash。
2. 保持单一全局队列和原有行顺序，不建立角色私有队列。
3. 每小时最多一批；每条启用通道每批最多领取一个任务，本轮结束前不滚动补位。
4. 一路失败、阻塞或需要决定时，另一路独立继续。
5. Worker 可以任意顺序完成，但集成必须按本批任务在全局队列中的原始顺序执行。
6. 手动开发优先；自动化不得 stash、reset、clean、覆盖或自动解决人工冲突。
7. 架构使用通用 `lanes[]`，不把状态结构写死为 Codex/DeepSeek 两个字段。
8. 首版只启用两路；未来新增第三个共事 AI 时允许三路同时运行，但必须先补齐其身份、wrapper、权限、限流和真实 canary。
9. DeepSeek 成果仍进入 Codex 独立复审，不因并行取消审核隔离。

## 3. 目标

- 在两个独立任务安全可并行时，让 Codex 与 DeepSeek Worker 的实际执行时间重叠。
- 保留全局队列顺序、任务卡唯一事实、路径授权、最小验证和复审规则。
- 让每个 Worker 拥有独立工作区、分支、任务 claim、session、验证结果和终态。
- 让固定集成器原子地落地业务修改与任务投影，不留下业务已合并但队列未更新的稳定中间状态。
- 让单路失败不取消另一路，同时让冲突、选择过期和中断恢复稳定失败关闭。
- 允许手动 worktree 与自动 Worker 同时工作；只有安全集成受人工冲突约束。
- 为未来三路及以上启用保留通用状态和冲突算法，不提前创建不存在的 AI 配置。

## 4. 非目标

- 不建立动态 Worker 池，不按机器负载自动扩缩容。
- 不在同一小时内为空闲通道滚动补任务。
- 不让两个 Worker 同时写主工作区、Git index 或共享管理投影。
- 不建立角色私有优先级或第二套队列。
- 不自动合并有 Git 冲突、语义冲突、依赖变化或人工事实变化的结果。
- 不取消 DeepSeek 的 `codex_review`、未审核标记和双提交交接要求。
- 不直接编辑 `~/.codex/automations/**/automation.toml`；实时 prompt 与状态只能通过自动化管理能力变更。
- 不在首版实现第三个真实 AI、通用插件市场或未知 provider 适配层。

## 5. 总体架构

### 5.1 协调器租约

全局租约改为“协调器租约”。它保证同一仓库同一时间只有一个自动控制器能够：

- 快照队列并选择一批任务；
- 创建或恢复 lane；
- 创建和删除自动 worktree；
- 运行固定集成器；
- 更新主分支、共享任务投影和自动化 runtime。

协调器租约不再表示仓库内只能有一个自动 Worker。Worker 不持有主工作区写权限，只能写各自的隔离 worktree。

### 5.2 通用 lane

runtime 使用数组保存启用通道：

```text
lanes[]:
  laneId
  owner
  identity
  acceptedRoutes[]
  invoker
  taskClaim
  worktree
  branch
  baseCommit
  workerPaths[]
  processOrSession
  workerTerminal
  integrationState
```

首版配置：

- `laneId=codex`：接受 `codex_execute`、`codex_review`。
- `laneId=deepseek`：接受 `external_execute` 且 `owner=deepseek`。
- `maxConcurrent=2`。

数组、选择算法、两两冲突检查和集成队列不得出现只支持固定两个位置的字段。测试使用一个未接真实 provider 的模拟第三 lane，证明状态与算法可扩展；生产配置不得启用该模拟 lane。

### 5.3 隔离 worktree

每个选中任务使用独立自动化 worktree 和唯一任务分支。目录、分支、batchId、laneId、taskId 和基线提交必须可相互校验。Worker：

- 只在传入 worktree 工作；
- 不切换分支或创建额外 worktree；
- 不访问其他 lane 的 worktree；
- 不修改主工作区；
- 不调用协调器租约的 RecordResult、Release 或集成动作；
- 不推送远端。

自动 worktree 生命周期由固定控制面管理，不复用手动 `.worktrees/` 的未决分支。

### 5.4 固定集成器

固定集成器是唯一允许把 Worker 结果落地主分支的组件。它不做业务判断、不生成代码、不自动解决冲突，只执行：

- 校验 task claim、baseCommit、候选提交、路径授权和结构化结果；
- 按队列原序控制集成放行；
- 在干净集成环境中应用候选提交；
- 机械更新任务卡、队列、backlog 和交接投影；
- 运行固定控制面检查；
- 创建 canonical businessCommit 和必要的 handoffCommit；
- 记录 lane 终态并安全清理 worktree。

## 6. 路径模型

任务卡在既有 `expectedPaths` 允许上界之外增加明确的执行路径分类：

- `workerPaths`：业务代码、数据、测试和本任务验证记录。Worker 候选提交只能包含这些路径。
- `coordinatorPaths`：任务卡、当前队列、source backlog、`AI合作沟通.txt` 及其他必须串行更新的共享管理路径。

`expectedPaths` 是两者的并集，继续作为最终任务允许上界。分类规则必须显式写入任务卡并由 `check-task-cards.ps1` 验证，不从目录名称、扩展名或经验猜测。

并行冲突判断只比较候选任务的 `workerPaths`，同时额外验证：

- 两个任务不存在直接或传递依赖；
- 一个任务不会修改另一个任务的事实源或验收输入；
- 两个任务没有相同 taskId、父子迁移关系或同一不可分割数据链；
- 任一任务的 workerPaths 不与当前人工 staged、unstaged、untracked 路径相交。

共享 coordinatorPaths 不会使所有任务天然冲突，因为它们由集成器串行处理。

## 7. 每小时选题算法

1. `Show` 协调器状态；存在未关闭 batch 时先恢复，不选择新批次。
2. 取得协调器租约并记录唯一 `batchId`、队列 HEAD、Git HEAD 和人工路径基线。
3. 按当前队列原始行顺序检查第一项可安全执行的任务，并绑定匹配 lane。
4. 从该行之后继续扫描：
   - lane 尚未占用；
   - route、owner 与 lane 合法；
   - task card 为同一 ready 投影；
   - 与已选任务无依赖和 workerPaths 冲突；
   - 与人工路径无冲突。
5. 为每个已选任务创建 task claim。没有安全第二项时无损退化为单路。
6. 最多启动每个启用 lane 一个 Worker；本批运行期间不领取第三项。
7. 记录每个任务的原始队列序号，作为唯一集成顺序。

未来启用第三个 AI 时，步骤 4 继续扫描并为第三个空闲 lane 选择第一项安全候选；最大并发由已批准的生产配置给出，而不是自动取无限值。

## 8. Worker 输出合同

Worker 成功时只创建内部候选提交，不直接创建主分支 canonical 提交。结构化终态至少包含：

- `status=completed`；
- identity、laneId、taskId、batchId 和 session；
- 完整候选提交 SHA；
- 实际 changedPaths；
- 任务要求的验证结果；
- 原问题、具体交付、影响、边界、后续；
- 期望的任务状态转换；
- 未验证项和残留风险；
- DeepSeek 所需交接正文。

现有 `needs_decision`、`blocked`、`failed` 终态继续保留稳定 detailCode 和可恢复 session。Worker 不得返回另一任务、另一 lane 或另一队列位置。

候选提交不含 coordinatorPaths，不被视为项目正式交付，也不发送成功通知。

## 9. 集成触发与顺序

集成采用“完成事件触发、按队列顺序放行”：

- 队列序号较前任务先完成：立即尝试集成，不等待其他 lane。
- 较后任务先完成：保存其候选提交和验证结果，等待所有更前任务进入终态。
- 更前任务成功：先集成更前任务，再重新核验较后任务。
- 更前任务失败、blocked 或 needs_decision：先记录该 lane 终态；较后任务若不依赖它，仍可继续集成。
- 本批不会因一个 lane 结束而补充新任务。

在集成每个候选前必须重新读取：

- 当前主分支 HEAD 和基线后的提交；
- 人工 staged、unstaged、untracked 路径；
- 同一任务卡、队列行、route、owner、dispatchState 和依赖；
- 已经集成的本批前序任务结果；
- workerPaths、coordinatorPaths 和实际 changedPaths。

## 10. 原子提交与任务状态

成功集成一个任务的固定顺序：

1. 验证候选提交只包含 workerPaths，提交可达且父提交等于记录基线。
2. 在干净集成环境中以非提交方式应用候选修改。
3. 固定集成器根据 Worker 的结构化结果机械更新 coordinatorPaths。
4. 对更新后的任务投影运行对应 `check-task-cards.ps1` 后置条件。
5. 运行路径、空白、提交元数据和 Git 差异检查。
6. 使用现有 finalizer 创建一个 canonical businessCommit。
7. DeepSeek 任务再创建只包含正式交接投影的 handoffCommit，并把同一任务留在 `codex_review/codex/ready`。
8. 核验提交父子关系、任务投影和主工作区无路径外变化。
9. 记录 `integrated`，发送与任务真实状态一致的通知。

两个任务始终形成各自独立的提交序列，不压成一个批次大提交。Flash 在本批完成后不会抢占正在运行的 Codex lane；其复审任务在下一小时按全局队列顺序处理。

## 11. 手动开发优先

自动化与手动开发可以同时进行，但手动工作拥有落地主分支优先权：

- task claim 对自动和手动选题均可见；手动流程必须跳过已被自动化 claim 的同一任务，除非负责人明确取消该 lane。
- 手动队列任务继续使用独立 worktree；不会占用自动 maxConcurrent。
- 自动选题跳过 workerPaths 与人工未提交路径相交的候选。
- Worker 运行期间出现新的人工提交或未提交路径不会修改其隔离现场。
- 集成时发现人工路径、任务事实或依赖变化，自动结果不覆盖、不 stash、不 reset。
- 人工修改同一任务或其直接事实源时，即使 Git 没有文本冲突，也返回 `stale_selection`。
- 与人工工作发生路径冲突时返回 `held_conflict`，保留候选提交、worktree 和证据。

主工作区存在无关人工修改时，只有现有 Git 与项目 guard 都能证明集成不会 stage、覆盖或提交人工路径，集成器才可继续；无法证明时失败关闭。

## 12. 失败、恢复与清理

每个 batch 保存 batch 级基线和 lane 数组。Worker 终态沿用：

- `completed`
- `needs_decision`
- `blocked`
- `failed`

集成状态只增加：

- `integrated`
- `held_conflict`
- `stale_selection`

一路失败不取消另一路。协调器只在所有已选 lane 都进入可持久化终态、成功项完成集成或明确被持有后关闭 batch。

控制器中断后，下次小时触发先恢复未关闭 batch：

1. Worker 仍运行且进程/session 与 lane 记录一致：继续等待同一 Worker。
2. Worker 已完成但未集成：按原队列序号继续固定集成。
3. 进程、worktree、分支、提交或 runtime 记录不一致：停止对应 lane，保留现场，不猜测、不重建提交。
4. 已 integrated lane 不重新执行、不重复通知。
5. 未关闭 batch 处理完成前不领取新批次。

只有 integrated、无业务修改的稳定失败，或确认无需保留证据的关闭状态才删除 worktree。needs_decision、held_conflict、stale_selection 和证据不完整状态保留现场，直到普通管理上下文处理。

## 13. 安全不变量

- 同一仓库只有一个协调器租约和一个固定集成器可以更新主分支。
- Worker 永不共享 worktree、Git index、分支或 session。
- task claim、lane、worktree、baseCommit 和候选提交必须全链一致。
- Worker changedPaths 必须是 workerPaths 子集；最终提交必须是 expectedPaths 子集。
- 任何依赖、任务事实、人工路径或 HEAD 变化都在集成前重新验证。
- 不自动解决冲突，不使用 stash、reset、checkout 覆盖、clean 或强制更新主分支。
- 不允许 DeepSeek 自审；Codex 同一任务的执行与复审边界继续服从审核规则。
- provider token、私密通知配置和用户标识不进入仓库、候选提交或 runtime 明文。
- 实时 automation prompt 未同步、契约检查未通过或控制器未处于可部署状态时，不启用并行生产配置。

## 14. 测试设计

### 14.1 选择与并发

1. Codex 与 DeepSeek 两个无依赖、workerPaths 不交叉的任务被同批选择。
2. 两个 Worker 的记录时间区间真实重叠。
3. 第二项冲突、依赖或 lane 不匹配时退化为单路。
4. 队列后方存在多个候选时只选择各空闲 lane 遇到的第一项安全任务。
5. 模拟第三 lane 能被通用算法选择和排序，生产配置仍只启用两路。

### 14.2 完成与集成

1. 两路按顺序完成并形成两个独立 canonical 提交。
2. 后序任务先完成时等待，最终仍按原队列序号集成。
3. 前序任务失败、blocked 或 needs_decision，后序独立任务仍可集成。
4. 已集成一项后，后一项重新进行 HEAD、依赖和路径检查。
5. Flash 结果稳定转为 `codex_review/codex/ready`，没有自审或跳过 handoff。

### 14.3 人工冲突

1. 启动前 staged、unstaged、untracked 任一 workerPaths 冲突都跳过候选。
2. Worker 运行期间出现人工无关修改，不被 stage、提交或清理。
3. 人工修改同一业务路径返回 held_conflict。
4. 人工修改任务卡、route、owner、依赖或直接事实源返回 stale_selection。
5. 人工 worktree 与自动 worktree 的任务 claim 阻止重复领取同一任务。

### 14.4 失败与恢复

1. 单 lane 进程失败不终止另一 lane。
2. 控制器在 Worker 运行、Worker 完成待集成、第一项已集成三个位置中断后均能恢复。
3. worktree、分支、提交或 session 证据不一致时失败关闭并保留现场。
4. integrated lane 不重复执行、不重复通知。
5. 未关闭 batch 不领取新任务。

### 14.5 既有回归

- 单路选择、执行、复审、队列维护和无候选行为保持现有语义。
- 现有任务卡、租约、workspace guard、finalizer、Codex wrapper、external wrapper、通知和工作流检查测试继续通过。
- 所有测试证明用户文件不被 stage、覆盖、清理或纳入自动提交。

## 15. 部署与启用

并行自动化属于共享控制面和写入隔离变更，实施时必须分阶段验证，但不保留长期双实现：

1. 在普通管理上下文中暂停实时控制器；确认无 lease、recovery 和活动 Worker。
2. 先实现并测试通用 lane runtime、task claim、路径分类与恢复协议。
3. 实现隔离 worktree 启动和 Worker 候选提交合同。
4. 实现固定集成器和原子任务投影更新。
5. 更新控制器 prompt、项目规则、任务卡 schema、wrapper 合同和直接测试。
6. 运行临时仓库双 Worker canary，证明并发时间重叠和串行集成。
7. 运行包含模拟第三 lane 的非生产结构测试。
8. 通过自动化管理能力同步实时 prompt；不得直接编辑 automation TOML。
9. 首次生产启用前只运行无业务修改或专用 canary 任务；确认 runtime 关闭、主工作区未污染、通知与提交一致后再恢复小时调度。

部署失败时保持控制器暂停并保留诊断证据；不在生产状态下回退为两个并存的租约或集成协议。

## 16. 完成标准

- 当前生产配置只启用 Codex 与 DeepSeek，两路实际并行、每小时最多一批且不补位。
- 选题保持全局队列顺序，第二项必须通过 lane、依赖、事实源和 workerPaths 检查。
- Worker 只写隔离 worktree，主分支只由固定集成器串行更新。
- 逆序完成、单路失败、手动冲突和控制器中断均按本规格稳定处理。
- 每个任务形成独立可审计提交；Flash 交付仍进入 Codex 复审。
- 单路退化与既有安全内核回归全部通过。
- 模拟第三 lane 证明数据结构和算法无双路硬编码；生产没有虚假第三 AI。
- 实时 prompt、仓库规则、租约/runtime schema、任务卡 schema、wrapper 和检查器同步，完整自动化契约通过后才启用。
