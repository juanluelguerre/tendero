/**
 * The header's search form as a tool, declared in the markup.
 *
 * **Experimental, and deliberately marked so.** This is WebMCP's *declarative*
 * proposal — a `<form>` with `toolname` and `tooldescription` becomes a tool,
 * its controls describe themselves with `toolparamdescription`, and
 * `toolautosubmit` lets the agent send it. It is **not in the specification**
 * (the Community Group's `declarative-api-explainer.md`; the README says
 * *being explored*), and it is in the browser all the same: tried in Edge 155
 * behind `#enable-webmcp-testing` on 2026-10-13. It may change or go. Every
 * other tool of the shop is registered the way the draft does define
 * (`storefront-tools.ts`).
 *
 * It is here because it is the other half of the comparison, and because it
 * does something the imperative `search_products` does not: that one hands the
 * agent a list and leaves the page alone; this one **moves the shopper's page**
 * to the results, as typing in the box and pressing Enter does. A browser that
 * knows nothing of it ignores the attributes, and the form is the form it was.
 */
export const SearchFormTool = {
  name: 'show_search_results',
  description:
    'Shows the shopper the results page for what they are looking for: the page ' +
    'they are on changes, as if they had typed it in the search box. To read the ' +
    'products yourself and leave the page where it is, use search_products.',
  /** The form's one field, as the agent is told it. */
  query: 'What to look for, in the language the page is in. At least two characters.',
} as const;

/** What the proposal adds to a form's `submit` event, where the browser has it. */
interface AgentSubmit {
  readonly agentInvoked?: boolean;
  respondWith?(answer: Promise<unknown>): void;
}

/**
 * Answers the agent that sent the form, when one did: what the shopper is
 * looking at once the page has got there, or why nothing moved. True when the
 * submission was the agent's and has been dealt with; false for a person —
 * and for every browser without the proposal — so the caller searches as it
 * always did.
 */
export function answerAgent(event: SubmitEvent, words: string, go: (query: string) => Promise<boolean>): boolean {
  const sent = event as SubmitEvent & AgentSubmit;
  if (!sent.agentInvoked || typeof sent.respondWith !== 'function') return false;

  const query = words.trim();

  // The floor the search box and `search_products` keep: one letter matches
  // so much that the page it led to would say nothing.
  sent.respondWith(
    query.length < 2
      ? Promise.resolve('A query needs at least two characters.')
      : go(query).then(() => `The shopper is now looking at the results for "${query}".`),
  );

  return true;
}
