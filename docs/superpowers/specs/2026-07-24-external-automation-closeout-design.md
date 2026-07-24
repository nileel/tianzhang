# 外部 AI 自动化收尾修复设计

## 目标

在不新增外部责任方 wrapper、不重构现有 Codex 固定调用器的前提下，修复 `tzg-hourly-controller` 外部 AI 路线的三个已确认问题：

1. DeepSeek V4 Pro 会话被错误标记为 Claude Code。
2. 外部 AI 已返回合法双提交后，控制器没有记录结果并释放租约。
3. 当前 `C-ENV-PROFILE-01` 留有未提交的 Unity 目录 `.meta`，管理状态正文与表头不一致。

## 边界

- 保留控制器直接串行调用现有 Claude CLI 的方式。
- 不新增进度数据库、重试层、第二状态机或新的外部执行脚本。
- 不修改 Codex 执行、复审、队列维护的固定调用器。
- 不在本修复中通过 `C-ENV-PROFILE-01` 复审，也不解除 `U-2_5D-01D` 的阻塞。
- 保留主工作区现有 `.agents/summary_state.json` 与 `设计总结.txt` 改动。

## 设计

### 身份命名

控制器在真实选中外部候选并取得租约后，按现有配置入口核验外部执行器身份：先读当前进程的 `ANTHROPIC_BASE_URL`，为空时只补读 `~/.claude/settings.json` 的同名字段。`http://127.0.0.1:15721` 同源地址（含 `/claude-desktop` 路径）统一判定为 DeepSeek V4 Pro。派工提示词、外部终态的 `identity` 字段、未审核标记和交接记录必须统一使用 `DeepSeek V4 Pro`；不得因父进程没有展开该变量就改写为 Claude Code。

身份配置与候选主责不匹配时，不启动外部 CLI，记录失败并释放租约。

### 成功收尾

外部 AI 继续负责 workspace guard、实施、验证、`businessCommit` 和 `handoffCommit`。最终输出必须包含 `status=completed`、`identity=DeepSeek V4 Pro`、真实 `businessCommit`、真实 `handoffCommit` 和 session ID。

控制器只核验以下终态形状，不读取业务 diff、不重跑领域验证：

- 两个 SHA 都存在。
- `businessCommit` 含当前 Task ID、`State: pending_review` 和 Automation 元数据。
- `handoffCommit` 是 `businessCommit` 的直接后继，且不含 Automation 元数据。
- 相对启动基线没有新增未提交路径。

全部成立后，控制器对当前 Run ID 调用 `RecordResult -Category success`，再调用 `Release`。任一步失败都不得报告 completed。

### 失败与残留

- 外部输出缺字段、SHA 关系不成立或提交元数据不符：记录 `failed`，释放租约。
- 外部进程异常退出且没有新增改动：记录 `failed`，释放租约。
- 外部进程异常退出或终态核验失败且存在相对基线新增改动：保留现场和租约，报告人工阻塞；不伪造 recovery，不启动第二责任方。
- 控制器不得把超时、yield 或空输出当作终态。

### 当前现场收口

当前 `C-ENV-PROFILE-01` 使用独立修复提交：

- 纳入 `src/Assets/Data/EnvironmentProfiles.meta`。
- 将说明文件和交接中的修改方统一为 `DeepSeek V4 Pro`。
- 将当前任务卡正文状态统一为“已完成（待复审）”。
- 保持 `U-2_5D-01D` 阻塞，保持交接待 Codex 复审。

确认任务相关工作区无残留后，人工把现有 Run ID 记录为 success 并释放过期租约。

## 测试

1. 扩展自动化工作流契约测试，要求控制器提示词明确包含外部完成后的 `RecordResult` 与 `Release`，并禁止把名称核验等同于终态收尾。
2. 扩展租约或控制器测试 fixture，覆盖：
   - 合法外部双提交会记录 success 并释放租约。
   - 非法终态不记录 success。
   - 存在新增未提交路径时不释放租约。
   - DeepSeek 身份不会被写成 Claude Code。
3. 运行现有自动化静态检查、工作流测试、租约测试和 Codex 固定调用器测试，证明未影响既有路线。
4. 对当前现场运行路径限定空白检查、审核文本检查、数据链检查和 Git 状态核验；不重复已经通过且输入未变化的 Unity 全量测试。

## 完成条件

- 控制器配置保持 `PAUSED` 直到代码、当前现场和 runtime 全部核验完成。
- 外部成功路线能够在同一轮留下正确 `lastResult` 且 `lease=null`。
- 当前工作区不再含 `C-ENV-PROFILE-01` 任务残留。
- 当前任务仍为待复审，`U-2_5D-01D` 仍阻塞。
- 恢复控制器为 `ACTIVE` 后，不立即手工启动第二责任方。
