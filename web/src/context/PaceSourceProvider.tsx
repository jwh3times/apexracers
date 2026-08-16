import { useMemo, useState } from 'react';
import type { ReactNode } from 'react';
import type { PersonalBestEvidenceOptions } from '../services/api';
import { useAuth } from './AuthContext';
import {
  PaceSourceContext,
  type PaceSourceContextValue,
  type PaceSourceValue,
} from './PaceSourceContext';

const OFFICIAL_PACE_SOURCE: PaceSourceValue = { mode: 'official', sessions: [] };

export function PaceSourceProvider({ children }: { children: ReactNode }) {
  const { user } = useAuth();
  const owner = user ? `user:${user.userId}` : 'guest';
  return <PaceSourceState key={owner}>{children}</PaceSourceState>;
}

function PaceSourceState({ children }: { children: ReactNode }) {
  const [value, setValue] = useState<PaceSourceValue>(OFFICIAL_PACE_SOURCE);

  const evidenceOptions = useMemo<PersonalBestEvidenceOptions>(() => {
    const blended = value.mode === 'blend';
    return {
      includePersonalLaps: blended,
      personalLapTypes: blended && value.sessions.length > 0 ? value.sessions : undefined,
    };
  }, [value]);

  const contextValue = useMemo<PaceSourceContextValue>(
    () => ({ value, setValue, evidenceOptions }),
    [value, evidenceOptions]
  );

  return <PaceSourceContext.Provider value={contextValue}>{children}</PaceSourceContext.Provider>;
}
