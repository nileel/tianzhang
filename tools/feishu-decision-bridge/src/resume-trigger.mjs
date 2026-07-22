import { spawn as nodeSpawn } from 'node:child_process';
import { homedir as systemHomedir } from 'node:os';
import {
  basename, dirname, isAbsolute, join, resolve,
} from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

import { main as consumeReplyMain } from './consume-reply.mjs';

const IDENTIFIER_PATTERN = /^[A-Za-z0-9][A-Za-z0-9._:-]{0,255}$/;
const HEX_FILE_PATTERN = /^[0-9a-f]{64}\.json$/;
const MAX_PROCESS_OUTPUT_BYTES = 64 * 1024;
const MAX_RELAY_DEDUPLICATION_KEYS = 4096;
const SOURCE_DIRECTORY = dirname(fileURLToPath(import.meta.url));
const DEFAULT_LEASE_TOOL_PATH = resolve(
  SOURCE_DIRECTORY,
  '..',
  '..',
  'hourly-automation-lease.ps1',
);
const DEFAULT_CODEX_SESSION_RUNNER_PATH = resolve(
  SOURCE_DIRECTORY,
  '..',
  '..',
  'codex-cli-session.ps1',
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

function requireOwner(value) {
  if (
    typeof value !== 'string'
    || value.trim().length === 0
    || value.length > 256
    || /[\u0000-\u001f\u007f]/u.test(value)
  ) {
    throw new Error('Invalid owner');
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

function spawnDetachedDefault(specification) {
  const child = nodeSpawn(
    specification.command,
    specification.args,
    specification.options,
  );
  child.unref();
  return child;
}

export function createPostAcceptRelay(options = {}) {
  const schedule = options.schedule ?? globalThis.setImmediate;
  const spawnDetached = options.spawnDetached ?? spawnDetachedDefault;
  const nodeExecutable = options.nodeExecutable ?? process.execPath;
  const scriptPath = requireAbsolutePath(
    options.scriptPath ?? fileURLToPath(import.meta.url),
    'scriptPath',
  );
  const stateRoot = requireAbsolutePath(
    options.stateRoot ?? defaultRuntimeRoot(options.homedir),
    'stateRoot',
  );
  if (typeof schedule !== 'function' || typeof spawnDetached !== 'function') {
    throw new Error('Invalid post-accept relay dependencies');
  }
  const scheduled = new Set();

  return ({ decisionId, replyPath }) => {
    const normalizedDecisionId = requireIdentifier(decisionId, 'decisionId');
    const normalizedReplyPath = requireAbsolutePath(replyPath, 'replyPath');
    const key = `${normalizedDecisionId}\u0000${normalizedReplyPath.toUpperCase()}`;
    if (scheduled.has(key)) {
      return false;
    }
    if (scheduled.size >= MAX_RELAY_DEDUPLICATION_KEYS) {
      scheduled.delete(scheduled.values().next().value);
    }
    scheduled.add(key);
    schedule(() => {
      try {
        spawnDetached({
          command: nodeExecutable,
          args: [
            scriptPath,
            '--queue',
            '--decision-id',
            normalizedDecisionId,
            '--reply-path',
            normalizedReplyPath,
            '--state-root',
            stateRoot,
          ],
          options: {
            detached: true,
            windowsHide: true,
            stdio: 'ignore',
          },
        });
      } catch {
        scheduled.delete(key);
      }
    });
    return true;
  };
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
  const parameters = {
    BlockingFingerprint: request.blockingFingerprint,
    Category: request.category,
    DecisionId: request.decisionId,
    DetailCode: request.detailCode,
    ReplyPath: request.replyPath,
    RunId: request.runId,
    TaskId: request.taskId,
  };
  for (const [name, value] of Object.entries(parameters)) {
    if (value !== undefined) {
      args.push(`-${name}`, String(value));
    }
  }
  return args;
}

async function waitForProcess(child) {
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
  });
}

async function invokeLeaseDefault(request, options = {}) {
  const stateRoot = requireAbsolutePath(
    options.stateRoot ?? defaultRuntimeRoot(options.homedir),
    'stateRoot',
  );
  const spawnChild = options.spawnChild ?? nodeSpawn;
  const child = spawnChild('pwsh', leaseArguments(request, stateRoot), {
    windowsHide: true,
    stdio: ['ignore', 'pipe', 'pipe'],
  });
  const completed = await waitForProcess(child);
  const lines = completed.stdout
    .split(/\r?\n/u)
    .filter((line) => line.trim().length > 0);
  if (lines.length !== 1) {
    throw new Error('Invalid lease response');
  }
  let parsed;
  try {
    parsed = JSON.parse(lines[0]);
  } catch {
    throw new Error('Invalid lease response');
  }
  if (!isPlainObject(parsed) || typeof parsed.status !== 'string') {
    throw new Error('Invalid lease response');
  }
  if (completed.code !== 0 && !['RUN_ID_MISMATCH', 'RECOVERY_NOT_FOUND'].includes(parsed.status)) {
    throw new Error('Lease action failed');
  }
  return parsed;
}

async function consumeSpecificReply({ decisionId, decisionRequestPath, replyPath }) {
  const normalizedDecisionId = requireIdentifier(decisionId, 'decisionId');
  const normalizedRequestPath = requireAbsolutePath(decisionRequestPath, 'decisionRequestPath');
  const normalizedReplyPath = requireAbsolutePath(replyPath, 'replyPath');
  const replyName = basename(normalizedReplyPath);
  if (!HEX_FILE_PATTERN.test(replyName)) {
    throw new Error('Invalid reply path');
  }
  let output = '';
  const code = await consumeReplyMain(
    ['--request-file', normalizedRequestPath],
    {
      stdout: {
        write(chunk) {
          output += String(chunk);
        },
      },
    },
  );
  const lines = output.split(/\r?\n/u).filter((line) => line.trim().length > 0);
  if (code !== 0 || lines.length !== 1) {
    throw new Error('Reply consumption failed');
  }
  let consumed;
  try {
    consumed = JSON.parse(lines[0]);
  } catch {
    throw new Error('Reply consumption failed');
  }
  if (
    !isPlainObject(consumed)
    || consumed.providerEventIdHash !== replyName.slice(0, -'.json'.length)
  ) {
    throw new Error('Reply did not match requested inbox path');
  }
  if (consumed.result === 'OPTION_ACCEPTED' && ['A', 'B', 'C'].includes(consumed.optionKey)) {
    return { kind: 'option', optionKey: consumed.optionKey };
  }
  if (
    consumed.result === 'CUSTOM_ACCEPTED'
    && consumed.decisionId === normalizedDecisionId
    && typeof consumed.customText === 'string'
  ) {
    return { kind: 'custom', customText: consumed.customText };
  }
  throw new Error('Reply consumption failed');
}

function validateDispatch(value) {
  if (!isPlainObject(value) || value.status !== 'DISPATCH') {
    throw new Error('Invalid dispatch');
  }
  const resumeKind = value.resumeKind;
  if (!['codex', 'claude'].includes(resumeKind)) {
    throw new Error('Invalid dispatch');
  }
  return {
    runId: requireIdentifier(value.runId, 'runId'),
    taskId: requireIdentifier(value.taskId, 'taskId'),
    owner: requireOwner(value.owner),
    repositoryRoot: requireAbsolutePath(value.repositoryRoot, 'repositoryRoot'),
    resumeKind,
    resumeId: requireIdentifier(value.resumeId, 'resumeId'),
    decisionId: requireIdentifier(value.decisionId, 'decisionId'),
    decisionRequestPath: requireAbsolutePath(value.decisionRequestPath, 'decisionRequestPath'),
    replyPath: requireAbsolutePath(value.replyPath, 'replyPath'),
  };
}

async function startDetachedModel({ dispatch, reply, spawnChild }) {
  const replyValue = reply.kind === 'option' ? reply.optionKey : reply.customText;
  if (typeof replyValue !== 'string' || replyValue.length === 0) {
    throw new Error('Invalid reply');
  }
  const input = `[TZG_DECISION_RESUME runId=${dispatch.runId}]\n${replyValue}`;
  const command = dispatch.resumeKind === 'codex' ? 'pwsh' : 'claude';
  const args = dispatch.resumeKind === 'codex'
    ? [
      '-NoProfile',
      '-ExecutionPolicy',
      'Bypass',
      '-File',
      DEFAULT_CODEX_SESSION_RUNNER_PATH,
      '-Action',
      'Resume',
      '-RepositoryRoot',
      dispatch.repositoryRoot,
      '-TaskId',
      dispatch.taskId,
      '-RunId',
      dispatch.runId,
      '-SessionId',
      dispatch.resumeId,
    ]
    : ['--resume', dispatch.resumeId, '--print'];
  const child = spawnChild(command, args, {
    cwd: dispatch.repositoryRoot,
    detached: true,
    windowsHide: true,
    stdio: ['pipe', 'ignore', 'ignore'],
  });
  await new Promise((resolvePromise, rejectPromise) => {
    let settled = false;
    child.once('error', () => {
      if (!settled) {
        settled = true;
        rejectPromise(new Error('Model start failed'));
      }
    });
    child.once('spawn', () => {
      if (settled) {
        return;
      }
      try {
        child.stdin.end(input);
        child.unref();
        settled = true;
        resolvePromise();
      } catch {
        settled = true;
        rejectPromise(new Error('Model start failed'));
      }
    });
  });
}

async function bestEffortStartFailure({ dispatch, invokeLease }) {
  try {
    await invokeLease({
      action: 'RecordResult',
      runId: dispatch.runId,
      category: 'failed',
      taskId: dispatch.taskId,
      detailCode: 'resume_child_start_failed',
    });
  } catch {
    // The release attempt below remains mandatory.
  }
  try {
    await invokeLease({
      action: 'QueueResume',
      decisionId: dispatch.decisionId,
      replyPath: dispatch.replyPath,
    });
  } catch {
    // Preserve the original recovery pointer even if queueing fails.
  }
  try {
    await invokeLease({ action: 'Release', runId: dispatch.runId });
  } catch {
    // An expired lease remains recoverable by the next hourly run.
  }
}

export async function runResumeRelay(options) {
  const mode = options?.mode;
  if (!['queue', 'dispatch-ready'].includes(mode)) {
    throw new Error('Invalid relay mode');
  }
  const stateRoot = requireAbsolutePath(
    options.stateRoot ?? defaultRuntimeRoot(options.homedir),
    'stateRoot',
  );
  const invokeLease = options.invokeLease
    ?? ((request) => invokeLeaseDefault(request, {
      stateRoot,
      homedir: options.homedir,
      spawnChild: options.processSpawn,
    }));
  const consumeReply = options.consumeReply ?? consumeSpecificReply;
  const spawnChild = options.spawnChild ?? nodeSpawn;
  if (
    typeof invokeLease !== 'function'
    || typeof consumeReply !== 'function'
    || typeof spawnChild !== 'function'
  ) {
    throw new Error('Invalid relay dependencies');
  }

  let leaseResponse;
  if (mode === 'queue') {
    leaseResponse = await invokeLease({
      action: 'QueueResume',
      decisionId: requireIdentifier(options.decisionId, 'decisionId'),
      replyPath: requireAbsolutePath(options.replyPath, 'replyPath'),
    });
  } else {
    leaseResponse = await invokeLease({ action: 'TakeResume' });
  }
  if (['QUEUED', 'EMPTY', 'BUSY'].includes(leaseResponse?.status)) {
    return { status: leaseResponse.status };
  }

  const dispatch = validateDispatch(leaseResponse);
  const reply = await consumeReply({
    decisionId: dispatch.decisionId,
    decisionRequestPath: dispatch.decisionRequestPath,
    replyPath: dispatch.replyPath,
  });
  try {
    await startDetachedModel({ dispatch, reply, spawnChild });
  } catch {
    await bestEffortStartFailure({ dispatch, invokeLease });
    return { status: 'START_FAILED' };
  }
  return { status: 'DISPATCHED' };
}

function parseCli(argv) {
  if (!Array.isArray(argv)) {
    throw new Error('Invalid arguments');
  }
  const values = new Map();
  let mode;
  for (let index = 0; index < argv.length; index += 1) {
    const argument = argv[index];
    if (argument === '--queue' || argument === '--dispatch-ready') {
      if (mode !== undefined) {
        throw new Error('Invalid arguments');
      }
      mode = argument === '--queue' ? 'queue' : 'dispatch-ready';
      continue;
    }
    if (!['--decision-id', '--reply-path', '--state-root'].includes(argument)) {
      throw new Error('Invalid arguments');
    }
    const value = argv[index + 1];
    if (typeof value !== 'string' || values.has(argument)) {
      throw new Error('Invalid arguments');
    }
    values.set(argument, value);
    index += 1;
  }
  if (mode === 'queue') {
    if (
      !values.has('--decision-id')
      || !values.has('--reply-path')
      || !values.has('--state-root')
      || values.size !== 3
    ) {
      throw new Error('Invalid arguments');
    }
  } else if (
    mode !== 'dispatch-ready'
    || !values.has('--state-root')
    || values.size !== 1
  ) {
    throw new Error('Invalid arguments');
  }
  return {
    mode,
    decisionId: values.get('--decision-id'),
    replyPath: values.get('--reply-path'),
    stateRoot: values.get('--state-root'),
  };
}

export async function main(argv = process.argv.slice(2)) {
  try {
    const result = await runResumeRelay(parseCli(argv));
    return result.status === 'START_FAILED' ? 31 : 0;
  } catch {
    process.stderr.write('resume-trigger: FAILED\n');
    return 22;
  }
}

const directExecution = process.argv[1] !== undefined
  && import.meta.url === pathToFileURL(resolve(process.argv[1])).href;

if (directExecution) {
  main().then((code) => {
    process.exitCode = code;
  }).catch(() => {
    process.stderr.write('resume-trigger: FAILED\n');
    process.exitCode = 22;
  });
}
