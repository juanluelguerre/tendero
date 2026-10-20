/**
 * The cards the fake payment provider can keep (P15-4, `FakePaymentProvider`),
 * standing in for the provider's own card field. A real adapter replaces this
 * list with its field — an iframe the card number is typed into, which never
 * reaches the shop — and nothing else on the page changes.
 *
 * They are on the screen for the reason checkout's test cards are: the demo is
 * the deliverable, and each answer the wallet has to handle — kept, a second
 * brand, already expired, declined when charged, provider down — is one choice.
 */
export const KeepableCards = [
  'card-ok',
  'card-mastercard',
  'card-expired',
  'card-declined',
  'card-unreachable',
] as const;

export type KeepableCard = (typeof KeepableCards)[number];

/** A test card's translation key: `card-mastercard` → `wallet.cards.cardMastercard`. */
export function keepableCardKey(card: KeepableCard): string {
  return `wallet.cards.${card.replace(/-([a-z])/g, (_, letter: string) => letter.toUpperCase())}`;
}
