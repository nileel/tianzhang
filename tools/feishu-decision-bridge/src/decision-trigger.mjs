import { spawn as nodeSpawn } from 'node:child_process';
import { homedir as systemHomedir } from 'node:os';
import {
  dirname, isAbsolute, join, resolve,
} from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

import { main as consumeReplyMain } from './consume-reply.mjs';

const IDENTIFIER_PATTERN = /^[A-Za-z0-9][A-Za-z0-9._:-]{0,255}$/;
const MAX_PROCESS_OUTPUT_BYTES = 64 * 1024;
const SOURCE_DIRECTORY = dirname(fileURLToPath(import.meta.url));
const DEFAULT_LEASE_TOOL_PATH = resolve(
  SOURCE_DIRECTORY,
  '..',
  '..',
  'hourly-automation-lease.ps1',
);
const DEFAULT_INVOKER_PATH = resolve(
  SOURCE_DIRECTORY,
  '..',
  '..',
  'invoke-codex-responsibility.ps1',
);

function isPlainObject(value) {
  if (value === null || typeof value !== 'object' || Array.isArray(value)) {
    return false;
  }
  const prototype = Object.getPrototypeOf(value);
  return prototype === Object.prototype || prototype === null;
}

function requireIdentifier(value, name) {
  if (typeof value !== 'string' || !IDENTIFIER_PATTERN.test(value)) {
    throw new Error(`Invalid ${name}`);
  }
  return value;
}

function requireStableText(value, name) {
  if (
    typeof value !== 'string'
    || value.trim().length === 0
    || value.length > 512
    || /[\u0000-\u001f\u007f]/u.test(value)
  ) {
    throw new Error(`Invalid ${name}`);
  }
  return value;
}

function requireAbsolutePath(value, name) {
  if (typeof value !== 'string' || !isAbsolute(value)) {
    throw new Error(`Invalid ${name}`);
  }
  return resolve(value);
}

function defaultRuntimeRoot(homedir = systemHomedir) {
  return join(
    homedir(),
    '.codex',
    'automation-state',
    'tzg-hourly-controller-runtime',
  );
}

async function waitForProcess(child, input) {
  return new Promise((resolvePromise, rejectPromise) => {
    let stdout = '';
    let stdoutBytes = 0;
    let stderrBytes = 0;
    let settled = false;
    const rejectOnce = () => {
      if (!settled) {
        settled = true;
        rejectPromise(new Error('Process failed'));
      }
    };
    child.stdout?.on('data', (chunk) => {
      stdoutBytes += chunk.length;
      if (stdoutBytes > MAX_PROCESS_OUTPUT_BYTES) {
        rejectOnce();
        return;
      }
      stdout += chunk.toString('utf8');
    });
    child.stderr?.on('data', (chunk) => {
      stderrBytes += chunk.length;
      if (stderrBytes > MAX_PROCESS_OUTPUT_BYTES) {
        rejectOnce();
      }
    });
    child.stdin?.on('error', rejectOnce);
    child.once('error', rejectOnce);
    child.once('close', (code) => {
      if (settled) {
        return;
      }
      settled = true;
      if (!Number.isInteger(code)) {
        rejectPromise(new Error('Process failed'));
        return;
      }
      resolvePromise({ code, stdout });
    });
    if (input === undefined) {
      child.stdin?.end();
    } else {
      child.stdin?.end(input);
    }
  });
}

function parseSingleJson(stdout, context) {
  const lines = stdout
    .split(/\r?\n/u)
    .filter((line) => line.trim().length > 0);
  if (lines.length !== 1) {
    throw new Error(`Invalid ${context} response`);
  }
  let parsed;
  try {
    parsed = JSON.parse(lines[0]);
  } catch {
    throw new Error(`Invalid ${context} response`);
  }
  if (!isPlainObject(parsed) || typeof parsed.status !== 'string') {
    throw new Error(`Invalid ${context} response`);
  }
  return parsed;
}

function leaseArguments(request, stateRoot) {
  const args = [
    '-NoProfile',
    '-ExecutionPolicy',
    'Bypass',
    '-File',
    DEFAULT_LEASE_TOOL_PATH,
    '-Action',
    request.action,
    '-StateRoot',
    stateRoot,
  ];
  if (request.action === 'Acquire') {
    args.push(
      '-TaskId',
      request.taskId,
      '-Owner',
      request.owner,
      '-RepositoryRoot',
      request.repositoryRoot,
      '-ResumeRecovery',
      '-DecisionId',
      request.decisionId,
    );
  }
  return args;
}

async function invokeLeaseDefault(request, options) {
  const child = (options.spawnChild ?? nodeSpawn)(
    'pwsh',
    leaseArguments(request, options.stateRoot),
    {
      windowsHide: true,
      stdio: ['ignore', 'pipe', 'pipe'],
    },
  );
  const completed = await waitForProcess(child);
  return parseSingleJson(completed.stdout, 'lease');
}

async function consumeReplyDefault({ decisionRequestPath }) {
  let output = '';
  const code = await consumeReplyMain(
    ['--request-file', decisionRequestPath],
    {
      stdout: {
        write(chunk) {
          output += String(chunk);
        },
      },
    },
  );
  if (code !== 0) {
    throw new Error('Reply consumption failed');
  }
  const lines = output.split(/\r?\n/u).filter((line) => line.trim().length > 0);
  if (lines.length !== 1) {
    throw new Error('Reply consumption failed');
  }
  let result;
  try {
    result = JSON.parse(lines[0]);
  } catch {
    throw new Error('Reply consumption failed');
  }
  if (!isPlainObject(result)) {
    throw new Error('Reply consumption failed');
  }
  return result;
}

function invokerArguments(request) {
  return [
    '-NoProfile',
    '-ExecutionPolicy',
    'Bypass',
    '-File',
    DEFAULT_INVOKER_PATH,
    '-Action',
    'Start',
    '-Route',
    'Recovery',
    '-RepositoryRoot',
    request.repositoryRoot,
    '-TaskId',
    request.taskId,
    '-RunId',
    request.runId,
    '-StateRoot',
    request.stateRoot,
    '-Model',
    request.model,
    '-DecisionId',
    request.decisionId,
    '-ReadDecisionReplyFromStdin',
  ];
}

async function invokeResponsibilityDefault(request, options) {
  const child = (options.spawnChild ?? nodeSpawn)(
    'pwsh',
    invokerArguments(request),
    {
      cwd: request.repositoryRoot,
      windowsHide: true,
      stdio: ['pipe', 'pipe', 'pipe'],
    },
  );
  const completed = await waitForProcess(child, request.reply);
  return parseSingleJson(completed.stdout, 'responsibility');
}

function validateDecisionRecovery(showResponse) {
  if (!isPlainObject(showResponse) || !isPlainObject(showResponse.state)) {
    throw new Error('Invalid runtime state');
  }
  const recovery = showResponse.state.recovery;
  if (recovery === null || recovery === undefined || recovery.trigger !== 'decision') {
    return null;
  }
  if (
    !isPlainObject(recovery)
    || Object.hasOwn(recovery, 'resumeKind')
    || Object.hasOwn(recovery, 'resumeId')
  ) {
    throw new Error('Invalid decision recovery');
  }
  return {
    taskId: requireIdentifier(recovery.taskId, 'taskId'),
    owner: requireStableText(recovery.owner, 'owner'),
    repositoryRoot: requireAbsolutePath(recovery.repositoryRoot, 'repositoryRoot'),
    decisionId: requireIdentifier(recovery.decisionId, 'decisionId'),
    decisionRequestPath: requireAbsolutePath(
      recovery.decisionRequestPath,
      'decisionRequestPath',
    ),
  };
}

function decisionReply(consumed, decisionId) {
  if (consumed?.result === 'NO_REPLY') {
    return null;
  }
  if (
    consumed?.result === 'OPTION_ACCEPTED'
    && ['A', 'B', 'C'].includes(consumed.optionKey)
  ) {
    return consumed.optionKey;
  }
  if (
    consumed?.result === 'CUSTOM_ACCEPTED'
    && consumed.decisionId === decisionId
    && typeof consumed.customText === 'string'
    && consumed.customText.trim().length > 0
    && consumed.customText.length <= 4000
  ) {
    return consumed.customText;
  }
  throw new Error('Invalid decision reply');
}

function validateAcquired(value, recovery) {
  if (
    !isPlainObject(value)
    || value.status !== 'RECOVERY_ACQUIRED'
    || value.trigger !== 'decision'
    || value.taskId !== recovery.taskId
    || value.owner !== recovery.owner
    || resolve(value.repositoryRoot ?? '') !== recovery.repositoryRoot
    || value.decisionId !== recovery.decisionId
    || Object.hasOwn(value, 'resumeKind')
    || Object.hasOwn(value, 'resumeId')
  ) {
    throw new Error('Invalid recovery acquisition');
  }
  return {
    runId: requireIdentifier(value.runId, 'runId'),
  };
}

export async function runDecisionTrigger(options = {}) {
  const stateRoot = requireAbsolutePath(
    options.stateRoot ?? defaultRuntimeRoot(options.homedir),
    'stateRoot',
  );
  const model = requireStableText(options.model, 'model');
  const invokeLease = (request) => invokeLeaseDefault(request, {
    stateRoot,
    spawnChild: options.processSpawn,
  });
  const showState = options.showState ?? (() => invokeLease({ action: 'Show' }));
  const consumeReply = options.consumeReply ?? consumeReplyDefault;
  const acquireRecovery = options.acquireRecovery ?? ((recovery) => invokeLease({
    action: 'Acquire',
    ...recovery,
  }));
  const invokeResponsibility = options.invokeResponsibility
    ?? ((request) => invokeResponsibilityDefault(request, {
      spawnChild: options.processSpawn,
    }));
  if (
    typeof showState !== 'function'
    || typeof consumeReply !== 'function'
    || typeof acquireRecovery !== 'function'
    || typeof invokeResponsibility !== 'function'
  ) {
    throw new Error('Invalid trigger dependencies');
  }

  const recovery = validateDecisionRecovery(await showState());
  if (recovery === null) {
    return { status: 'not_applicable' };
  }
  const reply = decisionReply(
    await consumeReply({
      decisionId: recovery.decisionId,
      decisionRequestPath: recovery.decisionRequestPath,
    }),
    recovery.decisionId,
  );
  if (reply === null) {
    return { status: 'waiting_decision' };
  }
  const acquired = validateAcquired(
    await acquireRecovery({
      taskId: recovery.taskId,
      owner: recovery.owner,
      repositoryRoot: recovery.repositoryRoot,
      decisionId: recovery.decisionId,
    }),
    recovery,
  );
  return invokeResponsibility({
    action: 'Start',
    route: 'Recovery',
    repositoryRoot: recovery.repositoryRoot,
    taskId: recovery.taskId,
    runId: acquired.runId,
    stateRoot,
    model,
    decisionId: recovery.decisionId,
    reply,
  });
}

function parseCli(argv) {
  if (!Array.isArray(argv) || argv.length !== 4) {
    throw new Error('Invalid arguments');
  }
  const values = new Map();
  for (let index = 0; index < argv.length; index += 2) {
    const name = argv[index];
    const value = argv[index + 1];
    if (
      !['--state-root', '--model'].includes(name)
      || typeof value !== 'string'
      || values.has(name)
    ) {
      throw new Error('Invalid arguments');
    }
    values.set(name, value);
  }
  if (!values.has('--state-root') || !values.has('--model')) {
    throw new Error('Invalid arguments');
  }
  return {
    stateRoot: values.get('--state-root'),
    model: values.get('--model'),
  };
}

export async function main(argv = process.argv.slice(2)) {
  try {
    const result = await runDecisionTrigger(parseCli(argv));
    process.stdout.write(`${JSON.stringify(result)}\n`);
    return 0;
  } catch {
    process.stdout.write(`${JSON.stringify({ status: 'failed', detailCode: 'decision_trigger_error' })}\n`);
    return 22;
  }
}

const directExecution = process.argv[1] !== undefined
  && import.meta.url === pathToFileURL(resolve(process.argv[1])).href;

if (directExecution) {
  main().then((code) => {
    process.exitCode = code;
  }).catch(() => {
    process.stdout.write(`${JSON.stringify({ status: 'failed', detailCode: 'decision_trigger_error' })}\n`);
    process.exitCode = 22;
  });
}
