# Claude / DeepSeek 项目入口

- 共同规则以根目录 `AGENTS.md` 为准；近期任务与完成记录只维护 `开发计划.md`。
- 用户指定任务优先；“下一项”按开发计划当前执行区选取，不再按旧 route／owner／ready 队列领取。
- 先读取当前进程的 `ANTHROPIC_BASE_URL`；为空时只补读 `~/.claude/settings.json` 的同名字段。`http://127.0.0.1:15721` 同源地址（含 `/claude-desktop`）的实际身份与修改方为 `DeepSeek V4.1 Flash`；其他 Claude CLI 环境为 `Claude Code`。
- 保留原有无关改动；与其他 AI 并行写入时使用独立目录的 worktree／分支，主责统一核对和合并。
- 必要验证、提交和按风险独立检查遵守 `AGENTS.md`；不得预填其他审阅者结论。
- 旧自动责任链、交接和复审入口保留为历史，不自动运行或维护；未经用户要求，不恢复自动化或推送远端。
