import { parsePrivateConfig, sha256 } from './config.mjs';
import { signEnvelope } from './envelope.mjs';
import { writeSignedInbox } from './inbox.mjs';

const IDENTIFIER_PATTERN = /^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$/;
const HEX_PATTERN = /^[0-9a-f]{64}$/;
const OPTION_KEYS = ['A', 'B', 'C'];
const ROOT_KEYS = ['schema', 'header', 'event'];
const HEADER_KEYS = ['event_id', 'create_time', 'event_type', 'tenant_key', 'app_id'];
const EVENT_KEYS = ['operator', 'action', 'context'];
const OPERATOR_KEYS = ['tenant_key', 'open_id'];
const ACTION_KEYS = ['tag', 'value'];
const CONTEXT_KEYS = ['open_message_id'];
const DECISION_VALUE_KEYS = ['kind', 'decisionId', 'optionKey', 'cardNonce'];
const PAIRING_VALUE_KEYS = ['kind', 'pairingNonce'];

const REJECTED = Object.freeze({
  accepted: false,
  response: Object.freeze({
    toast: Object.freeze({ type: 'warning', content: '未登记或已过期' }),
  }),
});

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
    if (
      !descriptor
      || !Object.hasOwn(descriptor, 'value')
      || !descriptor.enumerable
    ) {
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
  const lengthDescriptor = descriptors.length;
  if (
    !lengthDescriptor
    || !Object.hasOwn(lengthDescriptor, 'value')
    || lengthDescriptor.value !== value.length
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

function isIdentifier(value) {
  return typeof value === 'string' && IDENTIFIER_PATTERN.test(value);
}

function isHex(value) {
  return typeof value === 'string' && HEX_PATTERN.test(value);
}

function parseExactIso(value) {
  if (typeof value !== 'string') {
    return null;
  }
  const time = Date.parse(value);
  if (!Number.isFinite(time) || new Date(time).toISOString() !== value) {
    return null;
  }
  return time;
}

function parseCreateTime(value) {
  if (typeof value !== 'string' || !/^\d{16}$/.test(value)) {
    return null;
  }
  try {
    const micros = BigInt(value);
    const millis = micros / 1000n;
    if (millis > BigInt(Number.MAX_SAFE_INTEGER)) {
      return null;
    }
    const result = Number(millis);
    return Number.isFinite(new Date(result).getTime()) ? result : null;
  } catch {
    return null;
  }
}

function snapshotActionValue(value) {
  if (!isPlainObject(value)) {
    return null;
  }
  const kindDescriptor = Object.getOwnPropertyDescriptor(value, 'kind');
  if (!kindDescriptor || !Object.hasOwn(kindDescriptor, 'value')) {
    return null;
  }
  if (kindDescriptor.value === 'decision_reply') {
    const fields = exactDataObject(value, DECISION_VALUE_KEYS);
    if (
      fields === null
      || !isIdentifier(fields.decisionId)
      || !OPTION_KEYS.includes(fields.optionKey)
      || !isIdentifier(fields.cardNonce)
    ) {
      return null;
    }
    return {
      kind: 'decision_reply',
      decisionId: fields.decisionId,
      optionKey: fields.optionKey,
      cardNonce: fields.cardNonce,
    };
  }
  if (kindDescriptor.value === 'operator_pairing') {
    const fields = exactDataObject(value, PAIRING_VALUE_KEYS);
    if (fields === null || !isIdentifier(fields.pairingNonce)) {
      return null;
    }
    return {
      kind: 'operator_pairing',
      pairingNonce: fields.pairingNonce,
    };
  }
  return null;
}

export function normalizeCardAction(rawEvent) {
  try {
    const root = exactDataObject(rawEvent, ROOT_KEYS);
    const header = exactDataObject(root?.header, HEADER_KEYS);
    const event = exactDataObject(root?.event, EVENT_KEYS);
    const operator = exactDataObject(event?.operator, OPERATOR_KEYS);
    const action = exactDataObject(event?.action, ACTION_KEYS);
    const context = exactDataObject(event?.context, CONTEXT_KEYS);
    const actionValue = snapshotActionValue(action?.value);
    const createTimeMs = parseCreateTime(header?.create_time);
    if (
      root === null
      || root.schema !== '2.0'
      || header === null
      || event === null
      || operator === null
      || action === null
      || context === null
      || header.event_type !== 'card.action.trigger'
      || action.tag !== 'button'
      || actionValue === null
      || createTimeMs === null
      || !isIdentifier(header.event_id)
      || !isIdentifier(header.app_id)
      || !isIdentifier(header.tenant_key)
      || !isIdentifier(operator.tenant_key)
      || !isIdentifier(operator.open_id)
      || !isIdentifier(context.open_message_id)
    ) {
      throw new Error();
    }
    return {
      eventId: header.event_id,
      createTimeMs,
      eventType: header.event_type,
      appId: header.app_id,
      headerTenantKey: header.tenant_key,
      operatorTenantKey: operator.tenant_key,
      operatorOpenId: operator.open_id,
      messageId: context.open_message_id,
      action: actionValue,
    };
  } catch {
    throw new Error('Invalid card action');
  }
}

function snapshotAllowedOptions(value) {
  const options = exactDataArray(value);
  if (
    options === null
    || options.length !== OPTION_KEYS.length
    || options.some((option, index) => option !== OPTION_KEYS[index])
  ) {
    return null;
  }
  return [...options];
}

function snapshotDecisionBinding(value) {
  if (!isPlainObject(value)) {
    return null;
  }
  const keys = Reflect.ownKeys(value);
  const allowed = new Set([
    'kind', 'decisionId', 'allowedOptions', 'issuedAt', 'expiresAt',
    'cardNonceHash', 'providerMessageIdHash',
  ]);
  const required = [
    'decisionId', 'allowedOptions', 'expiresAt', 'cardNonceHash', 'providerMessageIdHash',
  ];
  if (
    keys.some((key) => typeof key !== 'string' || !allowed.has(key))
    || required.some((key) => !keys.includes(key))
    || keys.length < required.length
    || keys.length > allowed.size
  ) {
    return null;
  }
  const descriptors = Object.getOwnPropertyDescriptors(value);
  for (const key of keys) {
    const descriptor = descriptors[key];
    if (!descriptor || !Object.hasOwn(descriptor, 'value') || !descriptor.enumerable) {
      return null;
    }
  }
  const field = (key) => descriptors[key]?.value;
  const options = snapshotAllowedOptions(field('allowedOptions'));
  const expiresAtMs = parseExactIso(field('expiresAt'));
  const issuedAtMs = keys.includes('issuedAt') ? parseExactIso(field('issuedAt')) : null;
  if (
    (keys.includes('kind') && field('kind') !== 'decision_reply')
    || !isIdentifier(field('decisionId'))
    || options === null
    || expiresAtMs === null
    || (keys.includes('issuedAt') && issuedAtMs === null)
    || !isHex(field('cardNonceHash'))
    || !isHex(field('providerMessageIdHash'))
  ) {
    return null;
  }
  return {
    kind: 'decision_reply',
    decisionId: field('decisionId'),
    allowedOptions: options,
    expiresAtMs,
    issuedAtMs,
    cardNonceHash: field('cardNonceHash'),
    providerMessageIdHash: field('providerMessageIdHash'),
  };
}

function snapshotPairingBinding(value) {
  const fields = exactDataObject(value, ['kind', 'pairingNonceHash', 'expiresAt']);
  const expiresAtMs = parseExactIso(fields?.expiresAt);
  if (
    fields === null
    || fields.kind !== 'operator_pairing'
    || !isHex(fields.pairingNonceHash)
    || expiresAtMs === null
  ) {
    return null;
  }
  return {
    kind: 'operator_pairing',
    pairingNonceHash: fields.pairingNonceHash,
    expiresAtMs,
  };
}

function snapshotBindings(value) {
  const rawBindings = Array.isArray(value) ? exactDataArray(value) : [value];
  if (rawBindings === null || rawBindings.length === 0 || rawBindings.length > 128) {
    return null;
  }
  const bindings = rawBindings.map((binding) => {
    const kindDescriptor = isPlainObject(binding)
      ? Object.getOwnPropertyDescriptor(binding, 'kind')
      : null;
    if (
      kindDescriptor
      && Object.hasOwn(kindDescriptor, 'value')
      && kindDescriptor.value === 'operator_pairing'
    ) {
      return snapshotPairingBinding(binding);
    }
    return snapshotDecisionBinding(binding);
  });
  return bindings.some((binding) => binding === null) ? null : bindings;
}

function readonlyDecisionCard(optionKey, receivedAt) {
  return {
    config: { wide_screen_mode: true },
    header: {
      template: 'green',
      title: { tag: 'plain_text', content: `已选择 ${optionKey}` },
    },
    elements: [{
      tag: 'div',
      text: {
        tag: 'plain_text',
        content: `已选择 ${optionKey}\n登记时间：${receivedAt}`,
      },
    }],
  };
}

function acceptedDecisionResponse(optionKey, receivedAt) {
  return {
    accepted: true,
    response: {
      toast: { type: 'success', content: `已选择 ${optionKey}` },
      card: { type: 'raw', data: readonlyDecisionCard(optionKey, receivedAt) },
    },
  };
}

function acceptedPairingResponse() {
  return {
    accepted: true,
    response: {
      toast: { type: 'info', content: '配对信息已登记' },
    },
  };
}

export async function handleCardAction({ event, config, pendingBindings, now }) {
  try {
    const parsedConfig = parsePrivateConfig(config);
    if (!(now instanceof Date) || !Number.isFinite(now.getTime())) {
      throw new Error();
    }
    const normalized = normalizeCardAction(event);
    const bindings = snapshotBindings(pendingBindings);
    if (
      bindings === null
      || normalized.appId !== parsedConfig.appId
      || normalized.headerTenantKey !== normalized.operatorTenantKey
      || (parsedConfig.expectedTenantKey !== null
        && normalized.headerTenantKey !== parsedConfig.expectedTenantKey)
      || normalized.createTimeMs > now.getTime()
    ) {
      throw new Error();
    }

    const providerEventIdHash = sha256(normalized.eventId);
    const operatorOpenIdHash = sha256(normalized.operatorOpenId);
    const tenantKeyHash = sha256(normalized.headerTenantKey);
    const receivedAt = now.toISOString();

    if (normalized.action.kind === 'operator_pairing') {
      const binding = bindings.find((candidate) => candidate.kind === 'operator_pairing');
      if (
        binding === undefined
        || now.getTime() > binding.expiresAtMs
        || normalized.createTimeMs > binding.expiresAtMs
        || sha256(normalized.action.pairingNonce) !== binding.pairingNonceHash
      ) {
        throw new Error();
      }
      const payload = {
        kind: 'operator_pairing',
        pairingNonceHash: binding.pairingNonceHash,
        providerEventIdHash,
        operatorOpenIdHash,
        tenantKey: normalized.headerTenantKey,
        tenantKeyHash,
        receivedAt,
      };
      const envelope = signEnvelope(payload, parsedConfig.hmacKey);
      await writeSignedInbox({
        stateRoot: parsedConfig.stateRoot,
        envelope,
        eventIdHash: providerEventIdHash,
      });
      return acceptedPairingResponse();
    }

    const binding = bindings.find((candidate) => (
      candidate.kind === 'decision_reply'
      && candidate.decisionId === normalized.action.decisionId
    ));
    if (
      binding === undefined
      || parsedConfig.expectedTenantKey === null
      || parsedConfig.pairedOperatorOpenIdHash === null
      || operatorOpenIdHash !== parsedConfig.pairedOperatorOpenIdHash
      || now.getTime() > binding.expiresAtMs
      || normalized.createTimeMs > binding.expiresAtMs
      || (binding.issuedAtMs !== null && normalized.createTimeMs < binding.issuedAtMs)
      || !binding.allowedOptions.includes(normalized.action.optionKey)
      || sha256(normalized.action.cardNonce) !== binding.cardNonceHash
      || sha256(normalized.messageId) !== binding.providerMessageIdHash
    ) {
      throw new Error();
    }
    const payload = {
      kind: 'decision_reply',
      decisionId: normalized.action.decisionId,
      optionKey: normalized.action.optionKey,
      cardNonceHash: binding.cardNonceHash,
      providerMessageIdHash: binding.providerMessageIdHash,
      providerEventIdHash,
      operatorOpenIdHash,
      tenantKeyHash,
      receivedAt,
    };
    const envelope = signEnvelope(payload, parsedConfig.hmacKey);
    await writeSignedInbox({
      stateRoot: parsedConfig.stateRoot,
      envelope,
      eventIdHash: providerEventIdHash,
    });
    return acceptedDecisionResponse(normalized.action.optionKey, receivedAt);
  } catch {
    return REJECTED;
  }
}
