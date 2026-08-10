import { createContext, useContext } from 'react';

export interface FeatureFlagContextValue {
  isEnabled: (key: string) => boolean;
  ready: boolean;
}

export const FeatureFlagContext = createContext<FeatureFlagContextValue>({
  isEnabled: () => false,
  ready: false,
});

export function useFeatureFlags(): FeatureFlagContextValue {
  return useContext(FeatureFlagContext);
}

export function useFeatureFlag(key: string): boolean {
  return useFeatureFlags().isEnabled(key);
}

export function useIracingSurface(): { enabled: boolean; ready: boolean } {
  const { isEnabled, ready } = useFeatureFlags();
  const live = isEnabled('iracing-live');
  const demo = isEnabled('iracing-demo');
  return { enabled: live || demo, ready };
}
