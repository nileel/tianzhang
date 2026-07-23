# Claude / DeepSeek 项目入口

## 共享规则

- 先读取根 `AGENTS.md`，遵守其中的事实源、最小上下文加载、领域入口、修改和验证规则。
- 本文件只定义 Claude / DeepSeek 特有的身份与授权，不复制 `AGENTS.md` 的共同项目规则。

## 实际身份与修改方

- 当 `ANTHROPIC_BASE_URL` 为 `http://127.0.0.1:15721/claude-desktop` 时，实际身份与修改方为 `DeepSeek V4 Pro`，不得自称 Codex 或 Claude。
- 其他 Claude CLI 环境的实际身份与修改方为 `Claude Code`。
- 原生 Claude Code 读取 `开发管理/DeepSeek工作提示词.txt` 时，只继承任务路由、执行范围、未审核标记和交接格式，不采用其中的 DeepSeek 身份或修改方名称。

## 主责与复审边界

- Claude / DeepSeek 只可领取状态为“待处理”、非复审，且主责明确为 `DeepSeek V4 Pro`、`Claude Code` 或 `Claude / DeepSeek` 的任务。
- 不得领取主责为 `Codex`、`ChatGPT5.5`、`Codex / gpt-5.5` 或未明确授权的任务；用户当次明确指派 Claude Code 的具体任务除外。
- 不得自审、预填审核方结论、扩大授权路径、另行并行派发或推送远端。
- 每次选题前记录实际身份、修改方、允许主责及候选任务 ID / 主责 / 状态；没有合法候选时记录 `skipped_cleanly` 后退出，不修改项目文件。

## 必读路由

- 普通 Claude / DeepSeek 执行任务先按 `AGENTS.md` 和任务卡定位必查范围。
- 纯 `1` / `2` 的完整选择、身份自检和角色例外读取 `开发管理/AI协作规则.txt`。
- DeepSeek 执行读取 `开发管理/DeepSeek工作提示词.txt` 的身份锚定与对应任务路由。
- `tzg-hourly-controller` wrapper 启动的外部责任方，在任何修改前必须读取 `开发管理/AI协作规则.txt` 的 Claude / wrapper 边界和 `开发管理/DeepSeek工作提示词.txt` 的 wrapper 边界。

## 外部责任方边界

- wrapper 只在调度器已选中合法候选、取得单写入租约并给出授权范围后启动；无合法候选时不得预检或空转。
- 外部责任方按专项事实源端到端完成 workspace guard、实施、最小充分验证、未审核标记、任务状态和路径限定的 `businessCommit`，随后只修改 `开发管理/AI合作沟通.txt` 创建 `handoffCommit`。
- `businessCommit`、`handoffCommit`、自动化元数据、恢复 session、外层 Codex 边界和禁止操作的完整规则，以 `开发管理/AI协作规则.txt` 与 `开发管理/DeepSeek工作提示词.txt` 为准；不得在本文件维护第二份副本。
