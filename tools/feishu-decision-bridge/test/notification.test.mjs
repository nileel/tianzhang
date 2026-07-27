import assert from 'node:assert/strict';
import { mkdtemp, readFile, readdir, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';
import { Readable, Writable } from 'node:stream';
import test from 'node:test';

import { buildNotificationCard } from '../src/notification-card.mjs';
import {
  recordNotificationOutcome,
  summarizeNotificationOutcomes,
} from '../src/notification-audit.mjs';
import { parsePrivateConfig } from '../src/config.mjs';
import { sendNotification } from '../src/send-notification-core.mjs';
import { createSendIntentStore } from '../src/send-intent-store.mjs';
import { ProviderRejectedError } from '../src/send-runtime.mjs';
import { main } from '../src/send-notification.mjs';

const NOW = new Date('2026-07-27T08:00:00.000Z');
const HMAC_KEY = Buffer.alloc(32, 0x41).toString('base64');

function makeConfig(stateRoot, overrides = {}) {
  return parsePrivateConfig({
    schemaVersion: 1,
    appId: 'notification_test_app',
    appSecret: 'notification-secret',
    recipient: { type: 'email', value: 'operator@example.invalid' },
    expectedTenantKey: null,
    pairedOperatorOpenIdHash: null,
    hmacKey: HMAC_KEY,
    stateRoot,
    ...overrides,
  });
}

function makeHealth(overrides = {}) {
  return {
    status: 'CONNECTED',
    updatedAt: NOW.toISOString(),
    pid: 4321,
    pidAlive: true,
    ...overrides,
  };
}

function makeTaskNotification(overrides = {}) {
  return {
    kind: 'task_outcome',
    taskId: 'N-FPD-NPC-01',
    title: 'NPC 修炼行动权重',
    status: 'completed',
    goal: '让 NPC 修炼选择具有可调且可复现的权重',
    completed: '接入权重数据并形成确定性排序',
    impact: '合法行动会按已批准权重稳定排序',
    boundary: '未新增 NPC 全知信息或每日全世界扫描',
    verification: 'BattleSim 构建和固定场景回归通过',
    next: '解锁 Unity 共用消费，仍按固定队列推进',
    commitSha: '0123456789abcdef0123456789abcdef01234567',
    ...overrides,
  };
}

function passThroughStore() {
  return {
    async run(intent, operation) {
      return { ...intent, ...(await operation()), resultAt: intent.now.toISOString() };
    },
  };
}

function captureOutput() {
  let value = '';
  return {
    stream: new Writable({
      write(chunk, _encoding, callback) {
        value += chunk.toString();
        callback();
      },
    }),
    read: () => value,
  };
}

test('report cards preserve the complete body and have no reply controls', () => {
  const body = '# 日报\n\n## 净成果\n\n- 完整正文';
  const card = buildNotificationCard({
    kind: 'daily_report',
    title: '天章日报 · 2026-07-27',
    body,
  });
  assert.equal(card.elements[0].tag, 'markdown');
  assert.equal(card.elements[0].content, body);
  assert.equal(card.elements.some((element) => ['action', 'form'].includes(element.tag)), false);
});

test('task outcome cards expose all five content sections and no reply controls', () => {
  const card = buildNotificationCard(makeTaskNotification());
  const text = JSON.stringify(card);
  for (const heading of ['任务目标', '本次完成', '实际影响与明确边界', '验证结果', '后续关系']) {
    assert.match(text, new RegExp(heading));
  }
  assert.match(text, /0123456789ab/u);
  assert.equal(card.elements.some((element) => ['action', 'form'].includes(element.tag)), false);
});

test('notification cards reject incomplete fields and overlong reports instead of truncating', () => {
  assert.throws(
    () => buildNotificationCard(makeTaskNotification({ boundary: '' })),
    /^Error: Invalid notification card input$/,
  );
  assert.throws(
    () => buildNotificationCard({
      kind: 'weekly_report',
      title: '天章周报',
      body: '周'.repeat(6001),
    }),
    /^Error: Invalid notification card input$/,
  );
  assert.throws(
    () => buildNotificationCard({
      kind: 'daily_report',
      title: '天章日报',
      body: '正文\u202e伪装',
    }),
    /^Error: Invalid notification card input$/,
  );
  let getterCalls = 0;
  const accessor = {};
  Object.defineProperty(accessor, 'kind', {
    enumerable: true,
    get() {
      getterCalls += 1;
      return 'daily_report';
    },
  });
  assert.throws(() => buildNotificationCard(accessor), /^Error: Invalid notification card input$/);
  assert.equal(getterCalls, 0);
});

test('ordinary notifications use deterministic idempotency and do not resend accepted events', async (t) => {
  const root = await mkdtemp(join(tmpdir(), 'tzg-notification-send-'));
  t.after(() => rm(root, { recursive: true, force: true }));
  const config = makeConfig(root);
  const store = createSendIntentStore(root);
  const requests = [];
  const transport = {
    async sendInteractive(request) {
      requests.push(request);
      return { messageId: 'om_notification_raw', chatId: 'oc_notification_raw' };
    },
  };
  const request = {
    config,
    notification: makeTaskNotification(),
    idempotencyKey: 'task_outcome:N-FPD-NPC-01:completed:0123456789abcdef',
    transport,
    intentStore: store,
    health: makeHealth(),
    now: NOW,
  };
  const first = await sendNotification(request);
  const second = await sendNotification({
    ...request,
    transport: {
      async sendInteractive() {
        assert.fail('accepted event must not be sent twice');
      },
    },
  });
  assert.equal(first.result, 'PROVIDER_ACCEPTED');
  assert.deepEqual(second, first);
  assert.equal(requests.length, 1);
  const card = JSON.parse(requests[0].data.content);
  assert.equal(card.elements.some((element) => ['action', 'form'].includes(element.tag)), false);
});

test('ordinary notifications fail closed for unhealthy, rejected, and unknown outcomes', async () => {
  const config = makeConfig(resolve(tmpdir(), 'tzg-notification-outcome-test'));
  let calls = 0;
  const unavailable = await sendNotification({
    config,
    notification: makeTaskNotification(),
    idempotencyKey: 'unavailable',
    transport: { async sendInteractive() { calls += 1; } },
    intentStore: passThroughStore(),
    health: makeHealth({ status: 'UNAVAILABLE' }),
    now: NOW,
  });
  assert.deepEqual(unavailable, { result: 'CHANNEL_UNAVAILABLE' });
  assert.equal(calls, 0);

  const rejected = await sendNotification({
    config,
    notification: makeTaskNotification(),
    idempotencyKey: 'rejected',
    transport: { async sendInteractive() { throw new ProviderRejectedError(); } },
    intentStore: passThroughStore(),
    health: makeHealth(),
    now: NOW,
  });
  assert.equal(rejected.result, 'DELIVERY_FAILED');

  const unknown = await sendNotification({
    config,
    notification: makeTaskNotification(),
    idempotencyKey: 'unknown',
    transport: { async sendInteractive() { throw new Error('private provider detail'); } },
    intentStore: passThroughStore(),
    health: makeHealth(),
    now: NOW,
  });
  assert.equal(unknown.result, 'PROVIDER_OUTCOME_UNKNOWN');
  assert.doesNotMatch(JSON.stringify(unknown), /private provider detail/u);
});

test('ordinary notification unknown outcomes are persisted without automatic retry', async (t) => {
  const root = await mkdtemp(join(tmpdir(), 'tzg-notification-no-retry-'));
  t.after(() => rm(root, { recursive: true, force: true }));
  const config = makeConfig(root);
  const store = createSendIntentStore(root, { retryUnknown: false });
  let calls = 0;
  const request = {
    config,
    notification: makeTaskNotification(),
    idempotencyKey: 'task_outcome:no-retry',
    transport: {
      async sendInteractive() {
        calls += 1;
        throw new Error('unknown');
      },
    },
    intentStore: store,
    health: makeHealth(),
    now: NOW,
  };
  assert.equal((await sendNotification(request)).result, 'PROVIDER_OUTCOME_UNKNOWN');
  assert.equal((await sendNotification({
    ...request,
    now: new Date(NOW.getTime() + 60_000),
    health: makeHealth({ updatedAt: new Date(NOW.getTime() + 60_000).toISOString() }),
  })).result, 'PROVIDER_OUTCOME_UNKNOWN');
  assert.equal(calls, 1);
});

test('private audit stores only hashes, kind, result and time, then reports failures by kind', async (t) => {
  const root = await mkdtemp(join(tmpdir(), 'tzg-notification-audit-'));
  t.after(() => rm(root, { recursive: true, force: true }));
  await recordNotificationOutcome({
    stateRoot: root,
    idempotencyKey: 'daily_report:secret-window',
    kind: 'daily_report',
    result: 'CHANNEL_UNAVAILABLE',
    now: NOW,
  });
  await recordNotificationOutcome({
    stateRoot: root,
    idempotencyKey: 'task_outcome:secret-task',
    kind: 'task_outcome',
    result: 'PROVIDER_ACCEPTED',
    now: NOW,
  });
  const names = await readdir(join(root, 'notification-events'));
  assert.equal(names.length, 2);
  for (const name of names) {
    const raw = await readFile(join(root, 'notification-events', name), 'utf8');
    assert.doesNotMatch(raw, /secret-window|secret-task|operator@|om_|oc_/u);
    assert.deepEqual(Object.keys(JSON.parse(raw)).sort(), [
      'eventHash', 'kind', 'result', 'schemaVersion', 'updatedAt',
    ]);
  }
  assert.deepEqual(
    await summarizeNotificationOutcomes({
      stateRoot: root,
      since: new Date('2026-07-27T07:59:00.000Z'),
      until: new Date('2026-07-27T08:01:00.000Z'),
    }),
    { total: 2, undelivered: 1, byKind: { daily_report: 1 } },
  );
});

test('notification CLI returns one sanitized result and never creates decision bindings', async (t) => {
  const root = await mkdtemp(join(tmpdir(), 'tzg-notification-cli-'));
  t.after(() => rm(root, { recursive: true, force: true }));
  const configPath = join(root, 'private.json');
  await writeFile(configPath, JSON.stringify({
    schemaVersion: 1,
    appId: 'notification_test_app',
    appSecret: 'notification-secret',
    recipient: { type: 'email', value: 'operator@example.invalid' },
    expectedTenantKey: null,
    pairedOperatorOpenIdHash: null,
    hmacKey: HMAC_KEY,
    stateRoot: root,
  }));
  const output = captureOutput();
  let audit;
  const code = await main([], {
    stdin: Readable.from([JSON.stringify({
      notification: {
        kind: 'weekly_report',
        title: '天章周报 · 2026-07-20—2026-07-27',
        body: '# 完整周报',
      },
      idempotencyKey: 'weekly_report:tzg-weekly-project-summary:2026-07-27T08:00:00.000Z',
    })]),
    stdout: output.stream,
    env: { FEISHU_DECISION_CONFIG_PATH: configPath },
    now: () => NOW,
    readHealth: async () => makeHealth(),
    createTransport: async () => ({
      async sendInteractive() {
        return { messageId: 'om_private', chatId: 'oc_private' };
      },
    }),
    createIntentStore: () => passThroughStore(),
    recordAudit: async (record) => { audit = record; },
  });
  assert.equal(code, 0);
  assert.deepEqual(JSON.parse(output.read()), { result: 'PROVIDER_ACCEPTED' });
  assert.equal(audit.kind, 'weekly_report');
  await assert.rejects(readFile(join(root, 'pending-bindings.json')), /ENOENT/u);
});
