# 飞书决策恢复请求单文件转换设计

## 背景与根因

`DEC-20260722-U25D01B` 的回复 A 已正常进入签名 inbox，但恢复流程连续暴露两个独立缺陷：

1. 恢复中继把生产 owner `Codex/gpt-5.6-terra` 当作不允许斜杠的稳定 ID 校验，导致取得租约后立即退出。该缺陷已由生产格式回归测试修复。
2. 责任方把 `send-decision.mjs` 使用的发送请求路径保存为 `DecisionRequestPath`，而 `consume-reply.mjs` 要求该路径包含 `{ "pendingDecision": ... }`。发送请求仍是 `{ "attemptNumber": ..., "decision": ... }`，因此第二次恢复在消费回复前失败。

第二个缺陷的根因不是缺少兼容解析，而是同一路径在“发送成功”后没有完成从发送输入到恢复输入的状态转换。

## 目标

- 一个决策只使用一个私有请求文件路径。
- 提供方确认接收后，该文件原子地从发送请求转换成消费请求。
- `SaveRecovery` 在写入 runtime 前证明请求已完成转换且决策 ID 匹配。
- 当前已接收回复通过同一生产路径恢复，不手工伪造回复、不直接编辑 runtime。

## 非目标

- 不新增第二个 `.consume.json` 文件。
- 不给恢复中继增加发送/消费双格式兼容分支。
- 不修改 runtime schema，不新增状态库、重试层或恢复状态机。
- 不改变签名 inbox、pending binding、send intent 或单写入租约的既有职责。

## 数据流

1. 责任方在私有 `requests/` 下创建发送请求，并调用 `send-decision.mjs --request-file <path>`。
2. 发送器取得 `PROVIDER_ACCEPTED` 后，以同一个时间点和提供方哈希构造：
   - 既有 `pending-bindings.json` 条目；
   - 消费器要求的 `{ "pendingDecision": ... }` 快照。
3. 发送器先原子写入 pending binding，再以临时文件加原子替换把原请求路径转换为消费请求。全部成功后仍只输出既有的净化 `PROVIDER_ACCEPTED` 结果，不新增路径字段。
4. 责任方把同一个请求路径传给 `SaveRecovery`。租约工具读取并严格验证消费请求根对象、字段、时间、三个选项、哈希和匹配的 `decisionId`；验证发生在 runtime 修改前。
5. 回复到达后，恢复中继按既有 recovery 指针消费签名回复并 Resume 原 session。

若 pending binding 已写入、但请求文件原子转换失败，发送器返回 `PROVIDER_OUTCOME_UNKNOWN`。原发送请求因原子替换未完成而保持可重试；同一 send intent 会复用已接受结果，不重复发送卡片。责任方不得在该结果下调用 `SaveRecovery`。

## 组件修改

### `tools/feishu-decision-bridge/src/send-decision.mjs`

扩展现有成功持久化边界，使其同时生成 pending binding 和消费请求，并原子替换调用方传入的请求文件。接口仍为现有 `--request-file`，成功输出字段不变。

### `tools/hourly-automation-lease.ps1`

`SaveRecovery` 在保存 recovery 前验证 `DecisionRequestPath` 是匹配当前 `DecisionId` 的严格消费请求。发送请求、缺字段、错误选项、非法时间、非法哈希或不匹配 ID 均返回失败，且 lease、recovery 与 pending resumes 不变。

### 控制器规则与提示词

只补充一条明确契约：`PROVIDER_ACCEPTED` 后原发送请求路径已转换为消费请求，责任方必须把该同一路径交给 `SaveRecovery`；不得在其他发送结果下保存 recovery。

## 当前遗留恢复

修复部署后，使用当前发送请求再次调用 `send-decision.mjs`。既有 send intent 应返回缓存的 `PROVIDER_ACCEPTED`，不得发送第二张卡片，并把原路径转换为消费请求。验证路径内容与 recovery 的 `decisionId` 后，通过现有 `resume-trigger.mjs --queue` 消费仍在 inbox 的回复 A。只有原 session 清除 recovery、释放租约且工作区进入可判断终态后，才继续每日简报自动化部署。

## 验证

1. `send-decision` 测试证明接受结果会原子转换原请求路径，输出契约不增加字段；转换失败返回 `PROVIDER_OUTCOME_UNKNOWN` 并保留原发送请求。
2. `hourly-automation-lease` 测试先用发送请求复现失败，再证明合法消费请求可保存 recovery；两类失败均不修改 runtime。
3. 自动工作流检查证明规则与控制器提示词包含同一路径转换和 `PROVIDER_ACCEPTED` 门禁。
4. 恢复中继生产 owner 回归保持通过。
5. 当前生产恢复必须证明回复从 inbox 移入 processed、原 session 收到 A、runtime 最终无 lease/recovery/pending resume；不得仅依据进程启动判定成功。
