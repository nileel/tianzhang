# DeepSeek 自动任务默认 `src/` 授权设计

## 一、状态与决定

- 日期：2026-08-06。
- 决定方：项目负责人。
- 已选方案：所有 `external_execute / deepseek` 自动任务默认获得整个仓库相对目录 `src/` 的候选修改权限；非 `src/` 路径继续由任务卡逐项授权。
- 本文只冻结后续实现合同，不实施代码、不改任务卡、不处置当前活动 runtime。

## 二、背景与根因

当前 DeepSeek candidate 的授权只来自任务卡 `expectedPaths` 的精确文件列表。`invoke-deepseek-responsibility.ps1`、`invoke-hourly-owner.ps1` 的 candidate/checkpoint/formal 核验又分别使用精确包含判断。Unity 批处理可能在 `src/` 下创建合法的新 `.meta` 或同步生成资产；只要新文件没有预先列入任务卡，即使业务改动已形成合法 candidate，也会因路径或工作区清洁闸门停止。

`U-GZ-UI-TEXT-01` 的 run `914ea350-4d43-4673-86e9-a994df566578` 暴露了该问题：DeepSeek 已形成候选提交 `c4f0e12076b0774e3afb208ef00e7526f95e492e`，Unity 另生成未跟踪 `src/Assets/DataConfig/CharterSites.csv.meta`；wrapper 在 candidate 终态核验时返回 `deepseek_worktree_dirty`，runtime 留在 `attention_required`。后续小时轮次按现行恢复规则只能返回 `existing_run`。

项目既有分工已经允许 DeepSeek 在事实源、目标语义、停止条件和验收冻结后承担大型、跨模块 Unity 实施。继续逐文件列举所有潜在 `src/` 生成文件，与该执行模型不匹配，并把可机械处理的 Unity 文件生命周期升级成负责人决策。

## 三、目标与非目标

### 3.1 目标

1. DeepSeek 的 `external_execute` candidate 可修改、创建、删除 `src/` 下任意文件，无需为每个新文件追加任务卡路径。
2. `docs/`、`tools/`、`开发管理/`、`simulations/` 和仓库其他目录继续按任务卡精确授权。
3. candidate、decision checkpoint、共享入口 candidate 复核、最新 master 正式重放和组合验证使用同一授权语义。
4. 授权范围与冲突范围分离：允许范围可以是整个 `src/`，但重放、重验证、主工作区冲突与正式提交只针对 candidate 实际改动的精确文件。
5. 保留任务语义、停止条件、工作区清洁、人工改动保护、集成锁和 Codex 独立复审。

### 3.2 非目标

- 不给 Codex、Claude 或手动脚本增加隐式 `src/` 权限。
- 不删除任务卡 `expectedPaths`；它继续表达预计业务路径和所有非 `src/` 授权。
- 不允许 DeepSeek 修改 `.git/`、仓库外路径或通过 `src2/`、路径穿越、符号混淆命中 `src/`。
- 不允许以目录授权绕过任务正文、禁止项、完成条件或停止条件。
- 不把目录授权当成删除证明；删除 `src/` 文件仍须核对代码引用、Unity YAML/GUID、Resources/addressable 路径和运行时可达性，并在 candidate 证据中说明。
- 不自动提交所有 Unity 缓存；只有 `src/` 内的源文件、场景、资产和 `.meta` 属于本授权，`Library/`、`Temp/`、`Obj/`、日志及生成工程文件仍不在范围内。
- 不在本设计中自动恢复、关闭、清理或集成历史 run `914ea350-4d43-4673-86e9-a994df566578`。

## 四、方案比较

### 4.1 已选：DeepSeek 全局隐式 `src/` 根授权

共享策略按 `owner=deepseek` 且 `route=external_execute` 注入仓库相对授权根 `src/`。任务卡无需批量改写，新任务自动继承，非 `src/` 仍读取 `expectedPaths`。

优点：一次解决 Unity 新文件和跨模块实现的机械授权问题；任务卡仍保留预期文件清单；不增加逐卡决策字段。

### 4.2 未选：任务卡 `allowSrcTree=true`

每张外部任务卡决定是否放开 `src/`。隔离更细，但仍会把常规 Unity 实施变成重复授权，并要求迁移任务卡 schema、制卡逻辑和队列检查。

### 4.3 未选：继续精确列举，只补常见 `.meta`

改动最小，但只能修复已知文件；下一个新场景、测试、asset 或 `.meta` 仍会重复阻塞，不能满足负责人确认的默认开放目标。

## 五、统一授权合同

新增唯一共享策略所有者，建议为 `tools/hourly-path-authorization.ps1`，供 DeepSeek adapter 与共享入口共同 dot-source。不得在多个脚本分别复制前缀判断。

策略输入：

- `owner`
- `route`
- 任务卡 `expectedPaths`
- 待核验的仓库相对文件路径

策略输出：

- 精确授权文件集合：任务卡 `expectedPaths`
- 目录授权根集合：仅当 `owner=deepseek` 且 `route=external_execute` 时包含 `src/`
- `TestAuthorized(path)`：路径等于精确项，或路径是授权根的真实后代时返回真

路径规范：

- 统一使用 `/`，去除合法的前导 `./`。
- 拒绝绝对路径、驱动器前缀、空段、`.`、`..` 和 `.git`。
- 目录根按路径段边界比较；`src/A.cs` 允许，`src2/A.cs` 与 `x/src/A.cs` 拒绝。
- 大小写比较保持与现有 Windows 路径守卫一致，但报告路径继续使用 Git 的规范仓库相对形式。

任务卡 `expectedPaths` 不加入一个虚构的文件项 `src/`。目录授权属于 owner policy，避免 `check-task-cards.ps1` 把目录误当成任务预计修改文件，也避免所有现有任务卡产生无意义变更。

## 六、执行流程

### 6.1 选题与 claim

`select-hourly-task.ps1` 继续返回任务卡原始 `expectedPaths` 和任务卡摘要；runtime 的 `taskCardDigest` 仍只绑定任务事实，不把隐式 owner policy 写回任务卡。policy 代码变化通过最新 master 和 canary 管理，不伪装成任务事实变化。

### 6.2 DeepSeek candidate

`invoke-deepseek-responsibility.ps1` 读取共享策略，向 prompt 分别说明：

- `ExpectedTaskPaths`：任务卡预计业务路径。
- `AuthorizedRoots: src/`：DeepSeek 的额外目录权限。
- 非 `src/` 修改仍必须命中 `ExpectedTaskPaths`。
- 任何任务卡未预计但实际修改的 `src/` 文件必须出现在精确 `changedPaths`、验证证据和残留风险中，不能静默扩大业务语义。

candidate 与 decision checkpoint 的实际 diff 均通过 `TestAuthorized` 核验，不再使用数组精确包含。

工作区清洁仍是硬闸门。DeepSeek 在返回前必须对所有 `src/` 副作用作出一种明确处理：

1. 文件属于本任务需要的 Unity 源/asset/scene/`.meta`，则纳入 candidate 并验证；或
2. 文件只是本 run 的无关生成副作用，则只在 run worktree 中清理并确认未被引用。

未跟踪文件不能因为位于 `src/` 就被忽略；目录授权解决“能否提交”，不取消“必须干净”。
删除已有 `src/` 文件时同样必须先满足引用与运行时可达性证明，不能用“整个目录已授权”替代清理证据。

### 6.3 candidate finalizer

`automation-finalize-commit.ps1` 增加目录授权输入能力，但提交行为仍落到精确文件：

1. 根据 Git staged、unstaged、untracked、deleted 状态枚举授权根下的实际变化文件。
2. 合并精确授权项中实际变化的文件，排序去重。
3. 对每个实际存在的文本文件运行 `check-pending-whitespace.ps1`。
4. 只 stage 和 commit 这些实际文件；不把整个目录的未变化文件当成提交参数。
5. 提交后再次证明外部 index 未变化，并输出 candidate SHA。

空目录、空变化、仓库外路径和 `.git` 继续拒绝。最终 terminal `changedPaths` 必须逐项等于 base 到 candidate SHA 的真实 diff。

### 6.4 共享入口复核与 checkpoint

`invoke-hourly-owner.ps1` 的以下位置统一调用共享策略：

- candidate evidence 实际路径核验；
- decision checkpoint 实际路径核验；
- checkpoint 在新 run 中重放前的路径核验。

不得出现 DeepSeek wrapper 允许、共享入口却因精确列表拒绝的双重语义。

### 6.5 最新 master 重放、冲突与正式提交

授权根不能直接作为 formal conflict 范围。共享入口在 candidate 通过后使用两类精确路径：

- `candidateActualPaths`：candidateResult 与 Git diff 一致的实际业务文件；
- `formalManagementPaths`：任务状态转换、backlog/queue、归档、`AI合作沟通.txt` 等共享入口必需管理文件。

`formalPaths = candidateActualPaths + formalManagementPaths`，并用它执行：

- base 到最新 master 的重验证冲突检查；
- 主工作区 staged/unstaged/untracked 冲突检查；
- latest master 上的 cherry-pick `--no-commit`；
- finalizer 精确提交；
- 组合验证与 fast-forward 前复核。

这样，主工作区无关的 `src/OtherFeature/X.cs` 不会阻塞只修改 `src/Feature/Y.cs` 的 candidate；如果双方实际修改同一文件或父子路径，仍立即停止，不自动覆盖、stash 或解冲突。

## 七、失败语义与当前 run 边界

新增或保持稳定 detailCode：

- 非 `src/` 且未精确授权：`deepseek_candidate_path_violation` / `deepseek_checkpoint_paths_invalid`。
- 路径非法或逃逸仓库：沿用对应 invalid/path violation。
- candidate 返回时存在任何未处理文件：`deepseek_worktree_dirty`。
- 实际 candidate 路径与 terminal 不一致：`deepseek_candidate_path_mismatch`。
- 最新 master 或主工作区在实际 formal 路径冲突：沿用 `hourly_revalidation_required` / `hourly_main_path_conflict`。

当前 run `914ea350-4d43-4673-86e9-a994df566578` 在旧 policy 下已经进入 `attention_required`，且 runtime 未记录 `candidateCommit/candidateResult`，但 worktree 存在业务候选提交。按恢复规则，它不是可由新小时轮次自动重跑、覆盖或清理的普通现场。

因此本策略实施不得顺便改写该 runtime。策略上线并通过 canary 后，另开普通管理处置：独立核验候选 `c4f0e12`、`CharterSites.csv.meta` 的 GUID/引用与主工作区同路径人工内容，再决定保留候选、正式重做或保持现场。该处置必须单独满足恢复规则和集成锁，不在本设计中预设删除或集成结论。

## 八、预计修改边界

实现阶段预计只修改与路径策略直接相关的工具和规则：

- 新增 `tools/hourly-path-authorization.ps1`。
- 修改 `tools/invoke-deepseek-responsibility.ps1`。
- 修改 `tools/invoke-hourly-owner.ps1`。
- 修改 `tools/automation-finalize-commit.ps1`。
- 仅当 workspace baseline 的输入不能接收隐式根时，最小修改 `tools/automation-workspace-guard.ps1` 的策略接入，不改变其主工作区人工改动保护语义。
- 增加或更新上述工具的直接测试脚本。
- 同步 `开发管理/自动工作流规则.txt` 与 `开发管理/AI协作规则.txt` 中的授权事实。

不修改 Unity 业务代码、scene、asset、CSV、BattleSim、任务卡 schema 或现有任务业务边界。

## 九、验证设计

### 9.1 共享路径策略单测

- DeepSeek external：允许已有 `src/A.cs`、新建 `src/New.meta`、深层 `src/Assets/X/Y.asset`。
- 拒绝 `src2/A.cs`、`x/src/A.cs`、`../src/A.cs`、绝对路径和 `.git/*`。
- DeepSeek external：任务卡精确授权的非 `src/` 文件仍允许，未授权非 `src/` 文件拒绝。
- Codex 和其他 route 不获得隐式 `src/`。

### 9.2 candidate/finalizer 测试

- 未列入任务卡的新 `src/` 文件可由 candidate finalizer 精确 stage/commit。
- 目录授权下空白检查覆盖实际变化文本文件。
- 未跟踪文件遗留时 candidate 仍返回 `deepseek_worktree_dirty`。
- terminal 少报、多报或改报路径均失败。
- 非 `src/` 越界仍失败。

### 9.3 checkpoint 与正式集成测试

- decision checkpoint 可包含任务卡未列举但位于 `src/` 的文件。
- 新 run 能按同一 policy 重放 checkpoint。
- 主分支改动无关 `src` 文件时不触发重验证阻塞。
- 主分支或人工工作区改动 candidate 实际文件时稳定阻塞。
- formal commit 只包含 candidate 实际文件和共享入口管理文件，不包含授权根下其他文件。

### 9.4 项目检查与 canary

- 运行新增/更新的 PowerShell 直接测试。
- `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-review-text.ps1 -Paths AGENTS.md,CLAUDE.md,开发管理`。
- 对变更文件运行 `tools/check-pending-whitespace.ps1`，暂存后运行 `git diff --cached --check`。
- 分别运行 Codex 与 DeepSeek canary，证明身份、结构化终态、目录授权策略、主工作区隔离和成功清理。
- canary 与工具测试全部通过前，不处置历史 `attention_required` run，不用实时任务试错。

## 十、验收条件

1. 任意 DeepSeek `external_execute` 任务可提交任务卡未预列的新 `src/` 文件，且 candidate/checkpoint/formal 三层结论一致。
2. 非 `src/` 路径仍必须由任务卡精确授权。
3. 工作区脏文件不会被忽略；DeepSeek 必须提交或清理 run 自身副作用。
4. latest master 重验证和主工作区冲突只覆盖实际 formal 文件，不因授权整个 `src/` 产生全目录假冲突。
5. Codex 权限、任务卡事实、任务语义、集成锁和独立复审边界不变。
6. 直接测试、文本/空白检查及两个 owner canary 全部通过。

## 十一、回滚

该能力必须以单一、可回滚的策略提交进入 master。若 canary 或首个新任务暴露授权语义不一致，暂停两个写入 automation，保留 runtime/worktree 证据，回滚策略提交即可恢复任务卡精确路径模式；任务卡 schema 和既有 `expectedPaths` 未迁移，因此不需要数据回迁、第二 runtime 或兼容分支。
