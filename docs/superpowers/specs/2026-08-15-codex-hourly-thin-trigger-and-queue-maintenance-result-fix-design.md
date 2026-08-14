# Codex 小时薄触发器与 QueueMaintenance 成功终态修复设计

> 日期：2026-08-15
> 状态：方案已批准，DeepSeek 审核反馈已吸收，进入实施
> 基线：`213f7d0b08e87431bcee3a667d38d6bd2501ebef`

## 一、结论

本次在同一条最小变更链中完成两件直接相关的工作：

1. 修复普通 QueueMaintenance 已成功集成并关闭 runtime 后，因缺少可选 `decisionId` 属性而被误报为 `attention_required/hourly_formal_failed` 的确定性 Bug。
2. 完全移除 `codex-hourly-worker` 的空队列自暂停逻辑，使定时 prompt 只负责核验模型、调用并等待共享入口、原样报告结构化终态。

Bug 修复是第一验收项，不得以 prompt 瘦身、飞书通知兜底或错误分类改写代替。自暂停是可选的运行成本策略，不属于项目 runtime、任务投影或恢复合同；删除后，`no_candidate` 只结束当前轮次，不再改变 automation 状态。

## 二、已确认事实与根因

### 2.1 QueueMaintenance 误报

- 现场 runId 为 `fa64f285-54ec-4291-bb58-8cc8b6e484d3`。
- 普通 QueueMaintenance 已形成并 fast-forward 到 `master` 的正式提交 `213f7d0b08e87431bcee3a667d38d6bd2501ebef`，提交主题为 `chore(QUEUE-MAINTENANCE): maintain task queue`。
- candidateResult 的 `category=completed`，不含 `decisionId`；这是普通维护完成结果的合法结构。
- schema 5 runtime 已经通过 `CompleteRun` 清空，实时 `Show` 为 `runs.codex=null`、`runs.deepseek=null`、`integrationLockStatus=none`。
- `tools/invoke-hourly-owner.ps1` 在构造成功终态时，于 `Set-StrictMode -Version Latest` 下直接读取不存在的 `$Run.candidateResult.decisionId`，抛出 `PropertyNotFoundException`。异常发生在正式提交已合入、后置条件通过且 `CompleteRun` 已清空 runtime 之后。catch 发现 `master` HEAD 已等于 `formalHead`，只尝试再次写入 `integrated` 而不调用 `Set-Attention`；由于 run 已被清空，该步骤不形成新的 runtime 所有权。实际误报的是返回终态 JSON 被构造成 `attention_required/hourly_formal_failed`，不是仍有 run 停在 `attention_required`。
- 飞书 `{"result":"INVALID_INPUT"}` 是误报后尝试以虚拟任务 ID `QUEUE-MAINTENANCE` 发送失败通知的次生现象，不是根因。

### 2.2 定时 prompt 过厚

- 实时 `codex-hourly-worker` prompt 为 147 行、8635 字符；版本化 prompt 为 140 行、8431 字符。
- 实时 prompt 已使用并验证 `exec_command/write_stdin` 链路；版本化 prompt 仍保留旧 `shell_command` 表述，两者已经漂移。
- 绝大部分额外代码用于读取 automation TOML、重建完整更新参数、调用 `automation_update`、计算 prompt 长度与 SHA-256、逐字段回读验证。
- 自暂停仅在真实空队列时减少后续空跑，不参与 QueueMaintenance 成功、失败、runtime、Git、通知或恢复。

## 三、目标与非目标

### 3.1 目标

1. 普通 QueueMaintenance 的 candidateResult 不含 `decisionId` 时，正式集成返回 `maintenance_completed`，不得进入 `attention_required`。
2. 维护型决策创建或回复完成且含 `decisionId` 时，继续返回既有 `decisionTaskId`、`decisionId` 和可选 `resolutionState`。
3. 删除所有空队列自暂停判断、配置读取、automation 自更新与回读逻辑。
4. 保留实时已经正常工作的 `exec_command/write_stdin` 单次前台调用与同一 session 等待链路，并同步到版本化 prompt。
5. 保持 `codex-hourly-worker` 为 `PAUSED` 完成版本化修改、实时 prompt 更新、聚焦验证和真实 Codex canary；验证通过后仍不自动恢复，由负责人另行决定。
6. 在独立手动 worktree 中形成唯一提交并通过项目集成锁 fast-forward，不覆盖主工作区用户改动。

### 3.2 非目标

- 不修改 schema 5、owner claim、任务选择、candidate、正式重放、集成锁、`CompleteRun`、清理或恢复机制。
- 不修改 QueueMaintenance 的决策创建、回复消费、TTL、任务投影或飞书协议。
- 不为飞书 `INVALID_INPUT` 新增兜底、重试、虚拟任务映射或兼容分支。
- 不新增自暂停辅助脚本、skill、第二 automation、第二 runtime、后台进程或监视器。
- 不修改 DeepSeek、日报或周报 automation。
- 不清理 `fa64f285-54ec-4291-bb58-8cc8b6e484d3` 等历史 worktree、branch 或 session；历史现场清理由独立恢复任务处理。
- 不暂存、提交或覆盖主工作区的 `.agents/summary_state.json`、`设计总结.txt` 与 `docs/superpowers/specs/2026-08-14-codex-cli-isolated-canary-repair-design.md`。

## 四、方案比较与决定

### 4.1 方案 A：删除自暂停并直接修复可选属性访问（采用）

普通空队列终态只报告本轮无候选。automation 的暂停与恢复由显式管理操作负责。成功终态在读取 `decisionId` 前先检查属性存在，再按现有非空规则决定是否附加决策字段。

该方案删除无必要的自管理职责，不新增组件；QueueMaintenance Bug 仍按真实根因单点修复并由独立回归测试覆盖。

### 4.2 方案 B：保留自暂停并抽取确定性辅助脚本（不采用）

可以缩短 prompt，但会新增一个只为可选成本策略服务的组件，并保留新任务加入后需人工恢复的行为。

### 4.3 方案 C：只压缩 prompt 文字，保留内嵌自暂停代码（不采用）

表面行数可能减少，但 TOML 解析、自更新、回读和工具接口耦合仍由模型执行，不能解决职责过厚与版本漂移。

## 五、详细设计

### 5.1 QueueMaintenance 成功终态

`tools/invoke-hourly-owner.ps1` 保持现有成功顺序：正式验证、runtime 更新、fast-forward、后置条件、`CompleteRun`、构造终态。只修改决策字段附加条件：

1. 先判断 `candidateResult.PSObject.Properties.Name` 是否包含 `decisionId`。
2. 只有属性存在且值非空时，才读取并附加 `decisionTaskId` 与 `decisionId`。
3. `resolutionState` 继续使用现有属性存在检查。
4. 普通 QueueMaintenance 的 `category=completed` 不含 `decisionId` 时，条件短路，原始 `maintenance_completed` 结果直接返回。

不移动 catch、不改变 `hourly_formal_failed` 的真实失败语义，也不在通知层识别或修补该异常。

### 5.2 回归测试

新增 `tools/test-queue-maintenance-completion.ps1`，使用临时 Git 仓库和专用私有 state root 运行完整 `RunOnce` 夹具：

- 队列初始没有可领取任务，使 Codex 进入 QueueMaintenance。
- candidate 形成合法维护提交，candidateResult 为 `category=completed`、合法 `expectedTransition`，且明确不含 `decisionId`。
- 共享入口必须返回单个结构化终态，`status=maintenance_completed`、`category=success`，并含正式提交与成功清理证据。
- 终态不得为 `attention_required`，不得含 `decisionId` 或 `decisionTaskId`。
- 测试结束时 schema 5 两个 owner run 均为空，临时 worktree、branch、仓库和专用 state root 按精确路径安全清理。

现有 `tools/test-queue-maintenance-waiting-run.ps1` 与 `tools/test-queue-maintenance-decision-owner.ps1` 继续覆盖含 `decisionId` 的创建、等待与决策路径，防止属性保护误删合法决策字段。

### 5.3 薄触发 prompt

`开发管理/自动工作流控制器提示词.txt` 只保留：

1. 按 `AGENTS.md` 从正式 Node REPL request metadata 读取并核验实际 `gpt-...` model。
2. 通过一个长时间 `functions.exec` 调用一次 `tools/invoke-hourly-owner.ps1 -Owner codex -Action RunOnce -Model <实际 model> -OutputJson`。
3. 使用实时已验证的 `tools.exec_command` 首次启动；若返回 session id，则只用 `tools.write_stdin` 轮询同一 session，每次等待不超过 60 秒。
4. 如果外层 `functions.exec` 自身返回 `Script running with cell ID ...`，只对同一 cell 调用 `wait`，每次不超过 60 秒；不得重启共享入口或创建第二个 owner 调用。该外层 cell 等待与 cell 内 `write_stdin` 对 PowerShell session 的轮询是两个不同层级，均按实时 prompt 的现有链路保留。
5. 进程非零退出、缺少 session id 或终态不是单个合法 JSON 时，使用现有稳定错误停止。
6. 原样输出脚本结构化终态，再附加恰好一个简短 `::inbox-item`；memory 仍只记录执行时间与终态摘要。

删除以下全部内容：

- `shouldSelfPause` 及五字段自暂停判断；
- automation TOML 读取和解析；
- `codex_app__automation_update` 自管理调用；
- prompt 长度、SHA-256 与逐字段回读验证；
- “真实空队列已令 Codex 暂停”的输出分支。

`no_candidate`、`decision_requested`、`waiting_decision`、`maintenance_completed`、`attention_required` 等合法 JSON 均由触发层原样透传；触发层不设置业务 status 白名单。

### 5.4 规则、checker 与归档

- `开发管理/自动工作流规则.txt` 删除全部空队列自暂停合同，明确 `no_candidate` 只结束当前轮次；automation 状态只通过显式管理操作改变。删除范围同时包括实时入口段原第 8 行允许终态返回后执行短命配置 cell 的授权，以及通知与 QueueMaintenance 段原第 62–63 行的五字段自暂停、配置快照、幂等更新、回读校验和 `shouldSelfPause=false` 说明，不能只删除主自暂停段而保留前置授权。
- `tools/check-automation-workflow.ps1` 的规则侧断言同步调整：`Assert-Contains $rules` 移除仅由旧自暂停规则提供的 `taskId=QUEUE-MAINTENANCE` 与 `cleanup=cleaned` 两个锚点；其他 QueueMaintenance、决策和清理合同锚点保持不变。
- 同一 checker 的 prompt 侧 `Assert-Contains $codexPrompt` 改为完整的新链路清单，必须保留：`tools.mcp__node_repl__js`、`nodeRepl.requestMeta`、`codex_model_metadata_invalid`、`modelTexts.length !== 1`、`invoke-hourly-owner.ps1`、`-Owner codex`、`tools.exec_command`、`tools.write_stdin`、60 秒 session 轮询、外层 `Script running with cell ID` 同一 cell `wait`，以及四个软合同表述 `不读取队列或任务卡`、`Desktop automation memory`、`恰好一个简短 \`::inbox-item\``、`memory 不得改变固定命令`。
- prompt 侧旧必含清单必须移除：`timeout_ms: 3060000`、`shouldSelfPause`、五个 `terminal.status/owner/taskId/detailCode/cleanup === ...` 自暂停比较、`tools.codex_app__automation_update` 和 `status: 'PAUSED'`。这些 token 同时加入 `Assert-DoesNotContain $codexPrompt`；`shell_command` 与 `automation.toml` 也必须显式拒绝，防止版本化 prompt 回退到旧调用链或残留自管理配置读取。
- `tools/test-check-automation-workflow.ps1` 要求 Codex prompt 恰有一个主 JavaScript 代码块，保留上述模型核验、`exec_command/write_stdin`、外层同一 cell `wait` 和四个软合同表述，并逐项拒绝上述旧 `shell_command`、超时、自暂停五字段、automation 更新和 TOML token。
- `docs/superpowers/specs/2026-08-14-codex-hourly-self-pause-decoupling-design.md` 的状态改为“已退役”，补记 2026-08-15 的移除原因和本设计路径。旧 prompt 不另复制，精确实现由 Git 历史保存。
- `docs/superpowers/specs/2026-08-14-queue-maintenance-decision-lifecycle-design.md` 只对第 8 节“选择、自暂停与并发边界”增加局部退役说明：其中自暂停五字段、空队列自动暂停和对姊妹自暂停设计第 4.1 节的交叉引用自 2026-08-15 起不再是活动合同；决策创建、等待、TTL、回复消费、并发与 `decision_requested/waiting_decision/maintenance_completed` 终态合同仍有效。不得把整份维护决策设计标记为退役。

### 5.5 实时 automation 配置

版本化提交集成后，使用 Codex automation 管理能力更新现有 `codex-hourly-worker` 的完整配置：

- prompt 替换为已通过聚焦测试的版本化文本；
- `status` 保持 `PAUSED`；
- `id`、`kind`、名称、schedule、model、reasoning effort、notification policy、execution environment、project、cwd 全部保持不变；
- 不直接编辑 `automation.toml`，不更新其他 automation。

更新后重新只读配置，核对版本化与实时 prompt 规范化文本、长度和 SHA-256 一致，并再次确认状态仍为 `PAUSED`。

## 六、隔离、提交与集成

- 设计与后续实施使用 `.worktrees/manual/codex-hourly-thin-trigger-bugfix`，分支为 `codex/codex-hourly-thin-trigger-bugfix`。
- 预期版本化路径仅为：
  - `tools/invoke-hourly-owner.ps1`
  - `tools/test-queue-maintenance-completion.ps1`
  - `开发管理/自动工作流控制器提示词.txt`
  - `开发管理/自动工作流规则.txt`
  - `tools/check-automation-workflow.ps1`
  - `tools/test-check-automation-workflow.ps1`
  - `docs/superpowers/specs/2026-08-14-codex-hourly-self-pause-decoupling-design.md`
  - `docs/superpowers/specs/2026-08-14-queue-maintenance-decision-lifecycle-design.md`
  - `docs/superpowers/specs/2026-08-15-codex-hourly-thin-trigger-and-queue-maintenance-result-fix-design.md`
- 暂存前对上述实际变化路径运行 `tools/check-pending-whitespace.ps1`，暂存后运行 `git diff --cached --check`，只创建一个路径限定提交。
- 合并前从主工作区重新调用 schema 5 `Show`，确认两个 owner run 均空、集成锁空闲；重新检查主工作区 staged、unstaged、untracked 路径与待合并路径不重叠。
- 只通过 `tools/invoke-project-integration.ps1` 取得同一进程持有型集成锁并 fast-forward 到 `master`。任何事实变化、路径冲突或非 fast-forward 均停止，不 stash、不覆盖用户改动。

## 七、验证矩阵

### 7.1 Bug 与 QueueMaintenance

1. `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-queue-maintenance-completion.ps1`
2. `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-queue-maintenance-waiting-run.ps1`
3. `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-queue-maintenance-decision-owner.ps1`

第一项必须直接证明无 `decisionId` 的普通完成结果为 `maintenance_completed` 且非 `attention_required`；后两项证明合法决策路径未回归。

### 7.2 提交前 Prompt、规则与仓库检查

1. `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-check-automation-workflow.ps1`
2. `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-review-text.ps1 -Paths AGENTS.md,CLAUDE.md,开发管理`
3. `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-data-chain.ps1`
4. `tools/check-pending-whitespace.ps1` 与 `git diff --check`

此阶段不直接对尚未更新的生产 automation root 运行 `tools/check-automation-workflow.ps1`。`test-check-automation-workflow.ps1` 使用临时 automation fixtures 验证新版本化 prompt 与 checker 合同；若此时用生产配置做精确一致性检查，旧实时 prompt 必然与新版本化 prompt 不同，不能把预期部署前差异误判为代码失败。

聚焦测试还必须证明：规则文本不再授权短命自暂停配置 cell；checker 的规则锚点不再依赖 `taskId=QUEUE-MAINTENANCE` 或 `cleanup=cleaned`；prompt checker 保留新 `exec_command/write_stdin`、外层同一 cell `wait` 和四个软合同表述，同时移除并拒绝全部旧 `shell_command`、`timeout_ms`、自暂停五字段、automation 更新和 TOML token；两份 2026-08-14 spec 对自暂停合同的退役范围清楚且不误退役维护决策主体。

### 7.3 集成与实时配置更新后检查

1. 通过 automation 管理能力把已集成版本化 prompt 部署到现有 `codex-hourly-worker`，保持 `PAUSED` 和其余字段不变。
2. 在最新 `master` 运行 `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-automation-workflow.ps1`，使用默认生产 automation root，要求版本化与实时 prompt 精确一致。
3. 独立核对实时 prompt 长度与 SHA-256、automation 状态和所有保留字段。

不重复与实际输入无关的飞书桥完整测试、BattleSim 或 Unity 验证。

### 7.4 集成后真实 canary

1. 从本轮正式 request metadata 读取实际 Codex model，不使用系统提示或 `unknown`。
2. 在最新 `master` 上运行 `tools/invoke-hourly-owner.ps1 -Owner codex -Action Canary -Model <实际 model>`；canary 使用专用私有 runtime 和 canary worktree。
3. 要求真实模型核验、结构化终态、主工作区 HEAD/用户改动隔离、两个 owner run 为空、集成锁空闲与 canary worktree 清理全部通过。
4. 再运行实时配置一致性检查，确认 prompt 已瘦身且 automation 仍为 `PAUSED`。

canary 通过不自动恢复小时任务；最终报告证据并请负责人决定是否把 `codex-hourly-worker` 恢复为 `ACTIVE`。

## 八、停止条件

出现以下任一情况立即停止，不叠加补丁：

- 普通 QueueMaintenance 回归测试仍进入 `attention_required`，或修复需要通知层兜底；
- 属性存在检查影响含 `decisionId` 的维护决策创建、等待或回复路径；
- prompt 瘦身需要回退实时已验证的 `exec_command/write_stdin` 链路；
- 版本化与实时 prompt 无法精确一致，或更新需要直接编辑 automation TOML；
- 任一 owner run 非空、集成锁被持有、主工作区 HEAD 变化或待合并路径与用户改动冲突；
- canary 模型不符、终态无效、隔离或清理证据不完整；
- 修复开始需要新增重试、第二 runtime、后台监视器、通知兼容分支或超过本设计预期路径。

## 九、完成条件

1. 普通 QueueMaintenance 无 `decisionId` 的真实回归夹具返回 `maintenance_completed`，不产生 `attention_required`。
2. 含 `decisionId` 的维护型决策路径测试继续通过。
3. 版本化与实时 Codex prompt 使用同一 `exec_command/write_stdin` 薄入口，不含任何自暂停代码。
4. 旧自暂停解耦设计已标记退役，维护决策生命周期设计的自暂停章节已标记局部退役，Git 历史保留精确旧实现；维护决策主体合同继续有效。
5. 唯一版本化提交通过集成锁 fast-forward 到 `master`，用户三项改动保持原样。
6. 真实 Codex canary 与实时配置核验通过，schema 5 两个 owner run 和集成锁为空，automation 最终仍为 `PAUSED`。
7. 是否恢复 `ACTIVE` 留给负责人在验收报告后明确决定。
