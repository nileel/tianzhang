import { randomBytes } from 'node:crypto';
import * as nodeFs from 'node:fs/promises';
import { homedir as systemHomedir } from 'node:os';
import { isAbsolute, join, resolve } from 'node:path';
import { pathToFileURL } from 'node:url';

import { handleCardAction, normalizeCardAction } from './callback-core.mjs';
import { parsePrivateConfig, sanitizeError, sha256 } from './config.mjs';

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

function makeCallback({ loadConfig, fs, now, timers, rememberSensitive }) {
  return async (event) => {
    let actionKind;
    let config;
    try {
      const normalized = normalizeCardAction(event);
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
      return GENERIC_RESPONSE;
    }
    const bindingPath = join(
      config.stateRoot,
      actionKind === 'operator_pairing' ? 'pairing-binding.json' : 'pending-bindings.json',
    );
    let pendingBindings;
    try {
      const value = await readBoundedJson(bindingPath, fs);
      pendingBindings = actionKind === 'operator_pairing' && !Array.isArray(value) ? [value] : value;
    } catch {
      return GENERIC_RESPONSE;
    }
    const callbackNow = now();
    if (!(callbackNow instanceof Date) || !Number.isFinite(callbackNow.getTime())) {
      return GENERIC_RESPONSE;
    }
    let timeoutId;
    const timeout = new Promise((resolve) => {
      timeoutId = timers.setTimeout(() => resolve({ accepted: false, response: GENERIC_RESPONSE }), CALLBACK_TIMEOUT_MS);
    });
    try {
      const result = await Promise.race([
        handleCardAction({ event, config, pendingBindings, now: callbackNow }),
        timeout,
      ]);
      return isPlainObject(result) && isPlainObject(result.response)
        ? result.response
        : GENERIC_RESPONSE;
    } catch {
      return GENERIC_RESPONSE;
    } finally {
      timers.clearTimeout(timeoutId);
    }
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

  try {
    await enqueueHealth('CONNECTING');
    let EventDispatcher = options.EventDispatcher;
    let WSClient = options.WSClient;
    if (EventDispatcher === undefined || WSClient === undefined) {
      const sdk = await import('@larksuiteoapi/node-sdk');
      EventDispatcher ??= sdk.EventDispatcher ?? sdk.default?.EventDispatcher;
      WSClient ??= sdk.WSClient ?? sdk.default?.WSClient;
    }
    if (typeof EventDispatcher !== 'function' || typeof WSClient !== 'function') {
      throw new Error();
    }
    const eventDispatcher = new EventDispatcher({});
    const registered = eventDispatcher.register({
      'card.action.trigger': makeCallback({
        loadConfig: async () => parsePrivateConfig(await readBoundedJson(configPath, fs)),
        fs,
        now,
        timers,
        rememberSensitive,
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
        await enqueueHealth('DISCONNECTED');
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
