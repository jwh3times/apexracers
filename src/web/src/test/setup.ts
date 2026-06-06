import '@testing-library/jest-dom';

// Node 26 exposes globalThis.localStorage as undefined when --localstorage-file is absent.
// jsdom's window.localStorage may also be absent with an opaque (about:blank) origin.
// Provide a reliable in-memory implementation so all tests can use localStorage freely.
const _localStore: Record<string, string> = {};
Object.defineProperty(globalThis, 'localStorage', {
  configurable: true,
  writable: true,
  value: {
    getItem: (k: string): string | null =>
      Object.prototype.hasOwnProperty.call(_localStore, k) ? _localStore[k] : null,
    setItem: (k: string, v: string) => {
      _localStore[k] = String(v);
    },
    removeItem: (k: string) => {
      delete _localStore[k];
    },
    clear: () => {
      for (const k of Object.keys(_localStore)) delete _localStore[k];
    },
    key: (i: number): string | null => Object.keys(_localStore)[i] ?? null,
    get length() {
      return Object.keys(_localStore).length;
    },
  },
});

globalThis.ResizeObserver = class ResizeObserver {
  private cb: ResizeObserverCallback;
  constructor(cb: ResizeObserverCallback) {
    this.cb = cb;
  }
  observe(_target: Element) {
    this.cb([{ contentRect: { width: 300 } } as ResizeObserverEntry], this);
  }
  unobserve() {}
  disconnect() {}
};
