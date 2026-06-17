import { createContext, useContext } from 'react';

export interface FeatureFlagContextValue {
  isEnabled: (key: string) => boolean;
}

export const FeatureFlagContext = createContext<FeatureFlagContextValue>({
  isEnabled: () => false,
});

export function useFeatureFlag(key: string): boolean {
  return useContext(FeatureFlagContext).isEnabled(key);
}
