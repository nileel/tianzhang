# Codex 小时自动化自暂停解耦设计

> 日期：2026-08-14
> 状态：方案 A 已获负责人批准，等待书面设计复核。

## 一、问题与证据

2026-08-14 的 `codex-hourly-worker` 在项目共享入口已经结束后，仍有多条 Desktop automation 对话持续对同一个 `functions.exec` cell 调用 `wait`。本次现场确认：

- schema 5 的 `runs.codex` 与 `runs.deepseek` 均为空，集成锁为空，且没有 `invoke-hourly-owner.ps1` 进程；重复的不是项目 owner run。
- 三条长期等待对话分别调用 `wait` 194、249、303 次，持续约 3.4 至 5.4 小时；它们在等待期间合计记录约 2826 万 token。
- 当前 Codex 触发提示词把请求元数据核验、共享入口、终态解析、配置读取和 `codex_app__automation_update` 自暂停全部放在同一个长时间 `functions.exec` 中。
- `codex-hourly-worker` 在其中一条旧对话最终完成自暂停前仍保持 `ACTIVE`，因此后续 `:15` 轮次继续创建新的 Desktop 对话。
- 多条旧对话在收口时并发更新同一个 automation memory，已经出现 `apply_patch verification failed`；这是外层等待重叠的次生竞争，不是项目业务写入冲突。

根因边界因此冻结为：项目共享入口之后的 Desktop 触发层把 automation 自管理调用嵌套在长时间 cell 内，导致共享入口完成与对话 `task_complete`、自暂停生效不能及时解耦。

## 二、目标与非目标

### 目标

1. 保留单个前台 `RunOnce` 调用与同一 cell 等待合同。
2. 把 `codex-hourly-worker` 的自暂停管理移出长时间 `functions.exec`，改为共享入口终态返回后的独立短 cell；配置读取、automation 更新与回读比对在该 cell 的同一确定性作用域内完成。
3. 继续只在以下五个字段精确命中时自暂停：
   - `status=no_candidate`
   - `owner=codex`
   - `taskId=QUEUE-MAINTENANCE`
   - `detailCode=no_runnable_candidate`
   - `cleanup=cleaned`
4. 同步版本化提示词、持久规则、聚焦测试和实时 automation 配置，避免配置与仓库事实漂移。
5. 在保持入口暂停的情况下完成受控验证；没有完整 `task_complete` 证据时不恢复定时执行。

### 非目标

- 不修改 schema 5、owner claim、任务选择、candidate、worktree、正式集成、通知或清理合同。
- 不修改 `tools/invoke-hourly-owner.ps1`、`tools/hourly-automation-lease.ps1` 或 Codex candidate wrapper。
- 不新增 overlap lease、第二 runtime、后台守护、重试、超时兼容分支或旁路终态文件。
- 不修改或暂停 `deepseek-hourly-trigger`、日报、周报及其他 automation。
- 不批量结束 Node、PowerShell 或 Codex 进程，不清理历史 session、worktree 或 branch。

## 三、方案比较与决定

### 方案 A：解耦为独立短 cell 自暂停（采用）

长时间 `functions.exec` 只负责模型元数据核验、一次前台共享入口调用和终态 JSON 解析。它返回后，触发对话才在精确空队列终态下执行第二个独立短 `functions.exec`；该短 cell 用同一段确定性代码完成配置读取、`codex_app__automation_update` 和更新后回读比对，不让约 10KB 的 prompt 经过模型转抄。

该方案增加一个短步骤，但删除长 cell 对 automation 管理能力的嵌套依赖，并把字段保真留在同一 JavaScript 作用域；不新增组件或持久状态，净耦合下降。

### 方案 B：只修改实时 prompt（不采用）

可以更快止血，但会让实时配置与 `开发管理/自动工作流控制器提示词.txt`、聚焦测试和项目规则继续漂移，后续部署可能覆盖修复。

### 方案 C：新增并发租约或等待超时层（不采用）

schema 5 已经证明项目 owner 没有并发写入。新增租约或重试只能掩盖 Desktop 回调故障，并扩大 runtime 与恢复职责。

## 四、详细设计

### 4.1 长时间执行单元

`codex-hourly-worker` 的主 `functions.exec` 保留以下顺序：

1. 通过正式 Node REPL MCP 读取 `nodeRepl.requestMeta['x-codex-turn-metadata'].model`。
2. 核验恰有一个符合 `gpt-...` 命名边界的模型值。
3. 单次前台调用 `tools/invoke-hourly-owner.ps1 -Owner codex -Action RunOnce -Model <实际 model> -OutputJson`，保留现有 `3060000` 毫秒 shell 超时。
4. 从 shell 返回中解析唯一结构化终态；解析失败仍以现有稳定错误停止。
5. 计算并保留现有五字段 `shouldSelfPause` 布尔值，只输出包含规范化终态与该布尔值的小型 JSON envelope。

主 `functions.exec` 内不得读取 automation TOML、不得调用 `codex_app__automation_update`、不得修改 automation memory。返回 `Script running with cell ID ...` 时仍只等待同一 cell，不重启共享入口。

### 4.2 独立短 cell 自暂停

主 `functions.exec` 完成后，触发对话按终态分支：

- 五个空队列字段未全部精确命中：不读取 automation 配置，不调用 automation 管理能力，直接进入 memory 与最终输出。
- 五个字段全部精确命中：执行一次独立的短 `functions.exec`，在同一 JavaScript 作用域内读取当前配置、调用 `codex_app__automation_update`、回读配置并逐字段比对。

短 cell 的配置快照固定包含更新接口所需字段：`id`、`kind`、`name`、`prompt`、`status`、`rrule`、`model`、`reasoningEffort`、`notificationPolicy`、`executionEnvironment` 和 `projectId`。实时 automation 管理工具当前的 `view` 模式只渲染卡片，不向脚本返回可复用的结构化字段，因此短 cell 继续只读当前 `automation.toml`，但必须使用完整行锚定解析：顶层字符串字段按键名与 JSON 字符串完整匹配，`target` 必须完整匹配当前内联表结构并从中读取 `project_id`；任何缺失、重复、额外控制字符或结构偏差都以 `codex_automation_config_invalid` 停止。读取不构成配置写入，实际变更仍只允许通过 automation 管理能力。

短 cell 更新必须：

1. 精确确认 `id=codex-hourly-worker`、`kind=cron`。
2. 无论当前为 `ACTIVE` 还是 `PAUSED`，都调用一次更新并把 `status` 设为 `PAUSED`；其余字段直接使用同一作用域中的原始快照，不经过模型转抄。这样暂停状态下的受控轮次也会覆盖真实更新调用。
3. automation 管理结果必须明确包含成功标识；否则以 `codex_self_pause_failed` 停止。
4. 更新后立即重新只读配置，除预期 `status=PAUSED` 外逐字段与更新前快照精确相等；不一致时以 `codex_self_pause_config_mismatch` 停止。
5. 不重试、不直接编辑 TOML、不管理其他 automation。任何短 cell 失败都只令本 Desktop automation 轮次失败；已经关闭的项目 run 不恢复。现有 `notification_policy=failed_runs_only` 负责呈现该失败，不与“不重试”冲突。

### 4.3 memory 与最终输出

automation memory 仍只记录本轮时间与最终结构化终态。它在共享入口和自暂停动作均完成后更新，不参与是否暂停的判断。

最终输出合同不变：先放置单个结构化终态 JSON，再紧接恰好一个简短 `::inbox-item`。命中自暂停条件时明确说明真实空队列已令 Codex 暂停。

### 4.4 版本化与实时事实

实施时只修改以下版本化所有者：

- `开发管理/自动工作流控制器提示词.txt`：落实两阶段调用。
- `开发管理/自动工作流规则.txt`：增加“automation 管理调用不得嵌套在长时间共享入口 cell 内”的持久边界，并明确“只等待同一前台调用”约束的是共享入口主 cell；终态返回后允许一个不调用共享入口的只读／更新／回读短 cell。
- `tools/test-check-automation-workflow.ps1`：直接对版本化提示词增加断言，验证精确五字段合同仍存在、主长时间代码块不含 `codex_app__automation_update`，且独立短 cell 的读取、更新和回读步骤存在。本轮不修改 `tools/check-automation-workflow.ps1`。

新提示词必须保留现有 checker 与聚焦测试依赖的锚点 token，包括 `shouldSelfPause`、`tools.codex_app__automation_update`、`status: 'PAUSED'`、`terminal.cleanup === 'cleaned'`、请求元数据核验和同一 cell `wait` 合同；新增断言不得用删除旧锚点规避既有检查。

实时 `codex-hourly-worker` 只通过 automation 管理能力更新，prompt 使用通过聚焦测试的版本化文本，并保持 `PAUSED` 及其他实时字段不变。不得直接编辑 `automation.toml`。

## 五、验证

### 5.1 仓库聚焦验证

1. `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-check-automation-workflow.ps1`
2. `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-review-text.ps1 -Paths AGENTS.md,CLAUDE.md,开发管理`
3. 对本轮路径运行 `tools/check-pending-whitespace.ps1`。
4. `git diff --check`

聚焦测试必须同时证明：

- 请求元数据模型核验仍存在。
- 单次前台共享入口和同一 cell 等待合同未变。
- 精确五字段自暂停条件未放宽。
- 长时间 `functions.exec` 不再包含 automation 配置读取或 `codex_app__automation_update`。
- 独立短 cell 保留完整实时字段，只改变 `status`，并在同一作用域完成更新后回读比对。

### 5.2 实时配置验证

1. 更新前后都通过 automation 管理能力读取 `codex-hourly-worker`。
2. 更新时保持 `status=PAUSED`，并逐项核对 schedule、model、reasoning effort、notification policy、execution environment、project 和 cwd 未变。
3. 核对实时 prompt 与版本化 prompt 精确一致；对 prompt 使用长度与 SHA-256 双重比较，避免输出或人工比对截断。
4. 再次执行 schema 5 `Show`，确认两个 owner run 为空且集成锁为空。

### 5.3 受控 Desktop 验证

只有运行前再次确认队列没有 Codex 可执行候选、schema 5 两个 owner 为空且集成锁为空，才通过现有 Desktop“立即运行”能力对暂停中的 `codex-hourly-worker` 执行一次受控空队列轮次。

验收要求：

- 共享入口返回 `no_candidate/codex/QUEUE-MAINTENANCE/no_runnable_candidate/cleaned`。
- 即使运行前实时状态已经为 `PAUSED`，独立短 cell 仍实际调用一次幂等 `automation_update`，并通过更新后字段与 prompt SHA-256 比对。
- Desktop 对话产生唯一最终回复与 `task_complete`。
- 终态返回后不再出现新的 `wait`。
- 实时 `codex-hourly-worker` 最终仍为 `PAUSED`。
- schema 5 与集成锁仍为空；DeepSeek 与其他 automation 配置不变。

如果当前环境没有可调用的 Desktop“立即运行”能力，实施停在“版本化与实时配置已更新、Codex 仍暂停”，明确报告未完成的运行验证，不用临时激活定时器或创建替代 automation 绕过。

## 六、停止条件与回滚

- 实施前出现任一活动 owner run 或集成锁：停止实时配置更新，不与活动自动责任方并行变更控制面。
- 版本化提示词测试失败：不部署实时 prompt。
- 无法按精确结构读取完整实时配置时以 `codex_automation_config_invalid` 停止；automation 更新返回不明确时以 `codex_self_pause_failed` 停止；均不编辑 TOML。
- 更新后字段或 prompt 长度／SHA-256 不一致时以 `codex_self_pause_config_mismatch` 停止并保持 automation 为 `PAUSED`；不恢复项目 run、不重试更新。
- 受控轮次再次出现长时间空 `wait`、缺少 `task_complete`、runtime／锁不干净或配置漂移：保持 Codex 暂停，保留 session 证据，不叠加补丁。
- 实时 prompt 更新后需要回滚时，只通过 automation 管理能力恢复更新前完整 prompt；仍保持 `PAUSED`，不回滚 schema 5 或共享入口。

## 七、完成条件

1. 三个版本化所有者通过聚焦验证，且没有修改共享 PowerShell 内核。
2. 实时 `codex-hourly-worker` 使用同一修复 prompt，除 prompt 外配置保持不变并最终为 `PAUSED`。
3. 若 Desktop“立即运行”能力可用，受控轮次实际覆盖幂等暂停更新、字段回读、`task_complete` 和无续发 `wait`；不可用时明确保留该验证缺口。
4. schema 5 两个 owner 为空、集成锁为空，DeepSeek 和其他 automation 未改变。
5. 没有新增 runtime、租约、重试、兼容分支、后台进程或旁路状态。
