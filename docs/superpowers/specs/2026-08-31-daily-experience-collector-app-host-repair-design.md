# 每日经验候选收集器 App 主机修复设计

## 一、背景与根因

2026-08-31 13:15（香港时间），`codex-hourly-worker` 领取了 `M-EXP-DAILY-COLLECTOR-01`，建立 schema 5 Codex run `d36af4f5-1bc5-417b-85ea-9c729c602391`，并在独立 owner worktree 中启动 `codex exec` candidate 会话。

任务卡的单一结果同时包含两类责任：

1. 在仓库中实现并集成确定性的每日经验候选收集器及测试。
2. 通过 Codex App automation 管理能力创建、查看并启用唯一 `tzg-daily-experience-candidate-collector` 定时任务。

现有小时 owner 会把整个 `codex_execute` 任务交给 `tools/invoke-codex-candidate.ps1` 和 `codex exec` 子进程。该子进程只拥有候选实现所需的 CLI 工具合同；`tools/invoke-codex-candidate.ps1` 也明确禁止 candidate 管理 automation。它没有 Codex App 的 automation create/view 能力，因此在任何业务文件写入、candidate 提交或正式集成发生前，以 `status=failed`、`detailCode=daily_automation_management_unavailable` 停止。共享入口随后把 run 置为 `attention_required`。

14:16 和 15:16 的后续小时调用没有再次执行任务，只按 schema 5 合同返回同一个 Codex run 的 `existing_run`。因此用户界面中最近的三次异常实际是一次真实能力边界失败和两次残留 run 的重复报告，不是三次独立业务失败。

根因不是收集器算法、Git 集成或重试不足，而是任务卡包含 App 主机外部配置责任，现有 CLI candidate 执行面无法完成该责任。修复应把这一次 App 配置步骤放回具备 automation 管理能力的当前 Codex App 主机上下文，同时继续由仓库既有确定性脚本、验证和集成门禁承担项目写入。

本文中的“当前 Codex App 主机上下文”指当前远程连接背后的 Codex App 会话：它能够调用 automation 管理能力。它不是用户看到的远程客户端界面，也不是由小时 worker 启动的 `codex exec` 子进程。

## 二、目标与非目标

### 目标

1. 精确关闭没有 candidate、没有正式结果、没有业务差异的失败 run，并只清理与该 run 精确绑定的干净 worktree 和临时 branch。
2. 在当前 Codex App 主机上下文创建并核验唯一的每日薄触发，正式代码集成前保持 `PAUSED`，集成后才置为 `ACTIVE`。
3. 在全新隔离 worktree 中按任务卡实现确定性收集器和测试，使用现有项目集成锁与 fast-forward 门禁进入 `master`。
4. 保证候选制卡和来源 `collected/promotionTaskId` 回链在同一正式提交中原子发生；失败不留下部分项目事实。
5. 完成 App 配置签收后归档 `M-EXP-DAILY-COLLECTOR-01`，并只移除 `M-EXP-READY-SCHEMA2-ACTIVATE-01` 对本任务的具名前置。
6. 在修复全过程阻止小时 worker 重复领取或报告同一任务，并留下可精确恢复的停止点。

### 非目标

- 不给 `codex exec`、candidate wrapper 或小时 owner 增加 automation 管理权限。
- 不修改通用 candidate wrapper、schema 5 runtime、owner adapter、QueueMaintenance 或小时调度架构。
- 不新增第二 runtime、恢复对象、重试层、后台守护、自动冲突解决或兼容分支。
- 不调用模型判断候选价值、查重或晋级，不生成正式经验，不修改风险索引，不激活经验门禁。
- 不执行任何真实经验候选的整理任务，不扩大非技术领域的 runnable 范围。
- 不触碰主工作区现有的无关人工修改和未跟踪设计文档。

## 三、最小修复边界

修复由三个现有边界协同完成，不新建通用工作流：

1. **当前 Codex App 主机上下文**只负责 automation 的查重、创建、暂停、启用和最终 view 签收。
2. **新的人工隔离 worktree**只负责本任务授权路径内的实现、测试、提交和生命周期投影。
3. **仓库现有确定性门禁**继续负责 schema 5 run 精确关闭、风险预检、任务卡检查、进程持有型集成锁、路径冲突检查和 fast-forward。

项目 Git 事实与 App automation 配置不能形成跨系统事务。因此采用两个项目提交和一个明确的外部配置 checkpoint，而不是伪造原子性：

- 第一提交只集成收集器和测试；此时原任务仍为 `ready`，每日 automation 仍为 `PAUSED`。
- App 主机启用并 view 核验 automation 后，第二提交才归档原任务、更新队列／backlog，并移除唯一指定的下游前置。

这样任何中途失败都能停在“代码已存在但不可定时写入”或“配置已签收且生命周期已闭环”的可判断状态，不需要补偿性状态机。

## 四、事故 run 的精确关闭

### 4.1 暂停入口

先通过 view 确认当前 Codex App 主机上下文对 `codex-hourly-worker` 具备 automation 管理权；若不能读取并更新该实例，按第十二节停止，不进入 run 关闭或项目实现。

开始任何处置前，通过 automation 管理能力 view 当前 `codex-hourly-worker` 配置，保留其 name、prompt、project、destination、execution environment、model、reasoning、notification policy 和 schedule，仅把状态改为 `PAUSED`，再 view 确认。不得编辑 automation TOML。

若无法暂停或 view 确认，停止，不关闭 run、不清理 worktree、不开始实现。

### 4.2 关闭前证据

重新调用 schema 5 `Show`，并逐项确认：

- owner 为 `codex`；
- taskId 为 `M-EXP-DAILY-COLLECTOR-01`；
- runId 为 `d36af4f5-1bc5-417b-85ea-9c729c602391`；
- state 为 `attention_required`；
- recoveryReason 与已记录的 `daily_automation_management_unavailable` 失败事实精确一致；
- repository、baseCommit、taskCardDigest、worktree 路径和 worktree branch 均与 runtime 记录一致；
- candidateCommit、candidateResult、canonicalBranch、canonicalBase、canonicalHead 和正式结果均为空；
- owner worktree 干净，HEAD 仍等于冻结 base，没有 staged、unstaged 或 untracked 业务变化；
- 对应 `codex exec` 进程已经退出，没有仍可能写入该 worktree 的活动进程；
- `master` 中没有该 run 形成的业务提交。

任一证据不一致都保留现场并停止，不通过删除文件、改 runtime JSON 或猜测恢复来消除差异。

### 4.3 关闭和清理

证据完全匹配后，调用 `tools/hourly-automation-lease.ps1 -Action CompleteRun`，传入精确 owner、runId、`CompletionCategory=failed`、稳定 detailCode 和逐字匹配的 `ExpectedRecoveryReason`。只接受 `status=RUN_COMPLETED`；不得直接编辑 runtime JSON。

关闭后再次 `Show`，确认 `runs.codex=null` 且 DeepSeek run 未被改变。随后重新核验 worktree 的绝对路径、runId、branch、HEAD 和干净状态，只删除该 run 的精确 worktree，再只删除对应且未被其他 worktree 使用的临时 branch。保留历史会话与失败日志作为诊断证据。

若 CompleteRun 成功而清理证据不满足，保留 worktree／branch 并报告；不得泛化删除 `.worktrees/automation` 或其他 run。

空失败 run 的关闭和清理不发送成功通知，也不把任务改为 completed、blocked 或 archived。

## 五、每日 automation 的配置合同

### 5.1 唯一性与初始状态

在当前 Codex App 主机上下文中，按 automation ID 和名称查找 `tzg-daily-experience-candidate-collector`，并结合 automation 配置目录的只读事实确认是否已存在：

- 不存在时创建唯一实例；
- 存在且完整配置与本合同一致时复用并更新完整字段；
- 同名／同职责存在多个实例，或 ID 与名称绑定不一致时停止，不自行删除或合并。

目标配置固定为：

- name：`tzg-daily-experience-candidate-collector`；
- kind：`cron`；
- project：当前 `D:\天章游戏开发` 项目对应的已保存 project ID；
- execution environment：`local`；
- destination：`local`；
- schedule：香港时间每日一次 `00:10`；
- model：`gpt-5.6-terra`；
- reasoning effort：`low`；
- notification policy：仅失败通知；
- 初始 status：`PAUSED`。

创建后立即 view，确认完整配置与唯一性。第一项目提交正式进入 `master` 前，automation 必须始终保持 `PAUSED`。

### 5.2 薄 prompt

automation prompt 只承担触发层责任：

1. 在 `D:\天章游戏开发` 对确定性收集入口启动一个前台 `pwsh` 进程；
2. 等待同一进程完成，不后台化、不另开模型工作流；
3. 解析并原样报告该入口的单个结构化终态 JSON；
4. 不读候选正文、不选择任务、不写 Git、不管理 schema 5 owner run、不调用 Codex／DeepSeek candidate wrapper，也不自行重试。

实现阶段冻结收集脚本的公开参数和单 JSON 输出合同后，再把对应的唯一命令写入 prompt；不在设计阶段猜测额外参数或增加触发器兼容分支。

## 六、收集器的确定性行为

### 6.1 输入与窗口

新建 `tools/invoke-daily-experience-candidate-collector.ps1`。入口只读取 `开发管理/任务归档/<ID>.txt` 中：

- 合法 TASK-META／TASK-BODY；
- `dispatchState=completed` 的完成归档；
- 固定 `## 经验候选` 章节、三级候选标题和任务合同规定的精确字段。

主窗口为前一个香港自然日，同时补收所有更早仍为 `collectionState=pending` 的合法候选。`provisional`、`collected`、字段非法、来源未 completed 或已有 `promotionTaskId` 的候选均不制卡。

每轮在形成结果前，重新绑定并核验来源 task ID、candidate ID、候选正文 digest、来源任务状态和目标路径。事实变化导致整批停止，不使用首次扫描的陈旧结果继续提交。

### 6.2 稳定标识和预留路径

每个整理任务 ID 固定为：

`M-EXP-PROMOTE-` + `SHA-256(sourceTaskId + LF + candidateId)` 前 12 位大写十六进制。

若目标 task ID 已存在但绑定并非同一来源任务与候选，整批失败；不得改用随机 ID 或追加序号绕过碰撞。

预留经验 ID 只读取风险索引中的现有 ID，以及全部活动／归档整理卡 `expectedPaths` 已预留的经验卡路径，然后机械取得同领域最小未用三位序号。首期固定映射包括：

- `design -> management / MGMT`；
- `numeric -> battlesim / BS`；
- 其余领域只按已经批准的规格映射，不在实现中猜测新领域。

候选正文保留原始领域。预留 ID 或目标路径碰撞使整批失败；收集器不得为了预留而修改 `开发管理/经验库/风险索引.json`。

### 6.3 逐候选机械制卡

每条合法 pending 候选生成一张独立整理卡，固定为 `codex_execute / codex / P2 / implementation`，不得合并多个候选。卡片 `expectedPaths` 精确包含：

- 来源归档；
- `开发管理/经验库/风险索引.json`；
- 预留经验卡路径；
- 自身活动卡和未来归档路径；
- `开发管理/任务列表/管理与自动化任务.txt`；
- `开发管理/当前任务队列.txt`。

`design`、`content`、`numeric` 分别具名前置 `M-EXP-EXT-DESIGN-01`、`M-EXP-EXT-CONTENT-01`、`M-EXP-EXT-NUMERIC-01`，生成后保持 blocked 且不入队。其他首期领域只有在现行 runnable 合同满足时才可插入队列；插入顺序的唯一权威来源是 `开发管理/状态与建议维护规则.txt` 的队列稳定枚举，收集器不得复制或另建第二套排序／runnable 逻辑。任何插入不得改变既有 ready 行的相对顺序。

### 6.4 原子回链与防重

生成全部整理卡及投影后，在同一个正式提交中把每个来源候选改为：

- `collectionState=collected`；
- `promotionTaskId=<对应稳定整理任务 ID>`。

防重只依赖来源候选的 `collectionState` 与 `promotionTaskId` 回链，不建立第二候选索引、每日候选正文、恢复记录或重试队列。

零合法 pending 时不修改文件、不创建 worktree 提交，返回结构化 `no_candidate`。任一候选、来源 digest、路径、任务 ID、预留 ID、主工作区冲突、锁、验证、提交或集成失败时不产生部分正式提交，所有来源保持 pending，由后续自然日再次扫描。

### 6.5 Git 与锁边界

每次实际收集从当时最新 `master` 建立一次性独立 worktree，只允许确定性计算出的精确 changed paths。形成提交前重新核验来源和目标 digest；正式集成必须通过现有 `tools/hourly-integration-lock.ps1` 的进程持有型锁，并使用现有路径冲突、主分支 HEAD 和 fast-forward 合同。

不调用 `invoke-hourly-owner.ps1`、QueueMaintenance、Codex／DeepSeek candidate wrapper 或模型。收集器不写 schema 5 runtime，也不把自身变成第三个 owner pipeline。

## 七、实现与签收顺序

### 7.1 新实现 worktree

失败 run 关闭并完成精确清理后，从当时最新 `master` 建立一个新的人工隔离 worktree和专用 branch。写入前运行任务卡的风险预检，并重新读取任务卡、队列、backlog、下游依赖、主工作区 dirty paths 与 automation 配置；任何授权路径重叠或事实变化都停止。

不得在当前有人工修改的主工作区直接编辑、暂存或提交。

### 7.2 第一项目提交：实现提交

第一提交只包含：

- `tools/invoke-daily-experience-candidate-collector.ps1`；
- `tools/test-daily-experience-candidate-collector.ps1`。

这一阶段不归档 `M-EXP-DAILY-COLLECTOR-01`，不把它移出 ready 队列，不移除下游前置。完成最小验证后，使用 `tools/invoke-project-integration.ps1` 取得同一集成锁，核对最新 `master`、授权路径和主工作区冲突，再 fast-forward 第一提交。

第一提交集成后，以隔离的零候选输入运行一次结构化 dry run，证明不写文件、不提交；随后确认目标 automation 仍为 `PAUSED`。

### 7.3 App 配置 checkpoint

只有第一提交已经进入 `master`、验证仍成立、目标 prompt 指向已集成的公开脚本合同后，才在当前 Codex App 主机上下文把唯一目标 automation 更新为 `ACTIVE`。

立即 view 并确认 name、project、local destination／execution environment、schedule、model、reasoning、notification policy、prompt 和 status 全部符合第五节。任何字段不一致都立即把目标 automation 恢复为 `PAUSED` 并停止，不进行任务归档。

### 7.4 第二项目提交：生命周期提交

App 配置 checkpoint 通过后，从最新 `master` 更新同一隔离 branch，形成第二个路径限定提交：

- 把 `开发管理/任务卡/M-EXP-DAILY-COLLECTOR-01.txt` 移至对应任务归档，并写入实际验证与配置签收事实；
- 从 `开发管理/当前任务队列.txt` 移除本任务；
- 把 `开发管理/任务列表/管理与自动化任务.txt` 中本任务更新为完成投影；
- 只从 `M-EXP-READY-SCHEMA2-ACTIVATE-01` 的 `blockedBy` 中移除 `M-EXP-DAILY-COLLECTOR-01`，保留其他前置和现有状态，除非既有确定性维护规则要求同步投影。

重新运行生命周期相关检查后，再通过 `tools/invoke-project-integration.ps1` 的同一锁合同 fast-forward 第二提交。

第二提交进入 `master` 后，再 view 目标 automation，确认仍为 `ACTIVE` 且完整配置未漂移。

### 7.5 恢复小时 worker

全部项目事实和 App 配置签收完成后，把 `codex-hourly-worker` 恢复为开始时保留的完整配置，只把 status 改回 `ACTIVE`，并 view 确认。不得顺带修改其 schedule、prompt、model 或通知策略。

## 八、失败停止与补偿边界

全过程不自动重试。停止点固定如下：

| 停止位置 | 项目事实 | 目标每日 automation | 小时 worker | 处理 |
| --- | --- | --- | --- | --- |
| 事故 run 尚未精确关闭 | 原任务 ready，旧 run 保留 | 不创建或保持 PAUSED | PAUSED | 保留现场并报告 |
| 第一提交集成前失败 | master 无本次实现 | PAUSED | PAUSED | 保留实现 worktree 和证据 |
| 第一提交已集成、启用前失败 | 代码已在 master，原任务仍 ready | PAUSED | PAUSED | 安全停在不可定时写入状态 |
| 启用／view 不一致 | 代码已在 master，原任务仍 ready | 立即恢复 PAUSED | PAUSED | 不归档任务，报告字段差异 |
| automation 已签收、第二提交失败 | 代码已在 master，原任务仍 ready | 立即恢复 PAUSED | PAUSED | 保留生命周期 worktree，避免配置领先项目事实 |
| 第二提交已集成、小时 worker 恢复失败 | 功能和任务生命周期均完成 | 恢复 PAUSED | PAUSED | 不回滚项目，只报告两项 automation 均暂停 |

任何失败都保证目标每日 automation 保持或恢复为 `PAUSED`，`codex-hourly-worker` 保持 `PAUSED`，以便普通管理上下文从精确证据继续处置。不得用回滚提交、强制 reset、stash、clean、自动重试或新增 runtime 状态进行补偿。

## 九、验证设计

### 9.1 收集器测试

运行：

`pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-daily-experience-candidate-collector.ps1`

临时仓库至少覆盖：

- 零、一、多条 pending；
- 同一来源的多候选；
- 前一香港自然日主窗口和更早 pending 补收；
- `provisional`、`collected`、已有 `promotionTaskId` 跳过；
- 非 completed 来源、非法字段和来源 digest 变化；
- 稳定 task ID、task ID 碰撞、预留经验 ID／路径碰撞；
- 重复运行不重复制卡；
- `design/content/numeric` 整理卡保持具名 blocked；
- 既有 ready 行相对顺序不变；
- 制卡与来源 `collected/promotionTaskId` 在同一提交；
- 主工作区目标路径冲突、集成锁占用、验证失败、提交失败和集成失败均整批不形成正式提交；
- 下一轮能自然补收仍为 pending 的候选；
- 收集器从不修改风险索引、创建正式经验卡、写 schema 5 runtime、调用小时 owner／模型或建立空提交。

### 9.2 仓库门禁

按变化阶段运行最小充分集合：

1. `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-task-cards.ps1`
2. `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-automation-workflow.ps1`
3. `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-review-text.ps1 -Paths 开发管理,tools`
4. 对本轮精确预期路径运行 `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-pending-whitespace.ps1`
5. 每次提交前运行 `git diff --cached --check`
6. 每次集成前核验 task card digest、最新 `master`、branch／HEAD、精确 changed paths、主工作区冲突和集成锁

不运行与本任务无关的 Unity、BattleSim 或全数据链路验证。

### 9.3 App 配置与 dry run

- 创建后 view：唯一实例、完整字段、状态 `PAUSED`；
- 第一提交集成后：以隔离零候选输入运行脚本，得到单个结构化 `no_candidate`，Git 无变化；
- 启用后 view：香港时间每日仅一次 `00:10`，本地项目、`gpt-5.6-terra/low`、仅失败通知、状态 `ACTIVE`；
- 第二提交集成后再次 view，证明配置没有在生命周期更新期间漂移；
- 小时 worker 恢复后 view，证明除 status 外没有配置漂移。

## 十、DeepSeek 设计审核门禁

实现前，把本文、`M-EXP-DAILY-COLLECTOR-01` 任务卡、自动工作流规则和人工处置规则提供给 DeepSeek 做一次只读审核。审核范围固定为：

- 根因是否与实际能力边界一致；
- 两项目提交与 App checkpoint 是否存在不可恢复窗口；
- 空 run 关闭证据是否满足 schema 5 精确合同；
- 收集器原子性、防重、稳定 ID、路径和锁边界是否遗漏；
- 失败矩阵是否会让 automation 配置领先项目事实；
- 是否无意扩大通用小时工作流或引入第二机制。

DeepSeek 不得写文件、修改 Git、关闭 run 或管理 automation。审核意见只作为建议；只有能由现有事实证明、且能以最小改动修正设计的意见才纳入本文。超出任务卡范围、引入重试／新状态／新组件或把 App 权限下放给 candidate 的建议应拒绝，并记录理由。

2026-08-31 已通过本机 `deepseek-v4-pro`（DeepSeek V4 Pro 0813）完成无工具、只读审核。结论为“通过、无阻止项”，并提出两条低严重度澄清：把 App 管理权 view 前移为第一步，以及显式锁定队列稳定顺序的唯一规则来源。两条均由现有事实支持，已分别吸收到第 4.1 节和第 6.3 节；审核未建议新增 runtime、重试、通用 wrapper 改造或把 App 权限下放给 candidate。

## 十一、完成标准

- 原 `d36af4f5-1bc5-417b-85ea-9c729c602391` run 已按 schema 5 精确合同关闭，只有其精确干净 worktree／branch 被清理，历史失败证据保留。
- `tools/invoke-daily-experience-candidate-collector.ps1` 和对应测试已经通过最小门禁并进入 `master`。
- 合法 pending 候选逐条确定性制卡，来源回链与制卡同提交；重复运行防重，更早 pending 自然补收。
- 任一批次失败不产生部分正式提交；零候选不写文件、不提交。
- 没有风险索引写入、正式经验、模型判断、新 runtime、新 owner 分支、重试层或通用 wrapper 修改。
- 唯一 `tzg-daily-experience-candidate-collector` 仅在代码集成后启用，完整配置经两次 ACTIVE view 核验。
- `M-EXP-DAILY-COLLECTOR-01` 已归档，队列／backlog 一致，下游只移除本 ID 前置。
- `codex-hourly-worker` 已恢复为原完整配置并处于 `ACTIVE`；若最后恢复失败，则目标每日 automation 也恢复为 `PAUSED`，明确报告两项 automation 的暂停状态，不回滚已完成的项目提交。

## 十二、停止条件

- 当前 App 主机不能唯一创建并 view 目标 automation，或无法保证代码集成前保持 `PAUSED`。
- 事故 run 的 owner、runId、taskId、recoveryReason、提交字段、worktree、branch、HEAD、进程或干净状态与本文事实不一致。
- 候选字段或任务卡 schema 不能确定性映射，需要模型判断价值、查重或领域。
- 不能在单一正式提交中同时制卡和来源回链，或防重必须依赖第二索引／runtime。
- 实现需要修改通用 candidate wrapper、schema 5、小时 owner、风险索引或正式经验，或需要新增恢复／重试／后台机制。
- 主工作区相关路径存在人工修改，最新 `master` 变化导致任务卡／队列／依赖事实不再匹配，或集成锁与路径门禁不能满足。

命中任一停止条件时，立即停止实施，保持目标每日 automation 和小时 worker 处于本节前述安全状态，保留 worktree 与证据并请求重新确认；不得连续叠加补丁绕过根因。
