# Decision Resume Request Transition Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make every accepted Feishu decision request become a valid resume-consumption request at the same private path, reject invalid recovery pointers before runtime mutation, and safely resume `DEC-20260722-U25D01B` with the already received reply A.

**Architecture:** `send-decision.mjs` keeps its existing CLI and output contract, but after `PROVIDER_ACCEPTED` it atomically replaces the supplied send request with the exact `pendingDecision` snapshot required by `consume-reply.mjs`. `hourly-automation-lease.ps1 SaveRecovery` independently validates that same file and matching decision ID before writing runtime. No second request file, compatibility parser, runtime field, or retry mechanism is added.

**Tech Stack:** Node.js ESM and `node:test`; PowerShell 7; Git; existing Feishu decision bridge and hourly lease scripts.

---

### Task 1: Atomically transition an accepted send request

**Files:**
- Modify: `tools/feishu-decision-bridge/src/send-decision.mjs`
- Test: `tools/feishu-decision-bridge/test/send.test.mjs`

- [ ] **Step 1: Write the accepted-transition failing test**

In the existing `main enforces the request-file CLI contract and emits one sanitized JSON line` test, preserve the exact send request text, run the accepted case, and assert both the unchanged output whitelist and the transformed file:

```js
const accepted = await run(['--request-file', requestPath]);
assert.equal(accepted.code, 0);
const binding = JSON.parse(await readFile(join(root, 'pending-bindings.json'), 'utf8'));
assert.equal(binding.length, 1);
assert.deepEqual(Object.keys(assertOneJsonLine(accepted.stdout)).sort(), [
  'cardNonceHash',
  'intentKeyHash',
  'providerChatIdHash',
  'providerMessageIdHash',
  'result',
  'targetHash',
]);
assert.deepEqual(JSON.parse(await readFile(requestPath, 'utf8')), {
  pendingDecision: {
    decisionId: 'DEC-20260716-ABC123',
    allowedOptions: ['A', 'B', 'C'],
    allowCustomReply: true,
    createdAt: NOW.toISOString(),
    expiresAt: new Date(NOW.getTime() + 7 * 24 * 60 * 60 * 1000).toISOString(),
    cardNonceHash: binding[0].cardNonceHash,
    providerMessageIdHash: sha256('om_main'),
    providerChatIdHash: sha256('oc_main'),
  },
});
```

Before each later main invocation in the same test, restore the original send request with `writeFile(requestPath, sendRequestText, 'utf8')` so each case owns its input.

- [ ] **Step 2: Write the failed-transition preservation test**

Restore the send request, inject only the new request-transition writer as a failure, and prove that the original file remains byte-for-byte unchanged while the public result becomes outcome unknown:

```js
await writeFile(requestPath, sendRequestText, 'utf8');
const transitionFailed = await run(['--request-file', requestPath], {
  writeRecoveryRequest: async () => { throw new Error('request transition failed'); },
});
assert.equal(transitionFailed.code, 23);
assert.equal(await readFile(requestPath, 'utf8'), sendRequestText);
assert.deepEqual(assertOneJsonLine(transitionFailed.stdout), {
  result: 'PROVIDER_OUTCOME_UNKNOWN',
  targetHash: sha256('operator@example.invalid'),
  cardNonceHash: binding[0].cardNonceHash,
  intentKeyHash: hashSendIntentKey('feishu', 'DEC-20260716-ABC123', 1),
});
```

- [ ] **Step 3: Run the sender test and verify RED**

Run:

```powershell
node --test tools/feishu-decision-bridge/test/send.test.mjs
```

Expected: the accepted case fails because the request is still shaped as `attemptNumber + decision`, and the failure injection is unused.

- [ ] **Step 4: Implement the minimum atomic transition**

In `send-decision.mjs`, retain the current pending-binding writer and add one atomic request writer using the same private-file pattern:

```js
async function writeRecoveryRequest({ requestPath, decision, result, now }) {
  const temporaryPath = join(
    dirname(requestPath),
    `.${basename(requestPath)}.${randomUUID()}.tmp`,
  );
  const pendingDecision = {
    decisionId: decision.decisionId,
    allowedOptions: decision.options.map((option) => option.key),
    allowCustomReply: true,
    createdAt: now.toISOString(),
    expiresAt: new Date(now.getTime() + DECISION_TTL_MS).toISOString(),
    cardNonceHash: result.cardNonceHash,
    providerMessageIdHash: result.providerMessageIdHash,
    providerChatIdHash: result.providerChatIdHash,
  };
  try {
    await writeFile(temporaryPath, `${JSON.stringify({ pendingDecision })}\n`, {
      encoding: 'utf8',
      flag: 'wx',
      mode: 0o600,
    });
    await rename(temporaryPath, requestPath);
  } finally {
    await rm(temporaryPath, { force: true }).catch(() => {});
  }
}
```

Import `basename` and `dirname`, retain `requestPath` after argument parsing, add `dependencies.writeRecoveryRequest ?? writeRecoveryRequest`, and call it immediately after the existing pending-binding write:

```js
await writeBinding({ stateRoot: config.stateRoot, decision: request.decision, result: output, now });
await writeRequest({ requestPath, decision: request.decision, result: output, now });
```

Both writes remain inside the existing `PROVIDER_ACCEPTED` `try/catch`; any failure produces the existing `PROVIDER_OUTCOME_UNKNOWN` whitelist. Do not add fields to successful stdout.

- [ ] **Step 5: Run the sender test and verify GREEN**

Run:

```powershell
node --test tools/feishu-decision-bridge/test/send.test.mjs
```

Expected: all sender tests pass and the accepted output contains no new path field.

- [ ] **Step 6: Commit Task 1**

```powershell
git add -- tools/feishu-decision-bridge/src/send-decision.mjs tools/feishu-decision-bridge/test/send.test.mjs
git diff --cached --check
git commit -m "fix(automation): transition accepted decision request"
```

### Task 2: Reject invalid recovery request pointers before runtime mutation

**Files:**
- Modify: `tools/hourly-automation-lease.ps1`
- Test: `tools/test-hourly-automation-lease.ps1`

- [ ] **Step 1: Add a strict consume-request fixture helper**

Add this helper near the test assertions and use it immediately before every successful `SaveRecovery` call with the matching decision ID:

```powershell
function Write-ConsumeRequestFixture {
  param([string]$Path, [string]$DecisionId)

  $value = [ordered]@{
    pendingDecision = [ordered]@{
      decisionId = $DecisionId
      allowedOptions = @('A', 'B', 'C')
      allowCustomReply = $true
      createdAt = '2026-07-22T00:00:00.000Z'
      expiresAt = '2026-07-29T00:00:00.000Z'
      cardNonceHash = 'a' * 64
      providerMessageIdHash = 'b' * 64
      providerChatIdHash = 'c' * 64
    }
  }
  [IO.File]::WriteAllText(
    $Path,
    ($value | ConvertTo-Json -Depth 10 -Compress),
    [Text.UTF8Encoding]::new($false)
  )
}
```

- [ ] **Step 2: Write the send-shaped pointer failing test**

After acquiring a normal lease, write the observed bad shape and prove `SaveRecovery` returns `INVALID_ARGUMENT` without changing runtime bytes:

```powershell
[IO.File]::WriteAllText(
  $requestPath,
  '{"attemptNumber":1,"decision":{"decisionId":"decision-invalid-shape"}}',
  [Text.UTF8Encoding]::new($false)
)
Assert-RejectedWithoutStateChange -Action SaveRecovery -StatePath $statePath -ExpectedStatus 'INVALID_ARGUMENT' -Parameters @{
  StateRoot = $stateRoot
  RunId = $recoveryOwner.Json.runId
  DecisionId = 'decision-invalid-shape'
  DecisionRequestPath = $requestPath
  CodexThreadId = 'thread-invalid-shape'
}
```

- [ ] **Step 3: Run the lease test and verify RED**

Run:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-hourly-automation-lease.ps1
```

Expected: the send-shaped request is incorrectly accepted as `RECOVERY_SAVED`.

- [ ] **Step 4: Implement strict pre-mutation validation**

Add `Assert-DecisionConsumeRequest` before the action switch. It must enforce a maximum 64 KiB file, exact root key `pendingDecision`, exact eight inner keys, matching decision ID, ordered A/B/C options, Boolean `allowCustomReply`, exact UTC millisecond timestamps with `createdAt <= expiresAt`, and three lowercase SHA-256 hashes. Its final shape check is:

```powershell
function Assert-DecisionConsumeRequest {
  param([string]$Path, [string]$ExpectedDecisionId)

  if ((Get-Item -LiteralPath $Path).Length -gt 65536) {
    throw [ArgumentException]::new('DecisionRequestPath is not a valid consume request')
  }
  try {
    $root = Get-Content -Raw -LiteralPath $Path | ConvertFrom-Json -AsHashtable -Depth 20
  } catch {
    throw [ArgumentException]::new('DecisionRequestPath is not a valid consume request')
  }
  $required = @(
    'decisionId', 'allowedOptions', 'allowCustomReply', 'createdAt', 'expiresAt',
    'cardNonceHash', 'providerMessageIdHash', 'providerChatIdHash'
  )
  $pending = $root.pendingDecision
  $valid = $root -is [Collections.IDictionary] -and
    $root.Count -eq 1 -and $root.ContainsKey('pendingDecision') -and
    $pending -is [Collections.IDictionary] -and $pending.Count -eq $required.Count -and
    @($required | Where-Object { -not $pending.Contains($_) }).Count -eq 0 -and
    [string]$pending.decisionId -ceq $ExpectedDecisionId -and
    @($pending.allowedOptions).Count -eq 3 -and
    [string]$pending.allowedOptions[0] -ceq 'A' -and
    [string]$pending.allowedOptions[1] -ceq 'B' -and
    [string]$pending.allowedOptions[2] -ceq 'C' -and
    $pending.allowCustomReply -is [bool] -and
    [string]$pending.createdAt -match '^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z$' -and
    [string]$pending.expiresAt -match '^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z$' -and
    [string]$pending.cardNonceHash -match '^[0-9a-f]{64}$' -and
    [string]$pending.providerMessageIdHash -match '^[0-9a-f]{64}$' -and
    [string]$pending.providerChatIdHash -match '^[0-9a-f]{64}$'
  if (-not $valid) {
    throw [ArgumentException]::new('DecisionRequestPath is not a valid consume request')
  }
  $created = [DateTimeOffset]::ParseExact($pending.createdAt, 'yyyy-MM-ddTHH:mm:ss.fffZ', [Globalization.CultureInfo]::InvariantCulture)
  $expires = [DateTimeOffset]::ParseExact($pending.expiresAt, 'yyyy-MM-ddTHH:mm:ss.fffZ', [Globalization.CultureInfo]::InvariantCulture)
  if ($created -gt $expires) {
    throw [ArgumentException]::new('DecisionRequestPath is not a valid consume request')
  }
}
```

Call it in `SaveRecovery` immediately after `Resolve-ApprovedPrivateFile` and before computing the resume kind or changing `$state`:

```powershell
Assert-DecisionConsumeRequest -Path $normalizedRequestPath -ExpectedDecisionId $DecisionId
```

- [ ] **Step 5: Run the lease test and verify GREEN**

Run:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-hourly-automation-lease.ps1
```

Expected: `test-hourly-automation-lease: OK`.

- [ ] **Step 6: Commit Task 2**

```powershell
git add -- tools/hourly-automation-lease.ps1 tools/test-hourly-automation-lease.ps1
git diff --cached --check
git commit -m "fix(automation): validate resume request before recovery"
```

### Task 3: Publish the single-path contract to controller responsibilities

**Files:**
- Modify: `开发管理/自动工作流规则.txt`
- Modify: `开发管理/自动工作流控制器提示词.txt`
- Modify: `tools/check-automation-workflow.ps1`
- Test: `tools/test-check-automation-workflow.ps1`

- [ ] **Step 1: Add failing canonical-contract assertions**

Add these required literal phrases to both the prompt and short-rule assertion arrays, and to the fixture-removal loop:

```powershell
'PROVIDER_ACCEPTED'
'原发送请求路径已原子转换为消费请求'
'SaveRecovery'
```

Update the canonical prompt and rule fixtures with the same sentence used by production files.

- [ ] **Step 2: Run the workflow checker test and verify RED**

Run:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-check-automation-workflow.ps1
```

Expected: failure because production prompt/rules do not yet contain the single-path contract.

- [ ] **Step 3: Add the minimum contract sentence**

Append this sentence to decision handling in both production sources:

```text
只有 `send-decision.mjs` 返回 `PROVIDER_ACCEPTED` 后，原发送请求路径才已原子转换为消费请求；责任方必须把该同一路径作为 `DecisionRequestPath` 交给 `SaveRecovery`，其他发送结果不得保存 recovery。
```

Do not add a second filename, fallback parsing rule, retry count, or runtime state.

- [ ] **Step 4: Run focused text checks and verify GREEN**

Run:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-check-automation-workflow.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-review-text.ps1 -Paths AGENTS.md,CLAUDE.md,开发管理
```

Expected: both commands exit 0.

- [ ] **Step 5: Commit Task 3**

```powershell
git add -- tools/check-automation-workflow.ps1 tools/test-check-automation-workflow.ps1 开发管理/自动工作流规则.txt 开发管理/自动工作流控制器提示词.txt
git diff --cached --check
git commit -m "docs(automation): require converted recovery request"
```

### Task 4: Deploy the fix and resume the preserved reply

**Files:**
- No source edits; operate on the existing branch, main worktree, and private runtime.

- [ ] **Step 1: Run the focused implementation regression**

Run from the implementation worktree:

```powershell
node --test tools/feishu-decision-bridge/test/send.test.mjs
node --test tools/feishu-decision-bridge/test/resume-trigger.test.mjs
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-hourly-automation-lease.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-check-automation-workflow.ps1
```

Expected: all four focused commands exit 0. The known unrelated `message.test.mjs` assertion drift is not part of this slice and remains separately documented.

- [ ] **Step 2: Merge into `master` without disturbing recovery**

Confirm `lease=null`, the existing recovery is `DEC-20260722-U25D01B`, and the main worktree is clean. Then run:

```powershell
git pull --ff-only
git merge codex/automation-commit-briefing
```

Expected: merge succeeds; recovery and inbox bytes are unchanged.

- [ ] **Step 3: Convert the preserved request through the accepted send intent**

First confirm the matching send-intent file is already `ACCEPTED`. Then rerun the existing request:

```powershell
node tools/feishu-decision-bridge/src/send-decision.mjs --request-file 'C:\Users\WINDOWS\.codex\automation-state\tzg-feishu-decision-bridge\requests\DEC-20260722-U25D01B.json'
```

Expected: exactly one sanitized `PROVIDER_ACCEPTED` line; the send-intent evidence remains the same accepted intent; the request file now has only `pendingDecision` and decision ID `DEC-20260722-U25D01B`.

- [ ] **Step 4: Resume the original session through the existing relay**

Run:

```powershell
node tools/feishu-decision-bridge/src/resume-trigger.mjs --queue --decision-id DEC-20260722-U25D01B --reply-path 'C:\Users\WINDOWS\.codex\automation-state\tzg-feishu-decision-bridge\inbox\05def967fd7a777bce6d1509ce2191d97081b0fecb7e725216670b2411ee9e18.json' --state-root 'C:\Users\WINDOWS\.codex\automation-state\tzg-hourly-controller-runtime'
```

Expected: exit 0. Do not start another relay while the original session is active.

- [ ] **Step 5: Verify the real terminal state**

Poll only the existing runtime and session until the responsibility terminates. Require all of these facts:

```text
inbox reply absent
processed reply present
original session file modified after the resume dispatch
lease = null
recovery = null
pendingResumes = []
main worktree status is clean or contains only the original responsibility's authorized in-progress paths while it is still running
```

If the responsibility reports a new decision or another root cause, stop rather than clearing state manually.

- [ ] **Step 6: Return to the briefing rollout**

After recovery closes, continue Task 3 of `docs/superpowers/plans/2026-07-22-automation-commit-briefing-implementation.md`: update the hourly canonical prompt and daily briefing automation through the automation management capability, then run `tools/check-automation-workflow.ps1 -RequireActive`.
