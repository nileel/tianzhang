# 历史过程文档归档（2026-10-04）

本目录保存 2026 年 7—8 月的工作流设计、一次性修复方案与实施计划，供追溯使用。归档不表示其中所有步骤已完成，也不重新确认旧文档中的授权、状态或结论。当前执行以项目规则、活跃任务卡和实际实现为准。

## 本次整理

- 从 `docs/superpowers/specs/` 迁入 58 份，从 `docs/superpowers/plans/` 迁入 23 份，共 81 份；未删除文档。
- 检查了 Git 跟踪文本及本地 `.superpowers`、`.agents`、`.claude`、`.codex`、`.serena` 中的文本笔记，按文件名（含无扩展名形式）检查引用并沿引用关系保留依赖。迁入集合没有来自集合外的已检出引用。
- 保留现有历史任务归档和审核记录，不修改当前任务卡、脚本、游戏设计或资源。
- 迁入正文只替换本批归档文件的完整路径；保留原有状态、未勾选步骤和历史命令。这些命令是历史材料，不应直接用于当前工作区。
- 归档前 Git 基线：`b19973ff3b7c63bcb154e56612484056e7f08e61`。原文可通过 `git show <基线>:<原路径>` 查阅。

## 查找与路径映射

普通 `rg` 搜索通过仓库根 `.rgignore` 跳过本归档区。追溯时使用 `rg --no-ignore "关键词" docs/superpowers/archive`。原路径均以 `docs/superpowers/` 开头；下表链接指向迁移后文件。

| 原相对路径 | 归档文件 |
| --- | --- |
| `plans/2026-07-11-hourly-automation-controller-implementation.md` | [Hourly Automation Controller Implementation Plan](plans/2026-07-11-hourly-automation-controller-implementation.md) |
| `plans/2026-07-12-automation-conversation-titles-implementation.md` | [自动化对话命名 Implementation Plan](plans/2026-07-12-automation-conversation-titles-implementation.md) |
| `plans/2026-07-12-automation-decision-requests-implementation.md` | [Automation Decision Requests Implementation Plan](plans/2026-07-12-automation-decision-requests-implementation.md) |
| `plans/2026-07-12-automation-title-repair-implementation.md` | [自动化对话命名修复 Implementation Plan](plans/2026-07-12-automation-title-repair-implementation.md) |
| `plans/2026-07-12-management-document-slimming-implementation.md` | [开发管理文档瘦身 Implementation Plan](plans/2026-07-12-management-document-slimming-implementation.md) |
| `plans/2026-07-13-hourly-automation-controller-v2-implementation.md` | [Hourly Automation Controller v2 Implementation Plan](plans/2026-07-13-hourly-automation-controller-v2-implementation.md) |
| `plans/2026-07-14-daily-automation-briefing-content-plan.md` | [Daily Automation Briefing Content Implementation Plan](plans/2026-07-14-daily-automation-briefing-content-plan.md) |
| `plans/2026-07-14-hourly-automation-preflight-failure-implementation.md` | [小时自动化写前失败收尾 Implementation Plan](plans/2026-07-14-hourly-automation-preflight-failure-implementation.md) |
| `plans/2026-07-14-hourly-automation-recovery-hardening.md` | [Hourly Automation Recovery Hardening Implementation Plan](plans/2026-07-14-hourly-automation-recovery-hardening.md) |
| `plans/2026-07-15-automation-decision-lifecycle-v6.md` | [Automation Decision Lifecycle v6 Implementation Plan](plans/2026-07-15-automation-decision-lifecycle-v6.md) |
| `plans/2026-07-15-hourly-automation-controller-v3-1-candidate-discovery-implementation.md` | [小时自动化控制器 v3.1 候选发现修复 Implementation Plan](plans/2026-07-15-hourly-automation-controller-v3-1-candidate-discovery-implementation.md) |
| `plans/2026-07-15-hourly-automation-controller-v3-implementation.md` | [Hourly Automation Controller v3 Implementation Plan](plans/2026-07-15-hourly-automation-controller-v3-implementation.md) |
| `plans/2026-07-17-hourly-controller-orchestration-rebuild-implementation.md` | [每小时自动工作流编排层重建 Implementation Plan](plans/2026-07-17-hourly-controller-orchestration-rebuild-implementation.md) |
| `plans/2026-07-18-hourly-automation-simplification-implementation.md` | [天章每小时自动工作流精简 Implementation Plan](plans/2026-07-18-hourly-automation-simplification-implementation.md) |
| `plans/2026-07-21-hourly-automation-pause-and-runner-reliability.md` | [Hourly Automation Pause and Runner Reliability Implementation Plan](plans/2026-07-21-hourly-automation-pause-and-runner-reliability.md) |
| `plans/2026-07-22-automation-commit-briefing-implementation.md` | [Automation Commit Briefing Implementation Plan](plans/2026-07-22-automation-commit-briefing-implementation.md) |
| `plans/2026-07-22-automation-complexity-reduction-and-recovery-repair.md` | [自动化复杂度收敛与恢复链修复实施计划](plans/2026-07-22-automation-complexity-reduction-and-recovery-repair.md) |
| `plans/2026-07-22-decision-resume-request-transition-implementation.md` | [Decision Resume Request Transition Implementation Plan](plans/2026-07-22-decision-resume-request-transition-implementation.md) |
| `plans/2026-07-23-automation-manual-worktree-closure.md` | [Automation and Manual Worktree Closure Implementation Plan](plans/2026-07-23-automation-manual-worktree-closure.md) |
| `plans/2026-07-23-automation-runnable-queue-depth.md` | [Automation Runnable Queue Depth Implementation Plan](plans/2026-07-23-automation-runnable-queue-depth.md) |
| `plans/2026-07-23-minimal-context-loading-implementation.md` | [Minimal Context Loading Implementation Plan](plans/2026-07-23-minimal-context-loading-implementation.md) |
| `plans/2026-07-25-automation-closeout-simplification.md` | [Automation Closeout Simplification Implementation Plan](plans/2026-07-25-automation-closeout-simplification.md) |
| `plans/2026-07-25-task-unblocking-decomposition.md` | [阻塞任务依赖拆分与自动化恢复实施计划](plans/2026-07-25-task-unblocking-decomposition.md) |
| `specs/2026-07-11-hourly-automation-controller-design.md` | [天章每小时自动工作流控制器设计](specs/2026-07-11-hourly-automation-controller-design.md) |
| `specs/2026-07-12-automation-conversation-titles-design.md` | [天章自动化对话命名设计](specs/2026-07-12-automation-conversation-titles-design.md) |
| `specs/2026-07-12-automation-decision-requests-design.md` | [自动工作流待决策与邮件回执设计](specs/2026-07-12-automation-decision-requests-design.md) |
| `specs/2026-07-12-management-document-slimming-design.md` | [开发管理文档瘦身设计（方案 2）](specs/2026-07-12-management-document-slimming-design.md) |
| `specs/2026-07-13-hourly-automation-controller-v2-design.md` | [天章每小时自动工作流控制器 v2 设计](specs/2026-07-13-hourly-automation-controller-v2-design.md) |
| `specs/2026-07-14-hourly-automation-preflight-failure-design.md` | [小时自动化写前失败收尾设计](specs/2026-07-14-hourly-automation-preflight-failure-design.md) |
| `specs/2026-07-14-hourly-automation-recovery-hardening-design.md` | [每小时自动化提交与恢复加固设计](specs/2026-07-14-hourly-automation-recovery-hardening-design.md) |
| `specs/2026-07-15-hourly-automation-controller-v3-1-candidate-discovery-design.md` | [小时自动化控制器 v3.1 候选发现修复设计](specs/2026-07-15-hourly-automation-controller-v3-1-candidate-discovery-design.md) |
| `specs/2026-07-15-hourly-automation-controller-v3-design.md` | [天章每小时自动工作流控制器 v3 设计](specs/2026-07-15-hourly-automation-controller-v3-design.md) |
| `specs/2026-07-17-hourly-controller-orchestration-rebuild-design.md` | [每小时自动工作流编排层重建设计](specs/2026-07-17-hourly-controller-orchestration-rebuild-design.md) |
| `specs/2026-07-18-hourly-automation-simplification-design.md` | [天章每小时自动工作流精简设计](specs/2026-07-18-hourly-automation-simplification-design.md) |
| `specs/2026-07-21-hourly-automation-pause-and-runner-reliability-design.md` | [每小时自动化暂停与 Runner 可靠性修复设计](specs/2026-07-21-hourly-automation-pause-and-runner-reliability-design.md) |
| `specs/2026-07-22-automation-commit-briefing-design.md` | [自动化提交摘要与日报设计](specs/2026-07-22-automation-commit-briefing-design.md) |
| `specs/2026-07-22-decision-resume-request-transition-design.md` | [飞书决策恢复请求单文件转换设计](specs/2026-07-22-decision-resume-request-transition-design.md) |
| `specs/2026-07-23-automation-manual-worktree-closure-design.md` | [自动化与手动开发并行隔离设计](specs/2026-07-23-automation-manual-worktree-closure-design.md) |
| `specs/2026-07-23-automation-runnable-queue-depth-design.md` | [自动工作流可执行队列深度设计](specs/2026-07-23-automation-runnable-queue-depth-design.md) |
| `specs/2026-07-25-automation-finalizer-closeout-fix-design.md` | [自动化归档提交与闭环判定修复设计](specs/2026-07-25-automation-finalizer-closeout-fix-design.md) |
| `specs/2026-07-25-task-unblocking-decomposition-design.md` | [三项阻塞任务完整依赖解锁与拆分设计](specs/2026-07-25-task-unblocking-decomposition-design.md) |
| `specs/2026-07-26-hourly-controller-timeout-recovery-design.md` | [每小时控制器超时恢复与 N-FPD-GROWTH-01 收尾设计](specs/2026-07-26-hourly-controller-timeout-recovery-design.md) |
| `specs/2026-07-27-automation-feishu-reporting-design.md` | [日报、周报与自动化任务飞书通知设计](specs/2026-07-27-automation-feishu-reporting-design.md) |
| `specs/2026-07-28-external-automation-permission-fix-design.md` | [外部自动化权限与基线隔离修复设计](specs/2026-07-28-external-automation-permission-fix-design.md) |
| `specs/2026-07-29-automation-metadata-contract-simplification-design.md` | [自动化提交元数据契约收敛设计](specs/2026-07-29-automation-metadata-contract-simplification-design.md) |
| `specs/2026-08-02-deepseek-hourly-minimal-correction-design.md` | [DeepSeek 小时入口最小纠偏设计](specs/2026-08-02-deepseek-hourly-minimal-correction-design.md) |
| `specs/2026-08-02-deepseek-stray-temp-recovery-design.md` | [DeepSeek 候选临时文件阻塞恢复设计](specs/2026-08-02-deepseek-stray-temp-recovery-design.md) |
| `specs/2026-08-02-hourly-review-repair-design.md` | [Codex / DeepSeek 小时自动化复审修复设计](specs/2026-08-02-hourly-review-repair-design.md) |
| `specs/2026-08-03-hourly-automation-recovery-and-contract-repair-design.md` | [天章小时自动化现场恢复与合同修复设计](specs/2026-08-03-hourly-automation-recovery-and-contract-repair-design.md) |
| `specs/2026-08-03-hourly-workflow-deleted-path-validation-fix-design.md` | [小时工作流删除路径验证修复设计](specs/2026-08-03-hourly-workflow-deleted-path-validation-fix-design.md) |
| `specs/2026-08-03-hourly-workflow-simplification-design.md` | [Codex / DeepSeek 小时工作流精简设计](specs/2026-08-03-hourly-workflow-simplification-design.md) |
| `specs/2026-08-03-review-rework-decision-design.md` | [Codex 复审不通过后的飞书返工决策设计](specs/2026-08-03-review-rework-decision-design.md) |
| `specs/2026-08-04-codex-model-metadata-channel-repair-design.md` | [Codex 小时自动化模型元数据通道修复设计](specs/2026-08-04-codex-model-metadata-channel-repair-design.md) |
| `specs/2026-08-04-hourly-production-path-repair-design.md` | [小时自动化生产路径修复设计](specs/2026-08-04-hourly-production-path-repair-design.md) |
| `specs/2026-08-05-queue-maintenance-blocker-refresh-design.md` | [空队列维护阻塞状态刷新设计](specs/2026-08-05-queue-maintenance-blocker-refresh-design.md) |
| `specs/2026-08-06-deepseek-decision-id-and-stuck-run-repair-design.md` | [DeepSeek 决策 ID 合同与异常 run 处置设计](specs/2026-08-06-deepseek-decision-id-and-stuck-run-repair-design.md) |
| `specs/2026-08-06-deepseek-default-src-authorization-design.md` | [DeepSeek 自动任务默认 src/ 授权设计](specs/2026-08-06-deepseek-default-src-authorization-design.md) |
| `specs/2026-08-09-codex-attention-feishu-recovery-design.md` | [Codex 异常通知与重复复审恢复设计](specs/2026-08-09-codex-attention-feishu-recovery-design.md) |
| `specs/2026-08-09-deepseek-terminal-schema-recovery-design.md` | [DeepSeek 结构化终态最小修复设计](specs/2026-08-09-deepseek-terminal-schema-recovery-design.md) |
| `specs/2026-08-09-feishu-decision-bridge-recovery-and-replay-design.md` | [飞书决策桥恢复与四张返工卡补发设计](specs/2026-08-09-feishu-decision-bridge-recovery-and-replay-design.md) |
| `specs/2026-08-10-codex-candidate-path-contract-and-01c-recovery-design.md` | [Codex 候选路径合同与 01C 失败现场修复设计](specs/2026-08-10-codex-candidate-path-contract-and-01c-recovery-design.md) |
| `specs/2026-08-10-hourly-automation-empty-run-correction-design.md` | [小时自动化空转纠正设计](specs/2026-08-10-hourly-automation-empty-run-correction-design.md) |
| `specs/2026-08-10-hourly-automation-liveness-and-formalization-repair-design.md` | [小时自动化空转、正式提交与展示合同修复设计](specs/2026-08-10-hourly-automation-liveness-and-formalization-repair-design.md) |
| `specs/2026-08-10-hourly-finalizer-in-process-invocation-repair-design.md` | [小时自动化 Finalizer 进程内调用修复设计](specs/2026-08-10-hourly-finalizer-in-process-invocation-repair-design.md) |
| `specs/2026-08-11-codex-hourly-stale-waiter-recovery-design.md` | [Codex 小时自动化僵死等待线程处置设计](specs/2026-08-11-codex-hourly-stale-waiter-recovery-design.md) |
| `specs/2026-08-13-atomic-task-decomposition-and-subagent-workflow-design.md` | [原子任务拆分与子智能体协作工作流设计](specs/2026-08-13-atomic-task-decomposition-and-subagent-workflow-design.md) |
| `specs/2026-08-14-codex-cli-isolated-canary-repair-design.md` | [Codex CLI 空终态隔离修复与维护决策放行设计](specs/2026-08-14-codex-cli-isolated-canary-repair-design.md) |
| `specs/2026-08-14-codex-hourly-self-pause-decoupling-design.md` | [Codex 小时自动化自暂停解耦设计](specs/2026-08-14-codex-hourly-self-pause-decoupling-design.md) |
| `specs/2026-08-14-cross-decision-reply-isolation-repair-design.md` | [跨决策回复隔离修复与 A 证据恢复设计](specs/2026-08-14-cross-decision-reply-isolation-repair-design.md) |
| `specs/2026-08-15-codex-hourly-thin-trigger-and-queue-maintenance-result-fix-design.md` | [Codex 小时薄触发器与 QueueMaintenance 成功终态修复设计](specs/2026-08-15-codex-hourly-thin-trigger-and-queue-maintenance-result-fix-design.md) |
| `specs/2026-08-15-deepseek-workflow-contract-refresh-design.md` | [DeepSeek 工作流合同收口设计](specs/2026-08-15-deepseek-workflow-contract-refresh-design.md) |
| `specs/2026-08-22-hourly-binary-whitespace-recovery-design.md` | [每小时自动化二进制空白误报修复与失败 run 恢复设计](specs/2026-08-22-hourly-binary-whitespace-recovery-design.md) |
| `specs/2026-08-23-manual-recovery-feishu-notification-design.md` | [人工恢复成功飞书通知补齐设计](specs/2026-08-23-manual-recovery-feishu-notification-design.md) |
| `specs/2026-08-24-codex-unity-eol-recovery-design.md` | [Codex Unity 换行假脏与候选恢复设计](specs/2026-08-24-codex-unity-eol-recovery-design.md) |
| `specs/2026-08-25-automation-checkpoint-resume-kind-contract-fix-design.md` | [自动化 checkpoint 恢复 kind 合同修复设计](specs/2026-08-25-automation-checkpoint-resume-kind-contract-fix-design.md) |
| `specs/2026-08-25-automation-finalizer-unity-meta-whitespace-design.md` | [自动提交器 Unity .meta 空值尾随空格修复设计](specs/2026-08-25-automation-finalizer-unity-meta-whitespace-design.md) |
| `specs/2026-08-27-hourly-block-cleanup-unity-projection-repair-design.md` | [小时自动化 blocked 清理与 Unity 生成投影修复设计](specs/2026-08-27-hourly-block-cleanup-unity-projection-repair-design.md) |
| `specs/2026-08-29-hourly-semantic-meta-diff-and-run-recovery-design.md` | [小时自动化 Unity .meta 差异校验与失败 run 完整收口设计](specs/2026-08-29-hourly-semantic-meta-diff-and-run-recovery-design.md) |
| `specs/2026-08-30-codex-candidate-review-and-dependency-guard-design.md` | [Codex 候选复审与依赖投影边界修复设计](specs/2026-08-30-codex-candidate-review-and-dependency-guard-design.md) |
| `specs/2026-08-30-codex-decision-contract-recovery-design.md` | [Codex 决策终态合同修复与现有 checkpoint 恢复设计](specs/2026-08-30-codex-decision-contract-recovery-design.md) |
| `specs/2026-08-31-hourly-main-path-conflict-and-trigger-memory-recovery-design.md` | [小时自动化主工作区路径冲突与触发器 memory 恢复设计](specs/2026-08-31-hourly-main-path-conflict-and-trigger-memory-recovery-design.md) |
