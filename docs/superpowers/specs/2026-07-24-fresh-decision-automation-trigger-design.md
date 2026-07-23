# 决策回复改为后续自动化新会话触发设计

## 背景

现行决策链在负责人回复后立即由飞书桥启动模型，并恢复原 CLI session。2026-07-24 的
`DEC-20260724-ENVPROFILE = A` 已被飞书桥验收并移入已处理证据，但原 session 没有产生恢复轮次，
runtime 也没有保留可再次消费的回复；下一次整点控制器又在没有决定正文的情况下空恢复原 session，
最终重新记录 `waiting_decision`。

负责人已决定停止“决策回复后恢复原会话”，改为由后续整点自动化创建新会话重新触发同一任务。

## 目标

- 飞书桥只验收并持久化签名回复，不启动模型、不取得项目写入租约。
- 下一轮整点自动化发现当前 decision recovery 已有有效回复后，创建新的 CLI-native 责任方会话。
- 新会话收到原 `TaskId`、精确 `DecisionId` 和负责人原始选项或自定义答复。
- 已处理回复可以幂等读取；新会话启动或执行失败时，后续整点仍可基于同一签名证据再次新建会话。
- 当前已处理的 `DEC-20260724-ENVPROFILE = A` 无需负责人再次回复。

## 非目标

- 不改变 interruption recovery：存在 task-owned 未提交改动时，仍只允许原责任方 session 恢复。
- 不让飞书桥修改项目文件、提交 Git、管理自动化配置或直接启动责任方。
- 不新增决策数据库、进度状态机、重试定时器或第二套任务队列。
- 不在本次修改中执行 `DEC-20260724-ENVPROFILE = A` 对应的业务队列维护；恢复自动化后由下一轮自然触发。

## 设计

### 1. 回复接收

飞书桥继续验证租户、操作者、卡片 nonce、消息与决定编号，并把签名事件写入私有 inbox。
验收完成后不再调用 post-accept relay，也不启动 PowerShell、Codex 或外部 AI。

### 2. 回复事实源

`consume-reply.mjs` 继续以 decision request 和签名 inbox/processed 证据为唯一回复事实源。
当匹配回复已经从 inbox 移入 processed 时，重复消费必须返回同一份已验收结果，而不是 `NO_REPLY`。
因此崩溃窗口不会把负责人回复变成不可恢复状态。

### 3. 后续整点触发

控制器 `Show` 发现 `recovery.trigger=decision` 时：

1. 不调用旧 session `Resume`，也不按裸 `DecisionId` 直接取得恢复租约。
2. 调用固定 decision trigger 检查并消费当前 decision request。
3. 没有回复时返回 `waiting_decision`，不取得租约、不启动会话。
4. 有有效回复时，为 recovery 中的同一 `TaskId` 取得单写入租约。
5. 通过 `invoke-codex-responsibility.ps1` 的 `Start + Recovery` 路由创建全新 CLI session，
   并经标准输入传入精确决定回复。
6. 等待固定调用器返回唯一终态；提交核验、结果记录、recovery 清理和租约释放仍由固定调用器负责。

### 4. Runtime

decision recovery 不再保存或使用 `resumeKind`、`resumeId`，也不使用 `pendingResumes`。
runtime 升级时把旧 decision recovery 的 session 字段和旧 pending resume 丢弃，但保留
`TaskId`、owner、repository root、`DecisionId`、decision request path 和现有结果。

interruption recovery 保留 `resumeKind`、`resumeId`、changed paths 和原 session 恢复语义。

### 5. 当前状态迁移

现有 runtime 中的 `DEC-20260724-ENVPROFILE` recovery 在读取时迁移为无 session 的 decision recovery。
现有 processed 签名证据中的 `A` 通过幂等消费重新可见。恢复控制器后，下一轮整点自动化应创建新
session 处理 `QUEUE-MAINTENANCE`，而不是恢复 `019f8fc4-fe6a-7fa3-96f4-2b86d1ca34d9`。

## 失败处理

- 没有回复：保持 decision recovery，报告 `waiting_decision`。
- 回复无效或与 recovery 不匹配：不取得租约，报告失败并保留原 recovery 与证据。
- 取得租约后新会话失败且无项目改动：固定调用器记录失败并释放租约；processed 回复仍可由后续整点读取。
- 新会话产生未提交改动：按既有 interruption recovery 保存新 session 与精确 changed paths。
- 新会话提交成功：固定调用器核验提交后清除 decision recovery。

## 验证

- 回复回调验收后只写签名证据，不调用任何模型启动依赖。
- 同一 option/custom 回复从 inbox 首次消费与从 processed 再次消费得到相同结果。
- decision recovery 无回复时不取得租约、不启动会话。
- decision recovery 有回复时调用 `Start`，且参数中没有旧 `SessionId`。
- interruption recovery 仍调用 `Resume` 并使用原 session。
- schema 迁移删除旧 decision session/pending resume，同时保留 interruption session。
- 自动工作流契约检查、决策桥测试、lease 测试、固定调用器测试和 PowerShell 语法检查全部通过。
