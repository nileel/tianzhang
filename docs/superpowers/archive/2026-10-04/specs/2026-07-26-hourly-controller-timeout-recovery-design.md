# 每小时控制器超时恢复与 N-FPD-GROWTH-01 收尾设计

## 状态与范围

- 方案已于 2026-07-26 获用户批准：完成现有 `N-FPD-GROWTH-01` 半成品，并独立修复控制器超时后未保存 recovery 的缺口。
- 两个切片分别验证、分别提交；任一切片失败不以另一切片的改动掩盖。
- 本设计不改变每小时 schedule、3600 秒租约、runtime schema、队列顺序或 BattleSim 战斗规则。

## 已核验问题

1. `N-FPD-GROWTH-01` 责任方已经向 `simulations/BattleSim/Program.cs` 写入 162 行四阶段成长审计；Release build 通过，但任务卡指定的默认 BattleSim 运行在 3300 秒后超时，因此没有数值完成证据，任务仍为 ready。
2. 外层控制器与责任方内部验证使用同一 3300 秒上限。责任方在读取、设计、编辑之后才开始长验证，外层先耗尽上限，未能等待责任方终态。
3. `tools/invoke-codex-responsibility.ps1` 只有在 runner 返回后才取得 session ID、比较工作区并进入 `SaveInterruption`。外层等待被截断后，该收尾逻辑没有执行，最终形成未提交路径、无 recovery、租约自然过期。
4. 后续两轮控制器把 `simulations/BattleSim/Program.cs` 识别为两个 ready 任务的共同路径冲突，runtime 按稳定指纹进入逻辑暂停。

## 目标

- 用任务卡要求的真实 BattleSim 结果决定现有四阶段成长参数是否可以锁定。
- 成功时完成验证记录、设计事实同步、任务归档和队列更新；失败时保留任务为 ready，不提交候选数值。
- 让固定调用器在外层 3300 秒工具上限之前主动结束超长责任方，并使用已有 interruption recovery 与租约收尾机制。
- 完成后恢复逻辑暂停，使下一轮控制器能够按现有队列继续调度。

## 非目标

- 不为了缩短验证而优化、裁剪或改写无关 BattleSim 矩阵。
- 不把定向审计替代任务卡要求的默认完整运行。
- 不延长每小时租约，不新增重试器、checkpoint、第二套状态机或新的 recovery 类型。
- 不修改自动化 schedule、模型、推理强度或通知策略。
- 不创作功法专属机制，不修改无关伤害表、槽位、位格、金丹或丹相规则。
- 本设计文档是切片一开始前已知的独立工作流改动，只在切片二提交；切片一不得暂存它，也不得把它误判为本任务新产生的路径冲突。

## 切片一：完成 N-FPD-GROWTH-01

### 输入与所有者

- 候选实现：`simulations/BattleSim/Program.cs` 中的 `RunFoundationGrowthAudit`、四阶段份额和通用核心曲线。
- 事实源：任务卡列出的集成设计、角色数值、修行境界、功法规范与功法设计原文。
- 任务状态：`开发管理/任务卡/N-FPD-GROWTH-01.txt`、数值与战斗 backlog、当前队列和归档。

### 处理顺序

1. 逐项复核现有 162 行与事实源，确认它只做迁移审计，没有提前改动五段运行时结构或无关战斗规则。
2. 运行 Release build。
3. 运行任务卡指定的默认 BattleSim 命令。使用可持续汇报的 yielded/wait 方式，人工观察上限为 90 分钟；该上限只防止无限等待，不构成通过证据。
4. 只有完整进程以 0 退出并返回可用输出时，才比较练气出口、四阶段分配、金丹入口和代表 Build，写入 `开发管理/任务归档/验证记录/四阶段成长与道基核心曲线数值验证记录.txt`。
5. 将通过验证的预算、容差和曲线参数同步到任务卡允许的设计事实源；旧五段只保留为可追溯历史基线。
6. 更新数值与战斗 backlog、移出当前队列、归档任务卡，并创建只包含本任务路径的提交。

### 停止条件

- 完整 BattleSim 非 0 退出、90 分钟仍未结束、输出不完整或数值断言失败。
- 需要修改无关战斗矩阵才能让验证完成。
- 候选参数要求功法专属机制或改变任务卡禁止的规则。
- 相对切片一启动基线出现不属于任务预期路径的新工作区变化。

命中任一条件时不写结论、不归档、不提交候选参数，保留证据并报告具体阻塞。

## 切片二：固定调用器预留收尾窗口

### 时限设计

- `tools.shell_command` 的外层单轮上限保持 3300 秒。
- `tools/invoke-codex-responsibility.ps1` 增加可测试的责任方内部截止参数，生产默认 3000 秒。
- 300 秒差额专门用于终止子进程、读取 Git/runtime、保存 recovery、记录结果、释放租约并输出结构化终态。
- 不把 3000 秒传播给业务命令；它约束的是整个责任方子进程，而不是新增一层业务重试。

### session ID 与终止

1. `tools/codex-cli-session.ps1` 在首次且唯一的 `thread.started` 事件到达时，继续输出既有 `session_started` / `running` 进度，并在仅供固定调用器消费的 stderr 行中携带 session ID。
2. 固定调用器异步收集 stdout/stderr，并以内部截止参数等待 runner。
3. runner 正常退出时，继续要求唯一 JSON summary，并核对 summary session ID 与提前捕获的 session ID 一致。
4. runner 超过内部截止时，固定调用器终止整个子进程树、等待流关闭，并用已捕获的唯一 session ID 构造明确的超时结果；不把超时伪装成业务失败或成功。
5. 随后的 Git/runtime 核验沿用现有逻辑：若出现新增未提交路径且 session ID 有效，调用 `SaveInterruption`，再以 `interruption_recovery_saved` 记录失败并释放租约。
6. 若超时前没有取得唯一 session ID，则不得伪造 recovery：没有新增路径时以 `no_verified_outcome` 记录并释放租约；存在新增路径时返回 `changed_without_session`、保留租约与现场供人工处理。该异常分支不冒充正常 interruption recovery。

### 所有者与改动边界

- 运行与 session 捕获：`tools/codex-cli-session.ps1`。
- 子进程截止与统一收尾：`tools/invoke-codex-responsibility.ps1`。
- 稳定规则：`开发管理/自动工作流规则.txt`。
- 合同检查与回归：现有 `check-automation-workflow.ps1`、`test-codex-cli-session.ps1`、`test-invoke-codex-responsibility.ps1`、`test-check-automation-workflow.ps1`。
- 本设计文档与上述所有者一起进入工作流修复提交；不修改 automation TOML。

## 验证

### N-FPD-GROWTH-01

- `dotnet build -c Release --no-restore simulations/BattleSim`
- `dotnet run --no-build -c Release --project simulations/BattleSim`
- `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-review-text.ps1 -Paths 开发管理,docs,simulations`
- `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-data-chain.ps1`
- `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-task-cards.ps1 -TaskId N-FPD-GROWTH-01`
- 对本切片预期路径运行 `tools/check-pending-whitespace.ps1`、`git diff --check` 和暂存后的 `git diff --cached --check`。

### 工作流修复

- runner 正常成功、正常失败、无 session、多个 session 的既有测试保持通过。
- 新增一个短内部截止的慢 runner 用例：先产生唯一 session 和预期路径改动，再超过截止；断言子进程树结束、interruption recovery 保存、失败结果记录、租约释放。
- 新增正常快速 runner 用例，证明内部截止不改变成功提交核验。
- 运行：
  - `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-codex-cli-session.ps1`
  - `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-invoke-codex-responsibility.ps1`
  - `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-check-automation-workflow.ps1`
  - `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-automation-workflow.ps1`
  - `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-review-text.ps1 -Paths AGENTS.md,CLAUDE.md,开发管理`
  - 对本切片预期路径运行 whitespace、`git diff --check` 和暂存后的 `git diff --cached --check`。

## 提交与恢复

1. 提交一只包含 `N-FPD-GROWTH-01` 的实现、验证记录、事实同步与任务生命周期文件。
2. 提交二只包含固定调用器、runner、稳定规则、合同/测试和本设计文档。
3. 两个提交均成功且工作区干净后，调用 `Show` 确认 `lease=null`、`recovery=null`、`pauseRequested=true`。
4. 调用 `ClearBlocking` 清除稳定冲突指纹；自动化配置按完整现有字段保持 `ACTIVE`。
5. 再次调用 `Show`，完成条件为 `leaseStatus=none`、`recovery=null`、`blocking.count=0`、`pauseRequested=false`。

## 回滚边界

- 切片一失败时不创建完成提交，现有候选改动保持可检查状态。
- 切片二测试失败时不清除逻辑暂停；只回到该切片修改前的脚本行为，不触碰已经完成的业务提交。
- 不用 `reset --hard`、自动 stash、checkout、clean 或删除来处理任何失败现场。
