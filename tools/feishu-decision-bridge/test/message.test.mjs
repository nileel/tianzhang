import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { mkdtemp, readFile, readdir, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import test from 'node:test';

import { verifyEnvelope } from '../src/envelope.mjs';
import { handleDecisionTextMessage, normalizeMessageEvent } from '../src/message-core.mjs';
import { createMessageReplyTransport } from '../src/message-runtime.mjs';

const NOW = new Date('2026-07-16T08:00:00.000Z');
const APP_ID = 'app_message_fixture';
const APP_SECRET = 'message_secret_fixture';
const TENANT_KEY = 'tenant-message-fixture';
const OPEN_ID = 'ou-message-fixture';
const CHAT_ID = 'oc-message-fixture';
const MESSAGE_ID = 'om-message-fixture';
const EVENT_ID = 'evt-message-fixture';
const DECISION_ID = 'DEC-20260716-MESSAGEFIXTURE';
const HMAC_KEY = Buffer.alloc(32, 0x43).toString('base64');
const SDK_EVENT_TYPE = Symbol('event-type');

function sha256(value) {
  return createHash('sha256').update(value, 'utf8').digest('hex');
}

function makeConfig(stateRoot, overrides = {}) {
  return {
    schemaVersion: 1,
    appId: APP_ID,
    appSecret: APP_SECRET,
    recipient: { type: 'open_id', value: 'ou-recipient-fixture' },
    expectedTenantKey: TENANT_KEY,
    pairedOperatorOpenIdHash: sha256(OPEN_ID),
    hmacKey: HMAC_KEY,
    stateRoot,
    ...overrides,
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
    cardNonceHash: 'a'.repeat(64),
    providerMessageIdHash: 'b'.repeat(64),
    providerChatIdHash: sha256(CHAT_ID),
    ...overrides,
  };
}

function makeEvent(text = `${DECISION_ID}：自定义 采用双通道`, overrides = {}) {
  return {
    [SDK_EVENT_TYPE]: 'im.message.receive_v1',
    schema: '2.0',
    event_id: EVENT_ID,
    event_type: 'im.message.receive_v1',
    create_time: String(NOW.getTime()),
    token: 'verification-token-fixture',
    app_id: APP_ID,
    tenant_key: TENANT_KEY,
    sender: {
      sender_id: {
        union_id: 'on-message-fixture',
        user_id: 'user-message-fixture',
        open_id: OPEN_ID,
      },
      sender_type: 'user',
      tenant_key: TENANT_KEY,
      ...(overrides.sender ?? {}),
    },
    message: {
      message_id: MESSAGE_ID,
      create_time: String(NOW.getTime()),
      chat_id: CHAT_ID,
      chat_type: 'p2p',
      message_type: 'text',
      content: JSON.stringify({ text }),
      ...(overrides.message ?? {}),
    },
    ...(overrides.root ?? {}),
  };
}

test('normalizeMessageEvent snapshots only the exact SDK 1.71.1 text shape', async (t) => {
  const normalized = normalizeMessageEvent(makeEvent());
  assert.deepEqual(normalized, {
    eventId: EVENT_ID,
    eventType: 'im.message.receive_v1',
    tenantKey: TENANT_KEY,
    appId: APP_ID,
    openId: OPEN_ID,
    messageId: MESSAGE_ID,
    createdAtMs: NOW.getTime(),
    chatId: CHAT_ID,
    text: `${DECISION_ID}：自定义 采用双通道`,
  });

  const accessor = makeEvent();
  Object.defineProperty(accessor.message, 'content', {
    enumerable: true,
    get() { throw new Error('message getter must not execute'); },
  });
  for (const [name, event] of [
    ['unknown root field', makeEvent('ok', { root: { extra: true } })],
    ['unknown sender field', makeEvent('ok', { sender: { extra: true } })],
    ['unknown message field', makeEvent('ok', { message: { extra: true } })],
    ['wrong event type', makeEvent('ok', { root: { event_type: 'other' } })],
    ['bot sender', makeEvent('ok', { sender: { sender_type: 'app' } })],
    ['non-text', makeEvent('ok', { message: { message_type: 'image' } })],
    ['group chat', makeEvent('ok', { message: { chat_type: 'group' } })],
    ['invalid content json', makeEvent('ok', { message: { content: '{' } })],
    ['extra content field', makeEvent('ok', { message: { content: JSON.stringify({ text: 'ok', extra: true }) } })],
    ['accessor', accessor],
  ]) {
    await t.test(name, () => assert.equal(normalizeMessageEvent(event), null));
  }

  const optionalSdkFields = makeEvent('ok', {
    message: {
      root_id: 'om-root-fixture',
      parent_id: 'om-parent-fixture',
      update_time: String(NOW.getTime()),
      thread_id: 'omt-thread-fixture',
      mentions: [],
      user_agent: 'desktop',
      lark_agent_context: { active_chat_id: CHAT_ID },
    },
  });
  assert.equal(normalizeMessageEvent(optionalSdkFields)?.text, 'ok');
});

test('valid strict text writes a signed custom envelope and confirms once', async (t) => {
  const root = await mkdtemp(join(tmpdir(), 'tzg-message-valid-'));
  t.after(() => rm(root, { recursive: true, force: true }));
  const replies = [];
  const request = {
    event: makeEvent(`${DECISION_ID}: 自定义  采用双通道\r\n保留旧字段  `),
    config: makeConfig(root),
    pendingBindings: [makeBinding()],
    now: NOW,
    replyText: async (messageId, text) => replies.push({ messageId, text }),
  };
  const first = await handleDecisionTextMessage(request);
  const replay = await handleDecisionTextMessage(request);
  assert.deepEqual(first, { accepted: true, rejectionCode: null });
  assert.deepEqual(replay, { accepted: true, rejectionCode: null });
  assert.deepEqual(replies, [{
    messageId: MESSAGE_ID,
    text: `已登记 ${DECISION_ID} 自定义方案：\n采用双通道\n保留旧字段`,
  }]);

  const names = await readdir(join(root, 'inbox'));
  assert.deepEqual(names, [`${sha256(EVENT_ID)}.json`]);
  const raw = await readFile(join(root, 'inbox', names[0]), 'utf8');
  const payload = verifyEnvelope(JSON.parse(raw), HMAC_KEY);
  assert.deepEqual(payload, {
    kind: 'decision_custom_reply',
    decisionId: DECISION_ID,
    customText: '采用双通道\n保留旧字段',
    providerMessageIdHash: makeBinding().providerMessageIdHash,
    providerEventIdHash: sha256(EVENT_ID),
    operatorOpenIdHash: sha256(OPEN_ID),
    tenantKeyHash: sha256(TENANT_KEY),
    providerChatIdHash: sha256(CHAT_ID),
    receivedAt: NOW.toISOString(),
    source: 'feishu_text',
  });
  for (const secret of [APP_ID, APP_SECRET, TENANT_KEY, OPEN_ID, CHAT_ID, MESSAGE_ID, EVENT_ID]) {
    assert.equal(raw.includes(secret), false);
  }
});

test('message binding rejects wrong identity, chat, tenant, decision, time, and permissions silently', async (t) => {
  for (const [name, event, configOverride, bindingOverride] of [
    ['wrong operator', makeEvent('ok', { sender: { sender_id: { open_id: 'ou-other' } } }), {}, {}],
    ['wrong chat', makeEvent('ok', { message: { chat_id: 'oc-other' } }), {}, {}],
    ['wrong tenant', makeEvent('ok', { root: { tenant_key: 'tenant-other' }, sender: { tenant_key: 'tenant-other' } }), {}, {}],
    ['wrong app', makeEvent('ok', { root: { app_id: 'app-other' } }), {}, {}],
    ['wrong decision', makeEvent('DEC-20260716-OTHER：自定义 ok'), {}, {}],
    ['expired', makeEvent(), {}, { expiresAt: new Date(NOW.getTime() - 1).toISOString() }],
    ['before issued', makeEvent('ok', { message: { create_time: String(NOW.getTime() - 120_000) } }), {}, {}],
    ['custom disabled', makeEvent(), {}, { allowCustomReply: false }],
    ['unpaired', makeEvent(), { pairedOperatorOpenIdHash: null }, {}],
  ]) {
    await t.test(name, async (t) => {
      const root = await mkdtemp(join(tmpdir(), 'tzg-message-reject-'));
      t.after(() => rm(root, { recursive: true, force: true }));
      const replies = [];
      const result = await handleDecisionTextMessage({
        event,
        config: makeConfig(root, configOverride),
        pendingBindings: [makeBinding(bindingOverride)],
        now: NOW,
        replyText: async (...args) => replies.push(args),
      });
      assert.equal(result.accepted, false);
      assert.equal(replies.length, 0);
      await assert.rejects(readdir(join(root, 'inbox')));
    });
  }
});

test('paired operator in the bound chat gets one copyable format hint for ordinary text', async (t) => {
  const root = await mkdtemp(join(tmpdir(), 'tzg-message-hint-'));
  t.after(() => rm(root, { recursive: true, force: true }));
  const replies = [];
  const request = {
    event: makeEvent('我想用别的方案'),
    config: makeConfig(root),
    pendingBindings: [makeBinding()],
    now: NOW,
    replyText: async (messageId, text) => replies.push({ messageId, text }),
  };
  assert.equal((await handleDecisionTextMessage(request)).accepted, false);
  assert.equal((await handleDecisionTextMessage(request)).accepted, false);
  assert.deepEqual(replies, [{
    messageId: MESSAGE_ID,
    text: `请按以下格式回复：\n${DECISION_ID}：自定义 <你的方案>`,
  }]);
});

test('createMessageReplyTransport sends the official reply request with a silent SDK logger', async () => {
  const constructions = [];
  const requests = [];
  class FakeClient {
    constructor(options) {
      constructions.push(options);
      this.im = { message: { reply: async (request) => { requests.push(request); return { code: 0 }; } } };
    }
  }
  const reply = await createMessageReplyTransport(makeConfig('C:\\message-runtime-fixture'), {
    Client: FakeClient,
  });
  await reply(MESSAGE_ID, '确认文本');
  assert.deepEqual(requests, [{
    path: { message_id: MESSAGE_ID },
    data: { msg_type: 'text', content: JSON.stringify({ text: '确认文本' }) },
  }]);
  assert.equal(constructions[0].appId, APP_ID);
  assert.equal(constructions[0].appSecret, APP_SECRET);
  for (const level of Object.keys(constructions[0].logger)) {
    assert.equal(constructions[0].logger[level]('must stay silent'), undefined);
  }
});
