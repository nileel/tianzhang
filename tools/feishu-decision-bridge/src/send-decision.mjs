import { open } from 'node:fs/promises';
import { homedir as systemHomedir } from 'node:os';
import { isAbsolute, join, resolve } from 'node:path';
import { pathToFileURL } from 'node:url';

import { buildDecisionCard } from './card.mjs';
import { parsePrivateConfig } from './config.mjs';
import { sendDecision } from './send-core.mjs';
import { createSendIntentStore } from './send-intent-store.mjs';
import { createLarkTransport, readHealthSnapshot } from './send-runtime.mjs';

const MAX_JSON_BYTES = 64 * 1024;
const INVALID_RESULT = Object.freeze({ result: 'INVALID_INPUT' });

function isPlainObject(value) {
  if (value === null || typeof value !== 'object' || Array.isArray(value)) {
    return false;
  }
  const prototype = Object.getPrototypeOf(value);
  return prototype === Object.prototype || prototype === null;
}

async function readBoundedJson(path) {
  let handle;
  try {
    handle = await open(path, 'r');
    const buffer = Buffer.alloc(MAX_JSON_BYTES + 1);
    const { bytesRead } = await handle.read(buffer, 0, buffer.length, 0);
    if (bytesRead > MAX_JSON_BYTES) {
      throw new Error('Invalid input');
    }
    return JSON.parse(buffer.subarray(0, bytesRead).toString('utf8').replace(/^\ufeff/, ''));
  } finally {
    await handle?.close().catch(() => {});
  }
}

function requestPathFromArgs(argv) {
  if (
    !Array.isArray(argv)
    || argv.length !== 2
    || argv[0] !== '--request-file'
    || typeof argv[1] !== 'string'
    || !isAbsolute(argv[1])
  ) {
    throw new Error('Invalid input');
  }
  return argv[1];
}

function validateRequest(request) {
  if (!isPlainObject(request)) {
    throw new Error('Invalid input');
  }
  const keys = Object.keys(request).sort();
  if (
    keys.length !== 2
    || keys[0] !== 'attemptNumber'
    || keys[1] !== 'decision'
    || Reflect.ownKeys(request).length !== 2
    || !Number.isSafeInteger(request.attemptNumber)
    || request.attemptNumber <= 0
  ) {
    throw new Error('Invalid input');
  }
  try {
    buildDecisionCard(request.decision, 'validation');
  } catch {
    throw new Error('Invalid input');
  }
  return request;
}

function healthIsUsable(health, now) {
  if (!isPlainObject(health)) {
    return false;
  }
  const updatedAtMs = Date.parse(health.updatedAt);
  const ageMs = now.getTime() - updatedAtMs;
  return health.status === 'CONNECTED'
    && Number.isInteger(health.pid)
    && health.pid > 0
    && health.pidAlive === true
    && Number.isFinite(ageMs)
    && ageMs >= 0
    && ageMs <= 120_000;
}

function exitCodeFor(result) {
  switch (result?.result) {
    case 'PROVIDER_ACCEPTED': return 0;
    case 'CHANNEL_UNAVAILABLE': return 20;
    case 'DELIVERY_FAILED': return 21;
    case 'PROVIDER_OUTCOME_UNKNOWN': return 23;
    default: return 22;
  }
}

function sanitizeResult(result) {
  if (!isPlainObject(result)) {
    return null;
  }
  const descriptors = Object.getOwnPropertyDescriptors(result);
  const value = (key) => {
    const descriptor = descriptors[key];
    return descriptor && Object.hasOwn(descriptor, 'value') ? descriptor.value : undefined;
  };
  const category = value('result');
  const hex = (key) => typeof value(key) === 'string' && /^[0-9a-f]{64}$/.test(value(key));
  if (category === 'CHANNEL_UNAVAILABLE') {
    return { result: category };
  }
  if (category === 'DELIVERY_FAILED' && hex('targetHash')) {
    return { result: category, targetHash: value('targetHash') };
  }
  if (
    category === 'PROVIDER_OUTCOME_UNKNOWN'
    && hex('targetHash')
    && hex('cardNonceHash')
    && hex('intentKeyHash')
  ) {
    return {
      result: category,
      targetHash: value('targetHash'),
      cardNonceHash: value('cardNonceHash'),
      intentKeyHash: value('intentKeyHash'),
    };
  }
  if (
    category === 'PROVIDER_ACCEPTED'
    && hex('targetHash')
    && hex('providerMessageIdHash')
    && hex('providerChatIdHash')
    && hex('cardNonceHash')
    && hex('intentKeyHash')
  ) {
    return {
      result: category,
      targetHash: value('targetHash'),
      providerMessageIdHash: value('providerMessageIdHash'),
      providerChatIdHash: value('providerChatIdHash'),
      cardNonceHash: value('cardNonceHash'),
      intentKeyHash: value('intentKeyHash'),
    };
  }
  return null;
}

function writeResult(stdout, result) {
  stdout.write(`${JSON.stringify(result)}\n`);
}

export async function main(argv = process.argv.slice(2), dependencies = {}) {
  const env = dependencies.env ?? process.env;
  const getHomedir = dependencies.homedir ?? systemHomedir;
  const stdout = dependencies.stdout ?? process.stdout;
  const getNow = dependencies.now ?? (() => new Date());
  const readHealth = dependencies.readHealth ?? readHealthSnapshot;
  const createTransport = dependencies.createTransport ?? createLarkTransport;
  const createIntentStore = dependencies.createIntentStore ?? createSendIntentStore;
  const send = dependencies.send ?? sendDecision;

  let request;
  let config;
  let now;
  try {
    const requestPath = requestPathFromArgs(argv);
    request = validateRequest(await readBoundedJson(requestPath));

    const configuredPath = env.FEISHU_DECISION_CONFIG_PATH;
    const configPath = configuredPath === undefined
      ? join(getHomedir(), '.codex', 'automation-state', 'tzg-hourly-controller.feishu.private.json')
      : configuredPath;
    if (typeof configPath !== 'string' || !isAbsolute(configPath)) {
      throw new Error('Invalid input');
    }
    config = parsePrivateConfig(await readBoundedJson(configPath));
    now = getNow();
    if (!(now instanceof Date) || !Number.isFinite(now.getTime())) {
      throw new Error('Invalid input');
    }
  } catch {
    writeResult(stdout, INVALID_RESULT);
    return 22;
  }

  let health;
  try {
    health = await readHealth(config.stateRoot, now);
  } catch {
    health = { status: 'UNAVAILABLE', updatedAt: null, pid: null, pidAlive: false };
  }

  if (!healthIsUsable(health, now)) {
    writeResult(stdout, { result: 'CHANNEL_UNAVAILABLE' });
    return 20;
  }

  let transport;
  try {
    transport = await createTransport(config);
  } catch {
    writeResult(stdout, { result: 'CHANNEL_UNAVAILABLE' });
    return 20;
  }

  let intentStore;
  try {
    intentStore = createIntentStore(config.stateRoot);
  } catch {
    writeResult(stdout, { result: 'CHANNEL_UNAVAILABLE' });
    return 20;
  }

  let result;
  try {
    result = await send({
      config,
      decision: request.decision,
      attemptNumber: request.attemptNumber,
      transport,
      intentStore,
      health,
      now,
    });
  } catch {
    writeResult(stdout, INVALID_RESULT);
    return 22;
  }

  const output = sanitizeResult(result);
  if (output === null) {
    writeResult(stdout, INVALID_RESULT);
    return 22;
  }
  const code = exitCodeFor(output);
  writeResult(stdout, output);
  return code;
}

const isDirectExecution = process.argv[1] !== undefined
  && import.meta.url === pathToFileURL(resolve(process.argv[1])).href;

if (isDirectExecution) {
  main().then((code) => {
    process.exitCode = code;
  }).catch(() => {
    process.stdout.write(`${JSON.stringify(INVALID_RESULT)}\n`);
    process.exitCode = 22;
  });
}
