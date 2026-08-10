/** Convert a higher-is-better percentile rank to the displayed lower-is-better TOP value. */
export function toTopPercent(rank: number): number {
  return Math.max(1, Math.ceil(100 - rank));
}

/** Format a percentile rank as a "TOP X%" label. */
export function topPercentLabel(rank: number): string {
  return `TOP ${toTopPercent(rank)}%`;
}
