import { randomBytes } from 'node:crypto';
import * as nodeFs from 'node:fs/promises';
import { homedir as systemHomedir } from 'node:os';
import { isAbsolute, join, resolve } from 'node:path';
import { pathToFileURL } from 'node:url';

import { handleCardAction, normalizeCardAction } from './callback-core.mjs';
import { parsePrivateConfig, sanitizeError, sha256 } from './config.mjs';
import { acquireInstanceLock } from './instance-lock.mjs';
import { handleDecisionTextMessage, normalizeMessageEvent } from './message-core.mjs';
import { createMessageReplyTransport } from './message-runtime.mjs';

const MAX_JSON_BYTES = 64 * 1024;
const CALLBACK_TIMEOUT_MS = 2_800;
const HEARTBEAT_MS = 60_000;
const GENERIC_RESPONSE = Object.freeze({
  toast: Object.freeze({ type: 'warning', content: '未登记或已过期' }),
});

function isPlainObject(value) {
  if (value === null || typeof value !== 'object' || Array.isArray(value)) {
    return false;
  }
  const prototype = Object.getPrototypeOf(value);
  return prototype === Object.prototype || prototype === null;
}

function readDataProperty(value, key) {
  if (!isPlainObject(value)) {
    return undefined;
  }
  const descriptor = Object.getOwnPropertyDescriptor(value, key);
  return descriptor && Object.hasOwn(descriptor, 'value') ? descriptor.value : undefined;
}

function summarizeKeys(value) {
  if (!isPlainObject(value)) {
    return 'not_object';
  }
  return Reflect.ownKeys(value)
    .map((key) => (
      typeof key === 'string' && /^[A-Za-z][A-Za-z0-9_]{0,63}$/.test(key)
        ? key
        : '[unsafe]'
    ))
    .sort()
    .join(',');
}

function summarizeCardShape(rawEvent) {
  try {
    const nestedHeader = readDataProperty(rawEvent, 'header');
    const nestedEvent = readDataProperty(rawEvent, 'event');
    const header = nestedHeader ?? rawEvent;
    const event = nestedEvent ?? rawEvent;
    const operator = readDataProperty(event, 'operator');
    const action = readDataProperty(event, 'action');
    const context = readDataProperty(event, 'context');
    const actionValue = readDataProperty(action, 'value');
    const valueKeyCount = isPlainObject(actionValue) ? Reflect.ownKeys(actionValue).length : -1;
    return [
      `callback_shape:root=${summarizeKeys(rawEvent)}`,
      `header=${summarizeKeys(header)}`,
      `event=${summarizeKeys(event)}`,
      `operator=${summarizeKeys(operator)}`,
      `action=${summarizeKeys(action)}`,
      `context=${summarizeKeys(context)}`,
      `value_key_count=${valueKeyCount}`,
    ].join(';');
  } catch {
    return 'callback_shape:unavailable';
  }
}

async function readBoundedJson(path, fs) {
  let handle;
  try {
    handle = await fs.open(path, 'r');
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

function flattenLogValue(value, output, seen) {
  if (typeof value === 'string') {
    output.push(value);
    return;
  }
  if (typeof value === 'number' || typeof value === 'boolean' || typeof value === 'bigint') {
    output.push(String(value));
    return;
  }
  if (value instanceof Error) {
    try {
      if (typeof value.message === 'string') {
        output.push(value.message);
      }
    } catch {
      output.push('Unknown error');
    }
    return;
  }
  if (value === null || typeof value !== 'object' || seen.has(value)) {
    return;
  }
  seen.add(value);
  try {
    const descriptors = Object.getOwnPropertyDescriptors(value);
    for (const descriptor of Object.values(descriptors)) {
      if (Object.hasOwn(descriptor, 'value')) {
        flattenLogValue(descriptor.value, output, seen);
      }
    }
  } finally {
    seen.delete(value);
  }
}

function flattenLogArgs(args) {
  const output = [];
  for (const value of args) {
    flattenLogValue(value, output, new WeakSet());
  }
  return output.join(' ');
}

async function writeHealthAtomic({ fs, stateRoot, status, pid, updatedAt, appIdHash }) {
  const health = {
    schemaVersion: 1,
    status,
    pid,
    updatedAt,
    appIdHash,
  };
  const targetPath = join(stateRoot, 'health.json');
  const temporaryPath = `${targetPath}.${pid}.${randomBytes(12).toString('hex')}.tmp`;
  let handle;
  await fs.mkdir(stateRoot, { recursive: true });
  try {
    handle = await fs.open(temporaryPath, 'wx', 0o600);
    await handle.writeFile(JSON.stringify(health), 'utf8');
    await handle.sync();
    await handle.close();
    handle = null;
    await fs.rename(temporaryPath, targetPath);
  } finally {
    await handle?.close().catch(() => {});
    await fs.unlink(temporaryPath).catch(() => {});
  }
}

function resolveConfigPath(env, homedir) {
  const configured = env.FEISHU_DECISION_CONFIG_PATH;
  const path = configured === undefined
    ? join(homedir(), '.codex', 'automation-state', 'tzg-hourly-controller.feishu.private.json')
    : configured;
  if (typeof path !== 'string' || !isAbsolute(path)) {
    throw new Error();
  }
  return path;
}

export function makeCallback({
  loadConfig,
  loadBindings,
  fs,
  now,
  timers,
  rememberSensitive,
  reportRejection,
  normalizeAction = normalizeCardAction,
  handleAction = handleCardAction,
}) {
  return async (event) => {
    let actionKind;
    let config;
    try {
      const normalized = normalizeAction(event);
      actionKind = normalized.action.kind;
      rememberSensitive([
        normalized.eventId,
        normalized.appId,
        normalized.headerTenantKey,
        normalized.operatorTenantKey,
        normalized.operatorOpenId,
        normalized.messageId,
      ]);
      config = await loadConfig();
      rememberSensitive([
        config.appId,
        config.appSecret,
        config.recipient.value,
        config.expectedTenantKey,
        config.hmacKey,
      ]);
    } catch {
      reportRejection('invalid_shape', event);
      return GENERIC_RESPONSE;
    }
    const bindingPath = join(
      config.stateRoot,
      actionKind === 'operator_pairing' ? 'pairing-binding.json' : 'pending-bindings.json',
    );
    let pendingBindings;
    try {
      const value = loadBindings === undefined
        ? await readBoundedJson(bindingPath, fs)
        : await loadBindings(bindingPath);
      pendingBindings = actionKind === 'operator_pairing' && !Array.isArray(value) ? [value] : value;
    } catch {
      reportRejection('binding_read');
      return GENERIC_RESPONSE;
    }
    const callbackNow = now();
    if (!(callbackNow instanceof Date) || !Number.isFinite(callbackNow.getTime())) {
      reportRejection('invalid_now');
      return GENERIC_RESPONSE;
    }
    let timeoutId;
    const timeout = new Promise((resolve) => {
      timeoutId = timers.setTimeout(() => resolve({
        accepted: false,
        response: GENERIC_RESPONSE,
        rejectionCode: 'timeout',
      }), CALLBACK_TIMEOUT_MS);
    });
    try {
      const result = await Promise.race([
        handleAction({ event, config, pendingBindings, now: callbackNow }),
        timeout,
      ]);
      if (!isPlainObject(result) || result.accepted !== true) {
        const safeCode = typeof result?.rejectionCode === 'string'
          && /^[a-z_]{1,64}$/.test(result.rejectionCode)
          ? result.rejectionCode
          : 'validation';
        reportRejection(safeCode);
      }
      return isPlainObject(result) && isPlainObject(result.response)
        ? result.response
        : GENERIC_RESPONSE;
    } catch {
      reportRejection('callback_error');
      return GENERIC_RESPONSE;
    } finally {
      timers.clearTimeout(timeoutId);
    }
  };
}

function summarizeSdkKeys(value) {
  if (!isPlainObject(value)) {
    return 'not_object';
  }
  const strings = [];
  const symbols = [];
  for (const key of Reflect.ownKeys(value)) {
    if (typeof key === 'string' && /^[A-Za-z][A-Za-z0-9_]{0,63}$/.test(key)) {
      strings.push(key);
    } else if (
      typeof key === 'symbol'
      && typeof key.description === 'string'
      && /^[A-Za-z][A-Za-z0-9_-]{0,63}$/.test(key.description)
    ) {
      symbols.push(`@symbol:${key.description}`);
    } else {
      symbols.push('[unsafe]');
    }
  }
  return [...strings.sort(), ...symbols.sort()].join(',');
}

function diagnosticIdentifier(value) {
  return typeof value === 'string'
    && /^[A-Za-z0-9][A-Za-z0-9._:-]{0,255}$/.test(value);
}

function diagnosticDigitLength(value) {
  return typeof value === 'string' && /^\d+$/.test(value) ? value.length : -1;
}

function summarizeMessageChecks(rawEvent, sender, senderId, message) {
  const eventType = readDataProperty(rawEvent, 'event_type');
  const eventSymbols = isPlainObject(rawEvent)
    ? Reflect.ownKeys(rawEvent).filter((key) => typeof key === 'symbol')
    : [];
  const symbolDescriptor = eventSymbols.length === 1
    ? Object.getOwnPropertyDescriptor(rawEvent, eventSymbols[0])
    : undefined;
  const token = readDataProperty(rawEvent, 'token');
  const tenantKey = readDataProperty(rawEvent, 'tenant_key');
  let content = null;
  try {
    const rawContent = readDataProperty(message, 'content');
    content = typeof rawContent === 'string' && Buffer.byteLength(rawContent, 'utf8') <= 16 * 1024
      ? JSON.parse(rawContent)
      : null;
  } catch {
    content = null;
  }
  return [
    [
      `root_checks=schema:${readDataProperty(rawEvent, 'schema') === '2.0'}`,
      `symbol_match:${eventSymbols.length === 1 && Object.hasOwn(symbolDescriptor ?? {}, 'value') && symbolDescriptor.value === eventType}`,
      `event_type:${eventType === 'im.message.receive_v1'}`,
      `event_id:${diagnosticIdentifier(readDataProperty(rawEvent, 'event_id'))}`,
      `tenant_id:${diagnosticIdentifier(tenantKey)}`,
      `app_id:${diagnosticIdentifier(readDataProperty(rawEvent, 'app_id'))}`,
      `token:${typeof token === 'string' && token.length >= 1 && token.length <= 512 && !/[\p{Cc}\p{Cf}\p{Cs}\p{Zl}\p{Zp}]/u.test(token)}`,
      `create_time_digits:${diagnosticDigitLength(readDataProperty(rawEvent, 'create_time'))}`,
    ].join(','),
    [
      `sender_checks=sender_type:${readDataProperty(sender, 'sender_type') === 'user'}`,
      `tenant_match:${readDataProperty(sender, 'tenant_key') === tenantKey}`,
      `open_id:${diagnosticIdentifier(readDataProperty(senderId, 'open_id'))}`,
    ].join(','),
    [
      `message_checks=chat_type:${readDataProperty(message, 'chat_type') === 'p2p'}`,
      `message_type:${readDataProperty(message, 'message_type') === 'text'}`,
      `message_id:${diagnosticIdentifier(readDataProperty(message, 'message_id'))}`,
      `chat_id:${diagnosticIdentifier(readDataProperty(message, 'chat_id'))}`,
      `create_time_digits:${diagnosticDigitLength(readDataProperty(message, 'create_time'))}`,
      `content_keys:${summarizeSdkKeys(content)}`,
      `text_type:${typeof readDataProperty(content, 'text')}`,
    ].join(','),
  ];
}

function summarizeMessageShape(rawEvent) {
  try {
    const sender = readDataProperty(rawEvent, 'sender');
    const senderId = readDataProperty(sender, 'sender_id');
    const message = readDataProperty(rawEvent, 'message');
    const checks = summarizeMessageChecks(rawEvent, sender, senderId, message);
    return [
      `message_shape:root=${summarizeSdkKeys(rawEvent)}`,
      `sender=${summarizeSdkKeys(sender)}`,
      `sender_id=${summarizeSdkKeys(senderId)}`,
      `message=${summarizeSdkKeys(message)}`,
      ...checks,
    ].join(';');
  } catch {
    return 'message_shape:unavailable';
  }
}

export function makeMessageCallback({
  loadConfig,
  loadBindings,
  fs,
  now,
  replyText,
  rememberSensitive,
  reportRejection,
  normalizeMessage = normalizeMessageEvent,
  handleMessage = handleDecisionTextMessage,
}) {
  return async (event) => {
    const normalized = normalizeMessage(event);
    if (normalized === null) {
      reportRejection('invalid_shape', event);
      return undefined;
    }
    rememberSensitive([
      normalized.eventId,
      normalized.tenantKey,
      normalized.openId,
      normalized.messageId,
      normalized.chatId,
    ]);
    let config;
    let pendingBindings;
    try {
      config = await loadConfig();
      rememberSensitive([
        config.appId,
        config.appSecret,
        config.recipient.value,
        config.expectedTenantKey,
        config.hmacKey,
      ]);
      const bindingPath = join(config.stateRoot, 'pending-bindings.json');
      pendingBindings = loadBindings === undefined
        ? await readBoundedJson(bindingPath, fs)
        : await loadBindings(bindingPath);
    } catch {
      reportRejection('binding_read');
      return undefined;
    }
    const messageNow = now();
    if (!(messageNow instanceof Date) || !Number.isFinite(messageNow.getTime())) {
      reportRejection('invalid_now');
      return undefined;
    }
    const result = await handleMessage({
      event,
      config,
      pendingBindings,
      now: messageNow,
      replyText,
    }).catch(() => ({ accepted: false, rejectionCode: 'callback_error' }));
    if (result.rejectionCode === 'message_reply_failed') {
      reportRejection('message_reply_failed');
    } else if (!result.accepted && result.rejectionCode !== 'format_hint') {
      reportRejection(`validation_${result.rejectionCode}`);
    }
    return undefined;
  };
}

export async function startBridge(options = {}) {
  const env = options.env ?? process.env;
  const homedir = options.homedir ?? systemHomedir;
  const fs = options.fs ?? nodeFs;
  const timers = options.timers ?? {
    setInterval: globalThis.setInterval,
    clearInterval: globalThis.clearInterval,
    setTimeout: globalThis.setTimeout,
    clearTimeout: globalThis.clearTimeout,
  };
  const externalLogger = options.logger ?? console;
  const now = options.now ?? (() => new Date());
  const pid = options.pid ?? process.pid;
  let config;
  let configPath;
  try {
    if (!Number.isInteger(pid) || pid <= 0) {
      throw new Error();
    }
    configPath = resolveConfigPath(env, homedir);
    config = parsePrivateConfig(await readBoundedJson(configPath, fs));
  } catch {
    throw new Error('Bridge unavailable');
  }

  const appIdHash = sha256(config.appId);
  const sensitiveValues = [
    config.appId,
    config.appSecret,
    config.recipient.value,
    config.expectedTenantKey,
    config.hmacKey,
  ];
  const rememberSensitive = (values) => {
    for (const value of values) {
      if (typeof value === 'string' && value.length > 0 && !sensitiveValues.includes(value)) {
        sensitiveValues.push(value);
      }
    }
  };
  let status = 'CONNECTING';
  let intervalId;
  let shuttingDown = false;
  let healthChain = Promise.resolve();
  let instanceLock;

  const healthTimestamp = () => {
    const value = now();
    if (!(value instanceof Date) || !Number.isFinite(value.getTime())) {
      throw new Error();
    }
    return value.toISOString();
  };
  const enqueueHealth = (nextStatus) => {
    status = nextStatus;
    healthChain = healthChain.then(() => writeHealthAtomic({
      fs,
      stateRoot: config.stateRoot,
      status: nextStatus,
      pid,
      updatedAt: healthTimestamp(),
      appIdHash,
    }));
    return healthChain;
  };
  const emitSanitized = (level, args) => {
    const text = flattenLogArgs(args);
    const sanitized = sanitizeError(new Error(text || 'Unknown error'), sensitiveValues);
    const target = typeof externalLogger?.[level] === 'function'
      ? externalLogger[level]
      : externalLogger?.info;
    if (typeof target === 'function') {
      try {
        target.call(externalLogger, sanitized);
      } catch {
        // Logging must never affect bridge state.
      }
    }
  };
  const markConnected = () => {
    if (shuttingDown) {
      return;
    }
    void enqueueHealth('CONNECTED').catch(() => {});
    if (intervalId === undefined) {
      intervalId = timers.setInterval(() => {
        if (status === 'CONNECTED' && !shuttingDown) {
          return enqueueHealth('CONNECTED').catch(() => {});
        }
        return undefined;
      }, HEARTBEAT_MS);
    }
  };
  const sdkLogger = {};
  for (const level of ['trace', 'debug', 'info', 'warn', 'error']) {
    sdkLogger[level] = (...args) => {
      const flattened = flattenLogArgs(args).toLowerCase();
      if (flattened.includes('[ws]') && flattened.includes('ws client ready')) {
        markConnected();
      }
      emitSanitized(level === 'trace' ? 'debug' : level, args);
    };
  }

  const lockFactory = options.acquireInstanceLock ?? acquireInstanceLock;
  try {
    instanceLock = await lockFactory({
      stateRoot: config.stateRoot,
      pid,
      processProbe: options.processProbe,
      fs,
    });
  } catch {
    throw new Error('Bridge unavailable');
  }

  try {
    await enqueueHealth('CONNECTING');
    let EventDispatcher = options.EventDispatcher;
    let WSClient = options.WSClient;
    let MessageClient = options.MessageClient;
    if (EventDispatcher === undefined || WSClient === undefined) {
      const sdk = await import('@larksuiteoapi/node-sdk');
      EventDispatcher ??= sdk.EventDispatcher ?? sdk.default?.EventDispatcher;
      WSClient ??= sdk.WSClient ?? sdk.default?.WSClient;
      MessageClient ??= sdk.Client ?? sdk.default?.Client;
    }
    if (typeof EventDispatcher !== 'function' || typeof WSClient !== 'function') {
      throw new Error();
    }
    const eventDispatcher = new EventDispatcher({});
    let messageReplyTransport = options.messageReplyTransport;
    if (messageReplyTransport === undefined) {
      messageReplyTransport = typeof MessageClient === 'function'
        ? await createMessageReplyTransport(config, { Client: MessageClient })
        : async () => { throw new Error('Feishu message reply unavailable'); };
    }
    const registered = eventDispatcher.register({
      'card.action.trigger': makeCallback({
        loadConfig: async () => parsePrivateConfig(await readBoundedJson(configPath, fs)),
        fs,
        now,
        timers,
        rememberSensitive,
        reportRejection: (code, event) => {
          emitSanitized('warn', [`callback_rejected:${code}`]);
          if (code === 'invalid_shape') {
            emitSanitized('warn', [summarizeCardShape(event)]);
          }
        },
      }),
      'im.message.receive_v1': makeMessageCallback({
        loadConfig: async () => parsePrivateConfig(await readBoundedJson(configPath, fs)),
        fs,
        now,
        replyText: messageReplyTransport,
        rememberSensitive,
        reportRejection: (code, event) => {
          emitSanitized('warn', [`message_rejected:${code}`]);
          if (code === 'invalid_shape') {
            emitSanitized('warn', [summarizeMessageShape(event)]);
          }
        },
      }),
    });
    const activeDispatcher = registered ?? eventDispatcher;
    const client = new WSClient({
      appId: config.appId,
      appSecret: config.appSecret,
      logger: sdkLogger,
    });
    const markDisconnected = (...args) => {
      if (!shuttingDown) {
        void enqueueHealth('DISCONNECTED').catch(() => {});
      }
      if (args.length > 0) {
        emitSanitized('warn', args);
      }
    };
    if (typeof client.on === 'function') {
      client.on('close', markDisconnected);
      client.on('disconnect', markDisconnected);
      client.on('error', markDisconnected);
    }
    await client.start({ eventDispatcher: activeDispatcher });
    await healthChain;

    return {
      client,
      eventDispatcher: activeDispatcher,
      async flush() {
        await healthChain;
      },
      async shutdown() {
        if (shuttingDown) {
          await healthChain;
          return;
        }
        shuttingDown = true;
        if (intervalId !== undefined) {
          timers.clearInterval(intervalId);
          intervalId = undefined;
        }
        try {
          if (typeof client.stop === 'function') {
            await client.stop();
          } else if (typeof client.close === 'function') {
            await client.close();
          }
        } catch (error) {
          emitSanitized('warn', [error]);
        }
        try {
          await enqueueHealth('DISCONNECTED');
        } finally {
          const ownedLock = instanceLock;
          instanceLock = undefined;
          await ownedLock?.release().catch((error) => emitSanitized('warn', [error]));
        }
      },
    };
  } catch (error) {
    status = 'DISCONNECTED';
    healthChain = healthChain.catch(() => {}).then(() => writeHealthAtomic({
      fs,
      stateRoot: config.stateRoot,
      status: 'DISCONNECTED',
      pid,
      updatedAt: healthTimestamp(),
      appIdHash,
    }));
    await healthChain.catch(() => {});
    const ownedLock = instanceLock;
    instanceLock = undefined;
    await ownedLock?.release().catch(() => {});
    emitSanitized('error', [error]);
    throw new Error('Bridge unavailable');
  }
}

export async function main(dependencies = {}) {
  const start = dependencies.start ?? startBridge;
  const processTarget = dependencies.process ?? process;
  const runtime = await start();
  let shutdownPromise;
  const shutdown = () => {
    shutdownPromise ??= Promise.resolve(runtime.shutdown()).catch(() => {});
    return shutdownPromise;
  };
  if (typeof processTarget.once === 'function') {
    processTarget.once('SIGINT', shutdown);
    processTarget.once('SIGTERM', shutdown);
  }
  return runtime;
}

const directExecution = process.argv[1] !== undefined
  && import.meta.url === pathToFileURL(resolve(process.argv[1])).href;

if (directExecution) {
  main().catch(() => {
    process.stderr.write('Bridge unavailable\n');
    process.exitCode = 1;
  });
}
