# DeepSeek 决策 ID 合同与异常 run 处置设计

## 目标

修复 `D-COMBAT-PROD-01` 在 `PauseDecision` 状态投影前因非法 `decisionId` 停在 `attention_required` 的问题，并防止 DeepSeek 再次产出无法被共享内核接受的决策合同。通知策略保持不变。

## 已确认事实与决定

- 异常 run 为 `125605c2-8c75-4f82-905c-ca77f33f7a2a`，owner 为 `deepseek`，当前状态为 `attention_required`。
- candidate 使用了 `DEC-20260806-DSPROD01-SCOPE`；`set-task-automation-state.ps1` 只接受 `^DEC-[0-9]{8}-[A-Z0-9]+$`。
- candidate 提交 `0d5f8f72a309fec0a419214720dc399c3ee8f301` 只修改 `开发管理/任务归档/验证记录/关中基础攻击生产迁移验证记录.txt`，没有业务变化进入 `master`。
- 用户选择原决策方案 A：授权本卡修改环境边输入，由 DeepSeek 在新 run 中继续返工。
- 不修改飞书通知策略，不恢复旧模型会话，不强行重放非法决策合同。

## 方案

### 1. 在 DeepSeek candidate 边界拒绝非法决策 ID

修改 `tools/invoke-deepseek-responsibility.ps1`：

- 结构化输出 schema 只保留 `decisionId` 的字符串类型约束，不承担格式校验。该 schema 经命令行 wrapper 传递，不能作为权威合同边界。
- 只在 `Assert-DecisionCheckpoint` 中新增与 Codex、状态投影和飞书决策卡一致的格式校验 `^DEC-[0-9]{8}-[A-Z0-9]+$`；失败统一返回既有 `deepseek_decision_invalid`，使错误停在 candidate 核验阶段，不进入共享状态转换。
- 状态投影继续保留原有的最终合同校验，但不新增状态、分支或处理层。
- 不新增兼容格式、自动改写、重试或第二套 ID 生成器。

### 2. 记录方案 A 的任务授权

修改 `开发管理/任务卡/D-COMBAT-PROD-01.txt`：

- 在 `expectedPaths` 中加入：
  - `src/Assets/DataConfig/EnvironmentProfiles.csv`
  - `src/Assets/Data/EnvironmentProfiles/EnvironmentProfile_env_guanzhong_wild.asset`
  - `src/Assets/Data/EnvironmentProfiles/EnvironmentProfile_env_guanzhong_wild.asset.meta`
- 在正文记录用户于 2026-08-06 选择方案 A，以及授权只用于补齐 `env_guanzhong_wild` 既有相邻格的反向有向边。
- 保持任务为 `external_execute/deepseek/ready`，不扩大到战斗消费者、AI、数值或其他环境档案。

`EnvironmentProfiles.csv.meta` 与 `src/Assets/Data/EnvironmentProfiles.meta` 已存在，但本次不预期变化，因此不加入授权路径。

### 3. 精确关闭旧 run，下一轮重新领取

修复提交在隔离 worktree 中准备并验证完成后，选择刚结束一次 DeepSeek `:45` 触发的安全窗口执行：

1. 从主工作区重新 `Show`，核对 owner、runId、taskId、repository、base、digest、worktree、两个 branch、提交、进程和集成锁。
2. 证明旧 candidate 未进入 `master`、state worktree 干净且没有责任方进程。
3. 使用 schema 5 `CompleteRun` 将旧 run 以 `failed` 和明确 detail code 精确关闭；不删除旧 candidate/state branch 或 worktree。
4. 再次 `Show` 确认 DeepSeek owner 为空、集成锁空闲，且修复路径不与主工作区人工改动冲突。
5. 通过 `tools/invoke-project-integration.ps1` 取得共享集成锁并 fast-forward 修复提交。
6. 任一证据变化、自动化重新 claim、锁占用或路径冲突都立即停止，不自动重试或解冲突。

下一次 DeepSeek 小时轮次从最新 `master`、更新后的任务卡和新 digest 创建全新 run；旧模型会话与非法 decision context 均不恢复。

## 测试与验收

- 扩展 `tools/test-invoke-deepseek-responsibility.ps1`，覆盖带额外连字符的 ID 在 candidate 边界被拒绝为 `deepseek_decision_invalid`；不增加 schema 源码形态断言。
- 运行 `tools/test-invoke-deepseek-responsibility.ps1`。
- 对本轮路径运行 `tools/check-pending-whitespace.ps1`，暂存后运行 `git diff --cached --check`。
- 设计文档变化后运行 `tools/check-data-chain.ps1`；不修改 Unity、CSV 内容或业务 asset，因此不运行 BattleSim 或 Unity 验证。
- `tools/test-hourly-decision-checkpoint.ps1` 和 DeepSeek `Canary` 已在相关输入未变化的上一版修复中通过，本次精简不重复运行。

## 停止条件与残余风险

- 无法精确证明旧 run、worktree、branch、提交或进程归属时，保留现场并停止。
- 关闭旧 run 后若出现新的 DeepSeek claim，停止集成并重新核对，不覆盖新 run。
- 本修复只保证发送端收到合法决策合同，不改变哪些终态发送飞书。
- 旧检查点只作为诊断证据保留；后续是否清理由新业务 run 完成后的独立证据决定。
