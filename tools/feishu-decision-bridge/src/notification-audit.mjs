import { randomBytes } from 'node:crypto';
import { mkdir, open, rename, unlink, readdir } from 'node:fs/promises';
import { dirname, isAbsolute, join } from 'node:path';

import { sha256 } from './config.mjs';

const DIRECTORY = 'notification-events';
const MAX_RECORD_BYTES = 4096;
const KINDS = new Set(['task_outcome', 'daily_report', 'weekly_report']);
const RESULTS = new Set([
  'PROVIDER_ACCEPTED',
  'CHANNEL_UNAVAILABLE',
  'DELIVERY_FAILED',
  'PROVIDER_OUTCOME_UNKNOWN',
  'INVALID_INPUT',
]);
const HEX_PATTERN = /^[0-9a-f]{64}$/;

function validIsoTime(value) {
  if (typeof value !== 'string') {
    return false;
  }
  const milliseconds = Date.parse(value);
  return Number.isFinite(milliseconds) && new Date(milliseconds).toISOString() === value;
}

async function atomicWrite(path, record) {
  const temporaryPath = join(dirname(path), `.${randomBytes(16).toString('hex')}.tmp`);
  let handle;
  try {
    handle = await open(temporaryPath, 'wx', 0o600);
    await handle.writeFile(`${JSON.stringify(record)}\n`, 'utf8');
    await handle.sync();
    await handle.close();
    handle = undefined;
    await rename(temporaryPath, path);
  } finally {
    await handle?.close().catch(() => {});
    await unlink(temporaryPath).catch(() => {});
  }
}

async function readRecord(path) {
  let handle;
  try {
    handle = await open(path, 'r');
    const buffer = Buffer.alloc(MAX_RECORD_BYTES + 1);
    const { bytesRead } = await handle.read(buffer, 0, buffer.length, 0);
    if (bytesRead > MAX_RECORD_BYTES) {
      return null;
    }
    const value = JSON.parse(buffer.subarray(0, bytesRead).toString('utf8').replace(/^\ufeff/u, ''));
    if (
      value === null
      || typeof value !== 'object'
      || Array.isArray(value)
      || value.schemaVersion !== 1
      || !HEX_PATTERN.test(value.eventHash)
      || !KINDS.has(value.kind)
      || !RESULTS.has(value.result)
      || !validIsoTime(value.updatedAt)
    ) {
      return null;
    }
    return {
      eventHash: value.eventHash,
      kind: value.kind,
      result: value.result,
      updatedAt: value.updatedAt,
    };
  } catch {
    return null;
  } finally {
    await handle?.close().catch(() => {});
  }
}

export async function recordNotificationOutcome({
  stateRoot,
  idempotencyKey,
  kind,
  result,
  now,
}) {
  if (
    typeof stateRoot !== 'string'
    || !isAbsolute(stateRoot)
    || typeof idempotencyKey !== 'string'
    || idempotencyKey.length === 0
    || !KINDS.has(kind)
    || !RESULTS.has(result)
    || !(now instanceof Date)
    || !Number.isFinite(now.getTime())
  ) {
    throw new Error('Invalid notification audit record');
  }
  const eventHash = sha256(`notification-event-v1\u0000${idempotencyKey}`);
  const directory = join(stateRoot, DIRECTORY);
  await mkdir(directory, { recursive: true });
  await atomicWrite(join(directory, `${eventHash}.json`), {
    schemaVersion: 1,
    eventHash,
    kind,
    result,
    updatedAt: now.toISOString(),
  });
}

export async function summarizeNotificationOutcomes({ stateRoot, since, until }) {
  if (
    typeof stateRoot !== 'string'
    || !isAbsolute(stateRoot)
    || !(since instanceof Date)
    || !(until instanceof Date)
    || !Number.isFinite(since.getTime())
    || !Number.isFinite(until.getTime())
    || since.getTime() > until.getTime()
  ) {
    throw new Error('Invalid notification audit window');
  }
  const directory = join(stateRoot, DIRECTORY);
  let names;
  try {
    names = await readdir(directory);
  } catch (error) {
    if (error?.code === 'ENOENT') {
      return { total: 0, undelivered: 0, byKind: {} };
    }
    throw new Error('Notification audit unavailable');
  }
  const records = await Promise.all(
    names.filter((name) => HEX_PATTERN.test(name.replace(/\.json$/u, '')))
      .map((name) => readRecord(join(directory, name))),
  );
  const inWindow = records.filter((record) => {
    if (record === null) {
      return false;
    }
    const time = Date.parse(record.updatedAt);
    return time >= since.getTime() && time <= until.getTime();
  });
  const failures = inWindow.filter((record) => record.result !== 'PROVIDER_ACCEPTED');
  const byKind = {};
  for (const record of failures) {
    byKind[record.kind] = (byKind[record.kind] ?? 0) + 1;
  }
  return {
    total: inWindow.length,
    undelivered: failures.length,
    byKind,
  };
}
