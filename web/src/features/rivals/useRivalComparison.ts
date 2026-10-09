import { useState } from 'react';
import { api, type DriverComparison } from '../../services/api';
import { useDriverResource } from '../../hooks/useDriverResource';
import type { Resource } from '../../hooks/resourceRequest';

type ComparisonState =
  | { status: 'idle' }
  | { status: 'comparing' }
  | Exclude<Resource<DriverComparison>, { status: 'loading' }>;
type ComparisonTransport = Pick<typeof api, 'compareRival' | 'removeRival'>;

/** Selection and successful removal invalidate the entire bounded comparison read. */
export function useRivalComparison(transport: ComparisonTransport = api) {
  const [selection, setSelection] = useState<{ id: number | null; version: number }>({
    id: null,
    version: 0,
  });
  const selected = selection.id;
  const resource = useDriverResource(
    signal => transport.compareRival(selected!, signal),
    [selected, selection.version, transport],
    {
      enabled: selected !== null,
      fallbackMessage: 'Failed to load comparison.',
    }
  );
  const comparison: ComparisonState =
    selected === null
      ? { status: 'idle' }
      : resource.status === 'loading'
        ? { status: 'comparing' }
        : resource;
  const remove = async (customerId: number) => {
    await transport.removeRival(customerId);
    setSelection(current =>
      current.id === customerId ? { id: null, version: current.version + 1 } : current
    );
  };
  return {
    selected,
    comparison,
    compare: (id: number) => setSelection(current => ({ id, version: current.version + 1 })),
    remove,
  };
}
