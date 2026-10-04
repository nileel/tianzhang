# DeepSeek 候选临时文件阻塞恢复设计

## 目标

完成 `U-BOUNTY-01A` 的正式 DeepSeek 交付、Codex 独立复审、任务归档与后续任务解锁，同时修复本次 `deepseek_candidate_stray_temp_file` 的直接诱因。

本方案采用既有 schema 4 runtime、owner worktree、candidate、canonical、短时集成租约与通知流程，不增加恢复状态、候选收养接口、后台重试或第二套调度。

## 已确认事实

- 旧 run 为 `a39f07af-2890-4ca6-aef7-2ebd70d02044`，任务为 `U-BOUNTY-01A`，状态为 `attention_required`。
- DeepSeek 已创建候选提交 `8a208cc104c134ac962d23785cdb095cd9af3f4a`，它是 base `7ba70aee5de14a4fdfabbfa142ae7bd6ea179762` 的唯一直接后继，12 个变更路径均为任务卡 `expectedPaths` 子集。
- worktree 唯一未提交内容是根目录临时诊断脚本 `tzg-extract-failures.tmp.ps1`。该脚本只读取 Unity XML 并输出失败测试，不属于业务实现或验证记录。
- DeepSeek 报告的验证为：`dotnet build` 0 警告 0 错误；Unity EditMode 300 项中 298 项通过，本卡新增测试全部通过，另外 2 项为既有基线失败；文本、空白和 Git 检查通过。
- runtime 没有记录 `candidateCommit` 或 `candidateResult`，因此既有状态机不能把该 run 从 `attention_required` 恢复为 `candidate_ready`。

## 根因

直接根因是候选责任方在 RepositoryRoot 内创建了任务范围外的临时诊断脚本。当前 `dontAsk` 与允许工具集合允许通过 `Write` 创建该文件，却不允许责任方随后执行删除；项目规则又明确禁止用 `git clean` 等泛化清理掩盖现场。候选提交完成后，worktree 干净性前置条件失败，DeepSeek 只能返回 `blocked/deepseek_candidate_stray_temp_file`。

次生问题是 wrapper 对 `blocked` 终态只向固定入口返回 `detailCode`，没有把终态中已经存在的候选 SHA 和候选元数据登记到 runtime。由于 `attention_required` 是终态恢复事实，自动入口之后只能报告现场，不能继续 canonical。

本轮只修复直接诱因，不扩展 runtime 状态机。候选收养属于新的恢复协议，需要额外证据合同与状态迁移，不是完成当前任务所需的最小改动。

## 选定方案

### 1. 最小预防修复

在 `tools/invoke-deepseek-responsibility.ps1::New-CandidatePrompt` 增加明确约束：

- 不得在 RepositoryRoot 内创建临时、诊断、转换或辅助脚本／文件；
- 测试结果应直接读取既有输出，不能完成诊断时返回稳定 blocker；
- 提交后不得留下任务 `expectedPaths` 之外的文件。

为 `tools/test-invoke-deepseek-responsibility.ps1` 增加提示词合同断言，证明上述约束存在。除此之外不修改 DeepSeek 工具权限、runtime schema、状态迁移、固定入口或清理逻辑。

### 2. 关闭旧 run

普通管理上下文先再次核验旧 runId、taskId、recoveryReason、无活动进程、无集成租约、候选 branch／SHA 和唯一临时文件。

确认后精确删除 `tzg-extract-failures.tmp.ps1`。该文件内容已经核验，不属于候选提交，也不删除 worktree、branch、提交或其他未跟踪文件。

随后通过现有 `hourly-automation-lease.ps1 -Action CompleteRun`，以 `CompletionCategory=failed`、旧 runId 和完全匹配的 `ExpectedRecoveryReason` 关闭旧 attention run。旧候选 branch 与提交继续保留为审计证据，不尝试导入新 run。

### 3. 重新执行正式 DeepSeek 流程

在预防修复已经合入 `master`、旧 run 已关闭且 `integrationLease=null` 后，前台调用一次既有 `tools/invoke-deepseek-hourly.ps1 -Action RunOnce`。

固定入口重新选择仍为 `external_execute/deepseek/ready` 的 `U-BOUNTY-01A`，创建新的 run 与 owner worktree，由 DeepSeek 重新实施和验证。不得复用旧 Desktop/Cowork 会话，不向新责任方注入旧候选，也不在一次入口内增加重试。

成功路径仍由固定入口完成 candidate 核验、canonical 构建、`pending_review` 投影、business/handoff 提交、短时 fast-forward、`CompleteRun` 与飞书通知。

### 4. Codex 独立复审与关闭

DeepSeek 正式交付进入 `codex_review/codex/ready` 后，前台调用一次既有 `tools/invoke-codex-hourly.ps1`。Codex 按审核入口复审已经集成到 `master` 的实际组合，不使用旧候选代替正式提交。

复审通过时，由既有任务生命周期归档 `U-BOUNTY-01A`、移除待复审事实、更新队列／backlog 并按依赖事实解锁后续任务。复审不通过时保留原任务并按审核规则登记返工，不伪造完成。

## 验证

预防修复只运行与改动相称的检查：

- `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-invoke-deepseek-responsibility.ps1`
- `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-pwsh-runtime.ps1`
- 提交前对实际修改路径运行 `tools/check-pending-whitespace.ps1` 与 `git diff --cached --check`

重新执行的业务验证以 `U-BOUNTY-01A` 任务卡为准；不以旧候选报告替代新 run 的验证。Codex 复审按审核入口读取正式提交和本轮验证证据。

## 成功条件

- `U-BOUNTY-01A` 已归档，活动任务卡不再存在，队列和 source backlog 投影一致；
- DeepSeek 与 Codex runtime 均为空，`integrationLease=null`；
- `master` 包含正式 DeepSeek business/handoff 提交和 Codex 复审关闭提交；
- DeepSeek 正式交付已产生飞书任务结果通知；
- 后续任务只按真实依赖投影解锁；
- 预防修复没有新增调度、恢复状态、自动清理、兼容分支或重试层。

## 停止条件

- 删除前发现临时文件内容、路径或数量与已核验证据不一致；
- 旧 run、候选 branch、候选 SHA、task digest、主分支或租约发生变化；
- 预防修复需要扩大到工具权限、runtime schema 或状态迁移；
- 新 DeepSeek run 再次进入 `attention_required`、需要用户决定或产生路径越界；
- canonical／集成前置条件不满足，或主工作区在正式变更路径上存在冲突；
- Codex 复审未通过。

命中任一停止条件即保留现场并报告，不继续叠加补丁或自动重试。
