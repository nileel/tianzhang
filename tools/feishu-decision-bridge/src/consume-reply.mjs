import { open } from 'node:fs/promises';
import { homedir as systemHomedir } from 'node:os';
import { isAbsolute, join, resolve } from 'node:path';
import { pathToFileURL } from 'node:url';

import { parsePrivateConfig } from './config.mjs';
import { consumeCurrentReply } from './inbox.mjs';

const MAX_JSON_BYTES = 64 * 1024;
const HEX_PATTERN = /^[0-9a-f]{64}$/;
const IDENTIFIER_PATTERN = /^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$/;
const PENDING_KEYS = [
  'decisionId', 'allowedOptions', 'createdAt', 'expiresAt', 'cardNonceHash',
  'providerMessageIdHash',
];
const ACCEPTED_KEYS = [
  'result', 'optionKey', 'source', 'providerMessageIdHash', 'providerEventIdHash',
  'operatorOpenIdHash', 'tenantKeyHash', 'cardNonceHash', 'evidenceHash',
];
const INVALID_RESULT = Object.freeze({ result: 'INVALID_INPUT' });
const NO_REPLY_RESULT = Object.freeze({ result: 'NO_REPLY' });

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

function validatePending(value) {
  const fields = exactDataObject(value, PENDING_KEYS);
  const options = exactDataArray(fields?.allowedOptions);
  const createdAt = parseExactIso(fields?.createdAt);
  const expiresAt = parseExactIso(fields?.expiresAt);
  if (
    fields === null
    || typeof fields.decisionId !== 'string'
    || !IDENTIFIER_PATTERN.test(fields.decisionId)
    || options === null
    || options.length !== 3
    || options.some((option, index) => option !== ['A', 'B', 'C'][index])
    || createdAt === null
    || expiresAt === null
    || createdAt > expiresAt
    || typeof fields.cardNonceHash !== 'string'
    || !HEX_PATTERN.test(fields.cardNonceHash)
    || typeof fields.providerMessageIdHash !== 'string'
    || !HEX_PATTERN.test(fields.providerMessageIdHash)
  ) {
    return null;
  }
  return {
    decisionId: fields.decisionId,
    allowedOptions: [...options],
    createdAt: fields.createdAt,
    expiresAt: fields.expiresAt,
    cardNonceHash: fields.cardNonceHash,
    providerMessageIdHash: fields.providerMessageIdHash,
  };
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

function snapshotRequest(value) {
  const fields = exactDataObject(value, ['pendingDecision']);
  const pendingDecision = validatePending(fields?.pendingDecision);
  if (fields === null || pendingDecision === null) {
    throw new Error();
  }
  return { pendingDecision };
}

function sanitizeAccepted(value) {
  const fields = exactDataObject(value, ACCEPTED_KEYS);
  if (
    fields === null
    || fields.result !== 'REPLY_ACCEPTED'
    || !['A', 'B', 'C'].includes(fields.optionKey)
    || fields.source !== 'feishu_card'
    || ACCEPTED_KEYS.slice(3).some((key) => (
      typeof fields[key] !== 'string' || !HEX_PATTERN.test(fields[key])
    ))
  ) {
    return null;
  }
  return {
    result: 'REPLY_ACCEPTED',
    optionKey: fields.optionKey,
    source: 'feishu_card',
    providerMessageIdHash: fields.providerMessageIdHash,
    providerEventIdHash: fields.providerEventIdHash,
    operatorOpenIdHash: fields.operatorOpenIdHash,
    tenantKeyHash: fields.tenantKeyHash,
    cardNonceHash: fields.cardNonceHash,
    evidenceHash: fields.evidenceHash,
  };
}

function writeResult(stdout, value) {
  stdout.write(`${JSON.stringify(value)}\n`);
}

export async function main(argv = process.argv.slice(2), dependencies = {}) {
  const env = dependencies.env ?? process.env;
  const homedir = dependencies.homedir ?? systemHomedir;
  const stdout = dependencies.stdout ?? process.stdout;
  const getNow = dependencies.now ?? (() => new Date());
  const consume = dependencies.consume ?? consumeCurrentReply;
  try {
    const requestPath = requestPathFromArgs(argv);
    const request = snapshotRequest(await readBoundedJson(requestPath));
    const configuredPath = env.FEISHU_DECISION_CONFIG_PATH;
    const configPath = configuredPath === undefined
      ? join(homedir(), '.codex', 'automation-state', 'tzg-hourly-controller.feishu.private.json')
      : configuredPath;
    if (typeof configPath !== 'string' || !isAbsolute(configPath)) {
      throw new Error();
    }
    const config = parsePrivateConfig(await readBoundedJson(configPath));
    const now = getNow();
    if (!(now instanceof Date) || !Number.isFinite(now.getTime())) {
      throw new Error();
    }
    const result = await consume({
      stateRoot: config.stateRoot,
      config,
      pendingDecision: request.pendingDecision,
      now,
    });
    if (result === null) {
      writeResult(stdout, NO_REPLY_RESULT);
      return 0;
    }
    const sanitized = sanitizeAccepted(result);
    if (sanitized === null) {
      throw new Error();
    }
    writeResult(stdout, sanitized);
    return 0;
  } catch {
    writeResult(stdout, INVALID_RESULT);
    return 22;
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
