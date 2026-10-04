# 天章小时自动化现场恢复与合同修复设计

## 状态

- 日期：2026-08-03
- 设计状态：四个设计部分已经负责人批准；2026-08-04 负责人又批准了下述验证范围收敛复核。
- 实施边界：只按本文批准范围实施；旧 candidate 端到端 fixture 的既有失配不并入本次恢复。
- 隔离说明：当前文档位于独立手动 worktree。为保持现有 QueueMaintenance 候选仍可 fast-forward，不在第一阶段恢复前把本设计分支集成到 `master`；实施时在旧 run 关闭后的最新 `master` 上建立修复 worktree，并把同一份已复核设计纳入正式修复提交。

## 目标

1. 安全收口当前 `QUEUE-MAINTENANCE` 的 `attention_required` run，并保留候选中已经确认正确的两行 backlog 事实修正。
2. 修复 decision checkpoint 在候选侧接受无效 decisionId、到飞书发送侧才失败的合同断层。
3. 按负责人已经明确选择的 A 方案，更正 `U-TZ-CHARTER-SAVE-01A` 的允许路径并重新派发，但不伪造飞书签名回复。
4. 核验并清理已经没有 runtime 所有权的冗余 worktree；有唯一内容或证据不明时保留。
5. 同步 schema 5 的当前状态，完成双 owner canary 后再恢复写入自动化。

## 非目标

- 不新增第二 runtime、通用恢复对象、通知重试队列或自动冲突解决。
- 不让普通 Codex 对话冒充飞书操作者签名，也不生成虚假的 `automationReply` 或证据哈希。
- 不恢复旧模型会话，不自动重放普通失败，不扩大册界保存任务的业务范围。
- 不修改与本次恢复无关的 Unity、BattleSim、其他内容事实或用户主工作区改动。
- 不为宿主层额外生成的 `::inbox-item` 在项目脚本中增加过滤器或兼容层。
- 不在本次恢复中修复 `tools/test-invoke-codex-candidate.ps1` 自 schema 5 candidate／runner 接口变更后遗留的 fixture 失配；该测试债在本次恢复完成后另立任务。

## 已核验事实

### 实施前 owner run 快照

- schema 5 runtime 的 Codex run 为 `3355f006-0335-4413-9404-99a745d4c611`。
- taskId 为 `QUEUE-MAINTENANCE`，状态为 `attention_required`。
- recoveryReason 为 `Codex responsibility ended with failed/codex_candidate_metadata_format_invalid`。
- owner worktree 为 `.worktrees/automation/3355f006-0335-4413-9404-99a745d4c611/codex`。
- worktree HEAD `377d0b00b57d2fbc8bb1dd8f937070b181601278` 是基于 `1f4b0a4b8caacb20d395064b325e9c8f8b174be2` 的唯一直接后继，工作树干净。
- 候选只修改 `开发管理/任务列表/内容设计任务.txt` 两行：把已经完成归档的 `C-STORY-WM-L1A` 从父任务阻塞范围移除，并补记其完成事实。
- 候选失败只因为提交消息没有 Automation 元数据，不是内容、路径或工作树验证失败。

### 决策 checkpoint

- `U-TZ-CHARTER-SAVE-01A` 当前为 `pending_decision`，不在 ready 队列。
- checkpoint SHA 为 `2255afc0fe7e5d37e26ec5cba96cf9655385798a`，只新增册界保存合同设计文档。
- 当前 decisionId `U-TZ-CHARTER-SAVE-01A-PATHS-01` 不符合飞书卡要求的 `^DEC-[0-9]{8}-[A-Z0-9]+$`。
- 候选 wrapper 只检查 decisionId 非空，飞书卡生成器才执行格式校验，因此任务已经暂停而通知返回 `INVALID_INPUT`。
- 当前 `ResumeReady` 只恢复 dispatchState 和队列位置，不会修改 `expectedPaths`；单纯补发卡片并选择 A 会保留原路径缺口，不能解决根因。
- 负责人已在本次 Codex 对话中明确选择 A：把 `开发管理/任务卡/U-TZ-CHARTER-SAVE-01.txt` 纳入允许路径后重新派发。

### 其他现场

- 主工作区保留 `.agents/summary_state.json` 与 `设计总结.txt` 两个无关修改，本设计不读取或修改其内容。
- 除当前 run 与旧 checkpoint 外，还有 3 个注册 worktree 没有 runtime 所有权，其中 2 个包含未提交路径；只能逐个取证后决定是否清理。
- 实施前 automation 配置显示两个小时入口、日报和周报均为 `ACTIVE`；执行第一步后两个写入入口保持 `PAUSED`，日报和周报未改。`开发管理/自动工作流状态.txt` 仍保留 schema 4／暂停描述，已经过期。

## 2026-08-04 验证范围收敛复核

- 根因复核确认：`13adbf5` 为 `invoke-codex-candidate.ps1` 增加强制 `-Action`，并把 `codex-cli-session.ps1` 改为要求 `--output-last-message`；旧 `tools/test-invoke-codex-candidate.ps1` 没有同步这两个 fixture 合同。继续补该 fixture 会把本次 decisionId 合同修复扩大为独立端到端测试修复，而且其 completed 场景不能直接证明非法 decisionId 的拒绝边界。
- 本次恢复撤销正式修复 worktree 中对 `tools/test-invoke-codex-candidate.ps1` 的临时修改，不再把该旧 fixture 作为本次门禁，也不补假 Codex 终态、兼容路径或额外场景。
- 本次自动化控制面验证固定为：
  - `tools/test-hourly-decision-checkpoint.ps1`
  - `tools/test-hourly-owner-adapter.ps1`
  - `tools/test-check-automation-workflow.ps1`
  - `tools/check-automation-workflow.ps1`
- decisionId 的直接合同仍按原设计验证：结构化 schema 含唯一 pattern，`Assert-Decision` 使用同一正则，`PauseDecision` 非法 ID 负例证明任务卡、队列和 backlog 均未写入，合法暂停／恢复正例继续通过。
- 旧 candidate fixture 测试债在本次恢复、状态同步、双 canary 和自动化恢复全部完成后另立独立任务；当前不创建该任务卡，不改变当前队列顺序，也不阻塞本次最小恢复。

## 总体顺序

```text
暂停两个写入自动化
  -> 恢复当前 QUEUE-MAINTENANCE run
  -> 关闭 runtime 并清理其精确现场
  -> 在独立手动 worktree 修复 decisionId 合同
  -> 按人工授权更正并重新派发 U-TZ-CHARTER-SAVE-01A
  -> 集成代码与任务投影修复
  -> 核验并清理冗余 worktree
  -> 同步自动工作流状态
  -> Codex / DeepSeek canary
  -> runtime 最终 Show
  -> 恢复两个写入自动化
```

任一步失败都停止在当前阶段，两个写入自动化继续暂停，不进入后续步骤。

## 第一阶段：恢复当前 QueueMaintenance run

### 前置快照

1. 通过 automation 管理接口暂停 `codex-hourly-worker` 与 `deepseek-hourly-trigger`；不编辑 TOML。
2. 再次执行 schema 5 `Show`，要求：
   - Codex runId、taskId、state、recoveryReason 与本设计记录完全一致；
   - DeepSeek run 为空；
   - 集成锁空闲。
3. 核对主分支仍为 `master`，当前 HEAD 与候选父提交关系仍允许 fast-forward。
4. 核对候选 worktree、branch、HEAD、唯一父链、工作树清洁和实际变更路径。

### 候选修复

1. 保留候选文件内容不变，只重写原候选提交消息。
2. 提交消息必须满足现有 Automation 合同：
   - `Automation: tzg-hourly-controller`
   - `Task: QUEUE-MAINTENANCE`
   - `State: completed`
   - `Result` 使用 `问题/完成` 单行结构；
   - `Impact` 使用 `影响/边界` 单行结构；
   - `Verify` 使用 `验证/后续` 单行结构；
   - `Plain` 使用 `发生/影响/需要` 单行结构。
3. 不修改候选 subject、文件内容、父提交或授权路径以外的任何事实。

### 验证、集成与关闭

1. 验证候选仍只有 `开发管理/任务列表/内容设计任务.txt` 一个变更路径，且内容只包含已经批准的两行事实修正。
2. 运行相关管理文本、任务投影、待提交空白与 Git 差异检查。
3. 重新读取 `master` HEAD、schema 5 runtime、集成锁和主工作区冲突路径。
4. 通过 `tools/invoke-project-integration.ps1` 取得共享排他锁并执行 fast-forward；不得直接 `git merge` 绕过锁。
5. 证明主分支包含修正后的 SHA、任务投影有效、worktree 干净且没有重复集成风险。
6. 使用 runtime 中完全一致的 recoveryReason，按现有 `CompleteRun` 精确失败关闭合同关闭旧 run；detailCode 明确记录为人工恢复后的元数据故障收口，不伪装成普通自动成功。
7. 只有在主分支可达性、worktree 路径、branch、HEAD 和清洁状态全部匹配时，才删除该 worktree 与临时 branch。

## 第二阶段：修复 decisionId 合同

### 合同定义

decisionId 的唯一格式继续使用飞书卡现有合同：

```text
^DEC-[0-9]{8}-[A-Z0-9]+$
```

不引入第二种 ID、兼容旧 ID 或发送侧自动改写。

### 修改点

1. `tools/invoke-codex-candidate.ps1`
   - 在 Codex 结构化输出 schema 的 `decisionId` 字段增加同一 pattern。
   - 在 `Assert-Decision` 中对 decisionId 做同一格式校验；无效值返回现有 `codex_decision_invalid`。
2. `tools/set-task-automation-state.ps1`
   - `PauseDecision` 在写入任务卡前拒绝无效 decisionId。
   - 使用同一正则，不增加自动修正或 fallback。
3. `tools/test-hourly-decision-checkpoint.ps1`
   - 保留合法 ID 的暂停／恢复测试。
   - 新增无效 ID 在任务卡、队列和 backlog 写入前失败的测试，并证明三个事实源均未变化。

### 结果边界

- 无效 decisionId 必须在任务状态转换前失败，不能再次出现“任务已暂停、卡片才拒绝”的半闭环状态。
- 飞书卡现有校验不放宽，发送逻辑不增加重试。

## 第三阶段：按人工授权重新派发册界保存任务

### 为什么不用自动 ResumeReady

当前选择 A 的实际效果是扩大一条已经明确批准的任务路径合同，而现有 `ResumeReady` 没有 option effect，不会修改 `expectedPaths`。为避免伪造飞书回复或加入任务特例，本次使用普通管理上下文执行一次人工投影修正。

### 任务投影

在独立手动修复 worktree 中：

1. 向 `U-TZ-CHARTER-SAVE-01A.expectedPaths` 增加：
   - `开发管理/任务卡/U-TZ-CHARTER-SAVE-01.txt`
2. 把任务恢复为原 route／owner：
   - `route=codex_execute`
   - `owner=codex`
   - `dispatchState=ready`
3. 按 checkpoint 记录的原队列位置重新插入 `开发管理/当前任务队列.txt`。
4. 把来源 backlog 投影更新为已排队。
5. 移除失效的 `automationCheckpoint`，不创建 `automationReply`。
6. `stateReason` 明确记录：
   - 负责人在 2026-08-03 的当前 Codex 对话中选择 A；
   - 已补入缺失路径；
   - 旧 checkpoint SHA `2255afc0fe7e5d37e26ec5cba96cf9655385798a` 仅作人工追溯证据；
   - 新 run 从最新 `master` 重新实施，不恢复旧模型会话或自动吸收旧 checkpoint。

### 旧 checkpoint

- checkpoint branch、SHA 与私有无效请求暂时保留。
- 不补发旧卡，不把当前对话伪造成签名回复。
- 只有新 run 成功完成相同任务、且旧 checkpoint 不含主分支缺失的唯一有效内容时，才按精确证据清理。

## 第四阶段：冗余 worktree 清理

当前需要逐个核验的无 runtime 所有权 worktree：

- `.worktrees/automation/014f81a0-fc52-41c0-b643-3b9361f5249d/deepseek`
- `.worktrees/automation/04b8d73f-e247-436f-a5d8-32b842a45bbb/codex`
- `.worktrees/automation/51efd154-de6c-4c3e-bcd0-11f99e7cc07d/deepseek`

每个对象必须证明：

1. 对应任务已经由主分支中的正式提交完成或复审关闭。
2. worktree HEAD、未提交路径与主分支内容逐文件比较。
3. 所有残留内容已经被主分支覆盖、属于已拒绝候选，或只是主分支已有文件的重复副本。
4. 没有 runtime、decision checkpoint 或人工交接仍引用该 worktree。

只有四项全部成立，才删除该精确 worktree 和对应临时 branch。任一文件包含主分支没有的可能有效内容时保留并报告，不使用 `reset`、`clean` 或广泛递归删除。

## 第五阶段：状态同步与自动化重新启用

### 状态文档

更新 `开发管理/自动工作流状态.txt`：

- schema 5 为当前 runtime；
- 两个小时入口的实际启用状态以 automation 配置为准；
- 记录本次 `3355f006-...` 元数据故障的人工恢复结果；
- 记录 `U-TZ-CHARTER-SAVE-01A` 的人工 A 授权与重新派发边界；
- 删除或改写已经失效的 schema 4、两个入口暂停和旧 owner 现场描述。

### 触发层噪音

- 当前 automation prompt 已禁止读取／写入 memory 和附加 `::inbox-item`，小时入口的 notification policy 已为 `failed_runs_only`。
- 本次不重复堆叠同义 prompt，也不在项目脚本中增加输出过滤器。
- canary 后观察一次自然轮次；如果宿主仍附加 inbox 指令，把它记录为 Codex 宿主层残余问题，不阻塞已验证的项目业务集成，也不建立重试层。

### Canary 与启用条件

在所有正式修复集成后，保持两个写入自动化暂停并依次运行：

1. `tools/invoke-hourly-owner.ps1 -Owner codex -Action Canary`
2. `tools/invoke-hourly-owner.ps1 -Owner deepseek -Action Canary`

两者都必须证明：

- 实际模型／gateway 正确；
- 结构化终态有效；
- canary runtime 与实时 runtime 隔离；
- project-owned canary worktree 成功清理；
- 主工作区 HEAD 和状态未被 canary 修改。

最终 schema 5 `Show` 必须满足两个 owner 均为空、集成锁空闲。只有这些条件全部成立，才通过 automation 管理接口恢复两个写入入口。

## 验证矩阵

| 范围 | 验证 |
|---|---|
| QueueMaintenance 候选 | 实际路径、两行语义、Automation 元数据解析、管理文本、空白、`git diff --check` |
| decisionId 合同 | 结构化 schema pattern、`Assert-Decision`、PauseDecision 非法 ID 负例、合法暂停／恢复正例 |
| 任务重新派发 | `check-task-cards.ps1` 的 `CodexDispatchReady` 后置条件、队列位置、backlog 投影、expectedPaths 字面量 |
| 自动化控制面 | `test-hourly-decision-checkpoint.ps1`、`test-hourly-owner-adapter.ps1`、`test-check-automation-workflow.ps1`、`check-automation-workflow.ps1`；不运行已明确另立任务的旧 `test-invoke-codex-candidate.ps1` fixture |
| 文本与数据链 | `check-review-text.ps1`、`check-pending-whitespace.ps1`、`git diff --cached --check`；docs／管理事实变化后运行 `check-data-chain.ps1` |
| 清理 | worktree 注册、branch、HEAD、dirty paths、master 覆盖性与引用关系 |
| 启用 | Codex canary、DeepSeek canary、最终 schema 5 `Show`、automation 配置 view |

不运行与本次变更无关的 Unity 或 BattleSim 回归。

## 停止条件

出现以下任一情况立即停止，保持两个写入自动化暂停并保留现场：

- runtime、runId、taskId、recoveryReason、worktree、branch 或 HEAD 与记录不一致；
- 主分支不再能 fast-forward，或授权路径与人工修改冲突；
- 候选内容除已批准的两行外发生变化；
- decisionId 修复需要引入兼容 ID、任务特例或第二恢复对象；
- 任务重新派发需要伪造签名回复或证据哈希；
- 残留 worktree 含主分支没有的可能有效内容；
- 任一本设计明确列出的相关测试、门禁、canary 或最终 runtime 检查失败。

## 预期终态

- `3355f006-...` 已精确关闭，批准的 backlog 修正位于 `master`，其 automation worktree 已安全清理。
- 无效 decisionId 在候选和任务投影层被拒绝，飞书发送侧不再承担首次格式发现。
- `U-TZ-CHARTER-SAVE-01A` 含完整允许路径，并在重新启用前恢复为队首 ready；启用后允许全新 Codex run 从最新 `master` 正常领取。
- 冗余 worktree 只清理已经证明无唯一内容的对象，其余现场明确保留。
- 状态文档与 schema 5／automation 配置一致。
- 双 canary 通过，两个写入自动化恢复；若宿主仍制造额外 inbox，只作为已知宿主层残余记录。
