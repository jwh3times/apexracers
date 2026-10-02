import { IRacingNotLinkedError } from '../services/api';

export type Resource<T> =
  | { status: 'loading' }
  | { status: 'ok'; data: T }
  | { status: 'not-linked' }
  | { status: 'error'; message: string };

export type ResourceOptions<T> = {
  enabled?: boolean;
  fallbackMessage?: string;
  onNotLinked?: { fallback: T };
  onError?: { fallback: T };
};

/** Shared read lifecycle. Cancellation suppresses settlement even when transport ignores abort. */
export function createResourceRequest<T>() {
  let cancelCurrent = () => {};

  return {
    cancel() {
      cancelCurrent();
    },
    load(
      fetcher: (signal: AbortSignal) => Promise<T>,
      publish: (resource: Resource<T>) => void,
      options: ResourceOptions<T> = {}
    ) {
      cancelCurrent();
      let active = true;
      let settled = false;
      const controller = new AbortController();
      cancelCurrent = () => {
        active = false;
        controller.abort();
      };
      queueMicrotask(() => {
        if (active && !settled) publish({ status: 'loading' });
      });
      if (options.enabled === false) return;

      void (async () => {
        try {
          const data = await fetcher(controller.signal);
          settled = true;
          if (active) publish({ status: 'ok', data });
        } catch (error: unknown) {
          settled = true;
          if (!active) return;
          if (error instanceof IRacingNotLinkedError) {
            publish(
              options.onNotLinked
                ? { status: 'ok', data: options.onNotLinked.fallback }
                : { status: 'not-linked' }
            );
          } else if (options.onError) {
            publish({ status: 'ok', data: options.onError.fallback });
          } else {
            publish({
              status: 'error',
              message:
                error instanceof Error && error.message
                  ? error.message
                  : (options.fallbackMessage ?? 'Something went wrong. Please try again.'),
            });
          }
        }
      })();
    },
  };
}
