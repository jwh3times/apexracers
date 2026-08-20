/**
 * A Race Session divides into Splits, and ApexRacers carries a Split's position two ways.
 *
 * **Split Index** is the zero-based rank stored and sent over the wire: index 0 is the strongest
 * Split, ordered by Strength of Field descending. It is null whenever iRacing did not establish a
 * position — an unknown Split is deliberately not index 0, because "we don't know" and "the
 * strongest Split" are different facts and conflating them is what issue #192 fixed.
 *
 * **Split Number** is the one-based ordinal a reader expects in a label: the strongest Split reads
 * as "Split 1 of 3", not "Split 0 of 3". Convert at the point of display, the way `raceWeek` does,
 * and keep passing the index itself to the API.
 */

/** Split Index (0-based, as stored and sent) → Split Number (1-based, as shown). */
export function splitNumber(index: number): number {
  return index + 1;
}

/**
 * The reader-facing label for a Split's position, e.g. index 0 of 3 → "1 of 3". Returns null when
 * either half is unknown, so a caller can omit the label rather than render a half-known position.
 */
export function splitLabel(
  index: number | null | undefined,
  count: number | null | undefined
): string | null {
  if (index == null || count == null) return null;
  return `${splitNumber(index)} of ${count}`;
}
