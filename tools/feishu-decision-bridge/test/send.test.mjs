import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { access, mkdir, mkdtemp, readFile, readdir, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { dirname, join, resolve } from 'node:path';
import { spawnSync } from 'node:child_process';
import test from 'node:test';
import { fileURLToPath } from 'node:url';

import { buildDecisionCard } from '../src/card.mjs';
import { parsePrivateConfig, sha256 } from '../src/config.mjs';
import { sendDecision } from '../src/send-core.mjs';
import { createSendIntentStore, hashSendIntentKey } from '../src/send-intent-store.mjs';
import {
  ProviderOutcomeUnknownError,
  ProviderRejectedError,
  createLarkTransport,
  readHealthSnapshot,
} from '../src/send-runtime.mjs';
import { main } from '../src/send-decision.mjs';

const HMAC_KEY = Buffer.alloc(32, 0x41).toString('base64');
const NOW = new Date('2026-07-16T08:00:00.000Z');
const CLI_PATH = fileURLToPath(new URL('../src/send-decision.mjs', import.meta.url));

function makeRawConfig(overrides = {}) {
  return {
    schemaVersion: 1,
    appId: 'cli_test_app',
    appSecret: 'top-secret-app-secret',
    recipient: {
      type: 'email',
      value: 'operator@example.invalid',
    },
    expectedTenantKey: null,
    pairedOperatorOpenIdHash: null,
    hmacKey: HMAC_KEY,
    stateRoot: resolve(tmpdir(), 'tzg-feishu-test-state'),
    ...overrides,
  };
}

function makeConfig(overrides = {}) {
  return parsePrivateConfig(makeRawConfig(overrides));
}

function makeDecision(overrides = {}) {
  return {
    decisionId: 'DEC-20260716-ABC123',
    taskId: 'TQ-057',
    question: '应采用哪种实现方案？',
    options: [
      { key: 'A', label: '方案甲' },
      { key: 'B', label: '方案乙' },
      { key: 'C', label: '方案丙' },
    ],
    recommendedOption: 'B',
    impactSummary: 'A 改动较大；B 风险较低；C 会延期。',
    ...overrides,
  };
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

function makeCapturingTransport(messageId = 'om_provider_123') {
  const calls = [];
  return {
    calls,
    transport: {
      async sendInteractive(request) {
        calls.push(request);
        return { messageId };
      },
    },
  };
}

function makePassThroughIntentStore() {
  return {
    async run(intent, operation) {
      const outcome = await operation();
      return { ...intent, ...outcome };
    },
  };
}

function assertOneJsonLine(text) {
  assert.match(text, /^\{[^\r\n]*\}\n$/);
  return JSON.parse(text.trimEnd());
}

async function captureRejected(promise) {
  try {
    await promise;
  } catch (error) {
    return error;
  }
  assert.fail('Expected promise to reject');
}

test('sendDecision maps email and open_id recipients and sends the exact interactive card request', async (t) => {
  for (const recipient of [
    { type: 'email', value: 'operator@example.invalid' },
    { type: 'open_id', value: 'ou_test_operator' },
  ]) {
    await t.test(recipient.type, async () => {
      const config = makeConfig({ recipient });
      const decision = makeDecision();
      const { calls, transport } = makeCapturingTransport();

      const result = await sendDecision({
        config,
        decision,
        attemptNumber: 1,
        transport,
        intentStore: makePassThroughIntentStore(),
        health: makeHealth(),
        now: NOW,
      });

      assert.equal(calls.length, 1);
      const request = calls[0];
      assert.deepEqual(request.params, { receive_id_type: recipient.type });
      assert.deepEqual(Object.keys(request.data).sort(), ['content', 'msg_type', 'receive_id', 'uuid']);
      assert.equal(request.data.receive_id, recipient.value);
      assert.equal(request.data.msg_type, 'interactive');
      assert.match(request.data.uuid, /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/);
      assert.ok(request.data.uuid.length <= 50);

      const content = JSON.parse(request.data.content);
      const nonce = content.elements.at(-1).actions[0].value.cardNonce;
      assert.deepEqual(content, buildDecisionCard(decision, nonce));
      for (const action of content.elements.at(-1).actions) {
        assert.deepEqual(
          Object.keys(action.value).sort(),
          ['cardNonce', 'decisionId', 'kind', 'optionKey'],
        );
      }

      assert.deepEqual(Object.keys(result).sort(), [
        'cardNonceHash',
        'providerMessageIdHash',
        'result',
        'targetHash',
      ]);
      assert.equal(result.result, 'PROVIDER_ACCEPTED');
      assert.equal(result.targetHash, sha256(recipient.value));
      assert.equal(result.providerMessageIdHash, sha256('om_provider_123'));
      assert.equal(result.cardNonceHash, sha256(nonce));
      for (const hash of [result.targetHash, result.providerMessageIdHash, result.cardNonceHash]) {
        assert.match(hash, /^[0-9a-f]{64}$/);
      }
      const serialized = JSON.stringify(result);
      for (const sensitive of [recipient.value, 'om_provider_123', request.data.uuid, nonce, config.appSecret]) {
        assert.equal(serialized.includes(sensitive), false);
      }
    });
  }
});

test('sendDecision is deterministic per logical attempt and domain-separates attempt and decision', async () => {
  const config = makeConfig();
  const decision = makeDecision();
  const requests = [];
  const transport = {
    async sendInteractive(request) {
      requests.push(request);
      return { messageId: `om_${requests.length}` };
    },
  };

  for (const [currentDecision, attemptNumber] of [
    [decision, 2],
    [decision, 2],
    [decision, 3],
    [makeDecision({ decisionId: 'DEC-20260716-OTHER' }), 2],
    [decision, Number.MAX_SAFE_INTEGER],
  ]) {
    await sendDecision({
      config,
      decision: currentDecision,
      attemptNumber,
      transport,
      intentStore: makePassThroughIntentStore(),
      health: makeHealth(),
      now: NOW,
    });
  }

  assert.equal(requests[0].data.uuid, requests[1].data.uuid);
  assert.equal(requests[0].data.content, requests[1].data.content);
  assert.notEqual(requests[0].data.uuid, requests[2].data.uuid);
  assert.notEqual(requests[0].data.content, requests[2].data.content);
  assert.notEqual(requests[0].data.uuid, requests[3].data.uuid);
  assert.notEqual(requests[0].data.content, requests[3].data.content);
  assert.notEqual(requests[0].data.uuid, requests[4].data.uuid);
  assert.notEqual(requests[0].data.content, requests[4].data.content);
});

test('sendDecision fails closed for every unhealthy snapshot without calling transport', async (t) => {
  const cases = [
    ['missing', null],
    ['stale', makeHealth({ updatedAt: new Date(NOW.getTime() - 120_001).toISOString() })],
    ['future by one millisecond', makeHealth({ updatedAt: new Date(NOW.getTime() + 1).toISOString() })],
    ['dead', makeHealth({ pidAlive: false })],
    ['not connected', makeHealth({ status: 'DISCONNECTED' })],
    ['bad timestamp', makeHealth({ updatedAt: 'not-a-date' })],
    ['bad pid', makeHealth({ pid: 0 })],
  ];

  for (const [name, health] of cases) {
    await t.test(name, async () => {
      let calls = 0;
      const result = await sendDecision({
        config: makeConfig(),
        decision: makeDecision(),
        attemptNumber: 1,
        transport: { async sendInteractive() { calls += 1; } },
        intentStore: makePassThroughIntentStore(),
        health,
        now: NOW,
      });
      assert.deepEqual(result, { result: 'CHANNEL_UNAVAILABLE' });
      assert.equal(calls, 0);
    });
  }

  let calls = 0;
  const boundaryTransport = {
    async sendInteractive() {
      calls += 1;
      return { messageId: 'om_boundary' };
    },
  };
  await sendDecision({
    config: makeConfig(), decision: makeDecision(), attemptNumber: 1, transport: boundaryTransport,
    intentStore: makePassThroughIntentStore(),
    health: makeHealth({ updatedAt: new Date(NOW.getTime() - 120_000).toISOString() }), now: NOW,
  });
  await sendDecision({
    config: makeConfig(), decision: makeDecision(), attemptNumber: 1, transport: boundaryTransport,
    intentStore: makePassThroughIntentStore(),
    health: makeHealth({ updatedAt: NOW.toISOString() }), now: NOW,
  });
  assert.equal(calls, 2);
});

test('sendDecision distinguishes explicit provider rejection from unknown outcomes without leaking raw data', async (t) => {
  const config = makeConfig();
  const targetHash = sha256(config.recipient.value);
  const unknownCases = [
    ['throw', { async sendInteractive() { throw new Error(`${config.appSecret} ${config.recipient.value}`); } }],
    ['missing message id', { async sendInteractive() { return {}; } }],
    ['extra response field', { async sendInteractive() { return { messageId: 'om_ok', raw: config.appSecret }; } }],
    ['non-ascii id', { async sendInteractive() { return { messageId: '消息一' }; } }],
    ['non-string id', { async sendInteractive() { return { messageId: 123 }; } }],
    ['blank id', { async sendInteractive() { return { messageId: '  ' }; } }],
  ];

  for (const [name, transport] of unknownCases) {
    await t.test(name, async () => {
      const result = await sendDecision({
        config,
        decision: makeDecision(),
        attemptNumber: 1,
        transport,
        intentStore: makePassThroughIntentStore(),
        health: makeHealth(),
        now: NOW,
      });
      assert.deepEqual(Object.keys(result).sort(), [
        'cardNonceHash', 'intentKeyHash', 'result', 'targetHash',
      ]);
      assert.equal(result.result, 'PROVIDER_OUTCOME_UNKNOWN');
      assert.equal(result.targetHash, targetHash);
      const serialized = JSON.stringify(result);
      assert.equal(serialized.includes(config.appSecret), false);
      assert.equal(serialized.includes(config.recipient.value), false);
      assert.equal(serialized.includes('om_ok'), false);
    });
  }

  const rejected = await sendDecision({
    config,
    decision: makeDecision(),
    attemptNumber: 1,
    transport: {
      async sendInteractive() {
        throw new ProviderRejectedError();
      },
    },
    intentStore: makePassThroughIntentStore(),
    health: makeHealth(),
    now: NOW,
  });
  assert.deepEqual(rejected, { result: 'DELIVERY_FAILED', targetHash });
});

test('sendDecision rejects invalid input before transport', async (t) => {
  const invalidCases = [
    ['negative attempt', { attemptNumber: -1 }],
    ['zero attempt', { attemptNumber: 0 }],
    ['fractional attempt', { attemptNumber: 1.5 }],
    ['unsafe attempt', { attemptNumber: Number.MAX_SAFE_INTEGER + 1 }],
    ['string attempt', { attemptNumber: '1' }],
    ['bad decision', { decision: makeDecision({ options: [] }) }],
    ['bad config', { config: { ...makeRawConfig(), extra: true } }],
    ['bad transport', { transport: {} }],
    ['bad intent store', { intentStore: {} }],
    ['bad now', { now: new Date('invalid') }],
  ];

  for (const [name, override] of invalidCases) {
    await t.test(name, async () => {
      let calls = 0;
      const base = {
        config: makeConfig(),
        decision: makeDecision(),
        attemptNumber: 1,
        transport: { async sendInteractive() { calls += 1; } },
        intentStore: makePassThroughIntentStore(),
        health: makeHealth(),
        now: NOW,
      };
      await assert.rejects(sendDecision({ ...base, ...override }), /^Error: Invalid send request$/);
      assert.equal(calls, 0);
    });
  }

  await assert.rejects(sendDecision(null), /^Error: Invalid send request$/);

  let intentStoreGetterCalls = 0;
  const accessorStore = {};
  Object.defineProperty(accessorStore, 'run', {
    enumerable: true,
    get() {
      intentStoreGetterCalls += 1;
      throw new Error('store-accessor-secret');
    },
  });
  await assert.rejects(sendDecision({
    config: makeConfig(),
    decision: makeDecision(),
    attemptNumber: 1,
    transport: { async sendInteractive() {} },
    intentStore: accessorStore,
    health: makeHealth(),
    now: NOW,
  }), /^Error: Invalid send request$/);
  assert.equal(intentStoreGetterCalls, 0);

  let requestGetterCalls = 0;
  const accessorRequest = {
    config: makeConfig(),
    attemptNumber: 1,
    transport: { async sendInteractive() {} },
    intentStore: makePassThroughIntentStore(),
    health: makeHealth(),
    now: NOW,
  };
  Object.defineProperty(accessorRequest, 'decision', {
    enumerable: true,
    get() {
      requestGetterCalls += 1;
      throw new Error('request-accessor-secret');
    },
  });
  await assert.rejects(sendDecision(accessorRequest), /^Error: Invalid send request$/);
  assert.equal(requestGetterCalls, 0);

  let decisionGetterCalls = 0;
  const accessorDecision = makeDecision();
  Object.defineProperty(accessorDecision, 'decisionId', {
    enumerable: true,
    get() {
      decisionGetterCalls += 1;
      throw new Error('decision-accessor-secret');
    },
  });
  await assert.rejects(sendDecision({
    config: makeConfig(),
    decision: accessorDecision,
    attemptNumber: 1,
    transport: { async sendInteractive() {} },
    intentStore: makePassThroughIntentStore(),
    health: makeHealth(),
    now: NOW,
  }), /^Error: Invalid send request$/);
  assert.equal(decisionGetterCalls, 0);
});

test('createLarkTransport uses the official SDK request shape and canonicalizes accepted responses', async (t) => {
  const constructions = [];
  const requests = [];
  let response = { code: 0, data: { message_id: 'om_sdk_123' } };
  let thrownError = null;
  class FakeClient {
    constructor(options) {
      constructions.push(options);
      this.im = {
        message: {
          create: async (request) => {
            requests.push(request);
            if (thrownError !== null) {
              throw thrownError;
            }
            return response;
          },
        },
      };
    }
  }

  const config = makeConfig();
  const transport = await createLarkTransport(config, { Client: FakeClient });
  const request = {
    params: { receive_id_type: 'email' },
    data: {
      receive_id: 'operator@example.invalid',
      msg_type: 'interactive',
      content: '{"card":true}',
      uuid: '62dcb6ee-f21f-4f9d-8dcb-e6f5436eaf11',
    },
  };

  assert.deepEqual(await transport.sendInteractive(request), { messageId: 'om_sdk_123' });
  assert.deepEqual(constructions, [{ appId: config.appId, appSecret: config.appSecret }]);
  assert.deepEqual(requests, [request]);

  response = { data: { message_id: 'om_no_code' } };
  assert.deepEqual(await transport.sendInteractive(request), { messageId: 'om_no_code' });

  const invalidRequestError = await captureRejected(transport.sendInteractive(null));
  assert.ok(invalidRequestError instanceof ProviderOutcomeUnknownError);
  assert.equal(invalidRequestError.message, 'Feishu provider outcome unknown');

  thrownError = Object.assign(new Error(config.appSecret), {
    response: { data: { code: 230001, msg: config.recipient.value } },
  });
  const explicitHttpRejection = await captureRejected(transport.sendInteractive(request));
  assert.ok(explicitHttpRejection instanceof ProviderRejectedError);
  assert.equal(explicitHttpRejection.message, 'Feishu provider rejected request');
  assert.equal(explicitHttpRejection.message.includes(config.appSecret), false);
  assert.equal(explicitHttpRejection.message.includes(config.recipient.value), false);
  thrownError = null;

  for (const [name, badResponse] of [
    ['provider error code', { code: 999, msg: config.appSecret, data: { message_id: 'om_raw' } }],
    ['missing data', { code: 0, msg: config.appSecret }],
    ['invalid id', { code: 0, data: { message_id: '消息' } }],
    ['non-string id', { code: 0, data: { message_id: 123 } }],
  ]) {
    await t.test(name, async () => {
      response = badResponse;
      const error = await captureRejected(transport.sendInteractive(request));
      if (name === 'provider error code') {
        assert.ok(error instanceof ProviderRejectedError);
        assert.equal(error.message, 'Feishu provider rejected request');
      } else {
        assert.ok(error instanceof ProviderOutcomeUnknownError);
        assert.equal(error.message, 'Feishu provider outcome unknown');
      }
      assert.equal(error.message.includes(config.appSecret), false);
      assert.equal(error.message.includes('om_raw'), false);
    });
  }
});

test('readHealthSnapshot reads bounded plain health data and proves pid liveness with the injected probe', async (t) => {
  const root = await mkdtemp(join(tmpdir(), 'tzg-health-'));
  t.after(() => rm(root, { recursive: true, force: true }));
  await writeFile(join(root, 'health.json'), JSON.stringify({
    status: 'CONNECTED',
    updatedAt: NOW.toISOString(),
    pid: 9876,
  }));

  const probed = [];
  const live = await readHealthSnapshot(root, NOW, {
    probePid(pid) {
      probed.push(pid);
      return true;
    },
  });
  assert.deepEqual(live, {
    status: 'CONNECTED',
    updatedAt: NOW.toISOString(),
    pid: 9876,
    pidAlive: true,
  });
  assert.deepEqual(probed, [9876]);

  const dead = await readHealthSnapshot(root, NOW, { probePid: () => false });
  assert.deepEqual(dead, {
    status: 'CONNECTED',
    updatedAt: NOW.toISOString(),
    pid: 9876,
    pidAlive: false,
  });

  for (const [name, content] of [
    ['invalid json', '{secret:'],
    ['array json', '[]'],
    ['missing field', JSON.stringify({ status: 'CONNECTED', pid: 9876 })],
    ['too large', `{"padding":"${'x'.repeat(20_000)}"}`],
  ]) {
    await t.test(name, async () => {
      await writeFile(join(root, 'health.json'), content);
      assert.deepEqual(await readHealthSnapshot(root, NOW, { probePid: () => true }), {
        status: 'UNAVAILABLE',
        updatedAt: null,
        pid: null,
        pidAlive: false,
      });
    });
  }

  const missing = await readHealthSnapshot(join(root, 'missing'), NOW, { probePid: () => true });
  assert.deepEqual(missing, {
    status: 'UNAVAILABLE',
    updatedAt: null,
    pid: null,
    pidAlive: false,
  });
});

test('main enforces the request-file CLI contract and emits one sanitized JSON line', async (t) => {
  const root = await mkdtemp(join(tmpdir(), 'tzg-send-main-'));
  t.after(() => rm(root, { recursive: true, force: true }));
  const requestPath = join(root, 'request.json');
  const configPath = join(root, 'private.json');
  await writeFile(requestPath, JSON.stringify({ decision: makeDecision(), attemptNumber: 1 }));
  await writeFile(configPath, JSON.stringify(makeRawConfig({ stateRoot: root })));

  async function run(argv, overrides = {}) {
    let stdout = '';
    let stderr = '';
    let createCalls = 0;
    let createStoreCalls = 0;
    const code = await main(argv, {
      env: { FEISHU_DECISION_CONFIG_PATH: configPath },
      homedir: () => join(root, 'home'),
      stdout: { write(value) { stdout += value; } },
      stderr: { write(value) { stderr += value; } },
      readHealth: async () => makeHealth(),
      createTransport: async () => {
        createCalls += 1;
        return { async sendInteractive() { return { messageId: 'om_main' }; } };
      },
      createIntentStore: (stateRoot) => {
        createStoreCalls += 1;
        return createSendIntentStore(stateRoot);
      },
      send: sendDecision,
      now: () => NOW,
      ...overrides,
    });
    return { code, stdout, stderr, createCalls, createStoreCalls };
  }

  for (const [name, argv] of [
    ['unknown argument', ['--request-file', requestPath, '--secret', 'leak-me']],
    ['duplicate argument', ['--request-file', requestPath, '--request-file', requestPath]],
    ['relative path', ['--request-file', 'request.json']],
    ['missing value', ['--request-file']],
  ]) {
    await t.test(name, async () => {
      const result = await run(argv);
      assert.equal(result.code, 22);
      assert.deepEqual(assertOneJsonLine(result.stdout), { result: 'INVALID_INPUT' });
      assert.equal(result.stderr, '');
      assert.equal(result.createCalls, 0);
      assert.equal(result.createStoreCalls, 0);
      assert.equal(result.stdout.includes('leak-me'), false);
    });
  }

  const unavailable = await run(['--request-file', requestPath], {
    readHealth: async () => ({ status: 'UNAVAILABLE', updatedAt: null, pid: null, pidAlive: false }),
    createTransport: async () => { throw new Error('SDK must not load'); },
  });
  assert.equal(unavailable.code, 20);
  assert.deepEqual(assertOneJsonLine(unavailable.stdout), { result: 'CHANNEL_UNAVAILABLE' });
  assert.equal(unavailable.stderr, '');
  assert.equal(unavailable.createCalls, 0);
  assert.equal(unavailable.createStoreCalls, 0);

  const futureHealth = await run(['--request-file', requestPath], {
    readHealth: async () => makeHealth({
      updatedAt: new Date(NOW.getTime() + 1).toISOString(),
    }),
  });
  assert.equal(futureHealth.code, 20);
  assert.deepEqual(assertOneJsonLine(futureHealth.stdout), { result: 'CHANNEL_UNAVAILABLE' });
  assert.equal(futureHealth.stderr, '');
  assert.equal(futureHealth.createCalls, 0);
  assert.equal(futureHealth.createStoreCalls, 0);

  const sdkUnavailable = await run(['--request-file', requestPath], {
    createTransport: async () => { throw new Error('sdk-secret-import-failed'); },
  });
  assert.equal(sdkUnavailable.code, 20);
  assert.deepEqual(assertOneJsonLine(sdkUnavailable.stdout), { result: 'CHANNEL_UNAVAILABLE' });
  assert.equal(sdkUnavailable.stderr, '');
  assert.equal(sdkUnavailable.createStoreCalls, 0);

  const accepted = await run(['--request-file', requestPath]);
  assert.equal(accepted.code, 0);
  assert.equal(assertOneJsonLine(accepted.stdout).result, 'PROVIDER_ACCEPTED');
  assert.equal(accepted.stderr, '');
  assert.equal(accepted.createCalls, 1);
  assert.equal(accepted.createStoreCalls, 1);

  const failed = await run(['--request-file', requestPath], {
    send: async () => ({
      result: 'DELIVERY_FAILED', targetHash: 'a'.repeat(64), raw: 'must-not-pass-through',
    }),
  });
  assert.equal(failed.code, 21);
  assert.deepEqual(assertOneJsonLine(failed.stdout), {
    result: 'DELIVERY_FAILED',
    targetHash: 'a'.repeat(64),
  });

  const unknown = await run(['--request-file', requestPath], {
    send: async () => ({
      result: 'PROVIDER_OUTCOME_UNKNOWN',
      targetHash: 'a'.repeat(64),
      cardNonceHash: 'b'.repeat(64),
      intentKeyHash: 'c'.repeat(64),
      providerRaw: 'must-not-pass-through',
    }),
  });
  assert.equal(unknown.code, 23);
  assert.deepEqual(assertOneJsonLine(unknown.stdout), {
    result: 'PROVIDER_OUTCOME_UNKNOWN',
    targetHash: 'a'.repeat(64),
    cardNonceHash: 'b'.repeat(64),
    intentKeyHash: 'c'.repeat(64),
  });
  assert.equal(unknown.stdout.includes('must-not-pass-through'), false);

  const acceptedWhitelist = await run(['--request-file', requestPath], {
    send: async () => ({
      result: 'PROVIDER_ACCEPTED',
      targetHash: 'a'.repeat(64),
      providerMessageIdHash: 'b'.repeat(64),
      cardNonceHash: 'c'.repeat(64),
      raw: 'must-not-pass-through',
    }),
  });
  assert.equal(acceptedWhitelist.code, 0);
  assert.deepEqual(assertOneJsonLine(acceptedWhitelist.stdout), {
    result: 'PROVIDER_ACCEPTED',
    targetHash: 'a'.repeat(64),
    providerMessageIdHash: 'b'.repeat(64),
    cardNonceHash: 'c'.repeat(64),
  });

  for (const [name, request] of [
    ['array', []],
    ['unknown request field', { decision: makeDecision(), attemptNumber: 1, config: makeRawConfig() }],
    ['missing attempt', { decision: makeDecision() }],
    ['negative attempt', { decision: makeDecision(), attemptNumber: -1 }],
    ['zero attempt', { decision: makeDecision(), attemptNumber: 0 }],
    ['fractional attempt', { decision: makeDecision(), attemptNumber: 1.5 }],
    ['unsafe attempt', { decision: makeDecision(), attemptNumber: Number.MAX_SAFE_INTEGER + 1 }],
  ]) {
    await t.test(name, async () => {
      await writeFile(requestPath, JSON.stringify(request));
      const result = await run(['--request-file', requestPath]);
      assert.equal(result.code, 22);
      assert.deepEqual(assertOneJsonLine(result.stdout), { result: 'INVALID_INPUT' });
      assert.equal(result.createCalls, 0);
      assert.equal(result.createStoreCalls, 0);
      assert.equal(result.stdout.includes('top-secret-app-secret'), false);
    });
  }

  await writeFile(
    requestPath,
    `{"decision":${JSON.stringify(makeDecision())},"attemptNumber":9007199254740993}`,
  );
  const unsafeJsonInteger = await run(['--request-file', requestPath]);
  assert.equal(unsafeJsonInteger.code, 22);
  assert.deepEqual(assertOneJsonLine(unsafeJsonInteger.stdout), { result: 'INVALID_INPUT' });
  assert.equal(unsafeJsonInteger.createCalls, 0);
  assert.equal(unsafeJsonInteger.createStoreCalls, 0);

  await writeFile(requestPath, `{"padding":"${'x'.repeat(70_000)}"}`);
  const oversized = await run(['--request-file', requestPath]);
  assert.equal(oversized.code, 22);
  assert.deepEqual(assertOneJsonLine(oversized.stdout), { result: 'INVALID_INPUT' });
  assert.equal(oversized.createCalls, 0);
  assert.equal(oversized.createStoreCalls, 0);
});

test('direct CLI invalid and unavailable paths do not need the SDK and never expose credentials', async (t) => {
  const root = await mkdtemp(join(tmpdir(), 'tzg-send-cli-'));
  t.after(() => rm(root, { recursive: true, force: true }));
  const requestPath = join(root, 'request.json');
  const configPath = join(root, 'private.json');
  await mkdir(dirname(requestPath), { recursive: true });
  await writeFile(requestPath, JSON.stringify({ decision: makeDecision(), attemptNumber: 1 }));
  await writeFile(configPath, JSON.stringify(makeRawConfig({ stateRoot: join(root, 'missing-health') })));
  const env = {
    ...process.env,
    FEISHU_DECISION_CONFIG_PATH: configPath,
  };

  const invalid = spawnSync(process.execPath, [CLI_PATH, '--request-file', 'relative.json'], {
    cwd: root,
    env,
    encoding: 'utf8',
  });
  assert.equal(invalid.status, 22);
  assert.deepEqual(assertOneJsonLine(invalid.stdout), { result: 'INVALID_INPUT' });
  assert.equal(invalid.stderr, '');

  const unavailable = spawnSync(process.execPath, [CLI_PATH, '--request-file', requestPath], {
    cwd: root,
    env,
    encoding: 'utf8',
  });
  assert.equal(unavailable.status, 20);
  assert.deepEqual(assertOneJsonLine(unavailable.stdout), { result: 'CHANNEL_UNAVAILABLE' });
  assert.equal(unavailable.stderr, '');

  for (const output of [invalid.stdout, invalid.stderr, unavailable.stdout, unavailable.stderr]) {
    assert.equal(output.includes('top-secret-app-secret'), false);
    assert.equal(output.includes('operator@example.invalid'), false);
    assert.equal(output.includes(configPath), false);
  }
});

test('card nonce is a stable domain-separated HMAC and cannot be predicted by the old unkeyed digest', async () => {
  const decision = makeDecision();
  const requests = [];
  const transport = {
    async sendInteractive(request) {
      requests.push(request);
      return { messageId: `om_nonce_${requests.length}` };
    },
  };
  const configs = [
    makeConfig(),
    makeConfig(),
    makeConfig({ hmacKey: Buffer.alloc(32, 0x42).toString('base64') }),
  ];
  for (const [config, attemptNumber] of [
    [configs[0], 4], [configs[1], 4], [configs[0], 5], [configs[2], 4],
  ]) {
    await sendDecision({
      config,
      decision,
      attemptNumber,
      transport,
      intentStore: makePassThroughIntentStore(),
      health: makeHealth(),
      now: NOW,
    });
  }
  const nonces = requests.map((request) => JSON.parse(request.data.content)
    .elements.at(-1).actions[0].value.cardNonce);
  assert.equal(nonces[0], nonces[1]);
  assert.notEqual(nonces[0], nonces[2]);
  assert.notEqual(nonces[0], nonces[3]);
  assert.match(nonces[0], /^[0-9a-f]{64}$/);
  const oldDigest = createHash('sha256')
    .update(`card-nonce-v1\u0000feishu\u0000${decision.decisionId}\u00004`, 'utf8')
    .digest('hex')
    .slice(0, 32);
  assert.notEqual(nonces[0], oldDigest);
});

test('send intent store persists only sanitized atomic ACCEPTED evidence and caches acceptance', async (t) => {
  const root = await mkdtemp(join(tmpdir(), 'tzg-send-intent-accepted-'));
  t.after(() => rm(root, { recursive: true, force: true }));
  const config = makeConfig({ stateRoot: root });
  const store = createSendIntentStore(root);
  const decision = makeDecision();
  let calls = 0;
  const requests = [];
  const transport = {
    async sendInteractive(request) {
      calls += 1;
      requests.push(request);
      return { messageId: 'om_raw_provider_identity' };
    },
  };
  const first = await sendDecision({
    config, decision, attemptNumber: 6, transport, intentStore: store, health: makeHealth(), now: NOW,
  });
  const cached = await sendDecision({
    config,
    decision,
    attemptNumber: 6,
    transport: { async sendInteractive() { calls += 1; throw new Error('must not run'); } },
    intentStore: store,
    health: makeHealth({ updatedAt: new Date(NOW.getTime() + 60 * 60 * 1000).toISOString() }),
    now: new Date(NOW.getTime() + 60 * 60 * 1000),
  });
  assert.equal(calls, 1);
  assert.deepEqual(cached, first);
  assert.deepEqual(Object.keys(cached).sort(), [
    'cardNonceHash', 'providerMessageIdHash', 'result', 'targetHash',
  ]);

  const names = await readdir(join(root, 'send-intents'));
  assert.equal(names.length, 1);
  assert.match(names[0], /^[0-9a-f]{64}\.json$/);
  const raw = await readFile(join(root, 'send-intents', names[0]), 'utf8');
  const record = JSON.parse(raw);
  assert.deepEqual(Object.keys(record).sort(), [
    'attemptNumber', 'cardNonceHash', 'firstAttemptAt', 'intentKeyHash', 'lastUpdatedAt',
    'provider', 'providerMessageIdHash', 'requestContentHash', 'resultAt', 'schemaVersion',
    'status', 'targetHash', 'uuid',
  ]);
  assert.equal(record.schemaVersion, 1);
  assert.equal(record.provider, 'feishu');
  assert.equal(record.status, 'ACCEPTED');
  assert.equal(record.providerMessageIdHash, sha256('om_raw_provider_identity'));
  const cardNonce = JSON.parse(requests[0].data.content).elements.at(-1).actions[0].value.cardNonce;
  for (const forbidden of [
    config.recipient.value,
    config.appId,
    config.appSecret,
    config.hmacKey,
    decision.decisionId,
    'om_raw_provider_identity',
    cardNonce,
    requests[0].data.content,
  ]) {
    assert.equal(raw.includes(forbidden), false);
  }
});

test('explicit provider rejection is persisted and cached without another transport call', async (t) => {
  const root = await mkdtemp(join(tmpdir(), 'tzg-send-intent-rejected-'));
  t.after(() => rm(root, { recursive: true, force: true }));
  const config = makeConfig({ stateRoot: root });
  const store = createSendIntentStore(root);
  let calls = 0;
  const first = await sendDecision({
    config,
    decision: makeDecision(),
    attemptNumber: 7,
    transport: { async sendInteractive() { calls += 1; throw new ProviderRejectedError(); } },
    intentStore: store,
    health: makeHealth(),
    now: NOW,
  });
  const cached = await sendDecision({
    config,
    decision: makeDecision(),
    attemptNumber: 7,
    transport: { async sendInteractive() { calls += 1; } },
    intentStore: store,
    health: makeHealth({ updatedAt: new Date(NOW.getTime() + 24 * 60 * 60 * 1000).toISOString() }),
    now: new Date(NOW.getTime() + 24 * 60 * 60 * 1000),
  });
  assert.equal(calls, 1);
  assert.deepEqual(cached, first);
  assert.deepEqual(cached, { result: 'DELIVERY_FAILED', targetHash: sha256(config.recipient.value) });
  const [name] = await readdir(join(root, 'send-intents'));
  assert.equal(JSON.parse(await readFile(join(root, 'send-intents', name), 'utf8')).status, 'REJECTED');
});

test('unknown outcome retries the exact same request inside 55 minutes and locks out transport at the boundary', async (t) => {
  await t.test('inside window', async (t) => {
    const root = await mkdtemp(join(tmpdir(), 'tzg-send-intent-retry-'));
    t.after(() => rm(root, { recursive: true, force: true }));
    const config = makeConfig({ stateRoot: root });
    const store = createSendIntentStore(root);
    const seen = [];
    const first = await sendDecision({
      config,
      decision: makeDecision(),
      attemptNumber: 8,
      transport: {
        async sendInteractive(request) {
          seen.push(request);
          throw new ProviderOutcomeUnknownError();
        },
      },
      intentStore: store,
      health: makeHealth(),
      now: NOW,
    });
    const second = await sendDecision({
      config,
      decision: makeDecision(),
      attemptNumber: 8,
      transport: {
        async sendInteractive(request) {
          seen.push(request);
          return { messageId: 'om_retry_accepted' };
        },
      },
      intentStore: store,
      health: makeHealth({ updatedAt: new Date(NOW.getTime() + 54 * 60 * 1000).toISOString() }),
      now: new Date(NOW.getTime() + 54 * 60 * 1000),
    });
    assert.equal(first.result, 'PROVIDER_OUTCOME_UNKNOWN');
    assert.equal(second.result, 'PROVIDER_ACCEPTED');
    assert.equal(seen.length, 2);
    assert.equal(seen[0].data.uuid, seen[1].data.uuid);
    assert.equal(seen[0].data.content, seen[1].data.content);
    assert.equal(seen[0].data.receive_id, seen[1].data.receive_id);
  });

  await t.test('55 minute boundary', async (t) => {
    const root = await mkdtemp(join(tmpdir(), 'tzg-send-intent-expired-'));
    t.after(() => rm(root, { recursive: true, force: true }));
    const config = makeConfig({ stateRoot: root });
    const store = createSendIntentStore(root);
    let calls = 0;
    await sendDecision({
      config,
      decision: makeDecision(),
      attemptNumber: 9,
      transport: { async sendInteractive() { calls += 1; throw new Error('timeout'); } },
      intentStore: store,
      health: makeHealth(),
      now: NOW,
    });
    const locked = await sendDecision({
      config,
      decision: makeDecision(),
      attemptNumber: 9,
      transport: { async sendInteractive() { calls += 1; return { messageId: 'must-not-send' }; } },
      intentStore: store,
      health: makeHealth({ updatedAt: new Date(NOW.getTime() + 55 * 60 * 1000).toISOString() }),
      now: new Date(NOW.getTime() + 55 * 60 * 1000),
    });
    assert.equal(calls, 1);
    assert.deepEqual(Object.keys(locked).sort(), [
      'cardNonceHash', 'intentKeyHash', 'result', 'targetHash',
    ]);
    assert.equal(locked.result, 'PROVIDER_OUTCOME_UNKNOWN');
  });
});

test('accepted provider response followed by terminal persistence failure returns outcome unknown', async (t) => {
  const root = await mkdtemp(join(tmpdir(), 'tzg-send-intent-write-fail-'));
  t.after(() => rm(root, { recursive: true, force: true }));
  const store = createSendIntentStore(root, {
    async atomicWrite(path, record, defaultAtomicWrite) {
      if (record.status === 'ACCEPTED') {
        throw new Error('simulated terminal disk failure');
      }
      return defaultAtomicWrite(path, record);
    },
  });
  let calls = 0;
  const uuids = [];
  const result = await sendDecision({
    config: makeConfig({ stateRoot: root }),
    decision: makeDecision(),
    attemptNumber: 10,
    transport: {
      async sendInteractive(request) {
        calls += 1;
        uuids.push(request.data.uuid);
        return { messageId: 'om_accept_then_crash' };
      },
    },
    intentStore: store,
    health: makeHealth(),
    now: NOW,
  });
  assert.equal(calls, 1);
  assert.equal(result.result, 'PROVIDER_OUTCOME_UNKNOWN');
  assert.equal(JSON.stringify(result).includes('om_accept_then_crash'), false);
  const retried = await sendDecision({
    config: makeConfig({ stateRoot: root }),
    decision: makeDecision(),
    attemptNumber: 10,
    transport: {
      async sendInteractive(request) {
        calls += 1;
        uuids.push(request.data.uuid);
        return { messageId: 'om_accept_then_crash_again' };
      },
    },
    intentStore: store,
    health: makeHealth({ updatedAt: new Date(NOW.getTime() + 60_000).toISOString() }),
    now: new Date(NOW.getTime() + 60_000),
  });
  assert.equal(retried.result, 'PROVIDER_OUTCOME_UNKNOWN');
  assert.equal(calls, 2);
  assert.equal(uuids[0], uuids[1]);
  const [name] = await readdir(join(root, 'send-intents'));
  assert.equal(JSON.parse(await readFile(join(root, 'send-intents', name), 'utf8')).status, 'OUTCOME_UNKNOWN');
});

test('mismatched, corrupt, and busy send intents fail closed without transport or evidence overwrite', async (t) => {
  await t.test('content mismatch', async (t) => {
    const root = await mkdtemp(join(tmpdir(), 'tzg-send-intent-content-mismatch-'));
    t.after(() => rm(root, { recursive: true, force: true }));
    const config = makeConfig({ stateRoot: root });
    const store = createSendIntentStore(root);
    let calls = 0;
    await sendDecision({
      config,
      decision: makeDecision(),
      attemptNumber: 11,
      transport: { async sendInteractive() { calls += 1; throw new Error('unknown'); } },
      intentStore: store,
      health: makeHealth(),
      now: NOW,
    });
    const result = await sendDecision({
      config,
      decision: makeDecision({ question: '已经被更改的问题？' }),
      attemptNumber: 11,
      transport: { async sendInteractive() { calls += 1; } },
      intentStore: store,
      health: makeHealth(),
      now: new Date(NOW.getTime() + 1_000),
    });
    assert.equal(calls, 1);
    assert.equal(result.result, 'PROVIDER_OUTCOME_UNKNOWN');
  });

  await t.test('target mismatch', async (t) => {
    const root = await mkdtemp(join(tmpdir(), 'tzg-send-intent-target-mismatch-'));
    t.after(() => rm(root, { recursive: true, force: true }));
    const store = createSendIntentStore(root);
    let calls = 0;
    await sendDecision({
      config: makeConfig({ stateRoot: root }),
      decision: makeDecision(),
      attemptNumber: 12,
      transport: { async sendInteractive() { calls += 1; throw new Error('unknown'); } },
      intentStore: store,
      health: makeHealth(),
      now: NOW,
    });
    const result = await sendDecision({
      config: makeConfig({ stateRoot: root, recipient: { type: 'open_id', value: 'ou_other_target' } }),
      decision: makeDecision(),
      attemptNumber: 12,
      transport: { async sendInteractive() { calls += 1; } },
      intentStore: store,
      health: makeHealth(),
      now: new Date(NOW.getTime() + 1_000),
    });
    assert.equal(calls, 1);
    assert.equal(result.result, 'PROVIDER_OUTCOME_UNKNOWN');
  });

  for (const [name, raw] of [
    ['invalid json', '{'],
    ['array root', '[]'],
    ['oversized', JSON.stringify({ padding: 'x'.repeat(20_000) })],
    ['unknown field', JSON.stringify({ schemaVersion: 1, unknown: true })],
    ['prototype key', '{"__proto__":{"polluted":true}}'],
  ]) {
    await t.test(name, async (t) => {
      const root = await mkdtemp(join(tmpdir(), 'tzg-send-intent-corrupt-'));
      t.after(() => rm(root, { recursive: true, force: true }));
      const decision = makeDecision();
      const attemptNumber = 13;
      const hash = hashSendIntentKey('feishu', decision.decisionId, attemptNumber);
      const directory = join(root, 'send-intents');
      const path = join(directory, `${hash}.json`);
      await mkdir(directory, { recursive: true });
      await writeFile(path, raw);
      let calls = 0;
      const result = await sendDecision({
        config: makeConfig({ stateRoot: root }),
        decision,
        attemptNumber,
        transport: { async sendInteractive() { calls += 1; } },
        intentStore: createSendIntentStore(root),
        health: makeHealth(),
        now: NOW,
      });
      assert.equal(result.result, 'PROVIDER_OUTCOME_UNKNOWN');
      assert.equal(calls, 0);
      assert.equal(await readFile(path, 'utf8'), raw);
    });
  }

  await t.test('live lock is busy', async (t) => {
    const root = await mkdtemp(join(tmpdir(), 'tzg-send-intent-busy-'));
    t.after(() => rm(root, { recursive: true, force: true }));
    const decision = makeDecision();
    const attemptNumber = 14;
    const hash = hashSendIntentKey('feishu', decision.decisionId, attemptNumber);
    const directory = join(root, 'send-intents');
    await mkdir(directory, { recursive: true });
    await writeFile(join(directory, `${hash}.lock`), JSON.stringify({ pid: process.pid, time: NOW.toISOString() }));
    let calls = 0;
    const result = await sendDecision({
      config: makeConfig({ stateRoot: root }),
      decision,
      attemptNumber,
      transport: { async sendInteractive() { calls += 1; } },
      intentStore: createSendIntentStore(root, { pidProbe: () => true }),
      health: makeHealth(),
      now: NOW,
    });
    assert.equal(result.result, 'PROVIDER_OUTCOME_UNKNOWN');
    assert.equal(calls, 0);
  });

  await t.test('dead lock older than 120 seconds is reclaimed once', async (t) => {
    const root = await mkdtemp(join(tmpdir(), 'tzg-send-intent-stale-lock-'));
    t.after(() => rm(root, { recursive: true, force: true }));
    const decision = makeDecision();
    const attemptNumber = 15;
    const hash = hashSendIntentKey('feishu', decision.decisionId, attemptNumber);
    const directory = join(root, 'send-intents');
    const lockPath = join(directory, `${hash}.lock`);
    await mkdir(directory, { recursive: true });
    await writeFile(lockPath, JSON.stringify({
      pid: 999_999,
      time: new Date(NOW.getTime() - 120_001).toISOString(),
    }));
    let calls = 0;
    const result = await sendDecision({
      config: makeConfig({ stateRoot: root }),
      decision,
      attemptNumber,
      transport: { async sendInteractive() { calls += 1; return { messageId: 'om_after_stale' }; } },
      intentStore: createSendIntentStore(root, { pidProbe: () => false }),
      health: makeHealth(),
      now: NOW,
    });
    assert.equal(result.result, 'PROVIDER_ACCEPTED');
    assert.equal(calls, 1);
    await assert.rejects(access(lockPath));
  });
});

test('two concurrent calls for one intent invoke transport exactly once', async (t) => {
  const root = await mkdtemp(join(tmpdir(), 'tzg-send-intent-concurrent-'));
  t.after(() => rm(root, { recursive: true, force: true }));
  const config = makeConfig({ stateRoot: root });
  const store = createSendIntentStore(root);
  let calls = 0;
  let release;
  let started;
  const startedPromise = new Promise((resolveStarted) => { started = resolveStarted; });
  const releasePromise = new Promise((resolveRelease) => { release = resolveRelease; });
  const transport = {
    async sendInteractive() {
      calls += 1;
      started();
      await releasePromise;
      return { messageId: 'om_concurrent' };
    },
  };
  const request = {
    config,
    decision: makeDecision(),
    attemptNumber: 16,
    transport,
    intentStore: store,
    health: makeHealth(),
    now: NOW,
  };
  const firstPromise = sendDecision(request);
  await startedPromise;
  const second = await sendDecision(request);
  assert.equal(second.result, 'PROVIDER_OUTCOME_UNKNOWN');
  assert.equal(calls, 1);
  release();
  assert.equal((await firstPromise).result, 'PROVIDER_ACCEPTED');
  assert.equal(calls, 1);
});
