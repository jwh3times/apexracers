import { useEffect, useState } from 'react';
import type { ReactNode } from 'react';
import { api } from '../services/api';
import type { FeatureFlag } from '../services/api';
import { useAuth } from './AuthContext';
import { FeatureFlagContext } from './FeatureFlagContext';

export function FeatureFlagProvider({ children }: { children: ReactNode }) {
  const { user } = useAuth();
  const userId = user?.userId ?? null;
  const userRole = user?.role ?? null;
  // Identity of the flag owner: changes on login/logout and on role change, which
  // is exactly when the eligible flag set can differ. Stored alongside the fetched
  // map so a stale map (from a previous user/role) reads as empty until refreshed.
  const owner = userId == null ? null : `${userId}:${userRole}`;

  const [flags, setFlags] = useState<{ owner: string | null; map: Record<string, boolean> }>({
    owner: null,
    map: {},
  });

  useEffect(() => {
    if (owner == null) return;
    let cancelled = false;
    api
      .getFeatureFlags()
      .then((flagList: FeatureFlag[]) => {
        if (cancelled) return;
        const map: Record<string, boolean> = {};
        for (const f of flagList) map[f.key] = true;
        setFlags({ owner, map });
      })
      .catch(() => {
        if (!cancelled) setFlags({ owner, map: {} });
      });
    return () => {
      cancelled = true;
    };
  }, [owner]);

  const isEnabled = (key: string) => flags.owner === owner && flags.map[key] === true;

  return (
    <FeatureFlagContext.Provider value={{ isEnabled }}>{children}</FeatureFlagContext.Provider>
  );
}
