# DeepSeek 自动任务默认 `src/` 授权设计

> **状态：已由 2026-08-06 项目负责人决定停止实施。** 当前只采用任务卡显式列出 Unity 资产及对应 `.meta`、并人工核验现有 run 的轻量修复；只有后续至少另一个独立任务再次出现同类不可预列副作用阻塞时，才重新评估本文的授权根方案。以下内容仅保留为历史设计记录，不再是实施合同。

## 一、状态与决定

- 日期：2026-08-06。
- 决定方：项目负责人。
- 已选方向：所有 `external_execute / deepseek` 自动任务默认获得 `src/` 内 Unity 受版本控制源树的候选新增与修改权限；非 `src/` 路径继续由任务卡逐项授权。
- 安全收敛：缓存、日志、本机状态和生成工程文件即使位于 `src/` 也不授权；任务卡未预计的删除不授权。
- 本文是经两轮审核收敛后的冻结合同，只修订设计，不实施代码、不改任务卡、不处置当前活动 runtime。

## 二、背景与根因

当前 DeepSeek candidate 的授权只来自任务卡 `expectedPaths` 的精确文件列表。`invoke-deepseek-responsibility.ps1`、`invoke-hourly-owner.ps1` 的 candidate、checkpoint 和 formal 核验又分别使用精确包含判断。Unity 批处理可能在 `src/Assets/` 下创建合法的新 `.meta`、测试、场景或同步资产；只要新文件没有预先列入任务卡，即使业务改动已形成合法 candidate，也会因路径或工作区清洁闸门停止。

`U-GZ-UI-TEXT-01` 的 run `914ea350-4d43-4673-86e9-a994df566578` 暴露了该问题：DeepSeek 已形成候选提交 `c4f0e12076b0774e3afb208ef00e7526f95e492e`，Unity 另生成未跟踪 `src/Assets/DataConfig/CharterSites.csv.meta`；wrapper 在 candidate 终态核验时返回 `deepseek_worktree_dirty`，runtime 留在 `attention_required`。后续小时轮次按现行恢复规则只能返回 `existing_run`。

授权与清洁是两个独立合同。已授权文件被纳入 candidate 后，worktree 可以是干净的；任何未提交、未清理或并发新产生的文件仍使清洁闸门失败。根因不是“授权后无法保持干净”，而是现有 finalizer 只能提交任务卡预列路径，导致合法的新 `src/` 文件没有自洽的候选收口路径。

## 三、审核意见处置

### 3.1 接受并纳入

1. 不给共享 `automation-finalize-commit.ps1` 增加调用方可传入的目录授权参数，避免 Codex、queue maintenance 或其他 owner 意外继承权限。
2. 明确区分 Unity 受版本控制源树与 `src/` 内缓存、日志、本机状态、生成工程文件；后者硬拒绝。
3. workspace guard、冲突检查、重放、验证和 formal finalizer 只接收 candidate 实际文件，不接收授权根。
4. 未预计删除采用更严格合同，并补齐删除路径枚举与证据字段。
5. canary 必须直接验证未预计的新 `src` 文件、遗留副作用、非 `src` 越界、缓存拒绝、Codex 权限不变和精确 formal 提交。

### 3.2 不采纳

不采用“只在 finalizer 对 `*.meta` 等生成物放白名单”的替代方案。该方案能缓解当前 `.meta` 个案，但 DeepSeek 仍不能主动新增任务卡未预知的 C#、测试、场景、asset 或 Package/ProjectSettings 文件，会继续把常规 Unity 实施升级成负责人决策，不满足已确认的默认授权目标。

也不接受“删除文件无法精确枚举”的事实判断。Git 的 NUL 分隔 name-status/status 输出配合 `--no-renames` 可以稳定列出删除路径；真正需要解决的是删除是否授权以及证据是否充分，而不是路径能否枚举。

`src/Packages/**` 与 `src/ProjectSettings/**` 保持在允许源树内是明确的产品授权决定，不是未经识别的前缀副作用：Unity 依赖声明、依赖锁和工程设置本身属于可受审的版本控制工程源，部分跨模块任务确需新增或修改它们。`src/UserSettings/**` 属于本机状态，当前仓库由 `.gitignore` 排除，并继续由 `forbidden_generated` 硬拒绝。若以后要把 Packages 或 ProjectSettings 收回任务卡精确授权，应作为新的权限政策变更单独决策，不能在实现时隐式收窄负责人已经确认的 `src` 授权。

## 四、目标与非目标

### 4.1 目标

1. DeepSeek `external_execute` candidate 可新增或修改 `src/Assets/**`、`src/Packages/**`、`src/ProjectSettings/**`，无需为每个新文件追加任务卡路径。
2. `docs/`、`tools/`、`开发管理/`、`simulations/` 和仓库其他目录继续按任务卡精确授权。
3. candidate、decision checkpoint、共享入口复核、最新 master 重放和 formal 集成使用同一分类语义。
4. 任务卡预计路径、额外 `src` 路径与 Git 实际 diff 三者可机械核对；额外路径不能静默隐藏。
5. 授权范围与冲突范围分离：策略可允许源树，但冲突、重放、验证和提交只针对 candidate 实际改动的精确文件。
6. 保留任务语义、停止条件、worktree 清洁、主工作区人工改动保护、集成锁和 Codex 独立复审。

### 4.2 非目标

- 不给 Codex、Claude、queue maintenance 或手动脚本增加隐式 `src/` 权限。
- 不删除任务卡 `expectedPaths`；它继续表达预计业务文件和所有非 `src/` 授权。
- 不把路径授权当成任务相关性证明。额外 `src` 文件虽可通过路径闸门，仍须披露理由并接受 Codex 复审。
- 不允许通过 `src2/`、`x/src/`、路径穿越、绝对路径、仓库外路径或 `.git/` 命中授权。
- 不允许提交 `src/Library/**`、`src/Logs/**`、`src/UserSettings/**`、`src/Temp/**`、`src/Obj/**`、根级 `*.csproj`、`*.sln`、`*.log` 或其他已确认的 Unity 本机生成物。
- 不允许凭隐式授权删除任务卡没有精确预计的已有文件。
- 不在本设计中自动恢复、关闭、清理或集成历史 run `914ea350-4d43-4673-86e9-a994df566578`。

## 五、方案比较

### 5.1 已选：DeepSeek 专属源树授权与精确候选收口

共享分类策略仅在 runtime 证明 `owner=deepseek` 且 `route=external_execute` 时启用源树授权。DeepSeek 专属 candidate wrapper 自行枚举 Git 实际变化、分类并把精确文件列表交给现有共享 finalizer。任务卡无需批量改写，非 `src/` 仍读取 `expectedPaths`。

优点：满足新增 C#、测试、场景、asset、`.meta`、Packages 和 ProjectSettings 的常规实施需要；共享 finalizer 不获得通用目录能力；缓存、删除和其他 owner 权限仍有明确硬闸门。

### 5.2 未选：任务卡 `allowSrcTree=true`

每张外部任务卡决定是否放开源树。隔离更细，但仍会把常规 Unity 实施变成重复授权，并要求迁移任务卡 schema、制卡逻辑和队列检查。

### 5.3 未选：finalizer-only 生成物白名单

只给 `.meta` 等已知生成物开 finalizer 白名单，模型仍按任务卡精确路径形成 candidate。改动较小，但只能修复已知副作用，不能满足默认允许新增业务源文件的决定。

## 六、统一路径分类合同

新增唯一共享分类所有者，建议为 `tools/hourly-path-authorization.ps1`，只负责纯函数式规范化和分类，供 DeepSeek 专属 wrapper 与共享入口复核调用。不得在多个脚本复制前缀判断。

### 6.1 输入

- runtime 中已经验证的 `owner`、`route`、`taskId`、`runId`、worktree 和 branch。
- 任务卡 `expectedPaths`。
- Git 枚举出的仓库相对路径及变化类型。

调用方不能自行传入 `AuthorizedRoots`、伪造 owner 或通过 prompt 字段改变策略。

### 6.2 路径规范

- 使用 Git 的 NUL 分隔输出解析路径，统一报告为 `/`。
- 去除合法前导 `./`；拒绝绝对路径、驱动器前缀、空段、`.`、`..` 和 `.git`。
- 根按路径段边界比较；`src/Assets/A.cs` 可命中，`src2/A.cs` 与 `x/src/A.cs` 不命中。
- 大小写比较保持与现有 Windows 路径守卫一致，报告继续使用 Git 的规范仓库相对形式。

### 6.3 分类与优先级

按以下顺序得到唯一分类：

1. `forbidden_generated`：命中缓存、本机状态、生成工程或日志硬拒绝项；不因任务卡误列而自动放行。
2. `expected_exact`：路径精确命中任务卡 `expectedPaths`，且不属于硬拒绝项。
3. `src_expanded`：仅限 DeepSeek external，路径位于 `src/Assets/**`、`src/Packages/**` 或 `src/ProjectSettings/**`，变化为新增或修改。
4. `delete_requires_expected`：删除或以 `--no-renames` 表示的重命名源路径位于源树，但未精确命中 `expectedPaths`。
5. `unauthorized`：其他所有路径。

通过条件只有 `expected_exact` 与 `src_expanded`。`forbidden_generated`、`delete_requires_expected` 和 `unauthorized` 均阻塞 candidate。重命名使用 `--no-renames` 展开为删除加新增，因此旧路径必须被任务卡精确预计，新路径可按源树新增授权。

任务卡不加入虚构文件项 `src/`。`check-task-cards.ps1` 继续校验任务卡结构、管理路径和 ready 一致性，不负责覆盖 Git diff；本方案不需要为它增加目录豁免。

## 七、执行流程

### 7.1 选题与 claim

`select-hourly-task.ps1` 继续返回任务卡原始 `expectedPaths` 和摘要；runtime 的 `taskCardDigest` 仍只绑定任务事实，不把 owner policy 写回任务卡。源树策略由最新 master 的受审工具版本和 canary 管理。

### 7.2 DeepSeek prompt 与候选证据

`invoke-deepseek-responsibility.ps1` 从已验证 runtime 读取共享分类策略，向 prompt 明确给出：

- `ExpectedTaskPaths`：任务预计业务文件。
- `ExpandedSourceSurfaces`：`src/Assets/**`、`src/Packages/**`、`src/ProjectSettings/**`。
- `ForbiddenGeneratedSurfaces`：缓存、日志、本机状态和生成工程硬拒绝项。
- 非 `src` 修改仍必须命中 `ExpectedTaskPaths`。
- 未预计删除不允许；需要删除时先停在任务决策或补全任务事实，不得自行扩大。
- 所有 `src_expanded` 文件必须披露，不能用目录授权隐藏任务语义扩张。

terminal candidate schema 在现有 `changedPaths` 基础上增加：

- `expandedSrcPaths`：Git 实际 diff 中所有 `src_expanded` 路径的排序去重数组；没有时为空数组。
- `scopeExpansionReason`：存在 `expandedSrcPaths` 时必须非空，说明这些文件为何是完成本任务所需；没有时必须为空。
- `deletedPaths`：实际删除路径数组，必须全部属于 `expected_exact`。
- `deletionEvidence`：对象数组，每个删除路径恰有一项 `{ path, checks[], conclusion }`；`checks` 记录代码引用、Unity YAML/GUID、Resources/addressable 路径和运行时可达性核对。机械层验证对象集合与删除路径一一对应且文本非空，证据真实性由 Codex 复审。

以上四个字段在 `completed` 与 `needs_decision` 结构中都必须存在；没有扩展或删除时使用空数组和空字符串，不允许省略。模型的 terminal JSON 只在提交完成后返回，因此提交前 finalizer 不依赖这些字段，也不创建第二份临时结果状态。

额外 `src` 路径不触发负责人决策；它们是候选证据和复审输入。只有触及任务语义、禁止项或既有停止条件时才进入 decision checkpoint。

### 7.3 DeepSeek 专属 candidate finalizer

新增 `tools/finalize-deepseek-candidate.ps1`，作为 DeepSeek candidate 与 decision checkpoint 的唯一提交入口，并用 `-Mode Candidate|Checkpoint` 区分固定提交消息。它不得接受 owner、route 或目录授权参数，而是：

1. 从私有 runtime 读取活动 run，并验证 `owner=deepseek`、`route=external_execute`、task/run ID、worktree、branch、base commit 和 task digest。
2. 用 Git NUL 分隔 status/name-status 枚举 staged、unstaged、untracked、deleted 和重命名展开后的全部实际路径。
3. 用共享策略逐项分类；任一硬拒绝、未预计删除或非 `src` 越界立即失败。
4. 计算精确 `actualCandidatePaths`、`expandedSrcPaths` 和 `deletedPaths`。
5. 对实际存在的文本文件运行 `check-pending-whitespace.ps1`。
6. 只把 `actualCandidatePaths` 作为精确文件列表传给现有 `automation-finalize-commit.ps1`；共享 finalizer 保持精确路径接口，不增加目录授权能力。
7. 提交后从 `base..candidate` 重新枚举并复核三组路径、外部 index 不变和 candidate SHA；向 stdout 返回单个结构化对象 `{ candidateCommit, changedPaths, expandedSrcPaths, deletedPaths }`，供 DeepSeek 原样形成 terminal 证据。
8. 最后运行 `git status --porcelain=v2 --untracked-files=all`；任何残留或 finalizer 后并发生成的新文件仍返回 `deepseek_worktree_dirty`。

DeepSeek 在调用专属 finalizer 前，应删除本 run 产生且确认无关的副作用，或把属于任务的允许源树文件纳入证据。路径授权解决“能否形成候选”，不取消“候选必须自洽且 worktree 必须干净”。

### 7.4 共享入口复核与 checkpoint

`invoke-hourly-owner.ps1` 对 candidate、decision checkpoint 和 checkpoint 重放统一执行：

- 从 runtime 而非调用参数确定是否启用 DeepSeek external 策略。
- 从 Git patch/diff 独立计算 `actualCandidatePaths`、`expandedSrcPaths`、`deletedPaths`。
- 核对 terminal/checkpoint 自报数组与实际集合完全一致。
- 对 `scopeExpansionReason`、`deletionEvidence` 执行存在性和对应关系检查。
- 对每个实际路径重新分类，拒绝 shared wrapper 与 owner 入口之间的授权漂移。

`invoke-deepseek-responsibility.ps1` 在 CLI 返回 terminal 后执行相同的独立复核，再把核验后的路径与证据写入 runtime candidateResult。专属 finalizer 的 stdout 只帮助模型准确报告，不作为 owner 入口的事实源；最终事实始终是 `base..candidate` Git diff。

`deepseek_candidate_path_violation` 没有失效：它继续阻塞非 `src` 越界、缓存/生成物和未预计删除；对 `src_expanded` 通过则是本策略有意改变后的成功语义。`deepseek_candidate_path_mismatch` 和 `deepseek_worktree_dirty` 保持原含义。

### 7.5 workspace guard、最新 master 与 formal 集成

禁止把 `src/` 或任何源树根传给 `automation-workspace-guard.ps1`、共享 finalizer、冲突检查或 Git stage。实现阶段必须保证：

- claim 前的主工作区保护继续使用任务卡精确预计路径和管理路径，不因隐式授权把整个 `src` 变成冲突范围。
- candidate 通过后，`candidateActualPaths` 只来自已经核验的 `base..candidate` Git diff。
- `formalPaths = candidateActualPaths + formalManagementPaths`，排序去重后作为 workspace CAS、最新 master 冲突、重放、组合验证和 formal finalizer 的唯一业务路径输入。
- 如果主工作区在任务卡未预计但 candidate 实际修改的同一路径已有 staged、unstaged 或 untracked 内容，formal 阶段按精确路径稳定阻塞，不覆盖、不 stash、不自动解冲突。
- latest master 的 cherry-pick `--no-commit` 后再次证明实际 diff 等于 `formalPaths` 中候选部分，正式提交不包含授权源树中的其他文件。

这项接入是实现合同，不是“如果需要才改”的可选项。若现有 guard 调用点不能分别接受 claim 前预计路径与 candidate 后实际路径，就必须最小修改调用点或 guard 接口，但不得放宽其人工改动保护语义。

## 八、失败语义与当前 run 边界

保持或补充稳定 detailCode：

- 非 `src` 越界、缓存/生成物或非法路径：`deepseek_candidate_path_violation` / `deepseek_checkpoint_paths_invalid`。
- 未预计删除：新增 `deepseek_candidate_delete_not_expected` / `deepseek_checkpoint_delete_not_expected`。
- 额外路径数组、删除数组或 terminal `changedPaths` 与 Git 不一致：`deepseek_candidate_path_mismatch`。
- 候选提交后存在任何残留：`deepseek_worktree_dirty`。
- 最新 master 或主工作区在实际 formal 路径冲突：沿用 `hourly_revalidation_required` / `hourly_main_path_conflict`。

当前 run `914ea350-4d43-4673-86e9-a994df566578` 在旧 policy 下已经进入 `attention_required`，且 runtime 未记录 `candidateCommit/candidateResult`，但 worktree 存在业务候选提交。新策略不得顺便改写、清理或集成该 runtime。

策略上线并通过 canary 后，另开管理处置：独立核验候选 `c4f0e12`、`CharterSites.csv.meta` 的 GUID/引用与主工作区同路径人工内容，再决定保留候选、正式重做或保持现场。该处置必须单独满足恢复规则和集成锁。

## 九、预计修改边界

实现阶段预计只修改与路径策略直接相关的工具、测试和规则：

- 新增 `tools/hourly-path-authorization.ps1`。
- 新增 `tools/finalize-deepseek-candidate.ps1`。
- 修改 `tools/invoke-deepseek-responsibility.ps1`，只允许专属 finalizer 收口候选。
- 修改 `tools/invoke-hourly-owner.ps1`，统一复核分类并把实际路径传入 formal 流程。
- 按第 7.5 节合同修改 `tools/automation-workspace-guard.ps1` 的调用点或最小接口；不得接收授权根。
- 不给 `tools/automation-finalize-commit.ps1` 增加目录授权参数；若专属 wrapper 需要更稳定的精确路径输出，只做不改变其他 owner 语义的最小接口增强。
- 增加或更新上述工具的直接测试脚本。
- 同步 `开发管理/自动工作流规则.txt` 与 `开发管理/AI协作规则.txt` 的授权事实和失败语义。

`select-hourly-task.ps1`、`check-task-cards.ps1`、任务卡 schema、Unity 业务代码、scene、asset、CSV 和 BattleSim 不在预计修改范围。若实现证据迫使突破该边界，停止实施并重新评审根因，不连续叠加补丁。

## 十、验证设计

### 10.1 共享分类单测

- DeepSeek external 新增/修改 `src/Assets/A.cs`、`src/Assets/New.meta`、`src/Packages/X/package.json`、`src/ProjectSettings/TagManager.asset` 均分类为 `src_expanded`。
- `src/Library/**`、`Logs/**`、`UserSettings/**`、`Temp/**`、`Obj/**`、根级 `*.csproj`、`*.sln`、`*.log` 均为 `forbidden_generated`。
- 未预计删除为 `delete_requires_expected`；精确预计删除为 `expected_exact`。
- 拒绝 `src2/A.cs`、`x/src/A.cs`、`../src/A.cs`、绝对路径和 `.git/*`。
- DeepSeek external 的任务卡精确非 `src` 文件允许，未授权非 `src` 文件拒绝。
- Codex、其他 owner 和其他 route 不获得隐式源树权限。

### 10.2 candidate/finalizer 测试

- 未列入任务卡的新 C# 与 `.meta` 可由专属 finalizer 精确 stage/commit，并出现在 `expandedSrcPaths`。
- 未列入任务卡的已有源树文件修改可提交；未列入任务卡的删除稳定拒绝。
- 精确预计删除只有在 `deletedPaths` 和 `deletionEvidence` 对应时才能形成候选。
- forbidden、非 `src` 越界、terminal 少报/多报/改报、扩展数组不一致均稳定失败。
- finalizer 只提交 Git 实际允许文件，不提交整个目录；文本空白检查覆盖实际存在文件。
- finalizer 后再产生未跟踪文件时仍返回 `deepseek_worktree_dirty`。
- Codex 和 queue maintenance 继续只能给共享 finalizer 传精确已授权路径。

### 10.3 checkpoint、workspace 与 formal 测试

- decision checkpoint 可携带 `src_expanded` 新文件、扩展理由和 patch，新 run 按同一策略重放。
- checkpoint 中 forbidden、未预计删除或数组与 patch 不一致时拒绝。
- 主分支改动无关 `src` 文件不触发冲突；改动 candidate 实际文件稳定阻塞。
- 主工作区同路径 staged、unstaged、untracked 内容均稳定阻塞。
- formal commit 只包含 candidate 实际文件和共享入口管理文件，不包含授权源树其他文件。
- claim 前 guard 仍使用预计精确路径，candidate 后 guard 使用实际精确路径，任何阶段都不接收源树根。

### 10.4 项目检查与核心 canary

- 运行新增或更新的 PowerShell 直接测试。
- 运行 `check-review-text.ps1`、相关 `check-data-chain.ps1`、预提交空白检查和 `git diff --cached --check`。
- 在一次性隔离 fixture 中运行 DeepSeek canary，任务卡不预列 canary 新 C#/`.meta`，证明其能形成候选并准确报告 `expandedSrcPaths`。
- canary 在模型启动前预置一个由 fixture 明确标记为“与任务无关”的允许源树哨兵文件；DeepSeek 必须清理它，哨兵不得出现在 candidate diff、`changedPaths` 或 `expandedSrcPaths` 中，结束后 worktree 为空。该用例验证模型遵守任务语义；生产机械层仍不凭文件名猜测相关性。
- canary 分别尝试 `src/Library` 生成物和未授权非 `src` 文件，证明两者被拒绝。
- 运行 Codex canary，证明其不能修改任务卡未预列的 `src` 文件。
- 证明 formal 重放只作用于 canary 实际文件，未把其他 `src` 内容带入提交。
- 工具测试与两个 owner canary 全部通过前，不处置历史 `attention_required` run，不用实时业务任务试错。

## 十一、验收条件

1. 任意 DeepSeek `external_execute` 任务可新增或修改任务卡未预列的 Unity 受版本控制源树文件，candidate、checkpoint、formal 三层分类一致。
2. 非 `src` 路径仍必须由任务卡精确授权；`src` 内缓存、日志、本机状态和生成工程文件仍拒绝。
3. 未预计删除被机械阻塞；预计删除具有精确路径和独立复审证据。
4. `changedPaths`、`expandedSrcPaths`、`deletedPaths` 与 Git 实际 diff 完全一致，额外业务路径不能静默隐藏。
5. worktree 脏文件不会被忽略；DeepSeek 必须提交允许且相关的副作用或清理本 run 无关副作用。
6. workspace guard、latest master 重验证、冲突和提交只覆盖实际 formal 文件，不接收授权根。
7. Codex 权限、任务卡事实、任务语义、集成锁和独立复审边界不变。
8. 直接测试、文本/数据链/空白检查及两个 owner canary 全部通过。

## 十二、回滚

该能力必须以单一、可回滚的策略提交进入 master。若 canary 暴露授权、清洁或 formal 精确路径语义不一致，暂停两个写入 automation，保留 runtime/worktree 证据，回滚策略提交即可恢复任务卡精确路径模式。任务卡 schema 和既有 `expectedPaths` 未迁移，因此不需要数据回迁、第二 runtime 或兼容分支。
