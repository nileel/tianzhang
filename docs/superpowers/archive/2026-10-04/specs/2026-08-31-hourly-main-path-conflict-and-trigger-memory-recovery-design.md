# 小时自动化主工作区路径冲突与触发器 memory 恢复设计

> 日期：2026-08-31
> 状态：已按书面审核 P1–P5 修订并获批准实施
> 恢复对象：DeepSeek run `e545f77f-3f62-403b-b1f8-d73630d62e07`
> 关联任务：`C-HS-YY-JD-01I`
> 设计原则：保留人工事实、关闭旧 run、从最新 `master` 新建 run；不恢复旧模型会话，不增加重试层或第二套 runtime

## 1. 背景与根因

DeepSeek run `e545f77f-3f62-403b-b1f8-d73630d62e07` 已在隔离 worktree 中形成单一候选提交 `ce5049e475c8a7e8b39cdabef19e8409cccccf56`，候选父提交为 claim 时冻结的 `fa4c5f6d4ac524c9a674181017fbe96a3a0d9c0f`，候选工作树干净。正式集成前，共享入口发现主工作区已有对 `开发管理/AI合作沟通.txt` 的未提交修改，因此按 `hourly_main_path_conflict` 停止并把 run 置为 `attention_required`。

该冲突不是候选内容错误。DeepSeek 正式结果必须把任务转为 `codex_review/codex/ready` 并向 `开发管理/AI合作沟通.txt` 写入交接；该路径由 `Get-FormalPaths` 固定加入正式路径集合。现有主工作区修改早于本 run，内容是把 `M-EXP-TASK-SCHEMA2-01` 的两条失效交接移入新归档，负责人已明确要求全部保留。

故障后 `master` 已继续推进，并多次修改 `开发管理/当前任务队列.txt`。队列也是本任务的正式路径，因此旧 run 不再适合在原 base 上直接续跑或人工伪装成正常自动继续。正确恢复边界是：先把人工归档事实独立集成，再按 candidate-evidence 合同关闭旧 run，最后从当时最新 `master` 创建全新 run。

另有独立触发层问题：两个小时触发器曾把 automation memory 写到仓库相对路径，形成未跟踪的 `$CODEX_HOME/automations/.../memory.md` 和 `null/automations/.../memory.md`；DeepSeek 实际 memory 还记录了“共享入口已完成但 memory 写入失败，原始终态未返回”。memory 不参与选题、runtime 或恢复，因此本设计移除触发器对 memory 文件的读写责任，只保留共享入口终态和 automation 运行历史。

当前 canonical prompt、`开发管理/自动工作流规则.txt`、`tools/check-automation-workflow.ps1` 和 `tools/test-check-automation-workflow.ps1` 都把旧 memory 责任作为正向合同。只改实时 automation prompt 会导致 canonical prompt 精确匹配和静态检查必然失败。因此 memory 修复必须同时更新 canonical prompt、共享规则、检查器及其测试；这是合同同步，不是兼容分支或绕过检查。

## 2. 已批准决策与范围

1. 保留现有 `开发管理/AI合作沟通.txt` 删除内容和 `开发管理/AI合作归档/2026-08-30-M-EXP-TASK-SCHEMA2-01-复审归档.txt` 新文件，并把两者形成一个独立、路径限定的管理提交。
2. 不把旧候选人工提升为正式结果；旧候选只作为精确关闭 `attention_required` run 的证据保留。
3. 关闭旧 run 后只手动调用一次 DeepSeek `RunOnce`，由共享内核从最新 `master` 重新 claim、生成候选、形成正式提交、验证、集成、通知和清理。
4. 同步修正两个 canonical trigger prompt、实时 automation prompt、共享规则、工作流检查器及其测试：触发器不得读写任何 `memory.md`，不得解析 `$CODEX_HOME` 或自行创建 memory 路径；检查器正向要求新禁止合同并反向拒绝旧写入义务。模型、schedule、reasoning effort、notification policy、execution environment 和名称保持不变。
5. workflow-contract 提交的授权路径固定为：`开发管理/自动工作流控制器提示词.txt`、`开发管理/DeepSeek小时触发提示词.txt`、`开发管理/自动工作流规则.txt`、`tools/check-automation-workflow.ps1`、`tools/test-check-automation-workflow.ps1` 和本设计文档。不得借机修改共享入口、runtime 或其他工具。
6. 删除仓库内两个错误 memory 文件及确认为空的精确父目录；不使用 `git clean`、递归通配删除或宽泛 cleanup。
7. 不清理其他历史 automation worktree／branch。本次只保留旧 run 现场并证明新 run 按正常合同清理。
8. 不修改 `.agents/summary_state.json`、`设计总结.txt` 或其他既有无关改动，不运行 Unity 或 BattleSim。

## 3. 执行前冻结与所有权

当前实时配置已核验两个小时入口均为 `PAUSED`。恢复开始时再次通过 automation view 确认该状态；若任一入口已恢复 `ACTIVE`，先通过 automation 管理接口暂停并重新读取 runtime／master 后再继续。暂停只冻结新定时触发，不能修改或关闭 schema 5 runtime。保存两个 automation 的完整现有配置；本次修复的批准成功终态是在全部验证通过后把两个入口设为 `ACTIVE`，不是把 `PAUSED` 误记为旧活动状态。

随后重新读取：

- `git status --short --branch`；
- schema 5 `Show`，显式传入仓库根；
- `开发管理/当前任务队列.txt`；
- `开发管理/任务卡/C-HS-YY-JD-01I.txt`；
- 旧 run worktree、branch、HEAD、父链、状态和候选路径；
- 进程持有型集成锁。

若旧 run、候选 SHA、恢复原因、任务卡 route／owner、候选 worktree 或现有人工归档内容与本设计不一致，停止且不写 runtime、不删除文件。

## 4. 阶段 A：保留并集成人工归档事实

从执行时最新 `master` 创建独立 manual recovery worktree。只把主工作区以下两个路径的当前内容复制到该 worktree：

- `开发管理/AI合作沟通.txt`；
- `开发管理/AI合作归档/2026-08-30-M-EXP-TASK-SCHEMA2-01-复审归档.txt`。

复制前记录两个文件的字节数和 SHA-256；复制后再次核对完全相等。运行：

- `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-pending-whitespace.ps1`，范围只含上述两个路径；
- `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-review-text.ps1 -Paths 开发管理`；
- `git diff --check`；
- 暂存后 `git diff --cached --check`。

提交只能包含上述两个路径。提交形成后，比较 recovery worktree 的目标 blob 与主工作区当前文件，必须逐字节相等。只有证据已由提交保存，才允许在主工作区精确撤去这两个未提交路径，使主工作区回到其原 HEAD 对这两个路径的内容；其他 dirty／untracked 路径保持原样。

重新调用 schema 5 `Show` 并确认集成锁空闲，然后通过 `tools/invoke-project-integration.ps1` 获取共享锁并 fast-forward 该管理提交。调用参数固定按以下证据推导：

- `-RepositoryRoot`：主工作区绝对路径 `D:\天章游戏开发`；
- `-ExpectedMainHead`：创建 recovery worktree 时记录、且清除两个主工作区重叠路径后再次核验未变化的 `master` HEAD；
- `-TargetCommit`：recovery worktree 中仅含两个授权路径的管理提交完整 SHA；
- `-ExpectedPaths`：`开发管理/AI合作沟通.txt|开发管理/AI合作归档/2026-08-30-M-EXP-TASK-SCHEMA2-01-复审归档.txt`；
- `-LockTimeoutSeconds 0`：锁非空闲时立即停止，不等待或重试。

脚本必须返回 `status=integrated`，且 `previousHead`／`head` 分别等于上述 `ExpectedMainHead`／`TargetCommit`。若清除主工作区重叠路径后集成失败，立即从 recovery worktree 提交恢复这两个路径的工作区内容并停止；不得丢失已批准的归档事实。

## 5. 阶段 B：精确关闭旧 attention run

管理提交进入 `master` 后，再次核验旧 run：

- owner：`deepseek`；
- runId：`e545f77f-3f62-403b-b1f8-d73630d62e07`；
- state：`attention_required`；
- recoveryReason：`formal integration stopped: hourly_main_path_conflict`；
- candidateCommit：`ce5049e475c8a7e8b39cdabef19e8409cccccf56`；
- candidate branch：`codex/automation/deepseek/e545f77f-3f62-403b-b1f8-d73630d62e07/candidate`；
- worktree HEAD 等于 candidate，工作树干净；
- baseCommit 等于 `fa4c5f6d4ac524c9a674181017fbe96a3a0d9c0f`；
- candidate 唯一父提交等于旧 base；
- runtime 冻结的 taskCardDigest 等于 `d32fbd1f37ecb9d782398442cb1f4cf5ed1bbbfbc4856b4a28208bc50b16bde9`；使用共享入口同口径的 UTF-8、去 BOM、统一 LF 算法分别计算旧 base、当前 `master` 和主工作区任务卡摘要并记录。当前已复核三者均等于冻结值；不得用受 CRLF 影响的原始文件字节 SHA-256 替代任务卡 normalized digest。执行时任一 normalized digest 改变都停止关闭；
- candidateResult 与实际五个 changed paths 精确一致；
- canonical branch／base／head 仍为空；
- candidate 不在 `master` 可达链上。

满足全部条件后，调用 schema 5 `CompleteRun`，使用 `CompletionCategory=failed` 和固定 `DetailCode=hourly_main_path_conflict`，同时传入完整 `ExpectedRecoveryReason`、`ExpectedCandidateCommit`、`ExpectedWorktree`、`ExpectedWorktreeBranch` 和 `ExpectedWorktreeHead`。预期结果为 `RUN_COMPLETED`、回显同一 detailCode 且 `evidenceRetained=true`。

关闭旧 run 后：

- 不发送成功通知；
- 不删除旧 candidate worktree、branch 或提交；
- 不修改任务卡或队列；
- 证明 `runs.deepseek=null` 且任务仍为 `external_execute/deepseek/ready`。

## 6. 阶段 C：从最新 master 执行一个全新 DeepSeek run

保持两个 schedule 暂停，前台调用一次：

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/invoke-hourly-owner.ps1 `
  -Owner deepseek `
  -Action RunOnce `
  -RepositoryRoot "D:\天章游戏开发" `
  -OutputJson
```

只等待这一个前台进程，不启动第二次调用、不后台化、不在空输出或 yield 时重启。共享入口必须从最新 `master` 重新 claim `C-HS-YY-JD-01I` 并创建新的 runId、worktree、candidate branch 和模型会话。

成功终态必须同时证明：

- 结构化 status 为 `completed`；
- 新 formal SHA 已进入 `master`；
- 任务卡转为 `codex_review/codex/ready`；
- 当前队列包含该复审任务且不重复；
- `开发管理/AI合作沟通.txt` 保留阶段 A 的归档结果，并新增且只新增本次新 run 的交接；
- 新 run 已 `CompleteRun`；
- `runs.deepseek=null`；
- 成功通知使用新 runId 和 formal SHA；
- 新 run 现场按正常精确身份合同清理。

若新调用返回 `attention_required`、失败、决定等待或其他非成功终态，立即停止。保留新现场，禁止重试、关闭、修补或切换候选；两个小时 schedule 都继续保持暂停，等待新的人工诊断。

## 7. 阶段 D：同步仓库合同并修正触发器 memory 合同

新 DeepSeek run 成功关闭后，从当时最新 `master` 创建独立 workflow-contract worktree。只修改第 2 节授权的六个仓库路径，并把已书面批准的本设计文档带入该提交。

两个 canonical prompt 统一冻结以下边界：

1. 触发器唯一项目动作仍是一次前台共享入口调用和等待；
2. 不读取、创建、更新或删除任何 `memory.md`；
3. 不使用 `$CODEX_HOME`、`CODEX_HOME`、`null`、相对目录或 shell 环境变量推导 memory 路径；
4. automation memory 不参与项目事实、runtime、选题、恢复或结果判断；
5. 本轮脱敏终态由 automation 运行历史和最终原样 JSON 承载，不要求模型另写 memory；
6. 共享入口 JSON 解析完成后，先原样输出 `terminalText`，再输出恰好一个 `::inbox-item`，不得在其间执行文件写入。

`开发管理/自动工作流规则.txt` 的实时入口条款同步为：触发器不读写 automation memory；脱敏终态由 automation 运行历史和原始 JSON 承载；memory 不参与项目事实、选题、runtime 或恢复。不得保留“触发器应记录本轮摘要”的隐含义务。

`tools/check-automation-workflow.ps1` 使用既有 `Assert-DoesNotContain`，完成两类合同同步：

- `Assert-Contains` 对两个 canonical prompt 正向要求“不得读取、创建、更新或删除任何 `memory.md`”“运行历史和原始 JSON 承载终态”等新稳定标记；
- `Assert-DoesNotContain` 精确拒绝旧写入义务句，例如“读取并在结束时更新本 automation 的 memory”“按 Desktop automation memory 合同读取并在结束时更新”“memory 只记录本轮时间”。不得笼统禁止单词 `memory`，因为新禁止合同本身仍需使用该词。
- 对 `开发管理/自动工作流规则.txt` 同样正向要求“触发器不读写 automation memory”和“运行历史与原始 JSON 承载”，并反向拒绝旧句“automation memory 只记录本轮时间与脚本终态摘要”，防止规则文件重新引入已移除责任。

`tools/test-check-automation-workflow.ps1` 同步修改 canonical prompt soft-contract 断言，并至少增加／保留以下证明：

- 新 canonical prompt 和临时 automation 配置在 `PAUSED`、`ACTIVE` 两种状态下通过；
- 缺少新禁止合同的 prompt 被拒绝；
- 注入任一旧 memory 写入义务的 prompt 被拒绝；
- 任意实时 prompt 与 canonical prompt 不一致仍被拒绝。

在 workflow-contract worktree 先运行 `test-check-automation-workflow.ps1`；该测试使用 canonical prompt 生成临时 automation 配置，不依赖尚未更新的真实配置。随后对六个授权路径运行 whitespace、PowerShell parse、`git diff --check` 和 cached diff，形成单一 workflow-contract 提交。

集成该提交时再次使用 `tools/invoke-project-integration.ps1`，冻结 `ExpectedMainHead` 为创建 worktree 时的最新 `master`、`TargetCommit` 为合同提交 SHA、`ExpectedPaths` 为六个授权路径的管道串、`LockTimeoutSeconds=0`。任何主分支推进或路径冲突都停止，不重放或自动重试。

仓库合同提交进入 `master` 后，只通过 Codex automation 管理接口更新两个已存在的 automation，不直接编辑 TOML。实时 prompt 必须分别精确等于更新后的 canonical prompt；更新时保留除 prompt 和暂时 status 之外的全部字段。以只读 view 核对两个 automation 仍使用原名称、schedule、model、reasoning effort、notification policy 和 local project，并继续保持 `PAUSED`，待第 8 节全部通过后恢复 `ACTIVE`。

## 8. 错误 memory 文件清理与验证

删除前精确证明仓库内只有以下两个错误文件，且它们不是 Git 跟踪文件、任务事实、runtime 或 provider 凭据：

- `$CODEX_HOME/automations/deepseek-hourly-trigger/memory.md`；
- `null/automations/codex-hourly-worker/memory.md`。

记录路径、字节数、SHA-256 和文本内容用途后，用精确字面量路径删除文件；仅当逐级目录为空时删除对应空目录。不得递归删除 `$CODEX_HOME`、`null` 或任何解析后的环境目录，不得调用 `git clean`。

最终验证：

1. `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-check-automation-workflow.ps1`，证明新正向／反向 memory 合同和临时 automation 配置；
2. 实时 prompt 更新后运行 `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-automation-workflow.ps1`，证明仓库 canonical prompt、共享规则、检查器与两个真实 automation 配置一致；
3. schema 5 `Show`：两个 owner run 均为空、`activeTaskIds=[]`、`integrationLockStatus=none`；若新正式结果按设计进入复审队列，仍不得存在 owner run；
4. `git status --short`：阶段 A 两个路径不再 dirty；`.agents/summary_state.json`、`设计总结.txt` 等原有无关修改原样保留；两个错误 memory 目录消失；
5. `git log` 与任务检查器证明管理提交和新 DeepSeek formal 都在 `master`，任务状态为 `codex_review/codex/ready`；
6. automation view 证明两个小时入口 prompt 已更新且其余配置未漂移；
7. 将两个 automation 设为批准的成功终态 `ACTIVE`，再 view 一次确认状态。

不额外手动触发第二个业务 canary。新 DeepSeek RunOnce 已覆盖真实触发器下游共享入口；prompt 层以配置读取和下一自然轮次观测为验证，避免再次执行业务。

## 9. 停止、回滚与非目标

- 阶段 A 管理提交未成功进入 `master`：不关闭旧 run；恢复主工作区两个目标路径并停止。
- 旧 run 证据任一不一致：不调用 `CompleteRun`，保留现场并停止。
- 旧 run 已关闭而新 run 失败：不重建旧 runtime、不复用旧 runId；保留新 run 现场并按新的 state 进入人工处置。
- automation prompt 更新失败或配置字段漂移：两个小时入口保持 `PAUSED`，不尝试直接编辑 TOML。
- 错误 memory 目录包含预期之外文件：不删除，报告精确差异。
- 本设计只授权同步修改 `tools/check-automation-workflow.ps1` 与 `tools/test-check-automation-workflow.ps1` 的 memory 合同断言。若开始需要修改 `tools/invoke-hourly-owner.ps1`、schema 5 状态结构、任务卡 schema、恢复规则、其他 tools 脚本，或增加重试／兼容分支，视为突破本设计边界并立即停止。

本设计明确不处理其他历史 automation worktree、branch、旧候选或脏现场；这些对象可能是失败证据、决策 checkpoint 或待核验残留，必须另做逐项 cleanup proof。
