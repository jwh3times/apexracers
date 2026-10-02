import { useEffect, useState } from 'react';
import { createResourceRequest, type Resource, type ResourceOptions } from './resourceRequest';

export type { Resource } from './resourceRequest';

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
  const [request] = useState(() => createResourceRequest<T>());

  useEffect(() => {
    request.load(fetcher, setResource, options);
    return () => request.cancel();
    // The dependency list is deliberately part of this hook's public interface.
    // oxlint-disable-next-line react/exhaustive-deps
  }, [request, ...dependencies]);

  return resource;
}
