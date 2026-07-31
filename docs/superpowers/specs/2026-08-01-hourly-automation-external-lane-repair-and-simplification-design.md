# 每小时自动化 External Lane 启动修复与后续精简设计

> 日期：2026-08-01
> 状态：用户已批准书面规格，待实施
> 适用范围：`tzg-hourly-controller` 并行 lane 生产链
> 前置设计：`docs/superpowers/specs/2026-07-31-parallel-automation-lanes-design.md`

## 1. 背景与已确认事实

2026-08-01 03:17 的自然轮次创建批次 `ce0a4ed7-7392-4d81-8744-bd6734575f93`，选中 `D-COMBAT-02A` 的 DeepSeek lane。协调器已成功创建短路径 linked worktree 并切换 lane 分支，但 external Worker 在建立 provider session 前返回：

- `workerStatus=failed`
- `integrationState=failed`
- `detailCode=external_lane_repository_invalid`
- `sessionId=null`
- 没有 candidate、canonical businessCommit 或 handoffCommit

失败 lane 没有业务修改，自动 worktree 与分支已按清理规则移除；任务仍为 `external_execute/deepseek/ready`。当前 runtime 没有 lease、batch 或 recovery，`pauseRequested=false`。

直接失败点位于 `tools/invoke-external-lane-worker.ps1` 的仓库根检查。该检查把 Git 命令失败和规范化根路径不一致合并为同一 detailCode，并丢弃 Git stderr。现存批次回归使用假 Worker，没有执行生产 external Worker 的真实仓库预检，因此无法从保留证据继续区分两个分支，也没有测试阻止该回归进入自然轮次。

## 2. 目标与非目标

### 2.1 目标

1. 在不连接 provider 的隔离 fixture 中执行生产 external Worker 的真实仓库预检。
2. 区分路径无效、Git 根查询失败和根路径不一致，保留失败关闭语义。
3. 只修改可由复现证据支持的行为，不增加重试、fallback 或放宽校验。
4. 通过聚焦回归、完整 batch 回归、生产契约检查和下一次自然轮次验证修复。
5. 自然轮次成功后，单独审计并精简并行版本相对旧串行版本产生的迁移与偶发复杂度。

### 2.2 非目标

- 不修改选题顺序、lane 配置、`maxConcurrent`、task claim 或路径授权。
- 不修改协调器租约、batch schema、恢复协议、串行集成器或任务状态迁移。
- 不修改 DeepSeek endpoint、CLI 参数、候选提交合同或审核边界。
- 不通过移除 exact Git root 校验来让失败路径通过。
- 不把启动修复与旧链删除、核心拆分压进同一提交或同一验证事件。

## 3. 第一阶段：External Lane 启动修复

### 3.1 仓库预检边界

生产 external Worker 在同一脚本内把现有仓库检查封装为单一预检函数；不新增生产文件或外部入口。现有检查顺序和安全含义保持不变，只细化仓库预检结果：

1. `RepositoryRoot` 必须是可规范化的绝对现存目录；失败返回 `external_lane_repository_path_invalid`。
2. 对该目录执行 Git 顶层查询；命令失败、空输出或非唯一输出返回 `external_lane_git_root_unavailable`。
3. 将 Git 返回根与传入根按 Windows 绝对路径规则规范化并进行不区分大小写的 exact 比较；不一致返回 `external_lane_repository_mismatch`。
4. 预检通过后，继续使用现有 batch claim、lane worktree、owner/identity、endpoint、CLI、baseCommit 和 clean worktree 核验。

不保存 provider 信息或用户标识，不把原始 stderr 写入 runtime，不自动重试 Git，也不接受父仓库根代替 lane worktree 根。

### 3.2 复现与测试数据流

不新增生产入口或预检模式。扩展既有 `tools/test-automation-lane-batch.ps1`，从测试驱动器直接调用生产 `tools/invoke-external-lane-worker.ps1`：

1. 在临时 Git 仓库的短路径 `.worktrees/automation/<batchId>/deepseek` 创建真实 linked worktree。
2. 使用测试私有 StateRoot 和 ResultPath 调用生产 wrapper，但不建立有效 batch claim。
3. 合法 worktree 应通过仓库预检并稳定停在后续 `external_lane_claim_mismatch`；这证明真实预检成功且不会到达 endpoint 或 provider。
4. 普通非 Git 目录应返回 `external_lane_git_root_unavailable`。
5. linked worktree 内的子目录应返回 `external_lane_repository_mismatch`，证明不能把仓库内部任意目录当作 lane 根。
6. 非绝对、不存在或不能规范化的路径 fixture 应返回 `external_lane_repository_path_invalid`。

fixture 只在项目批准的临时测试根内创建可验证路径，结束时按绝对前缀检查后清理；不得接触真实生产 runtime、任务队列、provider 或主工作区业务文件。

### 3.3 根因与停止条件

先在当前代码上运行同结构 canary，再修改行为：

- 若 canary 稳定复现 Git 查询失败，读取该隔离进程的直接错误并只修复已确认原因。
- 若 canary 稳定复现根路径不一致，核对传参、Git 返回根和路径规范化，只修复已确认的错误边界。
- 若完全相同的 linked-worktree 结构无法复现原失败，不猜测增加重试、兼容路径或放宽校验；只落地精确 detailCode 和缺失的真实测试，让下一次自然轮次提供新的确定证据。
- 若继续推进要求改变 lease、recovery、batch schema、Git 安全不变量或 provider 调用协议，停止第一阶段并另行设计。

### 3.4 第一阶段验证

按最小充分范围运行：

1. 扩展后的真实 external Worker 仓库预检 fixture。
2. `tools/test-automation-lane-batch.ps1` 完整回归，覆盖初始化、单 lane 失败继续、双 lane 集成和清理。
3. `tools/check-automation-workflow.ps1` 生产 prompt、仓库实现和状态契约检查。
4. 变更路径的空白检查与 `git diff --check`。
5. 下一次自然 `tzg-hourly-controller` 轮次：必须建立非空 session 或返回新的精确 detailCode；只有任务形成符合 route 的正式终态或取得可行动的新证据，才结束第一阶段。

第一阶段不得用重复手动业务 batch 代替自然轮次，也不得把 candidate SHA 当作正式交付。

## 4. 复杂度审计结论

相对并行实现前基线 `b691c3d`，当前自动化相关改动约为 4,328 行新增、1,268 行删除。新增生产链主要包括：

- `tools/automation-lane-core.ps1`：1,213 行、31 个函数、约 130 个条件控制点。
- `tools/invoke-automation-lane-batch.ps1`：870 行、14 个函数、约 85 个条件控制点。
- 两个 lane Worker：合计约 548 行。
- `tools/hourly-automation-lease.ps1`：相对基线净增加约 288 行。

旧 `invoke-codex-responsibility.ps1` 与 `invoke-external-responsibility.ps1` 合计约 1,300 行，仍被旧 decision/interruption recovery 和契约检查引用。新控制器的普通执行路径不再调用它们。

复杂度分为：

- 必要复杂度：协调器 lease、隔离 worktree、通用 `lanes[]`、task claim、路径分类、串行集成和人工冲突保护。它们直接支持已批准的并行与安全目标，保留。
- 迁移复杂度：旧 responsibility wrapper、旧 recovery 入口及其契约检查。只有证明没有活动 recovery、没有新写入者且普通路径不可达后才可退休或缩减。
- 偶发复杂度：生产 external Worker 预检未被真实测试覆盖、错误码合并、检查器同时维护新旧普通执行契约。优先收敛。

## 5. 第二阶段：独立精简

第二阶段只在第一阶段自然轮次完成后开始，使用独立设计确认、提交和验证，不与启动修复混合。

### 5.1 旧链可达性审计

1. 建立实时 controller prompt、仓库调用者、runtime action 和 recovery 创建入口的调用图。
2. 证明当前 `Show` 没有 lease、batch 或 recovery，并检查是否仍有代码能创建旧 decision/interruption recovery。
3. 将旧 wrapper 中“普通执行”“决定恢复”“中断恢复”职责分别标为活跃、仅迁移或不可达；不得按文件整体名称猜测。
4. 删除确认不可达的旧普通执行入口和对应重复契约。
5. 若旧 recovery 仍可产生，只保留恢复所需的最小入口，不在本阶段改写恢复协议。

### 5.2 核心文件的两层拆分

在旧链清理后，将当前核心只拆成两个生产文件：

- `tools/automation-lane-core.ps1`：路径与配置、schema、任务卡/队列读取、选题、Worker 终态、候选提交验证、私有 JSON 和清理判定。
- `tools/automation-lane-integration.ps1`：集成预检、coordinatorChanges 合并、候选补丁应用、任务卡后置条件、finalizer、集成快照恢复和 canonical integration。

Worker 只加载 core；batch coordinator 按固定顺序加载 core 与 integration。测试按实际消费者加载同一依赖集合。不得把它进一步拆成按单个函数或任意工具分类的四至五个碎片文件。

`tools/invoke-automation-lane-batch.ps1` 虽有 870 行，但当前函数共同拥有 batch 生命周期、进程收集、顺序集成、通知与清理状态；本阶段不因行数单独拆分。只有发现可独立描述、无共享状态且有稳定接口的第二职责时才另行设计。

### 5.3 第二阶段完成标准

- 活跃生产组件、生产代码行数或条件控制点至少一项有明确净下降，且没有用新增适配层抵消删除量。
- Worker 不再加载主分支集成实现。
- 新旧普通执行协议不再长期并存；仍保留的旧 recovery 路径必须有当前写入者或明确迁移证据。
- 选择、失败隔离、人工冲突、恢复、串行集成、DeepSeek 待复审和通知语义保持不变。
- 聚焦测试、完整自动化回归、生产契约检查与下一次自然轮次通过。

## 6. 实施顺序

1. 第一阶段：在隔离 fixture 中执行当前生产预检，取得复现证据。
2. 第一阶段：细分稳定 detailCode，只修复已确认分支并补真实 wrapper 回归。
3. 第一阶段：完成代码与契约验证，等待并核验下一次自然轮次。
4. 第二阶段：重新读取实时状态，建立旧链可达性和 recovery 写入图。
5. 第二阶段：删除不可达旧普通路径和重复契约。
6. 第二阶段：按 core/integration 两层边界移动函数，不改变行为。
7. 第二阶段：运行完整自动化回归、契约检查和自然轮次验证，并记录复杂度净变化。

任何阶段出现需要连续叠加补丁、跨越已确认边界或改变安全协议的情况，都立即停止并重新确认根因与设计。
