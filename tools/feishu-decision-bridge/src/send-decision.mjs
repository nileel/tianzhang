import { randomUUID } from 'node:crypto';
import { open, rename, rm, writeFile } from 'node:fs/promises';
import { homedir as systemHomedir } from 'node:os';
import {
  basename, dirname, isAbsolute, join, resolve,
} from 'node:path';
import { pathToFileURL } from 'node:url';

import { buildDecisionCard } from './card.mjs';
import { parsePrivateConfig } from './config.mjs';
import { sendDecision } from './send-core.mjs';
import { createSendIntentStore } from './send-intent-store.mjs';
import { createLarkTransport, readHealthSnapshot } from './send-runtime.mjs';

const MAX_JSON_BYTES = 64 * 1024;
const DECISION_TTL_MS = 7 * 24 * 60 * 60 * 1000;
const HEX_PATTERN = /^[0-9a-f]{64}$/;
const IDENTIFIER_PATTERN = /^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$/;
const BINDING_KEYS = [
  'kind', 'decisionId', 'allowedOptions', 'allowCustomReply', 'issuedAt', 'expiresAt',
  'cardNonceHash', 'providerMessageIdHash', 'providerChatIdHash',
];
const INVALID_RESULT = Object.freeze({ result: 'INVALID_INPUT' });

function isPlainObject(value) {
  if (value === null || typeof value !== 'object' || Array.isArray(value)) {
    return false;
  }
  const prototype = Object.getPrototypeOf(value);
  return prototype === Object.prototype || prototype === null;
}

function exactDataObject(value, keys) {
  if (!isPlainObject(value)) {
    return null;
  }
  const ownKeys = Reflect.ownKeys(value);
  if (
    ownKeys.length !== keys.length
    || ownKeys.some((key) => typeof key !== 'string' || !keys.includes(key))
  ) {
    return null;
  }
  const descriptors = Object.getOwnPropertyDescriptors(value);
  const result = Object.create(null);
  for (const key of keys) {
    const descriptor = descriptors[key];
    if (!descriptor || !Object.hasOwn(descriptor, 'value') || !descriptor.enumerable) {
      return null;
    }
    result[key] = descriptor.value;
  }
  return result;
}

function exactDataArray(value) {
  if (!Array.isArray(value) || Object.getPrototypeOf(value) !== Array.prototype) {
    return null;
  }
  const descriptors = Object.getOwnPropertyDescriptors(value);
  if (
    descriptors.length?.value !== value.length
    || Reflect.ownKeys(value).length !== value.length + 1
  ) {
    return null;
  }
  const result = [];
  for (let index = 0; index < value.length; index += 1) {
    const descriptor = descriptors[String(index)];
    if (!descriptor || !Object.hasOwn(descriptor, 'value') || !descriptor.enumerable) {
      return null;
    }
    result.push(descriptor.value);
  }
  return result;
}

function parseExactIso(value) {
  if (typeof value !== 'string') {
    return null;
  }
  const time = Date.parse(value);
  return Number.isFinite(time) && new Date(time).toISOString() === value ? time : null;
}

function snapshotDecisionBinding(value) {
  const fields = exactDataObject(value, BINDING_KEYS);
  const allowedOptions = exactDataArray(fields?.allowedOptions);
  const issuedAtMs = parseExactIso(fields?.issuedAt);
  const expiresAtMs = parseExactIso(fields?.expiresAt);
  if (
    fields === null
    || fields.kind !== 'decision_reply'
    || typeof fields.decisionId !== 'string'
    || !IDENTIFIER_PATTERN.test(fields.decisionId)
    || allowedOptions === null
    || allowedOptions.length !== 3
    || allowedOptions.some((option, index) => option !== ['A', 'B', 'C'][index])
    || typeof fields.allowCustomReply !== 'boolean'
    || issuedAtMs === null
    || expiresAtMs === null
    || typeof fields.cardNonceHash !== 'string'
    || !HEX_PATTERN.test(fields.cardNonceHash)
    || typeof fields.providerMessageIdHash !== 'string'
    || !HEX_PATTERN.test(fields.providerMessageIdHash)
    || typeof fields.providerChatIdHash !== 'string'
    || !HEX_PATTERN.test(fields.providerChatIdHash)
  ) {
    return null;
  }
  return {
    binding: {
      kind: 'decision_reply',
      decisionId: fields.decisionId,
      allowedOptions: [...allowedOptions],
      allowCustomReply: fields.allowCustomReply,
      issuedAt: fields.issuedAt,
      expiresAt: fields.expiresAt,
      cardNonceHash: fields.cardNonceHash,
      providerMessageIdHash: fields.providerMessageIdHash,
      providerChatIdHash: fields.providerChatIdHash,
    },
    expiresAtMs,
  };
}

async function readActiveBindings(path, now) {
  let raw;
  try {
    raw = await readBoundedJson(path);
  } catch (error) {
    if (error?.code === 'ENOENT') {
      return [];
    }
    throw error;
  }
  const values = exactDataArray(raw);
  if (values === null) {
    throw new Error('Invalid pending bindings');
  }
  const seen = new Set();
  const bindings = [];
  for (const value of values) {
    const snapshot = snapshotDecisionBinding(value);
    if (snapshot === null || seen.has(snapshot.binding.decisionId)) {
      throw new Error('Invalid pending bindings');
    }
    seen.add(snapshot.binding.decisionId);
    if (snapshot.expiresAtMs >= now.getTime()) {
      bindings.push(snapshot.binding);
    }
  }
  return bindings;
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

async function writePendingBinding({ stateRoot, decision, result, now }) {
  const path = join(stateRoot, 'pending-bindings.json');
  const temporaryPath = join(stateRoot, `.pending-bindings.${randomUUID()}.tmp`);
  const binding = {
    kind: 'decision_reply',
    decisionId: decision.decisionId,
    allowedOptions: decision.options.map((option) => option.key),
    allowCustomReply: decision.allowCustomReply !== false,
    issuedAt: now.toISOString(),
    expiresAt: new Date(now.getTime() + DECISION_TTL_MS).toISOString(),
    cardNonceHash: result.cardNonceHash,
    providerMessageIdHash: result.providerMessageIdHash,
    providerChatIdHash: result.providerChatIdHash,
  };
  const existing = await readActiveBindings(path, now);
  const bindings = [
    ...existing.filter((candidate) => candidate.decisionId !== binding.decisionId),
    binding,
  ];
  try {
    await writeFile(temporaryPath, `${JSON.stringify(bindings)}\n`, {
      encoding: 'utf8',
      flag: 'wx',
      mode: 0o600,
    });
    await rename(temporaryPath, path);
  } finally {
    await rm(temporaryPath, { force: true }).catch(() => {});
  }
}

async function writeRecoveryRequest({ requestPath, decision, result, now }) {
  const temporaryPath = join(
    dirname(requestPath),
    `.${basename(requestPath)}.${randomUUID()}.tmp`,
  );
  const pendingDecision = {
    decisionId: decision.decisionId,
    allowedOptions: decision.options.map((option) => option.key),
    allowCustomReply: decision.allowCustomReply !== false,
    createdAt: now.toISOString(),
    expiresAt: new Date(now.getTime() + DECISION_TTL_MS).toISOString(),
    cardNonceHash: result.cardNonceHash,
    providerMessageIdHash: result.providerMessageIdHash,
    providerChatIdHash: result.providerChatIdHash,
  };
  try {
    await writeFile(temporaryPath, `${JSON.stringify({ pendingDecision })}\n`, {
      encoding: 'utf8',
      flag: 'wx',
      mode: 0o600,
    });
    await rename(temporaryPath, requestPath);
  } finally {
    await rm(temporaryPath, { force: true }).catch(() => {});
  }
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
  const writeBinding = dependencies.writeBinding ?? writePendingBinding;
  const writeRequest = dependencies.writeRecoveryRequest ?? writeRecoveryRequest;

  let request;
  let requestPath;
  let config;
  let now;
  try {
    requestPath = requestPathFromArgs(argv);
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

  let output = sanitizeResult(result);
  if (output === null) {
    writeResult(stdout, INVALID_RESULT);
    return 22;
  }
  if (output.result === 'PROVIDER_ACCEPTED') {
    const acceptedAt = typeof result.acceptedAt === 'string'
      ? new Date(result.acceptedAt)
      : new Date(Number.NaN);
    if (!Number.isFinite(acceptedAt.getTime()) || acceptedAt.toISOString() !== result.acceptedAt) {
      writeResult(stdout, INVALID_RESULT);
      return 22;
    }
    try {
      await writeBinding({
        stateRoot: config.stateRoot,
        decision: request.decision,
        result: output,
        now: acceptedAt,
      });
      await writeRequest({
        requestPath,
        decision: request.decision,
        result: output,
        now: acceptedAt,
      });
    } catch {
      output = {
        result: 'PROVIDER_OUTCOME_UNKNOWN',
        targetHash: output.targetHash,
        cardNonceHash: output.cardNonceHash,
        intentKeyHash: output.intentKeyHash,
      };
    }
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
