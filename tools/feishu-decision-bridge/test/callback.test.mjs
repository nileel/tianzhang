import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { mkdir, mkdtemp, readFile, readdir, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import test from 'node:test';

import { startBridge } from '../src/bridge.mjs';
import { handleCardAction, normalizeCardAction } from '../src/callback-core.mjs';
import { verifyEnvelope } from '../src/envelope.mjs';
import { acquireInstanceLock } from '../src/instance-lock.mjs';

const NOW = new Date('2026-07-16T08:00:00.000Z');
const APP_ID = 'app_fake_demo';
const APP_SECRET = 'fake_secret_for_tests_only';
const TENANT_KEY = 'tenant_fake_demo';
const OPERATOR_OPEN_ID = 'ou_fake_operator';
const MESSAGE_ID = 'om_fake_message';
const EVENT_ID = 'evt_fake_event';
const DECISION_ID = 'DEC-20260716-FAKE0001';
const CARD_NONCE = 'nonce_fake_card';
const PAIRING_NONCE = 'nonce_fake_pairing';
const HMAC_KEY = Buffer.alloc(32, 0x42).toString('base64');

function sha256(value) {
  return createHash('sha256').update(value, 'utf8').digest('hex');
}

function makeConfig(stateRoot, overrides = {}) {
  return {
    schemaVersion: 1,
    appId: APP_ID,
    appSecret: APP_SECRET,
    recipient: { type: 'open_id', value: 'ou_fake_recipient' },
    expectedTenantKey: TENANT_KEY,
    pairedOperatorOpenIdHash: sha256(OPERATOR_OPEN_ID),
    hmacKey: HMAC_KEY,
    stateRoot,
    ...overrides,
  };
}

function micros(date = NOW) {
  return (BigInt(date.getTime()) * 1000n).toString();
}

function makeEvent(overrides = {}) {
  const value = overrides.value ?? {
    kind: 'decision_reply',
    decisionId: DECISION_ID,
    optionKey: 'A',
    cardNonce: CARD_NONCE,
  };
  return {
    schema: '2.0',
    header: {
      event_id: EVENT_ID,
      create_time: micros(),
      event_type: 'card.action.trigger',
      tenant_key: TENANT_KEY,
      app_id: APP_ID,
      ...(overrides.header ?? {}),
    },
    event: {
      operator: {
        tenant_key: TENANT_KEY,
        open_id: OPERATOR_OPEN_ID,
        ...(overrides.operator ?? {}),
      },
      action: {
        tag: 'button',
        value,
        ...(overrides.action ?? {}),
      },
      context: {
        open_message_id: MESSAGE_ID,
        ...(overrides.context ?? {}),
      },
      ...(overrides.event ?? {}),
    },
    ...(overrides.root ?? {}),
  };
}

function flattenLikeSdk(rawEvent = makeEvent()) {
  const { header, event, ...rest } = rawEvent;
  return {
    [Symbol('event-type')]: header.event_type,
    event_type: header.event_type,
    ...rest,
    ...header,
    ...event,
  };
}

function makeBinding(overrides = {}) {
  return {
    kind: 'decision_reply',
    decisionId: DECISION_ID,
    allowedOptions: ['A', 'B', 'C'],
    allowCustomReply: true,
    issuedAt: new Date(NOW.getTime() - 60_000).toISOString(),
    expiresAt: new Date(NOW.getTime() + 60_000).toISOString(),
    cardNonceHash: sha256(CARD_NONCE),
    providerMessageIdHash: sha256(MESSAGE_ID),
    providerChatIdHash: sha256('oc_fake_chat'),
    ...overrides,
  };
}

function makeCustomEvent(customText = '  采用双通道\r\n保留旧字段  ', overrides = {}) {
  return makeEvent({
    value: {
      kind: 'decision_custom_reply',
      decisionId: DECISION_ID,
      cardNonce: CARD_NONCE,
      ...(overrides.value ?? {}),
    },
    action: {
      name: 'submitCustomDecision',
      form_value: { customDecision: customText },
      ...(overrides.action ?? {}),
    },
    ...(overrides.eventOverrides ?? {}),
  });
}

function rejectedResponse() {
  return {
    toast: {
      type: 'warning',
      content: '未登记或已过期',
    },
  };
}

function containsInteractiveAction(value) {
  if (Array.isArray(value)) {
    return value.some(containsInteractiveAction);
  }
  if (value === null || typeof value !== 'object') {
    return false;
  }
  return value.tag === 'button'
    || value.tag === 'action'
    || Object.values(value).some(containsInteractiveAction);
}

test('bridge instance lock rejects a live owner and reclaims a dead owner once', async (t) => {
  const root = await mkdtemp(join(tmpdir(), 'tzg-bridge-instance-lock-'));
  t.after(() => rm(root, { recursive: true, force: true }));

  await t.test('live owner rejects a second bridge', async () => {
    const first = await acquireInstanceLock({
      stateRoot: root,
      pid: 101,
      processProbe: async (pid) => pid === 101,
    });
    await assert.rejects(acquireInstanceLock({
      stateRoot: root,
      pid: 202,
      processProbe: async (pid) => pid === 101,
    }), /Bridge already running/);
    await first.release();
  });

  await t.test('dead owner is reclaimed once', async () => {
    await writeFile(join(root, 'bridge-instance.lock'), '{"schemaVersion":1,"pid":101}', 'utf8');
    const lock = await acquireInstanceLock({
      stateRoot: root,
      pid: 202,
      processProbe: async () => false,
    });
    assert.deepEqual(JSON.parse(await readFile(join(root, 'bridge-instance.lock'), 'utf8')), {
      schemaVersion: 1,
      pid: 202,
    });
    await lock.release();
    await assert.rejects(readFile(join(root, 'bridge-instance.lock'), 'utf8'), { code: 'ENOENT' });
  });
});

test('normalizeCardAction accepts only a complete schema 2.0 data envelope', () => {
  const normalized = normalizeCardAction(makeEvent());
  assert.deepEqual(Object.keys(normalized).sort(), [
    'action', 'appId', 'createTimeMs', 'eventId', 'eventType', 'headerTenantKey',
    'messageId', 'operatorOpenId', 'operatorTenantKey',
  ]);
  assert.deepEqual(normalized.action, {
    kind: 'decision_reply',
    decisionId: DECISION_ID,
    optionKey: 'A',
    cardNonce: CARD_NONCE,
  });
  assert.equal(normalized.createTimeMs, NOW.getTime());
  assert.equal(Object.hasOwn(normalized, 'raw'), false);
});

test('normalizeCardAction accepts the exact schema 2.0 shape flattened by the Feishu SDK', () => {
  const normalized = normalizeCardAction(flattenLikeSdk());
  assert.deepEqual(normalized.action, {
    kind: 'decision_reply',
    decisionId: DECISION_ID,
    optionKey: 'A',
    cardNonce: CARD_NONCE,
  });
  assert.equal(normalized.appId, APP_ID);
  assert.equal(normalized.createTimeMs, NOW.getTime());
  assert.equal(normalized.headerTenantKey, TENANT_KEY);
  assert.equal(normalized.operatorTenantKey, TENANT_KEY);
  assert.equal(normalized.operatorOpenId, OPERATOR_OPEN_ID);
  assert.equal(normalized.messageId, MESSAGE_ID);
});

test('normalizeCardAction accepts documented optional callback fields after SDK flattening', () => {
  const event = makeEvent({
    header: { token: 'verification_token_fixture' },
    operator: {
      user_id: 'user_fixture',
      union_id: 'union_fixture',
    },
    action: {
      timezone: 'Asia/Shanghai',
      form_value: {},
      name: 'PairButton_fixture',
    },
    context: { open_chat_id: 'oc_fixture_chat' },
    event: {
      token: 'card_update_token_fixture',
      host: 'im_message',
    },
  });
  const normalized = normalizeCardAction(flattenLikeSdk(event));
  assert.equal(normalized.appId, APP_ID);
  assert.equal(normalized.operatorOpenId, OPERATOR_OPEN_ID);
  assert.equal(normalized.messageId, MESSAGE_ID);
  assert.deepEqual(normalized.action, {
    kind: 'decision_reply',
    decisionId: DECISION_ID,
    optionKey: 'A',
    cardNonce: CARD_NONCE,
  });
});

test('normalizeCardAction accepts an exact SDK-flattened custom decision form', () => {
  const normalized = normalizeCardAction(flattenLikeSdk(makeCustomEvent()));
  assert.deepEqual(normalized.action, {
    kind: 'decision_custom_reply',
    decisionId: DECISION_ID,
    customText: '采用双通道\n保留旧字段',
    cardNonce: CARD_NONCE,
  });
});

test('custom decision forms reject malformed, accessor, blank, long, and unsafe content', async (t) => {
  const accessor = makeCustomEvent();
  Object.defineProperty(accessor.event.action.form_value, 'customDecision', {
    enumerable: true,
    get() { throw new Error('custom getter must not execute'); },
  });
  for (const [name, event] of [
    ['missing form field', makeCustomEvent('ok', { action: { form_value: {} } })],
    ['extra form field', makeCustomEvent('ok', { action: { form_value: { customDecision: 'ok', extra: 'no' } } })],
    ['wrong submit name', makeCustomEvent('ok', { action: { name: 'wrongName' } })],
    ['blank content', makeCustomEvent('   ')],
    ['long content', makeCustomEvent('x'.repeat(1001))],
    ['unsafe content', makeCustomEvent('ok\u0000bad')],
    ['accessor content', accessor],
  ]) {
    await t.test(name, () => {
      assert.throws(() => normalizeCardAction(event), /Invalid card action/);
    });
  }
});

test('normalizeCardAction rejects legacy, incomplete flattened, accessor, extra-value, and unsafe inputs', async (t) => {
  const oldSchema = makeEvent();
  oldSchema.schema = '1.0';
  const flattened = {
    schema: '2.0',
    event_id: EVENT_ID,
    event_type: 'card.action.trigger',
    open_id: OPERATOR_OPEN_ID,
    value: makeEvent().event.action.value,
  };
  const extraValue = makeEvent({
    value: {
      kind: 'decision_reply',
      decisionId: DECISION_ID,
      optionKey: 'A',
      cardNonce: CARD_NONCE,
      target: 'must-not-be-accepted',
    },
  });
  const unsafe = makeEvent({ header: { event_id: 'evt_fake\nleak' } });
  const extraEnvelopeField = makeEvent({ root: { token: 'must-not-be-accepted' } });
  const getter = makeEvent();
  Object.defineProperty(getter.event.operator, 'open_id', {
    enumerable: true,
    get() { throw new Error('getter must not execute'); },
  });

  for (const [name, event] of [
    ['old schema', oldSchema],
    ['flattened', flattened],
    ['extra value field', extraValue],
    ['unsafe identifier', unsafe],
    ['extra envelope field', extraEnvelopeField],
    ['getter', getter],
  ]) {
    await t.test(name, () => {
      assert.throws(() => normalizeCardAction(event), /Invalid card action/);
    });
  }
});

test('rejection paths are generic and never create inbox evidence', async (t) => {
  const cases = [
    ['wrong app', makeEvent({ header: { app_id: 'app_fake_other' } }), makeBinding()],
    ['wrong header tenant', makeEvent({ header: { tenant_key: 'tenant_fake_other' } }), makeBinding()],
    ['operator/header tenant mismatch', makeEvent({ operator: { tenant_key: 'tenant_fake_other' } }), makeBinding()],
    ['wrong operator', makeEvent({ operator: { open_id: 'ou_fake_other' } }), makeBinding()],
    ['expired binding', makeEvent(), makeBinding({ expiresAt: new Date(NOW.getTime() - 1).toISOString() })],
    ['future event', makeEvent({ header: { create_time: micros(new Date(NOW.getTime() + 1)) } }), makeBinding()],
    ['before issued time', makeEvent(), makeBinding({ issuedAt: new Date(NOW.getTime() + 1).toISOString() })],
    ['wrong nonce', makeEvent({ value: { kind: 'decision_reply', decisionId: DECISION_ID, optionKey: 'A', cardNonce: 'nonce_fake_other' } }), makeBinding()],
    ['unknown option', makeEvent({ value: { kind: 'decision_reply', decisionId: DECISION_ID, optionKey: 'D', cardNonce: CARD_NONCE } }), makeBinding()],
    ['wrong decision', makeEvent({ value: { kind: 'decision_reply', decisionId: 'DEC-20260716-FAKE0002', optionKey: 'A', cardNonce: CARD_NONCE } }), makeBinding()],
    ['wrong message', makeEvent({ context: { open_message_id: 'om_fake_other' } }), makeBinding()],
    ['legacy schema', { ...makeEvent(), schema: '1.0' }, makeBinding()],
    ['flattened event', { schema: '2.0', event_id: EVENT_ID }, makeBinding()],
    ['extra action value', makeEvent({ value: { ...makeEvent().event.action.value, debug: true } }), makeBinding()],
  ];

  for (const [name, event, binding] of cases) {
    await t.test(name, async (t) => {
      const root = await mkdtemp(join(tmpdir(), 'tzg-callback-reject-'));
      t.after(() => rm(root, { recursive: true, force: true }));
      const result = await handleCardAction({
        event,
        config: makeConfig(root),
        pendingBindings: [binding],
        now: NOW,
      });
      assert.deepEqual(result, { accepted: false, response: rejectedResponse() });
      await assert.rejects(readdir(join(root, 'inbox')));
      assert.equal(JSON.stringify(result).includes(APP_ID), false);
      assert.equal(JSON.stringify(result).includes(TENANT_KEY), false);
      assert.equal(JSON.stringify(result).includes(OPERATOR_OPEN_ID), false);
    });
  }
});

test('valid decision callback writes one signed hash-only envelope and a read-only card', async (t) => {
  const root = await mkdtemp(join(tmpdir(), 'tzg-callback-accept-'));
  t.after(() => rm(root, { recursive: true, force: true }));
  const request = {
    event: makeEvent(),
    config: makeConfig(root),
    pendingBindings: [makeBinding({ kind: 'decision_reply' })],
    now: NOW,
  };
  const first = await handleCardAction(request);
  const replay = await handleCardAction(request);
  assert.equal(first.accepted, true);
  assert.equal(replay.accepted, true);
  assert.deepEqual(Object.keys(first).sort(), ['accepted', 'response']);
  assert.equal(first.response.toast.type, 'success');
  assert.match(first.response.toast.content, /已选择 A/);
  assert.equal(first.response.card.type, 'raw');
  assert.match(JSON.stringify(first.response.card.data), /已选择 A/);
  assert.match(JSON.stringify(first.response.card.data), /登记时间/);
  assert.equal(containsInteractiveAction(first.response.card.data), false);
  assert.equal(Object.hasOwn(first, 'envelope'), false);

  const names = await readdir(join(root, 'inbox'));
  assert.deepEqual(names, [`${sha256(EVENT_ID)}.json`]);
  const raw = await readFile(join(root, 'inbox', names[0]), 'utf8');
  const envelope = JSON.parse(raw);
  const payload = verifyEnvelope(envelope, HMAC_KEY);
  assert.deepEqual(Object.keys(payload).sort(), [
    'cardNonceHash', 'decisionId', 'kind', 'operatorOpenIdHash', 'optionKey',
    'providerEventIdHash', 'providerMessageIdHash', 'receivedAt', 'tenantKeyHash',
  ]);
  assert.deepEqual(payload, {
    kind: 'decision_reply',
    decisionId: DECISION_ID,
    optionKey: 'A',
    cardNonceHash: sha256(CARD_NONCE),
    providerMessageIdHash: sha256(MESSAGE_ID),
    providerEventIdHash: sha256(EVENT_ID),
    operatorOpenIdHash: sha256(OPERATOR_OPEN_ID),
    tenantKeyHash: sha256(TENANT_KEY),
    receivedAt: NOW.toISOString(),
  });
  for (const forbidden of [APP_ID, APP_SECRET, TENANT_KEY, OPERATOR_OPEN_ID, MESSAGE_ID, EVENT_ID, CARD_NONCE]) {
    assert.equal(raw.includes(forbidden), false);
  }
  assert.equal((await readdir(join(root, 'inbox'))).filter((name) => name.includes('.tmp')).length, 0);
});

test('valid custom form callback writes one signed envelope and a read-only confirmation', async (t) => {
  const root = await mkdtemp(join(tmpdir(), 'tzg-callback-custom-accept-'));
  t.after(() => rm(root, { recursive: true, force: true }));
  const event = makeCustomEvent();
  const request = {
    event,
    config: makeConfig(root),
    pendingBindings: [makeBinding()],
    now: NOW,
  };
  const first = await handleCardAction(request);
  const replay = await handleCardAction(request);
  assert.equal(first.accepted, true);
  assert.equal(replay.accepted, true);
  assert.equal(first.response.toast.type, 'success');
  assert.match(first.response.toast.content, /已登记自定义方案/u);
  assert.match(JSON.stringify(first.response.card.data), /已登记自定义方案/u);
  assert.match(JSON.stringify(first.response.card.data), new RegExp(DECISION_ID, 'u'));
  assert.match(JSON.stringify(first.response.card.data), /采用双通道\\n保留旧字段/u);
  assert.equal(containsInteractiveAction(first.response.card.data), false);

  const names = await readdir(join(root, 'inbox'));
  assert.deepEqual(names, [`${sha256(EVENT_ID)}.json`]);
  const raw = await readFile(join(root, 'inbox', names[0]), 'utf8');
  const payload = verifyEnvelope(JSON.parse(raw), HMAC_KEY);
  assert.deepEqual(payload, {
    kind: 'decision_custom_reply',
    decisionId: DECISION_ID,
    customText: '采用双通道\n保留旧字段',
    cardNonceHash: sha256(CARD_NONCE),
    providerMessageIdHash: sha256(MESSAGE_ID),
    providerEventIdHash: sha256(EVENT_ID),
    operatorOpenIdHash: sha256(OPERATOR_OPEN_ID),
    tenantKeyHash: sha256(TENANT_KEY),
    receivedAt: NOW.toISOString(),
    source: 'feishu_card_input',
  });
  for (const forbidden of [APP_ID, APP_SECRET, TENANT_KEY, OPERATOR_OPEN_ID, MESSAGE_ID, EVENT_ID, CARD_NONCE]) {
    assert.equal(raw.includes(forbidden), false);
  }
});

test('custom form callback rejects binding, nonce, identity, and expiry mismatches', async (t) => {
  for (const [name, event, binding] of [
    ['custom disabled', makeCustomEvent(), makeBinding({ allowCustomReply: false })],
    ['wrong nonce', makeCustomEvent('ok', { value: { cardNonce: 'nonce_fake_other' } }), makeBinding()],
    ['wrong identity', makeCustomEvent('ok', {
      eventOverrides: { operator: { open_id: 'ou_fake_other' } },
    }), makeBinding()],
    ['expired', makeCustomEvent(), makeBinding({
      expiresAt: new Date(NOW.getTime() - 1).toISOString(),
    })],
  ]) {
    await t.test(name, async (t) => {
      const root = await mkdtemp(join(tmpdir(), 'tzg-callback-custom-reject-'));
      t.after(() => rm(root, { recursive: true, force: true }));
      const result = await handleCardAction({
        event,
        config: makeConfig(root),
        pendingBindings: [binding],
        now: NOW,
      });
      assert.deepEqual(result, { accepted: false, response: rejectedResponse() });
      await assert.rejects(readdir(join(root, 'inbox')));
    });
  }
});

test('operator pairing uses its own inbox and only the tenant key is retained raw', async (t) => {
  const root = await mkdtemp(join(tmpdir(), 'tzg-callback-pairing-'));
  t.after(() => rm(root, { recursive: true, force: true }));
  const event = makeEvent({
    value: { kind: 'operator_pairing', pairingNonce: PAIRING_NONCE },
  });
  const config = makeConfig(root, {
    expectedTenantKey: null,
    pairedOperatorOpenIdHash: null,
  });
  const result = await handleCardAction({
    event,
    config,
    pendingBindings: [{
      kind: 'operator_pairing',
      pairingNonceHash: sha256(PAIRING_NONCE),
      expiresAt: new Date(NOW.getTime() + 60_000).toISOString(),
    }],
    now: NOW,
  });
  assert.equal(result.accepted, true);
  assert.equal(JSON.stringify(result.response).includes('已选择'), false);
  assert.match(result.response.toast.content, /配对/);
  await assert.rejects(readdir(join(root, 'inbox')));
  const names = await readdir(join(root, 'pairing-inbox'));
  const raw = await readFile(join(root, 'pairing-inbox', names[0]), 'utf8');
  const payload = verifyEnvelope(JSON.parse(raw), HMAC_KEY);
  assert.deepEqual(Object.keys(payload).sort(), [
    'kind', 'operatorOpenIdHash', 'pairingNonceHash', 'providerEventIdHash',
    'receivedAt', 'tenantKey', 'tenantKeyHash',
  ]);
  assert.equal(payload.kind, 'operator_pairing');
  assert.equal(payload.tenantKey, TENANT_KEY);
  assert.equal(payload.operatorOpenIdHash, sha256(OPERATOR_OPEN_ID));
  assert.equal(payload.providerEventIdHash, sha256(EVENT_ID));
  assert.equal(payload.pairingNonceHash, sha256(PAIRING_NONCE));
  for (const forbidden of [APP_ID, APP_SECRET, OPERATOR_OPEN_ID, MESSAGE_ID, EVENT_ID, PAIRING_NONCE]) {
    assert.equal(raw.includes(forbidden), false);
  }
});

test('bridge registers the exact callback, waits for ready, heartbeats, disconnects, reconnects, and sanitizes logs', async (t) => {
  const root = await mkdtemp(join(tmpdir(), 'tzg-bridge-runtime-'));
  t.after(() => rm(root, { recursive: true, force: true }));
  const configPath = join(root, 'private.json');
  await writeFile(configPath, JSON.stringify(makeConfig(root, {
    expectedTenantKey: null,
    pairedOperatorOpenIdHash: null,
  })));
  await writeFile(join(root, 'pending-bindings.json'), JSON.stringify([makeBinding()]));

  const dispatcherArguments = [];
  let registered;
  class FakeEventDispatcher {
    constructor(options) { dispatcherArguments.push(options); }
    register(mapping) { registered = mapping; return this; }
  }
  let clientOptions;
  let startOptions;
  const listeners = new Map();
  class FakeWSClient {
    constructor(options) { clientOptions = options; }
    on(name, handler) { listeners.set(name, handler); }
    async start(options) {
      startOptions = options;
      clientOptions.logger.info({ nested: ['[ws]', 'ws client ready'] });
    }
  }
  let intervalCallback;
  let clearedInterval = null;
  const timers = {
    setInterval(callback, delay) {
      assert.equal(delay, 60_000);
      intervalCallback = callback;
      return 73;
    },
    clearInterval(id) { clearedInterval = id; },
    setTimeout,
    clearTimeout,
  };
  const logLines = [];
  const logger = Object.fromEntries(['debug', 'info', 'warn', 'error'].map((level) => [
    level,
    (...args) => logLines.push(`${level}:${args.map(String).join(' ')}`),
  ]));
  let now = NOW;
  const messageReplies = [];
  const runtime = await startBridge({
    env: { FEISHU_DECISION_CONFIG_PATH: configPath },
    EventDispatcher: FakeEventDispatcher,
    WSClient: FakeWSClient,
    timers,
    logger,
    now: () => now,
    pid: 4242,
    messageReplyTransport: async (messageId, text) => {
      messageReplies.push({ messageId, text });
    },
  });

  assert.deepEqual(dispatcherArguments, [{}]);
  assert.deepEqual(Object.keys(registered).sort(), [
    'card.action.trigger', 'im.message.receive_v1',
  ]);
  assert.deepEqual(startOptions, { eventDispatcher: runtime.eventDispatcher });
  assert.equal(clientOptions.appId, APP_ID);
  assert.equal(clientOptions.appSecret, APP_SECRET);
  assert.equal(typeof clientOptions.logger.info, 'function');
  assert.deepEqual(JSON.parse(await readFile(join(root, 'health.json'), 'utf8')), {
    schemaVersion: 1,
    status: 'CONNECTED',
    pid: 4242,
    updatedAt: NOW.toISOString(),
    appIdHash: sha256(APP_ID),
  });
  assert.deepEqual(JSON.parse(await readFile(join(root, 'bridge-instance.lock'), 'utf8')), {
    schemaVersion: 1,
    pid: 4242,
  });

  await writeFile(configPath, JSON.stringify(makeConfig(root)));
  const callbackResult = await registered['card.action.trigger'](makeEvent());
  assert.equal(callbackResult.toast.type, 'success');
  assert.equal((await readdir(join(root, 'inbox'))).length, 1);

  const textEvent = {
    [Symbol('event-type')]: 'im.message.receive_v1',
    schema: '2.0',
    event_id: 'evt_fake_text_event',
    event_type: 'im.message.receive_v1',
    create_time: String(NOW.getTime()),
    token: 'verification-token-fixture',
    app_id: APP_ID,
    tenant_key: TENANT_KEY,
    sender: {
      sender_id: { open_id: OPERATOR_OPEN_ID },
      sender_type: 'user',
      tenant_key: TENANT_KEY,
    },
    message: {
      message_id: 'om_fake_text_message',
      create_time: String(NOW.getTime()),
      chat_id: 'oc_fake_chat',
      chat_type: 'p2p',
      message_type: 'text',
      content: JSON.stringify({ text: `${DECISION_ID}：自定义 采用文字方案` }),
    },
  };
  await registered['im.message.receive_v1'](textEvent);
  assert.equal((await readdir(join(root, 'inbox'))).length, 2);
  assert.deepEqual(messageReplies, [{
    messageId: 'om_fake_text_message',
    text: `已登记 ${DECISION_ID} 自定义方案：\n采用文字方案`,
  }]);

  await registered['im.message.receive_v1']({
    ...textEvent,
    unexpected: `${APP_ID}-${TENANT_KEY}-${OPERATOR_OPEN_ID}-${MESSAGE_ID}`,
  });
  assert.equal(logLines.some((line) => line.includes('message_rejected:invalid_shape')), true);
  assert.equal(logLines.some((line) => (
    line.includes('message_shape:root=app_id,create_time,event_id,event_type,message,schema,sender,tenant_key,token,unexpected,@symbol:event-type')
    && line.includes(';sender=sender_id,sender_type,tenant_key')
    && line.includes(';sender_id=open_id')
    && line.includes(';message=chat_id,chat_type,content,create_time,message_id,message_type')
  )), true);

  const invalidCallbackResult = await registered['card.action.trigger']({
    ...makeEvent(),
    unexpected: `${APP_ID}-${TENANT_KEY}-${OPERATOR_OPEN_ID}-${MESSAGE_ID}`,
  });
  assert.deepEqual(invalidCallbackResult, rejectedResponse());
  assert.equal(logLines.some((line) => line.includes('callback_rejected:invalid_shape')), true);
  assert.equal(logLines.some((line) => (
    line.includes('callback_shape:root=event,header,schema,unexpected')
    && line.includes(';header=app_id,create_time,event_id,event_type,tenant_key')
    && line.includes(';event=action,context,operator')
  )), true);

  now = new Date(NOW.getTime() + 60_000);
  await intervalCallback();
  await runtime.flush();
  assert.equal(JSON.parse(await readFile(join(root, 'health.json'), 'utf8')).updatedAt, now.toISOString());

  listeners.get('disconnect')?.(new Error(`${APP_SECRET} ${TENANT_KEY} ${OPERATOR_OPEN_ID}`));
  await runtime.flush();
  assert.equal(JSON.parse(await readFile(join(root, 'health.json'), 'utf8')).status, 'DISCONNECTED');
  clientOptions.logger.info('[ws]', { message: 'ws client ready' });
  await runtime.flush();
  assert.equal(JSON.parse(await readFile(join(root, 'health.json'), 'utf8')).status, 'CONNECTED');
  clientOptions.logger.error(new Error(`${APP_ID} ${APP_SECRET} ou_fake_recipient ${TENANT_KEY}`));
  clientOptions.logger.error(new Error(`${OPERATOR_OPEN_ID} ${MESSAGE_ID}`));
  assert.equal(logLines.join('\n').includes(APP_ID), false);
  assert.equal(logLines.join('\n').includes(APP_SECRET), false);
  assert.equal(logLines.join('\n').includes('ou_fake_recipient'), false);
  assert.equal(logLines.join('\n').includes(TENANT_KEY), false);
  assert.equal(logLines.join('\n').includes(OPERATOR_OPEN_ID), false);
  assert.equal(logLines.join('\n').includes(MESSAGE_ID), false);
  assert.equal((await readdir(root)).some((name) => name.includes('.tmp')), false);

  await runtime.shutdown();
  assert.equal(clearedInterval, 73);
  assert.equal(JSON.parse(await readFile(join(root, 'health.json'), 'utf8')).status, 'DISCONNECTED');
  await assert.rejects(readFile(join(root, 'bridge-instance.lock'), 'utf8'), { code: 'ENOENT' });
});

test('bridge does not claim CONNECTED without the official ready log and records start failure', async (t) => {
  await t.test('no ready log', async (t) => {
    const root = await mkdtemp(join(tmpdir(), 'tzg-bridge-connecting-'));
    t.after(() => rm(root, { recursive: true, force: true }));
    const configPath = join(root, 'private.json');
    await writeFile(configPath, JSON.stringify(makeConfig(root)));
    class Dispatcher { register() { return this; } }
    class Client { on() {} async start() {} }
    const runtime = await startBridge({
      env: { FEISHU_DECISION_CONFIG_PATH: configPath },
      EventDispatcher: Dispatcher,
      WSClient: Client,
      timers: { setInterval: () => 1, clearInterval() {}, setTimeout, clearTimeout },
      logger: { info() {}, warn() {}, error() {}, debug() {} },
      now: () => NOW,
    });
    assert.equal(JSON.parse(await readFile(join(root, 'health.json'), 'utf8')).status, 'CONNECTING');
    await runtime.shutdown();
  });

  await t.test('start rejects', async (t) => {
    const root = await mkdtemp(join(tmpdir(), 'tzg-bridge-disconnected-'));
    t.after(() => rm(root, { recursive: true, force: true }));
    const configPath = join(root, 'private.json');
    await writeFile(configPath, JSON.stringify(makeConfig(root)));
    class Dispatcher { register() { return this; } }
    class Client { on() {} async start() { throw new Error(APP_SECRET); } }
    await assert.rejects(startBridge({
      env: { FEISHU_DECISION_CONFIG_PATH: configPath },
      EventDispatcher: Dispatcher,
      WSClient: Client,
      timers: { setInterval: () => 1, clearInterval() {}, setTimeout, clearTimeout },
      logger: { info() {}, warn() {}, error() {}, debug() {} },
      now: () => NOW,
    }), /Bridge unavailable/);
    assert.equal(JSON.parse(await readFile(join(root, 'health.json'), 'utf8')).status, 'DISCONNECTED');
    await assert.rejects(readFile(join(root, 'bridge-instance.lock'), 'utf8'), { code: 'ENOENT' });
  });
});
