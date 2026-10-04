# 人工恢复成功飞书通知补齐设计

日期：2026-08-23
适用范围：schema 5 自动化 run 的人工恢复收口、现有飞书普通通知发送器

## 1. 问题与根因

正常小时入口会在正式结果集成后调用 `tools/send-feishu-notification.ps1`。人工恢复则由普通管理对话按 `开发管理/自动工作流恢复规则.txt` 完成重新核验、正式集成、`CompleteRun` 与清理；现行恢复规则没有要求发送成功结果通知。

因此，人工恢复可以正确形成正式提交并关闭 run，但负责人不会收到对应的飞书 `TaskOutcome`。该缺口已经在 2026-08-22 的两次人工恢复中重复出现。发送器、收件目标和飞书 REST 出站本身均已核验可用。

## 2. 目标与非目标

### 目标

- 人工恢复形成正式提交并完成精确 runtime 关闭后，发送一次与正常小时入口语义一致的飞书任务结果。
- Codex 正式完成使用 `completed`；DeepSeek 正式交付复审使用 `pending_review`。
- 保持现有幂等、脱敏和失败不回滚合同。

### 非目标

- 不修改 PowerShell、Node、runtime、集成入口、自动化配置或飞书私有配置。
- 不补发历史遗漏通知。
- 不为失败、仅清理、仅关闭空 run 或尚未完成 runtime 收口的人工处置发送成功通知。
- 不新增重试队列、包装器、状态字段或第二套通知机制。

## 3. 最小方案

只修改 `开发管理/自动工作流恢复规则.txt`，把通知写入人工恢复成功的既有收口顺序：

1. 先按当前阶段合同完成候选／正式提交核验、最新 `master` 重放、验证和正式集成。
2. 证明任务后置状态正确，并使用 schema 5 `CompleteRun` 精确关闭原 run。
3. 只有前两步均成功，才直接调用现有 `tools/send-feishu-notification.ps1 -Kind TaskOutcome` 一次：
   - `-RepositoryRoot` 使用当前仓库根目录；
   - `-TaskId` 和 `-RunId` 使用被关闭 run 的原始身份；
   - `-CommitSha` 使用已进入 `master` 的正式提交；
   - Codex 完成结果传 `-Status completed`；
   - DeepSeek 交付独立复审传 `-Status pending_review`。
4. 返回 `PROVIDER_ACCEPTED` 时记录已投递；其他脱敏结果只报告，不回滚 Git、任务投影或 runtime，不重试。

现有发送器以 `taskId + status + commitSha` 形成幂等键。即使人工对话因展示或记录中断再次核对，同一正式结果也不会产生第二条实际消息。

## 4. 边界与停止条件

- `CompleteRun` 未成功时不得发送成功通知。
- 正式提交不在 `master`、任务后置状态不正确或提交元数据不满足通知适配器合同时停止，不以手写正文绕过。
- `blocked`、`failed`、`waiting_decision`、`waiting_reply` 等状态继续使用既有对应路径，不套用本规则。
- 普通手动任务和非自动化 run 不因本设计新增通知。

## 5. 验证

规格实施后运行：

- `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-automation-workflow.ps1`
- `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-review-text.ps1 -Paths 开发管理/自动工作流恢复规则.txt`
- `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-pending-whitespace.ps1 -Paths 开发管理/自动工作流恢复规则.txt`
- `git diff --check`

规则集成后，通过现有 `tools/send-feishu-notification.ps1 -Kind DailyReport` 发送一次带当前 UTC `WindowUntil`、固定金丝雀标题和简短正文的普通飞书金丝雀。该调用复用与 `TaskOutcome` 相同的 REST 发送核心和收件目标，但不依赖临时任务卡或伪造业务提交，也不推进日报游标。核对发送结果为 `PROVIDER_ACCEPTED` 且通知审计增加一条；负责人再确认客户端是否实际可见。金丝雀失败不重试、不修改自动化配置。

## 6. 完成条件

- 恢复规则明确冻结上述顺序、状态映射、失败边界与禁止重复发送合同。
- 所有文本和自动化工作流检查通过。
- 唯一金丝雀获得飞书提供方接受，并得到负责人客户端可见性确认或留下明确的客户端侧残余问题。
