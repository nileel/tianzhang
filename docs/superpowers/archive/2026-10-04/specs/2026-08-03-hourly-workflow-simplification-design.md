# Codex / DeepSeek 小时工作流精简设计

> 日期：2026-08-03
> 状态：设计已获负责人批准，按本文实施
> 目标：保留 Codex 与 DeepSeek 的并行吞吐，同时优先降低故障面和维护成本

## 1. 背景与已确认事实

当前方案已经实现两个 owner 独立领取、owner worktree、candidate、canonical、短时集成和 Codex 独立复审，但控制面出现了明显的重复和事故修复负担：

- `tools/invoke-codex-hourly.ps1` 与 `tools/invoke-deepseek-hourly.ps1` 分别约 321 行和 549 行，22 / 24 个函数中有 19 个同名机械函数；
- 双入口方案合入后的一天内，连续出现 Unicode JSON、attention closeout、入口并发、元数据合同、恢复过度设计、复审路径、触发超时和候选临时文件等修复；
- runtime 的阶段本身有人工诊断价值，复杂度主要来自跨自动轮次恢复、重放和事故专用迁移；
- 普通飞书通知实际通过 REST API 发送，却受长连接桥健康门禁控制，形成多次 `CHANNEL_UNAVAILABLE`；
- 两个实时 automation 的提示词曾分别出现返回值 `.map()`、超时、memory 和额外 `::inbox-item` 等漂移。

用户确认以下优先级与约束：

1. 同时追求降低故障、降低维护成本和缩短任务链路；冲突时优先稳定与易维护。
2. 必须保留 Codex 与 DeepSeek 同时开发不同任务的能力，不退回全局单业务任务。
3. 风险 worktree 不自动合并；自动流程停止后保留现场，用户回来逐项处理。
4. 保留详细阶段状态以支持人工判断，但取消跨自动轮次的自动恢复。
5. DeepSeek 正式结果先以 `pending_review` 进入 `master`，后续由 Codex 独立复审。
6. 开发中主动发现需要决定时，允许唯一一条受控 checkpoint 暂停与回复后恢复路径。

## 2. 设计目标与非目标

### 2.1 目标

- 两个 owner 各自最多一个活动业务 run，可以并行实施和验证；
- Git、runtime、路径、任务转换、集成和通知只保留一套机械实现；
- 成功路径在同一次前台调用中闭环；
- 中断后的新自动轮次只报告旧 run，不继续、不重跑、不领取同 owner 新任务；
- 冲突、验证失败、范围外文件和证据异常均保持主工作区不变；
- 正常成功的 worktree 自动安全清理，风险现场永不自动清理；
- 回复已核验的等待事项可以由对应 owner 在新会话中继续。

### 2.2 非目标

- 不建立通用 worker pool、动态 lane、第三责任方或第二套队列；
- 不恢复旧模型对话；
- 不为崩溃、冲突、残留或验证失败建立 candidate 收养、自动重试或事故专用状态迁移；
- 不让模型直接修改 runtime、主工作区或自动化配置；
- 不以通知成功作为业务提交成功的前置条件；
- 不迁移或猜测接管当前已有的活动 run。

## 3. 总体架构

继续保留两个实时 automation：

- `codex-hourly-worker`：每小时 `:15`，调用共享入口并传入 `owner=codex`；
- `deepseek-hourly-trigger`：每小时 `:45`，调用同一共享入口并传入 `owner=deepseek`。

新共享入口固定为：

```text
tools/invoke-hourly-owner.ps1
  -Owner codex|deepseek
  -Action RunOnce|Canary
  -RepositoryRoot <absolute path>
  -Model <Codex automation 实际 model，仅 codex 必需>
  -OutputJson
```

共享入口由确定性的 PowerShell 代码完成：

```text
Show → 选题 → ClaimRun → owner worktree → owner candidate
→ candidate 核验 → 集成排他锁 → 最新 master 重放
→ 组合验证 → 正式提交 → fast-forward → CompleteRun
→ 通知 → 成功清理
```

模型只承担 candidate 阶段：

- Codex adapter 启动 Codex candidate，允许 `codex_execute`、`codex_review` 和 QueueMaintenance；
- DeepSeek adapter 启动 DeepSeek V4 Flash，只允许 `external_execute/deepseek`；
- adapter 只返回统一结构化 candidate 合同，不读写 runtime、不集成、不管理 automation。

两个 owner 可以并行形成 candidate。正式集成使用自动与手动流程共用的进程持有型排他锁；锁只在当前进程存活期间存在，进程退出或崩溃即由操作系统释放，不保存可过期的长期业务租约。所有项目 worktree 的正式集成都必须通过同一集成入口取得该锁，主工作区最终仍在 fast-forward 前重新核验 HEAD、任务事实和相关路径。

## 4. 共享内核与 owner adapter

### 4.1 共享内核唯一职责

共享内核统一实现：

- runtime `Show`、原子 claim 和状态写入；
- 队列顺序、route、owner、依赖和任务卡摘要校验；
- owner worktree 与 branch 的确定性路径；
- candidate SHA、父链、changed paths、工作树清洁度和 candidateResult 校验；
- 最新 `master` 上的业务重放和任务生命周期投影；
- 组合验证、提交元数据、路径上界和 Git 后置条件；
- 排他集成、fast-forward、run 关闭、通知和成功清理。

Codex / DeepSeek 不再各自实现 Git、JSON 工具、路径重叠、candidateResult 写入、canonical 构建、状态路径、通知或集成函数。

### 4.2 owner adapter 唯一差异

owner adapter 只定义：

- 可领取的 route；
- 模型启动器和身份合同；
- candidate 提示词与输出 schema；
- 成功后的任务转换；
- 该 owner 必需的直接验证入口。

Codex 任务可以转换为 `completed`、`blocked`、`pending_decision` 或 `waiting_reply`；Codex 复审可以通过关闭或把同一卡退回 DeepSeek。DeepSeek 正常完成固定转换为 `codex_review/codex/ready`，不得自审或解锁依赖。

### 4.3 QueueMaintenance

QueueMaintenance 仍只属于 Codex adapter，但返回独立终态 `maintenance_completed`。它不伪装成业务 `completed`、不发送 TaskOutcome 飞书通知、零事实变化时返回 `no_candidate`。共享内核仍负责其 worktree、验证和集成，不另建第二条维护流程。

## 5. runtime、阶段与跨轮次行为

继续保留每个 owner 的详细运行证据状态：

```text
developing
candidate_ready
canonical_ready
integrated
attention_required
```

这些状态只描述现场停在哪一步，不再授权后续自动轮次继续处理。规则固定为：

1. 同一次前台调用可以依次写入并推进正常阶段；
2. 新的小时调用发现本 owner run 非空时，只返回 taskId、runId、state 和脱敏原因，不执行恢复、重放、关闭或新 claim；
3. 另一个 owner 不受影响，可以继续处理不同 taskId；
4. 人工处理通过普通 worktree 审核决定采用、返工或放弃，最后使用精确匹配 runId 与现场原因的关闭动作释放 owner；
5. 自动流程不删除 `attention_required` 或任何证据不一致现场。

run 至少保存：owner、taskId、runId、baseCommit、taskCardDigest、worktree、candidateBranch、当前阶段、可空 candidate SHA、可空 formal SHA、可空 candidateResult、更新时间和可空原因码。保留这些字段是为了人工可诊断性，不为自动恢复服务。

`integrationLease` 从持久 runtime 删除；集成互斥改由进程持有型排他锁承担。`Show` 需要报告锁是否正被持有，但不保存可过期 lease 或恢复分支。

## 6. 正常成功数据流

### 6.1 candidate

共享内核 claim 后创建 `.worktrees/automation/<runId>/<owner>`。owner adapter 在其中实施、验证并形成一个 candidate 提交。candidateResult 必须与 taskId、runId、baseCommit、candidate SHA、changed paths、验证和预期任务转换精确绑定。

### 6.2 最新 master 重放与验证

candidate 核验通过后，共享内核取得集成排他锁，再读取当时最新的 `master`、任务卡、队列、依赖和主工作区相关路径。正式结果只在 owner worktree 中基于这一最新 HEAD 重放；组合验证也在持锁阶段完成。

持锁阶段可能比当前 300 秒短租约更长，但只串行正式集成，不阻止另一 owner 在自己的 worktree 继续开发。这样以可预测的等待换取删除 canonical 过期、跨轮次重建和 lease 恢复逻辑。

### 6.3 正式提交

Codex execute / review 正常形成一个路径限定正式提交。

DeepSeek 从当前的 `businessCommit + handoffCommit` 精简为一个原子正式提交，同时包含：

- 已核验业务修改；
- 原任务转为 `codex_review/codex/ready` 的任务投影；
- `开发管理/AI合作沟通.txt` 的真实复审证据；
- Automation 元数据。

这删除双提交父链、只进入一半的防护和 `canonicalHead^` 特殊处理，同时仍让 Codex 从 `master` 读取独立复审事实。

### 6.4 集成、关闭与清理

正式提交验证通过后，主工作区只执行一次 fast-forward。随后写入 `integrated`、调用 `CompleteRun`、发送通知并关闭 run。

只有同时满足以下条件才自动清理：

- run 已成功关闭；
- formal commit 已在 `master`；
- worktree 干净；
- worktree、branch 和 runId 精确匹配；
- 没有其他 runtime 或人工流程引用目标。

满足时删除该 run 的 worktree 和临时分支。任一条件不成立都不清理。

## 7. 失败与人工处理

以下情况统一停止自动流程并保留现场：

- 模型中断、身份不符或输出不完整；
- worktree 存在范围外、未跟踪或未提交残留；
- candidate、父链、路径、元数据或验证证据不完整；
- 与最新 `master` 冲突；
- 组合验证失败；
- 任务卡、route、owner、依赖或队列已经变化；
- fast-forward 前主工作区相关路径存在冲突；
- fast-forward 后、run 关闭前发生中断。

共享内核记录准确阶段和原因码：仍在实施时使用 `attention_required`；已有合法 candidate 时保留 `candidate_ready` 并附失败原因；正式提交已预构建时保留 `canonical_ready`；已经 fast-forward 时保留 `integrated`。无论处于哪一状态，新的自动轮次都只报告，不推进。

人工处理可以检查 worktree、candidate/formal commit 和验证证据，然后选择：

- 接受并通过统一集成入口完成；
- 在原 worktree 返工后人工集成；
- 放弃结果并关闭 run；
- 将真实 blocker 写回任务卡后关闭 run。

不提供通用 candidate 收养 API、自动重跑、自动冲突解决或自动回滚。

## 8. 等待决定、等待回复与受控 checkpoint

### 8.1 任务生命周期状态

以下仍是任务卡／队列事实，而不是私有 run 的恢复状态：

```text
pending_decision
waiting_reply
blocked
frozen
pending_review
completed
```

没有业务修改的普通等待结果由共享内核形成一个任务状态提交、关闭 run并通知，不占用 owner。

### 8.2 开发中发现需要决定

这是唯一允许的受控暂停：

1. 模型立即停止继续猜测实现；
2. 只把当前已知合法修改整理为一个干净 checkpoint 提交，绝不进入 `master`；
3. candidateResult 记录决策 ID、问题、互斥选项、checkpoint SHA、baseCommit、branch、changed paths、已验证、未验证和残留风险；
4. 共享内核只把任务转为 `pending_decision` 或 `waiting_reply` 并提交决策信息，不合并 checkpoint 业务变化；
5. 当前 run 以受控暂停类别关闭，owner 可以领取其他任务；
6. 任务卡保存可核验的 checkpoint 引用，不保存私有凭据或任意绝对路径。

### 8.3 回复后的自动继续

飞书回复先按 decisionId、taskId、当前任务摘要和操作者身份验证。下一次对应 owner 的共享入口：

1. 机械消费已核验且未过期的回复；
2. 把原任务恢复为原 route／owner／`ready` 并按固定规则回到队列；
3. 从最新 `master` 创建新 run 和新 worktree，不恢复旧模型会话；
4. 精确核验 checkpoint commit、base、branch、路径授权和回复绑定；
5. checkpoint 能在最新 `master` 无冲突重放时，将其作为新会话的初始工作继续开发；
6. master、任务语义、路径授权或 checkpoint 证据不兼容时进入人工处理，不猜测合并。

checkpoint worktree 只有在新 run 成功吸收结果并满足正常安全清理条件后才删除。已经存在但尚未消费的有效回复必须在迁移中保留。

崩溃、残留、验证失败或普通冲突不适用此恢复例外。

## 9. 飞书通知与自动化展示

### 9.1 出站通知

普通 TaskOutcome、DailyReport 和 WeeklyReport 直接调用飞书 REST API，不再检查长连接桥 `health.json`。长连接桥只负责接收决策和文本回复。

继续保留稳定幂等键与 provider 明确确认：

- provider 返回可核验 message/chat identity 才记为 `PROVIDER_ACCEPTED`；
- 明确拒绝、结果未知或输入无效只记录脱敏失败；
- 通知失败不回滚业务、不恢复 run、不自动创建重试队列；
- QueueMaintenance 不进入 TaskOutcome 通知。

### 9.2 入站回复

长连接桥不可用时只影响回复接收。等待任务保持原状态，并由只读状态报告明确显示 bridge 不可用；不影响普通任务结果发送。

### 9.3 Codex Automation 展示

automation 对话只负责前台调用共享入口并展示脚本返回 JSON，不作为项目事实、runtime 或通知输入。两个提示词来自同一模板，只保留 owner、固定命令、timeout / wait 和原样输出差异。

即使模型额外产生 `::inbox-item`，也不得影响任何业务状态。提示词合同测试只证明配置文本一致，不把模型展示行为当作工作流正确性条件。

## 10. 测试策略

测试收敛为四组：

### 10.1 共享内核

- 双 owner 各自原子 claim，不重复领取同 taskId；
- 两个 candidate 可并行形成；
- 正式集成严格串行；
- 最新 `master` 重放和组合验证；
- 冲突、路径越界、任务变化、主工作区相关改动均保持主工作区不变；
- 各运行阶段被保留但新自动轮次只报告；
- 成功现场精确清理，风险现场零清理。

### 10.2 owner adapter

- Codex 实际 model、route 和 candidate schema；
- DeepSeek 本机 gateway、`deepseek-v4-flash`、route 和 candidate schema；
- DeepSeek 不能复审，Codex 不领取 external route；
- 两个 adapter 不包含 Git 集成或 runtime 修改逻辑。

### 10.3 决策 checkpoint

- 干净 checkpoint 与决策信息精确绑定；
- 等待期间 owner 已释放；
- 有效回复恢复 ready 并在新会话吸收 checkpoint；
- 过期回复、任务摘要变化、路径变化或 cherry-pick 冲突进入人工处理；
- 普通失败不能冒充 decision checkpoint 恢复。

### 10.4 飞书与实时配置

- 长连接桥停止时普通 REST 通知仍可发送；
- 长连接桥停止时回复接收明确不可用；
- 幂等键避免同一完成事件重复发送；
- 两个 automation 配置调用同一共享入口、保留原 schedule / model / notification policy，并处于预期 enabled 状态。

不再为 Codex / DeepSeek 复制整套 Git、canonical、通知和恢复测试。

## 11. 迁移方案

### 11.1 当前前置事实

2026-08-03 设计写入前已确认：

- `codex-hourly-worker` 与 `deepseek-hourly-trigger` 均为 `PAUSED`；
- schema 4 runtime 的 `integrationLease=null`；
- Codex run 为空；
- DeepSeek 存在旧 run `51efd154-de6c-4c3e-bcd0-11f99e7cc07d`，任务为 `U-BOUNTY-01B`，状态为 `attention_required`，原因是 `blocked_sandbox_denies_pwsh`；
- 该旧 run 不允许被新共享内核自动迁移或恢复。

### 11.2 实施顺序

1. 保持两个实时 automation 暂停；
2. 在隔离 worktree 实现共享内核、owner adapter 收敛、进程持有型集成锁、通知解耦和测试；
3. 不读取或修改旧 `U-BOUNTY-01B` worktree 的业务内容；由普通管理上下文按旧规则人工核验并关闭或保留；
4. 只有两个 owner run 都为空且集成锁空闲时，才迁移 runtime schema 和项目规则；
5. 运行共享内核、adapter、decision checkpoint、通知和 PowerShell 运行时测试；
6. 运行私有 canary，证明两 owner candidate 并行、正式集成串行、失败时主工作区不变；
7. 通过 automation 管理接口更新两个实时任务，使其调用同一共享入口；不直接编辑 TOML；
8. 先手动运行 DeepSeek 入口，再手动运行 Codex 入口；
9. 使用两个无直接依赖的低风险任务验证并行开发和串行集成；
10. 启用两个 automation，观察至少两个真实周期；
11. 验证稳定后删除旧重复编排脚本、旧恢复分支和对应重复测试，不长期保留兼容入口。

### 11.3 回滚

在删除旧入口前保留单个可回滚 Git 边界。若 canary 或真实周期失败，暂停两个 automation，保留新 runtime、worktree 和证据，回滚实时 prompt 到旧固定入口并停止迁移；不得同时启用新旧入口。

旧入口删除后若出现系统性错误，只回滚整个精简提交切片，不在新共享内核内加入旧兼容分支。

## 12. 停止条件

出现以下任一情况立即停止实施或启用：

- 需要第二套 runtime、后台守护进程、通用重试队列或长期兼容入口；
- 共享内核需要从模型自然语言正文猜测状态；
- 进程持有型集成锁不能同时约束自动与手动正式集成；
- 冲突、验证失败或拒绝 fast-forward 后主工作区发生变化；
- decision checkpoint 无法与 taskId、decisionId、baseCommit、branch、SHA 和路径授权精确绑定；
- 精简后持久状态、跨轮次恢复分支或 owner 专属 Git 代码反而增加；
- 旧活动 run 尚未人工关闭就需要迁移 runtime；
- DeepSeek 结果绕过 Codex 复审或提前解锁依赖；
- 飞书解耦需要在仓库保存新的 provider 凭据或用户标识。

## 13. 完成标准

- 两个 automation 继续独立按小时触发，同一时刻可分别运行一个不同任务；
- 两个实时 prompt 调用同一共享入口，只通过 owner 和 Codex model 参数区分；
- Codex / DeepSeek 不再复制 Git、runtime、canonical、通知和集成实现；
- 正常成功在一次前台调用内完成，异常现场保留详细阶段但不被下一轮自动恢复；
- DeepSeek 一个正式提交完成业务、pending review 投影和交接证据，Codex 后续独立复审；
- 决策 checkpoint 能在有效回复后由新会话受控继续，普通失败不能使用该路径；
- 普通飞书通知不依赖长连接桥，回复接收仍保持身份与 decision 绑定；
- 成功 worktree 被精确清理，风险 worktree 完整保留；
- runtime 中没有旧活动 run，实时配置、项目规则和实际脚本对 owner、状态、集成和通知描述一致；
- 没有新增通用 lane、第二队列、后台守护、自动重试或兼容状态机。
