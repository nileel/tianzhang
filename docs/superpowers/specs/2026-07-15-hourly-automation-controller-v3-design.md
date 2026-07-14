# 天章每小时自动工作流控制器 v3 设计

> 日期：2026-07-15
> 状态：用户已批准“薄提示词 + 厚控制器”方向
> 继承：`docs/superpowers/specs/2026-07-13-hourly-automation-controller-v2-design.md`、`docs/superpowers/specs/2026-07-14-hourly-automation-recovery-hardening-design.md`、`docs/superpowers/specs/2026-07-14-hourly-automation-preflight-failure-design.md`
> 范围：把确定性编排从自动化 prompt 下沉到固定入口；不处理任何业务任务，不改变主责、队列水位、审核、邮件私密配置或 Git 安全边界

## 1. 问题与设计结论

当前部署 prompt 有 7,844 个字符、45 行、25 个编号步骤、18 处“必须”和 41 处“不得/禁止”。它同时描述身份、租约、恢复、候选隔离、任务类型映射、DeepSeek 退避、待决策、验证、提交和失败关闭。2026-07-14 的真实故障证明，问题并非单纯篇幅过长，而是模型被要求把自然语言临场翻译为状态枚举、PowerShell 参数、checkpoint 顺序和恢复语义。

v3 采用“薄启动 prompt + 确定性编排门面 + 现有安全 helper”的结构。模型保留语义判断；状态迁移、命令参数、错误码和收尾顺序由一个固定入口执行。

## 2. 方案比较

### 2.1 继续缩写现有 prompt

只删重复文字，不改变职责归属。它仍要求模型拼接多段命令、记忆 checkpoint 和解释错误码，无法消除 `execution`/`execute`、多路径参数和空 `RUNNING` 这类故障，不采用。

### 2.2 用新单体脚本替换全部 helper

把状态机、workspace 快照、Git 指纹和提交逻辑全部重写进一个脚本，表面入口最少，但会重新实现已经过大量边界回归的安全内核，扩大共享控制面风险，不采用。

### 2.3 在现有 helper 外增加确定性门面

新增 `tools/automation-controller.ps1`，作为自动化 prompt 唯一允许调用的控制器入口。门面使用参数数组调用现有状态、workspace guard 和 finalizer，解析其退出码并输出统一 JSON。现有 helper 继续作为安全内核和独立测试对象。这是采用方案。

## 3. 职责边界

### 3.1 确定性门面负责

- 生成或校验 `runId`，执行 Acquire、Renew、Complete 和 Fail。
- 按 `runId` 在用户级状态目录确定 baseline、recovery evidence 和 session 路径。
- 固定调用 Snapshot、Check、Verify、CaptureRecoveryEvidence 和 CheckRecovery。
- 把外部工作类型 `execution`、`review`、`maintenance`、`recovery` 映射为状态工具的 `execute`、`review`、`maintenance`、`recovery`。
- 固定 `identity_checked → queues_loaded → task_selected → mutation_started → verification_completed → commit_completed` 的合法顺序。
- 从本机状态读取 `expectedPaths`；收尾时不接受模型重新提供一套路径。
- 调用 finalizer，并让 finalizer继续从允许上界推导实际 `changedPaths`，以 `git commit --only` 提交。
- 根据 helper 的稳定退出码返回 `candidate_conflict`、`baseline_changed`、`recovery_expected_changed`、`invalid_arguments` 等结构化结果。
- 自动执行写前失败清理；已登记任务或恢复证据时保留严格恢复指针。
- 包装 DeepSeek worker backoff 的记录、清除和到期判断。
- 校验待决策回复正文的严格格式、决策编号和选项键；发件人授权和邮件查询仍由私有连接器配置负责。
- 对每个动作返回下一步、所需事实源、失败策略和是否允许项目 mutation。

### 3.2 模型负责

- 通过 Node REPL 取得实际模型，并把结果交给入口；入口不猜测模型身份。
- 阅读任务卡和入口返回的 `requiredSources`。
- 计算候选优先级、依赖、冻结、主责和业务可执行性。
- 在修改前推导完整的允许路径，并逐个候选提交给入口检查。
- 实施任务、判断事实冲突、创建需要负责人决定的问题。
- 选择并执行任务直接相关的最小充分验证。
- 通过已连接的任务标题或 Gmail 工具完成需要外部语义和授权上下文的操作。

模型不得直接调用状态、guard 或 finalizer helper，也不得临时构造 Git/PowerShell 收尾命令。

## 4. 入口动作与协议

入口采用单脚本多动作协议。所有成功和预期失败都向 stdout 输出单个 JSON 对象；非预期脚本错误仍输出 JSON 后以非零码退出。公共字段为：

- `protocolVersion`：固定为 `1`。
- `ok`：动作是否完成。
- `action`：模型下一步应执行的动作。
- `runId`：当前租约标识；未取得租约时为 `null`。
- `branchKind`：`selection`、`execution`、`review`、`maintenance`、`recovery`、`pending_decision` 或 `none`。
- `taskId`、`executor`、`expectedPaths`：当前工作单元字段；没有工作单元时为空。
- `requiredSources`：当前分支必须读取的项目相对路径，去重并按稳定顺序返回。
- `requiredChecks`：由模型完成的语义/领域检查；Git 和控制面固定检查不放入此数组。
- `nextCommand`：下一次只允许调用的入口动作名称，不返回需要模型拼接的 shell 命令。
- `failurePolicy`：`skip_candidate`、`close_empty_run`、`preserve_recovery`、`auto_blocked` 或 `stop_read_only`。
- `errorCode`、`message`：稳定机器码和不含私密配置的简短说明。

### 4.1 `Start`

输入实际模型和可选 `runId`。入口验证模型非 `unknown`，Acquire 后创建 Snapshot，并读取本机状态：

- fresh `IDLE` 返回 `action=select_candidate`、`branchKind=selection`。
- 过期 `RUNNING` 且有完整任务与恢复字段时，调用 CheckRecovery；成功返回 `action=resume_task`、`branchKind=recovery`。
- 有 pending decision 时返回 `action=inspect_pending_decision`，但不替模型访问邮箱。
- 有活动租约、AUTO-BLOCKED、损坏状态、baseline 或恢复证据异常时返回对应失败策略，不产生项目 mutation。

`Start` 把本轮 baseline/session 写入用户级状态目录，不写项目文件。

### 4.2 `RegisterCandidate`

输入 `runId`、外部 `WorkType`、`taskId`、`executor` 和完整 `expectedPaths`。入口先规范化和检查路径，再调用普通 Check。candidate conflict 返回 `skip_candidate`，且不写 `task_selected`；baseline changed 返回 `stop_read_only`。通过时内部完成 TaskKind 映射和 `task_selected` checkpoint，并根据实际分支返回按需事实源：

- execution：当前队列、任务卡事实源和 AI 协作规则。
- review：审核入口及入口路由出的事实源。
- maintenance：状态与建议维护规则、当前队列及相关 backlog。
- DeepSeek executor：在 execution 基础上追加 DeepSeek 工作提示词和 AI 协作规则。

恢复工作不重新注册候选。

### 4.3 `BeginMutation`、`Renew` 与分支辅助动作

`BeginMutation` 只在 `task_selected` 后写入 `mutation_started`。`Renew` 只续租，不改变业务字段。`RecordQueueState`、`RecordWorkerFailure`、`ClearWorkerFailure`、`CreateDecision` 和 `ValidateDecisionReply` 通过同一入口封装已有状态动作与输入校验；它们不允许绕过当前租约或 expectedPaths。

### 4.4 `Finish`

模型完成并报告领域验证后，向入口提供提交信息，不再提供 expectedPaths。入口固定执行：

1. 从状态读取原始 baseline 和完整允许路径。
2. guard Verify。
3. CaptureRecoveryEvidence。
4. 原子写入 `verification_completed` 及证据引用。
5. 调用固定 finalizer；单路径和多路径都由参数数组传递。
6. 写入 `commit_completed`。
7. 再次 guard Verify。
8. Complete 回到 `IDLE`。

任一步失败都由入口调用 Fail。发生在 mutation 后或证据存在时采用 `preserve_recovery`；第二次恢复失败沿用 AUTO-BLOCKED。入口不自动推送远端。

### 4.5 `CompleteNoChange` 与 `Fail`

没有候选、全部候选冲突、未变化 backlog 或只读退出时使用 `CompleteNoChange`，它要求当前尚未 mutation 且工作单元没有项目变化。模型发现语义失败时调用 `Fail`；入口从 session 判断是否为恢复轮，不要求模型决定 `WasRecovery`。如果已经进入 `mutation_started` 且尚无恢复证据，入口先用原始 baseline 和状态中的 expectedPaths 调用 Verify 与 CaptureRecoveryEvidence；只有路径外 baseline 未变化时才把证据引用写回 checkpoint，然后调用状态 Fail。这样 mutation 后的精确控制器残留可以跨轮恢复，而人工或路径外变化仍失败关闭。证据捕获失败时保留诊断和现有状态，不凭路径名认领修改。

## 5. 会话状态与安全不变量

门面 session 保存于 `%USERPROFILE%/.codex/automation-state/tzg-hourly-controller-runs/`，只包含 runId、baseline/evidence 路径、是否恢复和当前协议阶段。项目状态仍由现有 schema v4 工具管理；v3 不迁移其 schema。Complete 成功后删除本轮 session 和已消费的临时证据；Fail、AUTO-BLOCKED 或无法证明安全的基线变化保留它们供下一轮恢复或人工审计。

以下不变量保持不变：

- 只有 `tzg-hourly-controller` 一个活动写入型自动化；WF1/WF3/WF4 保持暂停。
- 不使用 stash、reset、checkout、clean，不覆盖人工差异。
- expectedPaths 是允许上界，实际 changedPaths 由 finalizer 推导。
- 路径外 baseline、预先 staged/unstaged/untracked/rename/delete 指纹在提交前后必须一致。
- HEAD 在未完成恢复期间变化继续失败关闭，只有人工审计后才能重建基线。
- DeepSeek 子进程不得 stage、commit 或并行派发；控制器是唯一 finalizer。
- 不自动推送，不把邮件地址、白名单、搜索条件或令牌写入项目、memory 或回复。

## 6. 薄 prompt

部署 prompt 收敛为不超过 10 个步骤、目标约 2,000 至 3,000 字符：

1. 读取 `AGENTS.md` 与自动工作流规则。
2. 用 Node REPL 取得实际模型和当前任务 ID。
3. 只调用 `automation-controller.ps1 Start`。
4. 读取返回的 `requiredSources` 并执行 `action` 指示的语义工作。
5. 候选通过 `RegisterCandidate` 登记；修改前调用 `BeginMutation`。
6. 执行任务相关最小充分验证。
7. 成功调用 `Finish`，无变化调用 `CompleteNoChange`，失败调用 `Fail`。
8. 输出简短结构化结果。

prompt 不再复制状态枚举、guard 调用序列、Git 命令、恢复计数、邮件回复正则或 DeepSeek backoff 算法。

## 7. 测试设计

新增 `tools/test-automation-controller.ps1`，在临时 Git 仓库和临时状态目录中覆盖：

1. fresh run：Start 取得租约、Snapshot，并返回 selection。
2. task_selected 前失败：非法候选参数自动关闭空运行并回到 IDLE。
3. mutation 后恢复：失败保存指针，下一 run 使用原 baseline/evidence 恢复。
4. baseline changed：路径外或 HEAD 变化返回稳定错误并拒绝继续。
5. candidate conflict：冲突候选被跳过，租约仍可登记下一候选。
6. 映射：execution/review/maintenance/recovery 唯一映射到四个内部 TaskKind。
7. Finish：单路径和多路径只提交实际变化子集，路径外状态不变。
8. pending decision：创建、严格回复格式校验、无效编号/选项拒绝。
9. DeepSeek backoff：失败记录、退避中分支提示、清除后恢复。

现有状态、workspace guard 和 finalizer 测试继续作为安全内核回归。`check-automation-workflow.ps1` 增加部署契约：主 prompt 必须调用唯一入口、不得直接调用三个 helper、不得包含内部 TaskKind/guard/finalizer 状态机，并验证 prompt 指标和入口文件存在。

## 8. 实施、部署与回滚

1. 控制器保持 PAUSED，活动写入者为 0。
2. 先提交本规格与实施计划。
3. 按 TDD 先新增入口测试并确认 RED，再实现最小动作切片。
4. 修改入口或底层 helper 后只运行直接相关测试；全部切片完成后合并运行一次四项控制面回归。
5. 通过 `codex_app__automation_update` 部署薄 prompt，保留调度、模型、项目绑定和其余字段；不得直接编辑 TOML。
6. 部署前检查保持 PAUSED；通过后只激活 `tzg-hourly-controller`。
7. 运行不修改业务文件的真实金丝雀：Start → RegisterCandidate（测试用候选只走写前协议）→ CompleteNoChange，或 Start → CompleteNoChange。金丝雀不得领取 TQ-057 等业务任务。
8. 最终确认本机状态 IDLE、工作区干净、活动写入者恰好为 1。

若新入口测试、部署检查或金丝雀失败，立即保持或恢复控制器 PAUSED；不回退已验证的状态、guard 和 finalizer helper。回滚只把自动化 prompt 恢复为本轮部署前内容，并保留失败证据供人工审计。

## 9. 完成标准

- 主 prompt 不超过 10 个步骤，约 2,000 至 3,000 字符，不再实现自然语言状态机。
- prompt 只调用一个控制器入口，不直接调用状态、guard、finalizer 或 Git 提交命令。
- 新入口自动测试覆盖第 7 节九类场景；现有三项安全内核测试继续通过。
- workflow checker 能拒绝旧式厚 prompt、无入口配置、直接 helper 调用和非法内部 TaskKind。
- 部署前 PAUSED，部署后只有一个活动写入者。
- 无业务修改金丝雀通过，最终状态 IDLE、工作区干净。
- 设计、计划、实现和测试形成独立本地提交，不推送远端。
