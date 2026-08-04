# 小时自动化生产路径修复设计

> 状态：负责人已选择方案 B。本文只锁定 Codex candidate 元数据、DeepSeek CLI 权限、真实 canary 与现有失败现场的最小修复；不重构 schema 5、owner adapter、任务选择、通知或共享集成模型。

## 一、已确认根因

1. Codex run `5d659489-9b25-4be1-b9f5-851705ddf53a` 已形成候选提交 `9bc371991032f58426ef299acc5954e0b90e5f8c`，但提交只有 subject。`invoke-codex-candidate.ps1` 的 completed 验证要求完整 Automation 元数据，而 candidate prompt 没有提供唯一提交命令和元数据同源要求，因此 run 停在 `attention_required/codex_candidate_metadata_format_invalid`。
2. DeepSeek run `96501cdd-d5bb-44a4-8181-39dd359335de` 在 `dontAsk + allowedTools` 下执行验证。模型使用 `cd ... && pwsh ...`、绝对脚本路径和最小 `pwsh -Command` 探针时均未命中精确白名单，故被 Claude Code 权限层拒绝并返回 `PW_EXEC_PERMISSION_DENIED`；01B1 随后被机械转换为 blocked。
3. 现有 Codex canary 只核验身份、模型和无写入 JSON；DeepSeek canary 只允许 `Read`。二者都没有覆盖真实 candidate 的提交元数据或 PowerShell 执行能力。

## 二、采用方案与不采用方案

采用方案 B：修复两个责任方的真实生产合同，增加隔离 canary 探针，并人工收敛现有失败现场。

- 不采用只改 prompt／权限参数的方案 A，因为它仍会让 canary 对生产路径失明。
- 不采用让共享 wrapper 接管所有 candidate 提交的方案 C，因为它会改变现有责任边界并扩大重构范围。

## 三、Codex candidate 合同

`invoke-codex-candidate.ps1` 在读取任务卡并得到本轮授权路径后，把唯一提交命令写入 candidate prompt：使用 `automation-finalize-commit.ps1`、当前 worktree、精确授权路径和 `-RequireAutomationMetadata`，固定 `AutomationTask=<TaskId>`、`AutomationState=completed`。

模型生成的 `AutomationResult`、`AutomationImpact`、`AutomationVerify`、`AutomationPlain` 必须与结构化终态的 `result`、`impact`、`verify`、`plain` 完全相同。wrapper 继续读取实际 commit message 并用现有 parser 独立校验；不得自动补写、猜测或放宽格式。

QueueMaintenance 使用现有允许的管理路径集合形成 candidate 提交，仍由路径检查器证明没有越界。普通 execution／review 只使用任务卡 `expectedPaths`。不增加兼容提交格式。

## 四、DeepSeek 权限边界

Claude Code candidate 与 canary 均使用 `--permission-mode bypassPermissions`，不再传入 `--allowedTools`，也不维护 Bash 命令字符串白名单。DeepSeek 因此可以直接运行项目要求的 PowerShell、Git、Unity 和 dotnet 命令。

权限放开不取消仓库级安全边界：

- 责任方仍只在项目拥有的 run worktree 和 candidate branch 工作；
- task card、队列、backlog、归档、runtime、主工作区与其他 worktree 的所有权规则保持；
- wrapper 仍核验 base、唯一父提交、clean 状态、精确 changed paths、任务后置状态和结构化证据；
- 正式结果仍在最新 master 上重放，并经共享排他集成锁 fast-forward；路径冲突或事实漂移立即停止。

## 五、真实 canary

Codex canary 在专用私有 canary worktree 内创建一个 canary-only 探针文件，通过与生产相同的 finalizer 和 Automation 元数据合同形成唯一提交。wrapper 验证提交格式、终态字段同源和 worktree clean；外层证明主工作区 HEAD／status 未变后删除 canary worktree 与 branch。

DeepSeek canary 在专用私有 canary worktree 内实际调用一条无副作用 PowerShell 探针，并把固定输出写入结构化终态。wrapper 核验身份、模型、探针结果和 worktree clean；外层继续证明主工作区隔离并精确清理。canary 不读取队列、不 claim 业务任务、不产生主工作区提交。

## 六、现有失败现场收敛

实施前同时暂停 `codex-hourly-worker` 与 `deepseek-hourly-trigger`。代码修复先在独立手动 worktree 验证并通过共享集成锁进入最新 master。

随后人工处理 Codex run `5d659489-...`：保留 `9bc3719...` 原候选证据，在最新 master 上无旧 session 地重放其五条授权路径；队列／backlog 的冲突只按当前事实机械解析——保留 01B1 的 blocked 投影，同时完成 01A、建立 `U-TZ-CHARTER-SAVE-01` 并按原固定顺序投影。重新运行 01A 要求的任务卡、文本、whitespace 与 staged diff 检查，形成带合法 Automation 元数据的正式单提交，经共享锁 fast-forward。证明 master 包含正式提交后才精确 `CompleteRun` 并清理原 run worktree／branch。

DeepSeek 权限探针通过后，将仅因 `PW_EXEC_PERMISSION_DENIED` 阻塞的 `U-BOUNTY-01B1` 人工恢复为原 `external_execute/deepseek/ready`，同步 backlog 与队列；不创建 automationReply，不复用已清理的 DeepSeek session 或 run。

## 七、验证与恢复启用

最小自动化测试范围：

- `tools/test-invoke-codex-candidate.ps1`
- `tools/test-invoke-deepseek-responsibility.ps1`
- `tools/test-hourly-owner-adapter.ps1`
- `tools/test-check-automation-workflow.ps1`
- 与实际变化对应的 task-card、review-text、pending-whitespace 和 `git diff --cached --check`

测试必须新增负例，证明缺失／不一致的 Codex commit 元数据仍被拒绝；新增 DeepSeek 参数证据，证明不再传 `allowedTools` 且启用 bypass；新增 canary 证据，证明 Codex 真实提交合同和 DeepSeek PowerShell 探针都执行过。`check-data-chain` 只在最终实际改动命中其数据链检查范围时运行，不因自动化工具热修复无理由扩大验证。

只有以下事实同时满足才恢复两个 automation：schema 5 两个 owner 均为空、显式仓库范围的集成锁为 none、master 包含修复和现场收敛提交、主工作区原有改动保持、双真实 canary 通过、没有本轮残留 worktree／branch。恢复时保留原 cron、prompt、model、reasoning effort、project 与通知配置。

## 八、停止条件

- 若 `9bc3719...` 的业务内容或任务后置状态无法独立验证，不集成、不清理原 run，先报告具体证据。
- 若放开 Claude Code 权限仍不能执行最小 PowerShell 探针，停止并保留 canary 现场，不把问题改写成业务 blocker。
- 若修复需要第二 runtime、自动恢复旧 session、自动冲突解决、重试层或取消仓库路径／集成门禁，停止并重新确认范围。
