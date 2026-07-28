# 外部责任方固定调用入口设计

## 背景与根因

2026-07-28 17:17 的每小时自动化由控制器临时拼装 Claude CLI 调用，遗漏 `Write` 与精确的 `Bash(git diff --check)` 权限，导致需要新建文件的 `C-GZ-CITY-01` 无法完成。权限合同修复合入后，19:17 与 20:17 两轮均在启动前返回 `external_wrapper_unavailable`；第二次相同阻塞使 schema 3 runtime 设置 `pauseRequested=true`。

当前事实存在直接矛盾：

- `开发管理/自动工作流控制器提示词.txt` 与 `开发管理/自动工作流规则.txt` 要求外部路线调用既有固定 wrapper。
- 仓库只有 `tools/test-external-ai-self-commit.ps1` 真实 canary，没有生产外部 wrapper。
- `docs/superpowers/specs/2026-07-24-external-automation-closeout-design.md` 曾要求保留控制器直接调用 Claude CLI，并禁止新增 wrapper。

本设计废止 2026-07-24 设计中“不得新增外部责任方 wrapper”这一条窄限制；身份、双提交、控制器核验与失败保全边界继续有效。

## 目标

新增一个确定性的外部责任方调用入口，使薄控制器不再自行拼装 Claude CLI 命令，并让 `external_execute` 任务能够使用与真实 canary 相同的身份、权限、baseline、超时和严格终态合同。

## 非目标

- 不新增队列、重试层、进度数据库、恢复状态机或第二套 runtime。
- 不改变任务选择顺序、任务卡生命周期、双提交规则或 Codex 复审边界。
- 不把外部路线合并进 `tools/invoke-codex-responsibility.ps1`。
- wrapper 不调用 `Acquire`、`RecordResult`、`Release` 或飞书通知。
- wrapper 不读取业务 diff、不重跑领域验证、不替外部责任方 stage 或 commit。
- 本修复不手工执行 `C-GZ-CITY-01`。

## 固定入口

新增 `tools/invoke-external-responsibility.ps1`。

### 输入

固定入口只接受控制器已经选中的单一任务：

- `Action=Start|Resume`
- `RepositoryRoot`
- `TaskId`
- `RunId`
- `Owner=deepseek|claude`
- `SessionId`，仅 `Resume` 必需
- `DecisionOption=A|B|C`，仅 `Resume` 必需，并作为原会话的唯一标准输入
- `StateRoot`，默认使用 `~/.codex/automation-state/tzg-hourly-controller-runtime`
- `ResponsibilityTimeoutSeconds`，默认 3000 秒

不得接受自由形式的 shell、额外 allowed-tools、任意 prompt 或替代 endpoint 参数。

### 启动前门禁

wrapper 在启动 Claude CLI 前依次核验：

1. `RepositoryRoot` 是现有 Git 工作区，且与当前工作目录指向同一仓库。
2. schema 3 runtime 中存在 `RunId` 对应的 active lease，Task ID 与传入值一致。
3. 同一任务卡为 `route=external_execute`、`dispatchState=ready`，owner 与传入值一致；当前队列仍包含同一投影。
4. `owner=deepseek` 时，当前进程 `ANTHROPIC_BASE_URL` 或 `~/.claude/settings.json` 中的同名值必须是 `http://127.0.0.1:15721` 同源地址；`owner=claude` 时必须是原生 Claude Code 环境，不能使用该 DeepSeek 端点。
5. `claude.cmd`、workspace guard、任务卡检查器和 finalizer 均存在；任务卡声明了非空、仓库相对且不越界的 expected paths。expected paths 指向尚未创建的业务文件是合法情况，不要求目标预先存在。

任一门禁失败时不启动 CLI、不修改仓库，只返回稳定的 `failed` 终态和对应 `detailCode`。

### 私有 baseline 与权限

- baseline 固定为 `<StateRoot>/external-baselines/<RunId>.json`。
- wrapper 在启动前创建并核验父目录位于用户级私有 runtime；不得回退到仓库或 `.claude/`。
- Claude CLI 固定使用 `--output-format json` 与 wrapper 内置的 `--json-schema`。wrapper 只信任官方结果 envelope 的 `session_id` 与 `structured_output`，不从模型正文或 Markdown 中搜索、截取或猜测终态 JSON。
- Claude CLI 固定使用 `--permission-mode dontAsk`。
- `--allowedTools` 固定包含：
  - `Read`
  - `Edit`
  - `Write`
  - workspace guard、任务卡检查器、空白检查和 finalizer 所需的限定 `pwsh -File` 命令
  - 精确的 `Bash(git diff --check)`
- 不允许通配 Bash、任意 PowerShell、任意 Git、网络工具或并行代理。

## 责任方提示与会话

wrapper 生成固定提示，只传递：

- 已选中的 Task ID、Run ID、owner 和仓库根目录。
- 任务卡声明的 expected paths。
- 私有 baseline 路径。
- 必读入口：`AGENTS.md`、自动工作流规则、AI 协作规则、DeepSeek 工作提示词、当前队列和同一任务卡。
- 已取得单写入租约、必须直接在传入主工作区工作、不得创建 worktree 或重新扫描候选。
- 外部责任方负责实施、最小充分验证、同卡转换、`businessCommit` 与 `handoffCommit`。
- 不得调用租约工具、推送、自审或扩大授权路径。
- 最终只通过 Claude CLI 的结构化输出返回 `completed`、`needs_decision`、`blocked` 或 `failed`。

`Start` 创建由 wrapper 指定的真实 session ID；`Resume` 只恢复传入的同一 session。wrapper 不根据模型正文生成或猜测 session ID。

## 输出与控制器边界

wrapper 的 stdout 只能包含一行由官方 Claude CLI JSON envelope 规范化得到的 JSON：

- `completed`：包含 owner 对应 identity、真实 `sessionId`、`businessCommit` 和 `handoffCommit`。
- `needs_decision`：包含真实 `sessionId`、稳定 `decisionId`、问题与选项。
- `blocked` 或 `failed`：包含真实 `sessionId`（若已创建）和稳定 `detailCode`。

进度可在 stderr 投影 `session_started` 与 `running`，但不得输出完整提示、CLI 原始正文、官方 envelope、原始 JSONL、凭据或 child stderr。wrapper 输出的 `sessionId` 必须来自 envelope 的 `session_id`；模型结构化对象若也提供 session ID，则只能作为一致性校验，不能覆盖官方值。

wrapper 不关闭运行。控制器继续按现有规则核验 identity、双提交父子关系、Automation 元数据、任务卡 `ExternalPendingReview` 后置条件和相对启动基线残留，再决定：

- 成功：`RecordResult -Category success`，然后 `Release`。
- 无新增路径的无效终态：记录 `failed`，然后释放。
- 存在新增未提交路径：保留现场与租约，转人工处理。

## 超时与异常

- wrapper 内部责任方上限为 3000 秒，为外层 3600 秒租约保留 300 秒关闭窗口。
- 超时后终止整个 Claude CLI 进程树，返回 `failed/external_responsibility_timeout` 和真实 session ID。
- CLI 非零退出、官方 envelope 无效、`structured_output` 不符合 schema、identity 不匹配或终态字段缺失时，返回稳定失败；不自动重试、不重新启动会话、不猜测结果。
- wrapper 不自行清理业务残留，也不 stash、reset、checkout 或 clean。

## 实施范围

1. 新增 `tools/invoke-external-responsibility.ps1`。
2. 新增对应的固定入口合同测试，使用假 `claude.cmd` 验证参数、提示、身份、租约、任务投影、严格输出与失败关闭。
3. 将 `tools/test-external-ai-self-commit.ps1` 改为通过生产 wrapper 调用真实外部 CLI，不再在 canary 内复制生产启动逻辑。
4. 扩展 `tools/check-task-cards.ps1` 的外部启动前置条件，使 wrapper 可核验同一 Task ID 的 `external_execute`、owner 与 ready 投影。
5. 更新 `tools/check-automation-workflow.ps1` 及其测试，要求核心规则、现有薄控制器路由和固定入口保持一致。
6. 只在自动工作流核心规则中明确唯一入口为 `tools/invoke-external-responsibility.ps1`。薄控制器提示词已经强制先读该规则，并保留不含实现参数的通用外部 route 文案，因此项目 canonical prompt 与实时 automation prompt 均不改动，也不产生配置同步事件。

## 验证

最小充分验证集：

1. wrapper 的 PowerShell parser 与假 CLI 合同测试。
2. 任务卡检查器测试，覆盖合法 `deepseek` / `claude` 和 owner、route、ready、队列投影不匹配。
3. 自动工作流静态检查及其变异测试，证明缺失固定入口时失败。
4. 真实外部 canary，经生产 wrapper 完成：
   - previously nonexistent 文件创建；
   - 私有 baseline；
   - `git diff --check`；
   - `businessCommit` 与 `handoffCommit`；
   - 严格 completed JSON。
5. 审核文本、待提交空白、`git diff --check`。

已通过且输入未变化的领域检查不重复运行。

## 集成与恢复

本修复属于普通管理上下文：

1. 实施与验证完成后，确认主工作区无租约、无 recovery，任务卡与队列仍为原投影。
2. 只集成本设计列出的控制面路径。
3. 运行实时工作流检查，确认未改动的 canonical prompt 与 automation prompt 仍完全一致；不得直接编辑 TOML。
4. 再次调用 `Show`，确认 `lease=null`、`recovery=null`。
5. 调用 `ClearBlocking` 清除 `external-wrapper-unavailable` 指纹和 `pauseRequested=true`。
6. 保持控制器 `ACTIVE`，不手工触发业务任务，等待下一次每小时第 15 分钟调度。

## 完成条件

- 生产仓库存在且静态合同引用唯一外部固定入口。
- 控制器不再自行拼装 Claude CLI 命令。
- 真实 canary 经生产 wrapper 成功完成外部双提交闭环。
- runtime 的逻辑暂停已在无租约、无 recovery 条件下清除。
- `C-GZ-CITY-01` 仍为 `external_execute/deepseek/ready`，等待自然调度。
- 没有新增状态机、重试层、队列或业务职责。
