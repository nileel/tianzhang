# Codex 小时自动化僵死等待线程处置设计

> 日期：2026-08-11
>
> 状态：已实施并验证通过。

## 一、问题与证据

2026-08-11 的 `codex-hourly-worker` 出现两个同时显示为运行中的 Desktop 自动化线程：

- 21:15:38 启动的线程持续对同一个 `functions.exec` cell 执行 `wait`，没有 `task_complete`；其内部 Codex candidate 已于 21:18:29 返回 `no_candidate`。
- 22:15:39 的下一次定时触发仍被调度，形成第二个等待线程；其内部 candidate 已于 22:18:19 返回 `no_candidate`，外层同样没有 `task_complete`。
- 两次 candidate 对应的 automation worktree 与临时 branch 均已清理。
- schema 5 `Show` 返回 `runs.codex=null`、`runs.deepseek=null`、`integrationLockStatus=none`。
- `deepseek-hourly-trigger` 当前为 `PAUSED`；本次重叠不是 Codex 与 DeepSeek 同时领取任务。

因此，重复的是 Codex Desktop 的外层等待线程。项目共享入口已完成 QueueMaintenance、关闭 runtime 并释放集成锁；没有两个项目任务同时写入。故障边界位于共享入口结束之后、Desktop 等待 cell 收到完成信号之前。

## 二、目标与非目标

### 目标

1. 先暂停 `codex-hourly-worker`，阻止新的 `:15` 触发继续叠加。
2. 只停止 21:15 与 22:15 两个已证实僵死的外层等待线程，并清理它们对应的会话级残留进程。
3. 保持项目 schema 5 runtime、owner 互斥、选题、claim、worktree、集成锁和通知合同不变。
4. 通过一次 Codex Canary 与一次空队列 `RunOnce` 证明 Desktop 能收到共享入口终态并结束线程。
5. 仅在全部验证通过后恢复 `codex-hourly-worker`；任一验证失败时保持暂停。

### 非目标

- 不修改 `tools/invoke-hourly-owner.ps1`、candidate wrapper 或 schema 5 runtime 来掩盖 Desktop 返回故障。
- 不新增终态旁路文件、第二 runtime、持久重叠租约、后台守护、重试层或兼容状态。
- 不删除历史 automation worktree、branch、session 或证据文件。
- 不启用或修改 `deepseek-hourly-trigger`、日报、周报及其他 automation。
- 不执行 QueueMaintenance 之外的业务任务，不改任务卡或队列。

## 三、采用方案

采用“暂停、精确停止僵死线程、重置会话工具宿主、原链路验证、恢复”的处置方案。

不采用项目终态旁路文件：它会新增传输状态，并在尚未查明 Desktop 工具宿主为何未回传完成信号时形成猜测性兜底。不采用跨小时持久租约：它只能阻止第二次领取，无法结束第一个僵死线程；现有 schema 5 已经证明项目写入没有并发。

## 四、处置流程

### 4.1 暂停与现场确认

1. 通过 Codex automation 管理能力读取 `codex-hourly-worker` 的实时完整配置并设为 `PAUSED`；保留 prompt、schedule、model、reasoning effort、notification policy、execution environment、project 和 cwd，不直接编辑 TOML。
2. 再次调用 schema 5 `Show`。只有两个 owner run 都为空且集成锁为空时继续；否则停止并按对应 run state 进入人工恢复规则。
3. 记录两个目标线程的 session ID、启动时间、最后事件和对应 candidate 完成证据；停止目标精确限定为：
   - `019ff0f6-df69-7c11-8366-cade508914e5`（21:15）；
   - `019ff12d-d188-7ad0-aeb0-791d90f3f9ba`（22:15）。
4. 不把当前人工诊断对话、两个已完成 candidate session 或其他 automation session 纳入停止范围。

### 4.2 停止僵死线程与会话残留

1. 优先使用 Codex Desktop 对目标线程的停止能力，使两个等待 cell 进入明确终态。
2. 停止后重新枚举进程，只对能够通过父子关系、创建时间和 session 证据同时绑定到这两个线程的会话级 `node_repl`／MCP 子进程执行清理。
3. 如果无法把进程精确绑定到目标线程，不按名称批量结束 `node.exe`、`pwsh.exe` 或 `codex.exe`；改为保持 automation 暂停并报告需要重启 Codex Desktop。
4. 不自动重启整台机器，不停止主 Codex app-server，不终止当前人工对话。

### 4.3 原链路验证

1. 停止后确认两个目标 session 不再产生新的 `wait` 事件；schema 5 仍为空，集成锁仍为空。
2. 先通过 Node REPL 读取当次 `nodeRepl.requestMeta['x-codex-turn-metadata'].model`，再把该精确值传给 `tools/invoke-hourly-owner.ps1 -Owner codex -Action Canary -Model`，核验真实模型、结构化终态、主工作区隔离和成功清理；元数据缺失或不符合 `gpt-...` 命名边界时停止验证。
3. 在队列仍为空的前提下，通过 `codex-hourly-worker` 的正式触发合同执行一次受控 `RunOnce`，预期为 QueueMaintenance 的 `no_candidate/no_runnable_candidate/cleanup=cleaned`。
4. 验收不只看项目 runtime 已清空：Desktop 外层线程必须收到同一结构化终态、写出唯一最终回复并产生 `task_complete`，且不再继续 `wait`。
5. 验证期间不得由定时器产生新的并发触发；若验证跨越下一次 `:15`，保持 automation 暂停直至验证结束。

### 4.4 恢复

1. Canary、空队列 `RunOnce`、外层 `task_complete`、runtime、锁和清理证据全部通过后，通过 automation 管理能力把 `codex-hourly-worker` 恢复为 `ACTIVE`。
2. 恢复时只改变 status，其他配置逐字段保持暂停前实时值。
3. 恢复后读取配置确认 `status=ACTIVE` 与 `BYMINUTE=15`；不立即额外再跑一轮业务任务。

## 五、失败处理与停止条件

- 暂停失败或无法读取完整实时配置：立即停止，不编辑 automation TOML。
- 任一 schema 5 owner run 非空或集成锁被持有：不停止线程、不运行 Canary，转入对应人工恢复判断。
- 目标线程身份、session 或进程归属无法精确证明：不杀进程，保持暂停并请求用户重启 Codex Desktop。
- Canary 失败、空队列 `RunOnce` 再次卡住、没有 `task_complete`、出现 worktree／branch 残留或 runtime／锁不干净：保持暂停，保留证据；不得添加旁路、重试或新状态继续修补。
- 若重启 Codex Desktop 后仍能在干净工具宿主上稳定复现，再以新的证据单独设计 Desktop 传输层修复；本轮不预先实现猜测性代码补丁。

## 六、验证与完成条件

以下条件全部满足才算处置完成：

1. `codex-hourly-worker` 在处置期间保持暂停，没有第三个定时线程启动。
2. 两个指定僵死线程停止，且不再追加 `wait`；未误停其他 session 或 automation。
3. schema 5 两个 owner run 为空，集成锁为空，主工作区状态未被本次处置改变。
4. Codex Canary 返回单个合法终态并成功清理。
5. 空队列 `RunOnce` 返回 `no_candidate`、`taskId=QUEUE-MAINTENANCE`、`detailCode=no_runnable_candidate`、`cleanup=cleaned`，Desktop 外层同时产生 `task_complete`。
6. `codex-hourly-worker` 按暂停前配置恢复为 `ACTIVE`；DeepSeek 与其他 automation 配置没有变化。

## 七、实施结果

处置于 2026-08-11 23:04（Asia/Hong_Kong）完成：

- 已通过 Codex Desktop automation 管理界面暂停 `codex-hourly-worker`；未直接编辑 automation TOML。
- 暂停后，22:15 线程于 22:47:51、21:15 线程于 22:54:34 自然写出 `task_complete`。由于目标线程已经明确结束，不再执行停止或进程清理，避免误杀其他会话。
- 两个历史线程的最终终态均为 QueueMaintenance 的 `no_candidate/no_runnable_candidate/cleanup=cleaned`；schema 5 随后确认 `runs.codex=null`、`runs.deepseek=null`、`integrationLockStatus=none`。
- 通过 Node REPL 读取实际模型 `gpt-5.6-sol` 后执行 Codex Canary，返回 `status=verified`、`identity=Codex`、`privateState=isolated`，耗时 43.4 秒。
- 在 automation 保持暂停期间，通过其 Desktop“立即运行”入口执行受控空队列 `RunOnce`。外层 session `019ff155-390d-7113-8c23-8a7d334b40eb` 收到 run `74441c6b-9b86-4fbf-8179-b4cc10368e82` 的 `no_candidate/no_runnable_candidate/cleanup=cleaned` 终态，并于 23:03:05 写出唯一 `task_complete`；外层仅执行 4 次 `wait`。
- 验证完成后通过 Desktop 恢复 `codex-hourly-worker`。最终配置为 `status=ACTIVE`、`rrule=FREQ=HOURLY;INTERVAL=1;BYMINUTE=15`、`model=gpt-5.6-terra`、`reasoning_effort=high`；DeepSeek 仍为暂停状态。
- 最终 schema 5 复核再次确认两个 owner run 为空、活动 taskId 为空、集成锁为空。
