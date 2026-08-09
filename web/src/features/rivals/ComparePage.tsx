import { useEffect, useMemo, useState } from 'react';
import { Link } from 'react-router';
import {
  api,
  ApiError,
  IRacingNotLinkedError,
  type Rival,
  type RivalSuggestion,
  type DriverSearchResult,
  type DriverComparison,
  type ComparisonSide,
} from '../../services/api';
import { useFeatureFlag } from '../../context/FeatureFlagContext';
import IRatingCompareChart from '../../components/IRatingCompareChart';
import { formatLapTime } from '../../utils/lapTime';

const cardStyle: React.CSSProperties = {
  boxShadow: '0 1px 0 rgba(255,255,255,.03) inset, 0 18px 40px -24px rgba(0,0,0,.8)',
};
const scanTexture: React.CSSProperties = {
  backgroundImage:
    'repeating-linear-gradient(115deg, rgba(255,255,255,0.04) 0 1px, transparent 1px 9px)',
};
const RIVAL_COLOR = '#f5a623';

type ComparisonState =
  | { status: 'idle' }
  | { status: 'comparing' }
  | { status: 'ok'; data: DriverComparison }
  | { status: 'not-linked' }
  | { status: 'error'; message: string };

function Card({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <div className="card-r border border-line-2 bg-surface overflow-hidden" style={cardStyle}>
      <div className="card-hp border-b border-line-2" style={scanTexture}>
        <h3 className="text-section-head text-on-surface">{title}</h3>
      </div>
      <div className="card-p">{children}</div>
    </div>
  );
}

function LicenseBadges({ side }: { side: ComparisonSide }) {
  return (
    <div className="flex flex-wrap gap-2">
      {side.licenses.map(l => (
        <span
          key={l.categoryId}
          className="text-eyebrow px-2 py-1 rounded-[7px] border"
          style={{ color: `#${l.color}`, borderColor: `#${l.color}55` }}
          title={`${l.categoryName} · ${l.groupName}`}
        >
          {l.categoryName} {l.iRating.toLocaleString()} · {l.safetyRating.toFixed(2)}
        </span>
      ))}
    </div>
  );
}

function SideIdentity({ side, accent }: { side: ComparisonSide; accent: string }) {
  return (
    <div className="flex flex-col gap-2">
      <div className="flex items-center gap-2">
        <span className="inline-block w-2.5 h-2.5 rounded-full" style={{ background: accent }} />
        <span className="text-section-head text-on-surface">{side.displayName}</span>
      </div>
      <p className="text-small-fluid text-on-surface-variant">
        {[side.country, side.memberSince ? `Member since ${side.memberSince}` : null]
          .filter(Boolean)
          .join(' · ') || '—'}
      </p>
      <LicenseBadges side={side} />
    </div>
  );
}

function StatRow({ label, you, rival }: { label: string; you: string; rival: string }) {
  return (
    <div className="grid grid-cols-[1fr_auto_auto] gap-fluid items-baseline py-1 border-b border-line-2/60 last:border-0">
      <span className="text-small-fluid text-on-surface-variant">{label}</span>
      <span className="text-mono-fluid text-on-surface text-right w-20">{you}</span>
      <span className="text-mono-fluid text-right w-20" style={{ color: RIVAL_COLOR }}>
        {rival}
      </span>
    </div>
  );
}

function CareerCompare({ data }: { data: DriverComparison }) {
  // Union of categories present in either driver's career, keyed by id.
  const cats = useMemo(() => {
    const map = new Map<number, string>();
    for (const c of [...data.you.career, ...data.rival.career])
      map.set(c.categoryId, c.categoryName);
    return [...map.entries()].map(([id, name]) => ({ id, name }));
  }, [data]);

  return (
    <div className="flex flex-col gap-fluid">
      {cats.map(cat => {
        const you = data.you.career.find(c => c.categoryId === cat.id);
        const rival = data.rival.career.find(c => c.categoryId === cat.id);
        const f = (n?: number) => (n == null ? '—' : n.toLocaleString());
        const pct = (n?: number) => (n == null ? '—' : `${n.toFixed(1)}%`);
        return (
          <div key={cat.id}>
            <div className="grid grid-cols-[1fr_auto_auto] gap-fluid items-baseline mb-1">
              <span className="text-th text-on-surface-variant">{cat.name}</span>
              <span className="text-th text-primary-container text-right w-20">You</span>
              <span className="text-th text-right w-20" style={{ color: RIVAL_COLOR }}>
                Rival
              </span>
            </div>
            <StatRow label="Starts" you={f(you?.starts)} rival={f(rival?.starts)} />
            <StatRow label="Wins" you={f(you?.wins)} rival={f(rival?.wins)} />
            <StatRow label="Top 5" you={f(you?.top5)} rival={f(rival?.top5)} />
            <StatRow
              label="Win %"
              you={pct(you?.winPercentage)}
              rival={pct(rival?.winPercentage)}
            />
            <StatRow
              label="Avg finish"
              you={f(you?.avgFinishPosition)}
              rival={f(rival?.avgFinishPosition)}
            />
          </div>
        );
      })}
    </div>
  );
}

function IRatingOverlay({ data }: { data: DriverComparison }) {
  const cats = useMemo(() => {
    const map = new Map<number, string>();
    for (const c of [...data.you.iRatingHistory, ...data.rival.iRatingHistory])
      map.set(c.categoryId, c.categoryName);
    return [...map.entries()].map(([id, name]) => ({ id, name }));
  }, [data]);

  const [catId, setCatId] = useState<number | null>(cats[0]?.id ?? null);
  const active = catId ?? cats[0]?.id ?? null;

  const youPts = data.you.iRatingHistory.find(c => c.categoryId === active)?.points ?? [];
  const rivalPts = data.rival.iRatingHistory.find(c => c.categoryId === active)?.points ?? [];

  return (
    <div className="flex flex-col gap-3">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div className="flex flex-wrap gap-2">
          {cats.map(c => (
            <button
              key={c.id}
              type="button"
              onClick={() => setCatId(c.id)}
              className={`btn-fluid-sm rounded-[7px] border ${
                c.id === active
                  ? 'border-primary-container text-primary-container'
                  : 'border-line-2 text-on-surface-variant'
              }`}
            >
              {c.name}
            </button>
          ))}
        </div>
        <div className="flex items-center gap-3 text-small-fluid">
          <span className="flex items-center gap-1 text-primary-container">
            <span className="inline-block w-2.5 h-2.5 rounded-full bg-primary-container" />
            You
          </span>
          <span className="flex items-center gap-1" style={{ color: RIVAL_COLOR }}>
            <span
              className="inline-block w-2.5 h-2.5 rounded-full"
              style={{ background: RIVAL_COLOR }}
            />
            {data.rival.displayName}
          </span>
        </div>
      </div>
      {youPts.length >= 2 || rivalPts.length >= 2 ? (
        <IRatingCompareChart
          you={youPts.map(p => p.value)}
          rival={rivalPts.map(p => p.value)}
          h={120}
        />
      ) : (
        <p className="text-small-fluid text-on-surface-variant">
          Not enough iRating history to chart this category.
        </p>
      )}
    </div>
  );
}

function HeadToHead({ data }: { data: DriverComparison }) {
  const s = data.shared;
  const lap = (v: number) => (v > 0 ? formatLapTime(v) : '—');
  return (
    <div className="flex flex-col gap-fluid">
      <div className="grid grid-kpi gap-fluid">
        <div className="kpi-p card-r border border-line-2 bg-surface-container">
          <p className="text-th text-on-surface-variant mb-1">Shared races</p>
          <p className="text-kpi-value text-on-surface">{s.totalShared} shared races</p>
        </div>
        <div className="kpi-p card-r border border-line-2 bg-surface-container">
          <p className="text-th text-on-surface-variant mb-1">You ahead</p>
          <p className="text-kpi-value text-primary-container">{s.youAhead}</p>
        </div>
        <div className="kpi-p card-r border border-line-2 bg-surface-container">
          <p className="text-th text-on-surface-variant mb-1">Rival ahead</p>
          <p className="text-kpi-value" style={{ color: RIVAL_COLOR }}>
            {s.rivalAhead}
          </p>
        </div>
      </div>

      {s.races.length > 0 && (
        <div className="overflow-x-auto">
          <table className="w-full">
            <thead>
              <tr className="text-th text-on-surface-variant text-left border-b border-line-2">
                <th className="th-p">Track</th>
                <th className="th-p text-right">You</th>
                <th className="th-p text-right">Rival</th>
                <th className="th-p text-right">Your iR</th>
                <th className="th-p text-right">Rival iR</th>
              </tr>
            </thead>
            <tbody>
              {s.races.map(r => (
                <tr key={r.subsessionId} className="border-b border-line-2/60">
                  <td className="td-p text-body-fluid text-on-surface">{r.trackName}</td>
                  <td className="td-p text-mono-fluid text-right">P{r.yourFinish}</td>
                  <td className="td-p text-mono-fluid text-right">P{r.rivalFinish}</td>
                  <td
                    className={`td-p text-mono-fluid text-right ${
                      r.yourIRatingDelta >= 0 ? 'text-primary-container' : 'text-error'
                    }`}
                  >
                    {r.yourIRatingDelta >= 0 ? '+' : ''}
                    {r.yourIRatingDelta}
                  </td>
                  <td
                    className={`td-p text-mono-fluid text-right ${
                      r.rivalIRatingDelta >= 0 ? 'text-primary-container' : 'text-error'
                    }`}
                  >
                    {r.rivalIRatingDelta >= 0 ? '+' : ''}
                    {r.rivalIRatingDelta}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {s.trackPace.length > 0 && (
        <div className="flex flex-col gap-1">
          <p className="text-th text-on-surface-variant">Best lap at shared tracks</p>
          {s.trackPace.map(p => (
            <div
              key={p.trackName}
              className="grid grid-cols-[1fr_auto_auto] gap-fluid items-baseline py-1 border-b border-line-2/60 last:border-0"
            >
              <span className="text-body-fluid text-on-surface">{p.trackName}</span>
              <span className="text-mono-fluid text-primary-container text-right w-24">
                {lap(p.yourBestLapSeconds)}
              </span>
              <span className="text-mono-fluid text-right w-24" style={{ color: RIVAL_COLOR }}>
                {lap(p.rivalBestLapSeconds)}
              </span>
            </div>
          ))}
        </div>
      )}

      {s.totalShared === 0 && (
        <p className="text-small-fluid text-on-surface-variant">
          No shared races yet — once you both race an ingested session, the head-to-head shows here.
        </p>
      )}
    </div>
  );
}

export default function ComparePage() {
  const demoFlag = useFeatureFlag('iracing-demo');
  const [rivals, setRivals] = useState<Rival[]>([]);
  const [suggestions, setSuggestions] = useState<RivalSuggestion[]>([]);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [term, setTerm] = useState('');
  const [results, setResults] = useState<DriverSearchResult[]>([]);
  const [searchUnavailable, setSearchUnavailable] = useState(false);
  const [selected, setSelected] = useState<number | null>(null);
  const [comparison, setComparison] = useState<ComparisonState>({ status: 'idle' });

  // `isActive` lets the mount effect drop a result that landed after unmount; the add/remove
  // handlers pass nothing and always apply.
  const reloadRivals = (isActive: () => boolean = () => true) =>
    api
      .getRivals()
      .then(rows => {
        if (isActive()) setRivals(rows);
      })
      .catch((e: unknown) => {
        if (isActive()) setLoadError(e instanceof Error ? e.message : 'Failed to load rivals.');
      });

  const reloadSuggestions = (isActive: () => boolean = () => true) =>
    api
      .getRivalSuggestions()
      .then(rows => {
        if (isActive()) setSuggestions(rows);
      })
      .catch(() => {
        if (isActive()) setSuggestions([]); // not-linked / unavailable → no suggestions
      });

  useEffect(() => {
    let active = true;
    const isActive = () => active;
    void reloadRivals(isActive);
    void reloadSuggestions(isActive);
    return () => {
      active = false;
    };
  }, []);

  // Debounced driver name search. All state updates happen inside the timer (never
  // synchronously in the effect body) so short/cleared terms also settle after the debounce.
  useEffect(() => {
    const q = term.trim();
    // `active` as well as clearTimeout: clearing only stops a timer that hasn't fired yet,
    // so a search already in flight would still land its result after unmount.
    let active = true;
    const id = setTimeout(() => {
      if (q.length < 2) {
        setResults([]);
        setSearchUnavailable(false);
        return;
      }
      setSearchUnavailable(false);
      api
        .searchDrivers(q)
        .then(rows => {
          if (active) setResults(rows);
        })
        .catch((err: unknown) => {
          if (!active) return;
          setResults([]);
          // 503 = search backend unavailable (live: no creds; demo: term not in the
          // curated seed set) — worth telling apart from "no drivers matched".
          if (err instanceof ApiError && err.status === 503) setSearchUnavailable(true);
        });
    }, 300);
    return () => {
      active = false;
      clearTimeout(id);
    };
  }, [term]);

  const followedIds = useMemo(() => new Set(rivals.map(r => r.custId)), [rivals]);

  const add = async (custId: number, displayName: string) => {
    await api.addRival(custId, displayName);
    await reloadRivals();
    await reloadSuggestions();
    setResults(rs => rs.filter(r => r.custId !== custId));
  };

  const remove = async (custId: number) => {
    await api.removeRival(custId);
    if (selected === custId) {
      setSelected(null);
      setComparison({ status: 'idle' });
    }
    await reloadRivals();
  };

  const compare = (custId: number) => {
    setSelected(custId);
    setComparison({ status: 'comparing' });
    api
      .compareRival(custId)
      .then(data => setComparison({ status: 'ok', data }))
      .catch((err: unknown) => {
        if (err instanceof IRacingNotLinkedError) {
          setComparison({ status: 'not-linked' });
          return;
        }
        setComparison({
          status: 'error',
          message: err instanceof Error ? err.message : 'Failed to load comparison.',
        });
      });
  };

  return (
    <main className="page-wrap">
      <div className="mb-6">
        <p className="text-eyebrow text-primary-container">COMPARE</p>
        <h1 className="text-page-title text-on-surface mt-2">Driver Comparison</h1>
      </div>

      {loadError && (
        <div
          className="card-r border border-line-2 bg-surface p-6 text-body-fluid text-error mb-6"
          style={cardStyle}
        >
          {loadError}
        </div>
      )}

      <div className="grid gap-fluid-lg lg:grid-cols-[minmax(0,22rem)_1fr]">
        {/* Rival manager */}
        <div className="flex flex-col gap-fluid">
          <Card title="Find a rival">
            <input
              type="text"
              value={term}
              onChange={e => setTerm(e.target.value)}
              placeholder="Search drivers by name…"
              className="w-full btn-fluid-sm rounded-[7px] border border-line-2 bg-surface-container text-on-surface px-3"
              aria-label="Search drivers"
            />
            {searchUnavailable && (
              <p className="text-small-fluid text-on-surface-variant mt-2">
                Driver search isn&apos;t available right now — pick a driver from the suggestions
                below.
                {/* "demo" / "rival" / "driver" are verified members of the backend's
                    DemoDriverSearchData.Terms curated seed — keep in sync if it changes. */}
                {demoFlag &&
                  ' Demo mode: search covers sample drivers only — try "demo", "rival", or "driver".'}
              </p>
            )}
            {results.length > 0 && (
              <ul className="mt-3 flex flex-col gap-2">
                {results.map(r => (
                  <li key={r.custId} className="flex items-center justify-between gap-2">
                    <span className="text-body-fluid text-on-surface">{r.displayName}</span>
                    {followedIds.has(r.custId) ? (
                      <span className="text-small-fluid text-on-surface-variant">Following</span>
                    ) : (
                      <button
                        type="button"
                        onClick={() => void add(r.custId, r.displayName)}
                        className="btn-fluid-sm rounded-[7px] border border-primary-container text-primary-container"
                        aria-label={`Add ${r.displayName}`}
                      >
                        Add
                      </button>
                    )}
                  </li>
                ))}
              </ul>
            )}
          </Card>

          {suggestions.length > 0 && (
            <Card title="Drivers you've raced">
              <ul className="flex flex-col gap-2">
                {suggestions.map(s => (
                  <li key={s.custId} className="flex items-center justify-between gap-2">
                    <span className="flex flex-col">
                      <span className="text-body-fluid text-on-surface">{s.displayName}</span>
                      <span className="text-small-fluid text-on-surface-variant">
                        {s.sharedRaces} shared
                      </span>
                    </span>
                    <button
                      type="button"
                      onClick={() => void add(s.custId, s.displayName)}
                      className="btn-fluid-sm rounded-[7px] border border-primary-container text-primary-container"
                      aria-label={`Add ${s.displayName}`}
                    >
                      Add
                    </button>
                  </li>
                ))}
              </ul>
            </Card>
          )}

          <Card title="Your rivals">
            {rivals.length === 0 && suggestions.length === 0 ? (
              <p className="text-small-fluid text-on-surface-variant">
                No rivals yet — search for a driver above to start a comparison.
              </p>
            ) : rivals.length === 0 ? (
              <p className="text-small-fluid text-on-surface-variant">
                No rivals followed yet. Add one from your suggestions or search.
              </p>
            ) : (
              <ul className="flex flex-col gap-2">
                {rivals.map(r => (
                  <li
                    key={r.custId}
                    className={`flex items-center justify-between gap-2 p-2 rounded-[7px] border ${
                      selected === r.custId ? 'border-primary-container' : 'border-line-2'
                    }`}
                  >
                    <span className="text-body-fluid text-on-surface">{r.displayName}</span>
                    <span className="flex items-center gap-2">
                      <button
                        type="button"
                        onClick={() => compare(r.custId)}
                        className="btn-fluid-sm rounded-[7px] border border-primary-container text-primary-container"
                        aria-label={`Compare against ${r.displayName}`}
                      >
                        Compare
                      </button>
                      <button
                        type="button"
                        onClick={() => void remove(r.custId)}
                        className="btn-fluid-sm rounded-[7px] border border-line-2 text-on-surface-variant"
                        aria-label={`Remove ${r.displayName}`}
                      >
                        Remove
                      </button>
                    </span>
                  </li>
                ))}
              </ul>
            )}
          </Card>
        </div>

        {/* Comparison */}
        <div className="flex flex-col gap-fluid">
          {comparison.status === 'idle' && (
            <div
              className="card-r border border-line-2 bg-surface p-8 text-center text-body-fluid text-on-surface-variant"
              style={cardStyle}
            >
              Pick a rival and hit Compare to see the head-to-head.
            </div>
          )}

          {comparison.status === 'comparing' && (
            <p className="text-body-fluid text-on-surface-variant animate-pulse">
              Comparing&hellip;
            </p>
          )}

          {comparison.status === 'error' && (
            <div
              className="card-r border border-line-2 bg-surface p-6 text-body-fluid text-error"
              style={cardStyle}
            >
              {comparison.message}
            </div>
          )}

          {comparison.status === 'not-linked' && (
            <div
              className="card-r border border-line-2 bg-surface p-8 flex flex-col items-center gap-4 text-center"
              style={cardStyle}
            >
              <span
                className="material-symbols-outlined text-4xl text-primary-container"
                aria-hidden="true"
              >
                link_off
              </span>
              <p className="text-body-fluid text-on-surface-variant">
                Link your iRacing account to compare. Add your iRacing customer ID in{' '}
                <Link
                  to="/settings"
                  className="text-primary-container underline hover:opacity-80 transition-opacity"
                >
                  Settings
                </Link>
                .
              </p>
            </div>
          )}

          {comparison.status === 'ok' && (
            <>
              <Card title="Drivers">
                <div className="grid grid-cols-2 gap-fluid-lg">
                  <SideIdentity
                    side={comparison.data.you}
                    accent="var(--color-primary-container)"
                  />
                  <SideIdentity side={comparison.data.rival} accent={RIVAL_COLOR} />
                </div>
              </Card>
              <Card title="iRating trajectory">
                <IRatingOverlay data={comparison.data} />
              </Card>
              <Card title="Career">
                <CareerCompare data={comparison.data} />
              </Card>
              <Card title="Head-to-head">
                <HeadToHead data={comparison.data} />
              </Card>
            </>
          )}
        </div>
      </div>
    </main>
  );
}
