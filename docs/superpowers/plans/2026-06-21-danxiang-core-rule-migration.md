# Danxiang Core Rule Migration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` (recommended) or `superpowers:executing-plans` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将已审核的丹相设计与 17 分支源/化/界位格体系统一，迁入金丹、术法槽位、功法、术法和神通的正式规则；不以已废止的丹籍层级决定能力数量。

**Architecture:** 金丹能力总称为丹相，由具体源/化/界席位及其占据状态、丹名、丹性、紫府神通共同形成。源/化/界矩阵决定世界变量、边界与代价；丹枢只是在这些约束内调节媒介、对象偏向、代价分配、丹域景象、战斗表现和宗门叙事的个性化接口。丹相主枢是紫府神通在该接口中的金丹招牌表达，暂寄状态不生成主枢。

**Tech Stack:** UTF-8 TXT/Markdown 设定文档、PowerShell、ripgrep、`tools/check-review-text.ps1`。

---

## File Structure

- `docs/superpowers/specs/2026-06-19-jindan-danxiang-design.md`: 将旧丹籍型丹相稿改为与源化界事实源兼容的规格。
- `docs/基础设定/修行境界.txt`、`docs/基础设定/境界特性.txt`: 金丹入口和成丹规则的正式定义。
- `docs/角色养成/术法槽位设计.txt`: 槽位规则与 Unity 字段契约。
- `docs/角色养成/{功法,术法,神通}设计规范.txt` 与对应模板: 新内容生产的约束与字段。
- `开发管理/当前任务队列.txt`: 记录 TQ-015B-1 的待复审状态和 TQ-015C 的字段依赖。

### Task 1: Reconcile the Danxiang Specification

**Files:**
- Modify: `docs/superpowers/specs/2026-06-19-jindan-danxiang-design.md`

- [x] **Step 1: Replace superseded inputs and terminology**
  - Replace `丹籍` with `目标源/化/界席位 + 占据状态`.
  - Rename every current-rule `根本神通` reference to `紫府神通`.
  - Define `丹相 = 席位效果 + 占据状态 + 丹名 + 丹性 + 紫府神通`.

- [x] **Step 2: Resolve the danxiang/danshu boundary**
  - Define `丹枢` exclusively as the matrix-permitted individualized interface.
  - Define `丹相主枢` as the combat-signature expression selected from compatible purple-mansion divine abilities.
  - Define remaining compatible abilities as `辅枢`; do not give a fixed count before TQ-015C.
  - State that `暂寄` has no `丹相主枢`, while retaining an incomplete Danxiang and auxiliary traces.

- [x] **Step 3: Replace legacy outputs**
  - Replace mandatory `本命神通/本命法宝` mechanics with `丹相主枢/丹器显化`.
  - Keep the old terms only as UI or narrative aliases, never as system fields.

- [x] **Step 4: Verify the standalone specification**
  - Run: `rg -n "正籍|辅籍|敕籍|寄丹|根本神通" docs/superpowers/specs/2026-06-19-jindan-danxiang-design.md`
  - Expected: only historical-migration wording, if any; no active rule.

### Task 2: Migrate Core Realm Rules

**Files:**
- Modify: `docs/基础设定/修行境界.txt`
- Modify: `docs/基础设定/境界特性.txt`

- [x] **Step 1: Mark both documents as modified/unreviewed**
  - Replace the prior review header with `⚠️ 已修改/未审核` and name this migration's scope.

- [x] **Step 2: Replace Purple Mansion progression terms**
  - Rename `根本神通` to `紫府神通`.
  - Retain its binding to Purple Mansion positions and its role as a prerequisite for Danxiang formation.
  - Remove fixed four/five ability thresholds from current rules; TQ-015C owns numeric qualification and simulation evidence.

- [x] **Step 3: Replace Golden Core ability sections**
  - Describe the four Danxiang inputs and state that the matrix is the sole source for seat effects.
  - Replace the current `本命神通` section with `丹相、丹枢与丹相主枢`.
  - Replace the current `本命法宝` section with optional `丹器显化`.
  - State that `暂寄` generates no Danxiang main pivot and that all pivot counts remain data-task controlled.

- [x] **Step 4: Verify core terminology**
  - Run: `rg -n "正籍|辅籍|敕籍|寄丹|根本神通|丹籍" docs/基础设定/修行境界.txt docs/基础设定/境界特性.txt`
  - Expected: no active system rules; historical explanation may only say that old terminology is abolished.

### Task 3: Migrate Slot Rules and Data Contract

**Files:**
- Modify: `docs/角色养成/术法槽位设计.txt`

- [x] **Step 1: Replace Danji-driven slot tables**
  - Keep the base and Purple Mansion bonuses.
  - Replace all `丹籍` table rows with a target-seat/occupation-state contract.
  - Do not assign concrete slot bonuses for source/chemistry/world seats before TQ-015C.

- [x] **Step 2: Replace innate-divine-power rules**
  - Use `紫府神通 -> 丹相主枢/辅枢`.
  - Make main pivots occupy skill slots only when their finalized implementation declares it; reserve no global count.
  - Preserve the Taidaoist talisman exception as an expression of Danxiang, not a parallel universal system.

- [x] **Step 3: Update the Unity field contract without code changes**
  - Deprecate `danJiType` in favor of `targetPosition`, `positionOccupationState`, `danXiangId`, `danPivotRole`, `mansionBindings`, and optional `danArtifactForm`.
  - Mark field serialization and slot arithmetic as TQ-015C/TQ-013C work, not completed implementation.

- [x] **Step 4: Verify the slot document**
  - Run: `rg -n "正籍|辅籍|敕籍|寄丹|danJiType|根本神通" docs/角色养成/术法槽位设计.txt`
  - Expected: no active rules; legacy field appears only in the explicit compatibility mapping.

### Task 4: Migrate Content-Production Specifications and Templates

**Files:**
- Modify: `docs/角色养成/功法设计规范.txt`
- Modify: `docs/角色养成/术法设计规范.txt`
- Modify: `docs/角色养成/神通设计规范.txt`
- Modify: `docs/角色养成/功法/功法设计.txt`
- Modify: `docs/角色养成/术法/术法设计.txt`
- Modify: `docs/角色养成/神通/神通设计.txt`

- [x] **Step 1: Migrate purple-mansion terminology**
  - Rename content-source `根本神通` to `紫府神通`.
  - Require Purple Mansion binding for every such ability.

- [x] **Step 2: Replace Danji fields**
  - Replace `丹籍组合`/`丹籍适配` with `目标源化界席位` and `丹相适配`.
  - Require a matrix reference, occupancy-state compatibility, Danxiang input contribution, and Danshu interface boundary.

- [x] **Step 3: Replace innate-power output fields**
  - Replace `本命升格` as a direct destination with `丹相转化`.
  - Require the candidate's possible `丹相主枢` or `辅枢` role; do not require every spell to become a main pivot.
  - Treat `丹器显化` as optional and non-exclusive to physical items.

- [x] **Step 4: Delete obsolete high-realm rules in touched templates**
  - Remove current `炼虚` requirements and rules; high-tier NPC references may use `元婴/化神` only.

- [x] **Step 5: Verify all content-production documents**
  - Run: `rg -n "正籍|辅籍|敕籍|寄丹|根本神通|丹籍|炼虚" docs/角色养成/功法设计规范.txt docs/角色养成/术法设计规范.txt docs/角色养成/神通设计规范.txt docs/角色养成/功法/功法设计.txt docs/角色养成/术法/术法设计.txt docs/角色养成/神通/神通设计.txt`
  - Expected: no active design rule uses the removed terms.

### Task 5: Record Handoff State and Validate the Text Set

**Files:**
- Modify: `开发管理/当前任务队列.txt`

- [x] **Step 1: Update TQ-015B-1 state**
  - Record the scope as Danxiang/seat-interface core-rule migration.
  - Set it to `待 Codex / ChatGPT5.5 复审`; do not mark it audited.
  - State that TQ-015C remains responsible for numerical thresholds and runtime model implementation.

- [x] **Step 2: Run textual validation**
  - Run: `powershell -ExecutionPolicy Bypass -File tools/check-review-text.ps1 -Paths docs/基础设定,docs/角色养成,docs/superpowers/specs/2026-06-19-jindan-danxiang-design.md,docs/superpowers/plans/2026-06-21-danxiang-core-rule-migration.md,开发管理/当前任务队列.txt`
  - Run: `git diff --check`
  - Expected: no malformed text, control characters, or whitespace errors.

- [x] **Step 3: Commit the migration**
  - Stage only the eleven documents in Tasks 1-5.
  - Commit message: `docs: migrate danxiang core rules`.

## Plan Self-Review

- Spec coverage: Tasks 1-4 cover terminology unification, core docs, slot rules, content rules, and templates; Task 5 records handoff and validates all edited text.
- Scope: BattleSim, Unity, CSV, and numeric thresholds are explicitly deferred to TQ-015C/TQ-013C; this prevents undocumented mechanics from becoming runtime facts.
- Ambiguity resolved: `丹枢` is only the allowed individualization interface, not a second meaning for a Purple Mansion ability node.
