import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { randomUUID } from 'node:crypto';
import { rm } from 'node:fs/promises';
import { homedir } from 'node:os';
import {
  dirname, join, resolve, sep,
} from 'node:path';
import test from 'node:test';
import { fileURLToPath } from 'node:url';

import { runDecisionTrigger } from '../src/decision-trigger.mjs';
import {
  makeCallback,
  makeMessageCallback,
} from '../src/bridge.mjs';

const TEST_DIRECTORY = dirname(fileURLToPath(import.meta.url));
const PROJECT_ROOT = resolve(TEST_DIRECTORY, '..', '..', '..');
const LEASE_TOOL_PATH = resolve(TEST_DIRECTORY, '..', '..', 'hourly-automation-lease.ps1');

async function runProcessBytes(command, args) {
  const child = spawn(command, args, {
    windowsHide: true,
    stdio: ['ignore', 'pipe', 'pipe'],
  });
  const stdout = [];
  const stderr = [];
  child.stdout.on('data', (chunk) => stdout.push(Buffer.from(chunk)));
  child.stderr.on('data', (chunk) => stderr.push(Buffer.from(chunk)));
  const code = await new Promise((resolvePromise, rejectPromise) => {
    child.once('error', rejectPromise);
    child.once('close', resolvePromise);
  });
  return {
    code,
    stdout: Buffer.concat(stdout),
    stderr: Buffer.concat(stderr).toString('utf8'),
  };
}

function decisionState() {
  return {
    status: 'SHOWN',
    state: {
      lease: null,
      recovery: {
        trigger: 'decision',
        runId: 'old-run',
        taskId: 'task-one',
        owner: 'codex',
        repositoryRoot: 'C:\\repo',
        decisionId: 'decision-one',
        decisionRequestPath: 'C:\\private\\decision-request.json',
        hasUncommittedChanges: false,
        changedPaths: [],
      },
    },
  };
}

function acquiredDecision() {
  return {
    status: 'RECOVERY_ACQUIRED',
    runId: 'new-run',
    taskId: 'task-one',
    owner: 'codex',
    repositoryRoot: 'C:\\repo',
    trigger: 'decision',
    decisionId: 'decision-one',
    decisionRequestPath: 'C:\\private\\decision-request.json',
    changedPaths: [],
  };
}

test('decision recovery without a reply waits without acquiring or invoking', async () => {
  const acquireCalls = [];
  const invokeCalls = [];
  const result = await runDecisionTrigger({
    stateRoot: 'C:\\private\\runtime',
    model: 'gpt-test',
    showState: async () => decisionState(),
    consumeReply: async () => ({ result: 'NO_REPLY' }),
    acquireRecovery: async (value) => {
      acquireCalls.push(value);
      return acquiredDecision();
    },
    invokeResponsibility: async (value) => {
      invokeCalls.push(value);
      return { status: 'completed' };
    },
  });

  assert.deepEqual(result, { status: 'waiting_decision' });
  assert.equal(acquireCalls.length, 0);
  assert.equal(invokeCalls.length, 0);
});

test('accepted decision starts a fresh recovery session with no old session id', async () => {
  const acquireCalls = [];
  const invokeCalls = [];
  const terminal = {
    status: 'completed',
    category: 'success',
    taskId: 'task-one',
    runId: 'new-run',
    sessionId: 'fresh-session',
    commitSha: '0123456789abcdef0123456789abcdef01234567',
  };
  const result = await runDecisionTrigger({
    stateRoot: 'C:\\private\\runtime',
    model: 'gpt-test',
    showState: async () => decisionState(),
    consumeReply: async (value) => {
      assert.deepEqual(value, {
        decisionId: 'decision-one',
        decisionRequestPath: 'C:\\private\\decision-request.json',
      });
      return { result: 'OPTION_ACCEPTED', optionKey: 'A' };
    },
    acquireRecovery: async (value) => {
      acquireCalls.push(value);
      return acquiredDecision();
    },
    invokeResponsibility: async (value) => {
      invokeCalls.push(value);
      return terminal;
    },
  });

  assert.strictEqual(result, terminal);
  assert.deepEqual(acquireCalls, [{
    taskId: 'task-one',
    owner: 'codex',
    repositoryRoot: 'C:\\repo',
    decisionId: 'decision-one',
  }]);
  assert.equal(invokeCalls.length, 1);
  assert.deepEqual(invokeCalls[0], {
    action: 'Start',
    route: 'Recovery',
    repositoryRoot: 'C:\\repo',
    taskId: 'task-one',
    runId: 'new-run',
    stateRoot: 'C:\\private\\runtime',
    model: 'gpt-test',
    decisionId: 'decision-one',
    reply: 'A',
  });
  assert.equal(invokeCalls[0].sessionId, undefined);
});

test('lease JSON stays UTF-8 when PowerShell runs from a Unicode absolute path', {
  skip: process.platform !== 'win32',
}, async () => {
  const automationStateRoot = resolve(homedir(), '.codex', 'automation-state');
  const stateRoot = join(
    automationStateRoot,
    `tzg-hourly-controller-encoding-test-${randomUUID()}`,
  );
  try {
    const completed = await runProcessBytes('pwsh', [
      '-NoProfile',
      '-ExecutionPolicy',
      'Bypass',
      '-File',
      LEASE_TOOL_PATH,
      '-Action',
      'Acquire',
      '-StateRoot',
      stateRoot,
      '-TaskId',
      'encoding-test',
      '-Owner',
      'codex',
      '-RepositoryRoot',
      PROJECT_ROOT,
    ]);

    assert.equal(completed.code, 0, completed.stderr);
    const text = new TextDecoder('utf-8', { fatal: true }).decode(completed.stdout);
    const result = JSON.parse(text);
    assert.equal(result.repositoryRoot, PROJECT_ROOT);
  } finally {
    const prefix = automationStateRoot.endsWith(sep)
      ? automationStateRoot
      : automationStateRoot + sep;
    assert.ok(stateRoot.startsWith(prefix));
    await rm(stateRoot, { recursive: true, force: true });
  }
});

test('non-decision recovery is left for the interruption path', async () => {
  let consumed = false;
  const shown = decisionState();
  shown.state.recovery.trigger = 'interruption';
  shown.state.recovery.resumeKind = 'codex';
  shown.state.recovery.resumeId = 'old-session';
  delete shown.state.recovery.decisionId;
  delete shown.state.recovery.decisionRequestPath;
  const result = await runDecisionTrigger({
    stateRoot: 'C:\\private\\runtime',
    model: 'gpt-test',
    showState: async () => shown,
    consumeReply: async () => {
      consumed = true;
      return { result: 'NO_REPLY' };
    },
  });

  assert.deepEqual(result, { status: 'not_applicable' });
  assert.equal(consumed, false);
});

function inertTimers() {
  return {
    setTimeout() {
      return 1;
    },
    clearTimeout() {},
  };
}

function normalizedCard(action, eventId = `event-${action.optionKey ?? 'custom'}`) {
  return {
    eventId,
    appId: 'app-id',
    headerTenantKey: 'tenant-key',
    operatorTenantKey: 'tenant-key',
    operatorOpenId: 'operator-id',
    messageId: 'message-id',
    action: {
      decisionId: 'decision-one',
      cardNonce: 'nonce-one',
      ...action,
    },
  };
}

function bridgeConfig(stateRoot) {
  return {
    stateRoot,
    appId: 'app-id',
    appSecret: 'app-secret',
    recipient: { value: 'recipient-id' },
    expectedTenantKey: 'tenant-key',
    hmacKey: 'hmac-key',
  };
}

test('card replies persist responses without starting model work', async () => {
  const cases = [
    { kind: 'decision_reply', optionKey: 'A' },
    { kind: 'decision_reply', optionKey: 'B' },
    { kind: 'decision_reply', optionKey: 'C' },
    { kind: 'decision_custom_reply', customText: '维持当前边界' },
  ];
  for (const [index, action] of cases.entries()) {
    const order = [];
    const expectedResponse = {
      toast: { type: 'success', content: action.optionKey ?? '已登记自定义方案' },
      card: { type: 'raw', data: { marker: index } },
    };
    let relayed;
    const callback = makeCallback({
      loadConfig: async () => bridgeConfig('C:\\private\\bridge-state'),
      loadBindings: async () => [],
      fs: {},
      now: () => new Date('2026-07-18T00:00:00.000Z'),
      timers: inertTimers(),
      rememberSensitive() {},
      reportRejection() {},
      normalizeAction: () => normalizedCard(action, `event-${index}`),
      handleAction: async () => {
        order.push('signed-inbox');
        return { accepted: true, response: expectedResponse };
      },
      postAccept(value) {
        order.push('model-work');
        relayed = value;
      },
    });

    assert.strictEqual(await callback({ fixture: true }), expectedResponse);
    assert.deepEqual(order, ['signed-inbox']);
    assert.equal(relayed, undefined);
  }
});

test('text reply persists and confirms without starting model work', async () => {
  const confirmation = '已登记 decision-one 自定义方案：\n保持兼容';
  const order = [];
  const replies = [];
  let relayed;
  const callback = makeMessageCallback({
    loadConfig: async () => bridgeConfig('C:\\private\\bridge-state'),
    loadBindings: async () => [],
    fs: {},
    now: () => new Date('2026-07-18T00:00:00.000Z'),
    replyText: async (_messageId, text) => {
      replies.push(text);
    },
    rememberSensitive() {},
    reportRejection() {},
    normalizeMessage: () => ({
      eventId: 'event-text',
      tenantKey: 'tenant-key',
      openId: 'operator-id',
      messageId: 'message-id',
      chatId: 'chat-id',
    }),
    handleMessage: async ({ replyText }) => {
      order.push('signed-inbox');
      await replyText('message-id', confirmation);
      return { accepted: true, rejectionCode: null, decisionId: 'decision-one' };
    },
    postAccept(value) {
      order.push('model-work');
      relayed = value;
    },
  });

  assert.equal(await callback({ fixture: true }), undefined);
  assert.deepEqual(replies, [confirmation]);
  assert.deepEqual(order, ['signed-inbox']);
  assert.equal(relayed, undefined);
});
