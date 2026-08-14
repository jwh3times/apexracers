/**
 * Format a Field placement share as a "TOP X%" label.
 *
 * The share itself is computed server-side, because it cannot be recovered from a percentile
 * rank: the rank splits tied drivers and counts the subject in its own denominator, so two
 * different placements in two differently-sized Fields can produce the same rank.
 */
export function topShareLabel(topSharePercent: number): string {
  return `TOP ${topSharePercent}%`;
}

/** Format a percentile rank for display — one decimal, as the API returns it. */
export function percentileLabel(rank: number): string {
  return `${rank.toFixed(1)}%`;
}
