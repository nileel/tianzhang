# 跨决策回复隔离修复与 A 证据恢复设计

日期：2026-08-14
状态：已批准

## 1. 背景与已确认事实

`U-URP-VISUAL-BASELINE-01` 的维护型决策 `DEC-20260814-QM57A9A575FD7E` 已成功投递。负责人于 2026-08-14 12:36:06（Asia/Hong_Kong）选择 A，即“改用 Universal Renderer，采用 3D Mesh、标准 3D 灯光与阴影基线”。

该回复的签名、decisionId、卡片 nonce、provider message、操作者、租户、时间窗和选项均与公开任务卡及私有绑定一致，但证据文件 `2dcd060b6c7b338d1dfee52493262833321527d972ab55a4664bea0786e34245.json` 被移入飞书桥 `quarantine/`。后续 Codex RunOnce 因此返回 `waiting_decision / maintenance_decision_no_reply`，任务仍为 `pending_decision/awaiting_reply`。

对同一证据的隔离复演已经证明：

- 使用自身维护型决策请求消费时，返回 `OPTION_ACCEPTED`、`optionKey=A`，证据进入 `processed/`；
- 先使用另一条仍在等待的旧复审返工决策请求消费时，返回无回复，并把这条合法的维护型回复移入 `quarantine/`。

因此根因不是负责人未回复、飞书桥未连接或回复签名无效，而是消费者把“属于其他决策的合法证据”和“当前决策的非法证据”合并成同一种隔离结果。

## 2. 根因

`consumeCurrentReply` 扫描共享 `inbox/` 时，先完成结构、签名和文件名校验，再调用面向单个 `pendingDecision` 的当前性检查。当前性检查同时包含 decisionId、message、nonce、操作者、租户、时间窗和选项。

现行循环对任一当前性检查失败都执行同一动作：移入 `quarantine/`。因此只要共享入口先查询另一条等待决策，本条合法证据就会因 decisionId 不同被永久隔离。`invoke-hourly-owner.ps1` 又固定先检查复审返工决策和 checkpoint，最后才在空队列条件下检查维护型决策，使该缺陷可以稳定触发。

## 3. 目标与非目标

### 3.1 目标

- 查询某个决策时，不破坏属于其他决策的结构完整、签名有效证据。
- 保持当前决策的全部身份、绑定、时间窗、选项和幂等校验不变。
- 保持真正无效、篡改或冲突证据的 fail-closed 隔离语义。
- 受控恢复本次已经验证的 A 回复，并只通过正常 QueueMaintenance 生命周期把任务转为 `ready/resolved`。
- 用自动化测试覆盖“先查询其他决策，再查询目标决策”的真实顺序。

### 3.2 非目标

- 不新增按决策分片的第二 inbox、分发器、恢复队列、兼容状态或后台重试器。
- 不修改飞书卡片、发送、绑定、TTL、HMAC、操作者配对或 provider 合同。
- 不直接手写任务卡的 resolved 状态，不绕过 QueueMaintenance、任务卡检查器或正式集成。
- 不在回复恢复轮次顺带执行 `U-URP-VISUAL-BASELINE-01` 的 Unity 业务实现。
- 不处置其他历史等待决策；它们只作为回归顺序的一部分保留。

## 4. 选择的最小设计

消费者把 inbox 证据分为三类：

| 证据类别 | 判定 | 动作 |
|---|---|---|
| 不可信证据 | JSON、结构、签名、文件名或 provider event 身份无效 | 移入 `quarantine/` |
| 其他决策的可信证据 | 结构、签名和文件名有效，但 `decisionId` 与本次查询不同 | 留在 `inbox/`，本次查询忽略 |
| 当前决策证据 | `decisionId` 相同 | 继续执行现有 message、nonce、操作者、租户、时间窗、选项和冲突检查；合法者进入 `processed/`，非法者进入 `quarantine/` |

判定顺序必须先完成结构、签名与文件名验证，只有得到可信 payload 后才比较 decisionId。不得仅从未验签 JSON 读取 decisionId 并保留文件。

对不同 decisionId 的可信证据不做 TTL 推断。当前消费者没有该证据所对应的完整 pending contract，不能仅凭本次查询断言它已过期或无主。对应决策被查询时，现有时间窗检查仍会决定应用或隔离。

同一决策内的现有规则不改变：错误 message、nonce、操作者、租户、时间、选项、重复 nonce 或冲突身份继续 fail closed；已经进入 `processed/` 的合法首个结果继续幂等返回。

## 5. 实现边界

实现修改范围固定为：

- `tools/feishu-decision-bridge/src/inbox.mjs`
  - 在验签并取得 payload 后，显式区分“其他 decisionId”与“当前 decisionId 校验失败”。
  - 其他 decisionId 的可信证据不加入当前候选，也不移动文件。
- `tools/feishu-decision-bridge/test/consume.test.mjs`
  - 把“不同 decisionId 一律 quarantine”的旧断言改为跨决策保留合同。
  - 增加真实顺序回归：决策 B 的回复先到；查询决策 A 时 B 留在 inbox；随后查询 B 时正常接受并进入 processed。
  - 保留同 decisionId 下错误 option、nonce、message、operator、tenant、过早和过晚证据的隔离负例。
- `docs/superpowers/specs/2026-08-14-cross-decision-reply-isolation-repair-design.md`
  - 保留本次已批准设计、恢复目标和停止条件，不再扩展实现职责。

不修改 `consume-reply.mjs` 的 CLI 输出合同，不修改 `invoke-hourly-owner.ps1` 的决策顺序，也不新增状态字段。

## 6. 当前 A 回复恢复

代码和测试全部通过后，按以下顺序恢复当前证据：

1. 从主工作区重新执行 schema 5 `Show`，要求 `runs.codex=null`、`runs.deepseek=null`、`integrationLockStatus=none`。
2. 重读 `U-URP-VISUAL-BASELINE-01` 任务卡、空队列和私有维护型记录，要求 decisionId、任务摘要、`awaiting_reply`、未过期和选项合同仍精确匹配。
3. 重新验证 quarantine 证据的 HMAC、文件名、decisionId、option A、message、nonce、操作者、租户和时间窗。
4. 把同一字节证据复制回 `inbox/`，先保留 quarantine 原件；不得重签、改写时间、伪造 accepted context 或直接修改任务卡。
5. 只调用一次正常 Codex `RunOnce`。旧复审返工决策查询必须忽略并保留该证据，维护型决策查询随后接受 A，建立新的 QueueMaintenance run 并通过现有正式集成完成状态投影。
6. 要求终态为维护完成，任务通过 `MaintenanceResolvedReady`，`automationDecision.status=resolved`、`optionKey=A`、`targetState=ready`，并位于当前空队列的固定位置 0。
7. 要求同一 provider event 已存在于 `processed/`，accepted-maintenance-reply 与私有维护记录均绑定同一 evidence hash。
8. 只有 processed 与 quarantine 两份内容逐字一致、正式状态已集成且 runtime 再次为空时，才删除 quarantine 中的冗余副本；processed 证据继续作为可恢复事实保留。

恢复轮次只解除决策 blocker。实际 Unity 业务由后续 `codex_execute` run 领取，不在本次修复中手动启动第二次 RunOnce。

## 7. 并发与停止条件

以下任一情况立即停止，不修补或猜测：

- 任一 owner run 非空或集成锁被持有；
- 自动化在实施期间形成与预期路径冲突的正式提交；
- 任务卡摘要、decisionId、问题、选项、route、owner、状态或队列事实发生变化；
- quarantine 证据无法重新通过原始签名和全部绑定校验；
- inbox、processed 或 quarantine 已存在同 provider event 的不同内容；
- 测试失败，或修复需要新增第二存储、兼容分支、重试层或新状态；
- QueueMaintenance 终态不是精确的 ready/resolved A 投影。

主工作区已有 `.agents/summary_state.json` 与 `设计总结.txt` 改动不属于本修复，实施、暂存和提交均不得触碰。

## 8. 验证矩阵

### 8.1 消费器

- 当前合法 option 回复：接受并进入 processed。
- 其他 decisionId 的合法回复：当前查询返回无回复，证据仍留 inbox。
- 随后用对应 pending contract 查询：同一证据被接受并进入 processed。
- 当前 decisionId 的非法 option、nonce、message、operator、tenant、时间：进入 quarantine。
- JSON、签名或文件名无效：进入 quarantine。
- 同一决策多个合法／冲突回复：保持现有最早合法身份和冲突隔离语义。
- 并发消费者：保持 `consume.lock` 的排他和幂等语义。

### 8.2 项目自动化

- 飞书桥完整测试：`npm test --prefix tools/feishu-decision-bridge`。
- 自动化静态合同和相关 QueueMaintenance 测试保持通过。
- 修改路径执行 pending whitespace 检查；暂存后执行 `git diff --cached --check`。

### 8.3 当前事件

- 修复前证据复演：错误查询会隔离，正确查询会接受，用作根因基线。
- 修复后真实顺序：旧复审查询不移动本条证据，维护查询接受 A。
- `tools/check-task-cards.ps1 -TaskId U-URP-VISUAL-BASELINE-01 -Postcondition MaintenanceResolvedReady -OutputJson` 通过。
- schema 5 最终两个 owner 均为空，集成锁空闲，任务只进入一次队列。

## 9. 完成标准

- 不同 decisionId 的可信回复不再被当前查询隔离。
- 当前决策与不可信证据的安全校验没有放宽。
- 相关单元测试和自动化合同测试通过。
- 本次负责人 A 回复通过原流程被消费，任务精确变为 `ready/resolved` 并入队。
- 合法证据存在于 processed，错误 quarantine 副本在逐字证明后清除。
- 没有新增状态、恢复层、后台任务、兼容路径或无关改动。
