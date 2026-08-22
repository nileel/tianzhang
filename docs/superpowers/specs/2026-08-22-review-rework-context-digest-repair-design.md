# 复审返工上下文摘要与现场恢复设计

## 状态

- 日期：2026-08-22
- 设计状态：负责人已批准，等待书面规格复核
- 目标事件：`DEC-20260822-REV607519A26E5B`
- 目标任务：`C-SECT-ALIGN-01A`
- 已批准返工选择：A，交回 DeepSeek

## 目标

1. 修复复审返工投影中任务上下文摘要算法不一致的问题。
2. 使用原决策、原 A 选项和已核验回复证据完成一次受控恢复，使 `C-SECT-ALIGN-01A` 回到 `external_execute/deepseek/ready`。
3. 在相关回归、双 owner canary 和最终控制面检查全部通过后恢复两个小时写入自动化。

## 非目标

- 不修改北地三宗业务正文或复审结论。
- 不新增第二 runtime、恢复对象、决策卡、自动重试、兼容分支或后台守护。
- 不伪造飞书回复、操作者身份或证据哈希。
- 不重构整个摘要实现，也不修改无关 Unity、BattleSim、日报或周报流程。
- 不清理与本事件无精确所有权关系的其他历史 worktree 或 branch。

## 已核验事实

### 失败结果

- 自动化运行任务 `01a02981-784a-7140-b2c6-c3ffb29d5fbc` 属于 `deepseek-hourly-trigger`。
- 该轮在处理复审返工决策时返回 `attention_required/review_rework_projection_failed`。
- 决策记录状态为 `attention_required`，目标选项为 A；私有状态转换记录保存了原 `replyEvidenceHash`。
- `C-SECT-ALIGN-01A` 仍为 `codex_review/codex/blocked`，不在当前队列中；未通过审核清单仍保存定向返工事实。
- schema 5 当前两个 owner run 均为空，集成锁空闲。

### 根因

`tools/invoke-hourly-owner.ps1` 的 `Get-TaskContextDigest` 在 2026-08-22 的冻结输入物化改动中加入了规范化 `automationInputs`：即使任务未声明输入，摘要 JSON 也包含 `automationInputs: []`。

`tools/set-task-automation-state.ps1` 的同名摘要函数没有同步该字段。`RequeueReview` 使用后者复核决策记录中的摘要，因此同一任务得到不同 SHA-256：

- 共享入口与决策记录：`e2d7d01086b930421e61401df4577f3f827dae13ffbd103776ad6a30aa2a217d`
- 状态投影脚本：`2938cf284ec04b161b270f0a34eef1a376eb74abf8d46da5f7197fbf941f7e5f`

现有 `tools/test-review-rework-decision.ps1` 已在未修改代码的基线上稳定复现同一 `review_rework_projection_failed`，所以无需通过新增兜底来猜测失败原因。

### 保留现场

- 失败 worktree：`.worktrees/automation/decisions/dec-20260822-rev607519a26e5b`
- 失败 branch：`codex/automation/decision/dec-20260822-rev607519a26e5b/state-607519a26e5b`
- 现场 HEAD：复审提交 `607519a26e5bb50a8a4eb4b712fbff0af242356f`
- 已核验工作树干净，没有投影差异、正式提交或未集成业务内容。

这些事实必须在实施前重新读取；本文记录不能替代实时证据。

## 方案选择

采用“最小合同对齐 + 原决策一次性受控恢复”。

不采用以下方案：

1. 抽取新的共享摘要模块：能减少未来重复，但会扩大本次改动边界和调用面。
2. 为 `attention_required` 增加自动重试：会让普通失败进入新的自动恢复路径，违反现有停止条件。
3. 重新发送一张决策卡：会产生第二决策与重复消费风险，而且原 A 回复已有私有证据。

## 实施顺序

```text
暂停两个小时写入自动化
  -> 重新核验 runtime、锁、任务与决策现场
  -> 在独立手动 worktree 修复摘要合同
  -> 运行相关测试与静态门禁
  -> 通过共享集成锁合入代码修复
  -> 精确收回失败 decision worktree/branch
  -> 将原决策恢复到一次性可消费状态
  -> 手动调用 DeepSeek 共享入口消费原 A 回复
  -> 验证任务投影、决策消费、通知与清理
  -> Codex/DeepSeek canary
  -> 最终 Show 与自动化配置复核
  -> 恢复两个小时写入自动化
```

任一步失败都立即停止；两个小时写入自动化保持暂停，保留当时现场，不进入下一阶段。

## 第一阶段：暂停与前置核验

1. 只通过 Codex 自动化管理接口把 `codex-hourly-worker` 和 `deepseek-hourly-trigger` 设为 `PAUSED`，不编辑 automation TOML。
2. 不修改 `tzg-daily-automation-briefing` 与 `tzg-weekly-project-summary`。
3. 执行 schema 5 `Show`，要求：
   - `runs.codex` 与 `runs.deepseek` 均为空；
   - `activeTaskIds` 为空；
   - `integrationLockStatus=none`。
4. 核对主工作区仍位于 `master`，记录实时 HEAD 与相关路径状态。
5. 核对任务卡、当前队列、来源 backlog、未通过审核清单、复审提交和决策私有记录仍与本文事实一致。
6. 核对失败 decision worktree/branch 的路径、HEAD、清洁状态、主分支可达性，并证明没有正式结果或唯一业务内容。

## 第二阶段：摘要合同修复

### 修改点

1. `tools/set-task-automation-state.ps1`
   - 在 `Get-TaskContextDigest` 中加入 `automationInputs`。
   - 字段位置、数组顺序和每项字段顺序与 `tools/invoke-hourly-owner.ps1` 完全一致。
   - 每项只规范化为 `path`、`bytes`、`sha256`；未声明时固定为 `[]`。
   - 不改变任务卡合法性规则、RequeueReview 状态机或其他投影动作。
2. `tools/check-automation-workflow.ps1`
   - 在现有静态合同检查中要求共享入口和状态投影两处上下文摘要都包含规范化 `automationInputs`。
   - 不新建测试框架或第二份摘要实现。

### 回归验证

必须从修复前红灯转为修复后绿灯：

- `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-review-rework-decision.ps1`
- `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-hourly-task-input-materialization.ps1`
- `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-check-automation-workflow.ps1`
- `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-automation-workflow.ps1`

同时执行：

- PowerShell 语法解析检查；
- `tools/check-pending-whitespace.ps1`，范围只含本次实际修改路径；
- `git diff --check`，暂存后再运行 `git diff --cached --check`。

不运行 Unity、BattleSim 或数据链验证，因为本次没有相关输入变化。

### 集成

1. 在独立手动 worktree 中形成一个路径限定修复提交。
2. 合并前从主工作区重新执行 schema 5 `Show`，重读相关现场，并核对主工作区相关路径无 staged、unstaged 或 untracked 冲突。
3. 只通过 `tools/invoke-project-integration.ps1` 取得共享进程锁并 fast-forward；不得直接绕过锁合并。

## 第三阶段：原决策一次性恢复

### 失败现场收回

只有下列条件全部满足时，才移除失败 decision worktree 和 branch：

- 私有记录仍为同一 decisionId、taskId、reviewCommit 和 `review_rework_projection_failed`；
- 没有 `formalHead`；
- worktree 与 branch 字面量路径精确匹配记录；
- worktree 干净，HEAD 等于复审提交；
- HEAD 已由 `master` 包含，branch 没有唯一提交；
- 当前没有 owner run，集成锁空闲。

不使用通配符、递归泛化清理或对其他 worktree 的顺带处理。

### 恢复原记录

1. 读取原决策记录和原 `RequeueReview` 状态转换记录。
2. 要求转换记录为 option A，且 taskId、decisionId、taskDigest、taskContextDigest、reviewCommit、reviewEntryDigest 与决策记录完全一致。
3. 原 `replyEvidenceHash` 必须为 64 位小写十六进制，并与转换记录一致。
4. 通过现有私有记录原子写入与 ACL 合同，把同一记录从 `attention_required` 恢复为 `awaiting_reply`，只移除本次失败生成的 `detailCode`、`evidenceWorktree`、`evidenceBranch` 字段。
5. 不新建决策、不改 option、不生成新 evidence hash，也不直接编辑私有 JSON。

### 消费与投影

1. 在两个定时器保持暂停的情况下，手动前台调用一次 DeepSeek 共享入口 `RunOnce`。
2. 入口必须从原决策请求取得同一 `OPTION_ACCEPTED/A` 回复；如果返回 `NO_REPLY`、不同 option、不同 evidence hash 或任何非法结果，立即停止，不回退到人工伪造回复。
3. 入口按现有 `Apply-AnsweredReviewRework` 路径：
   - 复用原 decision worktree 字面量路径，并按现有算法从最新 `master` 创建新的 state branch；
   - 调用修复后的 `RequeueReview`；
   - 形成标准 Automation 元数据提交；
   - 持有共享集成锁 fast-forward；
   - 把原决策标记为 `consumed` 并记录 option A、原 replyEvidenceHash、formalHead 和 consumedAt；
   - 发送一次现有 TaskOutcome 通知，不重试；
   - 仅按精确合同清理成功 worktree/branch。

该步骤不启动 DeepSeek 业务 candidate；它只恢复任务投影。实际内容返工由恢复自动化后的全新 DeepSeek owner run 领取。

## 第四阶段：后置验证与复启

### 任务与决策后置条件

- `C-SECT-ALIGN-01A.route=external_execute`
- `C-SECT-ALIGN-01A.owner=deepseek`
- `C-SECT-ALIGN-01A.dispatchState=ready`
- `stateReason` 绑定原 decisionId 和 DeepSeek 返工选择
- 当前队列在原 `queueIndex=0` 插入该任务，队列行与任务卡一致
- 来源 backlog 主责为 DeepSeek、状态投影为已排队
- `check-task-cards.ps1 -TaskId C-SECT-ALIGN-01A -Postcondition ExternalDispatchReady -ExpectedOwner deepseek -OutputJson` 通过
- 原决策记录为 `consumed`，formalHead 位于 `master`，没有第二决策记录
- 失败和成功 decision worktree/branch 均只在满足各自精确清理合同时消失

### 控制面验证

1. 调用 Codex canary；`-Model` 参数必须来自本轮 Node REPL request metadata 核验，命令其余参数沿用 `tools/invoke-hourly-owner.ps1 -Owner codex -Action Canary -RepositoryRoot "D:\天章游戏开发" -OutputJson`。
2. `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/invoke-hourly-owner.ps1 -Owner deepseek -Action Canary -RepositoryRoot "D:\天章游戏开发" -OutputJson`
3. 最终 schema 5 `Show` 必须仍显示两个 owner run 为空、activeTaskIds 为空、集成锁空闲。
4. 通过自动化管理接口恢复两个小时写入自动化原有 schedule、model、reasoning effort、通知策略和本地项目绑定，只把状态改回 `ACTIVE`。
5. 再次 view 两个自动化，确认配置未发生其他漂移。

## 停止条件

出现以下任一情况立即停止，保持两个写入自动化暂停：

- runtime、集成锁、主分支或相关任务事实与实施前快照冲突；
- 修复需要新增自动重试、兼容摘要、第二决策或第二私有状态对象；
- 失败 decision worktree 存在差异、唯一提交、未知进程所有权或路径不匹配；
- 原回复不能以同一 option A 和 evidence hash 再次取得；
- 任务卡、队列、backlog、审核条目或复审提交摘要发生变化；
- 任何相关测试、投影后置条件、canary、空白或 Git 检查失败；
- 集成无法 fast-forward，或主工作区相关路径存在冲突。

不通过额外补丁、手工伪造回复、重发决策或跳过验证绕过停止条件。

## 预期终态

- 两处任务上下文摘要对 `automationInputs` 使用相同规范化合同。
- 复审返工集成测试与输入物化测试通过；静态门禁能发现字段再次缺失，集成测试负责发现会改变摘要结果的合同漂移。
- `C-SECT-ALIGN-01A` 使用原 A 决策恢复为队首 `external_execute/deepseek/ready`。
- 原决策记录完成消费，没有重复决策、伪造回复或残留 decision worktree。
- 双 owner canary 与最终控制面检查通过，两个小时自动化恢复为 `ACTIVE`。
