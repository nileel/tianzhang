import { createHash, randomBytes } from 'node:crypto';
import { open, mkdir, rename, unlink } from 'node:fs/promises';
import { dirname, isAbsolute, join } from 'node:path';

const DOMAIN = 'send-intent-key-v1';
const DIRECTORY = 'send-intents';
const MAX_RECORD_BYTES = 16 * 1024;
const MAX_LOCK_BYTES = 1024;
const LOCK_LEASE_MS = 120_000;
const SAFE_RETRY_MS = 55 * 60 * 1000;
const HEX_PATTERN = /^[0-9a-f]{64}$/;
const UUID_PATTERN = /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/;
const PROVIDER_PATTERN = /^[a-z][a-z0-9_-]{0,31}$/;
const STATUSES = new Set(['PREPARED', 'IN_FLIGHT', 'ACCEPTED', 'OUTCOME_UNKNOWN', 'REJECTED']);
const BASE_KEYS = [
  'schemaVersion',
  'provider',
  'intentKeyHash',
  'attemptNumber',
  'uuid',
  'targetHash',
  'requestContentHash',
  'cardNonceHash',
  'firstAttemptAt',
  'lastUpdatedAt',
  'status',
];

function isPlainObject(value) {
  if (value === null || typeof value !== 'object' || Array.isArray(value)) {
    return false;
  }
  const prototype = Object.getPrototypeOf(value);
  return prototype === Object.prototype || prototype === null;
}

function snapshotExactDataObject(value, keys) {
  if (!isPlainObject(value) || Reflect.ownKeys(value).length !== keys.length) {
    return null;
  }
  const expected = new Set(keys);
  const descriptors = Object.getOwnPropertyDescriptors(value);
  const snapshot = Object.create(null);
  for (const key of Reflect.ownKeys(descriptors)) {
    if (typeof key !== 'string' || !expected.has(key)) {
      return null;
    }
    const descriptor = descriptors[key];
    if (!Object.hasOwn(descriptor, 'value') || !descriptor.enumerable) {
      return null;
    }
    snapshot[key] = descriptor.value;
  }
  return snapshot;
}

function validIsoTime(value) {
  if (typeof value !== 'string') {
    return false;
  }
  const milliseconds = Date.parse(value);
  return Number.isFinite(milliseconds) && new Date(milliseconds).toISOString() === value;
}

function recordKeysFor(status) {
  if (status === 'ACCEPTED') {
    return [...BASE_KEYS, 'providerMessageIdHash', 'providerChatIdHash', 'resultAt'];
  }
  if (status === 'OUTCOME_UNKNOWN' || status === 'REJECTED') {
    return [...BASE_KEYS, 'resultAt'];
  }
  return BASE_KEYS;
}

function parseRecord(raw) {
  let parsed;
  try {
    parsed = JSON.parse(raw.replace(/^\ufeff/, ''));
  } catch {
    return null;
  }
  if (!isPlainObject(parsed)) {
    return null;
  }
  const statusDescriptor = Object.getOwnPropertyDescriptor(parsed, 'status');
  if (
    !statusDescriptor
    || !Object.hasOwn(statusDescriptor, 'value')
    || !STATUSES.has(statusDescriptor.value)
  ) {
    return null;
  }
  const fields = snapshotExactDataObject(parsed, recordKeysFor(statusDescriptor.value));
  if (
    fields === null
    || fields.schemaVersion !== 1
    || typeof fields.provider !== 'string'
    || !PROVIDER_PATTERN.test(fields.provider)
    || !HEX_PATTERN.test(fields.intentKeyHash)
    || !Number.isSafeInteger(fields.attemptNumber)
    || fields.attemptNumber <= 0
    || typeof fields.uuid !== 'string'
    || !UUID_PATTERN.test(fields.uuid)
    || !HEX_PATTERN.test(fields.targetHash)
    || !HEX_PATTERN.test(fields.requestContentHash)
    || !HEX_PATTERN.test(fields.cardNonceHash)
    || !validIsoTime(fields.lastUpdatedAt)
  ) {
    return null;
  }
  if (fields.status === 'PREPARED') {
    if (fields.firstAttemptAt !== null) {
      return null;
    }
  } else if (!validIsoTime(fields.firstAttemptAt)) {
    return null;
  }
  if (
    fields.status === 'ACCEPTED'
    && (!HEX_PATTERN.test(fields.providerMessageIdHash) || !HEX_PATTERN.test(fields.providerChatIdHash))
  ) {
    return null;
  }
  if (
    ['ACCEPTED', 'OUTCOME_UNKNOWN', 'REJECTED'].includes(fields.status)
    && !validIsoTime(fields.resultAt)
  ) {
    return null;
  }
  return fields;
}

async function readBounded(path, maximum) {
  let handle;
  try {
    handle = await open(path, 'r');
    const buffer = Buffer.alloc(maximum + 1);
    const { bytesRead } = await handle.read(buffer, 0, buffer.length, 0);
    if (bytesRead > maximum) {
      return { kind: 'CORRUPT' };
    }
    return { kind: 'FOUND', raw: buffer.subarray(0, bytesRead).toString('utf8') };
  } catch (error) {
    if (error?.code === 'ENOENT') {
      return { kind: 'MISSING' };
    }
    return { kind: 'CORRUPT' };
  } finally {
    await handle?.close().catch(() => {});
  }
}

async function defaultAtomicWrite(path, record) {
  const tempPath = join(dirname(path), `.${randomBytes(16).toString('hex')}.tmp`);
  let handle;
  try {
    handle = await open(tempPath, 'wx', 0o600);
    await handle.writeFile(`${JSON.stringify(record)}\n`, 'utf8');
    await handle.sync();
    await handle.close();
    handle = undefined;
    await rename(tempPath, path);
  } catch (error) {
    await handle?.close().catch(() => {});
    await unlink(tempPath).catch(() => {});
    throw error;
  }
}

function defaultPidProbe(pid) {
  try {
    process.kill(pid, 0);
    return true;
  } catch {
    return false;
  }
}

function parseLock(raw) {
  let parsed;
  try {
    parsed = JSON.parse(raw.replace(/^\ufeff/, ''));
  } catch {
    return null;
  }
  const fields = snapshotExactDataObject(parsed, ['pid', 'time']);
  if (
    fields === null
    || !Number.isInteger(fields.pid)
    || fields.pid <= 0
    || !validIsoTime(fields.time)
  ) {
    return null;
  }
  return fields;
}

async function createLock(lockPath, now) {
  let handle;
  let created = false;
  try {
    handle = await open(lockPath, 'wx', 0o600);
    created = true;
    await handle.writeFile(JSON.stringify({ pid: process.pid, time: now.toISOString() }), 'utf8');
    await handle.sync();
    await handle.close();
    return true;
  } catch (error) {
    await handle?.close().catch(() => {});
    if (created) {
      await unlink(lockPath).catch(() => {});
    }
    return false;
  }
}

async function acquireLock(lockPath, now, pidProbe) {
  if (await createLock(lockPath, now)) {
    return true;
  }
  const existing = await readBounded(lockPath, MAX_LOCK_BYTES);
  if (existing.kind !== 'FOUND') {
    return false;
  }
  const lock = parseLock(existing.raw);
  if (lock === null) {
    return false;
  }
  let alive = true;
  try {
    alive = (await pidProbe(lock.pid)) === true;
  } catch {
    alive = true;
  }
  const age = now.getTime() - Date.parse(lock.time);
  if (alive || !Number.isFinite(age) || age <= LOCK_LEASE_MS) {
    return false;
  }
  try {
    await unlink(lockPath);
  } catch {
    return false;
  }
  return createLock(lockPath, now);
}

function validateIntent(intent) {
  const fields = snapshotExactDataObject(intent, [
    'provider',
    'decisionId',
    'intentKeyHash',
    'attemptNumber',
    'uuid',
    'targetHash',
    'requestContentHash',
    'cardNonceHash',
    'now',
  ]);
  if (
    fields === null
    || typeof fields.provider !== 'string'
    || !PROVIDER_PATTERN.test(fields.provider)
    || typeof fields.decisionId !== 'string'
    || fields.decisionId.length === 0
    || !Number.isSafeInteger(fields.attemptNumber)
    || fields.attemptNumber <= 0
    || fields.intentKeyHash !== hashSendIntentKey(
      fields.provider,
      fields.decisionId,
      fields.attemptNumber,
    )
    || typeof fields.uuid !== 'string'
    || !UUID_PATTERN.test(fields.uuid)
    || !HEX_PATTERN.test(fields.targetHash)
    || !HEX_PATTERN.test(fields.requestContentHash)
    || !HEX_PATTERN.test(fields.cardNonceHash)
    || !(fields.now instanceof Date)
    || !Number.isFinite(fields.now.getTime())
  ) {
    return null;
  }
  return fields;
}

function sameIntent(record, intent) {
  return record.provider === intent.provider
    && record.intentKeyHash === intent.intentKeyHash
    && record.attemptNumber === intent.attemptNumber
    && record.uuid === intent.uuid
    && record.targetHash === intent.targetHash
    && record.requestContentHash === intent.requestContentHash
    && record.cardNonceHash === intent.cardNonceHash;
}

function unknownOutcome(intent) {
  return {
    status: 'OUTCOME_UNKNOWN',
    targetHash: intent.targetHash,
    cardNonceHash: intent.cardNonceHash,
    intentKeyHash: intent.intentKeyHash,
  };
}

function publicStoredOutcome(record) {
  const base = {
    status: record.status,
    targetHash: record.targetHash,
    cardNonceHash: record.cardNonceHash,
    intentKeyHash: record.intentKeyHash,
  };
  if (record.status === 'ACCEPTED') {
    base.providerMessageIdHash = record.providerMessageIdHash;
    base.providerChatIdHash = record.providerChatIdHash;
  }
  return base;
}

function makeRecord(intent, status, firstAttemptAt, now, extra = {}) {
  const record = {
    schemaVersion: 1,
    provider: intent.provider,
    intentKeyHash: intent.intentKeyHash,
    attemptNumber: intent.attemptNumber,
    uuid: intent.uuid,
    targetHash: intent.targetHash,
    requestContentHash: intent.requestContentHash,
    cardNonceHash: intent.cardNonceHash,
    firstAttemptAt,
    lastUpdatedAt: now.toISOString(),
    status,
  };
  if (['ACCEPTED', 'OUTCOME_UNKNOWN', 'REJECTED'].includes(status)) {
    record.resultAt = now.toISOString();
  }
  if (status === 'ACCEPTED') {
    record.providerMessageIdHash = extra.providerMessageIdHash;
    record.providerChatIdHash = extra.providerChatIdHash;
  }
  return record;
}

export function hashSendIntentKey(provider, decisionId, attemptNumber) {
  if (
    typeof provider !== 'string'
    || !PROVIDER_PATTERN.test(provider)
    || typeof decisionId !== 'string'
    || decisionId.length === 0
    || !Number.isSafeInteger(attemptNumber)
    || attemptNumber <= 0
  ) {
    throw new Error('Invalid send intent key');
  }
  return createHash('sha256')
    .update(`${DOMAIN}\u0000${provider}\u0000${decisionId}\u0000${attemptNumber}`, 'utf8')
    .digest('hex');
}

export function createSendIntentStore(stateRoot, options = {}) {
  if (typeof stateRoot !== 'string' || !isAbsolute(stateRoot)) {
    throw new Error('Invalid send intent store');
  }
  const directory = join(stateRoot, DIRECTORY);
  const pidProbe = typeof options?.pidProbe === 'function' ? options.pidProbe : defaultPidProbe;
  const atomicWrite = typeof options?.atomicWrite === 'function'
    ? options.atomicWrite
    : async (path, record) => defaultAtomicWrite(path, record);

  async function writeRecord(path, record) {
    await atomicWrite(path, record, defaultAtomicWrite);
  }

  return {
    async run(rawIntent, operation) {
      const intent = validateIntent(rawIntent);
      if (intent === null || typeof operation !== 'function') {
        throw new Error('Invalid send intent');
      }
      const path = join(directory, `${intent.intentKeyHash}.json`);
      const lockPath = join(directory, `${intent.intentKeyHash}.lock`);
      try {
        await mkdir(directory, { recursive: true });
      } catch {
        return unknownOutcome(intent);
      }
      if (!(await acquireLock(lockPath, intent.now, pidProbe))) {
        return unknownOutcome(intent);
      }

      try {
        const loaded = await readBounded(path, MAX_RECORD_BYTES);
        let record = null;
        if (loaded.kind === 'FOUND') {
          record = parseRecord(loaded.raw);
          if (record === null || !sameIntent(record, intent)) {
            return unknownOutcome(intent);
          }
        } else if (loaded.kind === 'CORRUPT') {
          return unknownOutcome(intent);
        }

        if (record?.status === 'ACCEPTED' || record?.status === 'REJECTED') {
          return publicStoredOutcome(record);
        }

        if (record?.status === 'IN_FLIGHT' || record?.status === 'OUTCOME_UNKNOWN') {
          const age = intent.now.getTime() - Date.parse(record.firstAttemptAt);
          if (!Number.isFinite(age) || age < 0 || age >= SAFE_RETRY_MS) {
            if (record.status === 'IN_FLIGHT') {
              const lockedUnknown = makeRecord(
                intent,
                'OUTCOME_UNKNOWN',
                record.firstAttemptAt,
                intent.now,
              );
              await writeRecord(path, lockedUnknown).catch(() => {});
            }
            return unknownOutcome(intent);
          }
        }

        if (record === null) {
          const prepared = makeRecord(intent, 'PREPARED', null, intent.now);
          try {
            await writeRecord(path, prepared);
          } catch {
            return unknownOutcome(intent);
          }
          record = prepared;
        }

        const firstAttemptAt = record.firstAttemptAt ?? intent.now.toISOString();
        const inFlight = makeRecord(intent, 'IN_FLIGHT', firstAttemptAt, intent.now);
        try {
          await writeRecord(path, inFlight);
        } catch {
          return unknownOutcome(intent);
        }

        let operationOutcome;
        try {
          operationOutcome = await operation();
        } catch {
          operationOutcome = { status: 'OUTCOME_UNKNOWN' };
        }
        let terminalStatus = 'OUTCOME_UNKNOWN';
        let providerMessageIdHash;
        let providerChatIdHash;
        if (isPlainObject(operationOutcome)) {
          const statusDescriptor = Object.getOwnPropertyDescriptor(operationOutcome, 'status');
          if (statusDescriptor && Object.hasOwn(statusDescriptor, 'value')) {
            if (statusDescriptor.value === 'REJECTED') {
              terminalStatus = 'REJECTED';
            } else if (statusDescriptor.value === 'ACCEPTED') {
              const hashDescriptor = Object.getOwnPropertyDescriptor(
                operationOutcome,
                'providerMessageIdHash',
              );
              const chatHashDescriptor = Object.getOwnPropertyDescriptor(
                operationOutcome,
                'providerChatIdHash',
              );
              if (
                hashDescriptor
                && Object.hasOwn(hashDescriptor, 'value')
                && HEX_PATTERN.test(hashDescriptor.value)
                && chatHashDescriptor
                && Object.hasOwn(chatHashDescriptor, 'value')
                && HEX_PATTERN.test(chatHashDescriptor.value)
              ) {
                terminalStatus = 'ACCEPTED';
                providerMessageIdHash = hashDescriptor.value;
                providerChatIdHash = chatHashDescriptor.value;
              }
            }
          }
        }
        const terminal = makeRecord(
          intent,
          terminalStatus,
          firstAttemptAt,
          intent.now,
          { providerMessageIdHash, providerChatIdHash },
        );
        try {
          await writeRecord(path, terminal);
        } catch {
          const unknown = makeRecord(intent, 'OUTCOME_UNKNOWN', firstAttemptAt, intent.now);
          await writeRecord(path, unknown).catch(() => {});
          return unknownOutcome(intent);
        }
        return publicStoredOutcome(terminal);
      } catch {
        return unknownOutcome(intent);
      } finally {
        await unlink(lockPath).catch(() => {});
      }
    },
  };
}
