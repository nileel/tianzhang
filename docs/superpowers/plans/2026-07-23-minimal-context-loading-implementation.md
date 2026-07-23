# Minimal Context Loading Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Reduce the fixed and initial project context used by one complex task without weakening fact-source, authorization, review, numerical-simulation, or verification requirements.

**Architecture:** Keep `AGENTS.md` as the concise shared project entry and `CLAUDE.md` as a Claude/DeepSeek-specific identity and authorization adapter. Define one section/symbol/entity-level `必查范围` contract in the existing management rules, then make all executor routes use the existing `rg` and bounded file reads instead of adding an index, database, MCP server, or script.

**Tech Stack:** Markdown/TXT rules, existing `rg`, PowerShell 7, Git, existing review-text and whitespace checks.

## Global Constraints

- Do not add RAG, embeddings, a vector database, MCP retrieval, a background indexer, sidecars, a context monitor, or a custom Codex compaction configuration.
- Do not batch-rewrite existing task cards; path-only `必读` entries receive the new bounded-read semantics until each card is naturally touched.
- Preserve the current domain truth responsibilities: `docs/` for design facts, `开发管理/` for task/status facts, BattleSim execution for numerical conclusions, and `src/` for current Unity behavior.
- Preserve Claude/DeepSeek identity, assignment, no-self-review, wrapper, and two-commit boundaries by routing to their existing authoritative sections; do not duplicate those long procedures in the root entries.
- Project PowerShell commands use PowerShell 7 only.
- Preserve UTF-8 BOM for modified `.md` and `.txt` files.
- Before each commit, run `tools/check-pending-whitespace.ps1` on exactly the planned paths, stage only those paths, and run `git diff --cached --check`.
- At execution start, inspect `git status --short`; never read, stage, overwrite, or commit unrelated user or automation changes.

---

### Task 1: Replace the two oversized root instruction entries

**Files:**
- Modify: `AGENTS.md:1-119`
- Modify: `CLAUDE.md:1-end`

**Interfaces:**
- Consumes: Existing authoritative details in `开发管理/AI协作规则.txt`, `开发管理/DeepSeek工作提示词.txt`, `开发管理/自动工作流规则.txt`, `开发管理/审核入口.txt`, and `开发管理/总结规则.txt`.
- Produces: A shared root rule entry with bounded-read semantics, plus a Claude/DeepSeek adapter that references rather than duplicates shared rules.

- [ ] **Step 1: Verify the baseline and planned-path isolation**

Run:

```powershell
git status --short

$expected = @{
  'AGENTS.md' = 13153
  'CLAUDE.md' = 16928
}

foreach ($entry in $expected.GetEnumerator()) {
  $actual = (Get-Item -LiteralPath $entry.Key).Length
  if ($actual -ne $entry.Value) {
    throw "$($entry.Key) changed from the reviewed baseline: expected $($entry.Value), got $actual"
  }
}
```

Expected: neither planned file appears in `git status`; byte sizes are exactly 13,150 and 16,928. If either file changed, stop and rebase this task on the new rule text instead of overwriting it.

- [ ] **Step 2: Replace `AGENTS.md` with the concise shared entry**

Replace the whole file with the following UTF-8 BOM content:

```markdown
# 《天章》项目共享规则

## 项目与事实源

- 游戏是 2D 沙盒世界、战棋玩法的修仙游戏；设定原文默认位于 `docs/`。
- 设计与设定事实读取 `docs/`；任务、进度与当前风险读取 `开发管理/`；角色数值结论读取 BattleSim 实际运行；Unity 当前行为读取 `src/`。
- 交接、摘要、索引和检索结果只提供线索，不能替代上述事实源。
- 用户当次明确要求优先；没有授权时不得扩大主责、修改范围或验证范围。

## 最小上下文加载

- 复杂任务先用 `rg`、文件名、标题、稳定 ID 或代码符号定位，再读取当前步骤所需原文；发现新依赖时补读。
- 任务卡中的 `必读` 与 `必查范围` 默认要求查明相关章节、符号、实体或用途，不表示整份加载。只有写明“完整文件”、无法保持语义边界、检索无结果或存在冲突时才读取全文。
- 修改前必须读取目标完整逻辑单元，例如完整方法、规则段、CSV 行或资源说明。检索片段只用于导航。
- 正常任务默认不读取历史归档；只有追溯原因、审核历史或迁移来源时进入归档。
- 具体语法与任务卡维护规则见 `开发管理/状态与建议维护规则.txt`。

## 身份与执行边界

- 被问到“你是谁”或纯 `1` / `2` 工作流要求确认身份时，Codex 通过 Node REPL 读取 `nodeRepl.requestMeta['x-codex-turn-metadata'].model`，以实际值为准。
- 每小时控制器启动的 Codex 责任方使用控制器通过 `tools/codex-cli-session.ps1 -Model` 和首条 stdin 传入的核验证明；不因 `codex exec` 子会话返回 `unknown` 再次阻塞。细节读取 `开发管理/AI协作规则.txt`。
- Claude Code、DeepSeek 和 Claude wrapper 的身份、主责、提交与复审边界读取 `CLAUDE.md` 及其路由的专项事实源。
- 不得以“能够实现”推导出“获得任务主责”；DeepSeek / Claude 不得自审。

## 领域必查入口

### 功法、术法与神通

- 功法：`docs/角色养成/功法设计规范.txt` 的模板字段、核心约束、任务相关成长规则和新增检查清单；`docs/角色养成/功法/功法设计.txt` 的功法模板字段。
- 术法：`docs/角色养成/术法设计规范.txt` 的对应模板字段、核心设计原则、通用约束和新增检查清单；`docs/角色养成/术法/术法设计.txt` 的对应术法模板字段。
- 神通：`docs/角色养成/神通设计规范.txt` 的模板字段、对应类型、核心设计原则、通用约束和新增检查清单；`docs/角色养成/神通/神通设计.txt` 的神通模板字段。
- 已有内容参考、分布表、倍率、成长、升格和丹相资料只在任务相关时读取。
- 非规则描述类完成品每项单独一个文件。

### 剧情、任务、NPC 与演出

- 先查 `docs/剧情/剧情生产规范.txt` 的事实源路由、对应内容类型、信息边界与禁止项、AI 生产流程和审核清单。
- 再按实际涉及对象读取世界背景、重要 NPC 规范以及相关 NPC、地图、门派、高阶、册界或战斗环境章节；不得因规范列出一个目录就读取整个目录。

### 数值与 BattleSim

- 讨论或验证角色数值平衡必须运行 BattleSim，不凭文本或直觉锁定结论。
- 修改模拟器后先运行：
  `dotnet build -c Release --no-restore "D:\天章游戏开发\simulations\BattleSim"`
- 构建通过后运行：
  `dotnet run --no-build -c Release --project "D:\天章游戏开发\simulations\BattleSim"`
- 只讨论既有结果且相关输入未变化时，不重复已经通过的同范围运行。

### 开发与美术

- 技术实现只查 `开发管理/开发-技术经验.txt` 中与当前问题相关的章节；系统事实只查 `开发管理/设计-当前状态.txt` 中的相关部分。
- 修改定时自动工作流时先查 `开发管理/自动工作流规则.txt`，再按其路由读取状态和任务事实源。
- 生成或整理角色立绘时先查 `assets/generated-character-art/README.md`、目标角色直接事实和目标用途提示词；直接事实不足时才补读更广背景，确需比较、生成或编辑时才加载图片本体。

## 审核、协作与管理路由

- 审核、复审、未审核标记、审核归档或未通过清单：先读 `开发管理/审核入口.txt`，再按命中路由读取细则和事实源。
- 双 AI 分工、交接、疑点、返工或冲突：读 `开发管理/AI协作规则.txt` 与当前 `开发管理/AI合作沟通.txt`。
- 规划下一步或纯 `1`：先读 `开发管理/当前任务队列.txt`；无适用项时按 `开发管理/状态与建议维护规则.txt` 进入分线 backlog 和建议索引。
- 纯 `2`：先读审核入口，只处理复审；DeepSeek / Claude 不得自审。
- 用户说“总结”：完整执行 `开发管理/总结规则.txt`。
- 普通执行任务不因存在未审核、交接或复审文件而预读审核材料；只有命中路由时才读取。

## 修改与验证

- 先确认根因，再做满足需求的最小修改；不得用猜测性补丁、额外兜底、兼容分支、重试层或新状态掩盖未查明问题。
- 如果修复开始连续叠加补丁、跨越多个原定边界或突破停止条件，立即停止并重新判断根因。
- 默认使用与风险和影响面相称的最小充分验证；相关输入未变化时不重复同范围检查。共享基础设施、安全/写入隔离、不可逆迁移、核心架构或数据语义、数值平衡、外部交接复审、已有失败/冲突证据或用户明确要求时才升级验证。
- 项目 PowerShell 脚本只支持 PowerShell 7；独立进程使用 `pwsh -NoProfile -ExecutionPolicy Bypass -File ...`。
- 暂存前对本轮预期路径运行 `tools/check-pending-whitespace.ps1`；暂存后运行 `git diff --cached --check`。不得 stage 或提交无关改动。
- 审核文本检查：`pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-review-text.ps1 -Paths AGENTS.md,CLAUDE.md,开发管理`
- docs / CSV / Unity 数据链路检查只在相关路径变化时运行：`pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-data-chain.ps1`

## 快捷推进

- 纯 `1` / `2` 的身份、选题、标题、主责与提交细节以 `开发管理/AI协作规则.txt` 为唯一完整规范。
- `1` 先选当前 AI 主责的执行型任务，跳过待复审项；只有命中未审核、交接或复审路由时读取审核入口。
- `2` 先读审核入口并只做复审；DeepSeek / Claude 只能补齐交接材料并请求 Codex 处理。
- 任何非纯 `1` / `2` 的明确用户要求优先。
```

- [ ] **Step 3: Replace `CLAUDE.md` with the identity and authorization adapter**

Replace the whole file with the following UTF-8 BOM content:

```markdown
# Claude / DeepSeek 项目入口

## 共享规则

- 先读取根 `AGENTS.md`，遵守其中的事实源、最小上下文加载、领域入口、修改和验证规则。
- 本文件只定义 Claude / DeepSeek 特有的身份与授权，不复制 `AGENTS.md` 的共同项目规则。

## 实际身份与修改方

- 当 `ANTHROPIC_BASE_URL` 为 `http://127.0.0.1:15721/claude-desktop` 时，实际身份与修改方为 `DeepSeek V4 Pro`，不得自称 Codex 或 Claude。
- 其他 Claude CLI 环境的实际身份与修改方为 `Claude Code`。
- 原生 Claude Code 读取 `开发管理/DeepSeek工作提示词.txt` 时，只继承任务路由、执行范围、未审核标记和交接格式，不采用其中的 DeepSeek 身份或修改方名称。

## 主责与复审边界

- Claude / DeepSeek 只可领取状态为“待处理”、非复审，且主责明确为 `DeepSeek V4 Pro`、`Claude Code` 或 `Claude / DeepSeek` 的任务。
- 不得领取主责为 `Codex`、`ChatGPT5.5`、`Codex / gpt-5.5` 或未明确授权的任务；用户当次明确指派 Claude Code 的具体任务除外。
- 不得自审、预填审核方结论、扩大授权路径、另行并行派发或推送远端。
- 每次选题前记录实际身份、修改方、允许主责及候选任务 ID / 主责 / 状态；没有合法候选时记录 `skipped_cleanly` 后退出，不修改项目文件。

## 必读路由

- 普通 Claude / DeepSeek 执行任务先按 `AGENTS.md` 和任务卡定位必查范围。
- 纯 `1` / `2` 的完整选择、身份自检和角色例外读取 `开发管理/AI协作规则.txt`。
- DeepSeek 执行读取 `开发管理/DeepSeek工作提示词.txt` 的身份锚定与对应任务路由。
- `tzg-hourly-controller` wrapper 启动的外部责任方，在任何修改前必须读取 `开发管理/AI协作规则.txt` 的 Claude / wrapper 边界和 `开发管理/DeepSeek工作提示词.txt` 的 wrapper 边界。

## 外部责任方边界

- wrapper 只在调度器已选中合法候选、取得单写入租约并给出授权范围后启动；无合法候选时不得预检或空转。
- 外部责任方按专项事实源端到端完成 workspace guard、实施、最小充分验证、未审核标记、任务状态和路径限定的 `businessCommit`，随后只修改 `开发管理/AI合作沟通.txt` 创建 `handoffCommit`。
- `businessCommit`、`handoffCommit`、自动化元数据、恢复 session、外层 Codex 边界和禁止操作的完整规则，以 `开发管理/AI协作规则.txt` 与 `开发管理/DeepSeek工作提示词.txt` 为准；不得在本文件维护第二份副本。
```

- [ ] **Step 4: Verify the root entries are smaller and structurally complete**

Run:

```powershell
$agents = Get-Content -Raw -LiteralPath 'AGENTS.md'
$claude = Get-Content -Raw -LiteralPath 'CLAUDE.md'

if ((Get-Item -LiteralPath 'AGENTS.md').Length -ge 13153) {
  throw 'AGENTS.md did not shrink from the reviewed baseline.'
}
if ((Get-Item -LiteralPath 'CLAUDE.md').Length -ge 16928) {
  throw 'CLAUDE.md did not shrink from the reviewed baseline.'
}

$agentRequired = @(
  '## 最小上下文加载',
  '## 领域必查入口',
  '开发管理/审核入口.txt',
  '开发管理/自动工作流规则.txt',
  '开发管理/总结规则.txt',
  'tools/check-pending-whitespace.ps1'
)
foreach ($literal in $agentRequired) {
  if (-not $agents.Contains($literal)) { throw "AGENTS.md missing: $literal" }
}

$claudeRequired = @(
  'http://127.0.0.1:15721/claude-desktop',
  'skipped_cleanly',
  '开发管理/AI协作规则.txt',
  '开发管理/DeepSeek工作提示词.txt',
  'businessCommit',
  'handoffCommit'
)
foreach ($literal in $claudeRequired) {
  if (-not $claude.Contains($literal)) { throw "CLAUDE.md missing: $literal" }
}

$claudeForbidden = @(
  '# 游戏类型',
  '# 设计规范',
  '# 数值模拟',
  '# 开发管理文件',
  '## 高频检查脚本',
  '## Unity 项目',
  '## CLI 模式'
)
foreach ($literal in $claudeForbidden) {
  if ($claude.Contains($literal)) { throw "CLAUDE.md still duplicates: $literal" }
}
```

Expected: exit zero; both files are smaller than their reviewed baselines; every required route remains; `CLAUDE.md` no longer duplicates shared sections.

- [ ] **Step 5: Run focused text checks and commit the two entries**

Run:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-review-text.ps1 -Paths AGENTS.md,CLAUDE.md
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-pending-whitespace.ps1 -ExpectedPaths 'AGENTS.md|CLAUDE.md'
git add -- AGENTS.md CLAUDE.md
git diff --cached --check
git diff --cached --name-status
git commit -m "docs: slim project instruction entries"
```

Expected: both checks exit zero; the staged set contains exactly `AGENTS.md` and `CLAUDE.md`; the commit succeeds.

---

### Task 2: Make bounded `必查范围` the single task-card reading contract

**Files:**
- Modify: `开发管理/状态与建议维护规则.txt:26-41`
- Modify: `开发管理/AI协作规则.txt:3-10`
- Modify: `开发管理/AI协作规则.txt:73-82`
- Modify: `开发管理/DeepSeek工作提示词.txt:12-22`

**Interfaces:**
- Consumes: The root `先定位、后读取` behavior from Task 1.
- Produces: One authoritative syntax and fallback contract for existing path-only `必读` entries and future section/symbol/entity-level task cards.

- [ ] **Step 1: Confirm the old file-level wording still exists**

Run:

```powershell
rg -n '必读文件|必读：|必要事实源|选定任务后只读' `
  '开发管理/状态与建议维护规则.txt' `
  '开发管理/AI协作规则.txt' `
  '开发管理/DeepSeek工作提示词.txt'
```

Expected: `状态与建议维护规则.txt` still requires `必读文件`; the other two files still use the older unbounded wording. If the wording has changed, stop and update the exact replacements below.

- [ ] **Step 2: Replace the task-card field and define its exact syntax**

In `开发管理/状态与建议维护规则.txt`, replace the current task-card requirement:

```text
3. `当前任务队列.txt` 的近期任务必须包含 ID、优先级、主责、类型、状态、依赖、必读文件、完整预期修改路径、验证命令和完成条件。
4. Codex 任务完成后归档；外部 AI 业务提交进入待复审，由 `2` 处理。
5. 队列不保存长解释、历史争论或完整审核过程；长期任务留在分线 backlog。
```

with:

```text
3. `当前任务队列.txt` 的近期任务必须包含 ID、优先级、主责、类型、状态、依赖、必查范围、完整预期修改路径、验证命令和完成条件。
4. `必查范围` 优先使用 `文档路径#章节`、`代码路径::类型.符号`、`CSV/JSON路径::实体ID` 或 `资源目录::用途/资源ID`；路径自身已经是不可再分的短事实源时可以只写路径。
5. 既有任务卡的路径级 `必读` 默认要求先定位其中与任务相关的章节、符号或实体，不表示整份加载。只有任务卡明确写“完整文件”、无法保持语义边界、检索无结果或存在事实冲突时才读取全文；修改前仍须读取目标完整逻辑单元。
6. Codex 任务完成后归档；外部 AI 业务提交进入待复审，由 `2` 处理。
7. 队列不保存长解释、历史争论或完整审核过程；长期任务留在分线 backlog。
```

In the same file, replace:

```text
4. 执行前从队列取得 ID 并按任务卡读取事实源；执行后只运行任务卡要求的最小充分验证。
```

with:

```text
4. 执行前从队列取得 ID，按任务卡必查范围先定位、后读取；发现新依赖时补读，修改前读取目标完整逻辑单元。执行后只运行任务卡要求的最小充分验证。
```

- [ ] **Step 3: Route all executors to the same contract**

In `开发管理/AI协作规则.txt` 的“总原则”中，在状态维护规则条目之后增加：

```text
- 任务卡的 `必读` / `必查范围` 统一按 `开发管理/状态与建议维护规则.txt` 解释：先定位相关章节、符号、实体或用途，再读取当前步骤需要的原文；路径级条目不默认等于整份加载，修改前仍读取目标完整逻辑单元。
```

Replace通用步骤第 8 条：

```text
8. 命中审核、未审核、交接或复审路由时，按审核入口读取细则与相应事实源；普通执行任务直接读取任务卡和必要事实源。不得处理另一个 AI 主责的高风险任务，应写入交接请求。
```

with:

```text
8. 命中审核、未审核、交接或复审路由时，按审核入口读取细则与相应事实源；普通执行任务按任务卡必查范围先定位、后读取，发现新依赖时补读，修改前读取目标完整逻辑单元。不得处理另一个 AI 主责的高风险任务，应写入交接请求。
```

In `开发管理/DeepSeek工作提示词.txt`, replace the first paragraph under `## 任务路由`:

```text
纯 `1` 先读 `AGENTS.md` 与 `开发管理/当前任务队列.txt`；只有命中未审核、交接或复审路由时再读 `开发管理/审核入口.txt` 及相应事实源。纯 `2` 先读审核入口。选定任务后只读一个最相关任务卡和必要事实源，不默认读取全部任务卡。
```

with:

```text
纯 `1` 先读 `AGENTS.md` 与 `开发管理/当前任务队列.txt`；只有命中未审核、交接或复审路由时再读 `开发管理/审核入口.txt` 及相应事实源。纯 `2` 先读审核入口。选定任务后只读一个最相关任务卡，并按 `开发管理/状态与建议维护规则.txt` 的必查范围语义先定位、后读取；路径级 `必读` 不默认等于整份加载，发现新依赖时补读，修改前读取目标完整逻辑单元。
```

- [ ] **Step 4: Verify the contract is singular and complete**

Run:

```powershell
$stateRules = Get-Content -Raw -LiteralPath '开发管理/状态与建议维护规则.txt'
$aiRules = Get-Content -Raw -LiteralPath '开发管理/AI协作规则.txt'
$deepSeekRules = Get-Content -Raw -LiteralPath '开发管理/DeepSeek工作提示词.txt'

$requiredState = @(
  '文档路径#章节',
  '代码路径::类型.符号',
  'CSV/JSON路径::实体ID',
  '资源目录::用途/资源ID',
  '路径级 `必读` 默认要求先定位'
)
foreach ($literal in $requiredState) {
  if (-not $stateRules.Contains($literal)) { throw "Missing task-card contract: $literal" }
}

if (-not $aiRules.Contains('统一按 `开发管理/状态与建议维护规则.txt` 解释')) {
  throw 'AI collaboration rules do not route to the authoritative contract.'
}
if (-not $deepSeekRules.Contains('按 `开发管理/状态与建议维护规则.txt` 的必查范围语义先定位、后读取')) {
  throw 'DeepSeek route does not use the authoritative contract.'
}
```

Expected: exit zero; the syntax is defined only in the state-maintenance source, while the other executors reference it.

- [ ] **Step 5: Run focused checks and commit the routed contract**

Run:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-review-text.ps1 -Paths AGENTS.md,CLAUDE.md,开发管理
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-pending-whitespace.ps1 -ExpectedPaths '开发管理/状态与建议维护规则.txt|开发管理/AI协作规则.txt|开发管理/DeepSeek工作提示词.txt'
git add -- '开发管理/状态与建议维护规则.txt' '开发管理/AI协作规则.txt' '开发管理/DeepSeek工作提示词.txt'
git diff --cached --check
git diff --cached --name-status
git commit -m "docs: define bounded task context reads"
```

Expected: all checks exit zero; the staged set contains exactly the three rule files; the commit succeeds.

---

### Task 3: Replay five representative reading routes and record the one-time result

**Files:**
- Modify: `docs/superpowers/specs/2026-07-23-minimal-context-loading-design.md:after section 十`

**Interfaces:**
- Consumes: The root entries from Task 1 and the authoritative `必查范围` contract from Task 2.
- Produces: One committed, non-runtime validation record proving the selected reading ranges preserve required sources while reducing project text loaded into context.

- [ ] **Step 1: Run the five fixed context-size comparisons**

Run this exact PowerShell 7 block from the repository root:

```powershell
& {
function Get-TextChars([string]$Path) {
  ([System.IO.File]::ReadAllText((Resolve-Path $Path))).Length
}

function Get-SegmentChars([string]$Path, [int]$Start, [int]$End) {
  $lines = Get-Content -LiteralPath $Path
  $last = [Math]::Min($End, $lines.Count)
  if ($Start -gt $last) { throw "Invalid range $Path $Start-$End" }
  (($lines[($Start - 1)..($last - 1)] -join "`n")).Length
}

$cases = @(
  @{
    Name = '术法设计'
    Baseline = @(
      'docs/角色养成/术法设计规范.txt',
      'docs/角色养成/术法/术法设计.txt'
    )
    Segments = @(
      @('docs/角色养成/术法设计规范.txt', 11, 42),
      @('docs/角色养成/术法设计规范.txt', 65, 113),
      @('docs/角色养成/术法设计规范.txt', 130, 9999),
      @('docs/角色养成/术法/术法设计.txt', 13, 59)
    )
  },
  @{
    Name = '副本叙事'
    Baseline = @(
      'docs/剧情/剧情生产规范.txt',
      'docs/剧情/背景与重要NPC设计规范.txt',
      'docs/剧情/世界背景故事.txt',
      'docs/地图/关陇玄域.txt'
    )
    Segments = @(
      @('docs/剧情/剧情生产规范.txt', 7, 26),
      @('docs/剧情/剧情生产规范.txt', 171, 206),
      @('docs/剧情/剧情生产规范.txt', 259, 306),
      @('docs/剧情/剧情生产规范.txt', 498, 9999),
      @('docs/地图/关陇玄域.txt', 19, 62),
      @('docs/地图/关陇玄域.txt', 385, 462),
      @('docs/地图/关陇玄域.txt', 523, 615),
      @('docs/剧情/世界背景故事.txt', 64, 9999)
    )
  },
  @{
    Name = 'Unity返回上下文'
    Baseline = @(
      '开发管理/开发-技术经验.txt',
      '开发管理/设计-当前状态.txt',
      '开发管理/运行时所有者记录-TQ-060.txt',
      'src/Assets/Scripts/Adventure/AdventureSceneController.cs',
      'src/Assets/Scripts/Game/SceneFlowManager.cs',
      'src/Assets/Scripts/Map/ExplorationController.cs',
      'src/Assets/Tests/EditMode/TacticalGridModelTests.cs'
    )
    Segments = @(
      @('开发管理/开发-技术经验.txt', 48, 65),
      @('开发管理/设计-当前状态.txt', 31, 70),
      @('开发管理/运行时所有者记录-TQ-060.txt', 6, 33),
      @('开发管理/运行时所有者记录-TQ-060.txt', 59, 65),
      @('src/Assets/Scripts/Adventure/AdventureSceneController.cs', 55, 95),
      @('src/Assets/Scripts/Game/SceneFlowManager.cs', 80, 140),
      @('src/Assets/Scripts/Map/ExplorationController.cs', 820, 865),
      @('src/Assets/Tests/EditMode/TacticalGridModelTests.cs', 880, 930)
    )
  },
  @{
    Name = 'BattleSim目标选择'
    Baseline = @(
      '开发管理/开发-技术经验.txt',
      '开发管理/设计-当前状态.txt',
      '开发管理/2v2范围技能协同走位与团队AI参数事实源及决策输入.txt',
      'simulations/BattleSim/Combat.cs',
      'simulations/BattleSim/Character.cs',
      'simulations/BattleSim/GameData.cs',
      'simulations/BattleSim/BattleSimSelfTests.cs',
      'simulations/BattleSim/Program.cs'
    )
    Segments = @(
      @('开发管理/开发-技术经验.txt', 27, 42),
      @('开发管理/设计-当前状态.txt', 31, 70),
      @('开发管理/2v2范围技能协同走位与团队AI参数事实源及决策输入.txt', 1, 60),
      @('simulations/BattleSim/Combat.cs', 70, 100),
      @('simulations/BattleSim/Combat.cs', 427, 560),
      @('simulations/BattleSim/BattleSimSelfTests.cs', 980, 1060)
    )
  },
  @{
    Name = '苻渊无字立绘'
    Baseline = @(
      'assets/generated-character-art/README.md',
      'docs/剧情/世界背景故事.txt',
      'docs/剧情/重要NPC/苻渊.txt',
      'assets/generated-character-art/no-title/prompts.md'
    )
    Segments = @(
      @('assets/generated-character-art/README.md', 11, 9999),
      @('docs/剧情/重要NPC/苻渊.txt', 1, 20),
      @('assets/generated-character-art/no-title/prompts.md', 5, 41)
    )
  }
)

$rows = foreach ($case in $cases) {
  $baseline = ($case.Baseline | ForEach-Object {
    Get-TextChars $_
  } | Measure-Object -Sum).Sum

  $selected = ($case.Segments | ForEach-Object {
    Get-SegmentChars $_[0] $_[1] $_[2]
  } | Measure-Object -Sum).Sum

  [pscustomobject]@{
    Case = $case.Name
    BaselineChars = $baseline
    SelectedChars = $selected
    ReductionPct = [Math]::Round((1 - ($selected / $baseline)) * 100, 1)
  }
}

$rows | Format-Table -AutoSize

if (($rows | Where-Object { $_.SelectedChars -gt $_.BaselineChars }).Count -gt 0) {
  throw 'Selected context exceeded baseline.'
}
if (($rows | Where-Object { $_.ReductionPct -ge 50 }).Count -lt 4) {
  throw 'Fewer than four cases reduced context by at least 50%.'
}
}
```

Expected with the reviewed source versions:

```text
Case             BaselineChars SelectedChars ReductionPct
术法设计                  8906          5455         38.7
副本叙事                 31846          9698         69.5
Unity返回上下文         116062         13597         88.3
BattleSim目标选择       207719         20847         90.0
苻渊无字立绘             13268          4545         65.7
```

The command must exit zero. If the source files changed, record the fresh values but keep both gates: no case may grow, and at least four cases must reduce by 50%.

- [ ] **Step 2: Verify each selected range still covers the mandatory facts**

Use bounded reads for the exact ranges from Step 1 and confirm:

```text
术法设计：模板字段、核心设计原则、通用约束、新增检查清单、战斗术法模板。
副本叙事：事实源路由、副本叙事类型、玩法接口、信息边界、AI 流程、审核清单、关陇概述/独有机制/副本、游戏当代知识。
Unity 返回上下文：相关 Unity 经验、当前系统事实、运行时所有者、结算返回方法、调用点和直接测试。
BattleSim 目标选择：模拟器与 2v2 建模规则、当前系统事实、团队目标决策输入、目标选择/2v2 主循环和直接自测。
苻渊无字立绘：生成流程与验收、角色身份/当前定位、通用风格、无字约束和苻渊专用提示词。
```

Expected: no required category is missing. If a category is missing, expand only that case's selected range and rerun Step 1; do not solve it by restoring whole-file default reads.

- [ ] **Step 3: Append the one-time result to the approved design**

Append this section after `## 十、完成条件`, using the actual Step 1 values if they differ from the reviewed values:

```markdown
## 十一、实施验证结果

2026-07-23 按五类既有任务重放资料定位和必查范围，不重新生产业务内容，不重复运行输入未变化的领域验证。

| 场景 | 整文件基线字符 | 必查范围字符 | 降幅 |
|---|---:|---:|---:|
| 术法设计 | 8,906 | 5,455 | 38.7% |
| 副本叙事 | 31,846 | 9,698 | 69.5% |
| Unity 返回上下文 | 116,062 | 13,597 | 88.3% |
| BattleSim 目标选择 | 207,719 | 20,847 | 90.0% |
| 苻渊无字立绘 | 13,268 | 4,545 | 65.7% |

五项均低于原整文件基线，其中四项降幅超过 50%；所列必查范围覆盖领域硬约束、直接事实、修改目标与直接验证入口。本验证只作为实施验收证据，不建立持续上下文监控。
```

- [ ] **Step 4: Run the final relevant checks**

Run:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-review-text.ps1 -Paths AGENTS.md,CLAUDE.md,开发管理
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-pending-whitespace.ps1 -ExpectedPaths 'docs/superpowers/specs/2026-07-23-minimal-context-loading-design.md'
git diff --check
git log -2 --oneline
git status --short
```

Expected: checks exit zero; the previous two commits are the two implementation slices; only the design spec is modified among planned files. Unrelated user or automation changes may remain visible and must stay untouched.

- [ ] **Step 5: Commit the validation record**

Run:

```powershell
git add -- 'docs/superpowers/specs/2026-07-23-minimal-context-loading-design.md'
git diff --cached --check
git diff --cached --name-status
git commit -m "docs: record minimal context validation"
```

Expected: the staged set contains exactly the design spec; the commit succeeds.
