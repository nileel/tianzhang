import { mkdir, open } from 'node:fs/promises';
import { join } from 'node:path';

import { parsePrivateConfig, sha256 } from './config.mjs';
import { formatCustomReplyCommand, parseCustomReplyCommand } from './custom-reply.mjs';
import { signEnvelope } from './envelope.mjs';
import { writeSignedInbox } from './inbox.mjs';

const IDENTIFIER_PATTERN = /^[A-Za-z0-9][A-Za-z0-9._:-]{0,255}$/;
const HEX_PATTERN = /^[0-9a-f]{64}$/;
const OPTION_KEYS = ['A', 'B', 'C'];
const SDK_EVENT_SYMBOL_DESCRIPTION = 'event-type';
const SDK_ROOT_KEYS = [
  'schema', 'event_id', 'token', 'create_time', 'event_type', 'tenant_key', 'app_id',
  'sender', 'message',
];
const SDK_MESSAGE_KEYS = [
  'message_id', 'root_id', 'parent_id', 'create_time', 'update_time', 'chat_id',
  'thread_id', 'chat_type', 'message_type', 'content', 'mentions', 'user_agent',
  'lark_agent_context',
];

function isPlainObject(value) {
  if (value === null || typeof value !== 'object' || Array.isArray(value)) {
    return false;
  }
  const prototype = Object.getPrototypeOf(value);
  return prototype === Object.prototype || prototype === null;
}

function exactDataObject(value, keys) {
  if (!isPlainObject(value) || Reflect.ownKeys(value).length !== keys.length) {
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
  if (Reflect.ownKeys(descriptors).some((key) => typeof key !== 'string' || !keys.includes(key))) {
    return null;
  }
  return snapshot;
}

function exactDataArray(value) {
  if (!Array.isArray(value) || Object.getPrototypeOf(value) !== Array.prototype) {
    return null;
  }
  const descriptors = Object.getOwnPropertyDescriptors(value);
  if (descriptors.length?.value !== value.length || Reflect.ownKeys(value).length !== value.length + 1) {
    return null;
  }
  const snapshot = [];
  for (let index = 0; index < value.length; index += 1) {
    const descriptor = descriptors[String(index)];
    if (!descriptor || !Object.hasOwn(descriptor, 'value') || !descriptor.enumerable) {
      return null;
    }
    snapshot.push(descriptor.value);
  }
  return snapshot;
}

function allowedDataObject(value, requiredKeys, allowedKeys) {
  if (!isPlainObject(value)) {
    return null;
  }
  const descriptors = Object.getOwnPropertyDescriptors(value);
  const ownKeys = Reflect.ownKeys(descriptors);
  if (
    ownKeys.some((key) => typeof key !== 'string' || !allowedKeys.includes(key))
    || requiredKeys.some((key) => !Object.hasOwn(descriptors, key))
  ) {
    return null;
  }
  const snapshot = Object.create(null);
  for (const key of ownKeys) {
    const descriptor = descriptors[key];
    if (!Object.hasOwn(descriptor, 'value') || !descriptor.enumerable) {
      return null;
    }
    snapshot[key] = descriptor.value;
  }
  return snapshot;
}

function snapshotSdkRoot(value) {
  if (!isPlainObject(value)) {
    return null;
  }
  const descriptors = Object.getOwnPropertyDescriptors(value);
  const symbolKeys = Reflect.ownKeys(descriptors).filter((key) => typeof key === 'symbol');
  if (symbolKeys.length !== 1 || symbolKeys[0].description !== SDK_EVENT_SYMBOL_DESCRIPTION) {
    return null;
  }
  const symbolDescriptor = descriptors[symbolKeys[0]];
  if (!Object.hasOwn(symbolDescriptor, 'value') || !symbolDescriptor.enumerable) {
    return null;
  }
  const stringValue = Object.create(null);
  for (const [key, descriptor] of Object.entries(descriptors)) {
    if (
      !SDK_ROOT_KEYS.includes(key)
      || !Object.hasOwn(descriptor, 'value')
      || !descriptor.enumerable
    ) {
      return null;
    }
    stringValue[key] = descriptor.value;
  }
  if (SDK_ROOT_KEYS.some((key) => !Object.hasOwn(stringValue, key))) {
    return null;
  }
  return symbolDescriptor.value === stringValue.event_type ? stringValue : null;
}

function identifier(value) {
  return typeof value === 'string' && IDENTIFIER_PATTERN.test(value);
}

function exactIso(value) {
  if (typeof value !== 'string') {
    return null;
  }
  const milliseconds = Date.parse(value);
  return Number.isFinite(milliseconds) && new Date(milliseconds).toISOString() === value
    ? milliseconds
    : null;
}

function snapshotBinding(value) {
  const fields = exactDataObject(value, [
    'kind', 'decisionId', 'allowedOptions', 'allowCustomReply', 'issuedAt', 'expiresAt',
    'cardNonceHash', 'providerMessageIdHash', 'providerChatIdHash',
  ]);
  const options = exactDataArray(fields?.allowedOptions);
  const issuedAtMs = exactIso(fields?.issuedAt);
  const expiresAtMs = exactIso(fields?.expiresAt);
  if (
    fields === null
    || fields.kind !== 'decision_reply'
    || !identifier(fields.decisionId)
    || options === null
    || options.length !== OPTION_KEYS.length
    || options.some((option, index) => option !== OPTION_KEYS[index])
    || typeof fields.allowCustomReply !== 'boolean'
    || issuedAtMs === null
    || expiresAtMs === null
    || issuedAtMs >= expiresAtMs
    || !HEX_PATTERN.test(fields.cardNonceHash)
    || !HEX_PATTERN.test(fields.providerMessageIdHash)
    || !HEX_PATTERN.test(fields.providerChatIdHash)
  ) {
    return null;
  }
  return { ...fields, issuedAtMs, expiresAtMs };
}

function snapshotBindings(value) {
  const source = exactDataArray(value);
  if (source === null || source.length < 1 || source.length > 128) {
    return null;
  }
  const bindings = source.map(snapshotBinding);
  return bindings.some((binding) => binding === null) ? null : bindings;
}

function parseMessageContent(value) {
  if (typeof value !== 'string' || Buffer.byteLength(value, 'utf8') > 16 * 1024) {
    return null;
  }
  try {
    const fields = exactDataObject(JSON.parse(value), ['text']);
    return fields !== null && typeof fields.text === 'string' ? fields.text : null;
  } catch {
    return null;
  }
}

export function normalizeMessageEvent(rawEvent) {
  try {
    const root = snapshotSdkRoot(rawEvent);
    const sender = allowedDataObject(
      root?.sender,
      ['sender_id', 'sender_type', 'tenant_key'],
      ['sender_id', 'sender_type', 'tenant_key'],
    );
    const senderId = allowedDataObject(
      sender?.sender_id,
      ['open_id'],
      ['union_id', 'user_id', 'open_id'],
    );
    const message = allowedDataObject(root?.message, [
      'message_id', 'create_time', 'chat_id', 'chat_type', 'message_type', 'content',
    ], SDK_MESSAGE_KEYS);
    const text = parseMessageContent(message?.content);
    if (
      root === null
      || sender === null
      || senderId === null
      || message === null
      || root.schema !== '2.0'
      || root.event_type !== 'im.message.receive_v1'
      || sender.sender_type !== 'user'
      || sender.tenant_key !== root.tenant_key
      || message.chat_type !== 'p2p'
      || message.message_type !== 'text'
      || !identifier(root.event_id)
      || !identifier(root.tenant_key)
      || !identifier(root.app_id)
      || (
        root.token !== null
        && (
          typeof root.token !== 'string'
          || root.token.length > 512
          || /[\p{Cc}\p{Cf}\p{Cs}\p{Zl}\p{Zp}]/u.test(root.token)
        )
      )
      || typeof root.create_time !== 'string'
      || !/^\d{13}$/.test(root.create_time)
      || !identifier(senderId.open_id)
      || !identifier(message.message_id)
      || !identifier(message.chat_id)
      || typeof message.create_time !== 'string'
      || !/^\d{13}$/.test(message.create_time)
      || !Number.isSafeInteger(Number(message.create_time))
      || !Number.isFinite(new Date(Number(message.create_time)).getTime())
      || text === null
    ) {
      return null;
    }
    return {
      eventId: root.event_id,
      eventType: root.event_type,
      tenantKey: root.tenant_key,
      appId: root.app_id,
      openId: senderId.open_id,
      messageId: message.message_id,
      createdAtMs: Number(message.create_time),
      chatId: message.chat_id,
      text,
    };
  } catch {
    return null;
  }
}

async function claimReply(stateRoot, eventIdHash) {
  const directory = join(stateRoot, 'message-replies');
  await mkdir(directory, { recursive: true });
  let handle;
  try {
    handle = await open(join(directory, `${eventIdHash}.json`), 'wx', 0o600);
    await handle.writeFile('{"schemaVersion":1}', 'utf8');
    await handle.sync();
    return true;
  } catch (error) {
    if (error?.code === 'EEXIST') {
      return false;
    }
    throw error;
  } finally {
    await handle?.close().catch(() => {});
  }
}

async function replyOnce({ stateRoot, eventIdHash, messageId, text, replyText }) {
  try {
    if (!(await claimReply(stateRoot, eventIdHash))) {
      return null;
    }
    await replyText(messageId, text);
    return null;
  } catch {
    return 'message_reply_failed';
  }
}

function rejected(rejectionCode) {
  return { accepted: false, rejectionCode };
}

export async function handleDecisionTextMessage({
  event, config, pendingBindings, now, replyText,
}) {
  let parsedConfig;
  let normalized;
  let bindings;
  try {
    parsedConfig = parsePrivateConfig(config);
    normalized = normalizeMessageEvent(event);
    bindings = snapshotBindings(pendingBindings);
  } catch {
    return rejected('configuration');
  }
  if (normalized === null) {
    return rejected('event_validation');
  }
  if (bindings === null) {
    return rejected('binding_shape');
  }
  if (!(now instanceof Date) || !Number.isFinite(now.getTime()) || typeof replyText !== 'function') {
    return rejected('runtime');
  }
  if (parsedConfig.expectedTenantKey === null || parsedConfig.pairedOperatorOpenIdHash === null) {
    return rejected('unpaired');
  }
  if (normalized.appId !== parsedConfig.appId) {
    return rejected('app_identity');
  }
  if (normalized.tenantKey !== parsedConfig.expectedTenantKey) {
    return rejected('tenant_identity');
  }
  if (sha256(normalized.openId) !== parsedConfig.pairedOperatorOpenIdHash) {
    return rejected('operator_identity');
  }
  if (normalized.createdAtMs > now.getTime()) {
    return rejected('event_time');
  }

  const providerChatIdHash = sha256(normalized.chatId);
  const chatBindings = bindings.filter((candidate) => (
    candidate.providerChatIdHash === providerChatIdHash
  ));
  if (chatBindings.length === 0) {
    return rejected('chat_binding');
  }
  const customBindings = chatBindings.filter((candidate) => candidate.allowCustomReply);
  if (customBindings.length === 0) {
    return rejected('custom_disabled');
  }
  const binding = customBindings.find((candidate) => (
    normalized.createdAtMs >= candidate.issuedAtMs
    && normalized.createdAtMs <= candidate.expiresAtMs
    && now.getTime() <= candidate.expiresAtMs
  ));
  if (binding === undefined) {
    return rejected(customBindings.some((candidate) => normalized.createdAtMs < candidate.issuedAtMs)
      ? 'binding_time'
      : 'binding_expired');
  }

  const providerEventIdHash = sha256(normalized.eventId);
  const command = parseCustomReplyCommand(normalized.text);
  if (command === null) {
    const replyFailure = await replyOnce({
      stateRoot: parsedConfig.stateRoot,
      eventIdHash: providerEventIdHash,
      messageId: normalized.messageId,
      text: `请按以下格式回复：\n${formatCustomReplyCommand(binding.decisionId)}`,
      replyText,
    });
    return rejected(replyFailure ?? 'format_hint');
  }
  if (command.decisionId !== binding.decisionId) {
    return rejected('decision_mismatch');
  }

  const payload = {
    kind: 'decision_custom_reply',
    decisionId: command.decisionId,
    customText: command.customText,
    providerMessageIdHash: binding.providerMessageIdHash,
    providerEventIdHash,
    operatorOpenIdHash: sha256(normalized.openId),
    tenantKeyHash: sha256(normalized.tenantKey),
    providerChatIdHash,
    receivedAt: now.toISOString(),
    source: 'feishu_text',
  };
  try {
    await writeSignedInbox({
      stateRoot: parsedConfig.stateRoot,
      envelope: signEnvelope(payload, parsedConfig.hmacKey),
      eventIdHash: providerEventIdHash,
    });
  } catch {
    return rejected('inbox_write_failed');
  }

  const replyFailure = await replyOnce({
    stateRoot: parsedConfig.stateRoot,
    eventIdHash: providerEventIdHash,
    messageId: normalized.messageId,
    text: `已登记 ${command.decisionId} 自定义方案：\n${command.customText}`,
    replyText,
  });
  return { accepted: true, rejectionCode: replyFailure };
}
