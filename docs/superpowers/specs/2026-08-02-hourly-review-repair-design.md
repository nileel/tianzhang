# Codex / DeepSeek 小时自动化复审修复设计

## 目标

以最小改动修复 2026-08-02 最近自动化暴露的问题：

1. `codex_review` 的路径授权无法覆盖审核通过或不通过都可能修改的 `开发管理/未通过审核清单.txt`，导致合法复审无法形成 candidate。
2. Codex 自动化触发层把前台命令限制为 60 秒，短于固定入口的 3000 秒责任时限，导致外层退出而子会话继续运行。
3. `D-COMBAT-02` 的 BattleSim 验收没有精确校验 34 列表头，也没有完整断言 `ArtConfig` 与 `DivineConfig` 的攻击投影字段。
4. 实施时进一步确认 `divine_fixture_line` 声明 `mp・12`，但 `DivineConfig` 没有资源成本承载；用户已明确授权增加一个不改变既有消费者行为的可选字段。

本修复不增加重试、守护进程、第二恢复对象、新 runtime 状态或自动 worktree 清理。

## 修改范围

### 1. 复审路径合同

- `tools/set-task-pending-review.ps1` 在把 DeepSeek 任务转换为 `codex_review/codex/ready` 时，确定性地把 `开发管理/未通过审核清单.txt` 加入任务卡 `expectedPaths`；已经存在时不重复添加。
- `tools/check-task-cards.ps1` 要求所有 `codex_review` 活跃任务都显式授权该路径，使任务卡继续保持完整路径事实源。
- `tools/test-set-task-pending-review.ps1` 和 `tools/test-check-task-cards.ps1` 分别覆盖自动补入与缺失时拒绝。
- 当前 `D-COMBAT-02` 的完成证据覆盖 `开发管理/未通过审核清单.txt`、`simulations/BattleSim/BattleSimSelfTests.cs` 与经用户追加授权的 `simulations/BattleSim/GameData.cs`。

不采用包装器隐式放行路径；`tools/invoke-codex-candidate.ps1` 继续只接受任务卡 `expectedPaths` 内的修改。

### 2. 触发等待与输出边界

- 通过 Codex automation 管理能力更新 `codex-hourly-worker` 与 `deepseek-hourly-trigger`，不直接编辑 automation TOML。
- 两个触发器调用固定入口时都把命令时限设为至少 3,060,000 毫秒；工具返回运行 cell 时，只以不超过 60 秒的单次等待继续等待同一 cell，直到得到固定入口终态。
- 超时或失败后不重试、不启动第二进程。
- 触发器只报告固定入口返回的单个结构化终态；不读写 automation memory，不附加 `::inbox-item` 或业务解释。

### 3. D-COMBAT-02 验收缺口

- 在 `simulations/BattleSim/BattleSimSelfTests.cs` 中定义契约规定的精确 34 列表头，并使用顺序敏感比较；列数相同但列名或顺序错误时仍返回 `attack_profile_header_not_exact`。
- 增加一个只变异表头名称、保持 34 列数量不变的负例，证明精确表头检查生效。
- `ArtConfig` 的公共投影断言覆盖名称、倍率、MP 成本、冷却、元素和施法距离；单目标额外断言无范围配置，圆形和扇形在公共断言后继续逐字段断言范围配置。
- `DivineConfig` 在现有可选参数末尾增加默认值为 `0` 的 `MPCost`；既有静态配置和 Character 消费逻辑不改。fixture 通过命名参数传入资源成本。
- `DivineConfig` 公共投影断言覆盖名称、倍率、破防、MP 成本、冷却、元素和施法距离，再继续逐字段断言范围配置。
- 不修改生产 CSV、Unity 运行时代码、生产 asset、Character 消费行为或冷却换算规则。

## 当前 run 与任务生命周期

1. 临时暂停两个小时自动化，防止修复期间产生新的 claim。
2. 再次核验当前 Codex run、owner worktree、candidate branch、HEAD、工作树、进程和 `integrationLease`。
3. 仅当仍满足“`attention_required`、无 candidate/canonical、worktree 位于记录路径且干净、HEAD 等于 baseCommit、无责任方进程、无 integration lease”时，使用现有 `CompleteRun failed` 合同关闭旧 run；不删除 worktree 或 branch。
4. 在隔离 worktree 中实施并验证本设计的项目修改，只提交本轮路径。
5. `D-COMBAT-02` 验证通过后，按 Codex 完成边界归档任务卡、移出队列与 backlog，并删除 `未通过审核清单.txt` 中对应条目。
6. 合并前重新核验 runtime、主分支和相关路径冲突；只在无 integration lease 且主分支相关路径未变化时集成。
7. 恢复两个小时自动化原有 schedule、model、enabled 和通知设置；本轮不主动制造 QueueMaintenance 业务提交。

若第 3 或第 6 步的证据发生变化，停止恢复或集成并报告，不通过额外补丁绕过。

## 验证

- 自动化合同：
  - `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-set-task-pending-review.ps1`
  - `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-check-task-cards.ps1`
  - `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-invoke-codex-candidate.ps1`
  - `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-invoke-codex-hourly.ps1`
- BattleSim：
  - `dotnet build -c Release --no-restore "D:\天章游戏开发\simulations\BattleSim"`
  - `dotnet run --no-build -c Release --project "D:\天章游戏开发\simulations\BattleSim"`
- 相关项目检查：
  - `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-data-chain.ps1`
  - `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-review-text.ps1 -Paths AGENTS.md,CLAUDE.md,开发管理`
  - 暂存前 `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-pending-whitespace.ps1`，暂存后 `git diff --cached --check`

Unity 输入没有变化，沿用本次复审已经通过的 9 项攻击档案用例和 286/288 全量结果，不重复运行同范围 Unity 测试；两项失败为已登记且输入未变化的无关基线失败。

## 完成标准

- 新转入的 `codex_review` 任务不再因未通过清单缺少授权而无法提交审核生命周期结果。
- 两个小时触发器不会在固定入口仍运行时因 60 秒工具时限提前退出，也不会额外写 memory 或生成 inbox 指令。
- BattleSim 能拒绝同列数的错误表头，并完整验证 Art 与 Divine 投影字段；Divine fixture 的资源成本不再丢失。
- 旧 Codex run 被可核验地关闭，`D-COMBAT-02` 完成归档，runtime 中两个 owner run 与 integration lease 均为空。
- 不覆盖主工作区既有 `.agents/summary_state.json` 与 `设计总结.txt` 修改。
