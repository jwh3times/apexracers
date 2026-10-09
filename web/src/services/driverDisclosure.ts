import { flushSync } from 'react-dom';

type DisplayEvent = 'pause' | 'revalidate';
export type WithdrawalScope = 'personal' | 'sharing';
export type PendingWithdrawal = { operationId: string; scope: WithdrawalScope };
const InvalidationKey = 'ar_driver_invalidation';

/** Browser lifecycle signals can only discard display and ask the protected endpoint again.
 * Neither another tab nor a local event grants authorization or carries Driver data. */
export function createDriverDisclosure() {
  const listeners = new Set<(event: DisplayEvent) => void>();
  let offline = false;
  let owner: string | null = null;
  let memoryPending: { owner: string; pending: PendingWithdrawal } | undefined;
  let channel: BroadcastChannel | undefined;
  const key = () => `ar_driver_withdrawal_${owner}`;
  const pending = (): PendingWithdrawal | null | 'uncertain' => {
    if (!owner) return null;
    if (memoryPending?.owner === owner) return memoryPending.pending;
    try {
      const value = localStorage.getItem(key());
      if (!value) return null;
      const parsed: unknown = JSON.parse(value);
      if (typeof parsed !== 'object' || parsed === null) return 'uncertain';
      const record = parsed as Record<string, unknown>;
      return typeof record.operationId === 'string' &&
        /^[a-f0-9]{8}(-[a-f0-9]{4}){3}-[a-f0-9]{12}$/i.test(record.operationId) &&
        (record.scope === 'personal' || record.scope === 'sharing')
        ? { operationId: record.operationId, scope: record.scope }
        : 'uncertain';
    } catch {
      return 'uncertain';
    }
  };
  const connected = () =>
    !offline && navigator.onLine && document.visibilityState !== 'hidden' && pending() === null;
  const emit = (event: DisplayEvent) =>
    flushSync(() => {
      for (const listener of listeners) listener(event);
    });
  const pause = () => emit('pause');
  const revalidate = () => emit(connected() ? 'revalidate' : 'pause');
  const disconnect = () => {
    offline = true;
    pause();
  };
  const reconnect = () => {
    offline = false;
    revalidate();
  };
  const resume = () => {
    offline = !navigator.onLine;
    revalidate();
  };
  const storage = (event: Event) => {
    const changed = (event as StorageEvent).key;
    if (
      changed === null ||
      changed === InvalidationKey ||
      changed?.startsWith('ar_driver_withdrawal_')
    )
      revalidate();
  };
  const broadcast = () => {
    channel?.postMessage('invalidate');
    try {
      localStorage.setItem(InvalidationKey, crypto.randomUUID());
    } catch {
      /* A lost signal cannot grant access. */
    }
  };
  const events: [EventTarget, string, (event: Event) => void][] = [
    [window, 'offline', disconnect],
    [window, 'online', reconnect],
    [window, 'focus', resume],
    [window, 'pagehide', pause],
    [window, 'pageshow', resume],
    [document, 'visibilitychange', resume],
    [window, 'storage', storage],
  ];
  return {
    canRead: connected,
    invalidate() {
      revalidate();
      broadcast();
    },
    setOwner(current: string | null) {
      owner = current;
      revalidate();
      broadcast();
    },
    pendingWithdrawal(): PendingWithdrawal | null {
      const value = pending();
      return value === 'uncertain' ? null : value;
    },
    beginWithdrawal(operationId: string, scope: WithdrawalScope) {
      if (!owner) throw new Error('Sign in before withdrawing Driver consent.');
      memoryPending = { owner, pending: { operationId, scope } };
      pause();
      broadcast();
      localStorage.setItem(key(), JSON.stringify(memoryPending.pending));
      broadcast();
    },
    finishWithdrawal(operationId: string) {
      const value = pending();
      if (!owner || value === null || value === 'uncertain' || value.operationId !== operationId)
        return;
      localStorage.removeItem(key());
      memoryPending = undefined;
      revalidate();
      broadcast();
    },
    subscribe(listener: (event: DisplayEvent) => void) {
      if (!listeners.size) {
        for (const [target, event, handler] of events) target.addEventListener(event, handler);
        if (typeof window.BroadcastChannel !== 'undefined') {
          channel = new window.BroadcastChannel('apexracers-driver-disclosure');
          channel.onmessage = event => {
            if (event.data === 'invalidate') revalidate();
          };
        }
      }
      listeners.add(listener);
      return () => {
        listeners.delete(listener);
        if (!listeners.size) {
          for (const [target, event, handler] of events) target.removeEventListener(event, handler);
          channel?.close();
          channel = undefined;
        }
      };
    },
  };
}

export const driverDisclosure = createDriverDisclosure();
