import { useParams } from 'react-router-dom';

export default function WeekDetailPage() {
  const { seriesId, weekId } = useParams<{ seriesId: string; weekId: string }>();

  // TODO: Call api.getCarsForWeek(Number(seriesId), Number(weekId)) on mount
  // TODO: Render a table of WeekCar rows: car name, entry count, fastest lap, median lap
  // TODO: Each row links to a percentile lookup for the authenticated user

  return (
    <main>
      <h1>Week Detail</h1>
      <p>
        Series <strong>{seriesId}</strong> &mdash; Week <strong>{weekId}</strong>
      </p>
      <p>
        <em>Car lap time stats — not yet implemented.</em>
      </p>
    </main>
  );
}
