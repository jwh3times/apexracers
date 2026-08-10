const DB_NAME = 'apexracers';
const DB_VERSION = 1;
const STORE = 'session';

let _db: IDBDatabase | null = null;

function requestError(request: IDBRequest): Error {
  return request.error ?? new Error('IndexedDB request failed.');
}

function open(): Promise<IDBDatabase> {
  if (_db) return Promise.resolve(_db);
  return new Promise((resolve, reject) => {
    const req = indexedDB.open(DB_NAME, DB_VERSION);
    req.onupgradeneeded = () => req.result.createObjectStore(STORE);
    req.onsuccess = () => {
      _db = req.result;
      resolve(_db);
    };
    req.onerror = () => reject(requestError(req));
  });
}

export async function dbGet<T>(key: string): Promise<T | undefined> {
  const db = await open();
  return new Promise((resolve, reject) => {
    const req = db.transaction(STORE, 'readonly').objectStore(STORE).get(key);
    req.onsuccess = () => resolve(req.result as T | undefined);
    req.onerror = () => reject(requestError(req));
  });
}

export async function dbSet(key: string, value: unknown): Promise<void> {
  const db = await open();
  return new Promise((resolve, reject) => {
    const req = db.transaction(STORE, 'readwrite').objectStore(STORE).put(value, key);
    req.onsuccess = () => resolve();
    req.onerror = () => reject(requestError(req));
  });
}

export async function dbRemove(key: string): Promise<void> {
  const db = await open();
  return new Promise((resolve, reject) => {
    const req = db.transaction(STORE, 'readwrite').objectStore(STORE).delete(key);
    req.onsuccess = () => resolve();
    req.onerror = () => reject(requestError(req));
  });
}
