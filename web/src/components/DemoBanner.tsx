import { useFeatureFlag } from '../context/FeatureFlagContext';

// Persistent, non-dismissible banner shown on every gated page while iracing-demo is
// on, so synthetic figures can never be mistaken for real iRacing results.
export default function DemoBanner() {
  const demo = useFeatureFlag('iracing-demo');
  if (!demo) return null;

  return (
    <div
      role="status"
      className="border-b border-primary-container/40 bg-primary-container/10 text-small-fluid text-on-surface card-hp flex items-center gap-2"
    >
      <span aria-hidden="true">🧪</span>
      <span>
        <strong className="text-primary-container">Demo data</strong> — figures are synthetic, not
        real iRacing results.
      </span>
    </div>
  );
}
