# 外部责任方收尾失败与现场恢复修复设计

## 一、背景与已确认根因

2026-07-29 22:17 的 `tzg-hourly-controller` 选中
`C-GZ-BOUNTY-01`，以 `owner=deepseek` 启动外部会话
`22d77f7d-e0d5-4c70-b63c-a4993a73bd47`，Run ID 为
`9d9fdfcb-b3de-446c-9c92-348c4bbac7db`。

外部责任方已经生成一次性石甲兽悬赏档案，并把同一卡、队列和
内容 backlog 转换到待 Codex 复审状态。失败发生在验证与双提交收尾：

1. 外部责任方从 Bash 调用 PowerShell 时，把多个中文路径写成
   `-Paths '路径一','路径二','路径三'`。Bash 没有把这段文本解析为
   PowerShell 数组，而是向脚本传入一个逗号连接的参数；同时带引号的
   中文管理路径出现了乱码。逐次只传一个未加逗号的路径时检查可以通过。
2. 外部责任方随后尝试使用 `cd ... && pwsh ... (Get-ChildItem ...)`
   绕过上述失败。该命令不属于 `dontAsk` 的精确允许范围，被权限门禁拒绝。
3. 四个预期文件进入暂存区后，外部责任方只输出“准备运行前置检查”的
   说明文字便结束回合，没有执行任务卡后置条件、`businessCommit`、
   `handoffCommit`，也没有形成 Claude CLI 约定的结构化终态。
4. 生产 wrapper 对缺失字段的官方 envelope 缺少完整的属性存在性检查。
   在 `Set-StrictMode -Version Latest` 下，部分缺字段路径抛出没有
   `DetailCode` 的异常，最终被降级为笼统的 `external_wrapper_error`，
   没有稳定归类为 `external_invalid_terminal`。

控制器随后发现相对启动基线新增了任务预期路径修改，按现有失败保全规则
保留现场和租约，没有记录失败、释放租约、启动第二责任方或清理文件。
启动基线中的七个未跟踪 `tools/*.ps1` 在本轮开始前已经存在，不属于本次
外部会话产物。

## 二、目标与非目标

### 目标

1. 在不丢失现有四个暂存文件的前提下，补齐原任务的验证、业务提交、
   交接提交和租约收尾，使该任务成为合法的
   `codex_review / codex / ready` 待复审项。
2. 消除已确认的多中文路径调用错误，避免外部责任方再次因 Bash 与
   PowerShell 参数语义不一致而偏离允许命令。
3. 让缺少结构化终态或必需字段的 Claude CLI envelope 始终返回稳定的
   `external_invalid_terminal`，不再丢失为 `external_wrapper_error`。
4. 继续保持单写入租约、外部双提交、严格终态、失败保全和 Codex 复审
   边界。

### 非目标

- 不新增 wrapper、提交脚本、恢复脚本、状态字段、重试层、队列、
  checkpoint 或第二套恢复状态机。
- 不让控制器替外部责任方实施业务、读取业务 diff 或自动提交半成品。
- 不自动重启、续跑或猜测失败会话的终态。
- 不扩大 Bash、PowerShell、Git、网络、worktree、推送或权限跳过范围。
- 不修改自动化的计划、模型、推理强度、工作目录或通知偏好。
- 不清理启动基线中已经存在的七个未跟踪工具文件。
- 不在本修复中复审 `C-GZ-BOUNTY-01` 的内容；这里只恢复其待复审交接。

## 三、方案选择

采用“现有机制定点恢复 + 现有 wrapper 定点修复”：

- 当前现场由普通管理上下文在隔离 worktree 中一次性核验并使用现有
  finalizer 完成两提交，再释放原租约并集成同一提交链。
- 防复发只修改 `tools/invoke-external-responsibility.ps1` 及其现有测试，
  不引入新的生产入口或自动恢复行为。

不采用以下方案：

- 新增确定性收尾脚本：会增加生产组件并改变外部责任边界。
- 丢弃当前现场后重新派发：会浪费已有内容，且不能证明当前任务已闭环。
- wrapper 在无结构化终态时替模型提交：会破坏“只信任官方 envelope”
  和外部责任方自提交边界。

## 四、当前现场恢复

恢复只在下列事实仍全部成立时进行：

- automation 配置仍是同一个 `tzg-hourly-controller`；
- runtime 中的 lease 仍指向上述 Run ID、Task ID、owner 和仓库；
- 没有 recovery，没有新的控制器或外部责任方进程仍在处理该 Run ID；
- HEAD 仍为该轮启动基线的 `213614990a745d36ccf83bd21a3d1ecb6993ce1c`；
- 相对基线新增内容只有任务卡 expected paths 中的四个暂存文件；
- 七个未跟踪工具文件与私有 baseline 中记录的启动前哈希一致。

任一事实变化时停止，不覆盖新运行或人工修改。

### 4.1 临时调度保护

通过 Codex 自动化管理能力读取完整实时配置，并只把状态临时设为
`PAUSED`；其他字段原样保留。暂停只是本次普通管理恢复的互斥保护，
不写入 prompt，不新增 runtime 字段，也不由自动化任务管理自身配置。

### 4.2 验证现有任务产物

不改写业务正文。先把主工作区现有四个暂存文件的 index patch 机械应用到
`.worktrees/automation-external-closeout-repair`，并核对两边的路径集合和
patch identity 完全一致。设计文档保持未暂存，不进入任务业务提交。

随后在隔离 worktree 中执行任务卡要求的最小充分验证：

1. 对每个目标路径分别调用一次
   `tools/check-pending-whitespace.ps1 -Paths <单一路径>`。
2. 运行 `tools/check-review-text.ps1 -Paths docs/剧情,开发管理`。
3. 运行 `git diff --cached --check`。
4. 运行
   `tools/check-task-cards.ps1 -TaskId C-GZ-BOUNTY-01
   -Postcondition ExternalPendingReview -OutputJson`。
5. 核对隔离 worktree 的暂存路径恰好是新悬赏档案、内容 backlog、同一卡和当前队列，
   不包含 `开发管理/AI合作沟通.txt` 或其他路径。

任何检查失败时保持现场、暂停和租约，不叠加修补；回到失败文件的直接
根因。

实施时后置门禁确认原现场的 backlog 行把主责和状态投影分别写成了
`deepseek / 待复审`，与任务卡 `owner=codex / dispatchState=ready`
不一致。只把该行修正为既有合法投影 `codex / 已排队` 后，门禁返回
`status=ok`；没有修改业务正文或其他任务。

### 4.3 补齐既有双提交

验证通过后：

1. 使用现有 `tools/automation-finalize-commit.ps1` 创建路径限定的
   `businessCommit`，`AutomationTask=C-GZ-BOUNTY-01`、
   `AutomationState=pending_review`，并按当前任务事实填写现有九个
   通知子字段。
2. 只修改 `开发管理/AI合作沟通.txt`，登记真实 business SHA、已验证、
   未验证、残留风险和 Codex 复审请求。
3. 再使用同一 finalizer 创建只含沟通文件的 `handoffCommit`，不写
   Automation 元数据，不重复领域验证。
4. 核对两个完整 SHA、父子关系、business 元数据、handoff 无 Automation
   元数据、任务卡后置条件以及相对基线无新增未提交路径。

这两个提交明确记录为对原 DeepSeek 会话产物的人工恢复收尾，不伪造该
会话曾返回 `completed`。

实施形成的 business commit 为
`530a3504fcc37e09d5a8ccc1514b299573504cf3`，handoff commit 为
`4e418ded916b7dc494c6420c099a1e56e148ee53`。

### 4.4 租约、主工作区与记忆收尾

原外部会话没有返回 `completed`，因此人工恢复不得伪造该 Run ID 的
`RecordResult success`，也不补发自动任务结果通知。全部提交和后置条件
成立后：

1. 再次确认主工作区 index patch 与隔离 worktree 的 business commit
   完全一致，HEAD 仍为启动基线，且自动化仍为临时暂停。
2. 使用原 Run ID 调用 `Release`，只清除被保留的租约；`lastResult`
   保持原值，忠实反映该自动轮次没有形成已核验终态。
3. 在 business commit 已提供可恢复副本且 patch identity 相同的前提下，
   只对主工作区四个任务路径执行定点 `git restore --staged --worktree`，
   不触碰七个启动前未跟踪工具文件或其他路径。
4. 由于主工作区 HEAD 与隔离分支基线相同，使用 `git merge --ff-only`
   集成 business / handoff 两提交；禁止 cherry-pick 改写提交链。
5. 更新用户级 automation memory，明确原会话已由普通管理上下文恢复、
   租约已清除、任务现为待 Codex 复审，避免后续轮次继续把旧现场描述为
   未恢复。

若 patch identity、HEAD 或 fast-forward 条件任一不成立，停止在定点
restore 之前；不会丢失主工作区现场。

## 五、wrapper 最小修复

### 5.1 固定提示

只修改现有 `New-ExternalPrompt` 文本，增加三条明确约束：

1. `check-pending-whitespace.ps1` 涉及多个路径时，每次 Bash 调用只传
   一个 `-Paths <单一路径>`；不得在 Bash 命令中使用逗号拼接、
   PowerShell 数组表达式、`Get-ChildItem`、`cd &&` 或替代 shell。
2. 被精确权限拒绝时不得构造复合命令绕过；若任务无法继续，只能返回
   合法的 `blocked` 或 `failed` 结构化终态。
3. 不能以“准备运行”“接下来执行”等说明文字结束；必须继续使用允许的
   工具完成收尾，或返回 schema 允许的结构化终态。

这些约束不增加权限、不改变工具集合，也不为模型增加新的执行步骤。

### 5.2 严格终态判定

在读取属性前先核对官方 envelope 明确包含：

- `type`
- `subtype`
- `is_error`
- `session_id`
- `structured_output`

随后核对 `structured_output` 是对象且包含 `status`。再按状态分别核对：

- `completed`：`identity`、`businessCommit`、`handoffCommit`
- `needs_decision`：`decisionId`、`question`、`options`
- `blocked` / `failed`：`detailCode`

任一属性缺失、类型不符、为空或不满足现有合同，统一通过现有
`Stop-External 'external_invalid_terminal'` 返回。不读取模型正文、
不补全 SHA、不推断状态、不自动恢复。

现有最外层 `external_wrapper_error` 只保留给 wrapper 自身未预期的真实
实现异常；无效 CLI 终态不再落入该兜底。

## 六、隔离、集成与恢复 ACTIVE

由于设计开始时主工作区存在 active lease 和被保留的暂存现场，控制面
修改继续在 `.worktrees/automation-external-closeout-repair` 的
`codex/automation-external-closeout-repair-design` 分支实施。

集成前必须重新调用 `Show` 并满足：

- 自动化仍处于本次管理上下文设置的临时暂停；
- 主工作区除了四个待恢复任务路径和启动基线已记录的七个未跟踪工具文件
  外，没有新增 staged、unstaged 或 untracked 变化；
- wrapper、测试和本设计文档路径不与主工作区改动冲突。

任务双提交按 4.4 先释放原租约再 fast-forward 到主工作区。随后确认
runtime 已经 `lease=null`、`recovery=null`，控制面提交只包含本设计文档、
现有 wrapper、现有 wrapper 合同测试和现有真实 canary。控制面提交也只
通过 fast-forward 集成，再运行主工作区直接检查。最后通过自动化管理能力
把完整原配置仅将状态恢复为 `ACTIVE`，不改其他字段。

## 七、验证

### 现场恢复

1. 四个路径逐个运行 `check-pending-whitespace.ps1`。
2. `check-review-text.ps1 -Paths docs/剧情,开发管理`。
3. `git diff --cached --check`。
4. `check-task-cards.ps1 -TaskId C-GZ-BOUNTY-01
   -Postcondition ExternalPendingReview -OutputJson`。
5. business / handoff 的完整 SHA、父子关系、元数据和路径范围。
6. `Show` 返回 `lease=null`、`recovery=null`，`lastResult` 未被伪造为
   原失败会话成功。

### wrapper

1. PowerShell parser 检查。
2. `tools/test-invoke-external-responsibility.ps1` 增加并通过：
   - 固定提示含单路径调用约束；
   - envelope 缺 `structured_output`；
   - `structured_output={}`；
   - completed / needs_decision / failed 分支缺必需字段；
   - 上述无效终态均稳定返回 `external_invalid_terminal`。
3. `tools/test-check-automation-workflow.ps1`。
4. `tools/check-automation-workflow.ps1`。
5. `tools/test-external-ai-self-commit.ps1` 真实临时仓库 canary，证明
   `dontAsk`、中文 expected paths、新文件创建、逐路径空白检查和两提交
   收尾仍可完成。
6. 对控制面预期路径运行 `check-pending-whitespace.ps1`。
7. `git diff --check`，暂存后 `git diff --cached --check`。

不运行 Unity、BattleSim 或数据链路检查；本修复不涉及这些输入。

实施时真实 canary 首次在启动外部会话前返回
`external_wrapper_dependency_missing`，确认其现有夹具漏拷贝 wrapper 已经
要求的 `automation-commit-metadata.ps1`。只把该现有依赖加入 canary 的复制
清单后重跑，真实 canary 返回 `test-external-ai-self-commit: OK`；没有修改
生产依赖、权限或执行步骤。

## 八、停止条件

- 当前 Run ID、Task ID、owner、HEAD、暂存路径或基线哈希发生变化。
- 发现外部责任方或控制器进程仍在运行。
- 现有四个任务文件有任何直接验证失败。
- 完成恢复必须新增脚本、状态、重试、队列或扩大权限。
- wrapper 修复需要控制器替外部责任方提交、读取业务 diff 或猜测终态。
- canary 产生仓库外泄残留、权限扩大、无效提交链或无法解释的失败。
- 集成时主工作区路径与控制面提交冲突，或 runtime 不为空。

触发任一停止条件时保留现场并报告，不继续叠加补丁。

## 九、完成标准

- `C-GZ-BOUNTY-01` 有合法 business / handoff 两提交，保持待 Codex 复审，
  runtime 无遗留 lease 或 recovery。
- 已知中文多路径调用错误不再出现在固定提示允许流程中。
- 所有缺字段 CLI 终态稳定归类为 `external_invalid_terminal`。
- 真实 canary 通过，控制器恢复原 `ACTIVE` 配置。
- 没有新增生产组件、状态字段、重试层、队列、恢复状态机或权限。
