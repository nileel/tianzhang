# 小时自动化 Finalizer 进程内调用修复设计

> 日期：2026-08-10
>
> 状态：设计与修订版均已确认；实现和验证结果随本次基础设施提交交付。

## 一、问题与已核验证据

自动化责任方完成 candidate 后，`tools/invoke-hourly-owner.ps1` 进入正式阶段：

1. `RunOnce` 调用 `Build-And-IntegrateCandidate`；
2. 在集成锁内基于最新 `master` 创建 canonical branch；
3. 通过 `cherry-pick --no-commit` 重放 candidate；
4. `Get-FormalPaths` 取得任务完整 `expectedPaths`；
5. `Invoke-Finalizer` 把路径以 `|` 拼接，并通过嵌套 `pwsh -File` 调用 `automation-finalize-commit.ps1`；
6. 正式提交通过组合验证后，在同一锁内 fast-forward 到 `master`。

根因位于第 5 步的进程边界，而不在 finalizer 的路径校验或 Git 集成逻辑：

- 已归档任务 `U-ARCH-REBUILD-01C` 有 648 条 `expectedPaths`，拼接字符串长度为 37,344 个字符；把它作为 Windows 子进程命令行参数传递超过常见命令行上限，可能抛出“文件名或扩展名太长”；
- 当前环境还在该嵌套调用期间实际出现过 `StandardOutputEncoding is only supported when standard output is redirected`。仅凭脚本源代码不足以单独定位到具体宿主捕获或 `ProcessStartInfo` 设置，因此本文把它记为移除该进程边界会一并消除的附加风险，不把它作为唯一确定根因；
- `Invoke-Finalizer` 同时被正常 candidate 正式化、任务状态转换和审核返工重新排队复用，所以问题并非单个业务任务特例；
- 自动化正式 fast-forward 直接在 `invoke-hourly-owner.ps1` 中持有现有集成锁执行，并不调用 `invoke-project-integration.ps1`。后者只用于手动正式集成及其测试。

2026-08-10 设计阶段实时核验：`master=2f6e609ab0e183467a54e01d50653b3f544e0b08`，schema 5 的两个 owner run 均为空，集成锁空闲，`codex-hourly-worker=PAUSED`；主工作区存在与本次无关的用户改动。

## 二、目标与非目标

### 目标

1. 让自动化实际调用链在当前 PowerShell 进程中执行现有 finalizer，消除嵌套 `pwsh` 的输出编码和 Windows 命令行长度风险。
2. 保持 `expectedPaths` 的规范化、授权、外部索引隔离、暂存、提交和差异校验不变。
3. 保持集成锁、canonical candidate 验证、失败状态记录和 fast-forward 语义不变。
4. 为 648 条以上路径、嵌套 PowerShell 回归、失败 detailCode、严格路径隔离和集成锁增加或复用可重复验证。
5. 全程保持 `codex-hourly-worker` 为 `PAUSED`，不改变默认 50 分钟超时机制。

### 非目标

- 不修改 `tools/invoke-project-integration.ps1`，不增加临时路径文件接口。
- 不修改 `tools/automation-finalize-commit.ps1` 的参数或路径合同。
- 不增加重试、兼容分支、runtime 字段、工作流状态或自动冲突处理。
- 不恢复、重跑或修改已经完成的 `U-ARCH-REBUILD-01C`。
- 不处理或提交主工作区中与本次无关的用户改动。

## 三、方案比较与选择

### 方案 A：调用运算符进程内执行（采用）

`Invoke-Finalizer` 使用 `& $finalizerPath` 在当前 PowerShell 进程的子作用域运行现有脚本。`expectedPaths` 仍是同一内存字符串，但不再序列化为 Windows 子进程命令行。

优点是改动只落在实际根因函数；现有 finalizer、调用点和集成流程不变。子作用域也避免 finalizer 的函数与脚本变量污染 owner 作用域。

### 方案 B：点源 finalizer（不采用）

点源同样能避开命令行上限，但会把函数、脚本变量和偏好设置引入 owner 作用域，扩大隐藏耦合和后续回归风险。

### 方案 C：提取共享模块函数（不采用）

把 finalizer 重构为模块函数可以提供显式类型接口，但需要同步修改独立入口、调用方和测试，超出消除当前进程边界所需的最小范围。

## 四、详细设计

### 4.1 `Invoke-Finalizer`

只修改 `tools/invoke-hourly-owner.ps1` 中完整的 `Invoke-Finalizer` 逻辑单元：

1. 保留 `Worktree`；把 CLI token 形式的 `string[] Arguments` 改为命名参数 `hashtable Parameters`。PowerShell 的数组 splatting 只按位置传参，不会把数组元素中的 `-ExpectedPaths` 等字符串重新解析为脚本命名参数，因此不能原样复用子进程 CLI token。
2. 三个现有调用点机械改为同字段 hashtable，不改变任何参数值；使用 `& $finalizerPath -RepositoryRoot $Worktree @Parameters` 在当前进程执行。
3. 保留 stdout/stderr 捕获，以最后一项输出作为正式提交 SHA；调用返回后立即保存 `$?`，不让后续命令覆盖脚本调用状态。
4. 用 `try/catch` 捕获 finalizer 的终止错误，并继续统一映射为 `hourly_formal_commit_failed`。
5. 成功判断要求 finalizer 未抛错、调用状态 `$?` 为真且末行符合 40～64 位小写十六进制 SHA；不再检查 `$LASTEXITCODE`。

不能沿用 `$LASTEXITCODE` 的原因是：进程内执行时，该变量可能保留 finalizer 内部最后一次 native `git diff --quiet` 的正常差异退出码 `1`，即使 finalizer 后续已经通过 `ProcessStartInfo` 成功提交并输出 SHA。嵌套 `pwsh` 时代码检查的是子 PowerShell 进程退出码；去掉进程边界后必须以 PowerShell 异常、紧随调用捕获的 `$?` 和 finalizer 结果合同为准。

本机 PowerShell 7.6.4 语义实验进一步确认：被调用脚本内部执行 native 退出码 `1` 后再正常输出 SHA，会留下 `$LASTEXITCODE=1`，但脚本调用 `$?=true`；输出 SHA 后显式 `exit 1` 则返回 `$?=false`、`$LASTEXITCODE=1`，调用方继续执行且 `finally` 正常运行。因此 `$?` 可以拒绝显式非零退出，同时不误拒 finalizer 内部已处理的 native 差异退出码。

### 4.2 不变边界

- `Get-FormalPaths` 仍返回任务完整授权路径集合；DeepSeek 交接路径追加规则不变。
- `automation-finalize-commit.ps1` 仍负责路径规范化、安全段检查、Git 管理路径拒绝、changed subset 计算、无关 index 隔离、pending whitespace、`git diff --cached --check` 和路径限定提交。
- `Invoke-CombinedValidation` 仍重新计算正式差异并要求每条 changed path 位于 formal paths 内。
- `Build-And-IntegrateCandidate`、`Integrate-StateTransition` 和 `Apply-AnsweredReviewRework` 的锁获取、canonical 状态、失败记录、fast-forward 与清理逻辑不变。
- `Invoke-CombinedValidation` 中调用 `check-pending-whitespace.ps1` 的嵌套 `pwsh` 本轮有意不改。它接收的是正式 diff 中实际存在的 `contentCheckPaths` changed subset，而不是任务完整 `expectedPaths`；已确认的 01C 现场为 99 条、拼接长度 5,663，未命中本次 37,344 字符根因。若未来实际 changed subset 自身达到命令行上限，应以新的直接证据另行设计，不在本次增加预防性接口。
- `invoke-project-integration.ps1` 及其 `ExpectedPaths` 参数不变；本轮正式合并的少量基础设施路径可在 PowerShell 7 当前进程中调用该入口。

## 五、错误处理与状态语义

- finalizer 的显式失败合同是抛出终止错误，不得调用 `exit`；当前 `automation-finalize-commit.ps1` 没有 `exit`，所有失败路径均为 `throw`。回归测试会把“无 `exit`”固定为生产 finalizer 的静态合同。
- finalizer 抛错、显式非零退出、没有输出或输出末行不是 SHA 时，`Invoke-Finalizer` 仍调用 `Stop-Hourly 'hourly_formal_commit_failed'`。
- 正常 candidate 正式阶段仍由 `Build-And-IntegrateCandidate` 捕获该 detailCode，保留现场并把 run 置为 `attention_required`；已完成 fast-forward 的精确保护分支不变。
- 状态转换和审核返工路径继续使用各自现有外层错误处理；本次不增加重试或替代状态。
- finalizer 失败前后的 Git index 与工作树处理仍由现有 finalizer 合同和调用方现场保留规则决定。

## 六、回归测试与验证

### 6.1 新增调用边界测试

新增 `tools/test-hourly-finalizer-invocation.ps1`，职责只覆盖 owner 到 finalizer 的调用边界：

1. 通过 PowerShell AST 从生产脚本提取实际 `Invoke-Finalizer` 定义，不复制一份测试实现；同时以真实 hashtable splatting 覆盖命名参数绑定。
2. 静态断言函数体不包含 `pwsh` 命令，并通过调用运算符执行 `$finalizerPath`。
3. 使用测试 finalizer 接收 648 条、拼接长度超过 32,767 字符的路径，断言完整值在当前进程内到达。
4. 让测试 finalizer 内部执行返回 `1` 的 native 命令后再正常输出合法 SHA，断言结果被接受；这精确覆盖 `$LASTEXITCODE` 被 callee 内部命令污染而 `$?` 仍为真的真实语义。
5. 静态断言生产 finalizer 不含 `ExitStatementAst`；让测试 finalizer 输出合法 SHA 后 `exit 1`，断言仍被拒绝，固定“失败必须 throw、不得 exit”的接口合同和防御行为。
6. 让测试 finalizer 抛错或返回非法结果，断言 detailCode 精确保持 `hourly_formal_commit_failed`。

测试只在系统临时目录创建带唯一 GUID 的私有夹具，并在精确核验路径后清理；不触碰项目 runtime、真实 worktree、automation 配置或主分支。

### 6.2 复用现有测试

- `tools/test-automation-finalize-commit.ps1`：证明 expected paths 严格匹配、changed subset、无关 staged/dirty/untracked 隔离、删除/新增原子提交和元数据合同不变。
- `tools/test-hourly-integration-lock.ps1`：证明锁占用返回、冲突拒绝和 fast-forward 结果不变。
- `tools/check-automation-workflow.ps1` 与 `tools/test-check-automation-workflow.ps1`：把共享入口静态合同从旧 CLI token 更新为进程内 named splatting，并证明其余工作流文件合同保持一致。
- `tools/check-pwsh-runtime.ps1`：证明相关脚本继续满足 PowerShell 7 运行边界。

### 6.3 提交门禁

在隔离 worktree 中：

1. 对本轮预期路径运行 `tools/check-pending-whitespace.ps1`；
2. 只暂存设计文档、`invoke-hourly-owner.ps1`、`check-automation-workflow.ps1` 和新增回归测试；
3. 运行 `git diff --cached --check`；
4. 核对提交不包含主工作区用户改动。

## 七、隔离、提交与正式集成

1. 设计与实施均在 `.worktrees/manual-hourly-finalizer-inprocess` 的 `codex/hourly-finalizer-inprocess-repair-design` 分支进行。
2. 实施前与合并前分别重新调用 schema 5 `Show`、检查集成锁、`master` HEAD、主工作区路径冲突和 worker 状态。
3. 仅当两个 owner run 均未占用本次基础设施工作、集成锁空闲、`master` 未发生未核验变化且本次路径不与主工作区改动冲突时，才正式集成。
4. 正式合并必须通过 `tools/invoke-project-integration.ps1` 持有同一集成锁执行 fast-forward，不直接 `git merge` 绕过入口。
5. 合并后再次确认 `master` 到达目标提交、runtime 为空、锁空闲且 `codex-hourly-worker` 仍为 `PAUSED`。

## 八、完成条件

以下条件全部满足才算完成：

1. 自动化实际 formalizer 调用链不再创建嵌套 `pwsh`。
2. 648 条以上、超过 Windows 命令行长度的 `expectedPaths` 能完整到达 finalizer。
3. finalizer 的路径严格校验、提交元数据和 changed subset 行为无变化。
4. finalizer 失败仍精确产生 `hourly_formal_commit_failed`，现有 attention 状态语义无变化。
5. 集成锁、冲突拒绝、candidate 验证和 fast-forward 语义通过现有测试。
6. 预期路径检查、pending whitespace 与 staged diff 门禁通过，并形成仅含本次路径的提交。
7. 提交已通过正式集成入口 fast-forward 到 `master`；主工作区无关用户改动未被提交。
8. `codex-hourly-worker` 全程和最终均保持 `PAUSED`；默认 50 分钟超时未修改。

## 九、首次启用后的输出流回归修订

原修复合并后，用户另行明确要求恢复 `codex-hourly-worker`。首次正式运行已经完成业务提交并把 `master` 推进到 `830669ebebb6a27c1bfaa3176aceb6008f3bb0ed`，但入口最终报告 `codex_terminal_json_invalid`。这证明 formalizer、路径校验、提交和 fast-forward 已成功，失败发生在共享入口返回值的 JSON 解析边界。

根因是进程内调用只使用 `2>&1` 捕获 error stream。`automation-finalize-commit.ps1` 调用的 `check-pending-whitespace.ps1` 通过 `Write-Host` 写 information/host stream；嵌套 `pwsh` 被移除后，该 stream 不再由子进程 stdout 边界隔离，因而先于最终 JSON 泄漏给触发器，使原本严格的单 JSON 输出无法解析。

本修订采用唯一最小方案：把 `Invoke-Finalizer` 的捕获从 `2>&1` 改为 `*>&1`。所有 finalizer stream 都只进入函数内部的 `$output`，最后一项 SHA、紧随调用保存的 `$?`、异常映射和 `hourly_formal_commit_failed` 语义保持不变。未采用修改 whitespace checker 输出、放宽入口 JSON 解析或增加兼容分支，因为这些方案都会把修复移离实际泄漏边界或削弱现有输出合同。

回归测试让 648 路径成功夹具先执行 `Write-Host`，再制造内部 native exit code `1` 并输出合法 SHA；从 `Invoke-Finalizer` 外层捕获全部 stream，要求可见结果严格只有一个 SHA。原有超长参数、生产 finalizer 无 `exit`、显式非零退出、throw 和非法输出测试继续保留。worker 在本修订期间及完成后保持用户当前要求的 `ACTIVE`，调度和默认 50 分钟超时均不修改。
