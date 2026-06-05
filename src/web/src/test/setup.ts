import '@testing-library/jest-dom';

global.ResizeObserver = class ResizeObserver {
  private cb: ResizeObserverCallback;
  constructor(cb: ResizeObserverCallback) { this.cb = cb; }
  observe(target: Element) {
    this.cb([{ contentRect: { width: 300 } } as ResizeObserverEntry], this);
  }
  unobserve() {}
  disconnect() {}
};
