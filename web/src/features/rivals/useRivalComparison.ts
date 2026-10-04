import { useEffect, useRef, useState } from 'react';
import { api, type DriverComparison } from '../../services/api';
import { createResourceRequest, type Resource } from '../../hooks/resourceRequest';

type ComparisonState =
  | { status: 'idle' }
  | { status: 'comparing' }
  | Exclude<Resource<DriverComparison>, { status: 'loading' }>;

type ComparisonTransport = Pick<typeof api, 'compareRival' | 'removeRival'>;

/** Owns selection, request replacement and successful-removal invalidation as one workflow. */
export function useRivalComparison(transport: ComparisonTransport = api) {
  const [state, setState] = useState<{
    selected: number | null;
    comparison: ComparisonState;
  }>({ selected: null, comparison: { status: 'idle' } });
  const selected = useRef<number | null>(null);
  const [request] = useState(() => createResourceRequest<DriverComparison>());

  useEffect(
    () => () => {
      selected.current = null;
      request.cancel();
    },
    [request]
  );

  const compare = (customerId: number) => {
    selected.current = customerId;
    setState({ selected: customerId, comparison: { status: 'comparing' } });
    request.load(
      signal => transport.compareRival(customerId, signal),
      resource =>
        setState({
          selected: customerId,
          comparison: resource.status === 'loading' ? { status: 'comparing' } : resource,
        }),
      { fallbackMessage: 'Failed to load comparison.' }
    );
  };

  const remove = async (customerId: number) => {
    await transport.removeRival(customerId);
    // Check current selection after the write, not the selection when removal began.
    if (selected.current === customerId) {
      selected.current = null;
      request.cancel();
      setState({ selected: null, comparison: { status: 'idle' } });
    }
  };

  return { ...state, compare, remove };
}
