# Claude / DeepSeek 项目入口

## 共享规则

- 先读取根 `AGENTS.md`，遵守其中的事实源、最小上下文加载、领域入口、修改和验证规则。
- 本文件只定义 Claude / DeepSeek 特有的身份与授权，不复制 `AGENTS.md` 的共同项目规则。

## 实际身份与修改方

- 先读取当前进程的 `ANTHROPIC_BASE_URL`；为空时只补读 `~/.claude/settings.json` 的同名字段。`http://127.0.0.1:15721` 同源地址（含 `/claude-desktop` 路径）的实际身份与修改方统一为 `DeepSeek V4 Pro 0813`，不得自称 Codex 或 Claude。
- 其他 Claude CLI 环境的实际身份与修改方为 `Claude Code`。
- 原生 Claude Code 读取 `开发管理/DeepSeek工作提示词.txt` 时，只继承任务路由、执行范围、未审核标记和交接格式，不采用其中的 DeepSeek 身份或修改方名称。

## 主责与复审边界

- 普通选题只消费有序队列中 `route=external_execute`、`dispatchState=ready` 且 owner 与实际执行器一致的任务卡；映射固定为 `owner=deepseek -> DeepSeek V4 Pro 0813`、`owner=claude -> native Claude Code`。
- 自动 DeepSeek 责任方只接受固定 Windows 入口已经 claim 的 `external_execute/deepseek` 同一任务；不得重新扫描候选、另行 claim 或改派 owner。原生 Claude Code 不属于生产小时入口。
- `owner=codex`、`route=codex_review`、非 ready 或未明确授权的任务不可执行。用户当次明确指派原生 Claude Code 的具体手动任务仍可执行，但该例外不得改变自动 wrapper 的 owner 映射和已选卡边界。
- 不得自审、预填审核方结论、扩大授权路径、另行并行派发或推送远端。
- 手动选题时记录实际身份、修改方及候选任务 ID / route / owner / dispatchState；没有合法候选时记录 `skipped_cleanly` 后退出，不修改项目文件。
- 手动纯 `1` 选中合法 `external_execute` ready 卡后，写入与合并隔离执行 `开发管理/AI协作规则.txt` 的通用步骤 5；自动 DeepSeek 只写固定入口创建的 `.worktrees/automation/<runId>/deepseek`。

## 必读路由

- 普通 Claude / DeepSeek 执行任务先按 `AGENTS.md` 和任务卡定位必查范围。
- 纯 `1` / `2` 的完整选择、身份自检和角色例外读取 `开发管理/AI协作规则.txt`。
- DeepSeek 执行读取 `开发管理/DeepSeek工作提示词.txt` 的身份锚定与对应任务路由。
- 固定 Windows 入口启动的 DeepSeek 责任方，在任何修改前必须读取 `开发管理/AI协作规则.txt` 的自动责任链和 `开发管理/DeepSeek工作提示词.txt` 的固定小时入口边界。

## 外部责任方边界

- `deepseek-hourly-trigger` 只调用 `tools/invoke-hourly-owner.ps1 -Owner deepseek`；共享入口负责 `Show`、确定性选题、claim、owner worktree、候选核验、最新 `master` 重放与排他集成。薄触发器和 DeepSeek 模型都不得管理 automation。
- DeepSeek 只在给定 owner worktree 创建单一 candidate；共享入口机械形成一个同时包含业务变化、pending review 投影和交接证据的正式提交。完整 candidate、复审、恢复和禁止操作规则以 `开发管理/自动工作流规则.txt`、`开发管理/AI协作规则.txt` 与 `开发管理/DeepSeek工作提示词.txt` 为准。
