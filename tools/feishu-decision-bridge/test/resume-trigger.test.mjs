import assert from 'node:assert/strict';
import { EventEmitter } from 'node:events';
import { dirname, join, resolve } from 'node:path';
import test from 'node:test';
import { fileURLToPath } from 'node:url';

import {
  createPostAcceptRelay,
  runResumeRelay,
} from '../src/resume-trigger.mjs';
import {
  makeCallback,
  makeMessageCallback,
} from '../src/bridge.mjs';
import { sha256 } from '../src/config.mjs';

const TEST_DIRECTORY = dirname(fileURLToPath(import.meta.url));
const CODEX_RESPONSIBILITY_INVOKER_PATH = resolve(
  TEST_DIRECTORY,
  '..',
  '..',
  'invoke-codex-responsibility.ps1',
);

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

function fakeSpawnRecorder({ fail = false } = {}) {
  const calls = [];
  const spawnChild = (command, args, options) => {
    const child = new EventEmitter();
    const call = {
      command,
      args: [...args],
      options: { ...options },
      stdin: '',
      unrefCalled: false,
    };
    child.stdin = {
      end(value) {
        call.stdin += value;
      },
    };
    child.unref = () => {
      call.unrefCalled = true;
    };
    calls.push(call);
    queueMicrotask(() => {
      if (fail) {
        child.emit('error', new Error('start failed'));
      } else {
        child.emit('spawn');
      }
    });
    return child;
  };
  return { calls, spawnChild };
}

test('card A/B/C and custom replies preserve responses without starting a relay', async () => {
  const stateRoot = 'C:\\private\\bridge-state';
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
    const normalized = normalizedCard(action, `event-${index}`);
    let relayed;
    const callback = makeCallback({
      loadConfig: async () => bridgeConfig(stateRoot),
      loadBindings: async () => [],
      fs: {},
      now: () => new Date('2026-07-18T00:00:00.000Z'),
      timers: inertTimers(),
      rememberSensitive() {},
      reportRejection() {},
      normalizeAction: () => normalized,
      handleAction: async () => {
        order.push('signed-inbox');
        return { accepted: true, response: expectedResponse };
      },
      postAccept(value) {
        order.push('relay');
        relayed = value;
      },
    });

    const actualResponse = await callback({ fixture: true });

    assert.strictEqual(actualResponse, expectedResponse);
    assert.deepEqual(order, ['signed-inbox']);
    assert.equal(relayed, undefined);
  }
});

test('text custom reply keeps Feishu confirmation text without starting a relay', async () => {
  const stateRoot = 'C:\\private\\bridge-state';
  const confirmation = '已登记 decision-one 自定义方案：\n保持兼容';
  const order = [];
  const replies = [];
  let relayed;
  const normalized = {
    eventId: 'event-text',
    tenantKey: 'tenant-key',
    openId: 'operator-id',
    messageId: 'message-id',
    chatId: 'chat-id',
  };
  const callback = makeMessageCallback({
    loadConfig: async () => bridgeConfig(stateRoot),
    loadBindings: async () => [],
    fs: {},
    now: () => new Date('2026-07-18T00:00:00.000Z'),
    replyText: async (_messageId, text) => {
      replies.push(text);
    },
    rememberSensitive() {},
    reportRejection() {},
    normalizeMessage: () => normalized,
    handleMessage: async ({ replyText }) => {
      order.push('signed-inbox');
      await replyText('message-id', confirmation);
      return { accepted: true, rejectionCode: null, decisionId: 'decision-one' };
    },
    postAccept(value) {
      order.push('relay');
      relayed = value;
    },
  });

  const externalResult = await callback({ fixture: true });

  assert.equal(externalResult, undefined);
  assert.deepEqual(replies, [confirmation]);
  assert.deepEqual(order, ['signed-inbox']);
  assert.equal(relayed, undefined);
});

test('accepted duplicate event schedules one hidden detached relay helper', () => {
  const scheduled = [];
  const spawned = [];
  const relay = createPostAcceptRelay({
    schedule: (callback) => scheduled.push(callback),
    spawnDetached: (specification) => spawned.push(specification),
    nodeExecutable: 'node.exe',
    scriptPath: 'C:\\private\\resume-trigger.mjs',
    stateRoot: 'C:\\private\\runtime',
  });
  const accepted = {
    decisionId: 'decision-one',
    replyPath: 'C:\\private\\inbox\\reply.json',
  };

  assert.equal(relay(accepted), true);
  assert.equal(relay(accepted), false);
  assert.equal(scheduled.length, 1);
  scheduled[0]();

  assert.equal(spawned.length, 1);
  assert.equal(spawned[0].command, 'node.exe');
  assert.deepEqual(spawned[0].args, [
    'C:\\private\\resume-trigger.mjs',
    '--queue',
    '--decision-id',
    'decision-one',
    '--reply-path',
    'C:\\private\\inbox\\reply.json',
    '--state-root',
    'C:\\private\\runtime',
  ]);
  assert.equal(spawned[0].options.detached, true);
  assert.equal(spawned[0].options.windowsHide, true);
  assert.equal(spawned[0].options.stdio, 'ignore');
});

test('QUEUED and dispatch-ready EMPTY return immediately without model start', async () => {
  const leaseCalls = [];
  let consumeCalls = 0;
  const spawnRecorder = fakeSpawnRecorder();
  const common = {
    stateRoot: 'C:\\private\\runtime',
    consumeReply: async () => {
      consumeCalls += 1;
      return { kind: 'option', optionKey: 'A' };
    },
    spawnChild: spawnRecorder.spawnChild,
  };
  const queued = await runResumeRelay({
    mode: 'queue',
    decisionId: 'decision-one',
    replyPath: 'C:\\private\\inbox\\reply.json',
    ...common,
    invokeLease: async (request) => {
      leaseCalls.push(request);
      return { status: 'QUEUED' };
    },
  });
  const empty = await runResumeRelay({
    mode: 'dispatch-ready',
    ...common,
    invokeLease: async (request) => {
      leaseCalls.push(request);
      return { status: 'EMPTY' };
    },
  });

  assert.deepEqual(queued, { status: 'QUEUED' });
  assert.deepEqual(empty, { status: 'EMPTY' });
  assert.deepEqual(leaseCalls.map((call) => call.action), ['QueueResume', 'TakeResume']);
  assert.equal(consumeCalls, 0);
  assert.equal(spawnRecorder.calls.length, 0);
});

test('DISPATCH accepts the production Codex owner and resumes with option through stdin', async () => {
  const leaseCalls = [];
  const consumed = [];
  const spawnRecorder = fakeSpawnRecorder();
  const providerHash = 'a'.repeat(64);
  const secret = 'config-secret-value';
  const result = await runResumeRelay({
    mode: 'queue',
    stateRoot: 'C:\\private\\runtime',
    decisionId: 'decision-one',
    replyPath: `C:\\private\\inbox\\${providerHash}.json`,
    invokeLease: async (request) => {
      leaseCalls.push(request);
      return {
        status: 'DISPATCH',
        runId: 'run-codex',
        taskId: 'task-one',
        owner: 'Codex/gpt-5.6-terra',
        repositoryRoot: 'C:\\repo',
        resumeKind: 'codex',
        resumeId: 'session-codex',
        decisionId: 'decision-one',
        decisionRequestPath: 'C:\\private\\request.json',
        replyPath: `C:\\private\\inbox\\${providerHash}.json`,
      };
    },
    consumeReply: async (request) => {
      consumed.push(request);
      return {
        kind: 'option',
        optionKey: 'A',
        providerEventIdHash: providerHash,
        messageId: 'provider-message-id',
        secret,
      };
    },
    spawnChild: spawnRecorder.spawnChild,
  });

  assert.deepEqual(result, { status: 'DISPATCHED' });
  assert.equal(leaseCalls.length, 1);
  assert.deepEqual(consumed, [{
    decisionId: 'decision-one',
    decisionRequestPath: 'C:\\private\\request.json',
    replyPath: `C:\\private\\inbox\\${providerHash}.json`,
  }]);
  assert.equal(spawnRecorder.calls.length, 1);
  const call = spawnRecorder.calls[0];
  assert.equal(call.command, 'pwsh');
  assert.deepEqual(call.args, [
    '-NoProfile',
    '-ExecutionPolicy',
    'Bypass',
    '-File',
    CODEX_RESPONSIBILITY_INVOKER_PATH,
    '-Action',
    'Resume',
    '-Route',
    'Recovery',
    '-RepositoryRoot',
    'C:\\repo',
    '-TaskId',
    'task-one',
    '-RunId',
    'run-codex',
    '-StateRoot',
    'C:\\private\\runtime',
    '-SessionId',
    'session-codex',
    '-DecisionId',
    'decision-one',
    '-ReadDecisionReplyFromStdin',
  ]);
  assert.equal(call.options.cwd, 'C:\\repo');
  assert.equal(call.options.detached, true);
  assert.equal(call.options.windowsHide, true);
  assert.equal(call.unrefCalled, true);
  assert.equal(call.stdin, 'A');
  assert.equal(call.args.includes('A'), false);
  assert.equal(call.stdin.includes(providerHash), false);
  assert.equal(call.stdin.includes('provider-message-id'), false);
  assert.equal(call.stdin.includes(secret), false);
  assert.equal(JSON.stringify(result).includes(providerHash), false);
});

test('DISPATCH resumes Claude with custom text through stdin only', async () => {
  const spawnRecorder = fakeSpawnRecorder();
  const customText = '保持原任务边界，不新增内容';
  const result = await runResumeRelay({
    mode: 'dispatch-ready',
    stateRoot: 'C:\\private\\runtime',
    invokeLease: async () => ({
      status: 'DISPATCH',
      runId: 'run-claude',
      taskId: 'task-two',
      owner: 'external',
      repositoryRoot: 'C:\\repo',
      resumeKind: 'claude',
      resumeId: 'session-claude',
      decisionId: 'decision-two',
      decisionRequestPath: 'C:\\private\\request-two.json',
      replyPath: 'C:\\private\\inbox\\reply-two.json',
    }),
    consumeReply: async () => ({ kind: 'custom', customText }),
    spawnChild: spawnRecorder.spawnChild,
  });

  assert.deepEqual(result, { status: 'DISPATCHED' });
  const call = spawnRecorder.calls[0];
  assert.equal(call.command, 'claude');
  assert.deepEqual(call.args, ['--resume', 'session-claude', '--print']);
  assert.equal(call.args.join(' ').includes(customText), false);
  assert.equal(call.stdin, `[TZG_DECISION_RESUME runId=run-claude]\n${customText}`);
});

test('reply consumption failure requeues the same dispatch and releases the lease', async () => {
  const leaseCalls = [];
  const spawnRecorder = fakeSpawnRecorder();
  const result = await runResumeRelay({
    mode: 'queue',
    stateRoot: 'C:\\private\\runtime',
    decisionId: 'decision-consume-failure',
    replyPath: 'C:\\private\\inbox\\reply-consume-failure.json',
    invokeLease: async (request) => {
      leaseCalls.push(request);
      if (request.action === 'QueueResume' && leaseCalls.length === 1) {
        return {
          status: 'DISPATCH',
          runId: 'run-consume-failure',
          taskId: 'task-consume-failure',
          owner: 'codex',
          repositoryRoot: 'C:\\repo',
          resumeKind: 'codex',
          resumeId: 'session-consume-failure',
          decisionId: 'decision-consume-failure',
          decisionRequestPath: 'C:\\private\\request-consume-failure.json',
          replyPath: 'C:\\private\\inbox\\reply-consume-failure.json',
        };
      }
      if (request.action === 'RecordResult') return { status: 'RECORDED' };
      if (request.action === 'QueueResume') return { status: 'QUEUED' };
      if (request.action === 'Release') return { status: 'RELEASED' };
      throw new Error('Unexpected lease action');
    },
    consumeReply: async () => {
      throw new Error('invalid signed reply');
    },
    spawnChild: spawnRecorder.spawnChild,
  });

  assert.deepEqual(result, { status: 'CONSUME_FAILED' });
  assert.deepEqual(
    leaseCalls.map((request) => request.action),
    ['QueueResume', 'RecordResult', 'QueueResume', 'Release'],
  );
  assert.equal(leaseCalls[1].detailCode, 'resume_reply_consume_failed');
  assert.equal(leaseCalls[2].replyPath, 'C:\\private\\inbox\\reply-consume-failure.json');
  assert.equal(leaseCalls[3].runId, 'run-consume-failure');
  assert.equal(spawnRecorder.calls.length, 0);
});

test('model start failure records failure, requeues once, and releases lease', async () => {
  const leaseCalls = [];
  const spawnRecorder = fakeSpawnRecorder({ fail: true });
  const result = await runResumeRelay({
    mode: 'queue',
    stateRoot: 'C:\\private\\runtime',
    decisionId: 'decision-failure',
    replyPath: 'C:\\private\\inbox\\reply-failure.json',
    invokeLease: async (request) => {
      leaseCalls.push(request);
      if (leaseCalls.length === 1) {
        return {
          status: 'DISPATCH',
          runId: 'run-failure',
          taskId: 'task-failure',
          owner: 'codex',
          repositoryRoot: 'C:\\repo',
          resumeKind: 'codex',
          resumeId: 'session-failure',
          decisionId: 'decision-failure',
          decisionRequestPath: 'C:\\private\\request-failure.json',
          replyPath: 'C:\\private\\inbox\\reply-failure.json',
        };
      }
      if (request.action === 'QueueResume') {
        return { status: 'QUEUED' };
      }
      if (request.action === 'RecordResult') {
        return { status: 'RECORDED' };
      }
      return { status: 'RELEASED' };
    },
    consumeReply: async () => ({ kind: 'option', optionKey: 'B' }),
    spawnChild: spawnRecorder.spawnChild,
  });

  assert.deepEqual(result, { status: 'START_FAILED' });
  assert.deepEqual(
    leaseCalls.map((request) => request.action),
    ['QueueResume', 'RecordResult', 'QueueResume', 'Release'],
  );
  assert.equal(leaseCalls.filter((request) => (
    request.action === 'QueueResume'
    && request.replyPath === 'C:\\private\\inbox\\reply-failure.json'
  )).length, 2);
  assert.equal(leaseCalls.at(-1).runId, 'run-failure');
});
