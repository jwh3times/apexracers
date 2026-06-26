/**
 * Format a percentile rank (where higher is better, e.g. 96 = faster than 96% of the
 * field) as a "TOP X%" label. Floors at TOP 1% so a rank of 100 never reads "TOP 0%".
 */
export function topPercentLabel(rank: number): string {
  return `TOP ${Math.max(1, Math.ceil(100 - rank))}%`;
}
