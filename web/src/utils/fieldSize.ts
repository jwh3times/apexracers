export function fieldSizeMessage(fieldSize: number): string {
  const noun = fieldSize === 1 ? 'driver has' : 'drivers have';
  return `Only ${fieldSize.toLocaleString()} ${noun} set a time this week.`;
}
