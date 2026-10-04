# 飞书决策桥恢复与四张返工卡补发设计

日期：2026-08-09
适用范围：飞书决策桥计划任务、`send-decision.mjs` 的多决策绑定写入、四个既有 `review_rework` 投递失败现场

## 1. 问题与事实

2026-08-09 03:20 的返工决策卡正常到达；飞书长连接桥随后约在 03:44 停止。当前计划任务 `TianZhang-Feishu-Decision-Bridge` 为 `Ready`，最后结果为 `0xC000013A`，健康文件仍记录旧的 `CONNECTED`，但其中 PID 已不存在。因此普通任务结果仍可通过飞书 REST 发送，依赖长连接健康的返工决策卡则返回 `CHANNEL_UNAVAILABLE`。

现有计划任务已经使用直接托管的隐藏 `pwsh`、`RestartCount=3` 和 `RestartInterval=PT1M`；对应修复提交已经包含在当前 `master`。本次没有证据支持继续修改计划任务或新增 watchdog。先按现有入口恢复；如果桥再次退出，停止补发并重新诊断外部终止原因。

以下四个复审任务已经正式转为 `blocked`，但返工决策卡投递失败且规则不会自动重试：

- `DEC-20260809-REV251968CD9CBD` / `C-HS-YY-JD-01K`
- `DEC-20260809-REV166657E020FE` / `C-HS-YY-JD-01O`
- `DEC-20260809-REV551D24262AA4` / `C-HS-YY-JD-01P`
- `DEC-20260809-REV954DE7919DFB` / `C-HS-YY-JD-01Q`

四条 `review-rework-decisions` 记录均为 `delivery_failed`，`sendResult.result=CHANNEL_UNAVAILABLE`；对应 `decision-requests` 仍保存未发送的原始 `decision + attemptNumber=1`。

桥的回调层已经支持 `pending-bindings.json` 中存在多个决策绑定，并按 `decisionId` 查找；但 `send-decision.mjs` 每次成功发送后会用单元素数组覆盖该文件。直接连续补发四张卡会使前三张虽然可见，却无法通过绑定校验回复。因此必须先修正这一处写入不一致。

## 2. 目标与非目标

### 目标

- 恢复现有飞书长连接桥，并证明健康时间戳持续更新且 PID 存活。
- 发送新决策绑定时保留其他合法、未过期的决策绑定。
- 精确补发上述四张既有返工决策卡，使四张卡都可分别选择 A、B 或 C。
- 只在飞书明确接受后，把对应私有记录从 `delivery_failed` 原子转换回 `awaiting_reply`。
- 保持四张任务卡为 `codex_review/codex/blocked`，由后续小时入口消费负责人回复。

### 非目标

- 不新增自动重试队列、第二 runtime、后台 watchdog 或通用恢复入口。
- 不修改任务卡、队列、审核结论、返工选项或通知幂等语义。
- 不自动替负责人选择 A、B 或 C，也不自动启动返工任务。
- 不补发 `no_candidate`、QueueMaintenance 或已经 `PROVIDER_ACCEPTED` 的普通通知。
- 不因本次外部终止猜测性修改计划任务设置。

## 3. 代码修复

只修改 `tools/feishu-decision-bridge/src/send-decision.mjs` 及其直接测试。

`writePendingBinding` 在写入新绑定前读取现有 `pending-bindings.json`：

1. 文件不存在时使用空集合。
2. 文件存在时必须是符合现有决策绑定合同的数组；任一条目结构无效、决策 ID 重复或时间字段无效均停止写入，不覆盖现场。
3. 删除在本次发送时间之前已经过期的绑定。
4. 同一 `decisionId` 的旧绑定由本次新绑定替换；其他有效绑定保持原值与原顺序。
5. 新绑定追加到数组末尾，并继续使用同目录临时文件、`wx`、`mode=0o600` 和原子 `rename`。

如果飞书已经接受消息但绑定持久化失败，继续沿用现有 `PROVIDER_OUTCOME_UNKNOWN`，不得把不可回复的卡片报告为成功。

直接测试至少证明：

- 无现有文件时仍写入单一绑定。
- 已有另一条未过期绑定时，两条均被保留。
- 同一 `decisionId` 重发时只保留新绑定。
- 过期绑定被移除。
- 无效或重复的既有绑定使持久化失败，原文件不被覆盖。

不修改回调层、消息消费层或发送意图存储，因为它们已经支持多绑定和幂等发送。

## 4. 桥恢复与补发

代码正式集成后才恢复和补发，使服务恢复、sender 行为和私有状态迁移都绑定同一个已验证的 `master`。桥进程本身不加载 `send-decision.mjs`，不以重启掩盖发送端代码问题。

### 4.1 恢复前置条件

- schema 5 `Show` 显示两个 owner run 均为空，集成锁空闲。
- 当前 `master` 包含本次正式修复提交。
- 计划任务仍是现有隐藏 `pwsh` 动作、`IgnoreNew`、三次一分钟有限重启和零执行时限。
- 四条任务仍为 `blocked` 且不在当前队列。
- 四条私有记录、请求文件、reviewed commit、review commit 和任务摘要与本设计列出的现场一致。
- 不存在本轮未纳入清单的 `awaiting_reply` 返工决策、pending decision 或 checkpoint；若存在则停止，避免越过未处理的负责人决策。已消费但仍在七天有效期内的旧绑定只作为无害历史绑定保留到自然过期，不据此重发或重开任务。

### 4.2 恢复桥

只通过现有安装管理脚本或计划任务启动入口启动一次，不直接创建第二个 Node 进程。核验：

- 任务状态为 `Running`。
- `health.json` 为新鲜 `CONNECTED`。
- 健康 PID 存活且命令行精确指向项目桥入口。
- 在至少两个健康刷新周期内时间戳继续前进。

任一项失败都停止，不补发卡片、不改私有决策记录。

### 4.3 精确补发

按原创建顺序处理四个固定 decisionId。每项均使用原 `decision-requests/<decisionId>.json` 调用现有 `send-decision.mjs`，不重建问题、选项或摘要。

每项只在以下条件全部成立时提交私有状态变化：

1. sender 退出码为 0，且唯一输出为 `PROVIDER_ACCEPTED`。
2. 请求文件已被 sender 原子改写为合法 `pendingDecision`，其中 decisionId、选项、消息哈希和卡片哈希完整。
3. `pending-bindings.json` 包含本项及此前本轮已接受的所有决策绑定。

随后使用与共享入口 `Write-PrivateJson` 相同的 UTF-8、同目录临时文件、原子替换和私有 ACL 合同，把对应记录的 `status` 改为 `awaiting_reply`，并把 `sendResult` 改为本次 sender 的脱敏接受结果。其他字段原样保持。

如果某项失败：保留已经成功的卡片和记录，停止处理剩余项；不得重复发送已接受项，不得把失败项标为 `awaiting_reply`。发送意图存储继续提供同请求幂等证据。

## 5. 最终验证

补发完成后同时证明：

- 桥仍为新鲜 `CONNECTED` 且 PID 存活。
- 四个 sender 结果均为 `PROVIDER_ACCEPTED`。
- 四个请求均为合法 `pendingDecision`。
- `pending-bindings.json` 包含这四个未过期 decisionId；发送前已经存在且结构合法的旧绑定按代码合同保留，其中 03:20 已消费绑定只等待自然过期，不参与本轮任务状态。
- 四条 review-rework 记录均为 `awaiting_reply` 且 `sendResult=PROVIDER_ACCEPTED`。
- 四张任务卡仍为 `blocked`，队列未加入这些任务。
- schema 5 两个 owner run 仍为空，集成锁空闲。
- 主工作区人工改动未被暂存、覆盖或提交。

负责人随后可在任意顺序回复四张卡。小时入口按现有 `Find-AnsweredReviewRework` 合同每轮消费一条已回答决策；未回答决策继续保持 `awaiting_reply`。

## 6. 实施、验证与提交边界

项目代码改动只允许：

- `tools/feishu-decision-bridge/src/send-decision.mjs`
- `tools/feishu-decision-bridge/test/send.test.mjs`
- 本设计文档

最小验证为相关 Node 直接测试、飞书桥测试入口、`tools/check-automation-workflow.ps1`、预期路径空白检查和暂存差异检查。不运行 Unity、BattleSim 或数据链检查。

代码在隔离 worktree 内形成路径限定提交。合并前重新调用 schema 5 `Show`、核对集成锁、`master` HEAD 和主工作区路径冲突；只通过 `tools/invoke-project-integration.ps1` 持锁 fast-forward。补发属于正式代码集成后的外部状态恢复，不进入 Git 提交。
