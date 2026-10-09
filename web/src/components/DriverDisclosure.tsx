import { useState } from 'react';
import { api } from '../services/api';
import {
  driverDisclosure,
  type PendingWithdrawal,
  type WithdrawalScope,
} from '../services/driverDisclosure';
import { useDriverResource } from '../hooks/useDriverResource';
import { formatLapTime } from '../utils/lapTime';

export function PersonalDriverCard() {
  const personal = useDriverResource(signal => api.getScopedDriverPersonal(signal), []);
  const [pending, setPending] = useState<PendingWithdrawal | null>(() =>
    driverDisclosure.pendingWithdrawal()
  );
  const [working, setWorking] = useState(false);
  const [message, setMessage] = useState('');
  const data = personal.status === 'ok' ? personal.data : undefined;
  async function withdraw(scope: WithdrawalScope) {
    const operation = pending ?? { operationId: crypto.randomUUID(), scope };
    setPending(operation);
    setWorking(true);
    setMessage('Recording withdrawal. Affected display stays closed.');
    try {
      driverDisclosure.beginWithdrawal(operation.operationId, operation.scope);
      const result = await api.withdrawDriver(operation);
      if (!result.withdrawalRecorded || result.operationId !== operation.operationId)
        throw new Error('Uncertain withdrawal');
      driverDisclosure.finishWithdrawal(operation.operationId);
      setPending(null);
      setMessage(
        `Withdrawal recorded. ${result.writersDrained ? 'Active writers have drained.' : 'Active writers are still draining.'} Live erasure and backup expiry have not been verified. Already delivered or exported bytes cannot be recalled.`
      );
    } catch {
      setMessage(
        'Withdrawal has not been confirmed. Affected display stays closed; retry the same operation.'
      );
    } finally {
      setWorking(false);
    }
  }
  if (!data && !pending && !message) return null;
  return (
    <section
      aria-label="Personal Driver data"
      className="card-r border border-line-2 bg-surface card-p mb-6"
    >
      <h2 className="text-section-head text-on-surface">Personal Driver data</h2>
      {data && (
        <div className="text-body-fluid text-on-surface mt-2">
          <p>{data.driverName}</p>
          <p>
            Best official lap: {formatLapTime(data.officialBestLapSeconds)} · iRating {data.iRating}
          </p>
          {data.provenance === 'synthetic' && (
            <p className="text-small-fluid text-on-surface-variant">Synthetic preview data</p>
          )}
        </div>
      )}
      <div className="flex gap-3 mt-3">
        {pending ? (
          <button
            type="button"
            disabled={working}
            className="btn-fluid-sm border border-line-2 text-on-surface"
            onClick={() => void withdraw(pending.scope)}
          >
            Retry withdrawal
          </button>
        ) : (
          data && (
            <>
              <button
                type="button"
                disabled={working}
                className="btn-fluid-sm border border-line-2 text-on-surface"
                onClick={() => void withdraw('personal')}
              >
                Withdraw personal consent
              </button>
              <button
                type="button"
                disabled={working}
                className="btn-fluid-sm border border-line-2 text-on-surface"
                onClick={() => void withdraw('sharing')}
              >
                Withdraw sharing consent
              </button>
            </>
          )
        )}
      </div>
      {message && (
        <output className="text-small-fluid text-on-surface-variant mt-3">{message}</output>
      )}
    </section>
  );
}

export function SharedDriverCard() {
  const discovery = useDriverResource(
    signal => api.getScopedDriverDiscovery(undefined, signal),
    []
  );
  const [selected, setSelected] = useState<string | null>(null);
  const [followsVersion, setFollowsVersion] = useState(0);
  const rows = discovery.status === 'ok' ? discovery.data : [];
  const current = rows?.find(row => row.comparisonReference === selected);
  const comparison = useDriverResource(
    signal => api.compareScopedDriver({ reference: selected }, signal),
    [selected, !!current],
    { enabled: !!current }
  );
  const follows = useDriverResource(signal => api.getScopedDriverFollows(signal), [followsVersion]);
  if (discovery.status !== 'ok') return null;
  return (
    <section
      aria-label="Shared Drivers"
      className="card-r border border-line-2 bg-surface card-p mb-6"
    >
      <h2 className="text-section-head text-on-surface">Shared Drivers</h2>
      <p className="text-small-fluid text-on-surface-variant mt-2">
        Only currently consented sharing is shown.
      </p>
      {rows?.map(row => (
        <div key={row.comparisonReference} className="flex gap-3 items-center mt-3">
          <span className="text-body-fluid text-on-surface">{row.driverName}</span>
          <button
            type="button"
            className="btn-fluid-sm border border-line-2 text-on-surface"
            onClick={() => setSelected(row.comparisonReference)}
          >
            Compare shared Driver
          </button>
          <button
            type="button"
            className="btn-fluid-sm border border-line-2 text-on-surface"
            onClick={() =>
              void api
                .followScopedDriver({ reference: row.followReference })
                .then(() => setFollowsVersion(value => value + 1))
                .catch(() => driverDisclosure.invalidate())
            }
          >
            Follow shared Driver
          </button>
        </div>
      ))}
      {current && comparison.status === 'ok' && comparison.data && (
        <p className="text-body-fluid text-on-surface mt-3">
          {comparison.data.driverName}: lap gap {comparison.data.lapDeltaSeconds.toFixed(3)}s
        </p>
      )}
      {follows.status === 'ok' && follows.data?.length > 0 && (
        <p className="text-small-fluid text-on-surface-variant mt-3">
          Private follows: {follows.data.map(row => row.driverName).join(', ')}
        </p>
      )}
    </section>
  );
}
