import { createHash } from 'node:crypto';

import { buildNotificationCard } from './notification-card.mjs';
import { parsePrivateConfig, sha256 } from './config.mjs';
import { hashSendIntentKey } from './send-intent-store.mjs';
import { ProviderRejectedError } from './send-runtime.mjs';

const PROVIDER = 'feishu';
const PROVIDER_ID_PATTERN = /^[\x21-\x7e]{1,256}$/;
const KEY_CONTROL_PATTERN = /[\u0000-\u001f\u007f]/u;

function stableUuid(key) {
  const bytes = createHash('sha256')
    .update(`ordinary-notification-uuid-v1\u0000${PROVIDER}\u0000${key}`, 'utf8')
    .digest()
    .subarray(0, 16);
  bytes[6] = (bytes[6] & 0x0f) | 0x40;
  bytes[8] = (bytes[8] & 0x3f) | 0x80;
  const hex = bytes.toString('hex');
  return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`;
}

function validDate(value) {
  return value instanceof Date && Number.isFinite(value.getTime());
}

function validIsoTime(value) {
  if (typeof value !== 'string') {
    return false;
  }
  const milliseconds = Date.parse(value);
  return Number.isFinite(milliseconds) && new Date(milliseconds).toISOString() === value;
}

function dataFields(value, keys) {
  if (value === null || typeof value !== 'object' || Array.isArray(value)) {
    return null;
  }
  const prototype = Object.getPrototypeOf(value);
  if (prototype !== Object.prototype && prototype !== null) {
    return null;
  }
  const descriptors = Object.getOwnPropertyDescriptors(value);
  const fields = Object.create(null);
  for (const key of keys) {
    const descriptor = descriptors[key];
    if (!descriptor || !Object.hasOwn(descriptor, 'value') || !descriptor.enumerable) {
      return null;
    }
    fields[key] = descriptor.value;
  }
  return fields;
}

function validMember(object, name, type) {
  if (object === null || typeof object !== 'object') {
    return false;
  }
  const descriptor = Object.getOwnPropertyDescriptor(object, name);
  return descriptor !== undefined
    && Object.hasOwn(descriptor, 'value')
    && typeof descriptor.value === type;
}

function providerIdentity(response) {
  const fields = dataFields(response, ['messageId', 'chatId']);
  return fields !== null
    && PROVIDER_ID_PATTERN.test(fields.messageId)
    && PROVIDER_ID_PATTERN.test(fields.chatId)
    ? fields
    : null;
}

function storedFields(outcome) {
  if (outcome === null || typeof outcome !== 'object' || Array.isArray(outcome)) {
    return null;
  }
  const fields = Object.create(null);
  const descriptors = Object.getOwnPropertyDescriptors(outcome);
  for (const key of [
    'status',
    'targetHash',
    'cardNonceHash',
    'intentKeyHash',
    'providerMessageIdHash',
    'providerChatIdHash',
    'resultAt',
  ]) {
    if (descriptors[key] && Object.hasOwn(descriptors[key], 'value')) {
      fields[key] = descriptors[key].value;
    }
  }
  return fields;
}

export async function sendNotification(request) {
  const fields = dataFields(request, [
    'config',
    'notification',
    'idempotencyKey',
    'transport',
    'intentStore',
    'now',
  ]);
  if (fields === null) {
    throw new Error('Invalid notification request');
  }
  let config;
  try {
    config = parsePrivateConfig(fields.config);
  } catch {
    throw new Error('Invalid notification request');
  }
  if (
    typeof fields.idempotencyKey !== 'string'
    || fields.idempotencyKey.length === 0
    || fields.idempotencyKey.length > 512
    || KEY_CONTROL_PATTERN.test(fields.idempotencyKey)
    || !validMember(fields.transport, 'sendInteractive', 'function')
    || !validMember(fields.intentStore, 'run', 'function')
    || !validDate(fields.now)
  ) {
    throw new Error('Invalid notification request');
  }
  let card;
  try {
    card = buildNotificationCard(fields.notification);
  } catch {
    throw new Error('Invalid notification request');
  }
  const intentId = `ordinary-notification:${fields.idempotencyKey}`;
  const uuid = stableUuid(fields.idempotencyKey);
  const targetHash = sha256(config.recipient.value);
  const content = JSON.stringify(card);
  const requestContentHash = sha256(content);
  const cardNonceHash = sha256(`ordinary-notification-card-v1\u0000${fields.idempotencyKey}`);
  const intentKeyHash = hashSendIntentKey(PROVIDER, intentId, 1);
  const transportRequest = {
    params: { receive_id_type: config.recipient.type },
    data: {
      receive_id: config.recipient.value,
      msg_type: 'interactive',
      content,
      uuid,
    },
  };

  let stored;
  try {
    stored = await fields.intentStore.run({
      provider: PROVIDER,
      decisionId: intentId,
      intentKeyHash,
      attemptNumber: 1,
      uuid,
      targetHash,
      requestContentHash,
      cardNonceHash,
      now: fields.now,
    }, async () => {
      try {
        const response = await fields.transport.sendInteractive(transportRequest);
        const identity = providerIdentity(response);
        return identity === null
          ? { status: 'OUTCOME_UNKNOWN' }
          : {
              status: 'ACCEPTED',
              providerMessageIdHash: sha256(identity.messageId),
              providerChatIdHash: sha256(identity.chatId),
            };
      } catch (error) {
        return error instanceof ProviderRejectedError
          ? { status: 'REJECTED' }
          : { status: 'OUTCOME_UNKNOWN' };
      }
    });
  } catch {
    return { result: 'PROVIDER_OUTCOME_UNKNOWN', targetHash, cardNonceHash, intentKeyHash };
  }

  const outcome = storedFields(stored);
  if (
    outcome !== null
    && outcome.targetHash === targetHash
    && outcome.cardNonceHash === cardNonceHash
    && outcome.intentKeyHash === intentKeyHash
    && outcome.status === 'ACCEPTED'
    && /^[0-9a-f]{64}$/u.test(outcome.providerMessageIdHash)
    && /^[0-9a-f]{64}$/u.test(outcome.providerChatIdHash)
    && validIsoTime(outcome.resultAt)
  ) {
    return {
      result: 'PROVIDER_ACCEPTED',
      acceptedAt: outcome.resultAt,
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
    return { result: 'DELIVERY_FAILED', targetHash };
  }
  return { result: 'PROVIDER_OUTCOME_UNKNOWN', targetHash, cardNonceHash, intentKeyHash };
}
