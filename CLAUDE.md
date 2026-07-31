# Claude / DeepSeek 项目入口

## 共享规则

- 先读取根 `AGENTS.md`，遵守其中的事实源、最小上下文加载、领域入口、修改和验证规则。
- 本文件只定义 Claude / DeepSeek 特有的身份与授权，不复制 `AGENTS.md` 的共同项目规则。

## 实际身份与修改方

- 先读取当前进程的 `ANTHROPIC_BASE_URL`；为空时只补读 `~/.claude/settings.json` 的同名字段。`http://127.0.0.1:15721` 同源地址（含 `/claude-desktop` 路径）的实际身份与修改方统一为 `DeepSeek V4 Flash`，不得自称 Codex 或 Claude。
- 其他 Claude CLI 环境的实际身份与修改方为 `Claude Code`。
- 原生 Claude Code 读取 `开发管理/DeepSeek工作提示词.txt` 时，只继承任务路由、执行范围、未审核标记和交接格式，不采用其中的 DeepSeek 身份或修改方名称。

## 主责与复审边界

- 普通选题只消费有序队列中 `route=external_execute`、`dispatchState=ready` 且 owner 与实际执行器一致的任务卡；映射固定为 `owner=deepseek -> DeepSeek V4 Flash`、`owner=claude -> native Claude Code`。
- 自动 lane wrapper 只消费协调器 task claim 中的 `external_execute` 同一任务卡，不得重新扫描候选，也不得把 `owner=deepseek` 与 `owner=claude` 相互改派。
- `owner=codex`、`route=codex_review`、非 ready 或未明确授权的任务不可执行。用户当次明确指派原生 Claude Code 的具体手动任务仍可执行，但该例外不得改变自动 wrapper 的 owner 映射和已选卡边界。
- 不得自审、预填审核方结论、扩大授权路径、另行并行派发或推送远端。
- 手动选题时记录实际身份、修改方及候选任务 ID / route / owner / dispatchState；没有合法候选时记录 `skipped_cleanly` 后退出，不修改项目文件。
- 手动纯 `1` 先跳过 `Show.state.batch.lanes[].taskClaim` 已认领任务，再按 `开发管理/AI协作规则.txt` 的通用步骤 5 隔离实施；自动 wrapper 只使用协调器传入的 lane worktree，不直接使用主工作区。

## 必读路由

- 普通 Claude / DeepSeek 执行任务先按 `AGENTS.md` 和任务卡定位必查范围。
- 纯 `1` / `2` 的完整选择、身份自检和角色例外读取 `开发管理/AI协作规则.txt`。
- DeepSeek 执行读取 `开发管理/DeepSeek工作提示词.txt` 的身份锚定与对应任务路由。
- `tzg-hourly-controller` wrapper 启动的外部责任方，在任何修改前必须读取 `开发管理/AI协作规则.txt` 的 Claude / wrapper 边界和 `开发管理/DeepSeek工作提示词.txt` 的 wrapper 边界。

## 外部责任方边界

- wrapper 只在协调器已 claim 合法 `external_execute` 同一卡、取得协调器租约并创建唯一 lane worktree 后启动；无合法 claim 时不得预检或空转，不得重新扫描或另选任务。
- 自动外部 Worker 只修改 `workerPaths`、运行最小充分验证并创建内部 candidate；任务状态、队列、backlog、正式 `businessCommit` 与 `handoffCommit` 只由固定串行集成器处理。
- candidate、正式提交、恢复 session、外层 Codex 边界和禁止操作的完整规则，以 `开发管理/AI协作规则.txt` 与 `开发管理/DeepSeek工作提示词.txt` 为准；不得在本文件维护第二份副本。
