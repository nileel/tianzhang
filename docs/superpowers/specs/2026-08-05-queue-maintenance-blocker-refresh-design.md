# 空队列维护阻塞状态刷新设计

> 状态：负责人已锁定。本文只修复 QueueMaintenance 信任陈旧 backlog 阻塞投影、因而漏建下游任务卡的问题；不新增 runtime 状态、调度器、接口或补偿流程。

## 一、问题与根因

8 月 5 日空队列维护期间，`开发管理/任务列表/场景与Unity任务.txt` 仍把 `U-GZ-FORMAL-E2E-01` 标为“阻塞（U-BOUNTY-01C）”，但 `U-BOUNTY-01C` 已以 `dispatchState=completed` 移入 `开发管理/任务归档/U-BOUNTY-01C.txt`。QueueMaintenance 扫描了 backlog 和现有任务卡，却直接把 backlog 的阻塞文字当作当前事实，没有用活跃任务卡与完成归档核对命名 blocker，最终连续返回 `no_runnable_candidate`。

根因包含同一事实漂移的两个环节：完成事件没有同步刷新下游 backlog 投影；空队列维护又没有在作出 `no_candidate` 判断前校验阻塞状态是否仍然有效。现有 `check-task-cards.ps1` 只校验已经存在的任务卡及其投影，不能发现“下游卡尚未建立且其 backlog blocker 已完成”的漏项。

## 二、采用方案

只加强现有空队列维护合同：队列为空时，QueueMaintenance 检查 backlog 中所有阻塞任务的阻塞状态是否仍为最新，再决定是否建立 ready 卡。它继续使用现有 backlog、活跃任务卡、完成归档、任务卡和队列，不增加新的中间状态或 cleared-dependency 接口。

未采用以下扩展方案：

- 不在 runtime 增加已解除依赖列表；这是可从现有事实源重新判断的派生状态。
- 不新增第二层调度或补偿任务；现有 QueueMaintenance 已是空队列补位入口。
- 不把修复扩大为所有规划内容的自动推导；本轮只验证 backlog 已明确写出的 blocker。

## 三、控制流

1. 只有当前 ready 队列为空时才进入 QueueMaintenance；队列非空时保持现有选择与执行流程。
2. 读取各分线 backlog，检查其中所有明确标为阻塞的任务。
3. 对阻塞描述中明确出现的稳定任务 ID，依次核对 `开发管理/任务卡/<ID>.txt` 与 `开发管理/任务归档/<ID>.txt`。backlog 中“阻塞”字样本身不能证明该前置仍未完成。
4. 按当前事实刷新阻塞投影：
   - 前置仍未完成：保持阻塞；
   - 命名前置已完成但仍有其他真实条件：改写为实际剩余 blocker；
   - 全部前置已完成且资料足以满足 runnable 合同：建立完整任务卡、同步 backlog，并按既有排序规则加入队列。
5. 完成全部阻塞项核对后仍没有合法候选，才允许无修改返回 `no_candidate`。
6. 本轮只维护状态与制卡，不执行新建立的业务任务。

## 四、事实优先级与失败关闭

- 活跃任务的当前状态以 `开发管理/任务卡/<ID>.txt` 为准；已完成状态只以精确路径 `开发管理/任务归档/<ID>.txt` 为准。
- 命名 blocker 在活跃任务卡和完成归档中都不存在时，保持阻塞，不猜测完成状态。
- 同一 ID 同时存在活跃卡与完成归档等事实冲突时，保持阻塞，不提升下游任务。
- 内容冻结、负责人授权、待决定、项目闸门等非任务 ID 条件继续按既有事实源判断；不得因某个任务 ID 已完成而一并清除。
- 无法从现有事实形成完整 `expectedPaths`、验证、完成条件和停止条件时，不制造 ready 卡。
- QueueMaintenance 仍只能修改现有允许集合中的管理路径；没有事实变化时不制造提交。

## 五、最小改动范围

只修改三个文件：

1. `tools/invoke-codex-candidate.ps1`
   - 扩充 `QueueMaintenance` 的 route instruction，明确“扫描全部 backlog 阻塞项 → 用活跃卡/完成归档核对命名 blocker → 先刷新投影和制卡 → 最后才可 no_candidate”的顺序。
   - 保留现有结构化终态、路径白名单、候选提交、后置检查和共享集成合同。
2. `开发管理/状态与建议维护规则.txt`
   - 在空队列维护规则中补充事实优先级和 `no_candidate` 前置要求，使稳定规则与执行提示一致。
   - 保留 `check-task-cards.ps1 -OutputJson` 的 `readyCount` 作为现有卡候选证据，但明确它不能替代对 backlog 命名 blocker 的新鲜度检查。
3. `tools/test-invoke-codex-candidate.ps1`
   - 沿用 fake Codex 的 prompt trace，只新增 QueueMaintenance prompt 合同断言。

不修改 `check-task-cards.ps1`、`invoke-hourly-owner.ps1`、schema 5 runtime、队列排序规则或任务卡格式。

## 六、验证

最小自动化验证为：

- `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-invoke-codex-candidate.ps1`
- `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-review-text.ps1 -Paths AGENTS.md,CLAUDE.md,开发管理`
- 暂存前对三个预期修改路径运行 `tools/check-pending-whitespace.ps1`，暂存后运行 `git diff --cached --check`

测试需证明 QueueMaintenance prompt 同时包含以下合同：扫描全部 backlog 阻塞项；按命名任务 ID 查询活跃卡和完成归档；陈旧 backlog 不能直接作为未完成证明；刷新完成后才可返回 `no_candidate`；本轮不执行新业务任务。Execution、Review 和 Canary 的既有提示及行为不变。

测试只复用现有 fixture 捕获 QueueMaintenance prompt，不新增产品运行时状态、第二调度器或全量自动化回归；若实施时发现仅靠上述三处无法表达或验证合同，则按停止条件返回重新判断根因。

## 七、验收标准

- 队列非空时，现有流程不变。
- 空队列维护会检查 backlog 中所有阻塞任务的阻塞状态是否仍然有效。
- 对 `U-GZ-FORMAL-E2E-01` 这类显式依赖已完成归档任务的条目，不再因陈旧投影直接返回 `no_candidate`；先刷新投影，再在资料充分时建立完整任务卡并入队。
- 真实未完成、非任务 ID 阻塞、事实冲突或制卡资料不足时继续保持阻塞。
- 只有完成全部检查且没有合法候选时才返回 `no_candidate`。
- 未引入新状态、接口、调度器、重试层或兼容分支。

## 八、停止条件

- 若 blocker 不是 backlog 已明确写出的稳定任务 ID，需要推测自然语言或新增规划事实，停止并报告缺失事实。
- 若修复需要修改 runtime、任务卡 schema、调度入口或 `check-task-cards.ps1` 才能成立，停止并重新确认根因与范围。
- 若目标文件与活动 owner run、集成锁或主工作区改动发生路径冲突，不继续写入或集成。
