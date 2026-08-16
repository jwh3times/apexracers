import { createContext, useContext } from 'react';
import type { Dispatch, SetStateAction } from 'react';
import type { LapSessionType, PersonalBestEvidenceOptions } from '../services/api';

export type PaceMode = 'official' | 'blend';

export interface PaceSourceValue {
  mode: PaceMode;
  sessions: LapSessionType[];
}

export interface PaceSourceContextValue {
  value: PaceSourceValue;
  setValue: Dispatch<SetStateAction<PaceSourceValue>>;
  evidenceOptions: PersonalBestEvidenceOptions;
}

export const PaceSourceContext = createContext<PaceSourceContextValue | undefined>(undefined);

export function usePaceSource(): PaceSourceContextValue {
  const context = useContext(PaceSourceContext);
  if (!context) throw new Error('usePaceSource must be used within PaceSourceProvider');
  return context;
}
