# 小时自动化空转纠正设计

> 状态：2026-08-10 用户已批准，并授权自审通过后直接实施。

## 一、问题与根因

Codex 小时入口恢复后连续四轮均创建 `QUEUE-MAINTENANCE` run，完整启动模型后返回 `no_candidate / no_runnable_candidate / cleanup=cleaned`。共享 runtime、排他锁和清理本身正常，错误在于恢复条件：

1. `U-ARCH-REBUILD-01B` 是唯一应继续推进的 P1 卡，却因阶段 2 同时要求提前迁移 CTB 和 EnvironmentProfile 适配而命中停止条件，被保留为 `blockedBy=[] / blocked`。
2. QueueMaintenance 已被收窄为只收口“本轮刚移除最后一个具名前置”的直接下游卡，不会重新分类这个原本就是 `blockedBy=[]` 的卡。
3. 在队列没有合法候选时仍恢复小时定时器，因此每轮只能重复相同空维护。
4. 之前的 canary 只证明入口、模型、runtime 和清理可用，没有验证恢复后的真实选择结果。这不足以证明工作流恢复了生产力。

## 二、采用方案

只修两个现有边界，不新增 runtime、调度器、恢复队列、守护进程或兼容层。

### 2.1 使 01B 重新可执行

- 阶段 2 只完成唯一六角坐标、格位状态和纯 Spatial 查询。
- 允许为统一坐标类型和 namespace 机械更新直接调用者；这类更新不得改变 Character、Content、Combat 或 Feature 的状态所有权和业务语义。
- `CTBEngine` 的实际迁移延至阶段 5／`U-ARCH-REBUILD-01E`，与 Combat、回合调度和战术运行时一起完成。阶段 2 不为 CTB 建兼容壳。
- `EnvironmentProfileData`、`EnvironmentProfileRuntime` 及依赖它们的 Unity Content 转换适配延至阶段 3／`U-ARCH-REBUILD-01C`。阶段 2 只保留纯 Spatial 输入和结果；不得为提前移动 Factory 反向依赖 Content。
- 重新扫描当前源码、直接调用者、测试、asmdef 与 `.meta`，把本阶段可能修改的字面量路径写入 01B；路径完整且任务卡检查通过后才设为 `ready` 并入队。

### 2.2 真正空队列时停止重复空转

- 共享 PowerShell 内核、QueueMaintenance 合同和 schema 5 不变。
- Codex Desktop 薄触发器仅在共享入口终态同时满足以下字段时，通过 automation 管理能力把自身设为 `PAUSED`：
  - `status=no_candidate`
  - `owner=codex`
  - `taskId=QUEUE-MAINTENANCE`
  - `detailCode=no_runnable_candidate`
  - `cleanup=cleaned`
- 该终态已经证明选择时 ready 队列为空、QueueMaintenance 无事实变化、run 已关闭且现场已清理。普通失败、活动 run、路径冲突、临时跳过、通知失败或未清理现场都不得触发自暂停。
- 自暂停只修改当前 `codex-hourly-worker` 的 `status`，保留名称、prompt、schedule、project、model、reasoning effort、execution environment 和 notification policy；不得管理 DeepSeek 或其他 automation，不得直接编辑 TOML。
- 本轮把 01B 恢复为 `ready` 后，再把 Codex 定时器恢复为 `ACTIVE`；DeepSeek 保持 `PAUSED`。

## 三、控制流

1. 暂停两个写入 automation，确认 schema 5 两个 owner run 为空且集成锁可取得。
2. 在隔离 worktree 修订架构阶段边界、01B 卡、直接下游卡说明、backlog 和 ready 队列。
3. 运行任务卡、文本、空白和差异检查；只提交本轮路径。
4. 从最新 `master` 重新确认 runtime、锁、01B 状态、队列投影和主工作区路径冲突，再通过现有项目集成入口 fast-forward。
5. 通过 automation 管理能力更新 Codex trigger 的空队列自暂停合同；DeepSeek 配置不变。
6. 最后确认 01B 是队首 `codex_execute / codex / ready`，再启用 Codex。
7. 后续正常小时轮次执行队首工作；任务链确实耗尽后，首个完整 QueueMaintenance 空结果令 Codex 自暂停，不再产生下一小时空任务。

## 四、验证

- 任务事实：`check-task-cards.ps1 -TaskId U-ARCH-REBUILD-01B -Postcondition CodexDispatchReady -ExpectedRoute codex_execute -OutputJson`，并运行全局任务卡检查。
- 文本与差异：`check-review-text.ps1`、`check-pending-whitespace.ps1`、`git diff --check`、暂存后的 `git diff --cached --check`。
- 自动选择：在修订 worktree 和集成后的 `master` 分别运行 `select-hourly-task.ps1 -Owner codex`，必须选择 01B，而不是 QueueMaintenance。
- 配置：通过 automation 管理能力查看 Codex 配置，确认自暂停合同存在且最终为 `ACTIVE`；DeepSeek 最终仍为 `PAUSED`。不以读取或编辑 TOML 代替配置更新。
- 运行现场：集成前后调用 schema 5 `Show` 并检查集成锁，两个 owner run 必须为空、锁可取得。
- 不重复运行与本轮无关且输入未变化的 Unity、BattleSim、数据链或全套 automation canary。

## 五、停止条件

- 实时扫描发现唯一坐标迁移仍必须改变 Character／Content／Combat 业务所有权，而不能只做机械调用者更新时，01B 不设为 ready。
- 01B 字面量路径无法覆盖直接调用者、测试或 `.meta` 时，不以宽泛目录、猜测路径或兼容层绕过。
- automation 管理能力不能只改变自身 status 并保留其他字段时，不直接编辑 TOML，也不新增第二套暂停机制。
- runtime 非空、集成锁被持有、主工作区相关路径冲突或验证失败时停止集成，保留隔离 worktree 证据。

## 六、完成条件

1. 01B 的阶段边界不再要求在阶段 2 提前解决 CTB／Content 所有权，精确路径已冻结且合法进入 ready 队列。
2. QueueMaintenance、schema 5、锁、finalizer 和提交形成逻辑未增加新分支或新状态。
3. Codex 只在带完整清理证据的真实空队列终态后暂停自身。
4. 集成后 Codex 为 `ACTIVE`、DeepSeek 为 `PAUSED`，下一轮能选择 01B；任务链真正耗尽后不会继续按小时制造空任务。
