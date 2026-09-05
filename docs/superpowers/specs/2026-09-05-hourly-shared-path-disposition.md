# 小时责任链共享路径并发处置定案

## 结论

现阶段保留 Codex 与 DeepSeek 两个 owner 对不同 taskId 的并行开发、整份 `expectedPaths` 冻结检查、主工作区人工改动拒绝和进程持有型排他集成锁，不修改 runtime claim、定时器、重试、恢复或自动合并机制。

最小处置是把“是否应该并行”放在任务编排阶段判断：真实业务前置继续决定下游何时 ready；同一批次已有固定顺序时不提前放行下游；没有业务依赖的独立任务仍可并行开发，并由既有集成锁串行正式交付。不能仅因多个任务都声明 `开发管理/当前任务队列.txt` 或分线 backlog，就建立跨项目全局串行链。

发生路径重叠后，分类只用于解释和后续编排，不改变当前拒绝结果：现有脚本只能证明整条路径是否变化，不能证明同一文件内的两个条目可安全合并，因此仍进入 `hourly_revalidation_required` 或 `hourly_main_path_conflict`，保留现场并交普通管理上下文处理。

## 当前实现证据

- `tools/hourly-automation-lease.ps1::ClaimRun` 在同一 mutex 内写入 `runs.codex` / `runs.deepseek`；每个 owner 最多一个 run，并拒绝两个 owner 领取同一 taskId。它允许不同 taskId 同时开发。
- `tools/invoke-hourly-owner.ps1::Get-FormalPaths` 对普通任务使用完整 `metadata.expectedPaths`，DeepSeek 正式结果还包含 `开发管理/AI合作沟通.txt`。
- `Build-And-IntegrateCandidate` 先取得同一进程持有型集成锁，再读取最新 `master`。只要 `baseCommit..latest` 的任一路径与 formal paths 相同或形成父子路径重叠，就以 `hourly_revalidation_required` 停止；主工作区 staged、unstaged 或 untracked 路径重叠则以 `hourly_main_path_conflict` 停止。
- candidate 本身仍须是单提交、工作树干净、实际 changed paths 与结构化报告一致且全部位于任务卡 `expectedPaths` 内。路径检查不是自动合并替代品。

## 已发生案例

### C-HS-YY-JD-01E

- 冻结 base：`6ed22f25a8e4fb1fbf566e4e3e9d00d1aa3b0572`；DeepSeek candidate：`0bfc731df03f07cb726a6a1ee19ef7928fe3af73`。
- candidate 开发期间，`master` 进入 `5a2ab528e24f84d9bd50967c01d878fbd8003d3a` 与 `bd7269751096b12c351cc4ccaf7b4990df9b2fcd`。两者处理静态 3D 玩家资产，和 01E 水道路内容没有业务关系。
- `base..bd726975` 与 01E formal paths 的唯一重叠是 `开发管理/当前任务队列.txt`。这是其他任务完成／授权造成的机械 ready 投影变化；01E 的三份内容、索引、本卡、内容 backlog 与未通过条目没有变化。
- 自动 run 因 `hourly_revalidation_required` 停止。普通管理上下文没有恢复旧模型会话，而是在最新 `master` 上形成正式提交 `d570191a058ed0109b1116e915ab94ed494e67f5`；Codex 后续以 `4e070736532eb2b781d4c121f9555accd6947b1f` 复审通过。

### C-HS-YY-JD-01G

- 冻结 base：`13e8c3f67e59dc54acb7ad26559bc73ce37ef51f`；DeepSeek candidate：`5b7c2ce18e80dd01c8ae624aa161d683495892c9`。
- candidate 开发期间，`master` 进入 `3978b709554759d49f84b17f6884961ecbbcec6b`、`4b1426e993ed8ee11eb093dfd8a25ae0f28fb118` 与 `d3cdda380616308c0c1d23129295be734eed9f9d`。它们分别处理经验任务复审和两项静态 3D 资产，与 01G 闻识道路内容没有业务关系。
- `base..d3cdda38` 与 01G formal paths 的重叠为 `开发管理/当前任务队列.txt`、`开发管理/未通过审核清单.txt`。`3978b709` 在未通过清单新增的是 `M-EXP-TASK-SCHEMA2-01` 条目，01G 自身条目未变；两条重叠在本案都属于无关管理投影变化。
- 自动 run 同样因 `hourly_revalidation_required` 停止。普通管理上下文在最新 `master` 上形成正式提交 `12b6f587b55d37ea1c13df73508792a8ef4f06a8`；Codex 后续以 `86f1205074833ff97fa4557094802fb09ced808f` 复审通过。

两个案例证明了路径级保护存在可避免的吞吐成本，但没有证明可以删减 formal paths：当时系统没有文件内条目摘要、结构化合并证明或候选重放后的等价性证据，继续自动集成会把“语义上看似无关”当成未经验证的事实。

## 冲突分类与动作

| 类别 | 例子 | 当前动作 | 编排处置 |
|---|---|---|---|
| 任务语义或冻结输入变化 | 本任务卡摘要、route/owner/ready、业务文件、同一任务的 backlog/队列条目、同一审核条目、输入字节或哈希变化 | 必须停止；不得自动重放、解冲突或关闭 attention run | 先完成上游并重新建立新鲜任务事实，再由新 run/手动正式流程处理 |
| 人工工作区冲突或集成锁占用 | staged、unstaged、untracked 路径与 formal paths 重叠；锁等待超时 | 必须停止并保留人工改动或现有持锁者 | 不改写、不暂存、不清理人工文件；等待普通管理上下文核验 |
| 无关机械管理投影变化 | 其他任务导致 `当前任务队列.txt` 改行，或共享 backlog/审核清单只改无关条目 | 现有粒度下仍安全停止 | 对真实依赖沿已有顺序逐项 ready；已知会连续收口同一批管理投影时按固定顺序交付，避免提前并行领取下游 |
| formal paths 无重叠 | 其他任务只改独立业务路径 | 允许在最新 `master` 重放，仍须通过组合验证和锁内 fast-forward | 保持两 owner 并行开发 |

“机械管理投影”不是自动忽略白名单。只有人工取证能证明变更与本任务条目无关时，才可把事故解释为可通过编排降低的冲突；自动入口仍按整路径拒绝。

## 方案比较

| 方案 | 安全性 | 吞吐与故障影响 | 定案 |
|---|---|---|---|
| 完全维持现状、不补编排说明 | 保留全部保护 | 独立任务可能因队列或共享清单的无关变化反复停止 | 不足；缺少对可避免冲突的前置约束 |
| 现有依赖与固定顺序优先，独立任务仍并行 | 保留全部保护 | 减少已知同批管理收口重叠；独立业务仍可并行，偶发冲突进入人工处置 | 采用 |
| 所有任务全局串行 | 安全但并未增加 formal path 证明 | 任一长任务或 `attention_required` 会阻断所有 owner，两个定时器退化为单通道 | 拒绝 |

当前 schema 5 的 `attention_required` 只占用对应 owner；另一 owner 在 taskId 不同且自身无 run 时仍可工作。若改为全局串行，一个 owner 的 attention 现场会阻断另一 owner，扩大事故影响。若两个 owner 各自因并发冲突进入 attention，则两侧都会停止领取新任务；这应由普通管理上下文按既有恢复规则分别核验，不能用自动重试或自关闭规避。

## 调度规则

1. 任务依赖只表达真实语义、输入或验收前置；不得为了所有任务都会更新队列而制造全局依赖链。
2. 已有真实依赖或同一批固定顺序时，前项未正式完成并通过所需复审前，后项不进入 ready。这样可直接避免本可预见的同批管理投影并发。
3. 没有真实依赖的任务可由不同 owner 在各自 worktree 并行开发；正式重放、验证和 fast-forward 继续由同一集成锁串行化。
4. 对已知会在短时间连续关闭同一组任务卡、backlog、队列或审核条目的维护工作，按现有队列固定顺序逐项交付，不提前批量 ready；不改普通业务任务的 owner 或 route。
5. formal path、任务摘要、人工 dirty 路径或锁状态一旦变化，当前 run 仍停止。分类结论不能用于自动恢复旧模型会话或跳过最新 `master` 验证。

## 代码范围与后续门槛

本次不需要代码修改：两个已知案例可由更准确的 ready/固定顺序编排降低，而安全拒绝、双 owner claim 与持锁集成均按设计工作。

只有未来出现新的、可重复证明为“同一共享文件的无关条目变化”且任务编排无法消除的失败证据时，才另行规划结构化条目摘要或重放等价性验证。那将至少涉及 `tools/invoke-hourly-owner.ps1`、任务卡/队列检查合同及确定性测试，必须独立冻结 schema、失败行为和人工改动保护；不得在本卡扩展。

## 验收边界

- 保留任务摘要、完整 `expectedPaths`、主工作区 dirty 路径和集成锁保护。
- 不启动 live claim 或 Canary，不修改 runtime、定时器、自动合并、恢复或通知机制。
- 本卡独立归档，不解锁 `M-CURRENT-FACTS-01`、`M-HANDOFF-ARCHIVE-01`、`M-HOURLY-CHECK-01` 或 `M-HANDOFF-CLOSE-AUTH-01`。
