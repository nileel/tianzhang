# Fresh Decision Automation Trigger Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace decision-time original-session resume with a later hourly run that starts a fresh responsibility session from durable signed reply evidence.

**Architecture:** The Feishu bridge ends after persisting a validated signed reply. A fixed decision trigger, called only by the hourly controller, reads the current decision recovery and idempotently consumes inbox or processed evidence, acquires the single-writer lease, and calls the existing responsibility invoker with `Start + Recovery`. Interruption recovery remains the only path that uses `Resume + SessionId`.

**Tech Stack:** PowerShell 7, Node.js ESM and `node:test`, Codex CLI runner, JSON runtime state, Codex automation configuration.

---

## File map

- `tools/feishu-decision-bridge/src/inbox.mjs`: make processed decision evidence idempotently readable.
- `tools/feishu-decision-bridge/src/bridge.mjs`: stop scheduling a model relay after accepted card or text replies.
- `tools/feishu-decision-bridge/src/decision-trigger.mjs`: replace the old resume relay with the hourly-only fresh-session trigger.
- `tools/hourly-automation-lease.ps1`: migrate runtime to schema 3 and separate decision recovery from interruption session recovery.
- `tools/invoke-codex-responsibility.ps1`: accept decision input for `Start + Recovery`.
- `tools/feishu-decision-bridge/test/consume.test.mjs`, `decision-trigger.test.mjs`, and PowerShell test scripts: prove the new boundaries before implementation.
- `开发管理/自动工作流规则.txt`, `开发管理/自动工作流控制器提示词.txt`, `开发管理/自动工作流状态.txt`, and workflow contract checks: make the production routing contract match the code.
- `$CODEX_HOME/automations/tzg-hourly-controller/automation.toml`: update through the automation management API after repository checks pass.

### Task 1: Make processed replies idempotent

**Files:**
- Modify: `tools/feishu-decision-bridge/test/consume.test.mjs`
- Modify: `tools/feishu-decision-bridge/src/inbox.mjs`

- [ ] **Step 1: Change the existing second-consume assertion to require the same accepted result**

```js
const repeated = await consumeCurrentReply({
  stateRoot: root,
  config: makeConfig(root),
  pendingDecision: makePending(),
  now: NOW,
});
assert.deepEqual(repeated, result);
```

- [ ] **Step 2: Run the focused test and verify RED**

Run: `node --test test/consume.test.mjs` from `tools/feishu-decision-bridge`.

Expected: FAIL because the second consume currently returns `null`.

- [ ] **Step 3: Return matching processed evidence from `readProcessedEvidence`**

Track the deterministic accepted output while verifying processed envelopes:

```js
let accepted = null;
if (
  payload.decisionId === pending.decisionId
  && payload.providerMessageIdHash === pending.providerMessageIdHash
) {
  identities.add(payloadIdentity(payload));
  accepted ??= acceptedOutput(payload, envelope);
}
return { accepted, healthy, identities, nonces };
```

When no new inbox winner exists, `consumeCurrentReply` returns `consumed.accepted`.

- [ ] **Step 4: Run the focused test and verify GREEN**

Run: `node --test test/consume.test.mjs`

Expected: all consume tests pass.

- [ ] **Step 5: Commit**

```powershell
git add -- tools/feishu-decision-bridge/src/inbox.mjs tools/feishu-decision-bridge/test/consume.test.mjs
git commit -m "fix(automation): keep decision replies reusable"
```

### Task 2: Remove immediate bridge-triggered model work

**Files:**
- Modify: `tools/feishu-decision-bridge/test/callback.test.mjs`
- Modify: `tools/feishu-decision-bridge/test/message.test.mjs`
- Modify: `tools/feishu-decision-bridge/src/bridge.mjs`

- [ ] **Step 1: Replace callback relay assertions with “acceptance writes evidence only” assertions**

```js
let modelLaunches = 0;
const callback = makeCallback({
  // existing fixtures
  postAccept() { modelLaunches += 1; },
});
await callback({ fixture: true });
assert.equal(modelLaunches, 0);
```

Add the same assertion for accepted text replies.

- [ ] **Step 2: Run the focused test and verify RED**

Run: `node --test test/callback.test.mjs test/message.test.mjs`

Expected: FAIL because accepted replies currently call `postAccept`.

- [ ] **Step 3: Remove production post-accept wiring**

Delete `createPostAcceptRelay` import, `postAccept` parameters and calls, and the `startBridge` relay construction. Accepted callbacks return after the signed inbox write and optional user confirmation.

- [ ] **Step 4: Run the focused test and verify GREEN**

Run: `node --test test/callback.test.mjs test/message.test.mjs`

Expected: callback and message acceptance tests pass without any relay call.

- [ ] **Step 5: Commit**

```powershell
git add -- tools/feishu-decision-bridge/src/bridge.mjs tools/feishu-decision-bridge/test/callback.test.mjs tools/feishu-decision-bridge/test/message.test.mjs
git commit -m "fix(automation): stop immediate decision resume"
```

### Task 3: Separate decision recovery from session recovery

**Files:**
- Modify: `tools/test-hourly-automation-lease.ps1`
- Modify: `tools/hourly-automation-lease.ps1`

- [ ] **Step 1: Add schema 3 migration and recovery-shape tests**

Tests must assert:

```powershell
Assert-Equal $show.Json.state.schemaVersion 3 'Runtime schema version mismatch'
Assert-True ($null -eq $show.Json.state.PSObject.Properties['pendingResumes']) 'pendingResumes survived migration'
Assert-True ($null -eq $decision.Json.recovery.PSObject.Properties['resumeId']) 'decision recovery retained a session id'
Assert-Equal $interruption.Json.recovery.resumeId 'session-interrupted' 'interruption session was removed'
```

Also remove QueueResume/TakeResume behavior tests and assert those actions are rejected by parameter validation.

- [ ] **Step 2: Run the lease test and verify RED**

Run: `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-hourly-automation-lease.ps1`

Expected: FAIL because runtime is schema 2 and decision recovery still requires a session.

- [ ] **Step 3: Implement schema 3 and conditional recovery shapes**

`New-RuntimeState` omits `pendingResumes`. `Convert-RuntimeStateSchema` converts schema 1/2 to schema 3, drops decision `resumeKind`/`resumeId`, preserves interruption session fields, and drops pending replies. `SaveRecovery` saves only:

```powershell
[pscustomobject][ordered]@{
  trigger = 'decision'
  runId = $state.lease.runId
  taskId = $state.lease.taskId
  owner = $state.lease.owner
  repositoryRoot = $state.lease.repositoryRoot
  decisionId = $DecisionId
  decisionRequestPath = $normalizedRequestPath
  hasUncommittedChanges = $false
  changedPaths = @()
}
```

Remove `QueueResume` and `TakeResume`; keep `Acquire -ResumeRecovery` for exact recovery acquisition and interruption ownership.

- [ ] **Step 4: Run the lease test and verify GREEN**

Run: `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-hourly-automation-lease.ps1`

Expected: all lease tests pass.

- [ ] **Step 5: Commit**

```powershell
git add -- tools/hourly-automation-lease.ps1 tools/test-hourly-automation-lease.ps1
git commit -m "refactor(automation): separate decision recovery state"
```

### Task 4: Start a fresh session from the next hourly run

**Files:**
- Delete: `tools/feishu-decision-bridge/src/resume-trigger.mjs`
- Delete: `tools/feishu-decision-bridge/test/resume-trigger.test.mjs`
- Create: `tools/feishu-decision-bridge/src/decision-trigger.mjs`
- Create: `tools/feishu-decision-bridge/test/decision-trigger.test.mjs`
- Modify: `tools/test-invoke-codex-responsibility.ps1`
- Modify: `tools/invoke-codex-responsibility.ps1`

- [ ] **Step 1: Write fresh-trigger tests**

The Node tests inject `showState`, `consumeReply`, `acquireRecovery`, and `invokeResponsibility` dependencies and assert:

```js
assert.deepEqual(await runDecisionTrigger(noReplyFixture), { status: 'waiting_decision' });
assert.equal(acquireCalls.length, 0);
assert.equal(invokeCalls.length, 0);

assert.equal(completed.action, 'Start');
assert.equal(completed.route, 'Recovery');
assert.equal(completed.sessionId, undefined);
assert.equal(completed.decisionId, 'decision-one');
assert.equal(completed.reply, 'A');
```

Add an invoker test proving `Start + Recovery + DecisionId + stdin` is accepted and `Resume + DecisionId` is rejected.

- [ ] **Step 2: Run focused tests and verify RED**

Run:

```powershell
node --test test/decision-trigger.test.mjs
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-invoke-codex-responsibility.ps1
```

Expected: the Node test cannot import `decision-trigger.mjs`; the PowerShell test rejects `Start + Recovery` decision input.

- [ ] **Step 3: Implement the fixed fresh decision trigger**

`runDecisionTrigger`:

1. reads `Show`;
2. returns `waiting_decision` for a decision recovery with `NO_REPLY`;
3. calls `Acquire -ResumeRecovery` only after an accepted reply exists;
4. invokes the fixed responsibility boundary synchronously with `Action=Start`, `Route=Recovery`, verified model, exact decision id, and reply through stdin;
5. returns the fixed invoker’s one-line terminal JSON.

The production CLI is:

```text
node decision-trigger.mjs --state-root <absolute-path> --model <verified-model>
```

- [ ] **Step 4: Allow only fresh decision sessions in the invoker**

Decision input is valid only when:

```powershell
$Action -ceq 'Start' -and $Route -ceq 'Recovery'
```

The invoker still validates that runtime recovery, active lease, task, repository and `DecisionId` all match. The generated prompt contains `[TZG_DECISION_TRIGGER ...]`, the exact reply, and “new CLI-native responsibility session”; it contains no old SessionId.

- [ ] **Step 5: Run focused tests and verify GREEN**

Run the two commands from Step 2.

Expected: all decision-trigger and invoker tests pass.

- [ ] **Step 6: Commit**

```powershell
git add -- tools/feishu-decision-bridge/src/decision-trigger.mjs tools/feishu-decision-bridge/test/decision-trigger.test.mjs tools/feishu-decision-bridge/src/resume-trigger.mjs tools/feishu-decision-bridge/test/resume-trigger.test.mjs tools/invoke-codex-responsibility.ps1 tools/test-invoke-codex-responsibility.ps1
git commit -m "feat(automation): trigger fresh sessions for decisions"
```

### Task 5: Align contracts and production automation

**Files:**
- Modify: `开发管理/自动工作流规则.txt`
- Modify: `开发管理/自动工作流控制器提示词.txt`
- Modify: `开发管理/自动工作流状态.txt`
- Modify: `tools/check-automation-workflow.ps1`
- Modify: `tools/test-check-automation-workflow.ps1`
- Update through API: `$CODEX_HOME/automations/tzg-hourly-controller/automation.toml`

- [ ] **Step 1: Write failing contract fixtures**

The canonical fixture must require `decision-trigger.mjs`, schema 3 actions without QueueResume/TakeResume, and these controller tokens:

```text
decision recovery 有回复时只启动新的责任方 session
decision recovery 无回复时不得 Acquire
interruption recovery 才允许 Resume 原 session
```

- [ ] **Step 2: Run contract tests and verify RED**

Run: `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-check-automation-workflow.ps1`

Expected: FAIL because active rules and required component still describe original-session decision resume.

- [ ] **Step 3: Update rules, prompt, status and checker**

Document the exact fresh-trigger flow and remove decision QueueResume/TakeResume/original-session wording. Keep interruption recovery wording unchanged. Record that the current processed `DEC-20260724-ENVPROFILE = A` is recoverable by the next natural hourly run.

- [ ] **Step 4: Run contract tests and verify GREEN**

Run:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-check-automation-workflow.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-automation-workflow.ps1
```

Expected: both checks pass.

- [ ] **Step 5: Update the paused automation through the management API**

Preserve name, schedule, model, reasoning effort, project and notification fields. Replace only the prompt with the reviewed repository prompt and keep status `PAUSED` until Task 6 completes.

- [ ] **Step 6: Commit**

```powershell
git add -- 开发管理/自动工作流规则.txt 开发管理/自动工作流控制器提示词.txt 开发管理/自动工作流状态.txt tools/check-automation-workflow.ps1 tools/test-check-automation-workflow.ps1
git commit -m "docs(automation): route decisions to fresh sessions"
```

### Task 6: Full verification and activation

**Files:**
- Verify all modified paths
- Update through API: `tzg-hourly-controller`

- [ ] **Step 1: Run the complete direct test set**

```powershell
Push-Location tools/feishu-decision-bridge
npm test
Pop-Location
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-hourly-automation-lease.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-invoke-codex-responsibility.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-check-automation-workflow.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-automation-workflow.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-review-text.ps1 -Paths AGENTS.md,CLAUDE.md,开发管理
git diff --check
```

Expected: every command exits 0.

- [ ] **Step 2: Verify the live migration without starting business work**

Run `hourly-automation-lease.ps1 -Action Show` and assert:

- schema version is 3;
- lease is null;
- current recovery is decision `DEC-20260724-ENVPROFILE`;
- recovery contains no session id;
- processed evidence still yields option A through a read-only decision check.

- [ ] **Step 3: Check expected paths and commit final adjustments**

Run `tools/check-pending-whitespace.ps1` for this turn’s paths, stage only expected files, run `git diff --cached --check`, and commit any necessary final test/config alignment.

- [ ] **Step 4: Reactivate the hourly controller**

Use the automation management API with the full verified configuration and `status=ACTIVE`.

- [ ] **Step 5: Final status**

Report repository commits, verification commands, live runtime migration, automation status, and that the next natural hourly run will start a fresh session from the existing A reply.
