import { describe, expect, it, vi } from 'vitest';
import { answerAgent, SearchFormTool } from './search-form-tool';

/**
 * The header's search form, offered to an agent as a tool by attributes
 * alone — WebMCP's *declarative* proposal, which is in the browser and not in
 * the specification. What is ours to get right is the one thing attributes
 * cannot say: what the agent is answered when it sends the form.
 */
function submitted(by: { agent: boolean }) {
  const respondWith = vi.fn<(answer: Promise<unknown>) => void>();
  return { event: { agentInvoked: by.agent, respondWith } as unknown as SubmitEvent, respondWith };
}

describe('the search form as a tool', () => {
  it('tells the agent what the shopper is now looking at, once the page has changed', async () => {
    const { event, respondWith } = submitted({ agent: true });
    let arrive!: (went: boolean) => void;
    const navigated = new Promise<boolean>((resolve) => (arrive = resolve));

    expect(answerAgent(event, 'sartén', () => navigated)).toBe(true);

    const answer = respondWith.mock.calls[0][0];
    let said: unknown;
    void answer.then((text) => (said = text));
    await Promise.resolve();
    expect(said).toBeUndefined();

    arrive(true);
    expect(await answer).toBe('The shopper is now looking at the results for "sartén".');
  });

  /** The same floor the search box and `search_products` keep: one letter is noise. */
  it('refuses a query too short to mean anything, and does not move the page', async () => {
    const { event, respondWith } = submitted({ agent: true });
    const go = vi.fn(() => Promise.resolve(true));

    expect(answerAgent(event, ' a ', go)).toBe(true);

    expect(go).not.toHaveBeenCalled();
    expect(await respondWith.mock.calls[0][0]).toBe('A query needs at least two characters.');
  });

  /** A person pressing Enter is not an agent: nothing is answered, and the caller searches as it always did. */
  it('leaves a person alone', () => {
    const { event, respondWith } = submitted({ agent: false });
    const go = vi.fn(() => Promise.resolve(true));

    expect(answerAgent(event, 'sartén', go)).toBe(false);
    expect(respondWith).not.toHaveBeenCalled();
    expect(go).not.toHaveBeenCalled();
  });

  /** A browser with no declarative WebMCP has no such members on the event, and nothing breaks. */
  it('does nothing in a browser that knows nothing of it', () => {
    expect(answerAgent(new Event('submit') as SubmitEvent, 'sartén', () => Promise.resolve(true))).toBe(false);
  });

  it('says what it does to the page, and that another tool only reads', () => {
    expect(SearchFormTool.name).toBe('show_search_results');
    expect(SearchFormTool.description).toContain('search_products');
  });
});
