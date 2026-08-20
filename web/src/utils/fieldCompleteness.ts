/**
 * A race's classified field and the results ApexRacers can store are not always the same set.
 *
 * A Race Result names exactly one Driver, so two kinds of entry produce none: a **team entry**,
 * which raced under a team's identity rather than one Customer ID, and an **AI entry**, which holds
 * no iRacing racing identity at all. Both still occupied finishing positions, so a race with either
 * one has a stored field with gaps — positions that skip, and lead-lap and interval context computed
 * over fewer cars than actually raced.
 *
 * Counting them is what lets an incomplete field be told from a complete one. Both counts null means
 * the race was ingested before they were recorded, so nothing is claimed either way.
 */

function entries(count: number, noun: string): string {
  return `${count} ${noun} ${count === 1 ? 'entry' : 'entries'}`;
}

/**
 * The reader-facing note for a field with entries that could not be shown, or null when there is
 * nothing to say — the field is complete, or was never counted.
 */
export function unrepresentedEntriesNote(
  teamEntryCount: number | null | undefined,
  aiEntryCount: number | null | undefined
): string | null {
  const team = teamEntryCount ?? 0;
  const ai = aiEntryCount ?? 0;
  if (team <= 0 && ai <= 0) return null;

  const parts: string[] = [];
  if (team > 0) parts.push(entries(team, 'team'));
  if (ai > 0) parts.push(entries(ai, 'AI'));

  const verb = team + ai === 1 ? 'is' : 'are';
  return `${parts.join(' and ')} ${verb} not listed — a result names exactly one driver, so these held finishing positions with no result to show.`;
}
