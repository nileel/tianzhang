import { createHash, randomBytes } from 'node:crypto';
import {
  mkdir, open, readFile, readdir, rename, stat, unlink,
} from 'node:fs/promises';
import { isAbsolute, join } from 'node:path';

import { parsePrivateConfig, sha256 } from './config.mjs';
import { normalizeCustomText } from './custom-reply.mjs';
import { canonicalize, verifyEnvelope } from './envelope.mjs';

const MAX_ENVELOPE_BYTES = 64 * 1024;
const HEX_PATTERN = /^[0-9a-f]{64}$/;
const IDENTIFIER_PATTERN = /^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$/;
const OPTION_KEYS = ['A', 'B', 'C'];
const ENVELOPE_KEYS = ['schemaVersion', 'payload', 'signature'];
const DECISION_PAYLOAD_KEYS = [
  'kind', 'decisionId', 'optionKey', 'cardNonceHash', 'providerMessageIdHash',
  'providerEventIdHash', 'operatorOpenIdHash', 'tenantKeyHash', 'receivedAt',
];
const CUSTOM_CARD_PAYLOAD_KEYS = [
  'kind', 'decisionId', 'customText', 'cardNonceHash', 'providerMessageIdHash',
  'providerEventIdHash', 'operatorOpenIdHash', 'tenantKeyHash', 'receivedAt', 'source',
];
const CUSTOM_TEXT_PAYLOAD_KEYS = [
  'kind', 'decisionId', 'customText', 'providerMessageIdHash', 'providerEventIdHash',
  'operatorOpenIdHash', 'tenantKeyHash', 'providerChatIdHash', 'receivedAt', 'source',
];
const PAIRING_PAYLOAD_KEYS = [
  'kind', 'pairingNonceHash', 'providerEventIdHash', 'operatorOpenIdHash',
  'tenantKey', 'tenantKeyHash', 'receivedAt',
];
const PENDING_KEYS = [
  'decisionId', 'allowedOptions', 'allowCustomReply', 'createdAt', 'expiresAt',
  'cardNonceHash', 'providerMessageIdHash', 'providerChatIdHash',
];

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

function isHex(value) {
  return typeof value === 'string' && HEX_PATTERN.test(value);
}

function isIdentifier(value) {
  return typeof value === 'string' && IDENTIFIER_PATTERN.test(value);
}

function snapshotDecisionPayload(value) {
  const fields = exactDataObject(value, DECISION_PAYLOAD_KEYS);
  if (
    fields === null
    || fields.kind !== 'decision_reply'
    || !isIdentifier(fields.decisionId)
    || !OPTION_KEYS.includes(fields.optionKey)
    || !isHex(fields.cardNonceHash)
    || !isHex(fields.providerMessageIdHash)
    || !isHex(fields.providerEventIdHash)
    || !isHex(fields.operatorOpenIdHash)
    || !isHex(fields.tenantKeyHash)
    || parseExactIso(fields.receivedAt) === null
  ) {
    return null;
  }
  return { ...fields };
}

function snapshotCustomDecisionPayload(value) {
  const sourceDescriptor = isPlainObject(value)
    ? Object.getOwnPropertyDescriptor(value, 'source')
    : null;
  if (!sourceDescriptor || !Object.hasOwn(sourceDescriptor, 'value')) {
    return null;
  }
  const fields = exactDataObject(
    value,
    sourceDescriptor.value === 'feishu_card_input'
      ? CUSTOM_CARD_PAYLOAD_KEYS
      : CUSTOM_TEXT_PAYLOAD_KEYS,
  );
  const customText = normalizeCustomText(fields?.customText);
  if (
    fields === null
    || fields.kind !== 'decision_custom_reply'
    || !isIdentifier(fields.decisionId)
    || customText === null
    || customText !== fields.customText
    || !isHex(fields.providerMessageIdHash)
    || !isHex(fields.providerEventIdHash)
    || !isHex(fields.operatorOpenIdHash)
    || !isHex(fields.tenantKeyHash)
    || parseExactIso(fields.receivedAt) === null
    || !['feishu_card_input', 'feishu_text'].includes(fields.source)
    || (fields.source === 'feishu_card_input' && !isHex(fields.cardNonceHash))
    || (fields.source === 'feishu_text' && !isHex(fields.providerChatIdHash))
  ) {
    return null;
  }
  return { ...fields };
}

function snapshotPairingPayload(value) {
  const fields = exactDataObject(value, PAIRING_PAYLOAD_KEYS);
  if (
    fields === null
    || fields.kind !== 'operator_pairing'
    || !isHex(fields.pairingNonceHash)
    || !isHex(fields.providerEventIdHash)
    || !isHex(fields.operatorOpenIdHash)
    || !isIdentifier(fields.tenantKey)
    || !isHex(fields.tenantKeyHash)
    || sha256(fields.tenantKey) !== fields.tenantKeyHash
    || parseExactIso(fields.receivedAt) === null
  ) {
    return null;
  }
  return { ...fields };
}

function snapshotEnvelope(value) {
  const fields = exactDataObject(value, ENVELOPE_KEYS);
  if (
    fields === null
    || fields.schemaVersion !== 1
    || !isHex(fields.signature)
  ) {
    return null;
  }
  const kindDescriptor = isPlainObject(fields.payload)
    ? Object.getOwnPropertyDescriptor(fields.payload, 'kind')
    : null;
  if (!kindDescriptor || !Object.hasOwn(kindDescriptor, 'value')) {
    return null;
  }
  let payload;
  if (kindDescriptor.value === 'decision_reply') {
    payload = snapshotDecisionPayload(fields.payload);
  } else if (kindDescriptor.value === 'decision_custom_reply') {
    payload = snapshotCustomDecisionPayload(fields.payload);
  } else {
    payload = snapshotPairingPayload(fields.payload);
  }
  if (payload === null) {
    return null;
  }
  return { payload, directory: payload.kind === 'operator_pairing' ? 'pairing-inbox' : 'inbox' };
}

function snapshotPending(value) {
  const fields = exactDataObject(value, PENDING_KEYS);
  const options = exactDataArray(fields?.allowedOptions);
  const createdAtMs = parseExactIso(fields?.createdAt);
  const expiresAtMs = parseExactIso(fields?.expiresAt);
  if (
    fields === null
    || !isIdentifier(fields.decisionId)
    || options === null
    || options.length !== OPTION_KEYS.length
    || options.some((option, index) => option !== OPTION_KEYS[index])
    || typeof fields.allowCustomReply !== 'boolean'
    || createdAtMs === null
    || expiresAtMs === null
    || createdAtMs > expiresAtMs
    || !isHex(fields.cardNonceHash)
    || !isHex(fields.providerMessageIdHash)
    || !isHex(fields.providerChatIdHash)
  ) {
    return null;
  }
  return {
    decisionId: fields.decisionId,
    allowedOptions: [...options],
    allowCustomReply: fields.allowCustomReply,
    createdAtMs,
    expiresAtMs,
    cardNonceHash: fields.cardNonceHash,
    providerMessageIdHash: fields.providerMessageIdHash,
    providerChatIdHash: fields.providerChatIdHash,
  };
}

function requireStateRoot(stateRoot) {
  if (typeof stateRoot !== 'string' || !isAbsolute(stateRoot)) {
    throw new Error();
  }
}

async function readBounded(path) {
  const details = await stat(path);
  if (!details.isFile() || details.size > MAX_ENVELOPE_BYTES) {
    throw new Error();
  }
  const value = await readFile(path);
  if (value.length > MAX_ENVELOPE_BYTES) {
    throw new Error();
  }
  return value.toString('utf8').replace(/^\ufeff/, '');
}

async function writeAtomic(path, content) {
  const temporaryPath = `${path}.${process.pid}.${randomBytes(12).toString('hex')}.tmp`;
  let handle;
  try {
    handle = await open(temporaryPath, 'wx', 0o600);
    await handle.writeFile(content, 'utf8');
    await handle.sync();
    await handle.close();
    handle = null;
    await rename(temporaryPath, path);
  } finally {
    await handle?.close().catch(() => {});
    await unlink(temporaryPath).catch(() => {});
  }
}

async function compareCanonicalFile(path, canonical) {
  try {
    const existing = await readBounded(path);
    return canonicalize(JSON.parse(existing)) === canonical ? 'same' : 'different';
  } catch (error) {
    if (error?.code === 'ENOENT') {
      return 'missing';
    }
    return 'different';
  }
}

async function checkExistingEvidence(stateRoot, directoryNames, eventIdHash, canonical) {
  for (const directoryName of directoryNames) {
    const result = await compareCanonicalFile(
      join(stateRoot, directoryName, `${eventIdHash}.json`),
      canonical,
    );
    if (result === 'same') {
      return 'same';
    }
    if (result === 'different') {
      return 'different';
    }
  }
  return 'missing';
}

export async function writeSignedInbox({ stateRoot, envelope, eventIdHash }) {
  let lockHandle;
  let lockPath;
  try {
    requireStateRoot(stateRoot);
    if (!isHex(eventIdHash)) {
      throw new Error();
    }
    const snapshot = snapshotEnvelope(envelope);
    if (snapshot === null || snapshot.payload.providerEventIdHash !== eventIdHash) {
      throw new Error();
    }
    const canonical = canonicalize(envelope);
    if (Buffer.byteLength(canonical, 'utf8') > MAX_ENVELOPE_BYTES) {
      throw new Error();
    }
    const evidenceDirectories = snapshot.directory === 'inbox'
      ? ['inbox', 'processed', 'quarantine']
      : ['pairing-inbox'];
    const directory = join(stateRoot, snapshot.directory);
    await mkdir(directory, { recursive: true });
    const targetPath = join(directory, `${eventIdHash}.json`);
    const existing = await checkExistingEvidence(
      stateRoot,
      evidenceDirectories,
      eventIdHash,
      canonical,
    );
    if (existing === 'same') {
      return { written: false, duplicate: true };
    }
    if (existing === 'different') {
      throw new Error();
    }

    lockPath = join(directory, `${eventIdHash}.lock`);
    lockHandle = await open(lockPath, 'wx', 0o600);
    const lockedExisting = await checkExistingEvidence(
      stateRoot,
      evidenceDirectories,
      eventIdHash,
      canonical,
    );
    if (lockedExisting === 'same') {
      return { written: false, duplicate: true };
    }
    if (lockedExisting === 'different') {
      throw new Error();
    }
    await writeAtomic(targetPath, canonical);
    return { written: true, duplicate: false };
  } catch {
    throw new Error('Inbox write failed');
  } finally {
    await lockHandle?.close().catch(() => {});
    if (lockPath !== undefined) {
      await unlink(lockPath).catch(() => {});
    }
  }
}

async function movePreserving(sourcePath, destinationDirectory, name, raw) {
  await mkdir(destinationDirectory, { recursive: true });
  let destinationPath = join(destinationDirectory, name);
  try {
    await rename(sourcePath, destinationPath);
    return;
  } catch (error) {
    if (!['EEXIST', 'EPERM'].includes(error?.code)) {
      throw error;
    }
  }
  try {
    if (await readBounded(destinationPath) === raw) {
      await unlink(sourcePath);
      return;
    }
  } catch {
    // Preserve both artifacts under a deterministic hash-derived name.
  }
  for (let counter = 0; counter < 32; counter += 1) {
    const derived = createHash('sha256')
      .update(`${name}\u0000${counter}\u0000${raw}`, 'utf8')
      .digest('hex');
    destinationPath = join(destinationDirectory, `${derived}.json`);
    try {
      await rename(sourcePath, destinationPath);
      return;
    } catch (error) {
      if (!['EEXIST', 'EPERM'].includes(error?.code)) {
        throw error;
      }
    }
  }
  throw new Error();
}

function isCurrentPayload(payload, pending, config, nowMs) {
  const receivedAtMs = parseExactIso(payload.receivedAt);
  const common = payload.decisionId === pending.decisionId
    && payload.providerMessageIdHash === pending.providerMessageIdHash
    && payload.operatorOpenIdHash === config.pairedOperatorOpenIdHash
    && payload.tenantKeyHash === sha256(config.expectedTenantKey)
    && receivedAtMs !== null
    && receivedAtMs >= pending.createdAtMs
    && receivedAtMs <= pending.expiresAtMs
    && receivedAtMs <= nowMs
    && nowMs <= pending.expiresAtMs;
  if (!common) {
    return false;
  }
  if (payload.kind === 'decision_reply') {
    return pending.allowedOptions.includes(payload.optionKey)
      && payload.cardNonceHash === pending.cardNonceHash;
  }
  if (payload.kind !== 'decision_custom_reply' || !pending.allowCustomReply) {
    return false;
  }
  return payload.source === 'feishu_card_input'
    ? payload.cardNonceHash === pending.cardNonceHash
    : payload.source === 'feishu_text'
      && payload.providerChatIdHash === pending.providerChatIdHash;
}

function payloadIdentity(payload) {
  return payload.kind === 'decision_reply'
    ? `option:${payload.optionKey}`
    : `custom:${sha256(payload.customText)}`;
}

function snapshotDecisionEvidence(value) {
  if (!isPlainObject(value)) {
    return null;
  }
  const kindDescriptor = Object.getOwnPropertyDescriptor(value, 'kind');
  if (!kindDescriptor || !Object.hasOwn(kindDescriptor, 'value')) {
    return null;
  }
  return kindDescriptor.value === 'decision_reply'
    ? snapshotDecisionPayload(value)
    : snapshotCustomDecisionPayload(value);
}

async function readProcessedEvidence(processedDirectory, encodedKey, pending) {
  const nonces = new Set();
  const identities = new Set();
  let healthy = true;
  const names = (await readdir(processedDirectory))
    .filter((name) => /^[0-9a-f]{64}\.json$/.test(name))
    .sort();
  for (const name of names) {
    try {
      const raw = await readBounded(join(processedDirectory, name));
      const envelope = JSON.parse(raw);
      const snapshot = snapshotEnvelope(envelope);
      const payload = snapshotDecisionEvidence(verifyEnvelope(envelope, encodedKey));
      if (
        snapshot === null
        || snapshot.directory !== 'inbox'
        || payload === null
        || `${payload.providerEventIdHash}.json` !== name
      ) {
        throw new Error();
      }
      if (payload.kind === 'decision_reply' || payload.source === 'feishu_card_input') {
        nonces.add(payload.cardNonceHash);
      }
      if (
        payload.decisionId === pending.decisionId
        && payload.providerMessageIdHash === pending.providerMessageIdHash
      ) {
        identities.add(payloadIdentity(payload));
      }
    } catch {
      healthy = false;
    }
  }
  return { nonces, identities, healthy: healthy && identities.size <= 1 };
}

function acceptedOutput(payload, envelope) {
  if (payload.kind === 'decision_custom_reply') {
    const result = {
      result: 'CUSTOM_ACCEPTED',
      decisionId: payload.decisionId,
      customText: payload.customText,
      source: payload.source,
      providerMessageIdHash: payload.providerMessageIdHash,
      providerEventIdHash: payload.providerEventIdHash,
      operatorOpenIdHash: payload.operatorOpenIdHash,
      tenantKeyHash: payload.tenantKeyHash,
      evidenceHash: sha256(canonicalize(envelope)),
    };
    if (payload.source === 'feishu_card_input') {
      result.cardNonceHash = payload.cardNonceHash;
    } else {
      result.providerChatIdHash = payload.providerChatIdHash;
    }
    return result;
  }
  return {
    result: 'OPTION_ACCEPTED',
    optionKey: payload.optionKey,
    source: 'feishu_card',
    providerMessageIdHash: payload.providerMessageIdHash,
    providerEventIdHash: payload.providerEventIdHash,
    operatorOpenIdHash: payload.operatorOpenIdHash,
    tenantKeyHash: payload.tenantKeyHash,
    cardNonceHash: payload.cardNonceHash,
    evidenceHash: sha256(canonicalize(envelope)),
  };
}

export async function consumeCurrentReply({ stateRoot, config, pendingDecision, now }) {
  let parsedConfig;
  let pending;
  try {
    requireStateRoot(stateRoot);
    parsedConfig = parsePrivateConfig(config);
    pending = snapshotPending(pendingDecision);
    if (
      parsedConfig.stateRoot !== stateRoot
      || parsedConfig.expectedTenantKey === null
      || parsedConfig.pairedOperatorOpenIdHash === null
      || pending === null
      || !(now instanceof Date)
      || !Number.isFinite(now.getTime())
    ) {
      return null;
    }
  } catch {
    return null;
  }

  await mkdir(stateRoot, { recursive: true }).catch(() => {});
  const lockPath = join(stateRoot, 'consume.lock');
  let lockHandle;
  try {
    try {
      lockHandle = await open(lockPath, 'wx', 0o600);
    } catch (error) {
      if (error?.code === 'EEXIST') {
        return null;
      }
      return null;
    }
    const inboxDirectory = join(stateRoot, 'inbox');
    const processedDirectory = join(stateRoot, 'processed');
    const quarantineDirectory = join(stateRoot, 'quarantine');
    await Promise.all([
      mkdir(inboxDirectory, { recursive: true }),
      mkdir(processedDirectory, { recursive: true }),
      mkdir(quarantineDirectory, { recursive: true }),
      mkdir(join(stateRoot, 'pairing-inbox'), { recursive: true }),
    ]);
    const names = (await readdir(inboxDirectory))
      .filter((name) => /^[0-9a-f]{64}\.json$/.test(name))
      .sort();
    const consumed = await readProcessedEvidence(processedDirectory, parsedConfig.hmacKey, pending);
    const valid = [];

    for (const name of names) {
      const sourcePath = join(inboxDirectory, name);
      let raw = '';
      try {
        raw = await readBounded(sourcePath);
        const envelope = JSON.parse(raw);
        const envelopeSnapshot = snapshotEnvelope(envelope);
        const payload = verifyEnvelope(envelope, parsedConfig.hmacKey);
        const payloadSnapshot = snapshotDecisionEvidence(payload);
        if (
          envelopeSnapshot === null
          || envelopeSnapshot.directory !== 'inbox'
          || payloadSnapshot === null
          || `${payloadSnapshot.providerEventIdHash}.json` !== name
          || !isCurrentPayload(payloadSnapshot, pending, parsedConfig, now.getTime())
          || !consumed.healthy
          || ((payloadSnapshot.kind === 'decision_reply'
            || payloadSnapshot.source === 'feishu_card_input')
            && consumed.nonces.has(payloadSnapshot.cardNonceHash))
        ) {
          throw new Error();
        }
        valid.push({
          name,
          sourcePath,
          raw,
          envelope,
          payload: payloadSnapshot,
          receivedAtMs: parseExactIso(payloadSnapshot.receivedAt),
        });
      } catch {
        await movePreserving(sourcePath, quarantineDirectory, name, raw).catch(() => {});
      }
    }

    if (valid.length === 0) {
      return null;
    }
    valid.sort((left, right) => (
      left.receivedAtMs - right.receivedAtMs
      || left.payload.providerEventIdHash.localeCompare(right.payload.providerEventIdHash)
    ));
    const existingIdentity = [...consumed.identities][0];
    const winner = existingIdentity === undefined ? valid[0] : null;
    const winningIdentity = existingIdentity ?? payloadIdentity(winner.payload);
    for (const item of valid) {
      const destination = payloadIdentity(item.payload) === winningIdentity
        ? processedDirectory
        : quarantineDirectory;
      await movePreserving(item.sourcePath, destination, item.name, item.raw);
    }
    return winner === null ? null : acceptedOutput(winner.payload, winner.envelope);
  } catch {
    return null;
  } finally {
    await lockHandle?.close().catch(() => {});
    if (lockHandle !== undefined) {
      await unlink(lockPath).catch(() => {});
    }
  }
}
