import { TestBed } from '@angular/core/testing';
import { Telemetry } from '@tendero/shared-telemetry';
import { describe, expect, it, vi } from 'vitest';
import { AgentTools, MODEL_CONTEXT, refuse, say } from './agent-tools';
import { supportsModelContext, type ModelContext, type ToolDescriptor } from './model-context';

/**
 * The registration mechanism, and — mostly — the degradation.
 *
 * The degradation test is the one nobody writes, and it is the one that matters:
 * WebMCP is Chrome-only and under incubation, so on the overwhelming majority of
 * visits this code has to do NOTHING, visibly and reliably. A surface that half
 * initialises on a browser that cannot host it is worse than one that is absent.
 */
function aTool(name: string): ToolDescriptor {
  return {
    name,
    description: 'A tool.',
    inputSchema: { type: 'object', properties: {} },
    execute: () => Promise.resolve(say('ok')),
  };
}

/** The tools a fake browser was handed, in the order it was handed them. */
function registered(registerTool: { mock: { calls: unknown[][] } }): ToolDescriptor[] {
  return registerTool.mock.calls.map(([tool]) => tool as ToolDescriptor);
}

describe('the agent surface', () => {
  /**
   * The draft of 9 October 2026: `registerTool`, **one call a tool**, each with
   * a signal whose abort takes the tool back. The `provideContext({ tools })`
   * this was first written against is in no draft any more, so in a browser
   * that follows the specification nothing registered — and the spec passed,
   * because its fake had the old method too.
   */
  it('offers its tools to a browser that can host an agent', () => {
    const registerTool = vi.fn<ModelContext['registerTool']>(() => Promise.resolve());
    const context: ModelContext = { registerTool };

    TestBed.configureTestingModule({
      providers: [{ provide: MODEL_CONTEXT, useValue: context }],
    });

    const agent = TestBed.inject(AgentTools);
    expect(agent.available).toBe(true);

    agent.register([aTool('search_products'), aTool('get_cart')]);

    expect(registered(registerTool).map((tool) => tool.name)).toEqual(['search_products', 'get_cart']);
    expect(registerTool.mock.calls.map(([, options]) => options?.signal?.aborted)).toEqual([false, false]);
  });

  /**
   * What a tool says of itself reaches the browser as it was written: an
   * agent decides from `readOnlyHint` whether a call is safe to make unasked.
   */
  it('hands the browser what each tool says of itself', () => {
    const registerTool = vi.fn<ModelContext['registerTool']>(() => Promise.resolve());
    TestBed.configureTestingModule({ providers: [{ provide: MODEL_CONTEXT, useValue: { registerTool } }] });

    TestBed.inject(AgentTools).register([
      { ...aTool('get_cart'), annotations: { readOnlyHint: true } },
      aTool('add_to_cart'),
    ]);

    expect(registered(registerTool).map((tool) => tool.annotations)).toEqual([{ readOnlyHint: true }, undefined]);
  });

  /**
   * `registerTool` answers with a promise, and rejects it: a name already
   * taken, a page not allowed the feature. A rejection nobody handles is an
   * error in the shopper's console for a surface they never asked for, so it
   * is handled — and the tools that did register stay.
   */
  it('is not broken by a browser that refuses a tool', async () => {
    const registerTool = vi.fn((tool: ToolDescriptor) =>
      tool.name === 'taken' ? Promise.reject(new DOMException('taken', 'InvalidStateError')) : Promise.resolve(),
    );
    TestBed.configureTestingModule({ providers: [{ provide: MODEL_CONTEXT, useValue: { registerTool } }] });
    const unhandled = vi.fn();
    process.on('unhandledRejection', unhandled);

    TestBed.inject(AgentTools).register([aTool('taken'), aTool('get_cart')]);
    await new Promise((done) => setTimeout(done));
    process.off('unhandledRejection', unhandled);

    expect(registerTool).toHaveBeenCalledTimes(2);
    expect(unhandled).not.toHaveBeenCalled();
  });

  /** The app going away takes its tools with it: the signal each was registered under aborts. */
  it('takes its tools back when the app is destroyed', () => {
    const registerTool = vi.fn<ModelContext['registerTool']>(() => Promise.resolve());
    TestBed.configureTestingModule({ providers: [{ provide: MODEL_CONTEXT, useValue: { registerTool } }] });

    TestBed.inject(AgentTools).register([aTool('get_cart')]);
    TestBed.resetTestingModule();

    expect(registerTool.mock.calls[0][1]?.signal?.aborted).toBe(true);
  });

  /**
   * The whole of `P6-5`. On a browser with no `document.modelContext` — which
   * is every browser but one — registering does nothing and throws nothing.
   *
   * jsdom has no `modelContext`, so this is not a simulated absence: it is the
   * real one.
   */
  it('does nothing at all on a browser that cannot host one', () => {
    TestBed.configureTestingModule({});

    const agent = TestBed.inject(AgentTools);

    expect(supportsModelContext()).toBe(false);
    expect(agent.available).toBe(false);
    expect(() => agent.register([aTool('search_products')])).not.toThrow();
  });

  /**
   * Angular builds the shell once per app; a hot reload does not respect that,
   * and a double registration is a browser holding two identical tool sets.
   */
  it('registers once however many times it is asked', () => {
    const registerTool = vi.fn<ModelContext['registerTool']>(() => Promise.resolve());

    TestBed.configureTestingModule({
      providers: [{ provide: MODEL_CONTEXT, useValue: { registerTool } }],
    });

    const agent = TestBed.inject(AgentTools);
    agent.register([aTool('one')]);
    agent.register([aTool('two')]);

    expect(registered(registerTool).map((tool) => tool.name)).toEqual(['one']);
  });

  /**
   * A refusal has to survive the trip. A model reading "no such SKU" as prose
   * may retry the same call; reading it as an error will not — and a shop whose
   * whole argument is what it says NO to cannot afford its refusals to arrive
   * looking like answers.
   */
  it('marks a refusal as one', () => {
    expect(say('here you go').isError).toBeUndefined();
    expect(refuse('no such SKU').isError).toBe(true);
    expect(refuse('no such SKU').content[0].text).toBe('no such SKU');
  });

  /**
   * The browser's half of telling a person from an agent (ADR 0036): every call
   * an agent makes through WebMCP is an event marked webmcp, and a refusal is
   * counted as a refusal rather than as an answer.
   */
  it('reports every agent call, and a refusal as one', async () => {
    const registerTool = vi.fn<ModelContext['registerTool']>(() => Promise.resolve());
    TestBed.configureTestingModule({ providers: [{ provide: MODEL_CONTEXT, useValue: { registerTool } }] });
    const trackEvent = vi.spyOn(TestBed.inject(Telemetry), 'trackEvent');

    const declines: ToolDescriptor = { ...aTool('add_to_cart'), execute: () => Promise.resolve(refuse('no such SKU')) };
    TestBed.inject(AgentTools).register([aTool('search_products'), declines]);
    const [search, add] = registered(registerTool);
    const options = { signal: new AbortController().signal };

    await search.execute({}, options);
    await add.execute({}, options);

    expect(trackEvent.mock.calls).toEqual([
      ['webmcp_tool_invoked', { tool: 'search_products', outcome: 'answered' }, { actor: 'webmcp' }],
      ['webmcp_tool_invoked', { tool: 'add_to_cart', outcome: 'refused' }, { actor: 'webmcp' }],
    ]);
  });

  /**
   * By the method, not by the name: a browser still on the first shape has a
   * `modelContext` and no `registerTool`, and calling it there would throw in
   * the shell's constructor.
   */
  it('detects the capability from the document it is given', () => {
    expect(supportsModelContext({} as Document)).toBe(false);
    expect(supportsModelContext({ modelContext: { provideContext: () => undefined } } as unknown as Document)).toBe(
      false,
    );
    expect(
      supportsModelContext({ modelContext: { registerTool: () => Promise.resolve() } } as unknown as Document),
    ).toBe(true);
  });
});
