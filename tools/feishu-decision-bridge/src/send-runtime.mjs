import { open } from 'node:fs/promises';
import { isAbsolute, join } from 'node:path';

import { parsePrivateConfig } from './config.mjs';

const MAX_HEALTH_BYTES = 16 * 1024;
const PROVIDER_ID_PATTERN = /^[\x21-\x7e]{1,256}$/;
const UNAVAILABLE_HEALTH = Object.freeze({
  status: 'UNAVAILABLE',
  updatedAt: null,
  pid: null,
  pidAlive: false,
});

function isPlainObject(value) {
  if (value === null || typeof value !== 'object' || Array.isArray(value)) {
    return false;
  }
  const prototype = Object.getPrototypeOf(value);
  return prototype === Object.prototype || prototype === null;
}

export class ProviderRejectedError extends Error {
  constructor() {
    super('Feishu provider rejected request');
    this.name = 'ProviderRejectedError';
  }
}

export class ProviderOutcomeUnknownError extends Error {
  constructor() {
    super('Feishu provider outcome unknown');
    this.name = 'ProviderOutcomeUnknownError';
  }
}

function providerMessageId(response) {
  if (!isPlainObject(response)) {
    throw new ProviderOutcomeUnknownError();
  }
  const descriptors = Object.getOwnPropertyDescriptors(response);
  const codeDescriptor = descriptors.code;
  if (codeDescriptor !== undefined) {
    if (!Object.hasOwn(codeDescriptor, 'value') || !Number.isInteger(codeDescriptor.value)) {
      throw new ProviderOutcomeUnknownError();
    }
    if (codeDescriptor.value !== 0) {
      throw new ProviderRejectedError();
    }
  }
  const dataDescriptor = descriptors.data;
  if (!dataDescriptor || !Object.hasOwn(dataDescriptor, 'value') || !isPlainObject(dataDescriptor.value)) {
    throw new ProviderOutcomeUnknownError();
  }
  const messageDescriptor = Object.getOwnPropertyDescriptor(dataDescriptor.value, 'message_id');
  if (!messageDescriptor || !Object.hasOwn(messageDescriptor, 'value')) {
    throw new ProviderOutcomeUnknownError();
  }
  const messageId = typeof messageDescriptor.value === 'string'
    && PROVIDER_ID_PATTERN.test(messageDescriptor.value)
    ? messageDescriptor.value
    : null;
  if (messageId === null) {
    throw new ProviderOutcomeUnknownError();
  }
  return messageId;
}

export async function createLarkTransport(config, options = {}) {
  let parsedConfig;
  try {
    parsedConfig = parsePrivateConfig(config);
  } catch {
    throw new Error('Feishu transport unavailable');
  }

  let Client = options?.Client;
  if (Client === undefined) {
    try {
      const sdk = await import('@larksuiteoapi/node-sdk');
      Client = sdk.Client ?? sdk.default?.Client;
    } catch {
      throw new Error('Feishu transport unavailable');
    }
  }
  if (typeof Client !== 'function') {
    throw new Error('Feishu transport unavailable');
  }

  let client;
  try {
    client = new Client({
      appId: parsedConfig.appId,
      appSecret: parsedConfig.appSecret,
    });
  } catch {
    throw new Error('Feishu transport unavailable');
  }

  return {
    async sendInteractive(request) {
      try {
        const { params, data } = request;
        const response = await client.im.message.create({ params, data });
        const messageId = providerMessageId(response);
        return { messageId };
      } catch (error) {
        if (error instanceof ProviderRejectedError) {
          throw error;
        }
        throw new ProviderOutcomeUnknownError();
      }
    },
  };
}

async function readBoundedUtf8(path) {
  let handle;
  try {
    handle = await open(path, 'r');
    const buffer = Buffer.alloc(MAX_HEALTH_BYTES + 1);
    const { bytesRead } = await handle.read(buffer, 0, buffer.length, 0);
    if (bytesRead > MAX_HEALTH_BYTES) {
      return null;
    }
    return buffer.subarray(0, bytesRead).toString('utf8');
  } finally {
    await handle?.close().catch(() => {});
  }
}

function defaultProbePid(pid) {
  process.kill(pid, 0);
  return true;
}

export async function readHealthSnapshot(stateRoot, now = new Date(), options = {}) {
  try {
    if (
      typeof stateRoot !== 'string'
      || !isAbsolute(stateRoot)
      || !(now instanceof Date)
      || !Number.isFinite(now.getTime())
    ) {
      return { ...UNAVAILABLE_HEALTH };
    }
    const raw = await readBoundedUtf8(join(stateRoot, 'health.json'));
    if (raw === null) {
      return { ...UNAVAILABLE_HEALTH };
    }
    const snapshot = JSON.parse(raw.replace(/^\ufeff/, ''));
    if (
      !isPlainObject(snapshot)
      || typeof snapshot.status !== 'string'
      || typeof snapshot.updatedAt !== 'string'
      || !Number.isFinite(Date.parse(snapshot.updatedAt))
      || !Number.isInteger(snapshot.pid)
      || snapshot.pid <= 0
    ) {
      return { ...UNAVAILABLE_HEALTH };
    }

    const probePid = options?.probePid ?? defaultProbePid;
    let pidAlive = false;
    try {
      pidAlive = (await probePid(snapshot.pid)) === true;
    } catch {
      pidAlive = false;
    }
    return {
      status: snapshot.status,
      updatedAt: snapshot.updatedAt,
      pid: snapshot.pid,
      pidAlive,
    };
  } catch {
    return { ...UNAVAILABLE_HEALTH };
  }
}
