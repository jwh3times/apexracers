import { api, type CategoryProgression } from '../../services/api';
import Sparkline from '../../components/Sparkline';
import ResourceView from '../../components/ResourceView';
import { useResource } from '../../hooks/useResource';

function Kpi({
  label,
  value,
  valueClassName,
}: {
  label: string;
  value: React.ReactNode;
  valueClassName?: string;
}) {
  return (
    <div className="kpi-p card-r border border-line-2 bg-surface-container">
      <p className="text-th text-on-surface-variant mb-1">{label}</p>
      <p className={`text-kpi-value ${valueClassName ?? 'text-on-surface'}`}>{value}</p>
    </div>
  );
}

function CategoryCard({ cat }: { cat: CategoryProgression }) {
  const series = cat.iRatingHistory.map(p => p.value);
  const delta = series.length >= 2 ? series[series.length - 1] - series[0] : null;

  return (
    <div className="card-r card-shadow border border-line-2 bg-surface overflow-hidden">
      <div className="card-hp scan-texture border-b border-line-2 flex items-center justify-between gap-3">
        <h3 className="text-section-head text-on-surface">{cat.categoryName}</h3>
        <span
          className="text-eyebrow px-2 py-1 rounded-[7px] border"
          style={{ color: `#${cat.color}`, borderColor: `#${cat.color}55` }}
        >
          {cat.groupName} · L{cat.licenseLevel}
        </span>
      </div>

      <div className="card-p flex flex-col gap-fluid">
        <div className="grid grid-kpi gap-fluid">
          <Kpi label="iRating" value={cat.iRating.toLocaleString()} />
          <Kpi
            label="Safety Rating"
            value={cat.safetyRating.toFixed(2)}
            valueClassName="text-primary-container"
          />
          <Kpi label="CPI" value={cat.cpi.toFixed(1)} />
          <Kpi label="TT Rating" value={cat.ttRating.toLocaleString()} />
        </div>

        {series.length >= 2 ? (
          <div className="flex flex-col gap-2">
            <div className="flex items-baseline justify-between">
              <span className="text-th text-on-surface-variant">
                iRating history ({series.length})
              </span>
              {delta !== null && (
                <span
                  className={`text-mono-fluid font-semibold ${
                    delta >= 0 ? 'text-primary-container' : 'text-error'
                  }`}
                >
                  {`${delta >= 0 ? '+' : ''}${delta.toLocaleString()} iR`}
                </span>
              )}
            </div>
            <div className="w-full">
              <Sparkline data={series} h={64} />
            </div>
          </div>
        ) : (
          <p className="text-small-fluid text-on-surface-variant">
            Not enough history to chart yet.
          </p>
        )}
      </div>
    </div>
  );
}

export default function ProgressionPage() {
  const resource = useResource(signal => api.getProgression(signal), [], {
    fallbackMessage: 'Failed to load progression.',
  });

  return (
    <main className="page-wrap">
      <div className="mb-6">
        <p className="text-eyebrow text-primary-container">PROGRESSION</p>
        <h1 className="text-page-title text-on-surface mt-2">iRating &amp; Safety Rating</h1>
      </div>

      <ResourceView
        resource={resource}
        notLinkedReason="Link your iRacing account to see your progression."
      />

      {resource.status === 'ok' && resource.data.categories.length === 0 && (
        <p className="text-body-fluid text-on-surface-variant">
          No license categories found for this account yet.
        </p>
      )}

      {resource.status === 'ok' && resource.data.categories.length > 0 && (
        <div className="grid grid-cards gap-fluid">
          {resource.data.categories.map(cat => (
            <CategoryCard key={cat.categoryId} cat={cat} />
          ))}
        </div>
      )}
    </main>
  );
}
