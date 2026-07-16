import { createHash, createHmac } from 'node:crypto';

import { buildDecisionCard } from './card.mjs';
import { parsePrivateConfig, sha256 } from './config.mjs';
import { hashSendIntentKey } from './send-intent-store.mjs';
import { ProviderRejectedError } from './send-runtime.mjs';

const PROVIDER = 'feishu';
const MAX_HEALTH_AGE_MS = 120_000;
const PROVIDER_ID_PATTERN = /^[\x21-\x7e]{1,256}$/;

function stableDigest(domain, decisionId, attemptNumber) {
  return createHash('sha256')
    .update(`${domain}\u0000${PROVIDER}\u0000${decisionId}\u0000${attemptNumber}`, 'utf8')
    .digest();
}

function stableUuid(decisionId, attemptNumber) {
  const bytes = stableDigest('message-uuid-v1', decisionId, attemptNumber).subarray(0, 16);
  bytes[6] = (bytes[6] & 0x0f) | 0x40;
  bytes[8] = (bytes[8] & 0x3f) | 0x80;
  const hex = bytes.toString('hex');
  return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`;
}

function stableCardNonce(hmacKey, decisionId, attemptNumber) {
  return createHmac('sha256', Buffer.from(hmacKey, 'base64'))
    .update(`card-nonce-hmac-v1\u0000${PROVIDER}\u0000${decisionId}\u0000${attemptNumber}`, 'utf8')
    .digest('hex');
}

function validDate(value) {
  return value instanceof Date && Number.isFinite(value.getTime());
}

function snapshotDataFields(value, keys) {
  if (value === null || typeof value !== 'object' || Array.isArray(value)) {
    return null;
  }
  const prototype = Object.getPrototypeOf(value);
  if (prototype !== Object.prototype && prototype !== null) {
    return null;
  }
  const descriptors = Object.getOwnPropertyDescriptors(value);
  const snapshot = Object.create(null);
  for (const key of keys) {
    const descriptor = descriptors[key];
    if (!descriptor || !Object.hasOwn(descriptor, 'value') || !descriptor.enumerable) {
      return null;
    }
    snapshot[key] = descriptor.value;
  }
  return snapshot;
}

function snapshotHealth(health) {
  if (health === null || typeof health !== 'object' || Array.isArray(health)) {
    return null;
  }
  const descriptors = Object.getOwnPropertyDescriptors(health);
  const values = Object.create(null);
  for (const key of ['status', 'updatedAt', 'pid', 'pidAlive']) {
    const descriptor = descriptors[key];
    if (!descriptor || !Object.hasOwn(descriptor, 'value') || !descriptor.enumerable) {
      return null;
    }
    values[key] = descriptor.value;
  }
  return values;
}

function isHealthy(health, now) {
  const snapshot = snapshotHealth(health);
  if (
    snapshot === null
    || snapshot.status !== 'CONNECTED'
    || typeof snapshot.updatedAt !== 'string'
    || !Number.isInteger(snapshot.pid)
    || snapshot.pid <= 0
    || snapshot.pidAlive !== true
  ) {
    return false;
  }
  const updatedAtMs = Date.parse(snapshot.updatedAt);
  if (!Number.isFinite(updatedAtMs)) {
    return false;
  }
  const ageMs = now.getTime() - updatedAtMs;
  return Number.isFinite(ageMs) && ageMs >= 0 && ageMs <= MAX_HEALTH_AGE_MS;
}

function strictProviderIdentity(response) {
  if (response === null || typeof response !== 'object' || Array.isArray(response)) {
    return null;
  }
  const prototype = Object.getPrototypeOf(response);
  if (prototype !== Object.prototype && prototype !== null) {
    return null;
  }
  const keys = Reflect.ownKeys(response);
  if (keys.length !== 2 || !keys.includes('messageId') || !keys.includes('chatId')) {
    return null;
  }
  const descriptors = Object.getOwnPropertyDescriptors(response);
  const providerId = (key) => {
    const descriptor = descriptors[key];
    return descriptor
      && Object.hasOwn(descriptor, 'value')
      && descriptor.enumerable
      && typeof descriptor.value === 'string'
      && PROVIDER_ID_PATTERN.test(descriptor.value)
      ? descriptor.value
      : null;
  };
  const messageId = providerId('messageId');
  const chatId = providerId('chatId');
  return messageId === null || chatId === null ? null : { messageId, chatId };
}

function validateTransport(transport) {
  if (transport === null || typeof transport !== 'object') {
    return false;
  }
  const descriptor = Object.getOwnPropertyDescriptor(transport, 'sendInteractive');
  return descriptor !== undefined
    && Object.hasOwn(descriptor, 'value')
    && typeof descriptor.value === 'function';
}

function validateIntentStore(intentStore) {
  if (intentStore === null || typeof intentStore !== 'object') {
    return false;
  }
  const descriptor = Object.getOwnPropertyDescriptor(intentStore, 'run');
  return descriptor !== undefined
    && Object.hasOwn(descriptor, 'value')
    && typeof descriptor.value === 'function';
}

function storedOutcomeFields(outcome) {
  if (outcome === null || typeof outcome !== 'object' || Array.isArray(outcome)) {
    return null;
  }
  const prototype = Object.getPrototypeOf(outcome);
  if (prototype !== Object.prototype && prototype !== null) {
    return null;
  }
  const descriptors = Object.getOwnPropertyDescriptors(outcome);
  const fields = Object.create(null);
  for (const key of [
    'status', 'targetHash', 'cardNonceHash', 'intentKeyHash', 'providerMessageIdHash',
    'providerChatIdHash',
  ]) {
    const descriptor = descriptors[key];
    if (descriptor !== undefined && Object.hasOwn(descriptor, 'value')) {
      fields[key] = descriptor.value;
    }
  }
  return fields;
}

function invalidRequest() {
  throw new Error('Invalid send request');
}

export async function sendDecision(request) {
  const fields = snapshotDataFields(request, [
    'config',
    'decision',
    'attemptNumber',
    'transport',
    'intentStore',
    'health',
    'now',
  ]);
  if (fields === null) {
    invalidRequest();
  }
  const {
    config,
    decision,
    attemptNumber,
    transport,
    intentStore,
    health,
    now,
  } = fields;
  let parsedConfig;
  try {
    parsedConfig = parsePrivateConfig(config);
  } catch {
    invalidRequest();
  }
  if (
    !Number.isSafeInteger(attemptNumber)
    || attemptNumber <= 0
    || !validateTransport(transport)
    || !validateIntentStore(intentStore)
    || !validDate(now)
  ) {
    invalidRequest();
  }

  if (!isHealthy(health, now)) {
    return { result: 'CHANNEL_UNAVAILABLE' };
  }

  const decisionFields = snapshotDataFields(decision, ['decisionId']);
  if (decisionFields === null || typeof decisionFields.decisionId !== 'string') {
    invalidRequest();
  }
  const { decisionId } = decisionFields;
  const uuid = stableUuid(decisionId, attemptNumber);
  const cardNonce = stableCardNonce(parsedConfig.hmacKey, decisionId, attemptNumber);
  let card;
  try {
    card = buildDecisionCard(decision, cardNonce);
  } catch {
    invalidRequest();
  }

  const targetHash = sha256(parsedConfig.recipient.value);
  const content = JSON.stringify(card);
  const requestContentHash = sha256(content);
  const cardNonceHash = sha256(cardNonce);
  const intentKeyHash = hashSendIntentKey(PROVIDER, decisionId, attemptNumber);
  const transportRequest = {
    params: {
      receive_id_type: parsedConfig.recipient.type,
    },
    data: {
      receive_id: parsedConfig.recipient.value,
      msg_type: 'interactive',
      content,
      uuid,
    },
  };

  let storedOutcome;
  try {
    const run = Object.getOwnPropertyDescriptor(intentStore, 'run').value;
    storedOutcome = await run.call(intentStore, {
      provider: PROVIDER,
      decisionId,
      intentKeyHash,
      attemptNumber,
      uuid,
      targetHash,
      requestContentHash,
      cardNonceHash,
      now,
    }, async () => {
      try {
        const response = await transport.sendInteractive(transportRequest);
        const providerIdentity = strictProviderIdentity(response);
        if (providerIdentity === null) {
          return { status: 'OUTCOME_UNKNOWN' };
        }
        return {
          status: 'ACCEPTED',
          providerMessageIdHash: sha256(providerIdentity.messageId),
          providerChatIdHash: sha256(providerIdentity.chatId),
        };
      } catch (error) {
        return error instanceof ProviderRejectedError
          ? { status: 'REJECTED' }
          : { status: 'OUTCOME_UNKNOWN' };
      }
    });
  } catch {
    return {
      result: 'PROVIDER_OUTCOME_UNKNOWN',
      targetHash,
      cardNonceHash,
      intentKeyHash,
    };
  }

  const outcome = storedOutcomeFields(storedOutcome);
  if (
    outcome !== null
    && outcome.targetHash === targetHash
    && outcome.cardNonceHash === cardNonceHash
    && outcome.intentKeyHash === intentKeyHash
    && outcome.status === 'ACCEPTED'
    && typeof outcome.providerMessageIdHash === 'string'
    && /^[0-9a-f]{64}$/.test(outcome.providerMessageIdHash)
    && typeof outcome.providerChatIdHash === 'string'
    && /^[0-9a-f]{64}$/.test(outcome.providerChatIdHash)
  ) {
    return {
      result: 'PROVIDER_ACCEPTED',
      targetHash,
      providerMessageIdHash: outcome.providerMessageIdHash,
      providerChatIdHash: outcome.providerChatIdHash,
      cardNonceHash,
      intentKeyHash,
    };
  }
  if (
    outcome !== null
    && outcome.targetHash === targetHash
    && outcome.cardNonceHash === cardNonceHash
    && outcome.intentKeyHash === intentKeyHash
    && outcome.status === 'REJECTED'
  ) {
    return {
      result: 'DELIVERY_FAILED',
      targetHash,
    };
  }
  return {
    result: 'PROVIDER_OUTCOME_UNKNOWN',
    targetHash,
    cardNonceHash,
    intentKeyHash,
  };
}
