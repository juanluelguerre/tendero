/** The steps a person says a round number in: 1, 2, 2.5, 5, and on by tens. */
const STEPS = [1, 2, 2.5, 5, 10];

/** The smallest round number at or above `max`; 1 for a chart with nothing on it. */
export function niceMax(max: number): number {
  if (!(max > 0)) return 1;
  const magnitude = 10 ** Math.floor(Math.log10(max));
  const step = STEPS.find((candidate) => candidate * magnitude >= max) ?? 10;
  return step * magnitude;
}

/** `count` equal steps from zero to `max`, both ends included. */
export function ticks(max: number, count = 4): number[] {
  return Array.from({ length: count + 1 }, (_, index) => (max * index) / count);
}
