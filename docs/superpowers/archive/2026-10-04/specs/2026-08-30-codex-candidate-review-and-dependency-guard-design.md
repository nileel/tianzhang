# Codex 候选复审与依赖投影边界修复设计

## 背景与根因

2026-08-30 的 Codex 自动复审 run `995186f8-31c7-4900-b5c9-59da506f22ef` 对任务 `C-HS-YY-JD-01I` 生成候选 `f053e314b2c5c2eed554b724e6316a91d7af9c36`。候选错误判定复审通过，并在 `开发管理/任务列表/内容设计任务.txt` 中提前从父任务 `C-HS-YY-JD-01` 的“阻塞于”投影移除 `C-HS-YY-JD-01I`，但父任务卡的 `blockedBy` 未同步。`tools/check-task-cards.ps1` 因此报 `backlog blocker mismatch`，候选以 `codex_candidate_postcondition_failed` 进入 `attention_required`。

候选未进入 `master`；人工复审随后确认三份业务文档仍缺独立证位核心，泥丸玄宫另有冲突胜负判定矛盾，并将任务设为 `blocked`。本设计只防止同类候选越界，不恢复失败候选、不改既有业务结论。

## 目标

1. 普通 `codex_execute`／`codex_review` 候选只维护当前任务自身状态，不提前收口其他任务的依赖投影。
2. `codex_review` 只审核被审事实，不通过修改业务语义来制造“通过”。
3. 用候选真实 prompt 的回归断言固定上述合同。
4. 保持现有 schema 5 runtime、后置条件、失败保留、QueueMaintenance 和通知机制不变。

## 非目标

- 不新增语义审核器、自动重试、自动回滚、兼容分支或第二恢复机制。
- 不让 wrapper 自动改写模型候选或替模型裁决业务内容。
- 不修改 `tools/check-task-cards.ps1` 的一致性门禁。
- 不清理历史失败 worktree／branch，不重新执行 `C-HS-YY-JD-01I`。

## 方案

### 1. 通用候选依赖边界

在 `tools/invoke-codex-candidate.ps1` 的 `New-Prompt` 通用合同中新增一条明确规则：

- 非 QueueMaintenance 候选不得从其他任务卡的 `blockedBy` 或 backlog 行的“阻塞于”投影移除当前 taskId，也不得顺带提升、重排或关闭下游任务。
- 当前任务完成后，其他任务对它的具名前置引用继续保留；只有正式结果进入 `master` 后，后续 QueueMaintenance 才按既有事实源同时更新下游任务卡和 backlog 投影。

这条规则同时约束 `Execution` 和 `Review`，避免只修复单一路由。

### 2. Codex 复审语义边界

扩展 `Review` 路由指令：

- 先以目标提交、任务卡完成条件和直接事实源形成“通过／部分通过／不通过”结论。
- 不得修改被审业务语义来消除缺口或制造通过。
- 结论通过时，只可更新任务生命周期、索引中的内容状态以及被审文件中明确存在的审核标记／审核记录；不得顺手补写规则或内容。
- 结论为部分通过或不通过时，保留被审业务文件，按既有格式写入 `开发管理/未通过审核清单.txt`，把当前任务设为 `blocked` 并移出 ready 队列。

既有完整 SHA、三级标题和审核结论格式继续沿用，不建立第二套复审格式。

### 3. 回归测试

在 `tools/test-invoke-codex-candidate.ps1` 中对实际传给 runner 的 prompt 增加断言：

- 普通候选 prompt 必须包含“其他任务依赖只由 QueueMaintenance 收口”的规则。
- Review prompt 必须包含“不得修改业务语义制造通过”的规则，以及通过和不通过两条处理边界。
- 既有 QueueMaintenance prompt 断言继续通过，证明其依赖收口职责未被削弱或转移。

测试只验证合同进入真实 prompt，不模拟或依赖自然语言模型的业务判断。

## 数据流与失败行为

```text
共享入口 claim 当前任务
-> invoke-codex-candidate 生成带边界的 prompt
-> Codex 只修改当前任务授权路径
-> candidate finalizer 形成唯一提交
-> wrapper 核对路径、元数据和 CodexClosedOrNonReady
-> 共享入口在最新 master 重放、验证并集成
-> 后续 QueueMaintenance 收口已完成前置对下游任务的影响
```

若候选仍违反合同，现有路径限制或 `check-task-cards.ps1` 必须继续 fail closed；不得自动修补、重试或清理失败现场。

## 修改范围

- `tools/invoke-codex-candidate.ps1`
- `tools/test-invoke-codex-candidate.ps1`

明确不修改：runtime、共享入口、任务卡检查器、QueueMaintenance 实现、自动化配置、任务卡、队列、业务文档。

## 验证

1. `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-invoke-codex-candidate.ps1`
2. `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-automation-workflow.ps1`
3. `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-pwsh-runtime.ps1`
4. 对本轮修改路径运行 `tools/check-pending-whitespace.ps1`。
5. 暂存后运行 `git diff --cached --check`。

不运行 BattleSim、Unity EditMode／PlayMode 或数据链检查，因为本修复不改数值、Unity 行为、CSV、asset 或业务数据链。

## 完成条件

- 两条新边界均出现在真实候选 prompt 中，并由定向测试锁定。
- 既有候选、QueueMaintenance 和自动化工作流检查全部通过。
- 仅修改设计列出的两个实现／测试文件；无 runtime、配置、任务或业务内容变化。
- 失败候选仍由原门禁进入人工处置，不引入自动恢复行为。

## 回滚

回滚仅需撤销上述两个文件中的提示合同与对应测试断言；schema、runtime 状态和仓库业务事实不受影响。
