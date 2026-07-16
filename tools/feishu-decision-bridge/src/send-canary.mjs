import { createHash } from 'node:crypto';
import { open } from 'node:fs/promises';
import { homedir as systemHomedir } from 'node:os';
import { isAbsolute, join, resolve } from 'node:path';
import { pathToFileURL } from 'node:url';

import { buildDecisionCard } from './card.mjs';
import { parsePrivateConfig, sha256 } from './config.mjs';
import {
  createLarkTransport,
  ProviderOutcomeUnknownError,
  ProviderRejectedError,
  readHealthSnapshot,
} from './send-runtime.mjs';

const MAX_JSON_BYTES = 32 * 1024;
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

async function readBoundedJson(path) {
  let handle;
  try {
    handle = await open(path, 'r');
    const buffer = Buffer.alloc(MAX_JSON_BYTES + 1);
    const { bytesRead } = await handle.read(buffer, 0, buffer.length, 0);
    if (bytesRead > MAX_JSON_BYTES) {
      throw new Error();
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
    throw new Error();
  }
  return argv[1];
}

function stableUuid(decisionId) {
  const bytes = createHash('sha256')
    .update(`feishu-canary-uuid-v1\u0000${decisionId}`, 'utf8')
    .digest()
    .subarray(0, 16);
  bytes[6] = (bytes[6] & 0x0f) | 0x40;
  bytes[8] = (bytes[8] & 0x3f) | 0x80;
  const hex = bytes.toString('hex');
  return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`;
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

function writeResult(stdout, result) {
  stdout.write(`${JSON.stringify(result)}\n`);
}

export async function main(argv = process.argv.slice(2), dependencies = {}) {
  const env = dependencies.env ?? process.env;
  const homedir = dependencies.homedir ?? systemHomedir;
  const stdout = dependencies.stdout ?? process.stdout;
  const getNow = dependencies.now ?? (() => new Date());
  const readHealth = dependencies.readHealth ?? readHealthSnapshot;
  const createTransport = dependencies.createTransport ?? createLarkTransport;
  let config;
  let decision;
  let cardNonce;
  let card;
  let now;
  try {
    const requestPath = requestPathFromArgs(argv);
    const request = exactDataObject(await readBoundedJson(requestPath), ['decision', 'cardNonce']);
    if (request === null) {
      throw new Error();
    }
    decision = request.decision;
    cardNonce = request.cardNonce;
    card = buildDecisionCard(decision, cardNonce);
    const configuredPath = env.FEISHU_DECISION_CONFIG_PATH;
    const configPath = configuredPath === undefined
      ? join(homedir(), '.codex', 'automation-state', 'tzg-hourly-controller.feishu.private.json')
      : configuredPath;
    if (typeof configPath !== 'string' || !isAbsolute(configPath)) {
      throw new Error();
    }
    config = parsePrivateConfig(await readBoundedJson(configPath));
    if (config.expectedTenantKey === null || config.pairedOperatorOpenIdHash === null) {
      throw new Error();
    }
    now = getNow();
    if (!(now instanceof Date) || !Number.isFinite(now.getTime())) {
      throw new Error();
    }
  } catch {
    writeResult(stdout, INVALID_RESULT);
    return 22;
  }

  let health;
  try {
    health = await readHealth(config.stateRoot, now);
  } catch {
    health = null;
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
  const targetHash = sha256(config.recipient.value);
  const request = {
    params: { receive_id_type: config.recipient.type },
    data: {
      receive_id: config.recipient.value,
      msg_type: 'interactive',
      content: JSON.stringify(card),
      uuid: stableUuid(decision.decisionId),
    },
  };
  try {
    const result = await transport.sendInteractive(request);
    if (
      !isPlainObject(result)
      || typeof result.messageId !== 'string'
      || typeof result.chatId !== 'string'
    ) {
      throw new ProviderOutcomeUnknownError();
    }
    writeResult(stdout, {
      result: 'PROVIDER_ACCEPTED',
      targetHash,
      providerMessageIdHash: sha256(result.messageId),
      providerChatIdHash: sha256(result.chatId),
      cardNonceHash: sha256(cardNonce),
    });
    return 0;
  } catch (error) {
    if (error instanceof ProviderRejectedError) {
      writeResult(stdout, { result: 'DELIVERY_FAILED', targetHash });
      return 21;
    }
    writeResult(stdout, {
      result: 'PROVIDER_OUTCOME_UNKNOWN',
      targetHash,
      cardNonceHash: sha256(cardNonce),
    });
    return 23;
  }
}

const directExecution = process.argv[1] !== undefined
  && import.meta.url === pathToFileURL(resolve(process.argv[1])).href;

if (directExecution) {
  main().then((code) => {
    process.exitCode = code;
  }).catch(() => {
    process.stdout.write(`${JSON.stringify(INVALID_RESULT)}\n`);
    process.exitCode = 22;
  });
}
