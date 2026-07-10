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
3. 内容冻结期间 P0 闸门任务始终优先；当没有主责匹配且依赖已满足的 P0 闸门任务时，才可补已登记、依赖满足的 P1 架构/状态任务或 TQ-045 等明确后置非内容任务；禁止补冻结内容或提前执行 TQ-071。
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
- 必读：`.gitignore`、`docs/基础设定/角色数值设计.txt`、`simulations/BattleSim/BattleSim.csproj`、`simulations/BattleSim/Combat.cs`、`simulations/BattleSim/BattleSimSelfTests.cs`、`simulations/BattleSim/Program.cs`。
- 范围：首个切片和首个提交必须先把 `.NET 10`、零外部依赖且不含任何 `PackageReference` 的 `simulations/BattleSim/BattleSim.csproj` 纳入版本控制，并在 `.gitignore` 增加仅针对该文件的窄例外；首个提交只恢复工具链，不得同时修改 CT。在该提交的干净 worktree 连续通过 restore → build → run 后，才可修正 2v2 CT 并增加确定性首次行动顺序测试。
- 验证：`git ls-files --error-unmatch simulations/BattleSim/BattleSim.csproj` 成功，`git check-ignore -q simulations/BattleSim/BattleSim.csproj` 不得返回 0，项目中无 `PackageReference`；干净 checkout 首次运行 `dotnet restore simulations/BattleSim/BattleSim.csproj --ignore-failed-sources`，预期成功并生成被忽略的 `simulations/BattleSim/obj/project.assets.json`；随后运行 `dotnet build -c Release --no-restore simulations/BattleSim` 与 `dotnet run --no-build -c Release --project simulations/BattleSim`；再验证 CT 增长对反应严格单调和同值顺序；`git diff --check`。
- 完成条件：无 `PackageReference` 的工具链提交已在干净 worktree 连续通过 restore → build → run；restore 失败或项目含 `PackageReference` 时不得进入 CT 修复。其后 CT 增长对反应严格单调，其余条件相同时，反应 20 必须早于反应 10 到达首次行动阈值；同值顺序稳定可复现。

### TQ-053 · N-TRUST-02 统一暴击倍率语义

- 来源：可信度闸门规格 §5。
- 当前状态：阻塞（TQ-052）；依赖：TQ-052。
- 主责：Codex / gpt-5.5。
- 必读：`docs/基础设定/战斗系统.txt`、`docs/基础设定/角色数值设计.txt`、`src/Assets/Scripts/Combat/DamageCalculator.cs`、`src/Assets/Scripts/Entity/Character.cs`、`simulations/BattleSim/Combat.cs`、两侧相关测试。
- 范围：以 `docs/基础设定/战斗系统.txt` 为基础暴击倍率事实源，基础暴击为 1.5 倍；`docs/基础设定/角色数值设计.txt` 管理二级属性和加成来源。`critDamage` 表示在 1.5 倍基础上的附加百分比点，不是总倍率；元素附加另按百分比点加入。
- 验证：Unity 与 BattleSim 分别固定断言零加成 = 1.50、`critDamage = 15` = 1.65；两侧编译通过；BattleSim 默认运行通过；`git diff --check`。
- 完成条件：文档、Unity、BattleSim 使用同一字段含义；两侧相同输入的 1.50/1.65 固定样例一致，禁止同一字段表达不同含义。

### TQ-056 · D-TRUST-01 数据检查器错误分级

- 来源：可信度闸门规格 §6。
- 当前状态：阻塞（TQ-043 复审）；依赖：TQ-043 经 Codex / ChatGPT5.5 复审通过。
- 主责：Codex / gpt-5.5。
- 必读：`tools/check-data-chain.ps1`、`开发管理/docs-csv-asset-alignment.txt`、`开发管理/realm_lianshen专项检查.txt`、三类 CSV 与 Unity 数据对象定义。
- 范围：把无批准豁免的 docs/CSV/asset 数量矛盾、必填字段缺失、删除内容激活、玩家内容边界失守和 schema 不匹配定义为错误并返回非 0；每个批准警告豁免必须在版本控制中精确绑定 `ruleId + contentId/文件/行键`，记录理由、负责人、移除或到期条件；禁止通配符、类别级、前缀匹配或自动覆盖未来记录。
- 验证：产出成功与失败测试样例，负例不得修改生产数据；新增同类但未精确列入的警告必须返回非 0 并作为回归负例；错误或缺失结果必须返回非 0，仅剩精确批准警告时 `powershell -ExecutionPolicy Bypass -File tools/check-data-chain.ps1` 返回 0；`git diff --check`。
- 完成条件：检查脚本能真实区分成功、警告和错误；正例返回 0、负例返回非 0，且仅精确绑定并记录完整的批准警告可返回 0。
```

- [ ] **Step 4: Verify queue size, dependency states, and frozen IDs**

Run:

```powershell
rg -n "^\| TQ-" 开发管理/当前任务队列.txt
rg -n "TQ-039：内容冻结|TQ-040：不单独执行；仅在 TQ-057 已登记且前置满足后由其吸收|TQ-045：G1 通过后|TQ-054～TQ-071：仅在对应分线 backlog 已登记且依赖满足时补位；当前未登记项不得补位" 开发管理/当前任务队列.txt
rg -n "内容冻结期间 P0 闸门任务始终优先|没有主责匹配且依赖已满足的 P0 闸门任务|禁止补冻结内容或提前执行 TQ-071" 开发管理/当前任务队列.txt
(Select-String -Path 开发管理/当前任务队列.txt -Pattern '^\| TQ-' | Measure-Object).Count
Select-String -Path 开发管理/当前任务队列.txt -Pattern '^\| TQ-.*\| 待处理 \|'
git diff --check
```

Expected: exactly six table rows; only TQ-049、TQ-052 are `待处理`; TQ-050、TQ-051、TQ-053、TQ-056 are blocked by explicit dependencies, and TQ-056 specifically waits for TQ-043 review; the TQ-040 line requires TQ-057 to be registered with prerequisites satisfied before absorption; the TQ-054～TQ-071 line forbids unregistered IDs from backfilling the active queue. The queue rule keeps every eligible P0 gate above registered, dependency-ready P1 architecture/state or TQ-045 backfills, while frozen content and premature TQ-071 remain forbidden.

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
- Modify: `docs/superpowers/plans/2026-07-10-trust-gate-task-system-implementation.md`（同步本 Task 2 的复核修订指令）
- Reference: `docs/superpowers/specs/2026-07-10-trust-gates-and-vertical-slice-task-system-design.md`

- [ ] **Step 1: Update the priority document title, current decision, and historicalize stale conclusions**

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

Immediately after the current decision and before the old asset inventory, insert this exact warning:

```text
> ⚠️ 历史快照总警示：以下资产盘点/三阶段路线/设计缺口/BattleSim → Unity 状态均为 2026-07-06 及更早历史快照；其中“当前阶段/已完成/闸门已解除”仅描述当时结论，凡与 2026-07-10 当前执行决策冲突者失效，不得用于任务选择、闸门判断或内容解冻。
```

Historicalize the stale section and stage headings exactly:

```text
## 设计资产盘点（2026-07-06 历史快照）
## 三阶段推进路线（历史路线）
### 阶段二：旧探索原型循环（历史完成；正式构建闭环待 G1）
### 阶段三：规模化填充与正式开发前校准（历史阶段；现行已收缩为 G1/G2/G3）
#### 3.2.7 场景架构与 2.5D 战棋表现（历史 Editor 壳验证已完成；G1 正式构建链未通过）
**历史进入条件（已被 2026-07-10 当前执行决策取代）**
## 2026-07-06 设计缺口快照（历史）
## BattleSim → Unity 同步状态（历史快照）
## 与上一版（v3.2）主要变化（历史记录）
```

Replace the two false-green rows in the historical design-gap table with:

```text
| ⚠️ | BattleSim 矩阵可信度待 G2 重验 | **G2 / TQ-052～TQ-055、TQ-044** | 旧校准因 2v2 CT 反应方向、非法 Build 输入、暴击口径问题失效，按 TQ-052～TQ-055 / TQ-044 重验 |
| ⚠️ | 正式构建链待 G1 重验 | **G1 / TQ-049～TQ-051、TQ-060～TQ-063** | 旧 Editor 壳验证不能证明正式 Adventure 可玩，按 TQ-049～TQ-051 / TQ-060～TQ-063 重验 |
```

Replace the later bold `**策略**` paragraph with:

```text
**策略**：冻结新增内容，先完成 G1/G2/G3；旧 TQ-016/TQ-010 只能作为历史证据，不能解除新闸门。
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

Expected: the current decision is authoritative; every old route/status section is visibly historical, the two false-green rows require G1/G2 revalidation, and the current strategy cannot use TQ-016/TQ-010 to clear the new gates.

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
| C-STORY-WM-P0 | P0 | Codex | 冻结（01/02/03/04/05A/06 已完成；05完整扩写未完成） | 世界主线 P0 前置包：寿元时间尺度、出生地接入、真形体系、矩阵占位、玄荒新路天才人物志规划骨架、低阶势力开放规则和第一章兼容复核 |
| GATE-EXIT-01 / TQ-071 | P0 | Codex / gpt-5.5 | 阻塞（G1/G2/G3） | 独立复核三道闸门证据；只批准一个“一地区、一出身、一据点、一场战斗”运行时薄内容切片 |
| C-STORY-WM-L1 | P1 | Codex | 内容冻结（TQ-071 前） | 世界主线第二至第七章 L1 章节骨架拆分，按 `docs/剧情/主线/世界主线-后续拆分工作清单.txt` 推进 |
| C-STORY-WM-L2 | P2 | Codex 规划；DeepSeek 执行；Codex 复审 | 内容冻结（TQ-071 前） | 世界主线 L2/L3/L4 拆分包：地区主线、势力线、NPC 分支和终局演出 |
| C-STORY-04 | P2 | Codex 规划；DeepSeek 执行；Codex 复审 | 内容冻结（TQ-071 前） | 低阶势力生态与样板成长线拆分包：中小门派/分院/家族/散修营地模板 + 1 条样板地区锚点成长线 |
| C-QUEST-01 | P2 | DeepSeek 执行；Codex 复审 | 内容冻结（TQ-071 前） | 宗门入门支线任务首批，一任务一文件 |
| C-DIALOGUE-01 | P3 | DeepSeek 执行；Codex 复审 | 内容冻结（TQ-071 前） | 据点通用称谓、寒暄和任务入口文案表 |
| C-ECON-01 | P3 | Codex 规划 | 内容冻结（TQ-071 前） | 灵石、丹药、天材地宝、掉落表经济基线 |
| C-HIGH-01 | P3 | Codex | 内容冻结（TQ-071 前） | 高阶内容按 NPC/资料片/历史说明分类收口 |
```

In `## 当前状态`, replace the WF2-CODEX-ONE follow-up clause so the historical recommendation cannot be executed during the freeze:

```text
“后续可拆 DeepSeek 扩写与 Codex 复审”是冻结前历史建议，当前不得执行，需 TQ-071 指定后才可恢复。
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
rg -n "历史快照总警示|待 G1 重验|待 G2 重验|05完整扩写未完成" 开发管理/开发优先级.txt 开发管理/任务列表/内容设计任务.txt
rg -n "^## 设计资产盘点$|^## 三阶段推进路线$|^### 阶段二：最小可玩循环 ✅ 已完成|^### 阶段三：规模化填充与正式开发前校准 ← 当前阶段|^#### 3\.2\.7 .*闸门已解除|^\*\*进入条件\*\*|^## 当前设计缺口|^## BattleSim → Unity 同步状态$|^\*\*策略\*\*：现在不宜" 开发管理/开发优先级.txt
git diff --check
```

Expected: the first and fourth searches have no matches; the second and third searches find the trust-gate, freeze, historical-warning, G1/G2 revalidation, and unfinished C-STORY-WM-05 wording.

- [ ] **Step 4: Commit priority and freeze rules**

```powershell
git add -- 开发管理/开发优先级.txt 开发管理/任务列表/内容设计任务.txt docs/superpowers/plans/2026-07-10-trust-gate-task-system-implementation.md
git commit -m "docs: retire stale pre-gate conclusions"
```

Expected: one commit containing only the global priority, content backlog, and synchronized Task 2 plan changes.

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

- [ ] **Step 3: Add explicit task boundaries and tightened G1 exit criteria**

Insert before `## 默认验证`:

```text
## G1/P1 任务执行边界

- **TQ-060 运行时所有者证明**：产物必须列出正式 Build Settings → `AdventureScene` / `ExplorationScene` / `SceneBuilder` / 战斗 / 返回的真实调用与所有者链，为每个竞争入口写明迁移、适配或淘汰结论，并锁定后续切片允许修改与禁止修改的文件清单；仅推测或仅写迁移计划不通过。
- **TQ-061 最小冒险战斗闭环**：必须复用 TQ-060 已证明的网格与战斗所有者；不得新增超过 500 行的类，也不得向现有超过 500 行的 Hub 类新增无关职责；胜利与失败两条结算路径都必须返回合法的据点/世界上下文。
- **TQ-062 出身起点**：当前两个可选出身必须进入各自起点；非法值或旧值必须有显式记录并走专用回退路径，不得写回、展示或伪装为合法出身值。
- **TQ-063 正式构建冒烟**：必须提供可重复的自动或半自动步骤、运行日志和结果；从创建角色到返回据点，玩家档案、出身、起点、Adventure 上下文和返回上下文全程不丢失。
- **TQ-064 结构事实图**：`UNITY_STRUCTURE.md` 必须来自对当前工作树的实时扫描，至少包含文件路径、场景、运行时所有者、asmdef 边界、验证入口和 open gaps；不得用旧文档推断代替扫描证据。
- **TQ-065 探索/场景职责拆分**：至少移除一条真实依赖边，或提取一个有独立测试的协作者；不得新增超过 500 行的类，不得继续增长现有 Hub 的职责。
- **TQ-066 UI/领域边界**：UI 展示状态与战斗/角色领域状态的所有者和写入路径必须明确；改造不得破坏 Unity 序列化字段、资产 GUID 或已有场景/预制体引用兼容。
- **TQ-067 asmdef 边界**：不得存在 sibling feature 直接依赖，不得有实现层向上反向引用；项目派生 asmdef 的编译和 Unity 序列化兼容验证必须通过。
- **TQ-068 最小会话状态**：`GameSession`、世界时间和起点状态在新游戏初始化、场景切换、战斗返回后必须一致，且有对应回归证据。
- **TQ-069 状态步骤与快照**：`shown != clicked`，`shown` / `clicked` / `opened` / `selected` / `applied` / `completed` / `persisted` 不得压成一个 bool；最小任务、背包和 NPC 快照必须各有明确持久化所有者。
- **TQ-070 存档迁移**：新档、旧档、非法档和重置路径都必须有确定行为，并提供存档版本判定与迁移前后的可复核证据。

- **补位调度说明**：TQ-064～TQ-070 与 TQ-045 在各自依赖满足后，仅作为低于任何未完成 P0 闸门任务的候选；G1 通过只解除相应依赖，不等于它们立即进入或抢占 `当前任务队列.txt`。

## G1 出口条件

- TQ-049、TQ-050、TQ-051、TQ-060、TQ-061、TQ-062、TQ-063 全部完成。
- 权威 Unity 测试入口报告全部测试通过，且执行前后工作区一致。
- TQ-051 必须从干净隔离工作区开始；Git 跟踪的 `src/Assets/Scenes/*.unity` 与 `src/ProjectSettings/EditorBuildSettings.asset` 在成功和异常路径前后 SHA256 相同，且 `git status --short --untracked-files=all` 证明 tracked/untracked 零残留。
- 正式 Build Settings 能连续完成“创建角色 → 世界节点 → 据点 → 冒险 → 战斗 → 结算 → 返回据点”。
- TQ-063 必须提供可重复的自动或半自动步骤、运行日志和结果；玩家档案、出身、起点、Adventure 上下文和返回上下文在正式构建链路全程不丢失。
- 正式 Build Settings 只有一个可达冒险运行时；旧 `ExplorationScene` 必须已迁入该所有者、从正式链路不可达或删除，不得与 `AdventureScene` 保持两个竞争可达入口；只写迁移计划不算 G1 通过。
```

- [ ] **Step 4: Verify all G1/P1 IDs and dependency states**

Run:

```powershell
rg -n "TQ-0(49|50|51|60|61|62|63|64|65|66|67|68|69|70)" 开发管理/任务列表/场景与Unity任务.txt
rg -n "U-MECH-02B.*阻塞（G1 通过后）" 开发管理/任务列表/场景与Unity任务.txt
rg -n "G1/P1 任务执行边界|不得新增超过 500 行|shown != clicked|只有一个可达冒险运行时" 开发管理/任务列表/场景与Unity任务.txt
rg -n "干净隔离工作区|SHA256|tracked/untracked 零残留|可重复的自动或半自动步骤|运行日志和结果|玩家档案、出身、起点" 开发管理/任务列表/场景与Unity任务.txt
git diff --check
```

Expected: every listed ID occurs exactly once in the active task table; U-MECH-02B is blocked by G1; TQ-060～TQ-070 each has an explicit minimum deliverable and acceptance boundary; P1/TQ-045 backfills remain below unfinished P0 gates; TQ-051 proves SHA256 and zero-residue cleanup from a clean isolated worktree; TQ-063 preserves all named state with repeatable evidence; and formal Build Settings has only one reachable adventure runtime.

- [ ] **Step 5: Commit the G1/P1 backlog**

```powershell
git add -- 开发管理/任务列表/场景与Unity任务.txt
git commit -m "docs: register formal build gate tasks"
```

### Task 4: Register G2 BattleSim trust tasks

**Files:**

- Modify: `开发管理/任务列表/数值与战斗任务.txt`
- Modify: `开发管理/当前任务队列.txt`（同步 TQ-052 / TQ-053 卡片）
- Modify: `docs/superpowers/plans/2026-07-10-trust-gate-task-system-implementation.md`（同步 Task 1 完整队列模板与本 Task 4）

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

- [ ] **Step 3: Add the G2 toolchain prerequisite, execution boundaries, and exit criteria**

Insert before `## 默认验证`:

```text
## G2 工具链前置

- 干净 checkout 必须存在受版本控制的 `simulations/BattleSim/BattleSim.csproj`；该项目使用 `.NET 10`、零外部依赖，且不得包含任何 `PackageReference`。
- 当前事实是干净 worktree 缺少该文件，且 `.gitignore` 的 `*.csproj` 会忽略它；因此现有 BattleSim 入口不可复现。
- TQ-052 的首个切片和首个提交必须先纳入该 `BattleSim.csproj`，并在 `.gitignore` 增加仅针对该文件的窄例外；首个提交只恢复工具链，不得同时修改 CT。
- 在该提交的干净 checkout 中，首次必须运行 `dotnet restore simulations/BattleSim/BattleSim.csproj --ignore-failed-sources`；预期成功并生成被忽略的 `simulations/BattleSim/obj/project.assets.json`。restore 成功后，再连续运行项目规定的 `dotnet build -c Release --no-restore simulations/BattleSim` 与 `dotnet run --no-build -c Release --project simulations/BattleSim`。
- restore → build → run 必须在干净 worktree 连续成功；restore 失败或项目含 `PackageReference` 时均不得进入 CT 修复。
- 任一 G2 任务标记完成前，该工具链前置都必须通过；本轮任务治理只登记要求，不创建 `BattleSim.csproj`。

## G2 任务执行边界

- **TQ-052 CT 反应方向**：先完成 G2 工具链前置，再修正公式、增加确定性行动顺序测试并产出修复前后对比；CT 增长对反应严格单调，其余条件相同时，反应 20 必须早于反应 10 到达首次行动阈值，同值顺序必须稳定可复现。
- **TQ-053 暴击倍率语义**：`docs/基础设定/战斗系统.txt` 是基础暴击倍率事实源，基础暴击为 1.5 倍；`docs/基础设定/角色数值设计.txt` 管理二级属性和加成来源。`critDamage` 表示在 1.5 倍基础上的附加百分比点，不是总倍率；固定样例为零加成 = 1.50、`critDamage = 15` = 1.65，元素附加另按百分比点加入。产出口径决策和 Unity/BattleSim 对齐，两侧固定样例必须一致，禁止同一字段在两侧表达不同含义。
- **TQ-054 21 Build 合法化**：复用或严格镜像实际角色创建的点数预算、上限和非线性成本；产出合法 Build 校验器、迁移表和非法输入测试；21 个 Build 必须全部合法，非法输入必须在进入矩阵前失败。
- **TQ-044 成长输入完整性**：产出成长表完整性断言、显式星级回退和回归；缺表、缺星级或非法组合必须明确失败或走批准的回退路径，每个矩阵 Build 的成长输入都必须可追溯，不得用静默 `0.2` 回退掩盖缺失。
- **TQ-055 G2 重验**：重跑修炼与 CTB 矩阵，产出可复现输出、境界样本覆盖、极端结果分类和 G2 结论。每个 Build 至少使用 200 个确定性修炼种子；每个目标矩阵格至少覆盖 20 对不同角色配对、总计至少 2000 场战斗，并输出胜率与 95% Wilson 置信区间。任一目标境界池少于 20、任一矩阵格配对少于 20 或场次少于 2000，一律记为 `INSUFFICIENT` 并继续阻塞；必须增加种子或配对后重跑，不能仅分类为“样本不足”就通过。0%/100% 结果只有满足上述覆盖且报告区间后才可判为设计极端；若归类为缺陷，必须修复并重跑。

## G2 出口条件

- 受版本控制的 `simulations/BattleSim/BattleSim.csproj` 与 `.gitignore` 窄例外已提交；`.NET 10`、零外部依赖项目不含任何 `PackageReference`，并已在干净 worktree 连续通过 restore → build → run，restore 生成的 `obj/project.assets.json` 保持被忽略。
- TQ-052、TQ-053、TQ-054、TQ-044、TQ-055 全部完成。
- 21 个 Build 全部满足角色创建规则，非法输入在进入矩阵前失败。
- Unity 与 BattleSim 的固定样例一致：零加成 = 1.50，`critDamage = 15` = 1.65；CT 增长对反应严格单调，反应 20 必须早于反应 10 到达首次行动阈值，同值顺序稳定可复现。
- 每个 Build 至少有 200 个确定性修炼种子；每个目标矩阵格至少有 20 对不同角色配对、总计至少 2000 场战斗，并报告胜率与 95% Wilson 置信区间。
- 任一目标境界池少于 20、任一矩阵格配对少于 20 或场次少于 2000 时必须标记 `INSUFFICIENT` 并保持 G2 阻塞，增加种子或配对后重跑；不得仅以“样本不足”分类通过。
- 所有 0%/100% 极端结果只有满足覆盖门槛并报告区间后才可判为设计极端；缺陷必须修复并重跑，存在未解释结果时 G2 保持阻塞。
```

- [ ] **Step 4: Verify the G2 chain and single TQ-044 definition**

Run:

```powershell
$g2Rows = @('N-TRUST-01 / TQ-052', 'N-TRUST-02 / TQ-053', 'N-TRUST-03 / TQ-054', 'N-BAL-02A / TQ-044', 'N-TRUST-04 / TQ-055')
foreach ($row in $g2Rows) { if ((Select-String -Path 开发管理/任务列表/数值与战斗任务.txt -SimpleMatch "| $row |").Count -ne 1) { throw "$row must occur exactly once" } }
rg -n "\| N-BAL-02 \|" 开发管理/任务列表/数值与战斗任务.txt
rg -n "阻塞（TQ-052）|阻塞（TQ-053）|阻塞（TQ-054）|阻塞（TQ-052、TQ-053、TQ-054、TQ-044）" 开发管理/任务列表/数值与战斗任务.txt
rg -n "G2 工具链前置|BattleSim.csproj|.NET 10|零外部依赖|仅针对该文件的窄例外|首个提交只恢复工具链" 开发管理/任务列表/数值与战斗任务.txt 开发管理/当前任务队列.txt
rg -n "dotnet restore|--ignore-failed-sources|PackageReference|project.assets.json|restore → build → run" 开发管理/任务列表/数值与战斗任务.txt 开发管理/当前任务队列.txt
rg -n "G2 任务执行边界|CT 增长对反应严格单调|反应 20 必须早于反应 10|战斗系统.txt|零加成 = 1.50|critDamage = 15.*1.65|每个矩阵 Build 的成长输入都必须可追溯" 开发管理/任务列表/数值与战斗任务.txt 开发管理/当前任务队列.txt
rg -n "200 个确定性修炼种子|20 对不同角色配对|2000 场战斗|95% Wilson 置信区间|INSUFFICIENT|缺陷必须修复并重跑" 开发管理/任务列表/数值与战斗任务.txt
rg -n "G2 出口条件|21 个 Build 全部满足角色创建规则|存在未解释结果时 G2 保持阻塞" 开发管理/任务列表/数值与战斗任务.txt
git diff --check
```

Expected: all five G2 rows occur exactly once; the standalone N-BAL-02 search has no match; the exact dependency states form an acyclic TQ-052 → TQ-053 → TQ-054 → TQ-044 → TQ-055 chain; TQ-052 first restores a tracked `.NET 10` BattleSim entry point with no `PackageReference`, then proves clean restore → build → run before strict CT monotonicity; TQ-053 uses the correct two-document authority split and 1.50/1.65 samples; TQ-055 enforces 200 seeds, 20 pairings, 2,000 battles, Wilson intervals, and blocking `INSUFFICIENT` outcomes. The complete Task 1 queue template must remain text-identical to `开发管理/当前任务队列.txt`, and this Task 4 G2 block must remain text-identical to the backlog section; historical rows remain unchanged.

- [ ] **Step 5: Commit the G2 backlog**

```powershell
git add -- 开发管理/任务列表/数值与战斗任务.txt 开发管理/当前任务队列.txt docs/superpowers/plans/2026-07-10-trust-gate-task-system-implementation.md
git commit -m "docs: add BattleSim clean restore gate"
```

### Task 5: Register G3 data semantics tasks and absorb TQ-040

**Files:**

- Modify: `开发管理/任务列表/数据链路任务.txt`
- Modify: `开发管理/当前任务队列.txt`（仅强化 TQ-056 卡片，不改六行或状态）
- Modify: `docs/superpowers/plans/2026-07-10-trust-gate-task-system-implementation.md`（同步 Task 1 模板与本 Task 5）

- [ ] **Step 1: Replace the stale data-chain status claim**

Append under `## 当前状态`:

```text
- ⚠️ 2026-07-10 审视：当前检查存在术法 docs 86 与 CSV/asset 75 的数量矛盾、`realm_lianshen` 语言键缺失、`realm_lianxu` 已删除内容仍激活；脚本仍返回 `OK`，因此 Missing 0 不等于数据链可信。
- ⚠️ `contentScope` 缺失时当前默认 `player`，运行时未证明 reserved 过滤；`realmReq`、`elementReq`、`affiliation` 也未形成完整限制链。G3 通过前禁止按数据规模继续扩写。
```

- [ ] **Step 2: Replace D-ASSET/D-IMPORT rows and add the G3 chain**

Replace the existing D-ASSET-01 and D-IMPORT-01 rows with:

```text
| D-ASSET-01 / TQ-040 | P2 | DeepSeek | 已合并至 TQ-057 | CSV/asset 字段完整性复查由 D-TRUST-02 统一执行，不再单独领取 |
| D-IMPORT-01 / TQ-043 | P0 | Codex | ⚠️ 已修改/待复审（HANDOFF-20260710-02） | 按表头解析全部核心字段；缺必需列/重复列失败，未知列按显式兼容策略处理 |
| D-TRUST-01 / TQ-056 | P0 | Codex | 阻塞（TQ-043 复审） | 检查脚本区分错误和警告；语义错误必须非零退出 |
| D-TRUST-02 / TQ-057 | P0 | DeepSeek 执行；Codex 复审 | 阻塞（TQ-056） | 对齐术法数量、补齐或隔离缺失语言键、处理已删除仍激活内容，并吸收 TQ-040 |
| D-TRUST-03 / TQ-058 | P0 | Codex | 阻塞（TQ-043、TQ-056） | `contentScope` 缺失/非法时失败关闭，并证明 CSV→asset→运行时过滤链 |
| D-TRUST-04 / TQ-059 | P0 | Codex | 阻塞（TQ-043、TQ-056、TQ-058） | 为 `realmReq`、`elementReq`、`affiliation` 逐字段选择并验证 `runtime_gate` 或 `metadata_only` |
```

- [ ] **Step 3: Write the approved G3 execution boundaries, queue card, and exit criteria**

Insert before `## 默认验证`:

```text
## G3 任务执行边界

- TQ-043：仅在 HANDOFF-20260710-02 经 Codex / ChatGPT5.5 独立复审通过后解阻；复审核对必需列、重复列、未知列、换序和短行，不得把外部提交直接当作完成。
- TQ-056：错误级必须包括无批准豁免的 docs/CSV/asset 数量矛盾、必填缺失、删除内容激活、玩家边界失守和 schema 错配；每个批准警告豁免必须在版本控制中精确绑定 `ruleId + contentId/文件/行键`，并记录理由、负责人、移除或到期条件。禁止通配符、类别级、前缀匹配或自动覆盖未来记录；新增同类但未精确列入的警告必须非 0，并加入回归负例。产出成功/失败测试样例，负例不得修改生产数据；错误或缺失结果必须非 0，仅精确批准的警告可返回 0。
- TQ-057：机械清理由 DeepSeek 执行，但必须标 `⚠️ 已修改/未审核` 并交接；产出数据修复、差异清单和交接材料；术法数量、语言键、删除激活和原 TQ-040 字段完整性均须处理。差异要么清零，要么有批准的显式排除；不得扩展设计语义。
- TQ-058：缺失或非法 `contentScope` 必须导入失败；reserved 不会进入玩家获得池，player 仍可用；既有空字段 asset 必须迁移、隔离或令检查失败，禁止静默视为 player。证明 CSV→asset→运行时 consumer 全链路并建立回归。
- TQ-059：对 `realmReq`、`elementReq`、`affiliation` 逐字段记录模式，每个字段必须恰好选择一条：`runtime_gate` 要求真实 asset 字段和 runtime consumer，并有一个允许样例和一个拒绝样例；`metadata_only` 要求改成明确不含 Req/限制含义的元数据命名，导入器和运行时限制 consumer 均不得读取它，测试证明不会被当作限制，且检查器拒绝“仍叫 `*Req` 但无 handler”的状态。任一字段无模式或两种模式同时存在均阻塞。

## G3 出口条件

- TQ-043 经 Codex / ChatGPT5.5 独立复审通过，TQ-056、TQ-057、TQ-058、TQ-059 全部完成，且 TQ-057 经 Codex 复审通过。
- 数量矛盾、必填字段缺失、删除内容激活、玩家内容边界失守和 schema 不匹配等错误类别均让检查脚本非 0 退出。
- 每个残留批准警告豁免都在版本控制中精确绑定 `ruleId + contentId/文件/行键`，并记录理由、负责人、移除或到期条件；禁止通配符、类别级、前缀匹配或自动覆盖未来记录，新增同类但未精确列入的警告必须非 0。
- reserved 不进入玩家获得池，缺失或非法 `contentScope` 不会默认成为 player。
- `realmReq`、`elementReq`、`affiliation` 各自完成其记录模式的验收并恰好选择 `runtime_gate` 或 `metadata_only`；任一字段无模式或两种模式同时存在均阻塞。`runtime_gate` 必须有运行时允许/拒绝样例；`metadata_only` 必须完成无 Req/限制含义重命名、导入器与运行时限制 consumer 不读取证明、不会被当作限制的测试，以及对“仍叫 `*Req` 但无 handler”状态的检查器拒绝。
- `check-data-chain.ps1` 正例返回 0、负例返回非 0，且负例不得修改生产数据。
```

Replace the TQ-056 card in `开发管理/当前任务队列.txt`, and make the Task 1 queue template text-identical:

```text
### TQ-056 · D-TRUST-01 数据检查器错误分级

- 来源：可信度闸门规格 §6。
- 当前状态：阻塞（TQ-043 复审）；依赖：TQ-043 经 Codex / ChatGPT5.5 复审通过。
- 主责：Codex / gpt-5.5。
- 必读：`tools/check-data-chain.ps1`、`开发管理/docs-csv-asset-alignment.txt`、`开发管理/realm_lianshen专项检查.txt`、三类 CSV 与 Unity 数据对象定义。
- 范围：把无批准豁免的 docs/CSV/asset 数量矛盾、必填字段缺失、删除内容激活、玩家内容边界失守和 schema 不匹配定义为错误并返回非 0；每个批准警告豁免必须在版本控制中精确绑定 `ruleId + contentId/文件/行键`，记录理由、负责人、移除或到期条件；禁止通配符、类别级、前缀匹配或自动覆盖未来记录。
- 验证：产出成功与失败测试样例，负例不得修改生产数据；新增同类但未精确列入的警告必须返回非 0 并作为回归负例；错误或缺失结果必须返回非 0，仅剩精确批准警告时 `powershell -ExecutionPolicy Bypass -File tools/check-data-chain.ps1` 返回 0；`git diff --check`。
- 完成条件：检查脚本能真实区分成功、警告和错误；正例返回 0、负例返回非 0，且仅精确绑定并记录完整的批准警告可返回 0。
```

Replace:

```text
- 不补 `realm_lianshen` Language key，不扩展 C# realm order，除非 Codex / ChatGPT5.5 重新给出架构决策。
```

with:

```text
- 不把 `realm_lianshen` 扩为玩家境界；缺失 Language key 必须补齐、隔离或停用，不能保持激活且只告警。
```

- [ ] **Step 4: Verify G3 IDs, merge state, and updated boundary**

Run:

```powershell
$g3Rows = @('D-ASSET-01 / TQ-040', 'D-IMPORT-01 / TQ-043', 'D-TRUST-01 / TQ-056', 'D-TRUST-02 / TQ-057', 'D-TRUST-03 / TQ-058', 'D-TRUST-04 / TQ-059')
foreach ($row in $g3Rows) { if ((Select-String -Path 开发管理/任务列表/数据链路任务.txt -SimpleMatch "| $row |").Count -ne 1) { throw "$row must occur exactly once" } }
if (Select-String -Path 开发管理/任务列表/数据链路任务.txt -Pattern '^\| D-ASSET-01 \|' -Quiet) { throw 'stale D-ASSET-01 row remains' }
if (Select-String -Path 开发管理/任务列表/数据链路任务.txt -Pattern '^\| D-IMPORT-01 \|' -Quiet) { throw 'stale D-IMPORT-01 row remains' }
rg -n "已合并至 TQ-057|⚠️ 已修改/待复审（HANDOFF-20260710-02）|阻塞（TQ-043 复审）" 开发管理/任务列表/数据链路任务.txt
rg -n "G3 任务执行边界|runtime_gate|metadata_only|恰好选择|ruleId|contentId|禁止通配符|新增同类.*未精确列入.*非 0|负例不得修改生产数据|既有空字段 asset|正例返回 0、负例返回非 0" 开发管理/任务列表/数据链路任务.txt 开发管理/当前任务队列.txt
rg -n "不把 `realm_lianshen` 扩为玩家境界|缺失 Language key 必须补齐、隔离或停用|G3 出口条件|check-data-chain.ps1.*正例返回 0、负例返回非 0" 开发管理/任务列表/数据链路任务.txt
rg -n "TQ-040：不单独执行；仅在 TQ-057 已登记且前置满足后由其吸收" 开发管理/当前任务队列.txt
(Select-String -Path 开发管理/当前任务队列.txt -Pattern '^\| TQ-' | Measure-Object).Count
Select-String -Path 开发管理/当前任务队列.txt -Pattern '^\| TQ-.*\| 待处理 \|'
$planText = Get-Content -Raw docs/superpowers/plans/2026-07-10-trust-gate-task-system-implementation.md
$templateMatch = [regex]::Match($planText, '(?s)Use `apply_patch` to create `开发管理/当前任务队列\.txt` with exactly this content:\s*```text\r?\n(?<queue>.*?)\r?\n```')
if (-not $templateMatch.Success) { throw 'Task 1 queue template not found' }
$templateQueue = ($templateMatch.Groups['queue'].Value -replace "`r`n", "`n").TrimEnd("`n")
$currentQueue = ((Get-Content -Raw 开发管理/当前任务队列.txt) -replace "`r`n", "`n").TrimEnd("`n")
if (-not [string]::Equals($templateQueue, $currentQueue, [System.StringComparison]::Ordinal)) { throw 'Task 1 queue template differs from current queue' }
$allowedFiles = @('docs/superpowers/plans/2026-07-10-trust-gate-task-system-implementation.md', '开发管理/任务列表/数据链路任务.txt', '开发管理/当前任务队列.txt') | Sort-Object
$changedFiles = @(git -c core.quotepath=false diff --name-only) | Sort-Object
if (($allowedFiles -join "`n") -ne ($changedFiles -join "`n")) { throw 'Task 5 must modify exactly the three allowed files' }
powershell -ExecutionPolicy Bypass -File tools/check-review-text.ps1 -Paths 开发管理,docs/superpowers/plans
git diff --check
git status --short
```

Expected: all six G3 rows occur exactly once; no stale standalone D-ASSET-01/D-IMPORT-01 row remains; TQ-043 remains pending review under `HANDOFF-20260710-02`; TQ-056 waits for TQ-043 review; TQ-040 cannot be claimed separately. TQ-059 requires each field to choose exactly one recorded `runtime_gate` or `metadata_only` mode and blocks missing or dual modes; no unconditional three-field runtime-sample requirement remains. TQ-056 warnings are exact, non-wildcard exceptions keyed by `ruleId + contentId/文件/行键`, and a newly introduced same-class warning not explicitly listed is a nonzero regression case. The old “do not add Language key” boundary is absent. The active queue still has exactly six rows with only TQ-049 and TQ-052 pending, and its complete text remains identical to the Task 1 template. Review-text and diff checks pass, and only the three files listed for this task are modified.

- [ ] **Step 5: Commit the G3 backlog**

```powershell
git add -- 开发管理/任务列表/数据链路任务.txt 开发管理/当前任务队列.txt docs/superpowers/plans/2026-07-10-trust-gate-task-system-implementation.md
git commit -m "docs: register data semantics gate tasks"
```

### Task 6: Cross-file consistency and workflow guard verification

**Files:**

- Verify: `docs/superpowers/specs/2026-07-10-trust-gates-and-vertical-slice-task-system-design.md`
- Verify: `docs/superpowers/plans/2026-07-10-trust-gate-task-system-implementation.md`
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
powershell -ExecutionPolicy Bypass -File tools/check-review-text.ps1 -Paths AGENTS.md,CLAUDE.md,开发管理,docs/superpowers/plans
git diff --check 3c8c1cd..HEAD
$placeholderPattern = @('T' + 'BD', 'T' + 'ODO', 'implement ' + 'later', 'fill in ' + 'details', '待' + '定', '以后' + '再说', '视情' + '况') -join '|'
rg -n -i -- $placeholderPattern docs/superpowers/specs/2026-07-10-trust-gates-and-vertical-slice-task-system-design.md docs/superpowers/plans/2026-07-10-trust-gate-task-system-implementation.md
$placeholderExit = $LASTEXITCODE
if ($placeholderExit -ne 1) { throw 'The spec/plan placeholder scan must return 1 (no matches).' }
Write-Output 'placeholder matches=0'
```

Expected: review-text script passes; every commit after the complete task-system baseline `3c8c1cd` has no whitespace errors. The constructed placeholder scan exits `1` with no matches in this specification and plan; historical game-content prose is intentionally outside this scan.

- [ ] **Step 2: Verify all task IDs and no claimable frozen content**

Run:

```powershell
rg -n "TQ-0(39|40|43|44|45|49|50|51|52|53|54|55|56|57|58|59|60|61|62|63|64|65|66|67|68|69|70|71)" 开发管理/当前任务队列.txt 开发管理/任务列表 开发管理/开发优先级.txt
rg -n "\| (C-TAIYI-01|C-GUXIU-03|C-SANXIU-01|C-NPC-01|C-STORY-WM-L1|C-STORY-WM-L2|C-STORY-04|C-QUEST-01|C-DIALOGUE-01|C-ECON-01|C-HIGH-01) .*\| 待处理 \|" 开发管理/任务列表/内容设计任务.txt

$sceneFile = '开发管理/任务列表/场景与Unity任务.txt'
$sceneIds = @('TQ-049', 'TQ-050', 'TQ-051', 'TQ-060', 'TQ-061', 'TQ-062', 'TQ-063', 'TQ-064', 'TQ-065', 'TQ-066', 'TQ-067', 'TQ-068', 'TQ-069', 'TQ-070')
foreach ($id in $sceneIds) {
    $count = @(Select-String -LiteralPath $sceneFile -Pattern "^\| [^|]+ / $id \|").Count
    Write-Output "$id scene rows=$count"
    if ($count -ne 1) { throw "$id must have exactly one scene activity row." }
}

$balanceFile = '开发管理/任务列表/数值与战斗任务.txt'
$balanceIds = @('TQ-044', 'TQ-052', 'TQ-053', 'TQ-054', 'TQ-055')
foreach ($id in $balanceIds) {
    $count = @(Select-String -LiteralPath $balanceFile -Pattern "^\| [^|]+ / $id \|").Count
    Write-Output "$id balance rows=$count"
    if ($count -ne 1) { throw "$id must have exactly one balance activity row." }
}
$oldBalanceCount = @(Select-String -LiteralPath $balanceFile -Pattern '^\| N-BAL-02 /').Count
Write-Output "old N-BAL-02 rows=$oldBalanceCount"
if ($oldBalanceCount -ne 0) { throw 'The old N-BAL-02 activity row must be absent.' }

$dataFile = '开发管理/任务列表/数据链路任务.txt'
$dataIds = @('TQ-040', 'TQ-043', 'TQ-056', 'TQ-057', 'TQ-058', 'TQ-059')
foreach ($id in $dataIds) {
    $count = @(Select-String -LiteralPath $dataFile -Pattern "^\| [^|]+ / $id \|").Count
    Write-Output "$id data rows=$count"
    if ($count -ne 1) { throw "$id must have exactly one data activity row." }
}
if (@(Select-String -LiteralPath $dataFile -Pattern '^\| [^|]+ / TQ-043 \| [^|]+ \| [^|]+ \| ⚠️ 已修改/待复审（HANDOFF-20260710-02） \|').Count -ne 1) { throw 'TQ-043 must remain pending review.' }
if (@(Select-String -LiteralPath $dataFile -Pattern '^\| [^|]+ / TQ-056 \| [^|]+ \| [^|]+ \| 阻塞（TQ-043 复审） \|').Count -ne 1) { throw 'TQ-056 must wait for the TQ-043 review.' }
if (@(Select-String -LiteralPath $dataFile -Pattern '^\| [^|]+ / TQ-040 \| [^|]+ \| [^|]+ \| 已合并至 TQ-057 \|').Count -ne 1) { throw 'TQ-040 must remain non-claimable and absorbed by TQ-057.' }

$contentFile = '开发管理/任务列表/内容设计任务.txt'
$gateExitCount = @(Select-String -LiteralPath $contentFile -Pattern '^\| [^|]+ / TQ-071 \|').Count
Write-Output "TQ-071 content rows=$gateExitCount"
if ($gateExitCount -ne 1) { throw 'TQ-071 must have exactly one content activity row.' }
$frozenContentIds = @('C-TAIYI-01', 'C-GUXIU-03', 'C-SANXIU-01', 'C-NPC-01', 'C-STORY-WM-L1', 'C-STORY-WM-L2', 'C-STORY-04', 'C-QUEST-01', 'C-DIALOGUE-01', 'C-ECON-01', 'C-HIGH-01')
foreach ($id in $frozenContentIds) {
    $count = @(Select-String -LiteralPath $contentFile -Pattern "^\| $([regex]::Escape($id)) \| P[123] \| [^|]+ \| 内容冻结（TQ-071 前） \|").Count
    Write-Output "$id frozen activity rows=$count"
    if ($count -ne 1) { throw "$id must have exactly one frozen activity row and no pending activity row." }
}
```

Expected: the task-ID search finds the designed dispositions and the frozen-content search exits `1` with no pending matches. Scene TQ-049～TQ-051 and TQ-060～TQ-070 each have exactly one activity row. Balance TQ-044 and TQ-052～TQ-055 each have exactly one activity row, with zero old N-BAL-02 rows. Data TQ-040、TQ-043、TQ-056～TQ-059 each have exactly one activity row; TQ-043 remains pending review, TQ-056 waits for that review, and TQ-040 is not separately claimable. Content TQ-071 has exactly one activity row, and all eleven named content activities each have exactly one frozen row and no pending row.

- [ ] **Step 3: Verify the current queue is short and dependency-safe**

Run:

```powershell
$queueFile = '开发管理/当前任务队列.txt'
$queueRows = @(Select-String -LiteralPath $queueFile -Pattern '^\| TQ-')
Write-Output "queue rows=$($queueRows.Count)"
if ($queueRows.Count -ne 6) { throw 'The current queue must contain exactly six task rows.' }
$claimableRows = @(Select-String -LiteralPath $queueFile -Pattern '^\| TQ-.*\| 待处理 \|')
$claimableIds = @($claimableRows | ForEach-Object { if ($_.Line -match '^\| (TQ-\d{3}) \|') { $Matches[1] } })
$claimableRows
if (@(Compare-Object -ReferenceObject @('TQ-049', 'TQ-052') -DifferenceObject $claimableIds).Count -ne 0) { throw 'Only TQ-049 and TQ-052 may be claimable.' }
$priorityRulePattern = '^3\. 内容冻结期间 P0 闸门任务始终优先；当没有主责匹配且依赖已满足的 P0 闸门任务时，才可补已登记、依赖满足的 P1 架构/状态任务或 TQ-045 等明确后置非内容任务；禁止补冻结内容或提前执行 TQ-071。$'
if (@(Select-String -LiteralPath $queueFile -Pattern $priorityRulePattern).Count -ne 1) { throw 'The P0-first and frozen-content guard rule must appear exactly once.' }
```

Expected: queue count is `6`; claimable rows are exactly TQ-049 and TQ-052. The queue states that eligible P0 gates take priority, and only when no matching dependency-ready P0 is claimable may a registered dependency-ready P1 architecture/state task or an explicit later non-content task such as TQ-045 be added; frozen content and premature TQ-071 are forbidden.

- [ ] **Step 4: Confirm no product files changed**

Run:

```powershell
git diff --name-only 3c8c1cd..HEAD
$allowedPaths = @(
    'docs/superpowers/plans/2026-07-10-trust-gate-task-system-implementation.md'
    '开发管理/当前任务队列.txt'
    '开发管理/开发优先级.txt'
    '开发管理/任务列表/场景与Unity任务.txt'
    '开发管理/任务列表/数值与战斗任务.txt'
    '开发管理/任务列表/数据链路任务.txt'
    '开发管理/任务列表/内容设计任务.txt'
    '开发管理/任务归档/2026-07-10-可信度闸门重排前队列归档.txt'
)
$changedPaths = @(git -c core.quotepath=false diff --name-only 3c8c1cd..HEAD)
$pathDelta = @(Compare-Object -ReferenceObject ($allowedPaths | Sort-Object) -DifferenceObject ($changedPaths | Sort-Object))
if ($pathDelta.Count -ne 0) { $pathDelta | Format-Table | Out-String | Write-Output; throw 'Changed paths do not exactly match the Task 6 allowlist.' }
git status --short
if (@(git status --short).Count -ne 0) { throw 'The worktree must be clean.' }
```

Expected: `git diff --name-only 3c8c1cd..HEAD` contains exactly the plan file, current queue, priority document, scene/Unity backlog, balance/combat backlog, data-chain backlog, content-design backlog, and dated queue archive listed above—no other path. The worktree is clean, so no `src/`, `simulations/`, `data/`, or game-content prose changed.

- [ ] **Step 5: Record the activation result**

No additional commit is needed when Steps 1–4 pass. Report:

```text
G1/G2/G3 task system: activated
Current queue rows: 6
Claimable tasks: TQ-049, TQ-052
Content expansion: frozen until TQ-071
TQ-043: pending review; TQ-056: blocked
Product files changed: no
```
