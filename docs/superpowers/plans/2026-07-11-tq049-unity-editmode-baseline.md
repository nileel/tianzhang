# TQ-049 Unity EditMode Baseline Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 修复 `SceneFlowManagerPreparesAdventureAndReturnContextsWithoutSceneLoad` 的 EditMode 空引用，并保持现有 47 个测试全部执行且通过。

**Architecture:** 保留 `SceneFlowManager.Awake → EnsureSession` 作为 Play Mode 运行时所有者；EditMode 测试不依赖未承诺执行的 `Awake` 生命周期，而与同文件其他测试一致，显式创建 `GameSession` 夹具。只修改测试夹具，不降低断言和测试数量。

**Tech Stack:** Unity 6.0.3 / NUnit / Unity Test Framework / C#。

---

### Task 1: Correct the EditMode fixture lifecycle assumption

**Files:**
- Modify: `src/Assets/Tests/EditMode/SceneArchitectureEditorTests.cs:202`
- Test: `src/Assets/Tests/EditMode/SceneArchitectureEditorTests.cs:202`

- [x] **Step 1: Verify the existing regression test is RED**

Run the filtered Unity EditMode test and require one failed test with `NullReferenceException` at line 210 where `GameSession.Instance` is dereferenced.

- [x] **Step 2: Write the minimal fixture fix**

Create `GameSessionTest` beside `SceneFlowManagerTest`, add `GameSession` explicitly, use that returned component as `session`, and destroy the fixture in `finally`.

- [x] **Step 3: Verify the targeted test is GREEN**

Run the same filtered Unity command and require `testcasecount=1`, `passed=1`, `failed=0` in the result XML.

- [x] **Step 4: Verify the complete baseline**

Run `dotnet build src/Assembly-CSharp.csproj`, `dotnet build src/TianZhang.EditModeTests.csproj`, and all Unity EditMode tests. Require 47/47 passed and preserve the result XML outside Unity's transient `Temp` directory.

- [x] **Step 5: Close task state and commit**

Record the root cause and verification evidence, update the queue and automation state, run `git diff --check`, stage only TQ-049 files, and commit with a TQ-049 message.
