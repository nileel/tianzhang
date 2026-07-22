# 自动化复杂度收敛与恢复链修复实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** 在不重建中央状态机、不放宽写入隔离的前提下，修复自动化遗留改动失联、无效 `waiting_decision`、CLI 启动不稳定、业务提交结果丢失和每日简报缺少端到端验收的问题，并把控制器重新收敛为薄路由器。

**Architecture:** 保留单写入租约、workspace guard、Git 提交元数据、CLI 原生 session 和外部决策恢复五个必要边界。写入链新增的唯一生产边界是一个固定的 Codex 责任方调用器，用来替代控制器每轮临时拼装 PowerShell/编码逻辑；它只负责传输、会话 ID、Git 结果核验和异常恢复，不负责选题或业务判断。租约状态只增加一种“中断恢复”原因，不增加 phase、checkpoint、manifest 或第二套队列。每日简报另用一个无状态、只读的 Git 数据源脚本替换提示词内不可测试的解析逻辑。

**Tech Stack:** PowerShell 7、Git、Codex CLI、现有 automation 配置、现有 PowerShell 自测脚本。

## 已确认的故障与停止条件

1. `src/Assets/Tests/EditMode/SpatialQueryBoardTests.cs` 是自动责任方会话 `019f891d-a12a-7230-944a-0b9e1db14220` 在 2026-07-22 17:21 留下的半成品，不是人工文件；实施期间不得删除、覆盖或当作普通冲突跳过。
2. 当前两个生产自动化都保持 `PAUSED`。本计划完成全部离线验证前，不恢复定时运行。
3. 现有三组自测和生产检查均通过，却未发现本次事故；不得以“现有测试全绿”作为恢复生产的依据。
4. 不重写 `automation-workspace-guard.ps1`、飞书决策链或外部 AI 双提交协议；它们不是本次根因。
5. 若实现需要新增 phase、checkpoint、中央 manifest、重试层或第二份 runtime 状态，立即停止并重新评审设计。

## 目标工作流

```text
控制器 Show/选题/Acquire
        |
        v
固定责任方调用器（一次命令）
        |
        +-- Codex CLI Start/Resume
        +-- 成功提交：从 Git 元数据核验 Task/State/SHA
        +-- 等待决策：核验 decision recovery 已存在
        +-- 异常且产生新改动：保存 interruption recovery
        +-- RecordResult + Release
        |
        v
控制器只报告已核验的 category/session/SHA
```

### Task 1: 先用失败测试钉死孤儿改动根因

**Files:**
- Modify: `tools/test-hourly-automation-lease.ps1`
- Modify: `tools/test-codex-cli-session.ps1`
- Create: `tools/test-invoke-codex-responsibility.ps1`

**Step 1: 增加当前必然失败的租约用例**

覆盖以下不变量：

```powershell
# 没有匹配 recovery 时不得记录等待决策。
Invoke-Lease -Action RecordResult -Category waiting_decision ... | Should-Fail

# decision recovery 只允许匹配相同 runId/taskId 的 waiting_decision。
# interruption recovery 不得进入 QueueResume。
# lastResult 必须记录 runId，避免调用器误读上一轮结果。
```

**Step 2: 增加责任方调用器的故障夹具**

使用临时 Git 仓库和假的 `codex` 可执行文件，至少覆盖：

- 子进程在 `thread.started` 后新增文件并以非零码退出：必须保留文件并生成中断恢复指针。
- 子进程成功且创建带完整 Automation 元数据的提交：输出精确 `commitSha` 和 `success/refilled`。
- 子进程成功但没有提交、没有 recovery：结果必须失败关闭，不能写成泛化 `success`。
- 子进程留下 decision recovery：只输出 `waiting_decision`，不得再保存一份中断恢复。
- 调用器在包含中文、反引号、换行的输入下工作，控制器无需使用 `Buffer`、`TextEncoder` 或临时 here-string。

**Step 3: 运行失败基线**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-hourly-automation-lease.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-invoke-codex-responsibility.ps1
```

预期：新增用例失败，且失败点分别落在 `waiting_decision` 缺少恢复校验、尚无中断恢复和固定调用器。

### Task 2: 用同一个 recovery 记录承接决策等待与意外中断

**Files:**
- Modify: `tools/hourly-automation-lease.ps1`
- Modify: `tools/test-hourly-automation-lease.ps1`
- Modify: `tools/feishu-decision-bridge/test/send.test.mjs`
- Modify: `开发管理/自动工作流规则.txt`

**Step 1: 扩展现有 recovery，而不是新建状态机**

在 recovery 对象加入唯一判别字段：

```powershell
trigger = 'decision'     # 现有 DecisionId/DecisionRequestPath 必填
# 或
trigger = 'interruption' # DecisionId/DecisionRequestPath 必须为 null
```

两种 recovery 都保存当前 `runId`。增加 `SaveInterruption` 动作，复用现有的 `taskId/runId/owner/repositoryRoot/resumeKind/resumeId/hasUncommittedChanges/changedPaths`。不得增加 phase、步骤号或 checkpoint。

**Step 2: 收紧状态转换**

- `RecordResult -Category waiting_decision` 仅在当前 lease 与 `trigger=decision` recovery 的 `taskId/runId` 匹配时成功。
- `QueueResume`、`TakeResume` 仅接受 `trigger=decision`。
- 普通 `Acquire` 遇到任一含未提交改动的 recovery 均返回 `RECOVERY_ONLY`。
- 原责任方恢复时以现有 `Acquire -ResumeRecovery` 显式取得新租约；仅当 TaskId、Owner、RepositoryRoot 与 recovery 完全匹配时返回 `RECOVERY_ACQUIRED`，不新增恢复 action。
- `lastResult` 增加 `runId`，调用器只能消费本轮结果。
- `Release` 不清空 recovery；无匹配 recovery 的 `waiting_decision` 不得通过先失败记录、再单独 Release 的方式绕过。

**Step 3: 在短规则中明确责任边界**

责任方可以调用 workspace guard、finalizer 和 `SaveRecovery`；不得自行 `RecordResult` 或 `Release`。后两项只由固定调用器在核验结果后执行。

同时明确：旧 `pending-bindings.json` 不是新决策的互斥锁。责任方必须实际调用 `send-decision.mjs`；只有 provider accepted 后生成的 consume request 才能传给 `SaveRecovery`。在 `send.test.mjs` 保留/补充“已完成旧 binding 可被新 accepted send 原子替换”的回归用例，禁止责任方因看到旧 binding 就直接记录 `waiting_decision`。

**Step 4: 运行最小测试**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-hourly-automation-lease.ps1
Set-Location tools/feishu-decision-bridge
node --test test/send.test.mjs
```

预期：原有恢复测试继续通过，新增 decision/interruption 互斥和 fail-closed 用例通过。

### Task 3: 固定 CLI 传输与结果回传，删除每轮即兴启动逻辑

**Files:**
- Create: `tools/invoke-codex-responsibility.ps1`
- Create: `tools/test-invoke-codex-responsibility.ps1`
- Modify: `tools/codex-cli-session.ps1`
- Modify: `tools/test-codex-cli-session.ps1`

**Step 1: 保持底层 runner 无业务语义**

`codex-cli-session.ps1` 继续只做 Start/Resume、stdin 传输和唯一 `thread.started` 捕获。补齐失败时也返回已捕获 `sessionId` 的测试，不解析模型最终文本。

**Step 2: 实现唯一生产调用器**

建议接口固定为：

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/invoke-codex-responsibility.ps1 `
  -Action Start `
  -Route Execution `
  -RepositoryRoot 'D:\天章游戏开发' `
  -TaskId 'U-2_5D-01D' `
  -RunId '<lease-run-id>' `
  -Model '<controller-verified-model>'
```

调用器内部使用已经测试过的 `System.Diagnostics.ProcessStartInfo` 向 `codex-cli-session.ps1` 写 stdin；控制器不再拼装 PowerShell、编码或 JSON 转义。Start 提示只传模型核验证明、Route、TaskId、RunId，并引用 `AGENTS.md`、`自动工作流规则.txt` 和对应入口，不复制规则正文。

**Step 3: 以 Git 和 runtime 生成可核验结果**

调用前记录 `HEAD` 与工作区状态；调用后：

```text
completed  = preHead..HEAD 中存在 Automation=true、Task 精确匹配、State 合法的业务提交
waiting    = 当前 run/task 的 decision recovery 存在
interrupted= runner 非零且相对基线出现新改动；保存 interruption recovery
failed     = 以上均不满足
```

若出现新改动但 runner 没有返回唯一 sessionId，则不得伪造恢复指针，也不得 Release；保留 lease 和工作区证据，返回需要人工接管的明确 blocker。

输出只保留：

```json
{"status":"completed","category":"success","taskId":"U-2_5D-01D","runId":"...","sessionId":"...","commitSha":"..."}
```

`QUEUE-MAINTENANCE` 的已核验提交映射为 `refilled`。不得用模型自然语言推断 SHA，也不得把 runner 的退出码直接等同于业务成功。

**Step 4: 统一关闭租约**

调用器根据上述核验结果执行一次 `RecordResult` 和一次 `Release`。若保存 recovery 或结果记录失败，保持 lease/recovery 证据并返回非零；不得 clean/reset/revert。

**Step 5: 运行传输与契约测试**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-codex-cli-session.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-invoke-codex-responsibility.ps1
```

### Task 4: 把控制器重新缩成薄路由，并清理失效规则

**Files:**
- Modify: `开发管理/自动工作流控制器提示词.txt`
- Modify: `开发管理/自动工作流规则.txt`
- Modify: `开发管理/状态与建议维护规则.txt`
- Modify: `开发管理/自动工作流状态.txt`
- Modify: `tools/check-automation-workflow.ps1`
- Modify: `tools/test-check-automation-workflow.ps1`

**Step 1: 控制器提示只保留六件事**

1. `Show`；2. 恢复优先；3. 按规则选唯一任务；4. `Acquire`；5. 调固定调用器或既有外部 wrapper；6. 报告核验后的 category/session/SHA。

删除提示词中 ProcessStartInfo、stdin 编码、提交字段说明、决策文件转换、暂停 UI 操作步骤等实现细节，只引用唯一规则源。

**Step 2: 删除已经不存在的队列维护契约**

从 `状态与建议维护规则.txt` 删除或改写以下失效概念：`RecordQueueState`、checkpoint、queue/backlog fingerprint、2/5 水位、DeepSeek backoff、`ClearWorkerFailure`。保留现有事实源优先级、去重和“无合法候选不启动外部 CLI”。不得为了让旧文档成立而反向实现这些字段。

**Step 3: 缩减字面检查，增加真实不变量检查**

`check-automation-workflow.ps1` 改为验证：

- canonical controller prompt 与已安装 prompt 在 `ACTIVE` 和 `PAUSED` 时都一致。
- 生产写入自动化最多一个 ACTIVE；当前修复期允许零个。
- controller 不包含 `Buffer`、`TextEncoder`、内嵌 here-string 启动器或已删除 action 名称。
- lease/runner/finalizer/固定调用器路径存在，且规则只声明实际支持的 action。
- 每日简报 canonical prompt 也与安装配置一致。

删除约 50 个易漂移的整句 `expectedPhrases`；测试应通过最小 fixture 验证上述不变量，而不是复制整份提示词。

**Step 4: 收敛状态文件职责**

`自动工作流状态.txt` 只保留迁移状态、人工阻塞和最后一次已核验的恢复事实；删除会随配置漂移的“生产入口 ACTIVE”静态宣称。实时 ACTIVE/PAUSED 以 automation 配置为准，运行结果以 runtime/Git 为准。

**Step 5: 修正错误归属记录**

将 `docs/superpowers/plans/2026-07-22-tianzhang-cosmology-story-and-rules-migration.md` 中“用户现有未跟踪文件”改为“自动责任方遗留、等待原责任恢复的文件”，并在 `自动工作流状态.txt` 写明 session 与 TaskId。

**Step 6: 运行规则检查**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-check-automation-workflow.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-automation-workflow.ps1
```

### Task 5: 让每日简报的数据选择可测试，不再只靠提示词解释 Git

**Files:**
- Create: `开发管理/自动化简报提示词.txt`
- Create: `tools/get-automation-briefing-source.ps1`
- Create: `tools/test-get-automation-briefing-source.ps1`
- Modify: `tools/check-automation-workflow.ps1`
- Modify: `tools/test-check-automation-workflow.ps1`
- Reference: `docs/superpowers/specs/2026-07-22-automation-commit-briefing-design.md`

**Step 1: 新增只读 Git 数据源脚本**

脚本只负责时间窗、六字段解析、`Automation: true` 过滤、handoff 排除、同 Task 多提交归组和 malformed metadata 报警；不判断业务价值，不写仓库，不读取 automation memory。

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/get-automation-briefing-source.ps1 `
  -RepositoryRoot 'D:\天章游戏开发' -Since '<iso-time>' -Until '<iso-time>'
```

输出稳定 JSON，供每日自动化做 diff 语义核验和中文呈现。

**Step 2: 建立设计文档要求但尚未实现的临时 Git fixture**

测试至少包含：Codex completed、外部 AI pending_review、handoff commit、queue maintenance、同 Task 多提交、缺字段提交、窗口外提交。断言候选集合、分组、排除和错误列表均准确。

**Step 3: 用当前真实提交做只读验收**

以 2026-07-22 21:11:27+08:00 之后的窗口运行脚本，必须识别 `fa383fbbbc787e841717d65583bcb359d1c094f4`，Task 为 `QUEUE-MAINTENANCE`，并排除未标记的人工提交 `7c6d58a`。

**Step 4: 缩短每日提示词**

每日提示只负责调用数据源、检查候选 diff 是否支持 Result/Impact/Verify、按 Task 汇总和报告元数据错误。生产配置继续保持 `PAUSED`，只更新 canonical/installed prompt 一致性。

### Task 6: 恢复 U-2_5D-01D，而不是删除本地测试文件

**Files:**
- Modify: `开发管理/当前任务队列.txt`
- Preserve then modify under recovered session: `src/Assets/Tests/EditMode/SpatialQueryBoardTests.cs`
- Expected new files: the five Unity `.cs.meta` files paired with the five new scripts listed by U-2_5D-01D
- Modify as required by task card: `simulations/BattleSim/BattleSim.csproj`

**Step 1: 修正任务卡授权路径**

在五个新 `.cs` 路径后明确加入各自 `.meta`，避免 Unity 导入产生越权写入。先运行 pending whitespace 检查，再提交任务卡修正；该提交不得夹带业务实现。

**Step 2: 建立人工恢复指针**

为 Task `U-2_5D-01D`、原 Codex session `019f891d-a12a-7230-944a-0b9e1db14220` 和现存测试文件登记 interruption recovery。不得把文件改名为用户文件或先删除再重建。

**Step 3: Resume 原责任方**

先用 `Acquire -ResumeRecovery` 为原 Task/session 取得新租约，再由固定调用器使用 `Resume`；责任方第一步执行 workspace guard `Verify`，不得再用只适合修改前的 `Check` 判断修改后状态。

**Step 4: 真正验证测试被编译**

在宣称 TDD RED/GREEN 前，先验证目标测试程序集确实包含 `SpatialQueryBoardTests`。若 Unity 生成的 `TianZhang.EditModeTests.csproj` 未纳入新文件，则用 Unity EditMode 测试运行器或重新生成项目文件；不得用一个未编译该测试的 `dotnet build` 代替 RED/GREEN 证据。

**Step 5: 仅按修正后的 expected paths 完成原任务**

业务责任方完成 workspace guard、最小领域验证和 `automation-finalize-commit.ps1`。外层控制器只接收调用器核验过的 SHA，不读业务 diff、不重验、不再次提交。

### Task 7: 离线验收、受控 canary 与恢复决策

**Files:**
- Modify if facts changed: `开发管理/自动工作流状态.txt`
- Production config: `tzg-hourly-controller`
- Production config: `tzg-daily-automation-briefing`

**Step 1: 一次性运行最小充分测试集**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-hourly-automation-lease.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-codex-cli-session.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-invoke-codex-responsibility.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-get-automation-briefing-source.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-check-automation-workflow.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-automation-workflow.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-review-text.ps1 -Paths AGENTS.md,CLAUDE.md,开发管理
```

**Step 2: 做一次非定时受控 canary**

选择一个无业务写入的临时 Git fixture，验证：固定调用器可启动、返回唯一 session、识别带元数据提交、在失败后保存 interruption recovery、控制器能报告 SHA。不得直接用未完成的 U-2_5D-01D 做 transport canary。

**Step 3: 验收简报**

用固定窗口生成一次预览，结果必须包含 `fa383f...` 的实际工作简述与影响，不得出现“无可报告业务 SHA”。

**Step 4: 保持 PAUSED 并交用户决定恢复时间**

所有修复提交完成后仍保持两个自动化 `PAUSED`，汇报：测试结果、canary 结果、U-2_5D-01D recovery 状态和精确提交 SHA。只有用户明确要求恢复时，再先确认 lease/recovery/pending resume 安全状态，随后更新生产配置。

## 提交切片

1. `test(automation): cover orphaned-change and result-contract failures`
2. `fix(automation): fail closed on invalid recovery transitions`
3. `fix(automation): add verified responsibility invocation boundary`
4. `refactor(automation): restore thin controller contracts`
5. `test(briefing): add deterministic Git source fixture`
6. `fix(queue): restore U-2_5D-01D ownership and expected paths`

每个切片只包含列出的路径；提交前依次运行 `tools/check-pending-whitespace.ps1` 和 `git diff --cached --check`。任何切片若需要跨出本计划列出的边界，停止并重新确认根因。
