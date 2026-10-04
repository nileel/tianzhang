# 小时自动化控制器 v3.1 候选发现修复设计

> 日期：2026-07-15
> 状态：用户已批准继续修复
> 范围：修复 v3 薄提示词无法从真实队列完成候选发现的功能回退；不处理业务任务，不改变单写入者、主责、审核、恢复、提交或私密配置边界

## 1. 问题与证据

手动触发会话 `019f6187-2e59-76c2-927f-a6c7899ae1e3` 没有破坏项目文件，但暴露了两个确定性缺陷：

1. 薄提示词没有给出 `Start` 的准确参数名。模型先猜测 `-ProjectRoot`，再尝试位置参数，两次均失败；读取脚本后才发现实际参数是 `-RepositoryRoot`。
2. `Start` 只返回 `开发管理/当前任务队列.txt`，而 `RegisterCandidate` 在返回执行分支事实源前已经要求完整 `expectedPaths`。对于必须先运行只读检查、搜索 docs/CSV/Unity asset 或读取任务来源才能确定路径的任务，协议形成循环依赖。

现有测试直接使用正确参数调用脚本，部署 checker 只验证关键词和长度，真实 canary 只执行 `Start → CompleteNoChange`。它们证明了安全空跑和收尾，却没有证明真实候选能够被检查、隔离和登记。

## 2. 目标与非目标

### 2.1 目标

- 模型从队列选出候选后，可以在任何项目修改前读取候选分支事实源并进行受约束的只读路径发现。
- `RegisterCandidate` 继续要求完整、项目相对的 `expectedPaths`，workspace guard 语义不放宽。
- prompt 使用准确、可复制的命令签名，不再让模型猜测 PowerShell 参数。
- 自动测试覆盖真实失败路径：准确启动命令、候选检查阶段、登记前置条件、冲突后继续选题，以及 PAUSED 真实队列 canary。

### 2.2 非目标

- 不让控制器理解具体 TQ/HANDOFF/DEC 的业务语义。
- 不在脚本中硬编码任务编号、业务路径或项目内容。
- 不允许候选检查阶段修改项目、暂存、提交、派发 worker 或执行有副作用的命令。
- 不改变既有恢复、待决策、DeepSeek backoff、证据、finalizer 或最小充分验证规则。

## 3. 方案比较

### 方案 A：扩大 `Start.requiredSources`

让每次 fresh run 一次性返回任务列表、审核、协作、技术经验和各领域事实源。实现最小，但重新制造过度读取和上下文膨胀；仍无法表达“只为已选候选做路径发现”。不采用。

### 方案 B：增加 `InspectCandidate` 两阶段协议（采用）

模型先从队列选择候选并调用 `InspectCandidate`，入口只在用户级 session 记录暂定任务身份，返回该分支的 `requiredSources`、`requiredChecks` 和只读发现策略。模型完成路径发现后，再用 `RegisterCandidate` 提交完整 `expectedPaths`。边界清晰，且不需要硬编码业务任务。

### 方案 C：建立任务 ID 到文件路径的静态清单

控制器从维护清单直接返回每个任务的路径。它能减少模型发现工作，但清单会与任务卡和代码持续漂移，也违反动态选题不得硬编码具体任务的规则。不采用。

## 4. 协议设计

### 4.1 `Start`

fresh run 仍负责租约、workspace baseline 和身份检查，但返回：

- `action=select_candidate`
- `branchKind=selection`
- `requiredSources=[开发管理/当前任务队列.txt]`
- `nextCommand=InspectCandidate`

prompt 使用完整命令：

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/automation-controller.ps1 Start -RepositoryRoot 'D:\天章游戏开发' -RunId '<uuid>' -ActualModel '<model>'
```

### 4.2 `InspectCandidate`

新增入口动作：

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/automation-controller.ps1 InspectCandidate -RepositoryRoot 'D:\天章游戏开发' -RunId '<uuid>' -WorkType '<execution|review|maintenance>' -TaskId '<id>' -Executor '<codex|deepseek>'
```

行为：

1. 只允许在 `identity_checked` 或 `candidate_inspection` session 阶段调用。
2. 验证工作类型、任务 ID、executor 和 DeepSeek backoff；不调用状态机的 `task_selected` checkpoint。
3. 在用户级 session 保存暂定 `workType/taskId/executor`，阶段设为 `candidate_inspection`。
4. 返回候选分支 `requiredSources`，并返回 `discoveryPolicy`：允许读取任务卡明确引用的项目事实源，允许使用 `rg`、`rg --files`、`Get-Content`、只读 Git 查询及任务卡明确指定的只读检查来推导路径；禁止项目写入、worker、stage、commit 和控制器底层 helper。
5. 返回 `nextCommand=RegisterCandidate`。

候选检查不领取项目工作，不形成恢复指针。模型可以再次调用 `InspectCandidate` 替换暂定候选；无候选时可以 `CompleteNoChange`。

### 4.3 `RegisterCandidate`

`RegisterCandidate` 只接受 `RunId` 和 `ExpectedPaths` 作为候选特有输入；任务身份从 session 取得，避免模型重复翻译或传错。为兼容现有调用，旧的 `WorkType/TaskId/Executor` 参数可以继续存在，但若提供则必须与 session 完全一致。

登记前必须满足 `candidate_inspection` 阶段和非空 `expectedPaths`。workspace guard `Check` 通过后才写 `queues_loaded`、`task_selected` checkpoint，并进入既有 `task_selected` session 阶段。

`candidate_conflict` 不关闭租约，返回 `nextCommand=InspectCandidate`，允许模型选择下一候选。非法路径、baseline 变化和工具错误仍按既有 failure policy 关闭。

### 4.4 其他动作

- `CompleteNoChange` 支持从 `identity_checked` 和 `candidate_inspection` 安静收尾。
- recovery、pending decision 和已经登记的任务不经过 `InspectCandidate`，继续使用状态中固定的任务身份与路径。
- `BeginMutation`、`Finish`、`Fail`、决策动作、worker backoff 和 `Renew` 的语义不变。

## 5. Prompt 与规则

薄 prompt 保持不超过 10 个步骤，并明确写出 `Start`、`InspectCandidate` 和 `RegisterCandidate` 的命名参数。它只描述两阶段语义：先检查候选，再登记路径；不复制内部状态机、guard 或 Git 命令。

部署 checker 除长度和禁用项外，必须验证：

- prompt 含准确的 `Start -RepositoryRoot ... -RunId ... -ActualModel ...` 顺序；
- prompt 含 `InspectCandidate`；
- prompt 不再把 `Start` 的下一步直接描述为 `RegisterCandidate`；
- controller 的 `Contract` 与测试暴露相同动作集合。

## 6. 错误处理与安全

- `InspectCandidate` 参数错误发生在 mutation 前，使用空运行失败关闭并回到 `IDLE`。
- DeepSeek backoff 返回 `skip_candidate`，`nextCommand=InspectCandidate`，不改变主责。
- 候选路径冲突返回冲突路径并继续候选检查；不把冲突路径加入允许集合。
- 候选检查期间若 baseline 变化，禁止登记并按既有策略只读关闭。
- session 中的暂定候选不是恢复授权；只有 `RegisterCandidate` 成功并形成 `task_selected` 后才可能产生恢复工作。

## 7. 测试与部署

TDD 必须先出现以下失败：

1. `Start.nextCommand` 仍为 `RegisterCandidate`，缺少 `InspectCandidate`。
2. 未检查候选即可登记，或检查动作不存在。
3. 候选冲突后不能返回 `InspectCandidate`。
4. checker 接受缺少准确参数名的 prompt。

转绿后运行控制面最小充分回归：controller、state、workspace guard、finalizer、workflow checker。PAUSED 真实 canary 使用当前真实队列执行 `Start → InspectCandidate(TQ-057) → CompleteNoChange`，断言候选分支事实源与只读发现策略存在、HEAD 和工作树不变、状态回到 `IDLE`。

只有测试、canary、prompt 源与部署内容一致且零活动写入者均通过后，才通过 automation API 恢复唯一控制器。不得推送远端。

## 8. 验收标准

- 手动触发不再猜测 `Start` 参数，也不读取脚本自救。
- 真实队列候选在登记路径前获得明确的候选检查阶段。
- `expectedPaths`、workspace guard 和路径限定提交边界未放宽。
- 测试能在旧 v3 实现上稳定失败，在 v3.1 上通过。
- 正式激活后只有一个写入型自动化，状态为 `IDLE`，Git 工作树干净。
