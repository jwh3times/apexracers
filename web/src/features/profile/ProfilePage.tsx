import { useState } from 'react';
import { Link } from 'react-router';
import {
  api,
  type Achievements,
  type Award,
  type DriverProfile,
  type UploadedBest,
  type Series,
} from '../../services/api';
import ResourceView, { NotLinkedCard } from '../../components/ResourceView';
import { raceWeekNumber } from '../../utils/raceWeek';
import { useAuth } from '../../context/AuthContext';
import { useIracingSurface } from '../../context/FeatureFlagContext';
import type { Resource } from '../../hooks/useResource';
import { useResource } from '../../hooks/useResource';
import { formatLapTime } from '../../utils/lapTime';

function StatTile({ label, value }: { label: string; value: React.ReactNode }) {
  return (
    <div className="kpi-p card-r border border-line-2 bg-surface-container">
      <p className="text-th text-on-surface-variant mb-1">{label}</p>
      <p className="text-kpi-value text-on-surface">{value}</p>
    </div>
  );
}

function DriverStats({ data: d }: { data: DriverProfile }) {
  return (
    <div className="flex flex-col gap-fluid">
      {/* Licenses */}
      <div className="card-r card-shadow border border-line-2 bg-surface overflow-hidden">
        <div className="card-hp scan-texture border-b border-line-2 flex items-center justify-between gap-3">
          <h3 className="text-section-head text-on-surface">Licenses</h3>
          <span className="text-small-fluid text-on-surface-variant">
            {[d.country, d.memberSince ? `Member since ${d.memberSince}` : null]
              .filter(Boolean)
              .join(' · ')}
          </span>
        </div>
        <div className="card-p flex flex-wrap gap-fluid">
          {d.licenses.length === 0 ? (
            <p className="text-small-fluid text-on-surface-variant">No license data.</p>
          ) : (
            d.licenses.map(lic => (
              <div
                key={lic.categoryId}
                className="flex flex-col gap-1 px-3 py-2 card-r border"
                style={{ borderColor: `#${lic.color}55` }}
              >
                <span className="text-eyebrow" style={{ color: `#${lic.color}` }}>
                  {lic.categoryName}
                </span>
                <span className="text-mono-fluid text-on-surface">
                  {lic.groupName} · {lic.safetyRating.toFixed(2)} SR
                </span>
                <span className="text-small-fluid text-on-surface-variant">
                  {lic.iRating.toLocaleString()} iR
                </span>
              </div>
            ))
          )}
        </div>
      </div>

      {/* This season */}
      <div className="grid grid-kpi gap-fluid">
        <StatTile
          label="Official Sessions (Year)"
          value={d.thisYear.officialSessions.toLocaleString()}
        />
        <StatTile label="Official Wins (Year)" value={d.thisYear.officialWins.toLocaleString()} />
      </div>

      {/* Favorites */}
      {(d.favoriteCar || d.favoriteTrack) && (
        <div className="flex flex-wrap gap-fluid">
          {d.favoriteCar && (
            <div className="flex items-center gap-3 px-4 py-3 card-r card-shadow border border-line-2 bg-surface">
              <span className="material-symbols-outlined text-primary-container" aria-hidden="true">
                directions_car
              </span>
              <div>
                <p className="text-th text-on-surface-variant">Favorite Car</p>
                <p className="text-body-fluid text-on-surface font-medium">
                  {d.favoriteCar.carName}
                </p>
              </div>
            </div>
          )}
          {d.favoriteTrack && (
            <div className="flex items-center gap-3 px-4 py-3 card-r card-shadow border border-line-2 bg-surface">
              <span className="material-symbols-outlined text-primary-container" aria-hidden="true">
                stadium
              </span>
              <div>
                <p className="text-th text-on-surface-variant">Favorite Track</p>
                <p className="text-body-fluid text-on-surface font-medium">
                  {d.favoriteTrack.trackName}
                </p>
              </div>
            </div>
          )}
        </div>
      )}

      {/* Career by category */}
      {d.career.length > 0 && (
        <div className="card-r card-shadow border border-line-2 bg-surface overflow-hidden">
          <div className="card-hp scan-texture border-b border-line-2">
            <h3 className="text-section-head text-on-surface">Career by Category</h3>
          </div>
          <div className="overflow-x-auto">
            <table className="w-full border-collapse">
              <thead>
                <tr className="scan-texture border-b border-line-2">
                  <th className="th-p text-th text-on-surface-variant text-left">Category</th>
                  <th className="th-p text-th text-on-surface-variant text-right">Starts</th>
                  <th className="th-p text-th text-on-surface-variant text-right">Wins</th>
                  <th className="th-p text-th text-on-surface-variant text-right">Top 5</th>
                  <th className="th-p text-th text-on-surface-variant text-right">Poles</th>
                  <th className="th-p text-th text-on-surface-variant text-right">Win %</th>
                  <th className="th-p text-th text-on-surface-variant text-right">Laps Led</th>
                </tr>
              </thead>
              <tbody>
                {d.career.map(c => (
                  <tr key={c.categoryId} className="border-b border-line-2 last:border-b-0">
                    <td className="td-p text-body-fluid text-on-surface">{c.categoryName}</td>
                    <td className="td-p text-mono-fluid text-on-surface text-right">
                      {c.starts.toLocaleString()}
                    </td>
                    <td className="td-p text-mono-fluid text-primary-container text-right">
                      {c.wins.toLocaleString()}
                    </td>
                    <td className="td-p text-mono-fluid text-on-surface text-right">
                      {c.top5.toLocaleString()}
                    </td>
                    <td className="td-p text-mono-fluid text-on-surface text-right">
                      {c.poles.toLocaleString()}
                    </td>
                    <td className="td-p text-mono-fluid text-on-surface text-right">
                      {c.winPercentage.toFixed(1)}%
                    </td>
                    <td className="td-p text-mono-fluid text-on-surface-variant text-right">
                      {c.lapsLed.toLocaleString()}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}
    </div>
  );
}

const TROPHY_PREVIEW = 18;

// iRacing icon colors come as bare 6-digit hex; prefix '#' so CSS accepts them.
function normalizeColor(color: string | null): string | undefined {
  if (!color) return undefined;
  return /^[0-9a-fA-F]{6}$/.test(color) ? `#${color}` : color;
}

function AwardTile({ award }: { award: Award }) {
  return (
    <div
      className="flex flex-col items-center gap-1.5 w-[84px] text-center"
      title={award.description ? `${award.name} — ${award.description}` : award.name}
    >
      <div className="relative">
        {award.iconUrl ? (
          <img src={award.iconUrl} alt="" className="w-12 h-12 object-contain" loading="lazy" />
        ) : (
          <div
            className="w-12 h-12 card-r flex items-center justify-center text-section-head text-on-surface"
            style={{ backgroundColor: normalizeColor(award.iconBackgroundColor) }}
          >
            {award.name.charAt(0) || '?'}
          </div>
        )}
        {award.count > 1 && (
          <span className="absolute -top-1 -right-1 text-[10px] font-mono px-1 rounded bg-primary-container text-on-primary-container">
            ×{award.count}
          </span>
        )}
      </div>
      <span className="text-[11px] leading-tight text-on-surface-variant line-clamp-2">
        {award.name}
      </span>
    </div>
  );
}

function TrophyCase({ resource }: { resource: Resource<Achievements> }) {
  const [showAll, setShowAll] = useState(false);

  if (resource.status === 'loading') {
    return (
      <p className="text-body-fluid text-on-surface-variant animate-pulse">
        Loading trophy case&hellip;
      </p>
    );
  }
  // Hidden when unlinked/errored/empty — the driver-stats section already prompts linking.
  if (resource.status !== 'ok' || resource.data.awards.length === 0) return null;

  const { awards, awardCount } = resource.data;
  const shown = showAll ? awards : awards.slice(0, TROPHY_PREVIEW);
  return (
    <div className="card-r card-shadow border border-line-2 bg-surface overflow-hidden">
      <div className="card-hp scan-texture border-b border-line-2 flex items-center justify-between gap-3">
        <h3 className="text-section-head text-on-surface">Trophy Case</h3>
        <span className="text-small-fluid text-on-surface-variant">{awardCount} awards</span>
      </div>
      <div className="card-p">
        <div className="flex flex-wrap gap-fluid">
          {shown.map(a => (
            <AwardTile key={a.awardId} award={a} />
          ))}
        </div>
        {awards.length > TROPHY_PREVIEW && (
          <button
            onClick={() => setShowAll(v => !v)}
            className="btn-fluid-sm border border-line-2 text-on-surface-variant hover:text-on-surface hover:border-primary-container/40 transition-colors mt-fluid"
          >
            {showAll ? 'Show fewer' : `Show all ${awards.length}`}
          </button>
        )}
      </div>
    </div>
  );
}

function trackLabel(lap: UploadedBest): string {
  return lap.configName ? `${lap.trackName} — ${lap.configName}` : lap.trackName;
}

function SeriesCard({ s }: { s: Series }) {
  const active = s.currentWeekNumber != null;
  return (
    <div className="glass-panel p-5 rounded-xl border border-line-2 relative overflow-hidden hover:bg-surface-container-highest transition-all hover:border-primary-fixed-dim/30 group">
      <div className="absolute top-0 right-0 p-3 opacity-10 pointer-events-none">
        <span className="material-symbols-outlined text-[64px]" aria-hidden="true">
          sports_score
        </span>
      </div>
      <div className="flex justify-between items-start mb-4">
        <h4 className="font-body-lg font-bold text-on-surface pr-2 truncate">{s.name}</h4>
        {active && (
          <span className="bg-primary-container text-on-primary-container font-label-caps text-label-caps px-2 py-1 rounded shrink-0">
            WK {raceWeekNumber(s.currentWeekNumber!)}
          </span>
        )}
      </div>
      <div className="grid grid-cols-2 gap-4 mb-4">
        <div>
          <span className="block font-label-caps text-label-caps text-on-surface-variant mb-1">
            SEASON
          </span>
          <span className="font-data-md text-data-md text-on-surface">{s.seasonId}</span>
        </div>
        <div>
          <span className="block font-label-caps text-label-caps text-on-surface-variant mb-1">
            STATUS
          </span>
          <span
            className={`font-data-md text-data-md ${active ? 'text-primary-fixed-dim' : 'text-on-surface-variant'}`}
          >
            {active ? 'Active' : 'Off Season'}
          </span>
        </div>
      </div>
      {active ? (
        <Link
          to={`/series/${s.id}/weeks/${s.currentWeekNumber}`}
          className="w-full py-2 bg-surface-container-highest hover:bg-surface-container-high border border-line-2 rounded font-body-sm text-body-sm text-on-surface transition-colors flex items-center justify-center gap-2"
        >
          View Week Details
          <span className="material-symbols-outlined text-[16px]" aria-hidden="true">
            arrow_forward
          </span>
        </Link>
      ) : (
        <div className="w-full py-2 border border-line rounded font-body-sm text-body-sm text-on-surface-variant/40 flex items-center justify-center">
          No Active Week
        </div>
      )}
    </div>
  );
}

export default function ProfilePage() {
  const { user } = useAuth();
  const displayName = user?.displayName ?? 'Driver';
  const { enabled: showIracing } = useIracingSurface();

  const linked = !!user?.iRacingCustomerId;
  const lapsResource = useResource(signal => api.getMyUploadedBests(signal), [], {
    onError: { fallback: [] },
  });
  const seriesResource = useResource(signal => api.getSeries(signal), [showIracing], {
    enabled: showIracing,
    onError: { fallback: [] },
  });
  const statsResource = useResource(signal => api.getProfileStats(signal), [linked, showIracing], {
    enabled: linked && showIracing,
    fallbackMessage: 'Failed to load driver stats.',
  });
  const achievementsResource = useResource(
    signal => api.getAchievements(signal),
    [linked, showIracing],
    {
      enabled: linked && showIracing,
      onNotLinked: { fallback: { customerId: 0, awardCount: 0, awards: [] } },
      onError: { fallback: { customerId: 0, awardCount: 0, awards: [] } },
    }
  );

  const laps = lapsResource.status === 'ok' ? lapsResource.data : [];
  const series = seriesResource.status === 'ok' ? seriesResource.data : [];
  const lapsLoading = lapsResource.status === 'loading';
  const seriesLoading = seriesResource.status === 'loading';

  const totalLaps = laps.reduce((sum, l) => sum + l.lapCount, 0);
  const uniqueCars = new Set(laps.map(l => l.carId)).size;
  const bestLap =
    laps.length > 0
      ? laps.reduce((best, l) => (l.bestLapSeconds < best.bestLapSeconds ? l : best))
      : null;

  // Best lap per car, sorted fastest first
  const carBests = Object.values(
    laps.reduce<Record<number, UploadedBest>>((acc, lap) => {
      if (!acc[lap.carId] || lap.bestLapSeconds < acc[lap.carId].bestLapSeconds) {
        acc[lap.carId] = lap;
      }
      return acc;
    }, {})
  ).sort((a, b) => a.bestLapSeconds - b.bestLapSeconds);

  return (
    <main className="px-6 pt-8 pb-20 max-w-[1440px] mx-auto w-full flex flex-col gap-8">
      {/* Driver overview */}
      <div className="flex flex-col md:flex-row justify-between items-start md:items-center gap-6">
        <div className="flex items-center gap-6">
          <div className="relative">
            <div className="w-20 h-20 md:w-24 md:h-24 rounded-xl border-2 border-primary-fixed-dim p-1 bg-surface-container-highest flex items-center justify-center">
              <span
                className="material-symbols-outlined text-4xl text-primary-fixed-dim"
                aria-hidden="true"
              >
                person
              </span>
            </div>
            <div className="absolute -bottom-2 -right-2 bg-gold text-black font-label-caps text-label-caps px-2 py-1 rounded shadow-[0_0_10px] shadow-gold/50">
              PRO TIER
            </div>
          </div>
          <div>
            <h2 className="font-headline-md text-headline-md text-on-surface mb-1">
              {displayName}
            </h2>
            <div className="flex items-center gap-3 text-on-surface-variant font-body-sm flex-wrap">
              {user?.iRacingCustomerId && (
                <span className="flex items-center gap-1">
                  <span className="material-symbols-outlined text-[16px]" aria-hidden="true">
                    badge
                  </span>
                  ID {user.iRacingCustomerId}
                </span>
              )}
              <span className="flex items-center gap-1">
                <span className="material-symbols-outlined text-[16px]" aria-hidden="true">
                  timer
                </span>
                {lapsLoading ? '—' : `${totalLaps} laps recorded`}
              </span>
            </div>
          </div>
        </div>

        {/* Quick stat chips */}
        <div className="flex gap-4 w-full md:w-auto">
          <div className="glass-panel p-4 rounded-xl flex-1 md:w-36 flex flex-col items-center justify-center">
            <span className="font-label-caps text-label-caps text-on-surface-variant mb-1">
              CARS DRIVEN
            </span>
            <span className="font-data-lg text-data-lg text-primary-fixed-dim">
              {lapsLoading ? '—' : uniqueCars}
            </span>
          </div>
          <div className="glass-panel p-4 rounded-xl flex-1 md:w-36 flex flex-col items-center justify-center relative overflow-hidden">
            <div className="absolute inset-0 bg-gradient-to-br from-gold/5 to-transparent pointer-events-none" />
            <span className="font-label-caps text-label-caps text-on-surface-variant mb-1">
              UPLOADED BEST
            </span>
            <span className="font-data-lg text-data-lg text-gold">
              {lapsLoading ? '—' : bestLap ? formatLapTime(bestLap.bestLapSeconds) : '—'}
            </span>
          </div>
        </div>
      </div>

      {/* Driver stats — career, licenses, favorites */}
      {showIracing &&
        (linked ? (
          statsResource.status === 'ok' ? (
            <DriverStats data={statsResource.data} />
          ) : (
            <ResourceView
              resource={statsResource}
              notLinkedReason="Link your iRacing customer ID to see career stats, licenses, and favorites."
            />
          )
        ) : (
          <NotLinkedCard reason="Link your iRacing customer ID to see career stats, licenses, and favorites." />
        ))}

      {/* Trophy case — earned awards/achievements */}
      {showIracing && linked && <TrophyCase resource={achievementsResource} />}

      {/* Active series */}
      {showIracing && (
        <section>
          <h3 className="font-headline-sm text-headline-sm text-on-surface mb-4">Active Series</h3>
          {seriesLoading && (
            <p className="font-body-sm text-body-sm text-on-surface-variant animate-pulse">
              Loading&hellip;
            </p>
          )}
          {!seriesLoading && series.length === 0 && (
            <p className="font-body-sm text-body-sm text-on-surface-variant">No active series.</p>
          )}
          {!seriesLoading && series.length > 0 && (
            <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
              {series.slice(0, 6).map(s => (
                <SeriesCard key={s.id} s={s} />
              ))}
            </div>
          )}
        </section>
      )}

      {/* Car performance table */}
      <section>
        <div className="glass-panel rounded-xl overflow-hidden border border-primary-fixed-dim/20 shadow-[0_4px_30px_rgba(0,0,0,0.5)]">
          <div className="bg-surface-container-high px-6 py-4 border-b border-line-2 flex justify-between items-center">
            <h3 className="font-headline-sm text-headline-sm text-primary-fixed-dim flex items-center gap-2">
              <span className="material-symbols-outlined" aria-hidden="true">
                data_table
              </span>
              Uploaded Best by Car
            </h3>
            <Link
              to="/my-laps"
              className="font-label-caps text-label-caps text-on-surface-variant hover:text-primary-fixed-dim transition-colors flex items-center gap-1"
            >
              Full History
              <span className="material-symbols-outlined text-[14px]" aria-hidden="true">
                arrow_forward
              </span>
            </Link>
          </div>

          {lapsLoading && (
            <p className="px-6 py-8 font-body-sm text-body-sm text-on-surface-variant animate-pulse">
              Loading&hellip;
            </p>
          )}

          {!lapsLoading && carBests.length === 0 && (
            <div className="px-6 py-8 flex flex-col items-center gap-3 text-center">
              <span
                className="material-symbols-outlined text-3xl text-on-surface-variant"
                aria-hidden="true"
              >
                timer_off
              </span>
              <p className="font-body-sm text-body-sm text-on-surface-variant">
                No lap data yet.{' '}
                <Link
                  to="/telemetry"
                  className="text-primary-fixed-dim underline hover:text-primary transition-colors"
                >
                  Upload a telemetry file
                </Link>{' '}
                to populate your profile.
              </p>
            </div>
          )}

          {!lapsLoading && carBests.length > 0 && (
            <div className="overflow-x-auto">
              <table className="w-full text-left border-collapse">
                <thead>
                  <tr className="bg-surface-container/50 border-b border-line-2">
                    <th className="p-4 font-label-caps text-label-caps text-on-surface-variant">
                      CAR MODEL
                    </th>
                    <th className="p-4 font-label-caps text-label-caps text-on-surface-variant">
                      BEST TRACK
                    </th>
                    <th className="p-4 font-label-caps text-label-caps text-on-surface-variant text-right">
                      UPLOADED BEST
                    </th>
                    <th className="p-4 font-label-caps text-label-caps text-on-surface-variant text-right">
                      LAPS
                    </th>
                  </tr>
                </thead>
                <tbody>
                  {carBests.map(lap => (
                    <tr
                      key={lap.carId}
                      className="border-b border-surface-container-high hover:bg-surface-container-highest transition-colors last:border-b-0"
                    >
                      <td className="p-4">
                        <div className="flex items-center gap-3">
                          <div className="w-10 h-6 bg-surface-container-high rounded flex items-center justify-center shrink-0">
                            <span
                              className="material-symbols-outlined text-[12px] text-on-surface-variant"
                              aria-hidden="true"
                            >
                              directions_car
                            </span>
                          </div>
                          <span className="font-body-sm text-body-sm text-on-surface">
                            {lap.carName}
                          </span>
                        </div>
                      </td>
                      <td className="p-4 font-body-sm text-body-sm text-on-surface-variant">
                        {trackLabel(lap)}
                      </td>
                      <td className="p-4 font-data-md text-data-md text-primary-fixed-dim text-right">
                        {formatLapTime(lap.bestLapSeconds)}
                      </td>
                      <td className="p-4 font-data-md text-data-md text-on-surface-variant text-right">
                        {lap.lapCount}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </div>
      </section>
    </main>
  );
}
