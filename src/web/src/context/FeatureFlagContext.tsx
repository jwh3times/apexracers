import { createContext, useContext, useEffect, useState } from 'react';
import type { ReactNode } from 'react';
import { api } from '../services/api';
import type { FeatureFlag } from '../services/api';
import { useAuth } from './AuthContext';

interface FeatureFlagContextValue {
  isEnabled: (key: string) => boolean;
}

const FeatureFlagContext = createContext<FeatureFlagContextValue>({ isEnabled: () => false });

export function FeatureFlagProvider({ children }: { children: ReactNode }) {
  const { user } = useAuth();
  const [flagMap, setFlagMap] = useState<Record<string, boolean>>({});

  useEffect(() => {
    if (!user) {
      setFlagMap({});
      return;
    }
    let cancelled = false;
    api
      .getFeatureFlags()
      .then((flags: FeatureFlag[]) => {
        if (cancelled) return;
        const map: Record<string, boolean> = {};
        for (const f of flags) map[f.key] = true;
        setFlagMap(map);
      })
      .catch(() => {
        if (!cancelled) setFlagMap({});
      });
    return () => {
      cancelled = true;
    };
  }, [user?.userId, user?.role]);

  return (
    <FeatureFlagContext.Provider value={{ isEnabled: key => flagMap[key] === true }}>
      {children}
    </FeatureFlagContext.Provider>
  );
}

export function useFeatureFlag(key: string): boolean {
  return useContext(FeatureFlagContext).isEnabled(key);
}
