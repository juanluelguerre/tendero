import { Injectable } from '@angular/core';
import { noopAdapter } from './noop-adapter';

/**
 * Who acted in the browser. A person, or an agent running in the person's own
 * tab through WebMCP. External agents never reach this code — MCP and UCP are
 * server to server — and the server decides who acted for everything it serves
 * (ADR 0036): this is only the browser's half of that answer.
 */
export type ActorKind = 'human' | 'webmcp';

/**
 * Every event the frontends emit, by name, with its attributes.
 *
 * Typed so that an event nobody planned does not compile. `P9b-5` generates this
 * map from the tracking plan, where each event also says the question it answers
 * and the decision its value would change; until then it holds the one event
 * this phase needs.
 */
export interface TrackedEvents {
  /** A browser agent called one of the shop's WebMCP tools. */
  readonly webmcp_tool_invoked: {
    readonly tool: string;
    readonly outcome: 'answered' | 'refused' | 'failed';
  };
}

export type TrackedEventName = keyof TrackedEvents;

/** What an adapter is told when it starts. */
export interface TelemetryContext {
  /** `storefront` or `backoffice`: the service name every record carries. */
  readonly service: string;

  /** The anonymous, tab-scoped session every record carries. Never a user id. */
  readonly sessionId: string;

  /** Where to post, on the page's own origin: `/otlp`. The dev server proxies it. */
  readonly endpoint: string;
}

/**
 * The port (ADR 0036). OpenTelemetry JS is the first adapter, Grafana Faro the
 * second, and a no-op the third. Each adapter owns its automatic
 * instrumentation and its transport; what goes through here is only what OUR
 * code says — an event, an error, a span of its own, the route it is on.
 */
export interface TelemetryAdapter {
  readonly name: string;
  start(context: TelemetryContext): void;
  event(name: string, attributes: Readonly<Record<string, string | number | boolean>>, actor: ActorKind): void;
  error(error: unknown): void;
  span<T>(name: string, attributes: Readonly<Record<string, string | number | boolean>>, work: () => T): T;
  route(template: string): void;

  /** Sends whatever is batched. For the contract suite, and for a page about to go away. */
  flush?(): Promise<void>;
}

/**
 * What the application injects. It holds the current adapter — the no-op until
 * the real one has loaded — and it is where the promise *telemetry never breaks
 * the shop* is kept: every call into an adapter is guarded, and an adapter that
 * throws is replaced by the no-op for the rest of the page's life.
 */
@Injectable({ providedIn: 'root' })
export class Telemetry {
  private adapter: TelemetryAdapter = noopAdapter;

  /** The adapter in use: `noop` until one has loaded, and after one has failed. */
  get adapterName(): string {
    return this.adapter.name;
  }

  trackEvent<N extends TrackedEventName>(
    name: N,
    attributes: TrackedEvents[N],
    options: { readonly actor?: ActorKind } = {},
  ): void {
    this.guarded((adapter) => adapter.event(name, attributes, options.actor ?? 'human'));
  }

  recordError(error: unknown): void {
    this.guarded((adapter) => adapter.error(error));
  }

  /**
   * Runs `work` inside a span of our own — a checkout, say — so that the
   * requests it makes are its children.
   *
   * `work` runs exactly once whatever the adapter does. If the adapter fails
   * before running it, it runs without a span; if `work` itself throws, the
   * error is the application's and it propagates untouched. In a zoneless app
   * only the requests `work` starts before its first `await` are children:
   * there is no browser-native way to carry the context further (ADR 0036).
   */
  withSpan<T>(name: string, work: () => T, attributes: Readonly<Record<string, string | number | boolean>> = {}): T {
    let ran = false;
    const once = () => {
      ran = true;
      return work();
    };

    try {
      return this.adapter.span(name, attributes, once);
    } catch (error) {
      if (ran) throw error;
      this.fail(error);
      return work();
    }
  }

  /** The route template the page is on — `/:lang/p/:code`, never the URL with its ids. */
  route(template: string): void {
    this.guarded((adapter) => adapter.route(template));
  }

  /** Hands over to a loaded adapter, starting it first; a failed start leaves the no-op. */
  use(adapter: TelemetryAdapter, context: TelemetryContext): void {
    try {
      adapter.start(context);
      this.adapter = adapter;
    } catch (error) {
      this.fail(error);
    }
  }

  private guarded(call: (adapter: TelemetryAdapter) => void): void {
    try {
      call(this.adapter);
    } catch (error) {
      this.fail(error);
    }
  }

  private fail(error: unknown): void {
    this.adapter = noopAdapter;
    // The one place telemetry is allowed to speak: once, in the console, so a
    // developer sees why the dashboard went quiet.
    console.warn('Telemetry is off for this page: the adapter failed.', error);
  }
}
