# 飞书决策短按钮与自定义回复实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 让飞书决策卡在移动端使用完整可见的 A/B/C 短按钮，并让已配对用户既可在卡片内输入自定义方案，也可发送带决定编号的严格格式文字来解决当前决定。

**Architecture:** Node 桥接层继续负责飞书长连接、身份/会话绑定、卡片与消息事件解析、HMAC 信封和私有收件箱；PowerShell 状态机升级到 schema v8，并以互斥的 option/custom resolution 持久化首个有效回复。现有 v7 回调兼容、发送器单行协议、私有 ACL 和单实例问题先作为部署前置修复，之后再增加自定义回复能力。

**Tech Stack:** PowerShell 7、Node.js `>=20`、`@larksuiteoapi/node-sdk@1.71.1`、Node `node:test`、Windows ACL、Windows 任务计划程序、飞书互动卡片与 `im.message.receive_v1` 长连接事件。

## Global Constraints

- 批准规格为 `docs/superpowers/specs/2026-07-16-feishu-custom-decision-replies-design.md`；任何实现偏离必须先修订规格。
- 所有 PowerShell 子进程使用 `pwsh -NoProfile -ExecutionPolicy Bypass -File ...`，不得调用 Windows PowerShell 5.1。
- 每个代码切片先写或补充直接失败测试，确认红灯原因，再写最小实现并运行直接相关测试。
- A/B/C 按钮文案固定为 `选择 A`、`选择 B`、`选择 C`；完整选项说明只出现在卡片正文。
- 卡片必须展示可复制格式 `DEC-编号：自定义 <你的方案>`。
- 自定义内容规范化 CRLF/CR 为 LF、去除首尾空白、保留内部换行，最多 1000 个 Unicode code point。
- 普通文字必须包含完整决定编号和固定 `自定义` 前缀；不搜索聊天历史，不把普通聊天交给模型判断。
- 只有已配对操作人、预期租户、原业务卡会话、当前未过期决定可以解决决定。
- 首个有效回复胜出；相同事件幂等，后续冲突事件进入 quarantine，不覆盖 resolution。
- App Secret、原始 Open ID、原始 chat ID、原始 provider message/event ID 和原始飞书事件不得进入 Git、项目状态、健康文件或日志。
- 自定义原文只进入私有 HMAC inbox、控制器私有状态和用户可见确认；公开状态只写类型、长度或摘要。
- 当前工作区已有的 `.codex-remote-attachments/` 与两份金丹规格文件属于用户，任何任务都不得暂存、删除或改写。
- 提交前先运行 `tools/check-pending-whitespace.ps1` 覆盖未跟踪文件，再 `git add`，最后运行 `git diff --cached --check`。

## File Map

### 新增文件

```text
tools/private-path-acl.ps1
tools/test-private-path-acl.ps1
tools/feishu-decision-bridge/src/instance-lock.mjs
tools/feishu-decision-bridge/src/custom-reply.mjs
tools/feishu-decision-bridge/src/message-core.mjs
tools/feishu-decision-bridge/src/message-runtime.mjs
tools/feishu-decision-bridge/test/custom-reply.test.mjs
tools/feishu-decision-bridge/test/message.test.mjs
```

### 修改文件

```text
tools/automation-controller.ps1
tools/automation-controller-state.ps1
tools/test-automation-controller.ps1
tools/test-automation-controller-state.ps1
tools/setup-feishu-decision-channel.ps1
tools/test-setup-feishu-decision-channel.ps1
tools/install-feishu-decision-bridge.ps1
tools/test-install-feishu-decision-bridge.ps1
tools/start-feishu-decision-bridge.ps1
tools/feishu-decision-bridge/src/bridge.mjs
tools/feishu-decision-bridge/src/callback-core.mjs
tools/feishu-decision-bridge/src/card.mjs
tools/feishu-decision-bridge/src/consume-reply.mjs
tools/feishu-decision-bridge/src/inbox.mjs
tools/feishu-decision-bridge/src/send-core.mjs
tools/feishu-decision-bridge/src/send-decision.mjs
tools/feishu-decision-bridge/src/send-runtime.mjs
tools/feishu-decision-bridge/test/callback.test.mjs
tools/feishu-decision-bridge/test/consume.test.mjs
tools/feishu-decision-bridge/test/core.test.mjs
tools/feishu-decision-bridge/test/send.test.mjs
开发管理/自动工作流规则.txt
开发管理/自动工作流控制器提示词.txt
开发管理/自动工作流状态.txt
docs/superpowers/plans/2026-07-15-feishu-decision-channel-implementation.md
```

---

## Task 1: 收口现有 v7 回调、发送器与私有 ACL 修复

**Files:**

- Create: `tools/private-path-acl.ps1`
- Create: `tools/test-private-path-acl.ps1`
- Modify: `tools/automation-controller.ps1`
- Modify: `tools/setup-feishu-decision-channel.ps1`
- Modify: `tools/feishu-decision-bridge/src/bridge.mjs`
- Modify: `tools/feishu-decision-bridge/src/callback-core.mjs`
- Modify: `tools/feishu-decision-bridge/src/send-runtime.mjs`
- Test: `tools/feishu-decision-bridge/test/callback.test.mjs`
- Test: `tools/feishu-decision-bridge/test/send.test.mjs`
- Test: `tools/test-private-path-acl.ps1`
- Test: `tools/test-automation-controller.ps1`
- Test: `tools/test-setup-feishu-decision-channel.ps1`

**Interfaces:**

- Produces: `Set-PrivatePathAcl -Path <absolute> [-Directory]` and `Assert-PrivatePathAcl -Path <absolute> [-Directory]` using a fresh DACL-only security descriptor.
- Preserves: sender CLI emits exactly one sanitized JSON line; card callback accepts official SDK-flattened schema 2 events.

- [ ] **Step 1: Confirm the already-written callback and sender regressions are green**

Run:

```powershell
Push-Location tools/feishu-decision-bridge
node --test test/callback.test.mjs test/send.test.mjs
Pop-Location
```

Expected: all callback/send tests pass, including SDK flattened optional fields, safe rejection diagnostics, and silent SDK client construction.

- [ ] **Step 2: Write the failing ACL helper test**

Create `tools/test-private-path-acl.ps1` with a unique `%TEMP%` directory. The test must fail because `tools/private-path-acl.ps1` does not exist, then after import must assert:

```powershell
#requires -Version 7.0
$helper = Join-Path $PSScriptRoot 'private-path-acl.ps1'
if (-not (Test-Path -LiteralPath $helper -PathType Leaf)) { throw 'private-path-acl.ps1 is missing' }
. $helper
$root = Join-Path ([IO.Path]::GetTempPath()) ('tzg-private-acl-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root | Out-Null
$file = Join-Path $root 'private.json'
[IO.File]::WriteAllText($file, '{}', [Text.UTF8Encoding]::new($false))
Set-PrivatePathAcl -Path $root -Directory
Set-PrivatePathAcl -Path $file
Assert-PrivatePathAcl -Path $root -Directory
Assert-PrivatePathAcl -Path $file
if ((Get-Content -Raw -LiteralPath $file) -cne '{}') { throw 'ACL write changed file content' }
'test-private-path-acl: OK'
```

The `finally` block must only recursively remove `$root` after proving its full path starts with the current temp directory prefix.

- [ ] **Step 3: Run the ACL test and verify the red reason**

Run:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-private-path-acl.ps1
```

Expected: non-zero with `private-path-acl.ps1 is missing`.

- [ ] **Step 4: Implement a fresh DACL-only descriptor**

Create `tools/private-path-acl.ps1`. `Set-PrivatePathAcl` must not call `Get-Acl`; construct a new security descriptor so `Set-Acl` cannot attempt to write an inherited SACL requiring `SeSecurityPrivilege`:

```powershell
#requires -Version 7.0
function Get-PrivateAclSids {
  @(
    [Security.Principal.WindowsIdentity]::GetCurrent().User,
    [Security.Principal.SecurityIdentifier]::new('S-1-5-18')
  )
}

function Set-PrivatePathAcl {
  param([Parameter(Mandatory)][string]$Path, [switch]$Directory)
  $security = if ($Directory) {
    [Security.AccessControl.DirectorySecurity]::new()
  } else {
    [Security.AccessControl.FileSecurity]::new()
  }
  $security.SetAccessRuleProtection($true, $false)
  $inheritance = if ($Directory) {
    [Security.AccessControl.InheritanceFlags]'ContainerInherit, ObjectInherit'
  } else {
    [Security.AccessControl.InheritanceFlags]::None
  }
  foreach ($sid in Get-PrivateAclSids) {
    $security.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new(
      $sid,
      [Security.AccessControl.FileSystemRights]::FullControl,
      $inheritance,
      [Security.AccessControl.PropagationFlags]::None,
      [Security.AccessControl.AccessControlType]::Allow
    )) | Out-Null
  }
  Set-Acl -LiteralPath $Path -AclObject $security
}
```

`Assert-PrivatePathAcl` must require a protected DACL with exactly two non-inherited Allow FullControl rules for current user and SYSTEM.

- [ ] **Step 5: Replace duplicated ACL functions with the helper**

At the top of both PowerShell entry points, after parameters and before first ACL use, add:

```powershell
. (Join-Path $PSScriptRoot 'private-path-acl.ps1')
```

Remove the local `Get-PrivateAclSids`, `Set-PrivatePathAcl`, and equivalent private file assertion definitions. Keep their call sites unchanged except rename assertions to `Assert-PrivatePathAcl`.

- [ ] **Step 6: Run the focused PowerShell and Node tests**

Run:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-private-path-acl.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-setup-feishu-decision-channel.ps1
Push-Location tools/feishu-decision-bridge
node --test test/callback.test.mjs test/send.test.mjs
Pop-Location
```

Expected: all commands exit 0 and the ACL test prints `test-private-path-acl: OK`.

- [ ] **Step 7: Reapply the repaired business binding without changing its JSON**

Run a read/hash/write verification against `%USERPROFILE%\.codex\automation-state\tzg-feishu-decision-bridge\pending-bindings.json`: capture SHA-256 before, call `Set-PrivatePathAcl`, capture SHA-256 after, and assert equality plus exactly two DACL rules. Do not print binding contents.

- [ ] **Step 8: Commit the v7 stability slice**

Run whitespace and cached-diff checks for only the Task 1 paths, then commit:

```powershell
git commit -m "fix(automation): stabilize Feishu decision bridge"
```

Expected: the commit excludes private state and all unrelated user files.

---

## Task 2: Enforce one live bridge process

**Files:**

- Create: `tools/feishu-decision-bridge/src/instance-lock.mjs`
- Modify: `tools/feishu-decision-bridge/src/bridge.mjs`
- Modify: `tools/start-feishu-decision-bridge.ps1`
- Modify: `tools/install-feishu-decision-bridge.ps1`
- Test: `tools/feishu-decision-bridge/test/callback.test.mjs`
- Test: `tools/test-install-feishu-decision-bridge.ps1`

**Interfaces:**

- Produces: `acquireInstanceLock({ stateRoot, pid, processProbe, fs }) -> Promise<{ release(): Promise<void> }>`.
- Guarantees: a live verified bridge PID prevents a second bridge; a dead PID lock is reclaimed exactly once.

- [ ] **Step 1: Add failing live/stale lock tests**

Add tests that create `bridge-instance.lock` under a temp state root:

```javascript
await t.test('live owner rejects a second bridge', async () => {
  const first = await acquireInstanceLock({ stateRoot, pid: 101, processProbe: async () => true });
  await assert.rejects(
    acquireInstanceLock({ stateRoot, pid: 202, processProbe: async (pid) => pid === 101 }),
    /Bridge already running/,
  );
  await first.release();
});

await t.test('dead owner is reclaimed once', async () => {
  await writeFile(join(stateRoot, 'bridge-instance.lock'), '{"pid":101}', 'utf8');
  const lock = await acquireInstanceLock({ stateRoot, pid: 202, processProbe: async () => false });
  assert.equal(JSON.parse(await readFile(join(stateRoot, 'bridge-instance.lock'), 'utf8')).pid, 202);
  await lock.release();
});
```

- [ ] **Step 2: Run the lock tests and verify red**

Run `node --test test/callback.test.mjs` from the bridge directory.

Expected: fail because `instance-lock.mjs` or `acquireInstanceLock` is missing.

- [ ] **Step 3: Implement bounded atomic PID lock acquisition**

`instance-lock.mjs` must create `bridge-instance.lock` using `open(path, 'wx')`, write exact JSON `{schemaVersion:1,pid}`, flush, and retain ownership until `release`. On `EEXIST`, read at most 128 bytes, accept only exact keys and positive integer PID, probe liveness, and either throw `Bridge already running` or unlink and retry once. `release` must only unlink when the on-disk PID still equals the current owner.

- [ ] **Step 4: Integrate the lock into bridge lifecycle**

Acquire after private config validation and before `WSClient.start`. Release in the same shutdown/failure `finally` that writes `STOPPED`; signal handlers must await bridge shutdown. Inject `instanceLock` in tests so lifecycle assertions remain deterministic.

- [ ] **Step 5: Make install/reinstall remove only verified legacy orphans**

Before starting the scheduled task, `install-feishu-decision-bridge.ps1` may stop a legacy orphan only when all of these match: executable name `node.exe`, normalized command line contains the exact absolute `tools\feishu-decision-bridge\src\bridge.mjs`, and the parent wrapper is no longer alive. Tests must prove near-match commands and other repositories are never stopped.

- [ ] **Step 6: Run Node and installer tests**

```powershell
Push-Location tools/feishu-decision-bridge
npm test
Pop-Location
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-install-feishu-decision-bridge.ps1
```

Expected: all tests pass; test output contains no credentials or raw identifiers.

- [ ] **Step 7: Commit the single-instance slice**

```powershell
git commit -m "fix(automation): enforce one Feishu bridge instance"
```

---

## Task 3: Add shared custom-text normalization and render the new card

**Files:**

- Create: `tools/feishu-decision-bridge/src/custom-reply.mjs`
- Create: `tools/feishu-decision-bridge/test/custom-reply.test.mjs`
- Modify: `tools/feishu-decision-bridge/src/card.mjs`
- Test: `tools/feishu-decision-bridge/test/core.test.mjs`

**Interfaces:**

- Produces: `normalizeCustomText(value) -> string | null`.
- Produces: `formatCustomReplyCommand(decisionId) -> string`.
- Produces: `parseCustomReplyCommand(text) -> { decisionId, customText } | null`.
- Preserves: `buildDecisionCard(input, cardNonce)` public signature.

- [ ] **Step 1: Write failing normalization/parser tests**

Create tests for the exact approved behavior:

```javascript
assert.equal(normalizeCustomText('  第一行\r\n第二行  '), '第一行\n第二行');
assert.equal(normalizeCustomText('   '), null);
assert.equal(normalizeCustomText('x'.repeat(1001)), null);
assert.equal(normalizeCustomText('ok\u0000bad'), null);
assert.deepEqual(
  parseCustomReplyCommand('DEC-20260716-ABC123：自定义 采用双通道\n并迁移旧数据'),
  { decisionId: 'DEC-20260716-ABC123', customText: '采用双通道\n并迁移旧数据' },
);
assert.deepEqual(
  parseCustomReplyCommand('DEC-20260716-ABC123: 自定义 采用双通道'),
  { decisionId: 'DEC-20260716-ABC123', customText: '采用双通道' },
);
assert.equal(parseCustomReplyCommand('我想采用双通道'), null);
assert.equal(formatCustomReplyCommand('DEC-20260716-ABC123'), 'DEC-20260716-ABC123：自定义 <你的方案>');
```

Count code points with `[...text].length`, not UTF-16 code units.

- [ ] **Step 2: Run the new test and verify red**

Run `node --test test/custom-reply.test.mjs`.

Expected: fail because `custom-reply.mjs` is missing.

- [ ] **Step 3: Implement the exact normalization and strict parser**

Implement:

```javascript
const DECISION_ID = /^DEC-[0-9]{8}-[A-Z0-9]+$/;
const COMMAND = /^\s*(DEC-[0-9]{8}-[A-Z0-9]+)\s*[：:]\s*自定义[ \t]+([\s\S]+?)\s*$/u;

export function normalizeCustomText(value) {
  if (typeof value !== 'string') return null;
  const normalized = value.replace(/\r\n?/g, '\n').trim();
  if ([...normalized].length < 1 || [...normalized].length > 1000) return null;
  if ([...normalized].some((c) => /[\p{Cc}\p{Cf}\p{Cs}\p{Zl}\p{Zp}]/u.test(c) && c !== '\n' && c !== '\t')) return null;
  return normalized;
}
```

The parser must call `normalizeCustomText` for the captured body and return a fresh frozen result. The formatter must reject invalid decision IDs.

- [ ] **Step 4: Write failing card shape assertions**

Update `core.test.mjs` to assert:

```javascript
const card = buildDecisionCard(decision, 'nonce-123');
const buttons = card.elements
  .flatMap((element) => element.actions ?? element.elements ?? [])
  .filter((element) => element.tag === 'button');
assert.deepEqual(buttons.slice(0, 3).map((button) => button.text.content), ['选择 A', '选择 B', '选择 C']);
assert.match(JSON.stringify(card), /DEC-20260716-ABC123：自定义 <你的方案>/u);
assert.equal(JSON.stringify(buttons).includes(decision.options[0].label), false);
```

Also assert a `form` element contains input name `customDecision`, placeholder `输入你希望采用的方案（最多 1000 字）`, and a submit button with `action_type: 'form_submit'` whose value contains only `kind`, `decisionId`, and `cardNonce`.

- [ ] **Step 5: Run the card test and verify red**

Run `node --test test/core.test.mjs`.

Expected: fail because existing buttons contain full labels and there is no form/copy format.

- [ ] **Step 6: Render short buttons, form input, and copy format**

Keep the existing complete option body. Change A/B/C button content to ``选择 ${key}``. Add one official form container after the A/B/C action:

```javascript
{
  tag: 'form',
  name: 'customDecisionForm',
  elements: [
    {
      tag: 'input',
      name: 'customDecision',
      input_type: 'multiline_text',
      placeholder: { tag: 'plain_text', content: '输入你希望采用的方案（最多 1000 字）' },
    },
    {
      tag: 'button',
      action_type: 'form_submit',
      text: { tag: 'plain_text', content: '提交自定义方案' },
      type: 'primary',
      value: { kind: 'decision_custom_reply', decisionId: decision.decisionId, cardNonce },
    },
  ],
}
```

Add a plain text note containing `formatCustomReplyCommand(decision.decisionId)` and `长按复制格式`.

- [ ] **Step 7: Run focused and full Node tests**

```powershell
Push-Location tools/feishu-decision-bridge
node --test test/custom-reply.test.mjs test/core.test.mjs
npm test
Pop-Location
```

Expected: all pass and no prior A/B/C test loses full option text in the body.

- [ ] **Step 8: Commit the card slice**

```powershell
git commit -m "feat(automation): add concise Feishu decision card inputs"
```

---

## Task 4: Bind the provider conversation without leaking raw chat ID

**Files:**

- Modify: `tools/feishu-decision-bridge/src/send-runtime.mjs`
- Modify: `tools/feishu-decision-bridge/src/send-core.mjs`
- Modify: `tools/feishu-decision-bridge/src/send-decision.mjs`
- Modify: `tools/automation-controller.ps1`
- Modify: `tools/setup-feishu-decision-channel.ps1`
- Test: `tools/feishu-decision-bridge/test/send.test.mjs`
- Test: `tools/test-automation-controller.ps1`
- Test: `tools/test-setup-feishu-decision-channel.ps1`

**Interfaces:**

- Changes: `sendInteractive(request) -> { messageId, chatId }` internally.
- Produces: sender CLI accepted result adds `providerChatIdHash` and never returns raw `chatId`.
- Changes: private decision binding adds exact field `providerChatIdHash`.

- [ ] **Step 1: Write failing transport and sanitizer tests**

Use a fake SDK response `{ data: { message_id: 'om_123', chat_id: 'oc_123' } }`. Assert the internal transport returns both raw values to `send-core`, while public `sendDecision` returns:

```javascript
{
  result: 'PROVIDER_ACCEPTED',
  targetHash: /^[0-9a-f]{64}$/,
  providerMessageIdHash: /^[0-9a-f]{64}$/,
  providerChatIdHash: /^[0-9a-f]{64}$/,
  cardNonceHash: /^[0-9a-f]{64}$/,
  intentKeyHash: /^[0-9a-f]{64}$/,
}
```

The serialized result must not contain `om_123` or `oc_123`. Missing/invalid `chat_id` is `PROVIDER_OUTCOME_UNKNOWN`, because the provider may have accepted a message that cannot be safely bound for text replies.

- [ ] **Step 2: Run send tests and verify red**

Run `node --test test/send.test.mjs`.

Expected: fail because current transport returns only message ID and accepted output lacks `providerChatIdHash`.

- [ ] **Step 3: Implement chat ID extraction and hashing**

Replace the message-only extractor with an exact response snapshot returning `{ messageId, chatId }`. Hash both immediately in `send-core`; only hashes may cross the CLI boundary. Add `providerChatIdHash` to sanitized ACCEPTED send intent evidence so idempotent recovery returns the same binding hash without another provider call.

- [ ] **Step 4: Add private binding tests**

Update controller/setup tests so a valid binding is exact-keyed and contains:

```powershell
@{
  kind = 'decision_reply'
  decisionId = $decisionId
  allowedOptions = @('A','B','C')
  allowCustomReply = $true
  createdAt = $createdAt
  expiresAt = $expiresAt
  cardNonceHash = $cardNonceHash
  providerMessageIdHash = $providerMessageIdHash
  providerChatIdHash = $providerChatIdHash
}
```

Unknown, missing, accessor, non-hex, or duplicate fields must fail closed.

- [ ] **Step 5: Persist only the chat hash in private binding**

Extend the controller sender-result whitelist and `New-FeishuDecisionBinding`; do not add raw chat ID to controller state or protocol output. Update Canary binding creation with the same exact field.

- [ ] **Step 6: Run focused tests and commit**

```powershell
Push-Location tools/feishu-decision-bridge
node --test test/send.test.mjs
Pop-Location
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-setup-feishu-decision-channel.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-automation-controller.ps1
git commit -m "feat(automation): bind Feishu decision conversations"
```

Expected: all tests pass; controller test may take 4–5 minutes and must receive at least a 10-minute timeout.

---

## Task 5: Accept card form custom replies

**Files:**

- Modify: `tools/feishu-decision-bridge/src/callback-core.mjs`
- Modify: `tools/feishu-decision-bridge/src/bridge.mjs`
- Test: `tools/feishu-decision-bridge/test/callback.test.mjs`

**Interfaces:**

- Extends: `normalizeCardAction(rawEvent)` returns either option action or `{ kind:'decision_custom_reply', decisionId, customText, cardNonce }`.
- Extends: `handleCardAction(...)` writes a signed `decision_custom_reply` payload with source `feishu_card_input`.

- [ ] **Step 1: Add failing form callback tests**

Build an official SDK-flattened card callback whose action contains:

```javascript
{
  tag: 'button',
  name: 'submitCustomDecision',
  form_value: { customDecision: '  采用双通道\r\n保留旧字段  ' },
  value: {
    kind: 'decision_custom_reply',
    decisionId: 'DEC-20260716-ABC123',
    cardNonce: 'nonce-123',
  },
}
```

Assert normalization returns `customText: '采用双通道\n保留旧字段'`. Add rejection cases for missing/extra form fields, blank/1001-code-point content, accessor properties, unsafe controls, wrong nonce, wrong identity, expired binding, and `allowCustomReply:false`.

- [ ] **Step 2: Run callback tests and verify red**

Run `node --test test/callback.test.mjs`.

Expected: fail because `decision_custom_reply` is not accepted.

- [ ] **Step 3: Extend exact action snapshots**

For `decision_reply`, continue requiring exact `kind,decisionId,optionKey,cardNonce`. For `decision_custom_reply`, require exact `kind,decisionId,cardNonce`, exact form object key `customDecision`, and call `normalizeCustomText`. Never merge `form_value` into `value` and never enumerate unknown values in logs.

- [ ] **Step 4: Write the signed custom payload and response**

Create this internal payload after binding validation:

```javascript
{
  kind: 'decision_custom_reply',
  decisionId,
  customText,
  cardNonceHash: binding.cardNonceHash,
  providerMessageIdHash: binding.providerMessageIdHash,
  providerEventIdHash: sha256(eventId),
  operatorOpenIdHash: sha256(operatorOpenId),
  tenantKeyHash: sha256(tenantKey),
  receivedAt: receivedAt.toISOString(),
  source: 'feishu_card_input',
}
```

The callback response toast and read-only card must say `已登记自定义方案`, show the decision ID and normalized custom content, and contain no buttons or inputs.

- [ ] **Step 5: Run callback/full Node tests and commit**

```powershell
Push-Location tools/feishu-decision-bridge
node --test test/callback.test.mjs
npm test
Pop-Location
git commit -m "feat(automation): accept Feishu card custom replies"
```

---

## Task 6: Accept strict-format text messages over the long connection

**Files:**

- Create: `tools/feishu-decision-bridge/src/message-core.mjs`
- Create: `tools/feishu-decision-bridge/src/message-runtime.mjs`
- Create: `tools/feishu-decision-bridge/test/message.test.mjs`
- Modify: `tools/feishu-decision-bridge/src/bridge.mjs`

**Interfaces:**

- Produces: `normalizeMessageEvent(rawEvent) -> normalized event | null`.
- Produces: `handleDecisionTextMessage({ event, config, pendingBindings, now, replyText }) -> { accepted, rejectionCode }`.
- Produces: `createMessageReplyTransport(config, options) -> async (messageId, text) => void`.

- [ ] **Step 1: Write failing exact message-event tests**

Use the SDK 1.71.1 flattened `im.message.receive_v1` shape:

```javascript
{
  event_id: 'evt-123',
  event_type: 'im.message.receive_v1',
  tenant_key: 'tenant-123',
  sender: {
    sender_id: { open_id: 'ou-123' },
    sender_type: 'user',
    tenant_key: 'tenant-123',
  },
  message: {
    message_id: 'om-reply-123',
    create_time: '1784210400000',
    chat_id: 'oc-123',
    chat_type: 'p2p',
    message_type: 'text',
    content: JSON.stringify({ text: 'DEC-20260716-ABC123：自定义 采用双通道' }),
  },
}
```

Assert exact snapshots reject unknown fields, accessors, wrong event type, bot sender, non-text type, invalid content JSON, wrong chat hash, operator, tenant, decision ID, expiry, and messages before binding creation.

- [ ] **Step 2: Run message tests and verify red**

Run `node --test test/message.test.mjs`.

Expected: fail because `message-core.mjs` is missing.

- [ ] **Step 3: Implement message normalization and binding**

Parse only exact data properties. The message `content` JSON must be a plain object with exactly one enumerable data key `text`. Match the command through `parseCustomReplyCommand`, then require:

```javascript
sha256(openId) === config.pairedOperatorOpenIdHash
sha256(tenantKey) === sha256(config.expectedTenantKey)
sha256(chatId) === binding.providerChatIdHash
command.decisionId === binding.decisionId
binding.allowCustomReply === true
createdAt >= binding.createdAt && createdAt <= binding.expiresAt
```

Write a signed `decision_custom_reply` without `cardNonceHash`, with source `feishu_text`, message/provider event hashes, operator/tenant hashes, chat hash, normalized text, and ISO `receivedAt`.

- [ ] **Step 4: Implement one-shot reply/hint transport**

`message-runtime.mjs` must construct the SDK Client with the same silent logger used by `send-runtime.mjs` and call `client.im.message.reply` with:

```javascript
{
  path: { message_id: incomingMessageId },
  data: { msg_type: 'text', content: JSON.stringify({ text }) },
}
```

Successful valid input receives `已登记 DEC-... 自定义方案：\n<normalized text>`. A near-format message from the paired operator in the bound chat may receive one usage hint containing `formatCustomReplyCommand`; all identity/chat/tenant failures remain silent. Reply failure must not delete or rewrite inbox evidence.

- [ ] **Step 5: Register the second dispatcher route**

Extend the existing single `eventDispatcher.register` call:

```javascript
const registered = eventDispatcher.register({
  'card.action.trigger': cardCallback,
  'im.message.receive_v1': messageCallback,
});
```

The message callback uses the same bounded config/binding reads, timeout, sanitized logger, and HMAC writer as the card callback. Health remains `CONNECTED` when message replies fail; log only `message_reply_failed` or a generic rejection category.

- [ ] **Step 6: Run message/callback/full tests and commit**

```powershell
Push-Location tools/feishu-decision-bridge
node --test test/message.test.mjs test/callback.test.mjs
npm test
Pop-Location
git commit -m "feat(automation): accept strict Feishu text decisions"
```

---

## Task 7: Consume option and custom replies with first-valid-wins semantics

**Files:**

- Modify: `tools/feishu-decision-bridge/src/inbox.mjs`
- Modify: `tools/feishu-decision-bridge/src/consume-reply.mjs`
- Test: `tools/feishu-decision-bridge/test/consume.test.mjs`

**Interfaces:**

- Extends: `consumeCurrentReply(...)` returns `OPTION_ACCEPTED`, `CUSTOM_ACCEPTED`, or no reply.
- `CUSTOM_ACCEPTED` carries normalized `customText`, source, and exact hash evidence; it carries `cardNonceHash` only for `feishu_card_input`.

- [ ] **Step 1: Add failing custom inbox tests**

Test card-input and text payloads separately, including exact-key enforcement. Add a mixed race:

```javascript
await writeEnvelope(optionAt1000);
await writeEnvelope(customAt1001);
const result = await consumeCurrentReply(args);
assert.equal(result.result, 'OPTION_ACCEPTED');
assert.equal(result.optionKey, 'A');
assert.equal((await readdir(quarantineDirectory)).length, 1);
```

Add the inverse race where custom is earlier, equal-time deterministic ordering by providerEventIdHash, same custom text duplicates, different custom text conflicts, processed nonce replay, and text replies with a forbidden nonce field.

- [ ] **Step 2: Run consume tests and verify red**

Run `node --test test/consume.test.mjs`.

Expected: fail because inbox accepts only `decision_reply` and quarantines all conflicts.

- [ ] **Step 3: Add exact custom payload snapshots**

Card custom payload exact keys include `cardNonceHash`; text custom exact keys include `providerChatIdHash` instead. Both require source-specific field sets, 64-lowercase-hex hashes, exact ISO time, valid decision ID, and `normalizeCustomText(customText) === customText`.

- [ ] **Step 4: Implement deterministic first-valid-wins**

Sort valid envelopes by parsed `receivedAt`, then `providerEventIdHash`. Select the first reply. Move all semantically identical replays to processed and all later conflicting replies to quarantine. A reply identity is:

```javascript
payload.kind === 'decision_reply'
  ? `option:${payload.optionKey}`
  : `custom:${sha256(payload.customText)}`;
```

Do not include raw custom text in filenames, logs, or quarantine reasons.

- [ ] **Step 5: Extend the CLI whitelist**

`consume-reply.mjs` may emit `customText` only when `result === 'CUSTOM_ACCEPTED'`. Reject any output with mixed `optionKey` and `customText`, missing source-specific evidence, extra fields, or unsafe custom text.

- [ ] **Step 6: Run consume/full tests and commit**

```powershell
Push-Location tools/feishu-decision-bridge
node --test test/consume.test.mjs
npm test
Pop-Location
git commit -m "feat(automation): consume custom Feishu decisions"
```

---

## Task 8: Upgrade controller state to schema v8 custom resolutions

**Files:**

- Modify: `tools/automation-controller-state.ps1`
- Modify: `tools/automation-controller.ps1`
- Test: `tools/test-automation-controller-state.ps1`
- Test: `tools/test-automation-controller.ps1`

**Interfaces:**

- Adds internal state action `ResolveCustomDecision`.
- Adds state parameters `CustomText` and sources `feishu_card_input`, `feishu_text`, `manual_custom`.
- Preserves model-visible controller action `ConsumeDecisionReply`; extends `ResolveDecisionManual` exact syntax to custom text.

- [ ] **Step 1: Write failing v7→v8 and custom resolution state tests**

Tests must prove:

```powershell
$migrated = Read-TestStateAfterShow $v7Fixture
if ($migrated.schemaVersion -ne 8) { throw 'v7 did not migrate to v8' }

$resolved = Invoke-StateTool @(
  'ResolveCustomDecision', '-RunId', 'run-custom',
  '-DecisionId', $decisionId, '-CustomText', "采用双通道`n保留旧字段",
  '-ReplySource', 'feishu_text',
  '-ProviderMessageIdHash', ('a' * 64), '-ProviderEventIdHash', ('b' * 64),
  '-OperatorHash', ('c' * 64), '-TenantKeyHash', ('d' * 64),
  '-ProviderChatIdHash', ('e' * 64), '-EvidenceHash', ('f' * 64)
)
```

Assert the resolution contains `customText` and no `optionKey`/`cardNonceHash`, existing accepted provider message evidence matches, and v7 option/Gmail/Feishu resolutions migrate byte-for-byte except schema version. Add invalid mixed fields, blank/overlong/unsafe text, wrong source evidence, duplicate resolve, and manual override tests.

- [ ] **Step 2: Run state tests and verify red**

Run `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-automation-controller-state.ps1`.

Expected: fail because schema remains 7 and `ResolveCustomDecision` is not an action.

- [ ] **Step 3: Add schema v8 migration and resolution shape validation**

Advance the current schema constant to 8. Migration accepts schemas 1–7, runs existing migrations, then validates each resolution as exactly one of:

```powershell
@{ optionKey = 'A'; source = 'feishu_card'; resolvedAt = $iso; evidenceHash = $hash; ... }
@{ customText = '...'; source = 'feishu_text'; resolvedAt = $iso; evidenceHash = $hash; ... }
```

Do not invent `optionKey = 'CUSTOM'` or option D.

- [ ] **Step 4: Implement `ResolveCustomDecision`**

Reuse the pending-decision ownership, accepted-notification, evidence-hash, atomic flow transition and lease code from `ResolveDecision`, but require `CustomText` instead of `OptionKey`. Source-specific exact evidence:

- `feishu_card_input`: provider message/event, operator, tenant, card nonce, evidence hash.
- `feishu_text`: provider message/event, operator, tenant, provider chat, evidence hash; no card nonce.
- `manual_custom`: `-ManualOverride`, thread ID, optional turn ID; no Feishu evidence.

- [ ] **Step 5: Add failing controller consumer tests**

Fake the Node consumer output:

```json
{"result":"CUSTOM_ACCEPTED","decisionId":"DEC-20260716-ABC123","customText":"采用双通道","source":"feishu_text","providerMessageIdHash":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","providerEventIdHash":"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb","operatorOpenIdHash":"cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc","tenantKeyHash":"dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd","providerChatIdHash":"eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee","evidenceHash":"ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff"}
```

Assert `ConsumeDecisionReply` calls `ResolveCustomDecision`, returns original TaskId/`InspectCandidate`, and never exposes raw provider identifiers. Add mixed/extra/unsafe CLI output rejection.

- [ ] **Step 6: Implement controller routing and manual custom fallback**

When consumer result is `OPTION_ACCEPTED`, retain current path. For `CUSTOM_ACCEPTED`, build the exact source-specific `ResolveCustomDecision` arguments. Extend manual reply parsing to accept:

```text
DEC-20260716-ABC123：自定义 采用双通道
```

and call source `manual_custom`; existing `DEC-...：选择 A` remains unchanged.

- [ ] **Step 7: Run state/controller tests and commit**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-automation-controller-state.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-automation-controller.ps1
git commit -m "feat(automation): persist custom decision resolutions"
```

Expected: both exit 0; controller test receives at least 10 minutes.

---

## Task 9: Extend setup, health, canaries, rules, and prompt

**Files:**

- Modify: `tools/setup-feishu-decision-channel.ps1`
- Modify: `tools/test-setup-feishu-decision-channel.ps1`
- Modify: `tools/install-feishu-decision-bridge.ps1`
- Modify: `tools/test-install-feishu-decision-bridge.ps1`
- Modify: `开发管理/自动工作流规则.txt`
- Modify: `开发管理/自动工作流控制器提示词.txt`
- Modify: `docs/superpowers/plans/2026-07-15-feishu-decision-channel-implementation.md`

**Interfaces:**

- Adds setup actions `CanaryCardCustom` and `CanaryTextCustom`.
- Health distinguishes card connectivity from optional text-event readiness.

- [ ] **Step 1: Write failing setup canary tests**

Add action-contract tests for:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/setup-feishu-decision-channel.ps1 CanaryCardCustom -PairTimeoutSeconds 300
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/setup-feishu-decision-channel.ps1 CanaryTextCustom -PairTimeoutSeconds 300
```

Fake inbox payloads must require exact custom text `CANARY_CUSTOM_OK`, correct source, current identity/chat binding, and one-time consumption. Text permission unavailable must produce sanitized `TEXT_REPLY_UNAVAILABLE` while existing card Canary remains usable.

- [ ] **Step 2: Run setup tests and verify red**

Run `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-setup-feishu-decision-channel.ps1`.

Expected: fail because the two actions are not in `ValidateSet`.

- [ ] **Step 3: Implement the two canaries**

`CanaryCardCustom` sends a temporary decision card and waits for card input exactly `CANARY_CUSTOM_OK`. `CanaryTextCustom` sends a temporary card whose body shows the exact command:

```text
DEC-<yyyyMMdd>-CANARY<uppercaseHexNonce>：自定义 CANARY_CUSTOM_OK
```

The generated ID must match `^DEC-[0-9]{8}-[A-Z0-9]+$`; for example `DEC-20260716-CANARYA1B2C3D4`. The action then waits for `feishu_text`. Both canaries remove only their own binding in `finally`, emit only hashes/counts, and print `CANARY_CARD_CUSTOM_ACCEPTED` or `CANARY_TEXT_CUSTOM_ACCEPTED`.

- [ ] **Step 4: Update rules and controller prompt**

Document that A/B/C and custom replies are controller-owned structured evidence; the model must never search Feishu history or infer a decision from arbitrary chat. Add the strict manual syntax and schema v8 status. Keep Gmail as legacy-only evidence.

- [ ] **Step 5: Run static and direct tests**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-setup-feishu-decision-channel.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-install-feishu-decision-bridge.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-automation-workflow.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-review-text.ps1 -Paths AGENTS.md,CLAUDE.md,开发管理
```

Expected: all exit 0.

- [ ] **Step 6: Commit setup and documentation**

```powershell
git commit -m "docs(automation): define custom Feishu decision workflow"
```

---

## Task 10: Deploy and verify all three live reply paths

**Files:**

- Modify: `开发管理/自动工作流状态.txt`
- Deploy: local bridge scheduled task and Feishu app event subscription
- Deploy: Codex automation `tzg-hourly-controller`

**Interfaces:**

- Consumes: all prior tasks and the existing paired private configuration.
- Produces: one live bridge, card option/custom/text canaries, schema v8 state, updated controller prompt, and restored unique automation writer.

- [ ] **Step 1: Run the complete offline regression**

```powershell
Push-Location tools/feishu-decision-bridge
npm test
Pop-Location
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-private-path-acl.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-setup-feishu-decision-channel.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-install-feishu-decision-bridge.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-automation-controller-state.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-automation-controller.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-automation-decision-status.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-automation-workflow.ps1
```

Expected: every command exits 0; no raw credential/identity/chat/message value appears in output.

- [ ] **Step 2: Upgrade the scheduled bridge and prove a unique process**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/install-feishu-decision-bridge.ps1 Install
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/install-feishu-decision-bridge.ps1 Status
```

Then enumerate `node.exe` processes and require exactly one whose normalized command line is the exact repository `bridge.mjs`. Require health `CONNECTED` updated within 120 seconds.

- [ ] **Step 3: Enable the minimum Feishu text-message event permission**

In the Feishu developer console, add the long-connection event `im.message.receive_v1` and only the receive/reply permissions required for bot P2P text messages. Publish the app version if the console requires it. Do not enable history search or unrelated contact/message permissions.

- [ ] **Step 4: Run option and card-input canaries**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/setup-feishu-decision-channel.ps1 Canary -PairTimeoutSeconds 300
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/setup-feishu-decision-channel.ps1 CanaryCardCustom -PairTimeoutSeconds 300
```

User clicks A on the first card and enters `CANARY_CUSTOM_OK` in the second. Expected sanitized results: `CANARY_ACCEPTED` and `CANARY_CARD_CUSTOM_ACCEPTED`.

- [ ] **Step 5: Run strict-text canary and a negative chat check**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/setup-feishu-decision-channel.ps1 CanaryTextCustom -PairTimeoutSeconds 300
```

User copies the exact command from the card and sends it. Expected: bot replies with the normalized content and setup prints `CANARY_TEXT_CUSTOM_ACCEPTED`. Then send a normal message without decision ID/prefix; verify no inbox envelope and no resolution is created.

- [ ] **Step 6: Publish project status through the controlled Finish path**

Record schema v8, one live bridge, three successful canaries, active Feishu provider, custom reply availability, and Gmail legacy rollback in `开发管理/自动工作流状态.txt`. Do not record raw app/user/chat/message identifiers or canary content. Run required whitespace/review checks and commit only the status path.

- [ ] **Step 7: Deploy the prompt and restore the unique automation writer**

Use the Codex application automation update tool to deploy `开发管理/自动工作流控制器提示词.txt` to `tzg-hourly-controller`; do not edit automation TOML. Re-read the automation and confirm it is the only enabled writer with the expected schedule and prompt. If the automation tool is unavailable, leave it paused and report that single explicit blocker instead of editing private automation files.

- [ ] **Step 8: Final verification and clean handoff**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/automation-controller-state.ps1 Show
git status --short
git log -8 --oneline
```

Expected: schema 8, no pending duplicate decision, no active lease, exactly one bridge process, all implementation paths committed, and only the user's unrelated untracked files remain.

## Acceptance Checklist

- [ ] Mobile card buttons show only `选择 A/B/C` and never truncate option descriptions.
- [ ] Full option descriptions remain visible in the card body.
- [ ] Card displays an input and the copyable exact text command.
- [ ] Card custom input and strict text command both create custom resolutions.
- [ ] Normal chat, wrong identity/chat/tenant/decision, expired events, and unsafe text never resolve a decision.
- [ ] First valid reply wins; retries are idempotent and conflicts cannot overwrite.
- [ ] v7 history migrates losslessly to schema v8.
- [ ] Private ACL changes do not require `SeSecurityPrivilege` and preserve file contents.
- [ ] Exactly one bridge process owns the long connection.
- [ ] Node, PowerShell, workflow, review-text, and live canaries all pass.
- [ ] The unique Codex automation writer is restored only after prompt/status verification.
