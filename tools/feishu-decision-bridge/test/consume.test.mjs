import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import {
  mkdir, mkdtemp, readFile, readdir, rm, unlink, writeFile,
} from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import test from 'node:test';

import { main as consumeMain } from '../src/consume-reply.mjs';
import { canonicalize, signEnvelope } from '../src/envelope.mjs';
import { consumeCurrentReply, writeSignedInbox } from '../src/inbox.mjs';

const NOW = new Date('2026-07-16T08:00:00.000Z');
const APP_ID = 'app_fake_demo';
const TENANT_KEY = 'tenant_fake_demo';
const OPERATOR_OPEN_ID = 'ou_fake_operator';
const MESSAGE_ID = 'om_fake_message';
const DECISION_ID = 'DEC-20260716-FAKE0001';
const CARD_NONCE = 'nonce_fake_card';
const HMAC_KEY = Buffer.alloc(32, 0x42).toString('base64');
const CLI_PATH = fileURLToPath(new URL('../src/consume-reply.mjs', import.meta.url));

function sha256(value) {
  return createHash('sha256').update(value, 'utf8').digest('hex');
}

function makeConfig(stateRoot, overrides = {}) {
  return {
    schemaVersion: 1,
    appId: APP_ID,
    appSecret: 'fake_secret_for_tests_only',
    recipient: { type: 'open_id', value: 'ou_fake_recipient' },
    expectedTenantKey: TENANT_KEY,
    pairedOperatorOpenIdHash: sha256(OPERATOR_OPEN_ID),
    hmacKey: HMAC_KEY,
    stateRoot,
    ...overrides,
  };
}

function makePending(overrides = {}) {
  return {
    decisionId: DECISION_ID,
    allowedOptions: ['A', 'B', 'C'],
    allowCustomReply: true,
    createdAt: new Date(NOW.getTime() - 60_000).toISOString(),
    expiresAt: new Date(NOW.getTime() + 60_000).toISOString(),
    cardNonceHash: sha256(CARD_NONCE),
    providerMessageIdHash: sha256(MESSAGE_ID),
    providerChatIdHash: sha256('oc_fake_chat'),
    ...overrides,
  };
}

function makeCustomPayload(eventId, customText = '采用双通道', source = 'feishu_card_input', overrides = {}) {
  const payload = {
    kind: 'decision_custom_reply',
    decisionId: DECISION_ID,
    customText,
    providerMessageIdHash: sha256(MESSAGE_ID),
    providerEventIdHash: sha256(eventId),
    operatorOpenIdHash: sha256(OPERATOR_OPEN_ID),
    tenantKeyHash: sha256(TENANT_KEY),
    receivedAt: NOW.toISOString(),
    source,
    ...overrides,
  };
  if (source === 'feishu_card_input') {
    payload.cardNonceHash ??= sha256(CARD_NONCE);
  } else {
    payload.providerChatIdHash ??= sha256('oc_fake_chat');
  }
  return payload;
}

function makePayload(eventId, overrides = {}) {
  return {
    kind: 'decision_reply',
    decisionId: DECISION_ID,
    optionKey: 'A',
    cardNonceHash: sha256(CARD_NONCE),
    providerMessageIdHash: sha256(MESSAGE_ID),
    providerEventIdHash: sha256(eventId),
    operatorOpenIdHash: sha256(OPERATOR_OPEN_ID),
    tenantKeyHash: sha256(TENANT_KEY),
    receivedAt: NOW.toISOString(),
    ...overrides,
  };
}

async function put(root, eventId, overrides = {}) {
  const envelope = signEnvelope(makePayload(eventId, overrides), HMAC_KEY);
  const eventIdHash = sha256(eventId);
  await writeSignedInbox({ stateRoot: root, envelope, eventIdHash });
  return { envelope, eventIdHash };
}

async function putCustom(root, eventId, customText, source, overrides = {}) {
  const payload = makeCustomPayload(eventId, customText, source, overrides);
  const envelope = signEnvelope(payload, HMAC_KEY);
  const eventIdHash = sha256(eventId);
  await writeSignedInbox({ stateRoot: root, envelope, eventIdHash });
  return { envelope, eventIdHash, payload };
}

function expectedAccepted(payload, envelope) {
  return {
    result: 'OPTION_ACCEPTED',
    optionKey: payload.optionKey,
    source: 'feishu_card',
    providerMessageIdHash: payload.providerMessageIdHash,
    providerEventIdHash: payload.providerEventIdHash,
    operatorOpenIdHash: payload.operatorOpenIdHash,
    tenantKeyHash: payload.tenantKeyHash,
    cardNonceHash: payload.cardNonceHash,
    evidenceHash: sha256(canonicalize(envelope)),
  };
}

test('writeSignedInbox is atomic, idempotent, strict, and fails closed on conflicting evidence', async (t) => {
  const root = await mkdtemp(join(tmpdir(), 'tzg-inbox-write-'));
  t.after(() => rm(root, { recursive: true, force: true }));
  const eventId = 'evt_fake_write';
  const eventIdHash = sha256(eventId);
  const envelope = signEnvelope(makePayload(eventId), HMAC_KEY);
  assert.deepEqual(await writeSignedInbox({ stateRoot: root, envelope, eventIdHash }), {
    written: true,
    duplicate: false,
  });
  assert.deepEqual(await writeSignedInbox({ stateRoot: root, envelope, eventIdHash }), {
    written: false,
    duplicate: true,
  });
  const files = await readdir(join(root, 'inbox'));
  assert.deepEqual(files, [`${eventIdHash}.json`]);
  const raw = await readFile(join(root, 'inbox', files[0]), 'utf8');
  for (const forbidden of [TENANT_KEY, OPERATOR_OPEN_ID, MESSAGE_ID, eventId, CARD_NONCE, 'raw', 'target']) {
    assert.equal(raw.includes(forbidden), false);
  }

  const conflict = signEnvelope(makePayload(eventId, { optionKey: 'B' }), HMAC_KEY);
  await assert.rejects(
    writeSignedInbox({ stateRoot: root, envelope: conflict, eventIdHash }),
    /Inbox write failed/,
  );
  assert.equal(await readFile(join(root, 'inbox', files[0]), 'utf8'), raw);
  await assert.rejects(
    writeSignedInbox({ stateRoot: 'relative', envelope, eventIdHash }),
    /Inbox write failed/,
  );
  await assert.rejects(
    writeSignedInbox({ stateRoot: root, envelope: { ...envelope, raw: 'forbidden' }, eventIdHash }),
    /Inbox write failed/,
  );
});

function expectedCustomAccepted(payload, envelope) {
  const result = {
    result: 'CUSTOM_ACCEPTED',
    decisionId: payload.decisionId,
    customText: payload.customText,
    source: payload.source,
    providerMessageIdHash: payload.providerMessageIdHash,
    providerEventIdHash: payload.providerEventIdHash,
    operatorOpenIdHash: payload.operatorOpenIdHash,
    tenantKeyHash: payload.tenantKeyHash,
    evidenceHash: sha256(canonicalize(envelope)),
  };
  if (payload.source === 'feishu_card_input') {
    result.cardNonceHash = payload.cardNonceHash;
  } else {
    result.providerChatIdHash = payload.providerChatIdHash;
  }
  return result;
}

test('consumer returns the fixed accepted structure, moves evidence, and consumes once', async (t) => {
  const root = await mkdtemp(join(tmpdir(), 'tzg-consume-valid-'));
  t.after(() => rm(root, { recursive: true, force: true }));
  const eventId = 'evt_fake_valid';
  const { envelope, eventIdHash } = await put(root, eventId);
  const result = await consumeCurrentReply({
    stateRoot: root,
    config: makeConfig(root),
    pendingDecision: makePending(),
    now: NOW,
  });
  assert.deepEqual(result, expectedAccepted(makePayload(eventId), envelope));
  assert.deepEqual(Object.keys(result).sort(), [
    'cardNonceHash', 'evidenceHash', 'operatorOpenIdHash', 'optionKey',
    'providerEventIdHash', 'providerMessageIdHash', 'result', 'source', 'tenantKeyHash',
  ]);
  assert.deepEqual(await readdir(join(root, 'inbox')), []);
  assert.deepEqual(await readdir(join(root, 'processed')), [`${eventIdHash}.json`]);
  assert.deepEqual(await writeSignedInbox({ stateRoot: root, envelope, eventIdHash }), {
    written: false,
    duplicate: true,
  });
  assert.equal(await consumeCurrentReply({
    stateRoot: root,
    config: makeConfig(root),
    pendingDecision: makePending(),
    now: NOW,
  }), null);
  assert.equal(JSON.stringify(result).includes(DECISION_ID), false);
  assert.equal(JSON.stringify(result).includes(OPERATOR_OPEN_ID), false);
  assert.equal(JSON.stringify(result).includes(TENANT_KEY), false);
  assert.equal(JSON.stringify(result).includes(MESSAGE_ID), false);
});

test('a new provider event cannot reuse a card nonce that was already processed', async (t) => {
  const root = await mkdtemp(join(tmpdir(), 'tzg-consume-nonce-replay-'));
  t.after(() => rm(root, { recursive: true, force: true }));
  const args = {
    stateRoot: root,
    config: makeConfig(root),
    pendingDecision: makePending(),
    now: NOW,
  };
  await put(root, 'evt_fake_nonce_first');
  assert.equal((await consumeCurrentReply(args))?.result, 'OPTION_ACCEPTED');
  const replay = await put(root, 'evt_fake_nonce_second');
  assert.equal(await consumeCurrentReply(args), null);
  assert.deepEqual(await readdir(join(root, 'quarantine')), [`${replay.eventIdHash}.json`]);
});

test('multiple valid envelopes with the same option are all processed using stable earliest evidence', async (t) => {
  const root = await mkdtemp(join(tmpdir(), 'tzg-consume-same-'));
  t.after(() => rm(root, { recursive: true, force: true }));
  const later = await put(root, 'evt_fake_later', {
    receivedAt: new Date(NOW.getTime() + 1_000).toISOString(),
  });
  const earlier = await put(root, 'evt_fake_earlier', {
    receivedAt: new Date(NOW.getTime() - 1_000).toISOString(),
  });
  const result = await consumeCurrentReply({
    stateRoot: root,
    config: makeConfig(root),
    pendingDecision: makePending(),
    now: new Date(NOW.getTime() + 2_000),
  });
  assert.equal(result.optionKey, 'A');
  assert.equal(result.providerEventIdHash, earlier.eventIdHash);
  assert.equal(result.evidenceHash, sha256(canonicalize(earlier.envelope)));
  assert.deepEqual((await readdir(join(root, 'processed'))).sort(), [
    `${earlier.eventIdHash}.json`, `${later.eventIdHash}.json`,
  ].sort());
});

test('earliest valid option wins and later conflicting options are quarantined', async (t) => {
  const root = await mkdtemp(join(tmpdir(), 'tzg-consume-conflict-'));
  t.after(() => rm(root, { recursive: true, force: true }));
  const a = await put(root, 'evt_fake_a', {
    optionKey: 'A', receivedAt: new Date(NOW.getTime() - 1).toISOString(),
  });
  const b = await put(root, 'evt_fake_b', {
    optionKey: 'B', receivedAt: NOW.toISOString(),
  });
  const result = await consumeCurrentReply({
    stateRoot: root,
    config: makeConfig(root),
    pendingDecision: makePending(),
    now: NOW,
  });
  assert.equal(result.result, 'OPTION_ACCEPTED');
  assert.equal(result.optionKey, 'A');
  assert.deepEqual(await readdir(join(root, 'processed')), [`${a.eventIdHash}.json`]);
  assert.deepEqual(await readdir(join(root, 'quarantine')), [`${b.eventIdHash}.json`]);
  assert.deepEqual(await readdir(join(root, 'inbox')), []);
});

test('card and text custom replies use exact source-specific evidence', async (t) => {
  for (const source of ['feishu_card_input', 'feishu_text']) {
    await t.test(source, async (t) => {
      const root = await mkdtemp(join(tmpdir(), 'tzg-consume-custom-'));
      t.after(() => rm(root, { recursive: true, force: true }));
      const item = await putCustom(root, `evt_custom_${source}`, '采用双通道', source);
      const result = await consumeCurrentReply({
        stateRoot: root,
        config: makeConfig(root),
        pendingDecision: makePending(),
        now: NOW,
      });
      assert.deepEqual(result, expectedCustomAccepted(item.payload, item.envelope));
      assert.equal(Object.hasOwn(result, source === 'feishu_text' ? 'cardNonceHash' : 'providerChatIdHash'), false);
    });
  }

  const root = await mkdtemp(join(tmpdir(), 'tzg-consume-custom-invalid-'));
  t.after(() => rm(root, { recursive: true, force: true }));
  const eventId = 'evt_text_forbidden_nonce';
  const invalid = makeCustomPayload(eventId, 'ok', 'feishu_text');
  invalid.cardNonceHash = sha256(CARD_NONCE);
  await assert.rejects(writeSignedInbox({
    stateRoot: root,
    envelope: signEnvelope(invalid, HMAC_KEY),
    eventIdHash: sha256(eventId),
  }), /Inbox write failed/);
});

test('processed evidence from an earlier decision does not compete with the current decision', async (t) => {
  const root = await mkdtemp(join(tmpdir(), 'tzg-consume-sequential-decisions-'));
  t.after(() => rm(root, { recursive: true, force: true }));
  const first = await put(root, 'evt_sequential_first');
  const firstResult = await consumeCurrentReply({
    stateRoot: root,
    config: makeConfig(root),
    pendingDecision: makePending(),
    now: NOW,
  });
  assert.equal(firstResult.result, 'OPTION_ACCEPTED');

  const nextDecisionId = 'DEC-20260716-NEXT001';
  const nextMessageHash = sha256('om_fake_next_message');
  const nextNonceHash = sha256('next-card-nonce');
  const nextEventId = 'evt_sequential_next';
  const nextPayload = makeCustomPayload(nextEventId, '下一项方案', 'feishu_card_input', {
    decisionId: nextDecisionId,
    providerMessageIdHash: nextMessageHash,
    cardNonceHash: nextNonceHash,
  });
  const nextEnvelope = signEnvelope(nextPayload, HMAC_KEY);
  const nextEventIdHash = sha256(nextEventId);
  await writeSignedInbox({ stateRoot: root, envelope: nextEnvelope, eventIdHash: nextEventIdHash });
  const nextResult = await consumeCurrentReply({
    stateRoot: root,
    config: makeConfig(root),
    pendingDecision: makePending({
      decisionId: nextDecisionId,
      providerMessageIdHash: nextMessageHash,
      cardNonceHash: nextNonceHash,
    }),
    now: NOW,
  });
  assert.deepEqual(nextResult, expectedCustomAccepted(nextPayload, nextEnvelope));
  assert.deepEqual((await readdir(join(root, 'processed'))).sort(), [
    `${first.eventIdHash}.json`, `${nextEventIdHash}.json`,
  ].sort());
});

test('first valid reply wins across option/custom races and equal custom replays are idempotent', async (t) => {
  const root = await mkdtemp(join(tmpdir(), 'tzg-consume-mixed-race-'));
  t.after(() => rm(root, { recursive: true, force: true }));
  const option = await put(root, 'evt_mixed_option', {
    receivedAt: new Date(NOW.getTime() - 2).toISOString(),
  });
  const sameOne = await putCustom(root, 'evt_mixed_custom_same_1', '采用双通道', 'feishu_text', {
    receivedAt: new Date(NOW.getTime() - 1).toISOString(),
  });
  const sameTwo = await putCustom(root, 'evt_mixed_custom_same_2', '采用双通道', 'feishu_text', {
    receivedAt: NOW.toISOString(),
  });
  const result = await consumeCurrentReply({
    stateRoot: root,
    config: makeConfig(root),
    pendingDecision: makePending(),
    now: NOW,
  });
  assert.equal(result.result, 'OPTION_ACCEPTED');
  assert.equal(result.providerEventIdHash, option.eventIdHash);
  assert.deepEqual(await readdir(join(root, 'processed')), [`${option.eventIdHash}.json`]);
  assert.deepEqual((await readdir(join(root, 'quarantine'))).sort(), [
    `${sameOne.eventIdHash}.json`, `${sameTwo.eventIdHash}.json`,
  ].sort());

  const customRoot = await mkdtemp(join(tmpdir(), 'tzg-consume-custom-replay-'));
  t.after(() => rm(customRoot, { recursive: true, force: true }));
  const first = await putCustom(customRoot, 'evt_custom_first', '同一方案', 'feishu_text', {
    receivedAt: new Date(NOW.getTime() - 1).toISOString(),
  });
  const duplicate = await putCustom(customRoot, 'evt_custom_duplicate', '同一方案', 'feishu_card_input');
  const different = await putCustom(customRoot, 'evt_custom_different', '不同方案', 'feishu_text');
  const customResult = await consumeCurrentReply({
    stateRoot: customRoot,
    config: makeConfig(customRoot),
    pendingDecision: makePending(),
    now: NOW,
  });
  assert.equal(customResult.result, 'CUSTOM_ACCEPTED');
  assert.equal(customResult.providerEventIdHash, first.eventIdHash);
  assert.deepEqual((await readdir(join(customRoot, 'processed'))).sort(), [
    `${first.eventIdHash}.json`, `${duplicate.eventIdHash}.json`,
  ].sort());
  assert.deepEqual(await readdir(join(customRoot, 'quarantine')), [`${different.eventIdHash}.json`]);

  const tieRoot = await mkdtemp(join(tmpdir(), 'tzg-consume-equal-time-'));
  t.after(() => rm(tieRoot, { recursive: true, force: true }));
  const tieA = await put(tieRoot, 'evt_equal_a', { optionKey: 'A' });
  const tieB = await put(tieRoot, 'evt_equal_b', { optionKey: 'B' });
  const tieWinner = tieA.eventIdHash.localeCompare(tieB.eventIdHash) < 0 ? tieA : tieB;
  const tieResult = await consumeCurrentReply({
    stateRoot: tieRoot,
    config: makeConfig(tieRoot),
    pendingDecision: makePending(),
    now: NOW,
  });
  assert.equal(tieResult.providerEventIdHash, tieWinner.eventIdHash);
});

test('tampered, malformed, filename-mismatched, stale, and identity-mismatched evidence is quarantined', async (t) => {
  const cases = [
    ['stale decision', { decisionId: 'DEC-20260716-STALE001' }],
    ['wrong option', { optionKey: 'D' }],
    ['wrong nonce', { cardNonceHash: '1'.repeat(64) }],
    ['wrong message', { providerMessageIdHash: '2'.repeat(64) }],
    ['wrong operator', { operatorOpenIdHash: '3'.repeat(64) }],
    ['wrong tenant', { tenantKeyHash: '4'.repeat(64) }],
    ['too early', { receivedAt: new Date(NOW.getTime() - 120_000).toISOString() }],
    ['too late', { receivedAt: new Date(NOW.getTime() + 120_000).toISOString() }],
  ];
  for (const [name, overrides] of cases) {
    await t.test(name, async (t) => {
      const root = await mkdtemp(join(tmpdir(), 'tzg-consume-invalid-'));
      t.after(() => rm(root, { recursive: true, force: true }));
      const eventId = `evt_fake_${name.replaceAll(' ', '_')}`;
      let item;
      if (name === 'wrong option') {
        const envelope = signEnvelope(makePayload(eventId, overrides), HMAC_KEY);
        const eventIdHash = sha256(eventId);
        await mkdir(join(root, 'inbox'), { recursive: true });
        await writeFile(join(root, 'inbox', `${eventIdHash}.json`), canonicalize(envelope));
        item = { envelope, eventIdHash };
      } else {
        item = await put(root, eventId, overrides);
      }
      assert.equal(await consumeCurrentReply({
        stateRoot: root,
        config: makeConfig(root),
        pendingDecision: makePending(),
        now: NOW,
      }), null);
      assert.deepEqual(await readdir(join(root, 'quarantine')), [`${item.eventIdHash}.json`]);
    });
  }

  await t.test('tampered signature and malformed json', async (t) => {
    const root = await mkdtemp(join(tmpdir(), 'tzg-consume-tamper-'));
    t.after(() => rm(root, { recursive: true, force: true }));
    const tampered = await put(root, 'evt_fake_tampered');
    const tamperedPath = join(root, 'inbox', `${tampered.eventIdHash}.json`);
    const parsed = JSON.parse(await readFile(tamperedPath, 'utf8'));
    parsed.payload.optionKey = 'B';
    await writeFile(tamperedPath, JSON.stringify(parsed));
    const malformedHash = '5'.repeat(64);
    await writeFile(join(root, 'inbox', `${malformedHash}.json`), '{');
    assert.equal(await consumeCurrentReply({
      stateRoot: root,
      config: makeConfig(root),
      pendingDecision: makePending(),
      now: NOW,
    }), null);
    assert.deepEqual((await readdir(join(root, 'quarantine'))).sort(), [
      `${malformedHash}.json`, `${tampered.eventIdHash}.json`,
    ].sort());
  });

  await t.test('filename does not match provider event hash', async (t) => {
    const root = await mkdtemp(join(tmpdir(), 'tzg-consume-name-'));
    t.after(() => rm(root, { recursive: true, force: true }));
    const actual = await put(root, 'evt_fake_name');
    const wrong = '6'.repeat(64);
    await mkdir(join(root, 'inbox'), { recursive: true });
    await writeFile(join(root, 'inbox', `${wrong}.json`), canonicalize(actual.envelope));
    await unlink(join(root, 'inbox', `${actual.eventIdHash}.json`));
    assert.equal(await consumeCurrentReply({
      stateRoot: root,
      config: makeConfig(root),
      pendingDecision: makePending(),
      now: NOW,
    }), null);
    assert.deepEqual(await readdir(join(root, 'quarantine')), [`${wrong}.json`]);
  });
});

test('consume.lock provides exclusive deterministic consumption under concurrency', async (t) => {
  const root = await mkdtemp(join(tmpdir(), 'tzg-consume-lock-'));
  t.after(() => rm(root, { recursive: true, force: true }));
  await put(root, 'evt_fake_lock');
  const args = {
    stateRoot: root,
    config: makeConfig(root),
    pendingDecision: makePending(),
    now: NOW,
  };
  const results = await Promise.all([
    consumeCurrentReply(args),
    consumeCurrentReply(args),
  ]);
  assert.equal(results.filter((value) => value?.result === 'OPTION_ACCEPTED').length, 1);
  assert.equal(results.filter((value) => value === null).length, 1);
  await assert.rejects(readFile(join(root, 'consume.lock'), 'utf8'));
});

test('consumer rejects non-current pending input without touching inbox', async (t) => {
  const root = await mkdtemp(join(tmpdir(), 'tzg-consume-pending-'));
  t.after(() => rm(root, { recursive: true, force: true }));
  const item = await put(root, 'evt_fake_pending');
  assert.equal(await consumeCurrentReply({
    stateRoot: root,
    config: makeConfig(root),
    pendingDecision: { ...makePending(), target: 'forbidden' },
    now: NOW,
  }), null);
  assert.deepEqual(await readdir(join(root, 'inbox')), [`${item.eventIdHash}.json`]);
});

function oneJsonLine(stdout) {
  const lines = stdout.trimEnd().split(/\r?\n/);
  assert.equal(lines.length, 1);
  return JSON.parse(lines[0]);
}

test('consume CLI emits exactly one whitelisted line for no reply, accepted, and invalid input', async (t) => {
  const root = await mkdtemp(join(tmpdir(), 'tzg-consume-cli-'));
  t.after(() => rm(root, { recursive: true, force: true }));
  const configPath = join(root, 'private.json');
  const requestPath = join(root, 'request.json');
  const cliNow = new Date();
  const cliPending = makePending({
    createdAt: new Date(cliNow.getTime() - 60_000).toISOString(),
    expiresAt: new Date(cliNow.getTime() + 60 * 60 * 1000).toISOString(),
  });
  await writeFile(configPath, JSON.stringify(makeConfig(root)));
  await writeFile(requestPath, JSON.stringify({ pendingDecision: cliPending }));
  const env = { ...process.env, FEISHU_DECISION_CONFIG_PATH: configPath };

  const none = spawnSync(process.execPath, [CLI_PATH, '--request-file', requestPath], {
    env, encoding: 'utf8',
  });
  assert.equal(none.status, 0);
  assert.deepEqual(oneJsonLine(none.stdout), { result: 'NO_REPLY' });
  assert.equal(none.stderr, '');

  const eventId = 'evt_fake_cli';
  const cliPayload = makePayload(eventId, { receivedAt: cliNow.toISOString() });
  const { envelope } = await put(root, eventId, { receivedAt: cliNow.toISOString() });
  const accepted = spawnSync(process.execPath, [CLI_PATH, '--request-file', requestPath], {
    env, encoding: 'utf8',
  });
  assert.equal(accepted.status, 0);
  assert.deepEqual(oneJsonLine(accepted.stdout), expectedAccepted(cliPayload, envelope));
  assert.equal(accepted.stderr, '');

  await writeFile(requestPath, JSON.stringify({ pendingDecision: cliPending, raw: APP_ID }));
  const invalid = spawnSync(process.execPath, [CLI_PATH, '--request-file', requestPath], {
    env, encoding: 'utf8',
  });
  assert.equal(invalid.status, 22);
  assert.deepEqual(oneJsonLine(invalid.stdout), { result: 'INVALID_INPUT' });
  assert.equal(invalid.stderr, '');
  for (const output of [none.stdout, accepted.stdout, invalid.stdout, invalid.stderr]) {
    assert.equal(output.includes('fake_secret_for_tests_only'), false);
    assert.equal(output.includes(OPERATOR_OPEN_ID), false);
    assert.equal(output.includes(TENANT_KEY), false);
  }
});

test('imported consume main does not auto-run and only returns whitelisted output', async () => {
  let stdout = '';
  const code = await consumeMain(['--request-file', 'relative.json'], {
    stdout: { write(chunk) { stdout += chunk; } },
    env: {},
  });
  assert.equal(code, 22);
  assert.deepEqual(oneJsonLine(stdout), { result: 'INVALID_INPUT' });
});

test('consume CLI whitelist accepts exact source-specific custom output and rejects mixed evidence', async (t) => {
  const root = await mkdtemp(join(tmpdir(), 'tzg-consume-custom-cli-'));
  t.after(() => rm(root, { recursive: true, force: true }));
  const configPath = join(root, 'private.json');
  const requestPath = join(root, 'request.json');
  await writeFile(configPath, JSON.stringify(makeConfig(root)));
  await writeFile(requestPath, JSON.stringify({ pendingDecision: makePending() }));
  const base = expectedCustomAccepted(
    makeCustomPayload('evt_custom_cli', '采用双通道', 'feishu_text'),
    signEnvelope(makeCustomPayload('evt_custom_cli', '采用双通道', 'feishu_text'), HMAC_KEY),
  );
  async function invoke(result) {
    let stdout = '';
    const code = await consumeMain(['--request-file', requestPath], {
      env: { FEISHU_DECISION_CONFIG_PATH: configPath },
      stdout: { write(chunk) { stdout += chunk; } },
      now: () => NOW,
      consume: async () => result,
    });
    return { code, value: oneJsonLine(stdout) };
  }
  assert.deepEqual(await invoke(base), { code: 0, value: base });
  assert.deepEqual(await invoke({ ...base, optionKey: 'A' }), {
    code: 22, value: { result: 'INVALID_INPUT' },
  });
  const wrongSourceEvidence = { ...base, source: 'feishu_card_input' };
  assert.deepEqual(await invoke(wrongSourceEvidence), {
    code: 22, value: { result: 'INVALID_INPUT' },
  });
});
