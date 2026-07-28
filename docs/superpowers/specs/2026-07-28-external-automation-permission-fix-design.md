# 外部自动化权限与基线隔离修复设计

## 一、背景与根因

2026-07-28 的每小时自动化选择了 `C-GZ-CITY-01`，取得外部责任方租约后通过 Claude CLI 调用 DeepSeek V4 Pro。任务要求新建 `docs/剧情/据点/关中城.txt`，但生产调用使用 `dontAsk` 权限模式，允许工具只有 `Read`、`Edit` 和少数限定脚本，没有 `Write`，也没有任务明确要求的 `git diff --check`。因此新文件写入和最终差异检查无法完成。

控制器传入的私有 baseline 路径位于用户级 automation runtime 下，但没有先创建父目录。外部责任方没有使用传入路径，而是在仓库现有 `.claude/` 目录中创建 `guard-baseline-gz-city.json`。该文件成为相对启动基线新增的未提交路径，触发现有失败保全规则。外部责任方随后返回说明文字而非约定的 JSON 终态，控制器因此保留现场和租约，没有记录失败、释放租约或发送任务结果通知。

现有 `tools/test-external-ai-self-commit.ps1` 没有发现问题，因为 canary 会预建 runtime 目录，并且只修改已经存在的业务文件，实际只覆盖了 `Edit`，没有覆盖生产任务的新文件创建路径。

## 二、目标与非目标

### 目标

1. 保持 `dontAsk` 和现有单写入租约、workspace guard、路径限定提交、外层终态核验不变。
2. 让合法的外部执行任务能够新建任务卡授权范围内的文件，并执行任务要求的 `git diff --check`。
3. 确保 workspace baseline 只能写入用户级私有 runtime，不在仓库中产生控制面临时文件。
4. 让 canary 同时覆盖新文件创建、已有文件编辑、私有 baseline 和两提交收尾。
5. 清理本次事故残留并恢复调度条件，但不人工执行 `C-GZ-CITY-01`；由下一次小时调度自动重试。

### 非目标

- 不关闭或绕过 Claude CLI 权限检查。
- 不新增外部 wrapper、重试层、恢复状态机或第二套任务队列。
- 不修改 `C-GZ-CITY-01` 的内容、状态、顺序或业务事实。
- 不扩大外部责任方的 Git、推送、worktree、stash、reset、checkout、clean 权限。
- 不修改自动化的名称、计划时间、模型、推理强度、工作目录或启停状态。

## 三、最小设计

### 3.1 生产启动前置

控制器选择合法 `external_execute` 任务并取得租约后，在启动 Claude CLI 前：

1. 生成当前 Run ID 专属的 baseline 路径：
   `~/.codex/automation-state/tzg-hourly-controller-runtime/external-baselines/<RunId>.json`。
2. 由控制器创建该路径的父目录。
3. 确认父目录位于上述用户级私有 runtime 内；不得使用仓库路径、任务 expected paths 或 `.claude/`。
4. 把完整 baseline 路径继续作为现有 workspace guard 命令的参数传给外部责任方。

目录创建是外部 CLI 启动前置，不交给模型推断，也不新增 runtime 状态字段。

### 3.2 最小权限白名单

外部 CLI 继续使用 `--permission-mode dontAsk`。允许工具固定为：

- `Read`
- `Edit`
- `Write`
- 现有 workspace guard、待提交空白检查、审核文本检查、任务卡检查和自动化提交 finalizer 的限定 `pwsh -File` 命令
- 精确的 `Bash(git diff --check)`

不允许通配的 Bash、任意 PowerShell、任意 Git、`git push` 或权限跳过选项。`Write` 负责新文件创建，授权路径仍由任务卡 expected paths、workspace guard、finalizer 和控制器终态检查共同约束。

### 3.3 项目事实源与薄路由边界

项目内同步修改：

- `开发管理/自动工作流规则.txt`：在外部 wrapper 稳定边界中记录同一约束，避免控制器提示词再次漂移。
- `tools/test-external-ai-self-commit.ps1`：让 canary 使用与生产相同的权限集合，并覆盖新文件创建。
- `tools/check-automation-workflow.ps1` 与 `tools/test-check-automation-workflow.ps1`：只对核心规则增加 baseline 父目录前置和最小允许工具集合的契约断言。

`开发管理/自动工作流控制器提示词.txt` 和实时 `tzg-hourly-controller` prompt 保持不变。薄路由已有明确步骤要求无 recovery 时读取 `开发管理/自动工作流规则.txt`，外部启动细节只保留在该稳定事实源中，避免复制到实时配置；不得直接编辑 `automation.toml`。

### 3.4 Canary 覆盖

canary 保留现有临时仓库和真实外部会话流程，并增加以下证明：

1. baseline 父目录在外部会话开始前由 harness 创建。
2. 外部责任方必须新建一个 expected paths 内的业务文件，而不只是编辑已有文件。
3. 新文件进入 business commit，已有任务卡、队列和 backlog 仍按原规则转换到 `codex_review`。
4. `git diff --check` 在 `dontAsk` 下可执行并通过。
5. baseline 文件存在于临时 runtime，仓库中不存在 `.claude/guard-baseline*` 或其他控制面临时文件。
6. business commit、handoff commit、作者、父子关系、Automation 元数据和最终干净工作树继续通过现有断言。

不增加第二个外部会话或重复领域验证。

## 四、运行与失败流程

正常数据流保持：

`Show → 选择同一卡 → Acquire → 预建私有 baseline 目录 → 外部 CLI → guard → 实施与验证 → business commit → handoff commit → 外层核验 → RecordResult → Release → 通知`

若 baseline 父目录创建失败，控制器不得启动外部 CLI；在工作树无新增路径时记录稳定失败并释放当前租约。

若外部责任方返回非 JSON、权限拒绝或其他无效终态：

- 相对基线没有新增未提交路径：沿用现有失败记录、释放和通知流程。
- 存在新增未提交路径：沿用现有现场保全规则，不自动删除、重试或伪造 recovery。

本修复不改变失败分类，只消除当前已知的必然权限失败和 baseline 路径前置缺失。

## 五、本次事故清理与恢复

实施和测试通过后，普通管理上下文执行一次定点清理：

1. 重新调用 `Show`，确认没有 active 租约、没有 recovery，遗留 Run ID 仍为 `ff0ca7ff-30cf-4352-a732-2594e5d74903` 且状态为 expired。
2. 确认业务目标文件未创建、HEAD 未形成事故业务提交，新增未跟踪路径只有 `.claude/guard-baseline-gz-city.json`。
3. 核对该文件内容仍是事故时的 schema 2 空 entries baseline，再只删除该文件。
4. 使用原 Run ID 清除过期租约，使 `Show` 返回 `lease=null`；不补造 success、failed、recovery 或业务通知。
5. 按项目 worktree 合并门禁重新核对队列、任务卡和主工作区路径冲突，再集成修复提交。
6. 保持 `C-GZ-CITY-01` 为原来的 `external_execute / deepseek / ready`，不手动触发控制器，等待下一次小时调度。

若上述事实有任一变化，停止清理或合并并报告，不覆盖新运行或人工修改。

## 六、验证

最小充分验证集：

1. `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-check-automation-workflow.ps1`
2. `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-automation-workflow.ps1`
3. `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-external-ai-self-commit.ps1`
4. `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-review-text.ps1 -Paths AGENTS.md,CLAUDE.md,开发管理`
5. 对本轮预期路径运行 `tools/check-pending-whitespace.ps1`
6. `git diff --check`
7. 暂存后运行 `git diff --cached --check`

外部 canary 已覆盖真实 `dontAsk` 权限边界，本轮不增加全项目 Unity、BattleSim 或数据链路验证。

## 七、完成标准

- 实时控制器继续读取核心规则，核心规则明确预建私有 baseline 目录及最小写入权限。
- canary 在 `dontAsk` 下成功创建新文件并完成两提交闭环。
- 仓库不产生 baseline 临时文件，事故残留已定点清除。
- runtime 没有遗留 lease 或 recovery。
- `C-GZ-CITY-01` 保持原 ready 投影，并由下一次小时调度自然重试。
- 没有修改自动化计划、模型、任务业务内容或其他队列项。
