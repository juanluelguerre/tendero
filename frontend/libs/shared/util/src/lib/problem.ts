/**
 * A refusal as the API writes every one (phase 13b, `docs/specs/wire-contract.md`;
 * RFC 9457): a **code** a client branches on, and the shop's sentence in the
 * language asked. Declared here by shape, because this library is below the one
 * that holds the generated types; the generated `Problem` is assignable to it.
 */
export interface ProblemBody {
  readonly code: string;
  readonly title: string;
  readonly status?: number;
  readonly detail?: string | null;
  /** The part of the request it is about, as a JSONPath. */
  readonly path?: string;
  /** The domain's own sentence, in English: for a log, not for a shopper's screen. */
  readonly reason?: string;
  /** A validation problem's map: a field to what is wrong with it. */
  readonly errors?: Readonly<Record<string, readonly string[]>>;
}

/** The body as a refusal, or null when it is anything else: a proxy's page, a string, nothing. */
export function problemOf(body: unknown): ProblemBody | null {
  if (!body || typeof body !== 'object') return null;
  const problem = body as Partial<ProblemBody>;
  return typeof problem.code === 'string' && typeof problem.title === 'string' ? (problem as ProblemBody) : null;
}

/**
 * The sentence a screen shows for a refusal — the shop's own, in the reader's
 * language, never one composed here: what is wrong with each field when the
 * request did not validate, else the detail, else the title. Empty when the
 * body is no refusal; then the screen says its own "could not be done".
 *
 * `withReason` adds the domain's own sentence, for a shopkeeper's screen: it is
 * in English and written for a log, and it names the rule that said no.
 */
export function refusalSentence(body: unknown, options: { withReason?: boolean } = {}): string {
  const problem = problemOf(body);
  if (!problem) return '';

  const fields = [...new Set(Object.values(problem.errors ?? {}).flat())];
  const sentence = fields.length > 0 ? fields.join(' ') : problem.detail || problem.title;

  return options.withReason && problem.reason ? `${sentence} ${problem.reason}` : sentence;
}
