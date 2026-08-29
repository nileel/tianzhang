# 小时自动化 Unity `.meta` 差异校验与失败 run 完整收口设计

日期：2026-08-29
状态：设计已批准并进入实施；最终完成证据写入 `开发管理/自动工作流状态.txt`
范围：Codex run `d9188688-487f-48fd-a0ec-a94bf5dcc581` 的人工恢复，以及共享入口中三处正式范围差异检查的同口径修复

## 背景与已证实根因

实施前 Codex run 领取 `U-CHAR-BATTLE-2D-EXPERIMENT-ADAPTER-01`，冻结 base 为 `786cf0b3ee3c54d2521823648dafcdd1a0b159cf`，candidate 为 `1b94a5714ba8f5d71794371768e2f06c002422f7`。共享入口已经在 run worktree 中形成正式提交 `53e5ffb551018ff0dc6dafded8fb70cba5a1f533`；该提交当时仍是 `master` 的直接后继，worktree 干净，尚未集成。

共享入口的直接停止原因不是业务代码或 Unity 测试失败，而是正式范围 whitespace 口径不一致。`tools/check-pending-whitespace.ps1` 明确只豁免下列三种 Unity `.meta` 合法空值行：

- `  userData: `
- `  assetBundleName: `
- `  assetBundleVariant: `

`tools/automation-finalize-commit.ps1` 随后以 `-c core.whitespace=-blank-at-eol` 运行暂存差异检查，因此 candidate 和 formal 提交均可保留上述 Unity 字节。但 `tools/invoke-hourly-owner.ps1` 的正式结果范围检查仍用默认 `git diff --check 786cf0b3..53e5ffb5`，把两个新增 `.meta` 文件中的六个合法空值行报告为 trailing whitespace，最终以 `hourly_diff_check_failed` 进入 `attention_required`。

同一文件另有维护型决策与复审返工的两处范围检查使用相同默认配置。它们尚未触发本次事故，但违反同一专用 whitespace 合同，必须在同一最小修复中统一；不新增 helper、runtime 状态或兼容分支。

候选会话原始 JSONL 进一步证明，最终提交说明中的 Unity 验证不能整体复用：EditMode 通过后 adapter 与 PlayMode 测试代码又发生修改；最后一次 PlayMode 通过后实验场景字节又发生修改。原始会话中最后一次 EditMode、PlayMode、程序集边界和数据链通过均早于至少一项相关输入变化。因此人工恢复必须在 formal 提交上重跑任务卡列出的完整相关验证，不能把提交消息或 candidateResult 的 `verified` 数组当作最终证据。

schema 5 `Show` 的权威接口是 `tools/hourly-automation-lease.ps1 -Action Show`；本机诊断文件当前位于 `C:\Users\WINDOWS\.codex\automation-state\tzg-hourly-controller-runtime\runtime.json`。物理文件只作路径、ACL 与时间戳诊断，不直接编辑，所有读取和关闭都通过 runtime 接口完成。独立审核时两个实时 automation 配置均为 `ACTIVE`；实施开始时仍须重新读取并保存实际状态，不能依赖这一本次快照。

上一小时触发层曾在没有发出 Node REPL 或 PowerShell 调用的情况下提前报告 Node REPL 不可用；相同配置的下一轮已经成功调用。该事件没有可复现的持久配置或工具错误，本设计不为它增加重试、备用身份来源或新的触发状态。

## 目标

1. 在不重做业务实现的前提下，把已验证的正式提交 `53e5ffb5` 安全 fast-forward 到 `master`。
2. 在不伪造 runtime 状态回退的前提下精确关闭 schema 5 run，发送一次人工恢复成功通知，并只在完整身份与可达性证明成立后清理该 run 的 worktree 和临时分支。
3. 让共享入口所有正式范围 `git diff --check` 与既有 Unity `.meta` 专用语义保持一致。
4. 通过现有测试体系锁定三处调用口径，防止后续只修 finalizer、遗漏外层范围检查。
5. 修复期间保持两个写入 automation 暂停，完成代码验证与两个 owner canary 后恢复各自原状态。

## 非目标

- 不修改 2D 实验 adapter、场景、测试或任务结果。
- 不修改 `.gitattributes`、系统／仓库／worktree Git 配置。
- 不扩大 `.meta` 豁免；除三种精确空值行外，普通尾随空格和相似错误形态继续失败。
- 不删除外层 Git whitespace 检查，不解析或过滤 Git 错误文本。
- 不恢复旧模型会话，不创建第二 runtime、恢复队列、后台重试或自动冲突解决。
- 不修改 Codex automation prompt 来处理单次 Node REPL 提前退出。

## 决策

### 1. 先恢复现有 run，再修改共享入口

人工恢复开始时 `master` 仍等于 run base，正式提交是其直接后继。先恢复该提交可以避免为了基础设施修复而重放或改写已经形成的业务 formal commit。实施第一步重新读取两个实时 automation 配置，保存各自精确状态后暂停两者；整个恢复、基础设施修复、canary 和恢复记录集成期间保持暂停，结束时逐项恢复保存的原状态。

人工恢复必须依次证明：

1. 通过 `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/hourly-automation-lease.ps1 -Action Show -RepositoryRoot 'D:\天章游戏开发'` 读取 schema 5 权威状态；owner、runId、taskId、repository、base、candidate、worktree 和 `attention_required/hourly_diff_check_failed` 必须与现状一致，DeepSeek 不占用同一 taskId，集成锁空闲。必要时只读核对用户级 `runtime.json` 的路径、ACL 与时间戳，不直接解析后写回。
2. run worktree 注册路径、当前 branch、HEAD `53e5ffb5`、父提交 `786cf0b3`、candidate `1b94a571`、工作树清洁状态和提交 Automation 元数据全部精确匹配。
3. candidate 与 formal 的业务路径集合相同；formal 相对 base 的变化不超出 run 的冻结 `changedPaths`，主工作区 staged、unstaged、untracked 路径与 formal 路径不冲突。
4. `check-pending-whitespace.ps1` 对 formal 的现存变化通过；`git -c core.whitespace=-blank-at-eol diff --check 786cf0b3..53e5ffb5` 通过；默认 `git diff --check 786cf0b3..53e5ffb5` 只命中已确认的六个合法 `.meta` 空值行，不得包含其他错误。
5. 任务卡后置投影、队列、归档和验证记录与 candidateResult 一致。候选会话原始 JSONL 只能证明各命令曾在某个中间版本运行；由于相关输入随后变化，必须在 formal `53e5ffb5` 上重新运行 `check-unity-assembly-boundaries.ps1`、EditMode、PlayMode、`check-task-cards.ps1 -TaskId U-CHAR-BATTLE-2D-EXPERIMENT-ADAPTER-01 -Postcondition CodexClosedOrNonReady`、`check-review-text.ps1`、`check-data-chain.ps1` 与冻结路径 `check-pending-whitespace.ps1`，并再次确认 worktree 干净、HEAD 未变。

当前 run 已是 `attention_required`，而 schema 5 明确禁止从该状态转回 `canonical_ready` 或 `integrated`；人工恢复不得直接编辑 runtime 或伪造这两个转换。证据成立后，通过 `tools/invoke-project-integration.ps1` 获取共享进程持有型锁，以精确 base、formal SHA 和冻结路径集合 fast-forward。集成后核验 `master` 可达 formal、任务后置状态正确、run worktree 仍干净，再对现有 `attention_required` run 调用 schema 5 `CompleteRun`：`CompletionCategory=failed`，并传入精确的 `ExpectedRecoveryReason`、`ExpectedCandidateCommit`、`ExpectedWorktree`、`ExpectedWorktreeBranch` 和 `ExpectedWorktreeHead`。这使用现有 candidate-attention 保留证据合同关闭原失败 run，不把人工集成伪装成正常自动状态流转。

只有 `CompleteRun` 返回 `RUN_COMPLETED` 且再次 `Show` 确认 Codex run 为空后，才使用原 taskId／runId 与已进入 `master` 的 formal SHA 发送一次 `completed` TaskOutcome。`PROVIDER_ACCEPTED` 记为已投递；其他脱敏结果只记录，不回滚 Git、不重开 run、不自动重试。

清理前再次证明 runtime 不再引用 worktree、formal 已由 `master` 可达、worktree 干净、branch 与 SHA 精确匹配且没有关联进程。随后只删除该 run 的 linked worktree、candidate branch、canonical branch 和变空的 run 目录；任一证明失败则保留现场并报告，不泛化清理其他历史 worktree。

run 恢复并清理完成后，当前设计 worktree 必须先在保留未提交设计文档的前提下 fast-forward 到新的 `master`，确认没有路径冲突，再开始共享入口代码修改；不得让基础设施提交继续建立在恢复前的旧 base 上。

### 2. 统一共享入口三处范围检查

在独立手动 worktree 中只修改：

- `tools/invoke-hourly-owner.ps1`
- `tools/test-hourly-finalizer-invocation.ps1`
- 本设计文档

人工恢复与基础设施 canary 完成后，还要在同一隔离 worktree 的后续路径限定提交中修改：

- `开发管理/自动工作流状态.txt`

该记录只写长期事实：原 run／task、formal SHA、人工集成与 `CompleteRun` 关闭方式、通知结果、基础设施修复提交、验证与两个 owner canary。实时 automation 配置和普通逐轮轨迹不写入该文件。

`tools/invoke-hourly-owner.ps1` 中以下三条现有范围检查统一从默认参数形态：

```powershell
@('diff', '--check', "$Base..$Head")
@('diff', '--check', "$latest..$formalHead")
```

改为仅对对应 Git 子进程传入：

```powershell
@('-c', 'core.whitespace=-blank-at-eol', 'diff', '--check', "$Base..$Head")
@('-c', 'core.whitespace=-blank-at-eol', 'diff', '--check', "$latest..$formalHead")
```

第二种 `$latest..$formalHead` 形态分别用于维护型决策与复审返工，共计三处范围检查。

覆盖范围为普通 formal 校验、维护型决策 formal 校验和复审返工 formal 校验。既有 `check-pending-whitespace.ps1` 仍在这些检查之前执行，继续承担精确 `.meta` 语义白名单和普通尾随空格拒绝；`automation-finalize-commit.ps1` 不再修改。

不引入新函数或脚本，因为三个调用只是同一现有命令的参数修正。这样既避免删除 Git 的其他 whitespace 检查，也不会产生持久 Git 配置副作用。

### 3. 回归测试

扩展 `tools/test-hourly-finalizer-invocation.ps1` 的现有 AST／源合同检查：

1. 找到 `tools/invoke-hourly-owner.ps1` 内全部三个 `diff --check` 范围调用。
2. 断言每个调用都包含 `-c` 与 `core.whitespace=-blank-at-eol`，且不再存在默认配置的正式范围调用。
3. 断言调用数量仍为三个，防止新增路径绕开统一合同。
4. 断言三条路径各自仍保留先行精确 whitespace 检查：普通 formal 与复审返工为显式 `check-pending-whitespace.ps1`，维护型决策为进入范围检查前已成功返回的 finalizer。配置型 Git 检查不能单独成为 `.meta` 白名单。

所有 PowerShell 测试在 PowerShell 7 的 UTF-8 控制台中执行，先设置代码页与当前进程编码，避免中文路径断言受 GBK／OEM 代码页影响：

```powershell
chcp 65001 > $null
$utf8NoBom = [Text.UTF8Encoding]::new($false)
[Console]::InputEncoding = $utf8NoBom
[Console]::OutputEncoding = $utf8NoBom
$OutputEncoding = $utf8NoBom
```

继续运行现有动态测试以证明允许边界没有扩大：

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-check-pending-whitespace.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-automation-finalize-commit.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-hourly-finalizer-invocation.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-check-automation-workflow.ps1
```

共享基础设施修改还必须运行：

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-pending-whitespace.ps1 -ExpectedPaths 'tools/invoke-hourly-owner.ps1|tools/test-hourly-finalizer-invocation.ps1|docs/superpowers/specs/2026-08-29-hourly-semantic-meta-diff-and-run-recovery-design.md'
git diff --cached --check
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-data-chain.ps1
```

提交和合并使用路径限定提交及 `tools/invoke-project-integration.ps1` 的共享锁。合并后分别运行 Codex 与 DeepSeek `Canary`；两个 canary 必须证明私有 runtime、worktree 隔离、owner adapter 和清理均通过，且主工作区 HEAD／状态只发生预期正式提交变化。

canary 通过后，在同一隔离 worktree 基于已集成的基础设施提交新增第二个路径限定提交，只修改 `开发管理/自动工作流状态.txt`。该提交运行：

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-review-text.ps1 -Paths AGENTS.md,CLAUDE.md,开发管理
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-pending-whitespace.ps1 -ExpectedPaths '开发管理/自动工作流状态.txt'
git diff --cached --check
```

状态记录提交也通过 `tools/invoke-project-integration.ps1` 的共享锁 fast-forward。确认主工作区只保留用户原有 dirty paths 后，逐项恢复两个 automation 的已保存原状态；恢复前若实时配置被外部改动则停止并报告，不覆盖外部变化。

## 错误处理与停止条件

出现以下任一情况立即停止，保持 automation 暂停并保留相关现场：

- `master` 不再等于预期 base，或 formal 不再是其直接后继。
- runtime、branch、SHA、路径、任务卡 digest、工作树清洁状态或进程证据不一致。
- formal 上重跑的程序集边界、EditMode、PlayMode、任务投影、审核文本、数据链或精确 whitespace 任一失败。
- 默认 `git diff --check` 除六个已知合法 `.meta` 空值行外还报告其他问题。
- 主工作区人工改动与 formal 或基础设施修复路径冲突。
- 任务后置投影、提交元数据、验证证据或正式路径集合不成立。
- 集成锁无法取得、fast-forward 被拒绝、canary 失败或自动化实时配置发生外部变化。

停止时不重置、stash、rebase、checkout、clean、删除现场、自动重试或引入兼容路径；报告精确阶段和证据差异，等待新的人工决定。

## 完成条件

- `53e5ffb5` 已通过共享锁 fast-forward 到 `master`，任务处于正确完成／归档状态。
- run `d9188688-487f-48fd-a0ec-a94bf5dcc581` 已按 attention candidate-evidence 合同精确关闭；未伪造 `canonical_ready/integrated` 转换；成功通知最多投递一次，关联 worktree／临时 branch 只在清理证明成立后移除。
- 三处共享入口正式范围检查均使用同一临时 `core.whitespace=-blank-at-eol` 参数；没有持久 Git 配置变化。
- formal 的程序集边界、EditMode、PlayMode、任务投影、审核文本、数据链与精确 whitespace 已在最终输入上重跑通过。
- 四个基础设施定向测试、路径 whitespace、暂存差异检查、数据链和两个 owner canary 全部通过。
- `开发管理/自动工作流状态.txt` 已记录本次长期恢复事实、formal／修复提交、通知结果与 canary 证据。
- Codex 与 DeepSeek automation 恢复到修复前状态。
- 主工作区原有 `.agents/summary_state.json`、`设计总结.txt` 与 `null/` 改动未被修改、暂存或提交。
