import { IRacingNotLinkedError } from '../services/api';
import type { Resource, ResourceOptions } from './resourceRequest';

type Clock = { now: () => number; monotonic?: () => number };
const CheckInterval = 15_000;
const DisplayLifetime = 30_000;

/** A protected read is also a current authorization check. Only the latest check may publish;
 * its display deadline is measured from its start, independently of response receipt. */
export function createDriverResource<T>(
  clock: Clock = { now: () => Date.now(), monotonic: () => performance.now() }
) {
  const empty: Resource<T> = { status: 'loading' };
  let snapshot: Resource<T> = empty;
  const listeners = new Set<() => void>();
  let generation = 0;
  let controller: AbortController | undefined;
  let poll: ReturnType<typeof setTimeout> | undefined;
  let expiry: ReturnType<typeof setTimeout> | undefined;
  let deadline = 0;
  let monotonicDeadline = 0;
  let saved:
    { fetcher: (signal: AbortSignal) => Promise<T>; options: ResourceOptions<T> } | undefined;
  const monotonic = clock.monotonic ?? clock.now;
  const expired = () => clock.now() >= deadline || monotonic() >= monotonicDeadline;
  const publish = (value: Resource<T>) => {
    snapshot = value;
    for (const listener of listeners) listener();
  };
  const pause = () => {
    generation++;
    controller?.abort();
    clearTimeout(poll);
    clearTimeout(expiry);
    publish(empty);
  };

  const resource = {
    getSnapshot: (): Resource<T> => {
      if (snapshot.status === 'ok' && expired()) snapshot = empty;
      return snapshot;
    },
    subscribe(this: void, listener: () => void) {
      listeners.add(listener);
      return () => {
        listeners.delete(listener);
      };
    },
    pause,
    cancel() {
      pause();
      saved = undefined;
    },
    revalidate() {
      if (saved) resource.load(saved.fetcher, saved.options);
    },
    load(
      fetcher: (signal: AbortSignal) => Promise<T>,
      options: ResourceOptions<T> = {},
      suspended = false
    ) {
      pause();
      saved = { fetcher, options };
      if (options.enabled === false || suspended) return;
      const check = () => {
        const current = ++generation;
        controller?.abort();
        controller = new AbortController();
        const started = clock.now();
        const monotonicStarted = monotonic();
        poll = setTimeout(check, CheckInterval);
        void (async () => {
          try {
            const data = await fetcher(controller.signal);
            if (current !== generation) return;
            if (
              clock.now() >= started + DisplayLifetime ||
              monotonic() >= monotonicStarted + DisplayLifetime
            ) {
              publish(empty);
              return;
            }
            deadline = started + DisplayLifetime;
            monotonicDeadline = monotonicStarted + DisplayLifetime;
            clearTimeout(expiry);
            expiry = setTimeout(
              () => {
                if (expired()) publish(empty);
              },
              Math.max(0, Math.min(deadline - clock.now(), monotonicDeadline - monotonic()))
            );
            publish({ status: 'ok', data });
          } catch (error: unknown) {
            if (current !== generation) return;
            clearTimeout(expiry);
            if (error instanceof IRacingNotLinkedError) {
              publish({ status: 'not-linked' });
            } else {
              publish({
                status: 'error',
                message:
                  error instanceof Error && error.message
                    ? error.message
                    : (options.fallbackMessage ?? 'Driver data is unavailable. Please try again.'),
              });
            }
          }
        })();
      };
      check();
    },
  };
  return resource;
}
