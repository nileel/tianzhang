# Data-chain Error Severity Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make `tools/check-data-chain.ps1` fail closed for unapproved semantic data-chain defects and allow only exact, documented warning waivers.

**Architecture:** The checker accepts an optional fixture root so tests can build isolated data trees. It validates fixed CSV schemas, row completeness, docs/CSV/asset coverage, asset `contentScope` serialization, deleted-realm activation, and precise waiver records. It emits stable `ruleId` diagnostics and exits non-zero for every error or unapproved warning.

**Tech Stack:** PowerShell 7, repository CSV/YAML assets, temporary fixture directories.

---

### Task 1: Lock the checker contract with fixtures

**Files:**

- Create: `tools/tests/check-data-chain-tests.ps1`
- Test: `tools/check-data-chain.ps1`

- [x] **Step 1: Add a self-contained fixture runner**

Create a temporary project tree with one valid row and matching asset for each of GongFa, Spells, and Skills. The runner invokes the checker with `-ProjectRoot`, captures stdout and exit code, and deletes the temporary tree in `finally`.

- [x] **Step 2: Add failing contract cases**

Assert that each fixture exits non-zero and emits its rule ID: a docs/CSV count mismatch, missing required field, missing serialized `contentScope`, active `realm_lianxu`, and an unknown unwaived warning.

- [x] **Step 3: Run the test before implementation**

Run: `powershell -ExecutionPolicy Bypass -File tools/tests/check-data-chain-tests.ps1`

Expected: failure because the current checker does not accept `-ProjectRoot` and does not enforce semantic error severities.

### Task 2: Implement the fail-closed checker and precise waiver format

**Files:**

- Modify: `tools/check-data-chain.ps1`
- Create: `tools/data-chain-warning-waivers.json`

- [x] **Step 1: Add root and waiver loading**

Use `-ProjectRoot` only when it resolves to a directory. Read a JSON array whose entries contain exactly `ruleId`, `subject`, `reason`, `owner`, and `removalCondition`; reject missing fields and wildcard-like subjects.

- [x] **Step 2: Validate schema and semantic invariants**

For all three core CSV files, reject duplicate/unknown/missing headers, short rows, empty required cells, invalid scopes, docs/CSV count differences, missing/extra assets, absent or mismatched serialized asset scopes, `realm_lianshen` without a language key, and active deleted `realm_lianxu` content.

- [x] **Step 3: Gate warnings through exact waivers**

Only a waiver with exact equality on `ruleId` and subject may downgrade a warning. An entry cannot waive a future content ID, a whole category, or a prefix.

### Task 3: Verify all paths and document the task outcome

**Files:**

- Modify: `开发管理/当前任务队列.txt`
- Create: `开发管理/任务归档/2026-07-11-TQ-056-数据检查器错误分级归档.txt`

- [x] **Step 1: Run the fixture suite**

Run: `powershell -ExecutionPolicy Bypass -File tools/tests/check-data-chain-tests.ps1`

Expected: every positive and negative fixture reports its asserted outcome.

- [x] **Step 2: Run the production checker**

Run: `powershell -ExecutionPolicy Bypass -File tools/check-data-chain.ps1`

Expected: non-zero with the currently unresolved semantic defects, leaving their cleanup for TQ-057/TQ-058.

- [x] **Step 3: Run repository checks**

Run: `powershell -ExecutionPolicy Bypass -File tools/check-review-text.ps1 -Paths 开发管理,tools,src/Assets/DataConfig,src/Assets/Scripts`; `git diff --check`.

Expected: both commands exit zero.
