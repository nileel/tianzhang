import { open } from 'node:fs/promises';
import { homedir as systemHomedir } from 'node:os';
import { isAbsolute, join, resolve } from 'node:path';
import { pathToFileURL } from 'node:url';

import { buildNotificationCard } from './notification-card.mjs';
import { recordNotificationOutcome } from './notification-audit.mjs';
import { parsePrivateConfig } from './config.mjs';
import { sendNotification } from './send-notification-core.mjs';
import { createSendIntentStore } from './send-intent-store.mjs';
import { createLarkTransport, readHealthSnapshot } from './send-runtime.mjs';

const MAX_JSON_BYTES = 64 * 1024;
const INVALID_RESULT = Object.freeze({ result: 'INVALID_INPUT' });
const RESULT_CODES = new Map([
  ['PROVIDER_ACCEPTED', 0],
  ['CHANNEL_UNAVAILABLE', 20],
  ['DELIVERY_FAILED', 21],
  ['INVALID_INPUT', 22],
  ['PROVIDER_OUTCOME_UNKNOWN', 23],
]);

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
    return JSON.parse(buffer.subarray(0, bytesRead).toString('utf8').replace(/^\ufeff/u, ''));
  } finally {
    await handle?.close().catch(() => {});
  }
}

async function readBoundedStdin(stdin) {
  const chunks = [];
  let bytes = 0;
  for await (const chunk of stdin) {
    const buffer = Buffer.isBuffer(chunk) ? chunk : Buffer.from(chunk);
    bytes += buffer.length;
    if (bytes > MAX_JSON_BYTES) {
      throw new Error('Invalid input');
    }
    chunks.push(buffer);
  }
  return JSON.parse(Buffer.concat(chunks).toString('utf8').replace(/^\ufeff/u, ''));
}

function validateRequest(value) {
  if (!isPlainObject(value) || Reflect.ownKeys(value).length !== 2) {
    throw new Error('Invalid input');
  }
  const descriptors = Object.getOwnPropertyDescriptors(value);
  const notification = descriptors.notification;
  const idempotencyKey = descriptors.idempotencyKey;
  if (
    !notification
    || !Object.hasOwn(notification, 'value')
    || !notification.enumerable
    || !idempotencyKey
    || !Object.hasOwn(idempotencyKey, 'value')
    || !idempotencyKey.enumerable
    || typeof idempotencyKey.value !== 'string'
    || idempotencyKey.value.length === 0
  ) {
    throw new Error('Invalid input');
  }
  buildNotificationCard(notification.value);
  return {
    notification: notification.value,
    idempotencyKey: idempotencyKey.value,
  };
}

function configPath(env, getHomedir) {
  const configured = env.FEISHU_DECISION_CONFIG_PATH;
  const path = configured === undefined
    ? join(getHomedir(), '.codex', 'automation-state', 'tzg-hourly-controller.feishu.private.json')
    : configured;
  if (typeof path !== 'string' || !isAbsolute(path)) {
    throw new Error('Invalid input');
  }
  return path;
}

function healthy(health, now) {
  if (!isPlainObject(health)) {
    return false;
  }
  const age = now.getTime() - Date.parse(health.updatedAt);
  return health.status === 'CONNECTED'
    && Number.isInteger(health.pid)
    && health.pid > 0
    && health.pidAlive === true
    && Number.isFinite(age)
    && age >= 0
    && age <= 120_000;
}

function writeResult(stdout, result) {
  stdout.write(`${JSON.stringify({ result: result.result })}\n`);
}

export async function main(argv = process.argv.slice(2), dependencies = {}) {
  const stdin = dependencies.stdin ?? process.stdin;
  const stdout = dependencies.stdout ?? process.stdout;
  const env = dependencies.env ?? process.env;
  const getHomedir = dependencies.homedir ?? systemHomedir;
  const getNow = dependencies.now ?? (() => new Date());
  const readHealth = dependencies.readHealth ?? readHealthSnapshot;
  const createTransport = dependencies.createTransport ?? createLarkTransport;
  const createIntentStore = dependencies.createIntentStore ?? createSendIntentStore;
  const send = dependencies.send ?? sendNotification;
  const recordAudit = dependencies.recordAudit ?? recordNotificationOutcome;

  let request;
  let config;
  let now;
  try {
    if (!Array.isArray(argv) || argv.length !== 0) {
      throw new Error('Invalid input');
    }
    request = validateRequest(await readBoundedStdin(stdin));
    config = parsePrivateConfig(await readBoundedJson(configPath(env, getHomedir)));
    now = getNow();
    if (!(now instanceof Date) || !Number.isFinite(now.getTime())) {
      throw new Error('Invalid input');
    }
  } catch {
    writeResult(stdout, INVALID_RESULT);
    return RESULT_CODES.get(INVALID_RESULT.result);
  }

  let health;
  try {
    health = await readHealth(config.stateRoot, now);
  } catch {
    health = { status: 'UNAVAILABLE', updatedAt: null, pid: null, pidAlive: false };
  }

  let result;
  if (!healthy(health, now)) {
    result = { result: 'CHANNEL_UNAVAILABLE' };
  } else {
    try {
      const transport = await createTransport(config);
      const intentStore = createIntentStore(config.stateRoot, { retryUnknown: false });
      result = await send({
        config,
        notification: request.notification,
        idempotencyKey: request.idempotencyKey,
        transport,
        intentStore,
        health,
        now,
      });
      if (!RESULT_CODES.has(result?.result)) {
        result = INVALID_RESULT;
      }
    } catch {
      result = { result: 'CHANNEL_UNAVAILABLE' };
    }
  }

  try {
    await recordAudit({
      stateRoot: config.stateRoot,
      idempotencyKey: request.idempotencyKey,
      kind: request.notification.kind,
      result: result.result,
      now,
    });
  } catch {
    // Audit is best-effort and cannot change delivery or business state.
  }
  writeResult(stdout, result);
  return RESULT_CODES.get(result.result) ?? RESULT_CODES.get('INVALID_INPUT');
}

const isDirectExecution = process.argv[1] !== undefined
  && import.meta.url === pathToFileURL(resolve(process.argv[1])).href;

if (isDirectExecution) {
  main().then((code) => {
    process.exitCode = code;
  }).catch(() => {
    process.stdout.write(`${JSON.stringify(INVALID_RESULT)}\n`);
    process.exitCode = RESULT_CODES.get('INVALID_INPUT');
  });
}
