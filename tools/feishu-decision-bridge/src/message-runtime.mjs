import { parsePrivateConfig } from './config.mjs';

const PROVIDER_ID_PATTERN = /^[\x21-\x7e]{1,256}$/;
const SILENT_LOGGER = Object.freeze({
  error() {},
  warn() {},
  info() {},
  debug() {},
  trace() {},
});

function safeReplyText(value) {
  return typeof value === 'string'
    && value.length > 0
    && Buffer.byteLength(value, 'utf8') <= 16 * 1024
    && !/[\p{Cc}\p{Cf}\p{Cs}\p{Zl}\p{Zp}]/u.test(value.replace(/[\n\t]/g, ''));
}

export async function createMessageReplyTransport(config, options = {}) {
  let parsedConfig;
  try {
    parsedConfig = parsePrivateConfig(config);
  } catch {
    throw new Error('Feishu message reply unavailable');
  }
  let Client = options?.Client;
  if (Client === undefined) {
    try {
      const sdk = await import('@larksuiteoapi/node-sdk');
      Client = sdk.Client ?? sdk.default?.Client;
    } catch {
      throw new Error('Feishu message reply unavailable');
    }
  }
  if (typeof Client !== 'function') {
    throw new Error('Feishu message reply unavailable');
  }
  let client;
  try {
    client = new Client({
      appId: parsedConfig.appId,
      appSecret: parsedConfig.appSecret,
      loggerLevel: 1,
      logger: SILENT_LOGGER,
    });
  } catch {
    throw new Error('Feishu message reply unavailable');
  }
  return async (messageId, text) => {
    if (
      typeof messageId !== 'string'
      || !PROVIDER_ID_PATTERN.test(messageId)
      || !safeReplyText(text)
    ) {
      throw new Error('Feishu message reply unavailable');
    }
    try {
      const response = await client.im.message.reply({
        path: { message_id: messageId },
        data: { msg_type: 'text', content: JSON.stringify({ text }) },
      });
      if (response?.code !== undefined && response.code !== 0) {
        throw new Error();
      }
    } catch {
      throw new Error('Feishu message reply unavailable');
    }
  };
}
