/**
 * Race weeks have two numberings and the app needs both.
 *
 * **Race Week Index** is iRacing's zero-based `race_week_num`. It is what persistence, the API
 * payloads, request parameters, cache keys and the `/series/:id/weeks/:n` route all carry, and none
 * of that changes — an index stays an index all the way to the wire.
 *
 * **Race Week Number** is the one-based ordinal drivers actually use: the first week of a season is
 * "Week 1", not "Week 0". Most screens used to interpolate the raw index into their label, so the
 * opening week of a season read as Week 0 everywhere except the qualifying standings selector,
 * which had quietly added one of its own.
 *
 * Converting at the point of display is what keeps those two facts from drifting: pass the index
 * you already have to `raceWeekLabel`, and keep passing that same index to the API and the router.
 */

/** Race Week Index (0-based, as stored and routed) → Race Week Number (1-based, as shown). */
export function raceWeekNumber(index: number): number {
  return index + 1;
}

/** The driver-facing label for a Race Week Index, e.g. index 0 → "Week 1". */
export function raceWeekLabel(index: number): string {
  return `Week ${raceWeekNumber(index)}`;
}
