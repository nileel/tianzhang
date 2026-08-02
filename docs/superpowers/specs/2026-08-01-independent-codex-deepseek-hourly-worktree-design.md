# Codex 与 DeepSeek 独立小时入口、worktree 开发和 Codex 复审设计

> 日期：2026-08-01
> 状态：负责人已确认；实现与 2026-08-02 固定 Windows 入口 canary 已通过，待生产合并与实时 automation 切换
> 取代方向：不继续采用已撤销的中央 batch、通用 `lanes[]` 和单控制器启动外部 AI 方案
> 当前自动责任方：Codex、DeepSeek V4 Flash

## 1. 背景、根因与设计边界

当前每小时控制器把任务选择、单写入租约和责任方启动集中在 Codex 业务轮次；`external_execute` 由同一控制器选题并等待外部 wrapper。这个结构把两个本可独立工作的责任方串成一条业务启动链，也让全局租约覆盖整个业务实施期。

2026-08-02 实测确认，当前 Claude Desktop 的 `Cowork → Scheduled` 会话在隔离 Linux VM 中运行：它能通过挂载读取项目，却没有 Windows `pwsh`、看不到主机私有 runtime，也没有创建可由 Windows Git 核验的 linked worktree。因此 Cowork Scheduled 不能承载本设计。替代方案不是恢复单控制器串行派工，而是新增第二个独立 Codex automation 作为薄触发器；它不读取队列、不选择任务、不承担业务，只调用一个固定 Windows PowerShell 入口。

目标是拆开两个原本被绑定的边界：

- Codex 业务轮次与 DeepSeek 固定入口分别由两个独立 Codex automation 触发，各自只处理自己的 route／owner；
- 业务实施发生在各自隔离 worktree 中，可以同时运行；
- 只有正式结果进入 `master` 的瞬间需要互斥；
- DeepSeek 的正式结果仍必须由 Codex 独立复审。

本设计不建立并行调度平台。它只支持两个已存在的固定责任方，不恢复中央 batch、通用 `lanes[]`、worker pool、第三集成角色或未来 AI 占位。候选预构建允许失败和重做；主工作区写入必须保持短时、确定且失败前不落地。

## 2. 已批准决策

1. Codex 业务轮次与 DeepSeek 各有一个独立的每小时 automation；两者可重叠运行，互不启动、暂停或恢复对方 automation。
2. `codex-hourly-worker` 只处理 `codex_execute/codex`、`codex_review/codex`、Codex 自身恢复以及队列维护，不启动外部 AI。
3. `deepseek-hourly-trigger` 是无业务判断的 Codex 薄触发器，只调用固定 Windows 入口 `tools/invoke-deepseek-hourly.ps1` 并等待其结构化终态；固定入口才负责 `Show`、确定性选题、原子 claim、worktree、DeepSeek CLI、候选核验与收尾。
4. Codex 业务入口与固定 DeepSeek Windows 入口读取同一有序队列，各自跳过不属于自己的行；同一责任方内部仍保持原队列顺序。DeepSeek 薄触发器不读队列。
5. 两个责任方都在独立 worktree 中实施和验证；路径重叠不阻止开发，只在正式集成时以最新 `master`、任务事实和验证结果裁决。
6. 不新增 `workerPaths`。任务候选继续受现有 `expectedPaths` 上界约束，共享任务投影由固定集成步骤基于最新事实重建。
7. 主工作区只接受已经在 owner worktree 的独立 canonical branch 中完整生成并验证的提交序列；短时 `integrationLease` 只保护最终重新核验与 fast-forward，不覆盖开发和长时间测试。
8. DeepSeek 的正式 `businessCommit` 与 `handoffCommit` 作为一个连续序列一次进入 `master`，不会让主工作区停在“已有业务提交但没有 handoff”的中间状态。
9. DeepSeek 任务不新建第二张复审卡；原任务卡就地转换为 `codex_review/codex/ready`。审核通过前不解锁依赖。
10. Codex 业务轮次每小时 `:15` 触发；DeepSeek 薄触发器每小时独立触发，实际分钟只作为运行观测，不作为正确性门禁或停止条件。
11. Cowork Scheduled 路径已由只读 canary 判定不适用。新生产入口启用前必须通过固定 Windows 入口的私有状态 canary；canary 未通过时不得启用新 runtime 或两个生产 automation。

## 3. 责任边界

### 3.1 DeepSeek 自动入口

固定 DeepSeek Windows 入口只为任务卡已经锁定目标、允许路径、停止条件和可运行验收的 `external_execute/deepseek/ready` 任务领取 run；DeepSeek V4 Flash 只接收已领取任务，可以完成实现、直接验证和候选提交，但不得：

- 决定未冻结的产品语义、架构所有权或世界观事实；
- 修改未列入 `expectedPaths` 的业务路径；
- 自行批准自己的结果；
- 临时拼装主工作区 Git 集成命令；
- 启动、暂停、恢复或管理 Codex 自动化。

### 3.2 Codex 自动入口

Codex 保留以下责任：

- `codex_execute/codex`；
- `codex_review/codex` 和所有独立复审；
- 队列维护、事实纠偏和恢复治理；
- 跨系统架构锁定、用户决定与主工作区语义冲突裁决；
- DeepSeek 返工证据不足时的诊断和重新拆卡。

Codex 不获得隐藏复审优先级，也不领取 `external_execute/deepseek`。DeepSeek 转换后的复审任务保留原队列逻辑位置，Codex 仍选择队列中第一项属于自己的合法任务。

## 4. Cowork canary 结论与固定 Windows 入口 canary

### 4.1 已否决的 Cowork Scheduled 路径

2026-08-02 已在 Claude Desktop `1.24012.9` 中手动运行只读 Cowork Scheduled canary，得到以下事实：

1. 会话运行在隔离 Linux VM，项目通过挂载可读，但不是 Windows 主机进程；
2. VM 内没有 `pwsh`，不能运行项目规定的 PowerShell 7 检查；
3. VM 看不到主机用户级私有 runtime，不能对真实运行状态执行统一 `Show`；
4. 主机 Git 没有出现 Cowork 创建的 linked worktree；
5. 会话记录虽显示 DeepSeek V4 Flash 模型名，但没有独立证据证明实际 gateway 端点；
6. canary 没有读取生产队列、领取任务、修改项目、暂存或提交。

这与 Cowork 使用隔离 VM 的官方架构说明一致：[Claude Cowork architecture overview](https://support.claude.com/en/articles/14479288-claude-cowork-architecture-overview)。Claude Code Desktop 的 Local Routine 文档描述的是另一套产品表面：[Claude Code Desktop scheduled tasks](https://code.claude.com/docs/en/desktop-scheduled-tasks)，当前用户界面没有暴露可满足本项目要求的 Local + Worktree 配置。因此本设计不再依赖 Desktop、Cowork 或其会话生命周期。

### 4.2 固定 Windows 入口 canary

生产启用前，先手动对 `tools/invoke-deepseek-hourly.ps1` 运行私有状态 canary。canary 只允许使用专用状态目录和 canary action，不读取或领取生产队列、不取得生产 claim、不修改 `master`，并必须证明：

1. 入口实际由 Windows PowerShell 7 执行，Git 与至少一个只读项目检查可运行；
2. 固定 Claude CLI／DeepSeek gateway 启动器返回可核验的 provider、endpoint 类别和 `deepseek-v4-flash` 模型身份，凭据不写入日志或仓库；
3. DeepSeek 能读取 `AGENTS.md`、`CLAUDE.md` 和项目规则；
4. 项目脚本在 `D:\天章游戏开发\.worktrees\automation\<runId>\deepseek` 创建 linked worktree，并能核验 canonical 仓库、`master` HEAD、当前 worktree 与分支；
5. runtime 工具能在 canary 私有状态目录完成无生产 claim 的 `Show`；
6. 主工作区 HEAD、index 和工作树不因 canary 改变，也不会把 automation worktree 显示为新的未跟踪路径。

canary 可以只在精确命名的 canary worktree／branch 创建测试提交。仅当该 worktree 干净、路径核验位于上述 automation 根目录且没有 runtime 引用时，固定入口才可删除它；不得泛化清理其他 worktree。为提供 canary 能力，可以先在隔离实施分支修改脚本、测试和文档，但 canary 通过前不得启用新生产 runtime 或两个生产 automation。身份、工具、worktree、私有状态或主工作区隔离任一项不成立，就停止迁移，不添加兼容层绕过。

## 5. 两个独立小时入口

### 5.1 Codex 小时入口

现有 Codex 自动化保留一个本地时区每小时 `:15` 的入口。每轮：

1. 调用统一 `Show`，读取两个活动 taskId、Codex 自身 run 和短时集成租约；
2. Codex run 已存在时只处理该 run，不选择新任务；
3. 无 Codex run 时，从共享队列选择第一项合法 Codex 任务并原子 claim；
4. 在 Codex 独立 worktree 中实施、验证并生成候选提交；
5. 通过固定集成入口预构建正式提交序列，最终短时集成；
6. 不调用 DeepSeek 固定入口，不读取 DeepSeek 私有会话，也不管理 `deepseek-hourly-trigger`。

### 5.2 DeepSeek 小时入口

固定 Windows 入口 canary 通过后创建第二个 Codex automation：

- 名称：`deepseek-hourly-trigger`；
- 项目目录：`D:\天章游戏开发`；
- 频率：每小时一次，独立于 `codex-hourly-worker`；
- 唯一动作：前台调用 `pwsh -NoProfile -ExecutionPolicy Bypass -File "D:\天章游戏开发\tools\invoke-deepseek-hourly.ps1" -Action RunOnce -RepositoryRoot "D:\天章游戏开发" -OutputJson`，等待退出并原样报告结构化终态。

薄触发器不得读取队列或任务卡、选择或 claim 任务、解释 DeepSeek 正文、判断审核结果、拼装任意外部命令、启动后台进程，或实施／恢复业务。固定脚本才执行以下闭环：

1. 调用统一 `Show`；已有 DeepSeek run 时只处理该 run，不领取第二项任务；
2. 无 DeepSeek run 时，从共享队列确定性选择第一项 `external_execute/deepseek/ready` 并原子 claim；
3. 在 `D:\天章游戏开发\.worktrees\automation\<runId>\deepseek` 创建项目拥有的 linked worktree；
4. 通过 canary 已核验的固定 Claude CLI／DeepSeek gateway 启动器运行 DeepSeek V4 Flash，并等待前台进程退出；
5. 校验结构化 `candidateResult`、candidate 提交、路径上界和 worktree 清洁状态；
6. 按第 9 节执行 canonical 预构建、短时集成与关闭，或返回 `occupied`、`no_candidate`、`attention_required` 等固定终态。

两个 automation 在触发与业务运行上互不等待，可以重叠。这个选择消除了 Codex 业务轮次启动 DeepSeek 的依赖，但 DeepSeek 的时钟触发仍依赖 Codex automation 服务，并非平台级故障域独立；这是已接受的简化取舍。触发延迟、电脑休眠或 automation 服务不可用只形成真实缺跑，不新增 Windows Task Scheduler、补跑守护进程或重试层。

### 5.3 固定 DeepSeek 工作提示词

```text
你是《天章》项目的 DeepSeek V4 Flash 实施责任方。本轮任务已经由项目固定
Windows 入口选择并原子领取。输入只包含 taskId、runId、RepositoryRoot、
Worktree、baseCommit 和要求的结构化输出路径。

进入指定 Worktree 后，依次读取根 AGENTS.md、CLAUDE.md、
开发管理/自动工作流规则.txt、开发管理/AI协作规则.txt 和
开发管理/DeepSeek工作提示词.txt。先按项目规则核验实际 provider、gateway
类别和模型身份；身份不符立即停止并返回结构化失败。

只执行已分配的 taskId。不得重新扫描队列、另选任务、claim、修改 runtime，
也不得启动、暂停或管理 Codex automation。开发、验证和 candidate 只发生
在指定 Worktree，并受任务 expectedPaths、停止条件和验收约束。

完成后创建单一 candidate 提交并写出与 taskId、runId、baseCommit、candidate SHA
精确绑定的 candidateResult。不得修改主工作区、共享任务投影或 handoff，
不得执行正式集成，也不得自审；这些都由固定 Windows 入口机械完成。

发生决定等待、冲突、身份或证据不完整时，保留现场并如实返回结构化结果；
不得猜测恢复、拼装主工作区 Git 命令、扩大路径或伪造验证结果。
```

automation prompt 只保存第 5.2 节的固定命令；本节工作提示词由固定入口从项目事实源加载并注入已领取参数。任务选择、认领、恢复、提交、审核与验证规则只保存在项目事实源，避免长 prompt 双份漂移。

## 6. 单队列选择与原子 claim

Codex 业务入口与固定 DeepSeek Windows 入口都从 `开发管理/当前任务队列.txt` 的已保存顺序扫描，以同 ID 任务卡为唯一业务事实。Codex 只考虑 `codex_execute/codex/ready`、`codex_review/codex/ready` 以及队列为空时的维护；固定 DeepSeek 入口只考虑 `external_execute/deepseek/ready`。`deepseek-hourly-trigger` 本身不扫描队列。

选择和 claim 规则：

1. 跳过另一责任方的行，在本责任方内部保持原相对顺序；
2. 依赖未完成、内容未冻结、队列与任务卡不一致或 route／owner 不匹配时不得 claim；
3. 不以路径重叠阻止 worktree 开发；显式依赖由 `blockedBy` 表达，潜在代码组合风险在最新 `master` 上重放并重跑验收；
4. claim 由固定 runtime 入口在同一个命名 mutex 内原子完成；owner run 非空或 taskId 已出现在任一 run 时失败关闭；
5. claim 保存所见 `master`、任务卡摘要、worktree 和分支；不修改任务卡、队列或主工作区；
6. `Show` 必须返回两个活动 taskId。手动 Codex／DeepSeek／Claude 在选题和合并前都必须跳过活动 taskId，不能只检查 `integrationLease` 是否为空；
7. 临时无候选只结束本轮，不写任务事实；明确 blocker 仍按任务维护规则写回非 ready 状态；
8. `owner=claude` 或其他未配置责任方不由这两个入口自动领取。

不同 owner 可以同时 claim 不同 taskId。claim 只防重复执行，不建立第二套队列，也不改变原队列顺序。

## 7. worktree 与路径合同

业务实施和候选验证只发生在 owner worktree。必须满足：

- candidate 实际 changed paths 是任务 `expectedPaths` 的子集；
- 不访问或修改另一责任方的 worktree；
- 不直接修改主工作区业务文件、Git index 或当前分支；
- 不 stash、reset、clean、覆盖人工改动或自动解决主工作区冲突；
- candidate 使用单一、可达且父链可核验的提交，worktree 在交付 candidate 时干净；
- canonical 主分支固定为 `master`；claim、candidate 和集成均核验该分支，不把“当前任意分支”当作主分支。

任务卡、当前队列、同卡归档、`sourceBacklog` 和 DeepSeek handoff 属于集成阶段的共享投影。责任方可以在自己的 candidate 中提出这些修改，但固定集成入口不直接信任候选版本：它从最新 `master` 恢复这些路径，再按实际结果机械重建。`开发管理/AI合作沟通.txt` 继续只由 DeepSeek 的正式 handoff 提交按既有协作规则修改，不并入业务 `expectedPaths` 上界。

路径重叠的两个任务可以同时开发。第一个正式集成后，第二个必须在新 `master` 上重新预构建并运行任务验收；发生文本冲突、任务事实变化或验收失败时保留候选并停止，不引入提前路径图、`workerPaths` 字段或自动冲突解决器。

DeepSeek worktree 由固定 Windows 入口创建在 `.worktrees/automation/<runId>/deepseek`，并由 runtime 记录所有权；Codex owner 使用同一 automation 根约定下自己的 owner 子目录。candidate 交付后，固定集成入口可以在同一 owner worktree 内创建可由 runId 验证的 canonical branch，但必须保留 candidate branch 和提交证据，不把该 branch 操作扩展到主工作区。正常运行不自动删除 worktree；只有 run 已关闭、证据已保留、目标路径与 runId 精确匹配且 worktree 干净时，才允许显式清理。

## 8. 最小私有 runtime

沿用现有用户级私有状态目录、ACL、原子写入和命名 mutex。runtime 的业务协调面只包含：

```text
runs.codex
runs.deepseek
integrationLease
```

每个 owner run 最多一个，字段固定为：

- `runId`、`taskId`、`route`、`owner`；
- `mainBranch=master`、`baseCommit`、任务卡摘要；
- `worktree`、`candidateBranch`、可空的 `canonicalBranch`；
- 可空的 `candidateCommit`、`canonicalBase`、`canonicalHead`；
- 可空的 `candidateResult`；
- `sessionKind` 与可空的 `sessionId`；DeepSeek 固定为 `sessionKind=claude_cli`，`sessionId` 只保存固定启动器可核验返回的值；
- `state`、`startedAt`、`updatedAt`、可空的 `recoveryReason`。

`state` 只允许：

```text
developing
candidate_ready
canonical_ready
integrated
attention_required
```

这五个状态对应真实的不可混淆边界，不保存 batch、lane、通用 worker 配置、队列镜像、阶段日志、心跳、阻塞计数或自动暂停状态。

`candidateResult` 复用现有正式提交与通知所需的事实合同，只保存固定字段：结果类别、预期任务转换、实际 changed paths、已验证、未验证、残留风险以及 finalizer 所需的结果／影响／验证／通俗说明。固定集成入口只消费这个结构化合同，不从模型正文猜测审核结论、任务转换或提交元数据。

`integrationLease` 只保存 `runId`、owner、taskId、所见 `master` HEAD 和短过期时间。所有 `Show`、claim、run 状态更新、lease 取得／释放与结果记录都经同一固定脚本和同一命名 mutex，禁止 Codex 业务入口与固定 DeepSeek Windows 入口直接读改写 runtime JSON。

过期 lease 不允许接着写。新尝试必须从 `master`、任务卡、队列、人工修改和 canonical 提交全链重新核验。

## 9. 候选、正式提交预构建与原子集成

### 9.1 candidate

责任方完成实施和直接验证后，在 owner worktree 创建 candidate 提交，同时写入与该提交和 taskId 精确绑定的 `candidateResult`，再把 run 转为 `candidate_ready`。candidate 不是正式项目交付，不更新共享任务投影、不发送完成通知，也不进入日报统计。

### 9.2 在 owner worktree 的 canonical branch 预构建正式序列

固定集成入口读取 `candidate_ready` run 后：

1. 读取当前 `master` HEAD、任务卡、队列、依赖和人工 staged／unstaged／untracked 路径，记录为 `canonicalBase`；
2. 核验 owner worktree 干净、candidate branch 与 candidate SHA 全链一致；保留 candidate branch 后，在同一 worktree 创建该 run 专属的 canonical branch，基于 `canonicalBase`；
3. 以不保留 candidate 提交身份的方式把 candidate 业务改动应用到 canonical branch；
4. 从 `canonicalBase` 恢复共享任务投影，再按实际结果机械更新任务卡、队列、backlog、归档或审核记录；
5. 核对 `candidateResult` 的结果类别、预期转换、验证事实和提交元数据与任务生命周期相容；
6. 在 canonical branch 上运行任务卡要求的直接验收、任务后置条件、路径检查、空白检查与 Git 检查；
7. 通过现有路径限定 finalizer 生成正式提交序列；
8. 核验提交父链、元数据、实际路径和任务投影后保存 `canonicalHead`，把 run 转为 `canonical_ready`。

这一步不取得 `integrationLease`，也不修改主工作区。另一个责任方可以同时开发或预构建自己的结果。若 candidate 在最新 `master` 上冲突、任务事实已经变化或验证失败，则固定入口只中止 owner worktree 内的 canonical 操作，run 转为 `attention_required`，保留 owner worktree、candidate branch、candidate SHA 和失败证据，不自动修冲突或叠加重试层。

### 9.3 短时 fast-forward

`canonical_ready` run 最终集成时：

1. 取得 `integrationLease`；失败时保留 `canonical_ready`，本轮结束；
2. 重新核对主工作区当前分支精确为 `master`、其 HEAD 仍等于 `canonicalBase`，任务卡摘要、route、owner、状态、依赖和队列投影均未变化；
3. 核对主工作区与正式提交涉及的路径没有人工 staged、unstaged 或 untracked 冲突；无关人工修改可以保留；
4. 核对 `canonicalBase..canonicalHead` 是允许的连续提交序列，业务提交不超出 `expectedPaths`，handoff 只修改既有协作事实源；
5. 在主工作区只执行一次 `git merge --ff-only <canonicalHead>`；不得在主工作区应用未提交补丁、机械改文件、跑会产生写入的业务生成步骤或创建 merge commit；
6. 核验主工作区 HEAD、任务后置条件和 index 隔离后，把 run 标记为 `integrated`；
7. 调用单个原子 `CompleteRun` 动作记录结果、清除该 owner run 并释放同一 `integrationLease`，再发送一次任务结果通知。通知失败只记录脱敏投递结果，不回滚提交、不重试业务、不恢复 run。

如果第 2～4 步不成立，主工作区保持不变，释放 lease；仅 `master` 已前进且 candidate 仍可重建时回到 `candidate_ready`，由下一轮从新 HEAD 预构建一次。真正的文本冲突、任务事实冲突、身份不符或证据不完整进入 `attention_required`。

生产启用前必须用自动测试证明：所有预期的 fast-forward 拒绝场景都保持主工作区 HEAD、工作树和 index 不变。任何一次失败后主工作区出现新修改都属于停止条件，不增加自动回滚层掩盖。

## 10. 正式提交与 Codex 复审

### 10.1 Codex 执行与维护

Codex execute 的 canonical 序列通常只有一个路径限定正式提交，包含业务修改与该任务的机械投影。Codex review 按审核入口形成审核闭环或返工状态；QueueMaintenance 使用第 11 节的特殊合同。它们都通过同一预构建与 fast-forward 边界进入 `master`。

### 10.2 DeepSeek 执行

DeepSeek 的 canonical 序列固定为两个连续提交：

1. `businessCommit`：candidate 业务修改、直接验证记录以及任务转换为待复审所需的共享投影；
2. `handoffCommit`：只更新 `开发管理/AI合作沟通.txt`，登记真实 business SHA、已验证、未验证和残留风险，不带 Automation 业务产出标记。

两者都在 owner worktree 的 canonical branch 中生成并核验，再作为同一 fast-forward 一次进入 `master`。原任务卡转换为：

```text
route=codex_review
owner=codex
dispatchState=ready
```

`pending_review` 不等于完成。Codex 复审 `master` 中实际组合后的 business／handoff 序列：

- 通过：创建审核闭环提交，归档原任务并解锁依赖；
- 返工：同一任务带审核证据退回 `external_execute/deepseek/ready`；
- 需要决定或明确阻塞：转为相应非 ready 状态；
- 不允许 Codex 在 `codex_review` 中直接改写 DeepSeek 业务实现后自行批准。

## 11. QueueMaintenance 特殊合同

QueueMaintenance 没有任务卡，因此不使用 `expectedPaths` 或虚构 `workerPaths`。它只在共享队列真实为空、Codex run 为空且不存在待集成结果时，由 Codex 以保留 ID `QUEUE-MAINTENANCE` 原子 claim。

维护候选只能修改 `开发管理/状态与建议维护规则.txt` 已授权的任务卡、队列、分线 backlog 和必要状态事实；继续使用现有全局投影检查与 `readyCount` 分类。它在 Codex worktree 中形成维护结果，再在同一 worktree 的 canonical branch 基于最新 `master` 重放、检查并生成单个正式提交；零候选且无事实变化时不制造 candidate、提交或 recovery。

DeepSeek 无候选不会触发 QueueMaintenance，DeepSeek 也不能 claim `QUEUE-MAINTENANCE`。

## 12. 恢复与失败边界

两个入口只处理自己的 owner run；一个 owner 的异常不阻止另一个 owner 开发或集成不同任务。

### 12.1 Codex

- `developing` 且存在唯一可核验 Codex session：通过现有 runner 恢复原 session；
- `candidate_ready`：不重新开发，重新预构建 canonical；
- `canonical_ready`：若 canonical 尚未进入 `master`，只尝试最终重新核验与 fast-forward；若 `canonicalHead` 已可证明为当前 `master` 的祖先且任务后置条件成立，只补记 `integrated`；
- `integrated`：只完成尚未记录的结果关闭，不重复提交或业务通知；
- 证据不一致：转为 `attention_required`。

### 12.2 固定 Windows 入口／DeepSeek

新的 `deepseek-hourly-trigger` 每次只重新调用同一固定入口；恢复按 runtime、进程、worktree 和提交证据进行，不依赖 automation 对话：

- `developing` 且原固定启动器仍有唯一可核验的活动进程／session：返回 `occupied`，不启动第二个 DeepSeek，也不领取新任务；
- `developing` 且原启动器已结束、没有 candidate：若 worktree 干净且没有 blocker，可按“无候选”原子关闭 run；若有未提交修改、权限／决定等待或 session 证据不一致，则保留 worktree 并转为 `attention_required`；
- 不自动恢复 `developing` 的模型对话。将来只有固定启动器能以唯一 sessionId 证明恢复不会重复业务时，才可另行设计并复核；本方案不实现该分支；
- `candidate_ready`：不重新运行 DeepSeek；固定入口在原 project-owned worktree 中核验 candidate SHA、branch、路径和结构化结果后继续 canonical 预构建；
- `canonical_ready`：若 canonical 尚未进入 `master`，只尝试最终重新核验与 fast-forward；若 `canonicalHead` 已可证明为当前 `master` 的祖先且任务后置条件成立，只补记 `integrated`；
- `integrated`：只完成结果关闭，不重复提交或业务通知；
- 进程、session、worktree、candidate／canonical branch、提交或 runtime 任一证据不一致：转为 `attention_required`。

`attention_required` 不自动过期、不自动清理、不领取同 owner 下一项任务，也不连续重试。只有 Codex 普通管理上下文或用户明确授权后才能修复、取消或释放该 run。

`attention_required` 是尚未收尾的运行恢复事实，不代替任务卡 blocker。若核验后确认问题已经构成任务级阻塞，Codex 普通管理上下文必须按现有维护规则把 blocker 写回任务卡与队列投影，再关闭该 run；不得让明确 blocker 长期只存在于私有 runtime。

## 13. 项目事实源同步范围

实施时同步修改以下事实源，避免固定入口、手动流程和两个 Codex automation 各说一套：

- `AGENTS.md`：手动选题与合并从“只看全局 lease”改为同时检查两个活动 taskId 和 `integrationLease`；补项目拥有的 automation worktree 边界。
- `CLAUDE.md`：自动 DeepSeek 入口改为只由 `deepseek-hourly-trigger` 调用的固定 Windows wrapper；保留身份锚定、不得自审和手动明确任务例外。
- `开发管理/DeepSeek工作提示词.txt`：DeepSeek 接收已经领取的 taskId、runId 和 worktree，不扫描队列或 claim；保留实际 provider／模型身份核验、candidate 与严格结构化结果合同。
- `开发管理/AI协作规则.txt`：把 `deepseek-hourly-trigger` 定义为非业务责任方，把固定 Windows 入口／DeepSeek 定义为 `external_execute/deepseek` 责任链，明确 Codex 复审边界。
- `开发管理/自动工作流规则.txt`：定义两个独立 automation、固定 DeepSeek 入口、双 run、原子 claim、owner worktree canonical branch 和短时 fast-forward；删除长业务租约和 Codex 业务轮次启动外部 wrapper 的生产路径。
- `开发管理/自动工作流恢复规则.txt`：分别定义 Codex session 恢复与固定 DeepSeek 入口的证据恢复，不把新的 automation 轮次写成模型对话 resume。
- `开发管理/自动工作流控制器提示词.txt`：只保留 Codex route、Codex run、QueueMaintenance 与 Codex 恢复；跳过外部 route。
- `tools/invoke-deepseek-hourly.ps1` 与实时 `deepseek-hourly-trigger`：前者保存业务闭环，后者只保存第 5.2 节的固定命令和 schedule。

生产 prompt、schedule、folder、model 和 enabled 状态继续以各自实时自动化配置为准，不复制到第二个仓库配置文件，也不直接编辑 Codex automation TOML。

## 14. 安全不变量

- `codex-hourly-worker` 不能启动 DeepSeek；只有独立 `deepseek-hourly-trigger` 可以调用精确固定的 `tools/invoke-deepseek-hourly.ps1`。
- `deepseek-hourly-trigger` 不得读取业务事实、claim、实施、复审或解释模型正文，也不得把固定入口替换为任意命令执行器。
- 固定 DeepSeek Windows 入口和 DeepSeek 模型不能启动、暂停、恢复或管理 Codex automation。
- 两个入口不能领取对方 route／owner，同一 taskId 不能同时出现在两个 run。
- 自动和手动入口都必须通过统一 `Show` 看见活动 taskId；私有 claim 不能成为手动流程不可见的隐藏占用。
- 业务开发与正式预构建只写 owner worktree 中相互可核验的 candidate／canonical branch。
- 主工作区只在有效 `integrationLease` 下执行已核验提交序列的一次 fast-forward。
- candidate 业务变化不超出 `expectedPaths`；正式业务提交不超出任务授权，handoff 只写既有协作事实源。
- `master`、任务事实、依赖、人工相关改动或 canonical 父链变化都会阻止集成。
- DeepSeek 不自审；`pending_review` 不解锁依赖、不作为完成产出重复统计。
- 不建立中央 batch、第二套队列、通用 lane、第三 AI 占位、自动冲突解决器、重复重试层或长期双启动兼容路径。
- provider 凭据、完整私有 API 地址、automation 私有权限和用户标识不写入项目 runtime、任务卡或仓库日志。

## 15. 验证矩阵

### 15.1 Canary 与触发

1. 手动固定 Windows 入口 canary 在不读取生产队列、不写 `master` 的情况下核验真实 DeepSeek provider／模型身份、项目规则、PowerShell 7、Git、project-owned worktree 和 canary 状态目录。
2. `codex-hourly-worker` 与 `deepseek-hourly-trigger` 分别按小时触发，运行区间可重叠；晚启动、休眠或 automation 服务不可用只产生真实运行历史，不触发补跑层。
3. Codex 业务 worker 不调用 DeepSeek；薄触发器只调用固定脚本且不读取队列；固定脚本负责 DeepSeek 闭环，DeepSeek 不调用或管理 Codex。

### 15.2 选题与 claim

1. 队首属于另一 owner 时，本责任方选择自己的第一项合法任务。
2. 同一 owner 多项 ready 时保持原相对顺序。
3. 同 taskId 的第二次自动或手动 claim 被拒绝；`Show` 对所有入口返回相同活动 taskId。
4. 两个不同 taskId 可分别由 Codex 与 DeepSeek 同时 claim；runtime 并发写不会丢失另一 run。
5. QueueMaintenance 只在其特殊条件成立时由 Codex claim。

### 15.3 worktree 与 candidate

1. 两个任务即使路径重叠，也只能修改各自 worktree；不会直接污染主工作区。
2. candidate 实际路径超出 `expectedPaths` 时拒绝进入 canonical 预构建。
3. DeepSeek worktree 位于 `.worktrees/automation/<runId>/deepseek`，由项目 runtime 精确记录且不出现在主工作区未跟踪状态中。
4. 任一 owner 失败不会清理另一 owner 或不属于本 run 的 worktree。

### 15.4 canonical 与主工作区原子性

1. candidate 在最新 `master` 上以 no-commit 方式重放，共享任务投影从最新事实机械重建。
2. 冲突、任务事实变化或验证失败只影响 owner worktree 的 canonical branch，主工作区 HEAD、工作树和 index 不变。
3. 两个 canonical 同时完成时只有一个取得 `integrationLease`；第一个 fast-forward 后，第二个因 `canonicalBase` 过期而回到 `candidate_ready`。
4. 主工作区无关 staged、unstaged、untracked 修改不被覆盖、暂存、提交或清理；相关路径修改阻止 fast-forward。
5. DeepSeek business／handoff 两提交一次进入 `master`，不存在只进入第一提交的生产中间态。
6. fast-forward 后结果记录中断可由 `integrated` 状态只做关闭，不重复提交或通知。

### 15.5 复审与恢复

1. DeepSeek 正式结果把原任务转换为 `codex_review/codex/ready`，审核通过前不解锁依赖。
2. Codex 复审读取 `master` 实际组合，不用旧 candidate 代替。
3. 新的 DeepSeek 触发轮次只能机械处理 `candidate_ready`／`canonical_ready` 的可核验证据，不能恢复旧对话、重跑业务或接管未提交修改。
4. 权限等待、决定等待、证据冲突和无法接管的旧 worktree 进入 `attention_required`，不自动重试或领取同 owner 下一项。
5. DeepSeek 无法领取 `codex_review`，Codex 不在审核中改后自批。

## 16. 部署、回滚与停止条件

### 16.1 部署顺序

0. 记录第 4.1 节 Cowork canary 的否决结论，不创建 Desktop／Cowork 生产任务。
1. 暂停现有 Codex 每小时控制器，确认旧 lease、recovery、外部 wrapper、业务进程和未完成提交均为空；不得迁移活动运行。
2. 先更新 runtime 原子合同与统一 `Show`／claim，并完成双进程、手动重复领取和迁移测试；此时不启用新生产入口。
3. 实现 `tools/invoke-deepseek-hourly.ps1`、project-owned worktree、owner canonical branch、原子 `CompleteRun` 与 fast-forward 拒绝测试；若必须增加主工作区回滚层或第三 worktree 协调层就停止并重新审查。
4. 同步第 13 节项目事实源和两个 automation prompt 合同；不新增任务卡字段，不批量迁移任务卡。
5. 按第 4.2 节手动运行固定 Windows 入口的私有状态 canary；未通过时不得启用任一生产入口。
6. canary 通过后先仅启用／手动运行 Codex 新入口，使用专用 fixture 或一项低风险任务完成 claim、candidate、canonical、fast-forward 和恢复验证。
7. 通过实时 automation 管理能力创建 `deepseek-hourly-trigger`，初始保持禁用或手动触发；`Run now` 完成 DeepSeek 单责任方 candidate、正式 business／handoff 双提交和 Codex 待复审投影 canary。
8. 分别验证手动任务不会重复领取活动 taskId，再用两个无依赖低风险任务验证并行开发和串行 fast-forward。
9. 通过实时 automation 管理能力启用两个 Codex automation；不得直接编辑 Codex automation TOML，也不创建 Windows Task Scheduler 或 Desktop Routine。
10. 观察至少两个真实小时周期，确认身份、选题、恢复、通知和复审投影一致。
11. 确认没有生产引用、旧 recovery 或测试依赖后删除旧通用 external wrapper 生产入口；允许固定 DeepSeek 启动器作为新脚本的内部依赖，但不保留第二条业务启动路径。

### 16.2 回滚

启用后出现系统性错误时，通过实时 automation 管理能力同时暂停 `codex-hourly-worker` 与 `deepseek-hourly-trigger`，并保留所有 worktree、candidate、canonical 和 runtime 证据。只有 `integrationLease` 为空、没有 `integrated` 未关闭 run、主工作区状态已核验且旧 runtime 无活动恢复时，才允许以单个 Git 回滚切片恢复旧控制器并重新启用；不得在代码中长期保留新旧双 runtime 或双启动兼容分支。

### 16.3 停止条件

出现以下任一情况立即停止启用并保留现场：

- 固定 Windows 入口 canary 无法证明实际 DeepSeek provider／模型身份、project-owned worktree、私有状态隔离或 PowerShell／Git 能力；
- `deepseek-hourly-trigger` 需要读取业务事实、解释模型正文、拼装任意命令或承担固定脚本之外的恢复判断；
- 自动或手动入口仍能重复领取活动 taskId，或两个 runtime 写入发生丢失；
- 任一责任方在开发阶段写主工作区；
- candidate 或正式提交突破路径上界；
- canonical 失败或 fast-forward 拒绝后主工作区出现新修改；
- DeepSeek 结果绕过 Codex 复审、business／handoff 只进入一半或提前解锁依赖；
- 新触发轮次接管未提交旧现场、伪装恢复模型对话或反复自动重试冲突；
- 必须增加兼容分支、第二套 runtime、自动回滚层、额外重试层或中央协调器才能继续。

## 17. 完成标准

- `codex-hourly-worker` 与 `deepseek-hourly-trigger` 独立按小时触发；Codex 业务 worker 不启动外部 AI，薄触发器只调用固定 Windows 入口。
- 固定 Windows 入口 canary 和真实运行记录能证明实际 DeepSeek provider／模型身份、权限、project-owned worktree 与工具能力。
- Codex 业务入口与固定 DeepSeek Windows 入口从同一队列各领自己的第一项合法任务；薄触发器不读队列，统一 `Show` 对业务自动入口和手动入口公开活动 taskId。
- 业务开发只发生在 owner worktree；路径重叠不会直接污染主工作区。
- candidate 在 owner worktree 的 canonical branch 基于最新 `master` 形成完整正式提交序列并通过验收。
- 主工作区只通过短时 `integrationLease` 下的一次 fast-forward 更新；所有预期拒绝路径都保持 HEAD、工作树和 index 不变。
- DeepSeek business／handoff 连续提交一次进入 `master`，随后由 Codex 复审实际集成结果；审核前不解锁依赖。
- 固定 DeepSeek 入口的证据恢复、Codex session 恢复和 `attention_required` 边界均可核验，不重复业务执行或通知。
- QueueMaintenance 不依赖虚构任务卡字段，仍保持现有零候选和事实来源边界。
- 没有新增 `workerPaths`、中央 batch、通用 lane、第三责任方、精确分钟硬门槛或长期双启动兼容层。
- 项目规则、两个实时 Codex automation 与固定 DeepSeek Windows 入口对身份、选题、claim、恢复、集成与复审的描述一致。
