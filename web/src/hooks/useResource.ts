import { useEffect, useState } from 'react';
import { IRacingNotLinkedError } from '../services/api';

export type Resource<T> =
  | { status: 'loading' }
  | { status: 'ok'; data: T }
  | { status: 'not-linked' }
  | { status: 'error'; message: string };

type ResourceFallback<T> = { fallback: T };

type ResourceOptions<T> = {
  enabled?: boolean;
  fallbackMessage?: string;
  onNotLinked?: ResourceFallback<T>;
  onError?: ResourceFallback<T>;
};

const DEFAULT_ERROR = 'Something went wrong. Please try again.';

function errorMessage(error: unknown, fallback: string): string {
  return error instanceof Error && error.message ? error.message : fallback;
}

/**
 * Owns the lifecycle shared by read-only page resources: loading, stale-request
 * suppression, abort signalling, typed not-linked classification, and errors.
 * Every value captured by fetcher must also appear in dependencies.
 */
export function useResource<T>(
  fetcher: (signal: AbortSignal) => Promise<T>,
  dependencies: readonly unknown[],
  options: ResourceOptions<T> = {}
): Resource<T> {
  const [resource, setResource] = useState<Resource<T>>({ status: 'loading' });

  useEffect(() => {
    let active = true;
    if (options.enabled === false) {
      queueMicrotask(() => {
        if (active) setResource({ status: 'loading' });
      });
      return () => {
        active = false;
      };
    }

    const controller = new AbortController();
    queueMicrotask(() => {
      if (active) setResource({ status: 'loading' });
    });

    void (async () => {
      try {
        const data = await fetcher(controller.signal);
        if (active) setResource({ status: 'ok', data });
      } catch (error: unknown) {
        if (!active) return;
        if (error instanceof IRacingNotLinkedError) {
          if (options.onNotLinked) {
            setResource({ status: 'ok', data: options.onNotLinked.fallback });
            return;
          }
          setResource({ status: 'not-linked' });
          return;
        }
        if (options.onError) {
          setResource({ status: 'ok', data: options.onError.fallback });
          return;
        }
        setResource({
          status: 'error',
          message: errorMessage(error, options.fallbackMessage ?? DEFAULT_ERROR),
        });
      }
    })();

    return () => {
      active = false;
      controller.abort();
    };
    // The dependency list is deliberately part of this hook's public interface.
    // oxlint-disable-next-line react/exhaustive-deps
  }, dependencies);

  return resource;
}
