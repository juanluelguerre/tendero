import type { TelemetryAdapter } from './telemetry';

/**
 * No telemetry at all, and it is a real adapter rather than an absence: the one
 * in use before the configured adapter has loaded, when the configuration says
 * `noop`, in every unit test, and after any adapter has failed. It runs the
 * work it is handed and does nothing else.
 */
export const noopAdapter: TelemetryAdapter = {
  name: 'noop',
  start: () => undefined,
  event: () => undefined,
  error: () => undefined,
  span: (_name, _attributes, work) => work(),
  route: () => undefined,
};
