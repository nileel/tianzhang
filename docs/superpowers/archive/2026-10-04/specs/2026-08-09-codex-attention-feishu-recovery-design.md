# Codex 异常通知与重复复审恢复设计

日期：2026-08-09
适用范围：`codex-hourly-worker`、复审返工证据、飞书决策桥计划任务、当前残留 Codex run

## 1. 问题与事实

本次故障由三个相邻缺口叠加形成：

1. 复审不通过后的飞书返工决策卡要求长连接桥处于 `CONNECTED`，但计划任务只有登录触发且 `RestartCount=0`。桥退出后不会有限自恢复，导致 2026-08-08 13:17、14:15、15:17 三次决策卡返回 `CHANNEL_UNAVAILABLE`。
2. 同一任务第二轮复审时，`开发管理/未通过审核清单.txt` 的同一三级标题条目保留旧轮次“审核对象”，并追加本轮“审核对象”。共享入口只读取第一条，因而把旧 SHA 与本轮被复审 SHA 比较，错误进入 `review_rework_reviewed_commit_changed`。
3. 共享入口首次进入 `attention_required` 时没有普通飞书失败通知；后续轮次按既定规则只返回 `existing_run`。因此负责人没有从飞书得知首次异常，随后每小时持续静默。

当前 Codex run `c836f8c2-3582-43f9-a353-a83e3a49e4d9` 保留合法 candidate 证据，但未形成可集成 canonical 结果。主分支中的任务 `C-HS-YY-JD-01L` 仍为 `codex_review/codex/ready`。

## 2. 目标与非目标

### 目标

- 同一未通过审核条目可保留历史轮次，同时精确识别本轮被复审提交。
- owner run 首次进入 `attention_required` 时发送一次普通飞书失败通知。
- 后续 `existing_run` 不重复通知。
- 飞书决策桥异常退出后由现有 Windows 计划任务执行有限重启。
- 精确关闭当前残留 Codex run，使后续小时轮次可创建全新 run 重新复审。

### 非目标

- 不补发任何历史通知。
- 不自动恢复旧模型会话，不自动重放当前 candidate，不把当前 candidate 直接合并到主分支。
- 不新增通知重试队列、第二 runtime、第二守护进程或自动冲突解决。
- 不改变复审不通过后由负责人选择返工方的业务流程。
- 不修改 Unity、BattleSim、游戏数据或内容设定。

## 3. 方案选择

采用定向永久修复：

- 审核证据以调用方已经核验的 `reviewedCommit` 为选择键，而不是依赖条目顺序。
- 异常通知挂在共享入口最终结构化结果形成之后，只处理本轮首次返回的 `attention_required`。
- 桥可靠性继续由现有计划任务负责，仅增加有限重启设置，不创建新进程体系。
- 当前 run 使用 schema 5 已有的 candidate-attention 精确关闭合同收口，保留现场证据并让任务保持 ready。

不采用“取最后一条审核对象”，因为条目顺序不是稳定身份；不采用“覆盖旧审核对象”，因为会丢失历史复审线索；不采用通用重试队列，因为超出既定架构边界。

## 4. 详细设计

### 4.1 本轮审核对象精确匹配

`Get-ReviewEntryEvidence` 增加必需的预期提交参数。函数仍先按任务 ID 定位唯一三级标题条目，然后提取该条目内全部规范“审核对象”行，并执行：

1. 预期 SHA 必须为完整 Git SHA。
2. 条目中与预期 SHA 精确相等的规范行必须恰好一条。
3. 零条表示本轮审核证据缺失；多条表示本轮证据重复；两者都沿用 `review_rework_entry_invalid` 停止。
4. 其他 SHA 的规范行作为历史轮次保留，不参与本轮选择。
5. 返回的 `ReviewedCommit` 固定为预期 SHA，条目 digest 仍覆盖整个三级标题逻辑单元。

共享入口在 formal integration、返工决策上下文形成和回复消费后的证据复核中，都传入各自已经绑定的本轮 `reviewedCommit`。不得由文件顺序重新推断。

### 4.2 首次 attention 通知

新增一个窄的最终结果通知步骤：

- 仅当本轮最终状态为 `attention_required`、存在当前 owner run、并具有 taskId、runId、detailCode 时调用。
- 使用现有 `send-feishu-notification.ps1 -Kind TaskOutcome -Status failed`，幂等键继续由 `taskId + failed + runId + detailCode` 形成。
- 通知使用普通飞书 REST 出站，不依赖长连接桥健康。
- 通知结果只附加到结构化终态；失败不改变 runtime、任务卡、Git 或退出状态。
- `existing_run`、`no_candidate`、`QueueMaintenance` 和没有 owner run 的前置异常不调用该步骤。

该位置确保 formal integration 停止、candidate 终态无效、集成锁超时等已经把本轮 run 置为 `attention_required` 的路径共享同一通知行为，不在每个 catch 分支重复实现。

### 4.3 飞书桥有限自恢复

现有 `TianZhang-Feishu-Decision-Bridge` 计划任务继续使用 AtLogOn、隐藏窗口、`IgnoreNew` 和零执行时限。安装时先按完整启动脚本与桥入口路径清理已验证的旧脱管进程链，再由任务动作直接托管带 `-WindowStyle Hidden` 的 `pwsh`，避免中间 `wscript` 提前退出后桥进程脱离任务生命周期。安装器把设置补充为：

- `RestartCount=3`；
- `RestartInterval=PT1M`。

只处理桥进程以失败状态退出的短时异常。三次仍失败后停止，由现有健康状态与首次 attention 通知暴露问题；不无限重启，不增加后台 watchdog。安装器测试同时验证计划和真实 adapter 都携带这两个设置。

### 4.4 当前残留 run 处置

代码与测试合并后，重新读取 schema 5 `Show`、任务卡、队列、集成锁和 worktree 证据。只有以下事实全部保持时才关闭当前 run：

- owner=`codex`；
- runId=`c836f8c2-3582-43f9-a353-a83e3a49e4d9`；
- taskId=`C-HS-YY-JD-01L`；
- state=`attention_required`；
- recoveryReason 与当前 runtime 完全一致；
- candidate commit、worktree、当前 worktree branch 和 HEAD 与 runtime／Git 现场精确匹配；
- 主分支任务仍为 `codex_review/codex/ready`；
- 集成锁空闲。

满足后调用现有 `CompleteRun` 的 `failed` candidate-attention 合同，并传入全部期望证据。预期返回 `evidenceRetained=true`；不删除该 worktree 或 branch，不发送补发通知。下一次自然 Codex 小时轮次从最新 master 创建全新 run 重新复审。

任一证据变化都停止处置并保留现场，不猜测、不改用人工 cherry-pick。

## 5. 数据流与状态

正常重复复审：

`candidate review` → 在同一条目追加本轮完整 SHA → 共享入口以捕获的 `reviewedCommit` 精确匹配 → formal validation → fast-forward → `CompleteRun(success)` → 一次性返工决策卡。

首次异常：

任一已领取 run 的阶段失败 → runtime 写入 `attention_required` → 最终结果统一调用普通失败通知 → 返回带脱敏通知状态的结构化终态。

后续轮次：

`Show` 发现已有 run → 返回 `existing_run` → 不发送重复通知、不恢复、不领取新任务。

## 6. 错误处理

- 审核条目找不到本轮 SHA 或同一 SHA 重复出现：停止为 `review_rework_entry_invalid`，不得选择其他历史 SHA。
- attention 通知投递失败：保留 `attention_required`，只返回脱敏通知结果。
- 桥三次重启仍失败：计划任务停止重启；不由小时入口启动第二桥。
- 当前 run 关闭证据不一致：不调用 `CompleteRun`，不清理任何现场。
- 实施期间发现需要修改 runtime schema、任务卡结构或通知重试语义：触发停止条件，重新设计。

## 7. 验证

最小充分验证为：

1. `tools/test-review-rework-decision.ps1`
   - 同一条目含旧 SHA 与本轮 SHA时选中本轮；
   - 本轮 SHA 缺失时拒绝；
   - 本轮 SHA 重复时拒绝；
   - 既有返工决策消费测试继续通过。
2. 新增或扩展共享入口测试
   - `attention_required` 且有 run 时恰好调用一次失败通知；
   - `existing_run`、成功、无 run 时不调用；
   - 通知失败不改变最终状态。
3. `tools/test-install-feishu-decision-bridge.ps1`
   - 计划包含三次、一分钟有限重启；
   - 安装后的 task 设置投影一致。
4. `tools/check-automation-workflow.ps1`。
5. 预期路径空白检查和 `git diff --cached --check`。
6. 安装更新后的计划任务，核验 `RestartCount=3`、`RestartInterval=PT1M`、任务运行且健康为 `CONNECTED`。
7. 关闭当前 run 后重新 `Show`，确认 `runs.codex=null`、DeepSeek run 未被改变、集成锁仍空闲、任务仍为 ready。

不运行 Unity、BattleSim 或数据链检查，因为相关输入不变。

## 8. 实施边界与提交

预期代码路径仅包括：

- `tools/invoke-hourly-owner.ps1`
- `tools/test-review-rework-decision.ps1` 或一个窄的共享入口通知测试
- `tools/install-feishu-decision-bridge.ps1`
- `tools/test-install-feishu-decision-bridge.ps1`
- `tools/check-automation-workflow.ps1`（仅当现有静态合同需要同步）
- 本设计文档

实施在独立 `.worktrees/` worktree 中完成。合并前重新核验两个 owner run、集成锁、主分支 HEAD 和路径冲突，通过 `tools/invoke-project-integration.ps1` 取得同一集成锁后 fast-forward。不得 stage 或提交主工作区已有改动。
