# 每小时自动化暂停与 Runner 可靠性修复设计

## 1. 背景与定性

2026-07-21 的连续生产轮次暴露出三个独立缺陷：

1. `tools/codex-cli-session.ps1` 在 CLI 已返回唯一合法 `thread.started`、Resume 会话 ID 匹配且子进程退出码为 0 时，仍会因为任意一行非 JSON 子输出把结果误报为 `failed`。N-GROUP-01 实际完成并提交 `c4e9db82a5a20850814136b550711fcbd6302ce0`，但对应 Resume runner 留下了这种假失败。
2. 运行中的 `tzg-hourly-controller` 对自身调用自动化管理能力时发生稳定的重入阻塞。自身 `PAUSED` 更新和只读 `view` 均多轮、长时间不返回；同一目标从普通对话调用 `view` 可在毫秒级返回。根因边界定性为“自动化自身任务内管理同一自动化的重入阻塞”；底层具体锁等待不在项目可观察范围内。
3. `开发管理/自动工作流状态.txt` 仍把已完成的 N-GROUP-01 写成等待恢复，与当前 runtime 的 `recovery=null`、`pendingResumes=[]` 和业务提交事实不一致。

“现有权威资料不足以形成完整新任务卡”不是缺陷。当前队列维护记录 `blocked / no_complete_backlog_task_card` 符合既有业务规则，本设计不通过伪造 backlog 卡来消除该阻塞。

## 2. 目标与非目标

### 2.1 目标

- CLI runner 只依据子进程退出码、唯一合法 session 和 Resume ID 匹配判断成功；无关非 JSON 子输出不再制造假失败。
- 连续两轮相同全阻塞后，runtime 形成能够独立阻止新普通租约的逻辑暂停，即使界面状态暂时仍为 `ACTIVE`，无人值守期间也不会启动业务 worker 或写项目。
- 自动化任务不再调用自动化管理能力管理自身，不再产生长时间悬挂。
- 外部普通管理上下文可在逻辑暂停后把界面状态同步为 `PAUSED`；界面同步失败不削弱 runtime 安全闸。
- 恢复必须显式清除逻辑暂停，再由外部管理上下文把自动化设为 `ACTIVE`。
- 项目可见状态与 Git、runtime 和当前生产配置一致。

### 2.2 非目标

- 不新增第二个定时自动化、Windows 计划任务、后台守护进程或进度数据库。
- 不修改队列候选、内容冻结、任务优先级或 DeepSeek 可用性。
- 不读取、解释或输出 CLI 模型正文、原始 JSONL、prompt、回复或 child stderr。
- 不改造 Codex App 的内部自动化管理服务，也不猜测其具体锁实现。
- 不运行 Unity、BattleSim 或数据链路回归；这些领域输入未变化。

## 3. 方案选择

### 3.1 采用：runtime 逻辑暂停加外部界面同步

连续阻塞达到门槛后，现有 `blocking.pauseRequested=true` 不再只是“请求外部暂停”的提示，而是逻辑暂停的权威状态。租约工具在该状态下拒绝任何新的普通 `Acquire`，因此安全性不依赖自动化管理调用是否可用。

控制器在每轮开头读取 runtime。若 `pauseRequested=true`，它不扫描候选、不取得租约、不启动 CLI 或外部责任方，只报告逻辑暂停后结束。生产自动化状态由普通对话或其他非自动化运行上下文随后通过 Codex 自动化管理能力同步为 `PAUSED`。本次修复完成验证后，由当前普通对话执行这一步。

恢复是显式双步骤：先调用租约工具的 `ClearBlocking` 清除逻辑暂停，再通过外部自动化管理能力把 `tzg-hourly-controller` 设为 `ACTIVE`。若第二步失败，控制器仍保持界面 `PAUSED`；若误把界面先设为 `ACTIVE` 而未清除逻辑暂停，租约工具仍拒绝普通任务，保持失败关闭。

### 3.2 不采用：只保留 `ACTIVE` 并靠提示软退出

仅靠模型提示跳过工作不能形成工具级安全边界，也无法阻止错误提示或未来提示漂移取得新租约。

### 3.3 不采用：新增专用暂停自动化

第二个自动化会增加长期调度、幂等和失败恢复责任，而且尚未证明自动化运行上下文调用管理能力管理其他自动化不会遇到同类阻塞。

## 4. 组件设计

### 4.1 CLI session runner

`tools/codex-cli-session.ps1` 继续逐行尝试解析子进程 stdout，只消费 `thread.started`。空行和无法解析为 JSON 的行一律忽略，不转发、不记录正文，也不影响最终成功判定。

成功条件保持为全部满足：

- 子进程退出码为 0；
- 恰好观察到一个非空 `thread.started.thread_id`；
- `Start` 接受该 ID，`Resume` 要求该 ID 与传入 `SessionId` 逐字一致。

缺失、重复或空 session，Resume ID 不匹配，以及子进程非 0 仍失败关闭。runner 仍只在 stdout 输出一行终态 JSON，在 stderr 输出固定的 `session_started`、`running`，不泄漏 prompt 或 child 内容。

### 4.2 租约工具逻辑暂停

`tools/hourly-automation-lease.ps1` 保留 runtime schema v1 和现有 `blocking` 三字段，不做私有状态迁移。新增动作 `ClearBlocking`，并改变 `Acquire` 行为：

- 当 `blocking.pauseRequested=true` 时，普通 `Acquire` 返回 `SUSPENDED`，不创建 lease；结果只带 fingerprint 和 count。
- `Show`、决策恢复动作和已有 recovery 处理不被普通暂停清理逻辑接管。
- `ClearBlocking` 只在 `lease=null`、`recovery=null`、`pendingResumes=[]` 时成功；否则返回稳定的非成功状态且不改 runtime。
- 成功清除时把 fingerprint 设为 null、count 设为 0、pauseRequested 设为 false，不改写 `lastResult`。
- `success` 与 `refilled` 继续清除 blocking；相同 `blocked` fingerprint 连续两次继续把 pauseRequested 设为 true。

这样即使控制器提示漂移、自动化仍显示 `ACTIVE`，工具边界也禁止新普通写入者。

### 4.3 薄路由规则与规范提示

`开发管理/自动工作流规则.txt` 与 `开发管理/自动工作流控制器提示词.txt` 同步改为：

- 本轮开始先 `Show`；若逻辑暂停已生效，立即以稳定状态结束。
- 当轮记录第二次相同全阻塞后，责任方先记录结果并释放租约；控制器报告逻辑暂停，不调用 `automation_update`、不调用只读自动化 `view`、不等待管理服务。
- 自动化任务不得管理自身的 prompt、schedule、workdir 或 status。
- 界面 `PAUSED` 同步与未来恢复只允许自动化任务之外的普通管理上下文执行。
- 未确认界面 `PAUSED` 时不得声称界面已暂停；可以准确报告“runtime 已逻辑暂停，界面尚未同步”。

`tools/check-automation-workflow.ps1` 和直接测试同时增加正反契约，防止规范提示重新引入自身配置更新。

### 4.4 项目状态与生产配置

`开发管理/自动工作流状态.txt` 删除 N-GROUP-01 的过期恢复摘要，改为记录：

- N-GROUP-01 已由 `c4e9db82a5a20850814136b550711fcbd6302ce0` 完成；
- 当前 runtime 无 lease、recovery 或 pending resume；
- 连续阻塞触发逻辑暂停的真实原因是缺少可安全形成的完整任务卡；
- 自管理重入缺陷已改为工具级逻辑暂停和外部界面同步。

项目文件验证通过后，当前普通对话使用 Codex 自动化管理能力保留既有 name、kind、schedule、model、reasoning、project、workdir 和通知字段，只更新规范 prompt 并把 status 设为 `PAUSED`。不得直接编辑 `automation.toml`。随后同时通过管理能力读取和本地只读配置检查确认实际状态。

## 5. 数据流

### 5.1 进入逻辑暂停

1. 控制器完成候选扫描，选中队列维护或确认全部候选阻塞。
2. 责任方取得单写入租约，记录 `blocked` 和稳定 fingerprint。
3. 第二次相同 fingerprint 使 `pauseRequested=true`。
4. 责任方释放租约；runtime 中不保留 recovery 或 pending resume。
5. 控制器报告逻辑暂停并结束，不调用自动化管理能力。
6. 后续任何普通 `Acquire` 返回 `SUSPENDED`。
7. 外部普通管理上下文有机会时把界面状态同步为 `PAUSED`。

### 5.2 恢复

1. 负责人先解决阻塞或明确决定恢复扫描。
2. 外部普通管理上下文确认 lease、recovery 和 pending resume 均为空。
3. 调用 `ClearBlocking`；必须得到成功终态。
4. 通过 Codex 自动化管理能力把完整现有配置更新为 `ACTIVE`。
5. 读取 runtime 与自动化配置，确认 blocking 已清、生产入口为 `ACTIVE`。

## 6. 错误处理

- 非 JSON CLI 子输出：忽略；退出码与 session 不变量仍决定成败。
- 逻辑暂停期间普通 Acquire：返回 `SUSPENDED`，不写 lease。
- 存在 lease、recovery 或 pending resume 时 ClearBlocking：失败关闭，不清除任何字段。
- 外部 `PAUSED` 同步无响应或失败：保留逻辑暂停，不重试自管理调用，不声称界面暂停成功。
- 项目状态与 runtime 冲突：以 Git 提交、租约工具 `Show` 和实际自动化配置为证据，修正项目状态，不修改业务成果。
- 工作区出现任务外改动：停止提交，保留用户改动，不 stash、reset、checkout 或 clean。

## 7. 测试与验收

### 7.1 Runner 直接测试

先增加失败用例：fake Codex 输出唯一合法 `thread.started`、一行非 JSON 诊断文本并以 0 退出；现实现应误报失败。修改后该用例必须成功。现有 Start、Resume、缺失 thread、重复 thread、Resume ID 错配和非 0 退出用例全部继续通过。

### 7.2 租约直接测试

先增加失败用例证明：

- 第二次相同 blocked fingerprint 后，新的普通 Acquire 必须返回 `SUSPENDED` 且 lease 仍为空；
- `ClearBlocking` 后普通 Acquire 可再次成功；
- 存在 lease、recovery 或 pending resume 时 ClearBlocking 不得清除状态。

修改后运行完整 `tools/test-hourly-automation-lease.ps1`。

### 7.3 工作流契约测试

先修改 fixture，使契约要求包含逻辑暂停、`SUSPENDED`、`ClearBlocking`、禁止自动化管理自身和外部界面同步，并拒绝“控制器直接更新自身为 PAUSED”的旧提示。确认现有实现失败后，再修改生产规则、规范提示和检查器使测试通过。

### 7.4 最小充分验证

- `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-codex-cli-session.ps1`
- `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-hourly-automation-lease.ps1`
- `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-check-automation-workflow.ps1`
- `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-automation-workflow.ps1 -RequireLegacyRetired`
- `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-review-text.ps1 -Paths 开发管理,docs,tools`
- `git diff --check`

在项目检查通过后更新生产自动化，并确认：

- `tzg-hourly-controller` 实际为 `PAUSED`；
- 安装 prompt 与规范提示逐字规范化一致；
- runtime 的 lease、recovery 和 pending resumes 为空；
- `pauseRequested=true` 继续保护当前逻辑暂停；
- 工作区没有任务外改动。

## 8. 回滚

- 项目代码尚未更新生产自动化时失败：保持当前逻辑阻塞状态和 `ACTIVE` 配置，不做私有状态清理，修复项目切片后重验。
- 生产 prompt 已更新但 status 同步失败：新提示不再自管理，runtime 逻辑暂停继续拒绝 Acquire；保留真实 `ACTIVE` 状态并报告未同步。
- status 已为 `PAUSED` 后发现项目检查失败：保持 `PAUSED`，不自动恢复；修正项目后重跑直接检查。
- 不自动 reset、revert、删除 runtime、清理旧日志或覆盖用户改动。

## 9. 验收结论

本修复只有同时满足以下条件才算完成：

1. 非 JSON 子输出不再把成功的 CLI session 误报为失败，所有原有失败关闭条件保持。
2. `pauseRequested=true` 能在工具边界拒绝普通 Acquire。
3. 控制器不再从自身任务调用自动化管理能力。
4. 无人值守时，即使界面暂时为 `ACTIVE`，也不会启动业务责任方或获得普通写入租约。
5. 外部普通管理上下文已把当前生产入口确认同步为 `PAUSED`。
6. N-GROUP-01 项目状态与提交、归档和 runtime 一致，不再显示等待恢复。
7. 所有直接测试和最小文本检查通过，未触碰 Unity、BattleSim、内容或用户改动。
