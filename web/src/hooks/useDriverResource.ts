import { useEffect, useMemo, useSyncExternalStore } from 'react';
import { createDriverResource } from './driverResource';
import type { Resource, ResourceOptions } from './resourceRequest';
import { driverDisclosure } from '../services/driverDisclosure';
export type { Resource } from './resourceRequest';

/** Sensitive page reads recheck their protected endpoint every 15 seconds. A changed dependency
 * creates an empty resource synchronously, so even the render before the effect cannot reuse it. */
export function useDriverResource<T>(
  fetcher: (signal: AbortSignal) => Promise<T>,
  dependencies: readonly unknown[],
  options: ResourceOptions<T> = {}
): Resource<T> {
  // The dependency list is this hook's public selection/authorization interface.
  // oxlint-disable-next-line react/exhaustive-deps
  const request = useMemo(() => createDriverResource<T>(), [...dependencies, options.enabled]);
  const resource = useSyncExternalStore(
    request.subscribe,
    request.getSnapshot,
    request.getSnapshot
  );
  useEffect(() => {
    request.load(fetcher, options, !driverDisclosure.canRead());
    const unsubscribe = driverDisclosure.subscribe(event => {
      if (event === 'pause') request.pause();
      else request.revalidate();
    });
    return () => {
      unsubscribe();
      request.cancel();
    };
    // oxlint-disable-next-line react/exhaustive-deps
  }, [request]);
  return resource;
}
