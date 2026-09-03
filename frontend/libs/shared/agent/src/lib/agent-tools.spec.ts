import { TestBed } from '@angular/core/testing';
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

describe('the agent surface', () => {
  it('offers its tools to a browser that can host an agent', () => {
    const provideContext = vi.fn();
    const context: ModelContext = { provideContext };

    TestBed.configureTestingModule({
      providers: [{ provide: MODEL_CONTEXT, useValue: context }],
    });

    const agent = TestBed.inject(AgentTools);
    expect(agent.available).toBe(true);

    agent.register([aTool('search_products'), aTool('get_cart')]);

    expect(provideContext).toHaveBeenCalledTimes(1);
    expect(provideContext.mock.calls[0][0].tools.map((t: ToolDescriptor) => t.name)).toEqual([
      'search_products',
      'get_cart',
    ]);
  });

  /**
   * The whole of `P6-5`. On a browser with no `navigator.modelContext` — which
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
    const provideContext = vi.fn();

    TestBed.configureTestingModule({
      providers: [{ provide: MODEL_CONTEXT, useValue: { provideContext } }],
    });

    const agent = TestBed.inject(AgentTools);
    agent.register([aTool('one')]);
    agent.register([aTool('two')]);

    expect(provideContext).toHaveBeenCalledTimes(1);
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

  it('detects the capability from the navigator it is given', () => {
    expect(supportsModelContext({} as Navigator)).toBe(false);
    expect(
      supportsModelContext({ modelContext: { provideContext: () => undefined } } as Navigator),
    ).toBe(true);
  });
});
