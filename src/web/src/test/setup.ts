import '@testing-library/jest-dom';

globalThis.ResizeObserver = class ResizeObserver {
  private cb: ResizeObserverCallback;
  constructor(cb: ResizeObserverCallback) { this.cb = cb; }
  observe(_target: Element) {
    this.cb([{ contentRect: { width: 300 } } as ResizeObserverEntry], this);
  }
  unobserve() {}
  disconnect() {}
};
