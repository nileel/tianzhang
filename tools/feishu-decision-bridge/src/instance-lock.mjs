import * as nodeFs from 'node:fs/promises';
import { join } from 'node:path';

const MAX_LOCK_BYTES = 128;

function validPid(value) {
  return Number.isSafeInteger(value) && value > 0;
}

function parseLock(value) {
  try {
    const parsed = JSON.parse(value);
    if (
      parsed === null
      || typeof parsed !== 'object'
      || Array.isArray(parsed)
      || Object.getPrototypeOf(parsed) !== Object.prototype
      || Object.keys(parsed).length !== 2
      || parsed.schemaVersion !== 1
      || !validPid(parsed.pid)
    ) {
      return null;
    }
    return { schemaVersion: 1, pid: parsed.pid };
  } catch {
    return null;
  }
}

async function readBoundedLock(path, fs) {
  const handle = await fs.open(path, 'r');
  try {
    const buffer = Buffer.alloc(MAX_LOCK_BYTES + 1);
    const { bytesRead } = await handle.read(buffer, 0, buffer.length, 0);
    if (bytesRead > MAX_LOCK_BYTES) {
      return null;
    }
    return parseLock(buffer.subarray(0, bytesRead).toString('utf8'));
  } finally {
    await handle.close();
  }
}

async function defaultProcessProbe(pid) {
  try {
    process.kill(pid, 0);
    return true;
  } catch (error) {
    return error?.code === 'EPERM';
  }
}

export async function acquireInstanceLock({
  stateRoot,
  pid = process.pid,
  processProbe = defaultProcessProbe,
  fs = nodeFs,
} = {}) {
  if (typeof stateRoot !== 'string' || stateRoot.length === 0 || !validPid(pid)) {
    throw new Error('Bridge lock unavailable');
  }
  if (typeof processProbe !== 'function' || typeof fs?.open !== 'function' || typeof fs?.unlink !== 'function') {
    throw new Error('Bridge lock unavailable');
  }

  const lockPath = join(stateRoot, 'bridge-instance.lock');
  const serialized = JSON.stringify({ schemaVersion: 1, pid });
  for (let attempt = 0; attempt < 2; attempt += 1) {
    let handle;
    try {
      handle = await fs.open(lockPath, 'wx', 0o600);
      await handle.writeFile(serialized, 'utf8');
      await handle.sync();
      await handle.close();
      handle = undefined;
      let released = false;
      return Object.freeze({
        async release() {
          if (released) {
            return;
          }
          released = true;
          try {
            const current = await readBoundedLock(lockPath, fs);
            if (current?.pid === pid) {
              await fs.unlink(lockPath);
            }
          } catch (error) {
            if (error?.code !== 'ENOENT') {
              throw error;
            }
          }
        },
      });
    } catch (error) {
      await handle?.close().catch(() => {});
      if (error?.code !== 'EEXIST') {
        throw new Error('Bridge lock unavailable');
      }
      let existing;
      try {
        existing = await readBoundedLock(lockPath, fs);
      } catch {
        throw new Error('Bridge already running');
      }
      if (existing === null) {
        throw new Error('Bridge already running');
      }
      let live = true;
      try {
        live = await processProbe(existing.pid) !== false;
      } catch {
        live = true;
      }
      if (live) {
        throw new Error('Bridge already running');
      }
      try {
        await fs.unlink(lockPath);
      } catch (unlinkError) {
        if (unlinkError?.code !== 'ENOENT') {
          throw new Error('Bridge already running');
        }
      }
    }
  }
  throw new Error('Bridge already running');
}
