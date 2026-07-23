# 自动化与手动开发并行隔离设计

## 背景

每小时自动化执行 `N-AI-01A` 时，手动 Codex 对话直接在主工作区创建了提交。固定调用器因此观察到“出现新提交，但提交不符合本轮自动化元数据”，返回 `unverified_commit_shape`。该分支没有记录结果、释放租约或保存已有任务改动，导致过期租约残留，原 CLI session 也没有进入 interruption recovery。

问题不是自动化不能与手动对话同时工作，而是两者同时写入同一个主工作区，以及固定调用器的异常分支没有完成既有收尾协议。

## 目标

- 自动化继续使用现有主工作区和单写入租约，不改调度架构。
- 手动对话需要写项目文件时使用隔离 worktree；只读对话不受影响。
- 固定调用器遇到并发提交形状异常时，仍保存可恢复现场或至少记录失败并释放租约。
- 恢复本次 `N-AI-01A` 的原 session 和三个未提交路径。

## 非目标

- 不让每轮自动化动态创建、合并或删除 worktree。
- 不修改 workspace guard、提交 finalizer、租约 schema 或自动化配置。
- 不增加新的恢复类型、重试层、状态机或自动合并机制。
- 不自动 reset、stash、clean 或覆盖当前未提交文件。

## 设计

### 1. 并行写入边界

自动化仍在主工作区运行。手动 Codex 对话准备写入项目文件时，若主工作区存在自动化租约记录，则在 `.worktrees/` 下创建隔离 worktree，并在独立分支提交。只有重新调用 `hourly-automation-lease.ps1 -Action Show` 得到 `lease=null`，且主工作区改动与待合并路径不冲突时，才把手动分支合并回主分支。

该规则只需在 `AGENTS.md` 增加一条简短约束。它不要求只读查询创建 worktree，也不改变自动化本身的执行位置。

### 2. 固定调用器收尾

`invoke-codex-responsibility.ps1` 保留现有成功判定优先级：

1. 唯一且带正确自动化元数据的业务提交继续按成功收尾。
2. 成功条件不成立，但相对启动基线存在新增未提交路径且有唯一 session ID 时，按现有 interruption 协议保存原 session 和新增路径，记录失败并释放租约。
3. 存在无法归属的新提交但没有新增未提交路径时，记录 `blocked / unverified_commit_shape` 并释放租约。
4. 没有唯一 session ID 且存在任务现场时，继续保留租约并转人工阻塞；不伪造恢复指针。

实现只调整现有分支顺序并补齐已有 `Close-Run` 调用，不引入新的状态或辅助组件。

### 3. 本次现场恢复

使用现有租约工具，把 run `bb10b771-1717-4f18-9cbc-7a99f643c176`、原 session `019f8e45-8de8-7522-a455-cda2d21e4aa4` 与当前三个新增路径保存为 interruption recovery，记录本轮失败并释放残留租约。之后只允许该任务通过现有 `Acquire -ResumeRecovery` 和固定调用器 `Resume` 继续。

恢复操作不修改三个业务文件；原责任方完成并提交后，确认 `lease=null`，再合并本修复分支。

## 验证

在 `tools/test-invoke-codex-responsibility.ps1` 增加两个回归场景：

- 出现无关提交且仍有新增未提交任务路径：返回 interruption，恢复指针保存原 session 与路径，租约已释放。
- 出现无关提交但没有新增未提交任务路径：返回 `unverified_commit_shape`，结果已记录，租约已释放。

随后运行该脚本的全部既有场景，确认正常成功、决策恢复和无终态行为不变。涉及 `AGENTS.md` 时再运行审核文本检查，并按项目规则执行本轮路径的空白与 staged diff 检查。

## 停止条件

如果修复需要修改 workspace guard、租约 schema、自动化配置，或增加新的恢复状态，立即停止并重新核对根因；这些变化不属于本设计。
