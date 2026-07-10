# Trust Gate Task System Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将已批准的三道 P0 可信度闸门、内容冻结规则和 TQ-049～TQ-071 任务正式写入项目管理入口，使纯 `1` 和自动工作流只能领取依赖已满足的闸门任务。

**Architecture:** 本计划只重排开发管理文档，不执行 Unity、BattleSim 或数据修复。`当前任务队列.txt` 保持 5–10 条短任务卡，分线 backlog 保存完整路线，`开发优先级.txt` 与内容 backlog 共同提供不可绕过的冻结口径；旧队列全文移入归档。

**Tech Stack:** Markdown/TXT 管理文档、PowerShell 检查脚本、ripgrep、Git。

---

## Scope and file map

本计划是一个单一的“任务治理”实施单元。规格中的 G1、G2、G3 是后续独立软件任务，本计划只建立它们的任务卡、依赖和入口。

**Create:**

- `开发管理/任务归档/2026-07-10-可信度闸门重排前队列归档.txt`：保存重排前 `当前任务队列.txt` 全文。

**Replace:**

- `开发管理/当前任务队列.txt`：只保留冻结规则、六条首批任务和完整近期任务卡。

**Modify:**

- `开发管理/开发优先级.txt`：把当前阶段改为三道可信度闸门，废止旧“闸门已解除”的执行结论。
- `开发管理/任务列表/场景与Unity任务.txt`：登记 G1、P1 架构与状态基础任务。
- `开发管理/任务列表/数值与战斗任务.txt`：登记 G2，提升 TQ-044。
- `开发管理/任务列表/数据链路任务.txt`：登记 G3，把 TQ-043 记为待复审前置，并合并 TQ-040。
- `开发管理/任务列表/内容设计任务.txt`：冻结新增内容并登记 TQ-071 解冻判定。

**Do not modify:**

- `src/`
- `simulations/BattleSim/`
- `data/`、CSV、Unity asset
- `docs/` 下除本计划与已提交规格之外的游戏设计/剧情正文
- `开发管理/审核入口.txt`、`开发管理/未通过审核清单.txt`、`开发管理/AI合作沟通.txt`

## 2026-07-10 执行修订：TQ-043 外部交接

- 本计划写完后，Claude Code / WF3 已提交 TQ-043 / D-IMPORT-01，并登记为 `HANDOFF-20260710-02`；`3c8c1cd` 是 TQ-043 已落地且进入待复审状态的执行基线提交，不代表后续执行时的当前 HEAD。
- TQ-043 当前状态为 `⚠️ 已修改/待复审`，必须由 Codex / ChatGPT5.5 独立复审；不得再从纯 `1` 流程领取，也不得写成复审通过。
- TQ-043 仍是 G3 的前置和复审对象；TQ-056 必须保持阻塞，直至 TQ-043 复审通过。

### Task 1: Archive the old queue and activate the six-task P0 queue

**Files:**

- Move: `开发管理/当前任务队列.txt` → `开发管理/任务归档/2026-07-10-可信度闸门重排前队列归档.txt`
- Create: `开发管理/当前任务队列.txt`
- Reference: `docs/superpowers/specs/2026-07-10-trust-gates-and-vertical-slice-task-system-design.md`

- [ ] **Step 1: Verify the starting worktree and queue IDs**

Run:

```powershell
git status --short
git merge-base --is-ancestor 3c8c1cd HEAD
rg -n "TQ-0(39|40|43|44|45|48)" 开发管理/当前任务队列.txt
```

Expected: worktree is clean; `git merge-base --is-ancestor 3c8c1cd HEAD` succeeds; the current queue marks TQ-043 as completed by Claude Code and pending Codex / ChatGPT5.5 review under `HANDOFF-20260710-02`, while TQ-048 appears only in history.

- [ ] **Step 2: Move the complete old queue into the dated archive**

Use `apply_patch` so the entire source file is preserved:

```text
*** Begin Patch
*** Update File: 开发管理/当前任务队列.txt
*** Move to: 开发管理/任务归档/2026-07-10-可信度闸门重排前队列归档.txt
@@
-# 当前任务队列（✅ 已审核）
+# 当前任务队列（可信度闸门重排前归档）
*** End Patch
```

Expected: the archive contains all previous history and task cards; `开发管理/当前任务队列.txt` no longer exists until Step 3.

- [ ] **Step 3: Create the new short queue with exact dependency states**

Use `apply_patch` to create `开发管理/当前任务队列.txt` with exactly this content:

```text
# 当前任务队列（✅ 已审核）

> 2026-07-10 Codex / gpt-5.5：用户批准启用“三道 P0 可信度闸门 + 最小纵向切片”任务体系。重排前全文归档至 `开发管理/任务归档/2026-07-10-可信度闸门重排前队列归档.txt`；设计规格见 `docs/superpowers/specs/2026-07-10-trust-gates-and-vertical-slice-task-system-design.md`。
> 内容冻结：G1 正式构建链、G2 BattleSim 数值可信度、G3 数据链路语义可信度全部通过且 TQ-071 完成前，禁止把新的剧情、角色、功法、术法、神通或世界设定扩写补入本队列。
> 本文件只放近期可执行、可验证、可提交的任务切片；复审和交接任务走 `开发管理/审核入口.txt`。

## 使用规则

1. 用户发送纯 `1` 时，先确认身份，再选择主责匹配、状态为“待处理”、依赖已满足的最高优先级任务。
2. 状态为“阻塞”的任务不得执行；前置完成后由队列维护工作流改为“待处理”。
3. 内容冻结期间，队列维护工作流只能从 G1/G2/G3 未完成任务中补位。
4. 本文件保持 5–10 条任务；完成项归档，长期事实写入 `开发管理/设计-当前状态.txt`。
5. 用户发送纯 `2` 时读取 `开发管理/审核入口.txt`，不从本文件领取执行任务。

## 队列表头

| ID | 优先级 | 主责 | 类型 | 状态 | 任务 |
|----|--------|------|------|------|------|
| TQ-049 | P0 | Codex / gpt-5.5 | G1 验证 | 待处理 | Q-UNITY-01：修复 Unity EditMode 基线失败 |
| TQ-050 | P0 | Codex / gpt-5.5 | G1 验证 | 阻塞（TQ-049） | Q-UNITY-02：建立权威 Unity 测试入口 |
| TQ-051 | P0 | Codex / gpt-5.5 | G1 验证 | 阻塞（TQ-050） | Q-UNITY-03：测试路径恢复场景与 Build Settings |
| TQ-052 | P0 | Codex / gpt-5.5 | G2 数值 | 待处理 | N-TRUST-01：修正 CT 反应方向并建立回归 |
| TQ-053 | P0 | Codex / gpt-5.5 | G2 数值 | 阻塞（TQ-052） | N-TRUST-02：统一暴击倍率语义 |
| TQ-056 | P0 | Codex / gpt-5.5 | G3 数据 | 阻塞（TQ-043 复审） | D-TRUST-01：数据检查器错误分级 |

## 不进入 `1` 队列的当前事项

- HANDOFF-20260710-01 / TQ-038：维持现有 `2` 复审入口。
- HANDOFF-20260710-02 / TQ-043：Claude Code / WF3 已提交，维持 `⚠️ 已修改/待复审`，不得从纯 `1` 领取。
- TQ-039：内容冻结。
- TQ-040：不单独执行；仅在 TQ-057 已登记且前置满足后由其吸收。
- TQ-045：G1 通过后再作为 P2 机制回归。
- TQ-054～TQ-071：仅在对应分线 backlog 已登记且依赖满足时补位；当前未登记项不得补位。

## 任务卡片

### TQ-049 · Q-UNITY-01 修复 Unity EditMode 基线失败

- 来源：可信度闸门规格 §4。
- 当前状态：待处理；依赖：无。
- 主责：Codex / gpt-5.5。
- 必读：`开发管理/开发-技术经验.txt`、可信度闸门规格、`src/Assets/Tests/EditMode/SceneArchitectureEditorTests.cs`、`src/Assets/Scripts/Game/SceneFlowManager.cs`、`src/Assets/Scripts/Game/GameSession.cs`。
- 范围：定位并修复 `SceneFlowManagerPreparesAdventureAndReturnContextsWithoutSceneLoad` 空引用；禁止删除断言、忽略测试或减少测试数量。
- 验证：`dotnet build src/Assembly-CSharp.csproj`；`dotnet build src/TianZhang.EditModeTests.csproj`；Unity EditMode 结果必须为 47/47 通过；`git diff --check`。
- 完成条件：根因已记录，47 个测试全部执行且全部通过，工作区无非预期场景或 Build Settings 改动。

### TQ-050 · Q-UNITY-02 建立权威 Unity 测试入口

- 来源：可信度闸门规格 §4。
- 当前状态：阻塞（TQ-049）；依赖：TQ-049。
- 主责：Codex / gpt-5.5。
- 必读：`开发管理/开发-技术经验.txt`、可信度闸门规格、现有 Unity 测试日志与 `src/Assets/Tests/EditMode/`。
- 范围：新增 `tools/run-unity-editmode-tests.ps1`；等待测试完成并解析结果 XML，禁止以进程退出码代替测试结果。
- 验证：成功 XML 返回 0；失败、缺失、不可解析或零测试 XML 返回非 0；`powershell -ExecutionPolicy Bypass -File tools/run-unity-editmode-tests.ps1`。
- 完成条件：统一入口在成功与四类失败样例上均返回正确退出码。

### TQ-051 · Q-UNITY-03 测试路径恢复场景与 Build Settings

- 来源：可信度闸门规格 §4。
- 当前状态：阻塞（TQ-050）；依赖：TQ-050。
- 主责：Codex / gpt-5.5。
- 必读：`src/Assets/Tests/EditMode/SceneArchitectureEditorTests.cs`、`src/Assets/Scripts/Editor/SceneBuilder.cs`、`tools/run-unity-editmode-tests.ps1` 及 TQ-050 引入的相关测试脚本。
- 范围：覆盖权威 Unity 测试入口的成功与异常路径，确保两类路径都恢复执行前的场景和 Build Settings；必须从干净隔离工作区开始，不得依赖人工清理。
- 验证：分别执行成功路径和异常路径；每条路径测试前保存、测试后比较 `src/Assets/Scenes/*.unity` 与 `src/ProjectSettings/EditorBuildSettings.asset` 中存在且受 Git 跟踪文件的 SHA256 快照，可用 PowerShell `git ls-files 'src/Assets/Scenes/*.unity' 'src/ProjectSettings/EditorBuildSettings.asset' | Where-Object { Test-Path -LiteralPath $_ } | ForEach-Object { Get-FileHash -Algorithm SHA256 -LiteralPath $_ }` 生成；每条路径前后的 `git status --short --untracked-files=all` 必须都为空；最后运行 `git diff --check`。
- 完成条件：成功与异常路径各自执行前后的场景与 Build Settings 内容哈希一致，且无 tracked 或 untracked 残留。

### TQ-052 · N-TRUST-01 修正 CT 反应方向并建立回归

- 来源：可信度闸门规格 §5。
- 当前状态：待处理；依赖：无。
- 主责：Codex / gpt-5.5。
- 必读：`docs/基础设定/角色数值设计.txt`、`simulations/BattleSim/Combat.cs`、`simulations/BattleSim/BattleSimSelfTests.cs`、`simulations/BattleSim/Program.cs`。
- 范围：修正 2v2 CT 中 `100.0 / 反应` 导致低反应更快的问题，增加确定性首次行动顺序测试。
- 验证：`dotnet build -c Release --no-restore simulations/BattleSim`；`dotnet run --no-build -c Release --project simulations/BattleSim`；`git diff --check`。
- 完成条件：其余条件相同时，高反应单位不晚于低反应单位取得首次行动；同值顺序稳定可复现。

### TQ-053 · N-TRUST-02 统一暴击倍率语义

- 来源：可信度闸门规格 §5。
- 当前状态：阻塞（TQ-052）；依赖：TQ-052。
- 主责：Codex / gpt-5.5。
- 必读：`docs/基础设定/角色数值设计.txt`、`src/Assets/Scripts/Combat/DamageCalculator.cs`、`src/Assets/Scripts/Entity/Character.cs`、`simulations/BattleSim/Combat.cs`、两侧相关测试。
- 范围：统一基础暴击倍率和 `critDamage` 加成字段语义，确保相同输入在 Unity 与 BattleSim 得到相同倍率。
- 验证：Unity 与 BattleSim 分别覆盖零加成和一个非零加成断言；两侧编译通过；BattleSim 默认运行通过；`git diff --check`。
- 完成条件：文档、Unity、BattleSim 使用同一字段含义，两个固定样例结果一致。

### TQ-056 · D-TRUST-01 数据检查器错误分级

- 来源：可信度闸门规格 §6。
- 当前状态：阻塞（TQ-043 复审）；依赖：TQ-043 经 Codex / ChatGPT5.5 复审通过。
- 主责：Codex / gpt-5.5。
- 必读：`tools/check-data-chain.ps1`、`开发管理/docs-csv-asset-alignment.txt`、`开发管理/realm_lianshen专项检查.txt`、三类 CSV 与 Unity 数据对象定义。
- 范围：把数量矛盾、必填字段缺失、删除内容激活、玩家内容边界失守和 schema 不匹配定义为错误并返回非 0；保留明确批准的警告。
- 验证：构造或使用已知错误输入时返回非 0；仅剩批准警告时 `powershell -ExecutionPolicy Bypass -File tools/check-data-chain.ps1` 返回 0；`git diff --check`。
- 完成条件：检查脚本能真实区分成功、警告和错误，不再输出风险后仍无条件 `OK`。
```

- [ ] **Step 4: Verify queue size, dependency states, and frozen IDs**

Run:

```powershell
rg -n "^\| TQ-" 开发管理/当前任务队列.txt
rg -n "TQ-039：内容冻结|TQ-040：不单独执行；仅在 TQ-057 已登记且前置满足后由其吸收|TQ-045：G1 通过后|TQ-054～TQ-071：仅在对应分线 backlog 已登记且依赖满足时补位；当前未登记项不得补位" 开发管理/当前任务队列.txt
git diff --check
```

Expected: exactly six table rows; only TQ-049、TQ-052 are `待处理`; TQ-050、TQ-051、TQ-053、TQ-056 are blocked by explicit dependencies, and TQ-056 specifically waits for TQ-043 review; the TQ-040 line requires TQ-057 to be registered with prerequisites satisfied before absorption; the TQ-054～TQ-071 line forbids unregistered IDs from backfilling the active queue.

- [ ] **Step 5: Commit the queue activation**

```powershell
git add -- 开发管理/当前任务队列.txt 开发管理/任务归档/2026-07-10-可信度闸门重排前队列归档.txt
git commit -m "docs: activate trust gate task queue"
```

Expected: one commit containing only the new queue and archive.

### Task 2: Replace outdated priority conclusions and freeze content expansion

**Files:**

- Modify: `开发管理/开发优先级.txt`
- Modify: `开发管理/任务列表/内容设计任务.txt`
- Reference: `docs/superpowers/specs/2026-07-10-trust-gates-and-vertical-slice-task-system-design.md`

- [ ] **Step 1: Update the priority document title and current execution decision**

Use `apply_patch` to replace the title exactly:

```text
-# 天章 — 开发优先级与推进路线（v3.3 总结校准）
+# 天章 — 开发优先级与推进路线（v3.4 可信度闸门重排）
```

Then insert this section before `## 设计资产盘点`:

```text
## 2026-07-10 当前执行决策

- 冻结新的剧情、角色、功法、术法、神通和世界设定扩写；既有数据纠错、运行时字段补齐和复审不属于新增内容。
- 当前只有三道 P0 闸门：G1 正式构建链、G2 BattleSim 数值可信度、G3 数据链路语义可信度。
- 旧“场景架构闸门已解除”“BattleSim 矩阵可信”“docs/CSV/asset 数量闭合即可继续扩写”的结论不再作为执行依据。
- 当前队列只允许领取依赖已满足的 G1/G2/G3 任务；TQ-071 独立复核三门证据后，只能解冻一个运行时薄内容切片。
- 任务编号、依赖和验收标准以 `docs/superpowers/specs/2026-07-10-trust-gates-and-vertical-slice-task-system-design.md` 为准。
```

Replace the paragraph beginning `**结论：项目已经过了` and the following numbered priority list with:

```text
**结论：项目方向可保留，但尚不能宣称正式最小可玩循环、BattleSim 平衡结论或数据语义链可信。当前阶段从“规模化填充前校准”收缩为“三道可信度闸门验证”；旧探索场景中的可玩原型不能替代正式 Build Settings 闭环。**

**当前优先级执行顺序（2026-07-10 重排）**：

1. **G1 正式构建链**：先修 Unity 测试基线和权威测试入口，再锁定 Adventure/Exploration 唯一运行时所有者，完成正式构建端到端闭环。
2. **G2 BattleSim 数值可信度**：修正 2v2 CT 反应方向、暴击口径、非法 Build 输入和成长表回退，再重跑 21 Build 矩阵。
3. **G3 数据链路语义可信度**：完成表头解析、错误分级、现存数据矛盾清理、`contentScope` 失败关闭和运行时限制接入。
4. **P1 架构治理**：建立项目结构图，拆分大型职责中心，补最小状态与存档基础。
5. **内容解冻**：TQ-071 复核三门通过后，只允许一地区、一出身、一据点、一场战斗的薄内容切片。
```

Expected: the top-level priority no longer says the relevant gates are already cleared.

- [ ] **Step 2: Add a content freeze banner and gate-exit task**

In `开发管理/任务列表/内容设计任务.txt`, insert after `## 当前状态`:

```text
- 2026-07-10 用户批准内容冻结：G1/G2/G3 与 TQ-071 完成前，不新增剧情、角色、功法、术法、神通或世界设定；既有错误修复和复审继续按原流程处理。
```

Replace the affected active rows with this exact block; leave completed rows unchanged:

```text
| C-TAIYI-01 | P2 | Codex 规划；DeepSeek 整理 | 内容冻结（TQ-071 前） | 太一道庭法箓 CSV/BattleSim/Unity 字段方案 |
| C-GUXIU-03 | P2 | DeepSeek | 内容冻结（TQ-071 前） | 古修第一批神通，每个完成品单独文件 |
| C-SANXIU-01 | P2 | DeepSeek | 内容冻结（TQ-071 前） | 散修通用内容可玩性补强清单 |
| C-NPC-01 | P2 | DeepSeek 执行；Codex 复审 | 内容冻结（TQ-071 前） | 重要 NPC 源/化/界字段后的叙事扩写 |
| C-STORY-WM-P0 | P0 | Codex | 冻结（已完成 01/02/03/04/05A/06） | 世界主线 P0 前置包：寿元时间尺度、出生地接入、真形体系、矩阵占位、玄荒新路天才人物志规划骨架、低阶势力开放规则和第一章兼容复核 |
| GATE-EXIT-01 / TQ-071 | P0 | Codex / gpt-5.5 | 阻塞（G1/G2/G3） | 独立复核三道闸门证据；只批准一个“一地区、一出身、一据点、一场战斗”运行时薄内容切片 |
| C-STORY-WM-L1 | P1 | Codex | 内容冻结（TQ-071 前） | 世界主线第二至第七章 L1 章节骨架拆分，按 `docs/剧情/主线/世界主线-后续拆分工作清单.txt` 推进 |
| C-STORY-WM-L2 | P2 | Codex 规划；DeepSeek 执行；Codex 复审 | 内容冻结（TQ-071 前） | 世界主线 L2/L3/L4 拆分包：地区主线、势力线、NPC 分支和终局演出 |
| C-STORY-04 | P2 | Codex 规划；DeepSeek 执行；Codex 复审 | 内容冻结（TQ-071 前） | 低阶势力生态与样板成长线拆分包：中小门派/分院/家族/散修营地模板 + 1 条样板地区锚点成长线 |
| C-QUEST-01 | P2 | DeepSeek 执行；Codex 复审 | 内容冻结（TQ-071 前） | 宗门入门支线任务首批，一任务一文件 |
| C-DIALOGUE-01 | P3 | DeepSeek 执行；Codex 复审 | 内容冻结（TQ-071 前） | 据点通用称谓、寒暄和任务入口文案表 |
| C-ECON-01 | P3 | Codex 规划 | 内容冻结（TQ-071 前） | 灵石、丹药、天材地宝、掉落表经济基线 |
| C-HIGH-01 | P3 | Codex | 内容冻结（TQ-071 前） | 高阶内容按 NPC/资料片/历史说明分类收口 |
```

Replace the sentence `后续可继续拆世界主线 L1 或将 C-STORY-WM-05 扩写交给 DeepSeek` with:

```text
后续世界主线 L1、人物志扩写和 DeepSeek 批量内容全部冻结；只有 TQ-071 通过后指定的唯一薄内容切片可以恢复。
```

Append under `## 边界`:

```text
- 内容冻结期间，自动工作流不得把本文件中的待处理/冻结内容补入 `当前任务队列.txt`。
- TQ-071 不是普通 `1` 任务；任一道闸门证据不完整时必须维持冻结。
```

- [ ] **Step 3: Verify that no pending expansion remains claimable**

Run:

```powershell
rg -n "\| (C-TAIYI-01|C-GUXIU-03|C-SANXIU-01|C-NPC-01|C-STORY-WM-L1|C-STORY-WM-L2|C-STORY-04|C-QUEST-01|C-DIALOGUE-01|C-ECON-01|C-HIGH-01) .*\| 待处理 \|" 开发管理/任务列表/内容设计任务.txt
rg -n "TQ-071|内容冻结|G1/G2/G3" 开发管理/开发优先级.txt 开发管理/任务列表/内容设计任务.txt
git diff --check
```

Expected: first search has no matches; second search finds the new priority and freeze rules.

- [ ] **Step 4: Commit priority and freeze rules**

```powershell
git add -- 开发管理/开发优先级.txt 开发管理/任务列表/内容设计任务.txt
git commit -m "docs: freeze content behind trust gates"
```

Expected: one commit containing only global priority and content backlog changes.

### Task 3: Register G1 and P1 Unity/system tasks

**Files:**

- Modify: `开发管理/任务列表/场景与Unity任务.txt`

- [ ] **Step 1: Record the current G1 facts without changing historical completed rows**

Append these bullets under `## 当前状态`:

```text
- ⚠️ 2026-07-10 审视：正式 Build Settings 中的 `AdventureScene` 仍是入口/返回壳，旧 `ExplorationScene` 才承载主要可玩探索战斗循环；G1 未通过前不得再写“正式最小可玩循环已完成”。
- ⚠️ Unity EditMode 当前基线为 47 个测试中 1 个失败；仅看带 `-quit` 的进程退出码可能产生假绿，TQ-049～TQ-051 先修复验证可信度。
```

- [ ] **Step 2: Insert G1 and P1 rows into the task table**

Insert these rows before U-MECH-01, and change U-MECH-02B state to `阻塞（G1 通过后）`:

```text
| Q-UNITY-01 / TQ-049 | P0 | Codex | 待处理 | 修复 `SceneFlowManagerPreparesAdventureAndReturnContextsWithoutSceneLoad`，保持 47 个 EditMode 测试全部执行 |
| Q-UNITY-02 / TQ-050 | P0 | Codex | 阻塞（TQ-049） | 建立权威 Unity EditMode 入口；缺失/失败/不可解析/零测试 XML 必须非零退出 |
| Q-UNITY-03 / TQ-051 | P0 | Codex | 阻塞（TQ-050） | 测试成功与异常路径都恢复场景和 Build Settings，前后工作区一致 |
| U-SLICE-01 / TQ-060 | P0 | Codex | 阻塞（TQ-051） | 证明 Adventure/Exploration/SceneBuilder 调用链并锁定唯一冒险运行时所有者 |
| U-SLICE-02 / TQ-061 | P0 | Codex | 阻塞（TQ-060） | 正式冒险入口接通最小地图、单敌人、战斗、结算和返回据点 |
| U-SLICE-03 / TQ-062 | P0 | Codex | 阻塞（TQ-060） | 角色出身驱动起始世界节点，移除江左硬编码并定义非法值回退 |
| U-SLICE-04 / TQ-063 | P0 | Codex | 阻塞（TQ-061、TQ-062） | 正式 Build Settings 端到端冒烟；通过后 G1 才可标记通过 |
| M-ARCH-01 / TQ-064 | P1 | Codex | 阻塞（G1） | 建立 `UNITY_STRUCTURE.md` 与 runtime/content/assemblies 聚焦图 |
| U-ARCH-01 / TQ-065 | P1 | Codex | 阻塞（TQ-064） | 拆分 `ExplorationController` / `SceneBuilder` 至少一个真实职责和依赖边 |
| U-ARCH-02 / TQ-066 | P1 | Codex | 阻塞（TQ-064） | 拆分 `BattleUIManager` / `Character` 的 UI 与领域职责 |
| U-ARCH-03 / TQ-067 | P1 | Codex | 阻塞（TQ-065、TQ-066） | 建立项目派生 asmdef 边界并保持序列化兼容 |
| S-STATE-01 / TQ-068 | P1 | Codex | 阻塞（G1） | 最小 GameSession、世界时间和起点状态模型 |
| S-STATE-02 / TQ-069 | P1 | Codex | 阻塞（TQ-068） | 最小任务、背包和 NPC 状态快照，分离状态步骤 |
| S-SAVE-01 / TQ-070 | P1 | Codex | 阻塞（TQ-068、TQ-069） | 存档版本、新旧档、非法档和重置迁移基础 |
```

- [ ] **Step 3: Add explicit G1 exit criteria**

Insert before `## 默认验证`:

```text
## G1 出口条件

- TQ-049、TQ-050、TQ-051、TQ-060、TQ-061、TQ-062、TQ-063 全部完成。
- 权威 Unity 测试入口报告全部测试通过，且执行前后工作区一致。
- 正式 Build Settings 能连续完成“创建角色 → 世界节点 → 据点 → 冒险 → 战斗 → 结算 → 返回据点”。
- 旧 `ExplorationScene` 不再作为隐藏的唯一可玩入口；竞争运行时所有者有明确迁移或淘汰结论。
```

- [ ] **Step 4: Verify all G1/P1 IDs and dependency states**

Run:

```powershell
rg -n "TQ-0(49|50|51|60|61|62|63|64|65|66|67|68|69|70)" 开发管理/任务列表/场景与Unity任务.txt
rg -n "U-MECH-02B.*阻塞（G1 通过后）" 开发管理/任务列表/场景与Unity任务.txt
git diff --check
```

Expected: every listed ID occurs exactly once in the active task table; U-MECH-02B is blocked by G1.

- [ ] **Step 5: Commit the G1/P1 backlog**

```powershell
git add -- 开发管理/任务列表/场景与Unity任务.txt
git commit -m "docs: register formal build gate tasks"
```

### Task 4: Register G2 BattleSim trust tasks

**Files:**

- Modify: `开发管理/任务列表/数值与战斗任务.txt`

- [ ] **Step 1: Record the invalidated assumptions**

Append under `## 当前状态`:

```text
- ⚠️ 2026-07-10 审视：2v2 CT 使用 `100.0 / 反应`，导致低反应更快；该缺陷修复前，2v2 结果不能作为平衡依据。
- ⚠️ 当前 BuildDefs 存在角色创建规则下不可生成的初始属性，Unity/BattleSim 暴击伤害字段也与设计文档基础倍率口径不一致；G2 未通过前不得用矩阵结果确认平衡。
```

- [ ] **Step 2: Replace N-BAL-02 and add the G2 chain**

Replace the N-BAL-02 row and add the other rows immediately after it:

```text
| N-TRUST-01 / TQ-052 | P0 | Codex | 待处理 | 修正 2v2 CT 反应快慢方向并增加确定性首次行动回归 |
| N-TRUST-02 / TQ-053 | P0 | Codex | 阻塞（TQ-052） | 统一文档、Unity、BattleSim 基础暴击倍率与 `critDamage` 语义 |
| N-TRUST-03 / TQ-054 | P0 | Codex | 阻塞（TQ-053） | 让 21 Build 输入符合角色创建点数预算、上限和非线性成本 |
| N-BAL-02A / TQ-044 | P0 | Codex | 阻塞（TQ-054） | 修复成长表完整性与星级回退；缺失输入不得静默掩盖 |
| N-TRUST-04 / TQ-055 | P0 | Codex | 阻塞（TQ-052、TQ-053、TQ-054、TQ-044） | 重跑 21 Build，审计样本覆盖与 0%/100% 极端结果；通过后 G2 才可标记通过 |
```

Remove the old standalone `N-BAL-02` row so the same work has only one active definition.

- [ ] **Step 3: Add G2 exit criteria**

Insert before `## 默认验证`:

```text
## G2 出口条件

- TQ-052、TQ-053、TQ-054、TQ-044、TQ-055 全部完成。
- 21 个 Build 全部满足角色创建规则，非法输入在进入矩阵前失败。
- Unity 与 BattleSim 的暴击固定样例一致；高反应单位的首次行动不晚于低反应单位。
- 所有 0%/100% 极端结果被归类为设计预期、样本不足或缺陷；未解释风险存在时 G2 保持阻塞。
```

- [ ] **Step 4: Verify the G2 chain and single TQ-044 definition**

Run:

```powershell
rg -n "TQ-0(44|52|53|54|55)" 开发管理/任务列表/数值与战斗任务.txt
rg -n "\| N-BAL-02 \|" 开发管理/任务列表/数值与战斗任务.txt
git diff --check
```

Expected: first search finds the five G2 rows; second search has no match.

- [ ] **Step 5: Commit the G2 backlog**

```powershell
git add -- 开发管理/任务列表/数值与战斗任务.txt
git commit -m "docs: register BattleSim trust gate tasks"
```

### Task 5: Register G3 data semantics tasks and absorb TQ-040

**Files:**

- Modify: `开发管理/任务列表/数据链路任务.txt`

- [ ] **Step 1: Replace the stale data-chain status claim**

Append under `## 当前状态`:

```text
- ⚠️ 2026-07-10 审视：当前检查输出存在术法 docs 86 与 CSV/asset 75 的数量矛盾、realm_lianshen 语言键缺失、realm_lianxu 已删除内容仍激活等语义风险；脚本仍返回 `OK`，因此 Missing 0 不能等同于数据链可信。
- ⚠️ `contentScope` 缺失时当前默认 `player`，运行时未证明 reserved 过滤；`realmReq`、`elementReq`、`affiliation` 也未形成完整运行时限制链。G3 未通过前禁止按数据规模继续扩写。
```

- [ ] **Step 2: Replace D-ASSET/D-IMPORT rows and add the G3 chain**

Replace the existing D-ASSET-01 and D-IMPORT-01 rows with:

```text
| D-ASSET-01 / TQ-040 | P2 | DeepSeek | 已合并至 TQ-057 | CSV/asset 字段完整性复查由 D-TRUST-02 统一执行，不再单独领取 |
| D-IMPORT-01 / TQ-043 | P0 | Codex | ⚠️ 已修改/待复审（HANDOFF-20260710-02） | 按表头解析全部核心字段；缺必需列/重复列失败，未知列按显式兼容策略处理 |
| D-TRUST-01 / TQ-056 | P0 | Codex | 阻塞（TQ-043 复审） | 检查脚本区分错误和警告；语义错误必须非零退出 |
| D-TRUST-02 / TQ-057 | P0 | DeepSeek 执行；Codex 复审 | 阻塞（TQ-056） | 对齐术法数量、补齐或隔离缺失语言键、处理已删除仍激活内容，并吸收 TQ-040 |
| D-TRUST-03 / TQ-058 | P0 | Codex | 阻塞（TQ-043、TQ-056） | `contentScope` 缺失/非法时失败关闭，并证明 CSV→asset→运行时过滤链 |
| D-TRUST-04 / TQ-059 | P0 | Codex | 阻塞（TQ-043、TQ-056、TQ-058） | 接通 `realmReq`、`elementReq`、`affiliation` 资产字段与运行时允许/拒绝测试 |
```

- [ ] **Step 3: Replace the contradictory high-realm boundary and add G3 exit criteria**

Replace:

```text
- 不补 `realm_lianshen` Language key，不扩展 C# realm order，除非 Codex / ChatGPT5.5 重新给出架构决策。
```

with:

```text
- 不把 `realm_lianshen` 扩展为当前玩家境界；缺失 Language key 必须补齐、隔离或停用对应行，不能保持激活且只告警。
```

Insert before `## 默认验证`:

```text
## G3 出口条件

- TQ-043、TQ-056、TQ-057、TQ-058、TQ-059 全部完成并通过规定复审。
- 数量矛盾、必填字段缺失、删除内容激活、玩家内容边界失守和 schema 不匹配都会让检查脚本非零退出。
- reserved 内容不会进入玩家池；缺失/非法 `contentScope` 不能默认成为 player。
- `realmReq`、`elementReq`、`affiliation` 各有一个运行时允许样例和拒绝样例。
```

- [ ] **Step 4: Verify G3 IDs, merge state, and updated boundary**

Run:

```powershell
rg -n "TQ-0(40|43|56|57|58|59)" 开发管理/任务列表/数据链路任务.txt
rg -n "已合并至 TQ-057|不把 `realm_lianshen` 扩展为当前玩家境界|G3 出口条件" 开发管理/任务列表/数据链路任务.txt
git diff --check
```

Expected: all G3 rows exist; TQ-043 remains pending review under `HANDOFF-20260710-02`; TQ-056 waits for TQ-043 review; TQ-040 is non-claimable; the old “do not add Language key” boundary is absent.

- [ ] **Step 5: Commit the G3 backlog**

```powershell
git add -- 开发管理/任务列表/数据链路任务.txt
git commit -m "docs: register data semantics gate tasks"
```

### Task 6: Cross-file consistency and workflow guard verification

**Files:**

- Verify: `开发管理/当前任务队列.txt`
- Verify: `开发管理/开发优先级.txt`
- Verify: `开发管理/任务列表/场景与Unity任务.txt`
- Verify: `开发管理/任务列表/数值与战斗任务.txt`
- Verify: `开发管理/任务列表/数据链路任务.txt`
- Verify: `开发管理/任务列表/内容设计任务.txt`
- Verify: `开发管理/任务归档/2026-07-10-可信度闸门重排前队列归档.txt`

- [ ] **Step 1: Run mechanical text and whitespace checks**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File tools/check-review-text.ps1 -Paths AGENTS.md,CLAUDE.md,开发管理
git diff --check HEAD~5..HEAD
```

Expected: review-text script passes; no whitespace errors in the five implementation commits.

- [ ] **Step 2: Verify all task IDs and no claimable frozen content**

Run:

```powershell
rg -n "TQ-0(39|40|43|44|45|49|50|51|52|53|54|55|56|57|58|59|60|61|62|63|64|65|66|67|68|69|70|71)" 开发管理/当前任务队列.txt 开发管理/任务列表 开发管理/开发优先级.txt
rg -n "\| (C-TAIYI-01|C-GUXIU-03|C-SANXIU-01|C-NPC-01|C-STORY-WM-L1|C-STORY-WM-L2|C-STORY-04|C-QUEST-01|C-DIALOGUE-01|C-ECON-01|C-HIGH-01) .*\| 待处理 \|" 开发管理/任务列表/内容设计任务.txt
```

Expected: first search finds the designed task dispositions; second search has no matches.

- [ ] **Step 3: Verify the current queue is short and dependency-safe**

Run:

```powershell
(Select-String -Path 开发管理/当前任务队列.txt -Pattern '^\| TQ-' | Measure-Object).Count
Select-String -Path 开发管理/当前任务队列.txt -Pattern '^\| TQ-.*\| 待处理 \|'
```

Expected: count is `6`; claimable rows are only TQ-049、TQ-052.

- [ ] **Step 4: Confirm no product files changed**

Run:

```powershell
git diff --name-only 3c8c1cd..HEAD
git status --short
```

Expected: changes after the task-system implementation baseline `3c8c1cd` are limited to the plan, the six management files, and the one queue archive; worktree is clean. No path begins with `src/`, `simulations/`, `data/`, or a game-design content directory.

- [ ] **Step 5: Record the activation result**

No additional commit is needed when Steps 1–4 pass. Report:

```text
G1/G2/G3 task system: activated
Current queue rows: 6
Claimable tasks: TQ-049, TQ-052
Content expansion: frozen until TQ-071
Product files changed: no
```
