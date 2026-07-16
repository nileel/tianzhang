import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { mkdtemp, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import test from 'node:test';

import { buildPairingCard, main } from '../src/send-pairing.mjs';
import { main as canaryMain } from '../src/send-canary.mjs';

const APP_ID = 'cli_pairing_fixture';
const APP_SECRET = 'pairing_secret_never_log';
const RECIPIENT = 'pairing@example.invalid';
const NONCE = 'pairing-nonce-0123456789';
const NOW = new Date('2026-07-15T08:00:00.000Z');

function sha256(value) {
  return createHash('sha256').update(value, 'utf8').digest('hex');
}

function makeConfig(stateRoot) {
  return {
    schemaVersion: 1,
    appId: APP_ID,
    appSecret: APP_SECRET,
    recipient: { type: 'email', value: RECIPIENT },
    expectedTenantKey: null,
    pairedOperatorOpenIdHash: null,
    hmacKey: Buffer.alloc(32, 0x61).toString('base64'),
    stateRoot,
  };
}

function captureStdout() {
  let value = '';
  return {
    stdout: { write(chunk) { value += chunk; } },
    text() { return value; },
  };
}

test('buildPairingCard creates one binding button with the supplied nonce', () => {
  const card = buildPairingCard(NONCE);
  assert.equal(card.header.title.content, '天章飞书负责人配对');
  const action = card.elements.find((element) => element.tag === 'action');
  assert.equal(action.actions.length, 1);
  assert.deepEqual(action.actions[0].value, {
    kind: 'operator_pairing',
    pairingNonce: NONCE,
  });
  assert.throws(() => buildPairingCard('contains space'), /Invalid pairing request/);
});

test('pairing CLI sends an interactive card and emits hash-only evidence', async (t) => {
  const root = await mkdtemp(join(tmpdir(), 'tzg-pairing-cli-'));
  t.after(() => rm(root, { recursive: true, force: true }));
  const configPath = join(root, 'private.json');
  const requestPath = join(root, 'request.json');
  await writeFile(configPath, JSON.stringify(makeConfig(root)));
  await writeFile(requestPath, JSON.stringify({ pairingNonce: NONCE }));
  const capture = captureStdout();
  let transportRequest;
  const code = await main(['--request-file', requestPath], {
    env: { FEISHU_DECISION_CONFIG_PATH: configPath },
    stdout: capture.stdout,
    now: () => NOW,
    readHealth: async () => ({
      status: 'CONNECTED', updatedAt: NOW.toISOString(), pid: 42, pidAlive: true,
    }),
    createTransport: async () => ({
      async sendInteractive(request) {
        transportRequest = request;
        return { messageId: 'om_pairing_fixture' };
      },
    }),
  });
  assert.equal(code, 0);
  assert.deepEqual(transportRequest.params, { receive_id_type: 'email' });
  assert.equal(transportRequest.data.receive_id, RECIPIENT);
  assert.equal(transportRequest.data.msg_type, 'interactive');
  assert.equal(JSON.parse(transportRequest.data.content).elements.at(-1).actions[0].value.pairingNonce, NONCE);
  const output = JSON.parse(capture.text());
  assert.deepEqual(output, {
    result: 'PROVIDER_ACCEPTED',
    targetHash: sha256(RECIPIENT),
    providerMessageIdHash: sha256('om_pairing_fixture'),
  });
  for (const literal of [APP_ID, APP_SECRET, RECIPIENT, NONCE, 'om_pairing_fixture']) {
    assert.equal(capture.text().includes(literal), false);
  }
});

test('pairing CLI fails closed before transport when bridge health is unusable', async (t) => {
  const root = await mkdtemp(join(tmpdir(), 'tzg-pairing-unavailable-'));
  t.after(() => rm(root, { recursive: true, force: true }));
  const configPath = join(root, 'private.json');
  const requestPath = join(root, 'request.json');
  await writeFile(configPath, JSON.stringify(makeConfig(root)));
  await writeFile(requestPath, JSON.stringify({ pairingNonce: NONCE }));
  const capture = captureStdout();
  let transportCreated = false;
  const code = await main(['--request-file', requestPath], {
    env: { FEISHU_DECISION_CONFIG_PATH: configPath },
    stdout: capture.stdout,
    now: () => NOW,
    readHealth: async () => ({ status: 'CONNECTING', updatedAt: NOW.toISOString(), pid: 42, pidAlive: true }),
    createTransport: async () => { transportCreated = true; },
  });
  assert.equal(code, 20);
  assert.equal(transportCreated, false);
  assert.deepEqual(JSON.parse(capture.text()), { result: 'CHANNEL_UNAVAILABLE' });
});

test('canary CLI requires paired identity and emits only hashed delivery evidence', async (t) => {
  const root = await mkdtemp(join(tmpdir(), 'tzg-canary-cli-'));
  t.after(() => rm(root, { recursive: true, force: true }));
  const configPath = join(root, 'private.json');
  const requestPath = join(root, 'request.json');
  const operatorHash = sha256('ou_fixture_operator');
  await writeFile(configPath, JSON.stringify({
    ...makeConfig(root),
    expectedTenantKey: 'tenant_fixture',
    pairedOperatorOpenIdHash: operatorHash,
  }));
  const decision = {
    decisionId: 'DEC-20260716-CANARYFIXTURE',
    taskId: 'FEISHU-CANARY',
    question: '请选择 A。',
    options: [
      { key: 'A', label: '确认' },
      { key: 'B', label: '不确认' },
      { key: 'C', label: '稍后' },
    ],
    recommendedOption: 'A',
    impactSummary: '仅验证通道。',
  };
  const cardNonce = 'canary-nonce-0123456789';
  await writeFile(requestPath, JSON.stringify({ decision, cardNonce }));
  const capture = captureStdout();
  let transportRequest;
  const code = await canaryMain(['--request-file', requestPath], {
    env: { FEISHU_DECISION_CONFIG_PATH: configPath },
    stdout: capture.stdout,
    now: () => NOW,
    readHealth: async () => ({
      status: 'CONNECTED', updatedAt: NOW.toISOString(), pid: 42, pidAlive: true,
    }),
    createTransport: async () => ({
      async sendInteractive(request) {
        transportRequest = request;
        return { messageId: 'om_canary_fixture' };
      },
    }),
  });
  assert.equal(code, 0);
  const card = JSON.parse(transportRequest.data.content);
  const decisionActions = card.elements.find((element) => element.tag === 'action').actions;
  assert.equal(decisionActions[0].value.cardNonce, cardNonce);
  assert.deepEqual(JSON.parse(capture.text()), {
    result: 'PROVIDER_ACCEPTED',
    targetHash: sha256(RECIPIENT),
    providerMessageIdHash: sha256('om_canary_fixture'),
    cardNonceHash: sha256(cardNonce),
  });
  for (const literal of [APP_ID, APP_SECRET, RECIPIENT, cardNonce, 'om_canary_fixture']) {
    assert.equal(capture.text().includes(literal), false);
  }

  await writeFile(configPath, JSON.stringify(makeConfig(root)));
  const rejected = captureStdout();
  let transportCreated = false;
  const rejectedCode = await canaryMain(['--request-file', requestPath], {
    env: { FEISHU_DECISION_CONFIG_PATH: configPath },
    stdout: rejected.stdout,
    now: () => NOW,
    readHealth: async () => ({
      status: 'CONNECTED', updatedAt: NOW.toISOString(), pid: 42, pidAlive: true,
    }),
    createTransport: async () => { transportCreated = true; },
  });
  assert.equal(rejectedCode, 22);
  assert.equal(transportCreated, false);
  assert.deepEqual(JSON.parse(rejected.text()), { result: 'INVALID_INPUT' });
});
