# Codex 小时自动化模型元数据通道修复设计

> 状态：负责人已批准方案 A；仓库侧实现与聚焦验证已完成，实时 automation prompt 仍等待正式管理能力同步。本文只修复 `codex-hourly-worker` 的实际模型证明、共享入口领取前门禁与当前干净失败现场；不改变 schema 5、任务选择、candidate 业务合同、DeepSeek 链路、正式集成或通知模型。

## 一、已确认根因

1. 实时 `codex-hourly-worker` 配置和外层 automation turn 实际模型均为 `gpt-5.6-terra`，但触发层在 `functions.exec` 的 JavaScript 作用域直接读取 `nodeRepl`。该对象不在此作用域中，因此取值结果为 `unknown`。
2. 当前环境提供的正式入口是 `functions.exec` 内的 `tools.mcp__node_repl__js`。通过该工具读取 `nodeRepl.requestMeta['x-codex-turn-metadata'].model` 已实际返回当前 turn 模型 `gpt-5.6-sol`，证明 request metadata 本身可用，错误只在调用通道。
3. 2026-08-04 15:17，触发层把 `-Model "unknown"` 传给共享入口。Codex 子会话因此以 `turn_context.model=unknown` 启动，11 秒后以 `last_agent_message=null` 结束；`codex-cli-session.ps1` 记录为 `runner_terminal_missing`。
4. 同日 11:16 与 14:16 使用 `gpt-5.6-terra` 的相同 candidate 链路均返回合法结构化终态并形成提交，因此本轮不重开 runner、candidate prompt、任务业务实现或 Unity 验证链。

## 二、采用方案与不采用方案

采用方案 A：触发层在同一个 `functions.exec` 中先调用正式 Node REPL MCP 工具取得实际 model，机械校验后再把该值传给唯一一次前台 `shell_command`；共享入口在没有既有 owner run、准备选择或领取新任务前再次拒绝未核验 model。

- 不采用方案 B（把 `gpt-5.6-terra` 静态写入 prompt），因为 automation model 变化时会产生配置与证明漂移。
- 不采用方案 C（取消外层证明并让子 Codex 使用默认模型），因为它会扩大 adapter 与 candidate 身份合同，并削弱现有核验边界。

## 三、触发层模型证明合同

`开发管理/自动工作流控制器提示词.txt` 改为给出唯一、完整的 `functions.exec` 代码：

1. 调用 `tools.mcp__node_repl__js`，其 JavaScript 只读取 `nodeRepl.requestMeta['x-codex-turn-metadata'].model` 并用 `nodeRepl.write` 返回字符串。
2. 从 MCP 返回的 text content 中取得唯一 model。model 必须是单个、无控制字符、符合当前 Codex model 命名边界的 `gpt-...` 字符串；空值、多个值、`unknown` 或其他格式立即终止，不调用 PowerShell 共享入口。
3. 仅在校验通过后，把刚取得且已校验的 model 作为 `-Model` 参数，构造固定的 `pwsh ... invoke-hourly-owner.ps1 -Owner codex ... -OutputJson` 调用，并执行唯一一次前台 `shell_command`。
4. 保留现有 3060000 毫秒超时、同一 cell 等待和结构化终态原样返回合同。不得读取或写入 automation memory，不得追加 `::inbox-item`。

实时 automation prompt 必须通过 Codex automation 管理界面同步为同一规范文本；不得直接编辑用户级 TOML。实时 model、schedule、reasoning effort、project、notification policy 和 enabled 状态保持原值。

## 四、共享入口领取前门禁

`hourly-owner-adapter.ps1` 提供一个只负责判断 Codex model 是否已核验的窄函数，`invoke-hourly-owner.ps1` 在两个位置使用：

- `Canary` 在创建 canary worktree 或启动 candidate 前拒绝未核验 model；
- `RunOnce` 先按现有规则执行 runtime `Show`。若已有本 owner run，仍原样返回 `existing_run`；只有 owner run 为空且即将处理决定回复、选择或 claim 新任务时，才拒绝未核验 model。

拒绝结果使用单一稳定 detail code `hourly_codex_model_unverified`，不得 claim 任务、创建 worktree、修改 runtime、读取队列或启动子会话。DeepSeek adapter 与 DeepSeek 入口不经过该门禁。

## 五、当前失败现场处理

当前 run `f6773063-12f0-46ec-a5c9-5b09c1bc32e4` 必须按 `开发管理/自动工作流恢复规则.txt` 处理：

- owner/task/repository/base/digest/worktree/branch 与 runtime 记录一致；
- worktree 位于 base `ddeae3e490a6ed9b60d75493c2ec8f6128bd3b14`，工作树干净；
- 没有 candidate commit、candidate result、canonical 证据或残留责任方进程；
- integration lock 为 `none`，master 尚未包含本 run 的任何业务结果。

代码和实时 prompt 修复就绪前不关闭 run，避免下一小时立即再次领取并复现。修复就绪后才按精确关闭合同调用 `CompleteRun`，并只在 runId、owner、路径、branch、HEAD 与 clean 证据全部匹配时删除该 run worktree 和临时 branch。`U-BOUNTY-01B1` 保持现有 `codex_review/codex/ready`，不修改任务卡、队列或 DeepSeek 正式提交。

## 六、验证与恢复顺序

最小验证范围：

1. `tools/test-hourly-owner-adapter.ps1`：增加有效 `gpt-...`、`unknown`、空值和非 Codex owner 边界；证明 DeepSeek 不受影响。
2. `tools/test-check-automation-workflow.ps1`：证明 canonical prompt 必须包含正式 Node REPL MCP 调用、机械 model 校验、单次 shell 调用，并继续拒绝与实时 prompt 不一致的配置。
3. `tools/check-automation-workflow.ps1` 与 `tools/check-pwsh-runtime.ps1`：核验生产文件、实时 prompt 与 PowerShell 7 合同。
4. `tools/check-review-text.ps1 -Paths AGENTS.md,CLAUDE.md,开发管理`、预期路径 whitespace 检查和 `git diff --cached --check`。
5. 当前失败 run 精确关闭且 schema 5 两个 owner 均为空后，从实时 automation 配置核对其 model，并把该精确值传给 `invoke-hourly-owner.ps1 -Owner codex -Action Canary -Model`；canary 必须形成并清理现有真实提交探针，且主工作区 HEAD/status 不变。

实施与恢复顺序固定为：通过管理界面暂停写入 automation；在隔离手动 worktree 修改、测试、提交；重新核验 runtime、任务占用、主工作区路径冲突和集成锁；通过共享集成脚本 fast-forward；同步实时 prompt；精确关闭当前干净 run；运行 Codex canary；再次核验 schema 5 与清理证据；恢复 automation 原启用状态。

不运行 Unity、BattleSim 或数据链检查，因为本修复不改变业务代码、数值、docs 业务事实、CSV 或 Unity 数据链。

## 七、停止条件

- 若正式 Node REPL MCP 工具在 automation turn 中不可调用，停止，不回退到静态模型名、系统提示猜测、automation memory 或直接 TOML 编辑。
- 若当前 run 的 base、digest、worktree、branch、HEAD、clean、进程或 runtime 任一证据变化，停止关闭与清理，保留现场。
- 若实时 prompt 无法通过 Codex automation 管理界面更新，停止恢复启用；仓库规范文本与实时配置不得长期漂移。
- 若修复需要第二 runtime、重试层、旧 session 恢复、自动冲突解决、任务投影变化或 DeepSeek 链路修改，停止并重新确认范围。
