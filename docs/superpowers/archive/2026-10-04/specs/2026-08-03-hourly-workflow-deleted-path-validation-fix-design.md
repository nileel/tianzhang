# 小时工作流删除路径验证修复设计

日期：2026-08-03
状态：已批准

## 1. 背景与根因

共享小时入口的正式组合验证先取得 `base..formalHead` 的全部 changed paths，再把同一列表传给 `check-pending-whitespace.ps1`。Codex 复审完成任务时会把活跃任务卡移动到归档，因此 changed paths 同时包含新增归档文件和已删除的原任务卡。

`check-pending-whitespace.ps1` 的通用合同要求每个输入路径当前存在；它正确地把不存在路径视为调用错误。共享入口把合法删除路径传给该检查器，导致 `hourly_whitespace_failed`。失败发生在 formal commit 已形成、runtime 尚未记录 canonical 证据、`master` 尚未 fast-forward 的阶段。

根因属于共享入口的路径分类错误，不属于业务内容、Git diff 或通用空白检查器错误。

## 2. 目标与非目标

目标：

- 合法删除路径不再进入“当前文件内容”空白检查；
- 全部 changed paths 继续受任务授权范围和 Git diff 检查约束；
- 精确关闭当前失败 run，保留其 worktree、branch、candidate 和 formal 证据；
- 由新 run 重新执行同一 Codex 复审，通过正常共享入口闭环；
- 两个 automation 在修复、人工处置和重新验证完成前保持暂停。

非目标：

- 不恢复旧 Codex 会话；
- 不接纳 runtime 未记录的 formal commit；
- 不增加 `attention_required -> canonical_ready` 恢复路径；
- 不修改通用空白检查器对缺失输入路径的失败语义；
- 不新增第二 runtime、通用重试、事故专用迁移、自动冲突解决或长期兼容入口。

## 3. 方案选择

采用“过滤删除路径 + 精确关闭失败 run + 新 run 重做复审”。

未采用的方案：

- 直接接纳现有 formal commit：需要为未记录 canonical 证据新增事故恢复路径；
- 让通用空白检查器忽略缺失路径：会掩盖路径拼写错误或意外文件丢失；
- 直接编辑私有 runtime JSON：绕过 schema 5 的原子写入、ACL 和关闭合同。

## 4. 组合验证修改

`Invoke-CombinedValidation` 保留两组路径：

1. `changedPaths`：`base..formalHead` 的完整路径集合，用于非空检查、任务授权上界、后置条件、数据链触发判断和完整 Git diff 检查；
2. `contentCheckPaths`：从同一 Git diff 以非删除状态过滤得到的路径集合，只用于 `check-pending-whitespace.ps1`。

过滤必须依据 Git diff 状态排除 `D`，不依据当前文件系统静默吞掉缺失文件。这样，Git 认为应当存在的新增、修改、复制或重命名目标如果实际缺失，通用空白检查器仍会失败。

当 `contentCheckPaths` 为空时跳过当前文件内容检查；`git diff --check base..formalHead` 仍无条件执行。删除路径仍保留在 `changedPaths`，因此不能绕过授权路径、任务后置条件或数据链判断。

不修改 `check-pending-whitespace.ps1`。

## 5. 当前失败 run 的人工关闭合同

当前 run：

- owner：`codex`
- taskId：`U-BOUNTY-01B`
- runId：`04b8d73f-e247-436f-a5d8-32b842a45bbb`
- state：`attention_required`
- recoveryReason：`formal integration stopped: hourly_whitespace_failed`

现有 `CompleteRun` 只允许没有 candidate 证据的 `attention_required` run 以 `failed` 关闭，与批准设计中“人工放弃结果并关闭 run”不一致。关闭合同扩充为一个人工放弃分支，但不提供继续或接纳能力。

人工放弃必须同时满足：

- `CompletionCategory=failed`；
- owner、runId 和完整 `ExpectedRecoveryReason` 与 runtime 严格相等；
- 调用方提供的 `ExpectedCandidateCommit` 与 runtime 严格相等；
- 调用方提供的 `ExpectedWorktree` 与 runtime 规范化绝对路径严格相等，且位于记录的 repository `.worktrees/automation/<runId>/<owner>`；
- worktree 仍被 Git 注册，当前 branch、HEAD 与调用方提供的 `ExpectedWorktreeBranch`、`ExpectedWorktreeHead` 严格相等；
- worktree 干净；
- runtime 中已有的 candidateResult、session 和其他证据不被改写；
- canonicalBranch、canonicalBase、canonicalHead 仍与 runtime 当前记录精确一致；本次现场三者均必须为 null。

任一证据不符时返回不可关闭并保持 runtime、worktree 和 Git 引用不变。关闭成功只清空 owner run；不得删除 worktree、branch、candidate、formal commit 或发送成功通知。

该分支只实现批准设计已有的“人工放弃并关闭”，不允许把 run 转回其他阶段，不允许自动调用，也不允许吸收或集成现有 formal commit。

## 6. 新 run 重做复审

精确关闭失败 run 后，`master` 上的 `U-BOUNTY-01B` 仍保持 `codex_review/codex/ready` 与 `pending_review` 事实。人工调用共享 Codex 入口，由确定性选题创建新 run、新 worktree 和新模型会话，重新执行独立复审。

新 run 不读取旧模型会话，也不 cherry-pick 旧 candidate 或未记录 formal commit。正常成功必须重新形成 candidate、在最新 `master` 上重放、执行修复后的组合验证、在同一进程持有型锁下 fast-forward、`CompleteRun`、通知和精确清理。

旧失败 worktree 继续作为人工保留证据，不参与新 run 的成功清理。

## 7. 测试与验证

代码验证：

- 在共享 Codex 入口夹具中加入真实“删除活跃任务卡并新增归档文件”的正式结果，断言组合验证通过；
- 断言删除路径仍属于正式 changed paths 和任务授权路径；
- 断言新增／修改文件中的尾随空白仍导致 `hourly_whitespace_failed`；
- 保持 `test-check-pending-whitespace.ps1` 的缺失路径失败用例通过，证明通用检查器未被放宽；
- 执行共享 owner、runtime、adapter、decision checkpoint、统一集成锁和 PowerShell 语法测试。

人工关闭验证：

- 添加候选 SHA、recoveryReason、worktree 路径、branch、HEAD、dirty 状态和 canonical 字段的正反例；
- 每个不匹配用例均断言 runtime 与 worktree 不变；
- 成功关闭断言 owner run 为空、worktree 和 branch 仍存在、`master` 未变化。

真实验证：

1. 保持两个 automation 暂停并核验共享锁空闲；
2. 在实施 worktree 完成代码、测试、whitespace 和 cached diff 检查；
3. 通过统一手动集成锁把修复 fast-forward 到最新 `master`；
4. 按完整证据调用人工放弃关闭合同；
5. 手动运行新的 Codex 入口，确认同一任务正常复审、集成、通知和安全清理；
6. 重新运行两 owner 私有 canary；
7. 只有上述步骤全部通过，才把实时 prompt 切回共享入口并继续原迁移设计的并行任务与真实周期观察。

## 8. 停止条件

出现以下任一情况立即停止，不增加兼容或恢复层：

- 当前失败 run 的 candidate、worktree、branch、HEAD、清洁状态或 recoveryReason 与本设计记录不符；
- 修复需要改变通用空白检查器的缺失路径失败语义；
- 人工关闭需要接纳、重放或集成 runtime 未记录的 formal commit；
- 关闭失败 run 或新 run 失败后 `master` 发生变化；
- 主工作区正式路径与已有人工改动冲突；
- 新 run 再次出现组合验证、路径、后置条件、通知前业务状态或清理证据异常；
- 需要新增第二 runtime、通用重试、自动冲突解决或事故恢复入口。

## 9. 提交与回滚边界

修复代码、回归测试和关闭合同形成路径限定提交，并在最新 `master` 上重新验证后通过统一锁 fast-forward。实时 automation 在真实验证完成前保持旧 prompt 和 `PAUSED`。

若代码测试或新 Codex 真实 run 失败，保留 schema 5 runtime 与全部 worktree 证据，不启用 automation、不删除旧入口、不继续迁移。回滚只撤销本修复提交，不改写已经集成的 DeepSeek 业务提交或用户工作区修改。
