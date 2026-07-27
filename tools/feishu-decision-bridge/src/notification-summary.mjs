import { open } from 'node:fs/promises';
import { homedir as systemHomedir } from 'node:os';
import { isAbsolute, join, resolve } from 'node:path';
import { pathToFileURL } from 'node:url';

import { parsePrivateConfig } from './config.mjs';
import { summarizeNotificationOutcomes } from './notification-audit.mjs';

const MAX_JSON_BYTES = 64 * 1024;

async function readBoundedJson(path) {
  let handle;
  try {
    handle = await open(path, 'r');
    const buffer = Buffer.alloc(MAX_JSON_BYTES + 1);
    const { bytesRead } = await handle.read(buffer, 0, buffer.length, 0);
    if (bytesRead > MAX_JSON_BYTES) {
      throw new Error('Invalid input');
    }
    return JSON.parse(buffer.subarray(0, bytesRead).toString('utf8').replace(/^\ufeff/u, ''));
  } finally {
    await handle?.close().catch(() => {});
  }
}

function parseIso(value) {
  const milliseconds = Date.parse(value);
  if (!Number.isFinite(milliseconds)) {
    throw new Error('Invalid input');
  }
  const date = new Date(milliseconds);
  if (date.toISOString() !== value) {
    throw new Error('Invalid input');
  }
  return date;
}

export async function main(argv = process.argv.slice(2), dependencies = {}) {
  const stdout = dependencies.stdout ?? process.stdout;
  const env = dependencies.env ?? process.env;
  const getHomedir = dependencies.homedir ?? systemHomedir;
  const summarize = dependencies.summarize ?? summarizeNotificationOutcomes;
  try {
    if (
      !Array.isArray(argv)
      || argv.length !== 4
      || argv[0] !== '--since'
      || argv[2] !== '--until'
    ) {
      throw new Error('Invalid input');
    }
    const configured = env.FEISHU_DECISION_CONFIG_PATH;
    const path = configured === undefined
      ? join(getHomedir(), '.codex', 'automation-state', 'tzg-hourly-controller.feishu.private.json')
      : configured;
    if (typeof path !== 'string' || !isAbsolute(path)) {
      throw new Error('Invalid input');
    }
    const config = parsePrivateConfig(await readBoundedJson(path));
    const summary = await summarize({
      stateRoot: config.stateRoot,
      since: parseIso(argv[1]),
      until: parseIso(argv[3]),
    });
    stdout.write(`${JSON.stringify(summary)}\n`);
    return 0;
  } catch {
    stdout.write(`${JSON.stringify({ result: 'SOURCE_UNAVAILABLE' })}\n`);
    return 1;
  }
}

const isDirectExecution = process.argv[1] !== undefined
  && import.meta.url === pathToFileURL(resolve(process.argv[1])).href;

if (isDirectExecution) {
  main().then((code) => {
    process.exitCode = code;
  }).catch(() => {
    process.stdout.write(`${JSON.stringify({ result: 'SOURCE_UNAVAILABLE' })}\n`);
    process.exitCode = 1;
  });
}
