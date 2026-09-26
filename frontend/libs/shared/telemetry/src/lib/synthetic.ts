/**
 * The User-Agent suffix `tools/TrafficSim` adds to every browser it drives (ADR
 * 0037). A module of its own so each adapter can read it without importing the
 * other adapter's SDK — the bundle comparison between them depends on it.
 */
export const SYNTHETIC_USER_AGENT = 'TenderoTrafficSim/';

export function isSynthetic(): boolean {
  return typeof navigator !== 'undefined' && navigator.userAgent.includes(SYNTHETIC_USER_AGENT);
}
