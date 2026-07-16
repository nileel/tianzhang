import { createHmac, timingSafeEqual } from 'node:crypto';

const DANGEROUS_KEYS = new Set(['__proto__', 'prototype', 'constructor']);
const ENVELOPE_KEYS = new Set(['schemaVersion', 'payload', 'signature']);

function decodeHmacKey(value) {
  if (typeof value !== 'string') {
    return null;
  }
  const decoded = Buffer.from(value, 'base64');
  if (decoded.length !== 32 || decoded.toString('base64') !== value) {
    return null;
  }
  return decoded;
}

function normalize(value, ancestors) {
  if (value === null || typeof value === 'string' || typeof value === 'boolean') {
    return value;
  }
  if (typeof value === 'number') {
    if (
      !Number.isFinite(value)
      || (Number.isInteger(value) && !Number.isSafeInteger(value))
    ) {
      throw new Error();
    }
    return Object.is(value, -0) ? 0 : value;
  }
  if (typeof value !== 'object') {
    throw new Error();
  }
  if (ancestors.has(value)) {
    throw new Error();
  }

  ancestors.add(value);
  try {
    if (Array.isArray(value)) {
      if (Object.getPrototypeOf(value) !== Array.prototype) {
        throw new Error();
      }
      const ownKeys = Reflect.ownKeys(value);
      if (
        ownKeys.some((key) => typeof key === 'symbol')
        || ownKeys.some((key) => key !== 'length' && !/^(0|[1-9]\d*)$/.test(key))
        || Object.keys(value).length !== value.length
      ) {
        throw new Error();
      }
      const result = [];
      for (let index = 0; index < value.length; index += 1) {
        if (!Object.hasOwn(value, index)) {
          throw new Error();
        }
        const descriptor = Object.getOwnPropertyDescriptor(value, String(index));
        if (!descriptor || !Object.hasOwn(descriptor, 'value') || !descriptor.enumerable) {
          throw new Error();
        }
        result.push(normalize(descriptor.value, ancestors));
      }
      return result;
    }

    const prototype = Object.getPrototypeOf(value);
    if (prototype !== Object.prototype && prototype !== null) {
      throw new Error();
    }
    const ownKeys = Reflect.ownKeys(value);
    if (ownKeys.some((key) => typeof key === 'symbol')) {
      throw new Error();
    }
    const result = {};
    for (const key of ownKeys.sort()) {
      if (DANGEROUS_KEYS.has(key)) {
        throw new Error();
      }
      const descriptor = Object.getOwnPropertyDescriptor(value, key);
      if (!descriptor || !Object.hasOwn(descriptor, 'value') || !descriptor.enumerable) {
        throw new Error();
      }
      result[key] = normalize(descriptor.value, ancestors);
    }
    return result;
  } finally {
    ancestors.delete(value);
  }
}

function canonicalClone(value) {
  return JSON.parse(canonicalize(value));
}

function deepFreeze(value) {
  if (value !== null && typeof value === 'object' && !Object.isFrozen(value)) {
    for (const child of Object.values(value)) {
      deepFreeze(child);
    }
    Object.freeze(value);
  }
  return value;
}

function computeSignature(payload, key) {
  return createHmac('sha256', key).update(canonicalize(payload), 'utf8').digest('hex');
}

function readEnvelope(value) {
  if (value === null || typeof value !== 'object' || Array.isArray(value)) {
    throw new Error();
  }
  const prototype = Object.getPrototypeOf(value);
  if (prototype !== Object.prototype && prototype !== null) {
    throw new Error();
  }
  const ownKeys = Reflect.ownKeys(value);
  if (
    ownKeys.some((key) => typeof key === 'symbol')
    || ownKeys.length !== ENVELOPE_KEYS.size
    || ownKeys.some((key) => !ENVELOPE_KEYS.has(key))
  ) {
    throw new Error();
  }
  const descriptors = Object.getOwnPropertyDescriptors(value);
  for (const key of ENVELOPE_KEYS) {
    const descriptor = descriptors[key];
    if (!descriptor || !Object.hasOwn(descriptor, 'value') || !descriptor.enumerable) {
      throw new Error();
    }
  }
  return {
    schemaVersion: descriptors.schemaVersion.value,
    payload: descriptors.payload.value,
    signature: descriptors.signature.value,
  };
}

export function canonicalize(value) {
  try {
    return JSON.stringify(normalize(value, new WeakSet()));
  } catch {
    throw new Error('Cannot canonicalize value');
  }
}

export function signEnvelope(payload, encodedKey) {
  try {
    const key = decodeHmacKey(encodedKey);
    if (key === null) {
      throw new Error();
    }
    const clonedPayload = canonicalClone(payload);
    const envelope = {
      schemaVersion: 1,
      payload: deepFreeze(clonedPayload),
      signature: computeSignature(clonedPayload, key),
    };
    return Object.freeze(envelope);
  } catch {
    throw new Error('Envelope operation failed');
  }
}

export function verifyEnvelope(envelope, encodedKey) {
  try {
    const key = decodeHmacKey(encodedKey);
    if (key === null) {
      throw new Error();
    }
    const parsed = readEnvelope(envelope);
    if (
      parsed.schemaVersion !== 1
      || typeof parsed.signature !== 'string'
      || !/^[0-9a-f]{64}$/.test(parsed.signature)
    ) {
      throw new Error();
    }

    const payloadClone = canonicalClone(parsed.payload);
    const expected = Buffer.from(computeSignature(payloadClone, key), 'hex');
    const actual = Buffer.from(parsed.signature, 'hex');
    if (actual.length !== expected.length || !timingSafeEqual(actual, expected)) {
      throw new Error();
    }
    return deepFreeze(payloadClone);
  } catch {
    throw new Error('Envelope verification failed');
  }
}
