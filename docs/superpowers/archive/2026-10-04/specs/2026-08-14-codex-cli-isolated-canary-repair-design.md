# Codex CLI 空终态隔离修复与维护决策放行设计

日期：2026-08-14
状态：已批准，待新对话实施

## 1. 目标

在不再次占用生产 automation runtime、不把 `U-URP-VISUAL-BASELINE-01` 提前放入队列、也不执行 Unity 业务内容的前提下，隔离定位并修复 Codex 非交互子会话的空终态问题。只有独立 canary 完整通过后，才在生产入口应用负责人已经选择并验证的 A，使任务进入 `ready`，由后续新的 execute run 开工。

本设计处理两个已经分离的事实：

1. 跨决策回复误隔离已经由提交 `b2f47b9` 修复，A 回复已经验签并进入 `processed`。
2. `codex exec` 子会话仍连续产生 `task_complete` 且 `last_agent_message=null`，没有 assistant message、最终文件或 candidate；升级到 Codex CLI `0.147.0` 后仍可复现。

## 2. 当前生产事实与保护边界

- 维护型决策：`DEC-20260814-QM57A9A575FD7E`。
- 目标任务：`U-URP-VISUAL-BASELINE-01`。
- 已接受选项：A，即改用 Universal Renderer，采用 3D Mesh、标准 3D 灯光与阴影基线。
- 私有维护记录状态：`answered`，`optionKey=A`。
- 回复证据 SHA-256：`98e5b05cd4993abec9d314c1310cd5f83243c8bc265abf30dd8cd8e750ba1fa1`。
- 公开任务仍为 `pending_decision/awaiting_reply`，队列为空；这是有意保留的安全状态。
- 生产 schema 5 runtime 在交接时应为两个 owner run 均空、集成锁空闲；新对话必须重新调用 `Show`，不得相信本文的时间快照。
- 用户现有 `.agents/summary_state.json` 与 `设计总结.txt` 改动不属于本工作，不得暂存、提交或覆盖。

在隔离 canary 通过前禁止：

- 调用生产 `RunOnce`；
- 修改生产 runtime、任务卡、队列或维护型私有记录；
- 把目标任务直接改为 `ready`；
- 恢复旧模型会话、添加自动重试、模型回退或第二套 runtime；
- 执行 `U-URP-VISUAL-BASELINE-01` 的 Unity 业务内容。

## 3. 工作面拆分

### 3.1 工作面 A：隔离诊断与最小修复

新对话先创建独立手动 worktree 和分支。诊断使用新的临时目录与专用私有 state root；不得指向 `tzg-hourly-controller-runtime` 或飞书桥生产 inbox。临时产物不提交到仓库。

诊断矩阵严格按以下顺序执行，每一步只运行一次并保存脱敏的 JSONL 事件类型、子进程退出码、stderr 分类、最终文件存在性和 rollout 终态：

1. 当前实际模型的最小 `codex exec --json`，不带 output schema；提示只要求返回一句固定文本。
2. 同一最小提示加一个两字段最小 JSON Schema 和 `--output-last-message`。
3. 使用当前 `New-TerminalSchema`，但仍采用最小只读提示。
4. 只有前三步正常时，才在隔离 worktree 与专用 state root 中运行现有 Codex canary／candidate 合同。

一旦某一步首次复现空终态就停止扩大测试，按最小分界判断：

- 第 1 步失败：模型、认证、provider 或 Codex CLI 非交互执行层问题；项目内不得伪造修复，应保留脱敏证据并停止生产放行。
- 第 1 步通过、第 2 步失败：`--output-schema`／`--output-last-message` 路径问题。
- 前两步通过、第 3 步失败：项目终态 schema 问题。
- 前三步通过、第 4 步失败：candidate prompt、项目规则加载、wrapper 或 adapter 问题。

修复只能改首次失败边界的真实所有者。允许的候选路径必须在复现后重新冻结；预期首先检查：

- `tools/codex-cli-session.ps1`
- `tools/invoke-codex-candidate.ps1`
- 它们的直接测试

不能仅为得到更清楚的错误而宣称业务问题已修复。若 JSONL 已提供 `turn.failed` 或 `error`，wrapper 可增加稳定、脱敏的终态分类，但仍必须证明正常调用能够产生 assistant message 和 output-last-message 文件。

不得猜测性加入 `--ignore-user-config`、缩短正式 prompt、放宽 schema、恢复旧 session、自动重试或更换模型。只有隔离矩阵证明某项是首次失败边界且正式合同仍完整时才能修改。

### 3.2 工作面 B：生产维护决策放行

工作面 A 修复完成并提交后，先在专用 state root 运行 Codex canary，要求同时证明：

- 实际模型与外层核验证明一致；
- JSONL 存在单一 `thread.started`、正常终态和 agent message；
- output-last-message 文件存在且通过 schema；
- canary candidate 提交、路径、元数据和清理合同均通过；
- 不触碰生产 runtime、任务卡或队列。

只有以上全部通过，才进入生产放行：

1. 从主工作区重新调用 schema 5 `Show`，确认两个 owner run 均空且集成锁空闲。
2. 重读同一任务卡、队列、私有维护记录和 accepted reply，核对 decisionId、A、任务摘要与证据哈希。
3. 调用一次正常 Codex `RunOnce`；不得恢复此前任何空终态 session。
4. 该维护 run 只准备 A 路线的完整 expectedPaths、验证、完成条件和停止条件，再由共享内核调用 `ResolveMaintenanceDecision`。
5. 预期终态为 `maintenance_completed`，任务达到 `ready/resolved/A`，队列位置为 0。
6. 本轮不执行 Unity 业务；下一次新的 `codex_execute` run 才能领取并开工。

## 4. 验证合同

隔离修复至少运行：

- `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-codex-cli-session.ps1`
- `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-invoke-codex-candidate.ps1`
- `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-automation-workflow.ps1`
- 使用专用 state root 的真实 Codex canary

如修改飞书桥以外路径，不重复已经通过且输入未变化的 338 项桥接器测试。修改自动化共享基础设施时，补跑与实际变更直接相关的 owner、runtime 和 QueueMaintenance 合同测试。

生产放行后验证：

- `tools/check-task-cards.ps1 -TaskId U-URP-VISUAL-BASELINE-01 -Postcondition MaintenanceResolvedReady -OutputJson`
- 队列第一行是 `U-URP-VISUAL-BASELINE-01`，route／owner 保持 `codex_execute/codex`。
- `automationDecision.status=resolved`、`selectedOption=A`、目标状态为 `ready`，证据哈希与 accepted reply 一致。
- 回复证据只留在 `processed`；仅在 processed 与 quarantine 副本逐字节相同且生产 runtime 已关闭后，定点删除冗余 quarantine 副本。
- 主工作区最终只保留用户原有无关改动，以及已经正式集成的预期提交。

## 5. 停止条件

出现以下任一情况立即停止，不通过兼容补丁推进生产任务：

- 最小无 schema 的 `codex exec` 仍为空终态；
- 实际模型、认证、CLI 版本或 provider 证据不一致；
- 修复需要重试层、模型回退、第二 runtime 或恢复旧模型会话；
- canary 未产生合法 agent message、最终文件、candidate 或清理证据；
- 生产任务摘要、accepted reply、decisionId、证据哈希、owner run 或集成锁发生变化；
- 预期修改路径与主工作区现有人工改动冲突；
- 任务卡无法为 A 路线冻结完整、原子的实施边界。

## 6. 交接给新对话

新对话的明确任务是：按照本文先完成工作面 A；只有隔离 canary 全部通过后，才执行工作面 B。开场先读取本文、`开发管理/自动工作流规则.txt`、实时 `Show`、目标任务卡、`UNITY_STRUCTURE.md`，并使用 `openai-docs` 与 `unity-agent-workflows`。不得重新讨论 A 是否有效，也不得再次从 quarantine 恢复回复；A 已经处于 answered／processed。
