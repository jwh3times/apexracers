export function formatLapTime(seconds: number): string {
  const mins = Math.floor(seconds / 60);
  const secs = (seconds % 60).toFixed(3).padStart(6, '0');
  return `${mins}:${secs}`;
}
