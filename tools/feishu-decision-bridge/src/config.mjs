import { createHash } from 'node:crypto';
import { posix, win32 } from 'node:path';

const CONFIG_KEYS = new Set([
  'schemaVersion',
  'appId',
  'appSecret',
  'recipient',
  'expectedTenantKey',
  'pairedOperatorOpenIdHash',
  'hmacKey',
  'stateRoot',
]);
const RECIPIENT_KEYS = new Set(['type', 'value']);
const UNSAFE_GENERAL_CATEGORY_PATTERN = /[\p{Cc}\p{Cf}\p{Cs}\p{Zl}\p{Zp}]/u;
const DEFAULT_IGNORABLE_PATTERN = /\p{Default_Ignorable_Code_Point}/u;
const ALLOWED_DEFAULT_IGNORABLE = new Set(['\u200c', '\u200d', '\ufe0e', '\ufe0f']);
const SAFE_REPLACEMENT_CHARACTER = ' ';

function isPlainObject(value) {
  if (value === null || typeof value !== 'object' || Array.isArray(value)) {
    return false;
  }
  const prototype = Object.getPrototypeOf(value);
  return prototype === Object.prototype || prototype === null;
}

function readExactDataObject(value, allowedKeys) {
  if (!isPlainObject(value)) {
    return null;
  }
  const keys = Reflect.ownKeys(value);
  if (
    keys.length !== allowedKeys.size
    || keys.some((key) => typeof key !== 'string' || !allowedKeys.has(key))
  ) {
    return null;
  }
  const descriptors = Object.getOwnPropertyDescriptors(value);
  const result = Object.create(null);
  for (const key of allowedKeys) {
    const descriptor = descriptors[key];
    if (!descriptor || !Object.hasOwn(descriptor, 'value') || !descriptor.enumerable) {
      return null;
    }
    result[key] = descriptor.value;
  }
  return result;
}

function isNonEmptyString(value) {
  return typeof value === 'string' && value.trim().length > 0;
}

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

function deepFreeze(value) {
  if (value !== null && typeof value === 'object' && !Object.isFrozen(value)) {
    for (const child of Object.values(value)) {
      deepFreeze(child);
    }
    Object.freeze(value);
  }
  return value;
}

export function sha256(value) {
  if (typeof value !== 'string') {
    throw new TypeError('Invalid input');
  }
  return createHash('sha256').update(value, 'utf8').digest('hex');
}

function isRejectedDisplayCharacter(character) {
  if (ALLOWED_DEFAULT_IGNORABLE.has(character)) {
    return false;
  }
  return UNSAFE_GENERAL_CATEGORY_PATTERN.test(character)
    || DEFAULT_IGNORABLE_PATTERN.test(character);
}

function isIgnoredSecretMatchCharacter(character) {
  return isRejectedDisplayCharacter(character)
    || DEFAULT_IGNORABLE_PATTERN.test(character);
}

export function normalizeSafeSingleLine(value) {
  if (typeof value !== 'string') {
    throw new TypeError('Invalid input');
  }
  let normalized = '';
  for (const character of value) {
    normalized += isRejectedDisplayCharacter(character)
      ? SAFE_REPLACEMENT_CHARACTER
      : character;
  }
  return normalized;
}

export function isSafeSingleLine(value) {
  return typeof value === 'string'
    && [...value].every((character) => !isRejectedDisplayCharacter(character));
}

function normalizeSecretCandidate(value) {
  let normalized = '';
  for (const character of value) {
    if (!isIgnoredSecretMatchCharacter(character)) {
      normalized += character;
    }
  }
  return normalized;
}

function redactNormalizedMessage(message, literals) {
  const messageCharacters = [...message].map((character) => ({
    match: isIgnoredSecretMatchCharacter(character) ? null : character,
    output: isRejectedDisplayCharacter(character)
      ? SAFE_REPLACEMENT_CHARACTER
      : character,
  }));
  const literalCharacters = literals.map((literal) => [...literal]);
  let result = '';
  let index = 0;

  while (index < messageCharacters.length) {
    let matchedEnd = null;
    for (const literal of literalCharacters) {
      let messageIndex = index;
      let literalIndex = 0;
      while (messageIndex < messageCharacters.length && literalIndex < literal.length) {
        const character = messageCharacters[messageIndex].match;
        if (character === null) {
          messageIndex += 1;
          continue;
        }
        if (character !== literal[literalIndex]) {
          break;
        }
        messageIndex += 1;
        literalIndex += 1;
      }
      if (literalIndex === literal.length) {
        matchedEnd = messageIndex;
        break;
      }
    }

    if (matchedEnd !== null) {
      result += '[REDACTED]';
      index = matchedEnd;
    } else {
      result += messageCharacters[index].output;
      index += 1;
    }
  }
  return result;
}

export function parsePrivateConfig(input) {
  try {
    const fields = readExactDataObject(input, CONFIG_KEYS);
    if (fields === null) {
      throw new Error();
    }
    const recipient = readExactDataObject(fields.recipient, RECIPIENT_KEYS);
    if (
      fields.schemaVersion !== 1
      || !isNonEmptyString(fields.appId)
      || !isNonEmptyString(fields.appSecret)
      || recipient === null
      || !['email', 'open_id'].includes(recipient.type)
      || !isNonEmptyString(recipient.value)
      || !(fields.expectedTenantKey === null || isNonEmptyString(fields.expectedTenantKey))
      || !(fields.pairedOperatorOpenIdHash === null
        || (typeof fields.pairedOperatorOpenIdHash === 'string'
          && /^[0-9a-f]{64}$/.test(fields.pairedOperatorOpenIdHash)))
      || decodeHmacKey(fields.hmacKey) === null
      || !isNonEmptyString(fields.stateRoot)
      || !(win32.isAbsolute(fields.stateRoot) || posix.isAbsolute(fields.stateRoot))
    ) {
      throw new Error();
    }

    return deepFreeze({
      schemaVersion: 1,
      appId: fields.appId,
      appSecret: fields.appSecret,
      recipient: {
        type: recipient.type,
        value: recipient.value,
      },
      expectedTenantKey: fields.expectedTenantKey,
      pairedOperatorOpenIdHash: fields.pairedOperatorOpenIdHash,
      hmacKey: fields.hmacKey,
      stateRoot: fields.stateRoot,
    });
  } catch {
    throw new Error('Invalid private config');
  }
}

export function sanitizeError(error, sensitiveValues = []) {
  if (!(error instanceof Error)) {
    return 'Unknown error';
  }

  let message;
  try {
    message = error.message;
  } catch {
    return 'Unknown error';
  }
  if (typeof message !== 'string' || message.trim().length === 0) {
    return 'Unknown error';
  }

  let sanitized = message;
  if (Array.isArray(sensitiveValues)) {
    const literalValues = [...new Set(
      sensitiveValues
        .filter((value) => typeof value === 'string')
        .map((value) => normalizeSecretCandidate(value))
        .filter((value) => value.length > 0),
    )].sort((left, right) => right.length - left.length);
    sanitized = redactNormalizedMessage(message, literalValues);
  } else {
    sanitized = normalizeSafeSingleLine(message);
  }

  sanitized = normalizeSafeSingleLine(sanitized)
    .replace(/[\w.%+-]+@[\w.-]+\.[A-Za-z]{2,}/g, '[REDACTED]')
    .replace(/\bou_[A-Za-z0-9_-]+\b/g, '[REDACTED]')
    .trim();
  sanitized = normalizeSafeSingleLine(sanitized).trim();
  if (sanitized.length === 0 || !isSafeSingleLine(sanitized)) {
    return 'Unknown error';
  }
  return sanitized;
}
