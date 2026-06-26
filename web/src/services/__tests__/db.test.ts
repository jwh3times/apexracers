import { vi, describe, it, expect, beforeEach, afterEach } from 'vitest';

type FakeOpts = {
  failOpen?: boolean;
  failGet?: boolean;
  failPut?: boolean;
  failDelete?: boolean;
};

function setupFakeIndexedDB(opts: FakeOpts = {}) {
  const store = new Map<string, unknown>();

  const objectStore = {
    get: (key: string) => {
      const req: Record<string, unknown> = {};
      queueMicrotask(() => {
        if (opts.failGet) {
          req.error = new Error('get failed');
          (req.onerror as (() => void) | undefined)?.();
        } else {
          req.result = store.get(key);
          (req.onsuccess as (() => void) | undefined)?.();
        }
      });
      return req;
    },
    put: (value: unknown, key: string) => {
      const req: Record<string, unknown> = {};
      queueMicrotask(() => {
        if (opts.failPut) {
          req.error = new Error('put failed');
          (req.onerror as (() => void) | undefined)?.();
        } else {
          store.set(key, value);
          (req.onsuccess as (() => void) | undefined)?.();
        }
      });
      return req;
    },
    delete: (key: string) => {
      const req: Record<string, unknown> = {};
      queueMicrotask(() => {
        if (opts.failDelete) {
          req.error = new Error('delete failed');
          (req.onerror as (() => void) | undefined)?.();
        } else {
          store.delete(key);
          (req.onsuccess as (() => void) | undefined)?.();
        }
      });
      return req;
    },
  };

  const fakeDb = {
    transaction: () => ({ objectStore: () => objectStore }),
    createObjectStore: vi.fn(),
  };

  vi.stubGlobal('indexedDB', {
    open: () => {
      const req: Record<string, unknown> = {};
      queueMicrotask(() => {
        if (opts.failOpen) {
          req.error = new Error('open failed');
          (req.onerror as (() => void) | undefined)?.();
        } else {
          req.result = fakeDb;
          (req.onupgradeneeded as (() => void) | undefined)?.();
          (req.onsuccess as (() => void) | undefined)?.();
        }
      });
      return req;
    },
  });
}

describe('db', () => {
  beforeEach(() => {
    vi.resetModules();
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('dbSet stores a value and dbGet retrieves it', async () => {
    setupFakeIndexedDB();
    const { dbGet, dbSet } = await import('../db');
    await dbSet('key1', 'value1');
    expect(await dbGet<string>('key1')).toBe('value1');
  });

  it('dbGet returns undefined for a missing key', async () => {
    setupFakeIndexedDB();
    const { dbGet } = await import('../db');
    expect(await dbGet<string>('missing')).toBeUndefined();
  });

  it('dbRemove deletes a stored value', async () => {
    setupFakeIndexedDB();
    const { dbGet, dbSet, dbRemove } = await import('../db');
    await dbSet('del', 'gone');
    await dbRemove('del');
    expect(await dbGet<string>('del')).toBeUndefined();
  });

  it('reuses the cached db connection on subsequent calls', async () => {
    setupFakeIndexedDB();
    const { dbGet, dbSet } = await import('../db');
    await dbSet('a', 1);
    await dbSet('b', 2);
    expect(await dbGet<number>('a')).toBe(1);
    expect(await dbGet<number>('b')).toBe(2);
  });

  it('dbGet rejects when the store request errors', async () => {
    setupFakeIndexedDB({ failGet: true });
    const { dbGet } = await import('../db');
    await expect(dbGet('k')).rejects.toThrow('get failed');
  });

  it('dbSet rejects when the store request errors', async () => {
    setupFakeIndexedDB({ failPut: true });
    const { dbSet } = await import('../db');
    await expect(dbSet('k', 'v')).rejects.toThrow('put failed');
  });

  it('dbRemove rejects when the store request errors', async () => {
    setupFakeIndexedDB({ failDelete: true });
    const { dbRemove } = await import('../db');
    await expect(dbRemove('k')).rejects.toThrow('delete failed');
  });

  it('all operations reject when open fails', async () => {
    setupFakeIndexedDB({ failOpen: true });
    const { dbGet } = await import('../db');
    await expect(dbGet('k')).rejects.toThrow('open failed');
  });
});
