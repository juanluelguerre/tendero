# The redesign's walkthrough — the kit

**For**: R-0's baseline and R-6's comparison ([`redesign.md`](redesign.md), *Decisions* 3)
**Participants**: the author (P1) and one person who has never seen Tendero (P2),
ideally someone who has used a shop's admin
**Page**: [`tools/walkthrough/index.html`](../../tools/walkthrough/index.html) —
open it in a browser from disk. Nothing leaves the machine.

The same kit runs twice: once on today's interface (the baseline) and once after
R-4 and R-5 (R-6). The tasks, their wording and their order do not change between
the two, or the comparison means nothing.

## What is the moderator's, and what is the participant's

| Who | Does |
|---|---|
| Moderator | prepares the shop (below), reads each task aloud, starts and stops the timer, notes success, errors and interactions, asks SEQ after each task and SUS once, exports the results |
| Participant | does the tasks, thinking aloud, and answers the questions |

When the author is the participant, the timer and notes can be kept by the author
between tasks: read the task, start, do it, stop, then fill in the rest. It is
less clean than having a moderator, and the article says so.

## Before the session

1. **The shop.** Start the stack (`dotnet run --project src/AppHost`), start
   `traffic-sim` from the Aspire dashboard for a few minutes so there are orders,
   returns and claims to work on, and **stop it**: its shopkeeper publishes every
   draft and ships what it finds.
2. **Pre-flight**, one command from `frontend/`. Each job needs something to act
   on, and a task with nothing to do measures nothing:

   ```bash
   WALKTHROUGH_PREPARE=1 npx playwright test -c apps/backoffice-e2e/playwright.config.ts walkthrough-prepare
   ```

   It signs in as the shopkeeper and uses the shop's own operations:
   - B1 — withdraws active products back to the review queue until there are
     three drafts (`WALKTHROUGH_DRAFTS`), never a backpack, which S1 needs.
   - B5 — reinstates a suspended agent, or registers the development issuer's
     agent when none is registered.
   - It then prints one line per job: the drafts, the claims waiting, the
     confirmed orders with the first one's number for B3's card, the open
     returns, and the active agent's name for B5's card. It fails if any job
     has nothing, and running it twice changes nothing more.

   What stays by hand:
   - S2 — choose a product and write a description of it that does not use its
     name (for example "something to keep coffee hot on the way to work").
   - S3 — choose a product with more than one variant and open its page.
   - S4 — write a made-up address on the card. Payment is the fake provider.
3. **Recording, if the participant agrees.** A screen recording makes counting
   interactions exact: count clicks, taps and filled fields afterwards, then
   delete the recording. Without one, the moderator counts live.
4. **Widths.** The participant does all nine tasks at their **first width**, then
   the three where width decides the most (B3, S1, S4) at the second. **P1 starts
   at the desktop (1440 px), P2 at the phone (390 px, the browser's device
   toolbar)**, so learning does not always favour the same width.

## Consent (read aloud, then the page records that it was given)

> **es** — Gracias por ayudar. Vamos a probar la interfaz, no a ti: si algo cuesta,
> es un fallo del diseño. Te pediré que hagas nueve tareas en una tienda de
> laboratorio, pensando en voz alta, y que valores cada una. Anoto el tiempo, si
> se completó y los pasos. Tus resultados se guardan con un código (P2), nunca con
> tu nombre, y pueden aparecer, agregados, en un artículo sobre el rediseño. Si
> aceptas, grabo la pantalla solo para contar pasos, y la borro después. Puedes
> parar cuando quieras sin dar explicaciones. ¿Te parece bien?
>
> **en** — Thank you for helping. We are testing the interface, not you: if
> something is hard, that is the design's fault. I'll ask you to do nine tasks in a
> laboratory shop, thinking aloud, and to rate each one. I note the time, whether
> it was completed and the steps. Your results are kept under a code (P2), never
> your name, and may appear, aggregated, in an article about the redesign. If you
> agree, I record the screen only to count steps, and delete it afterwards. You can
> stop whenever you like without giving a reason. Is that all right?

## The tasks, as read to the participant

The wording avoids the interface's own labels ("publish", "suspend", "review
queue"), so the task does not tell the participant where to click. Order:
backoffice, SUS, storefront, then the second width.

| # | es | en | Starts at | Done when |
|---|---|---|---|---|
| B1 | Hay un producto pendiente de revisar antes de ponerse a la venta. Compruébalo y ponlo a la venta. | A product is waiting to be checked before it goes on sale. Check it and put it on sale. | backoffice home | the product is Active |
| B2 | El sistema ha propuesto datos sobre los productos que alguien tiene que validar. Mira el que ofrece menos confianza y decide si es correcto. | The system has proposed facts about products that somebody has to validate. Look at the one it is least sure of and decide whether it is right. | backoffice home | approved or rejected |
| B3 | El pedido de la tarjeta ya está empaquetado. Déjalo registrado como enviado. | The order on the card is packed. Record it as shipped. | backoffice home | Shipped |
| B4 | Un cliente ha pedido devolver algo. Decide esa devolución con tu criterio. | A customer has asked to return something. Decide that return as you see fit. | backoffice home | approved or rejected |
| B5 | El agente de la tarjeta está haciendo pedidos raros. Impide que siga comprando y deja anotado por qué. | The agent on the card is placing strange orders. Stop it from buying, and note why. | backoffice home | Suspended, with a reason |
| S1 | Buscas una mochila y quieres ver todas las que tiene la tienda. | You want a backpack and would like to see every one the shop has. | first width: home · second width: the cart | the category's listing is on screen |
| S2 | Buscas lo que describe la tarjeta, pero no sabes cómo lo llama la tienda. Encuéntralo y abre su ficha. | You are looking for what the card describes, but you do not know what the shop calls it. Find it and open its page. | home | its page is open |
| S3 | Añade este producto al carrito, en la versión que prefieras. | Add this product to the cart, in whichever version you prefer. | the product chosen in pre-flight | the cart shows it |
| S4 | Compra lo que hay en el carrito sin crear una cuenta. Usa la dirección de la tarjeta. | Buy what is in the cart without creating an account. Use the address on the card. | the cart | the order page is shown |

**Success** is one of: *done*, *done with help* (the moderator said anything that
pointed the way), or *not done* (gave up, or three minutes passed). **An error** is
a wrong turn the participant had to undo: a wrong tab, a wrong button, a
validation message.

## The questions

**SEQ**, after every task (Sauro and Dumas, 2009), 1 to 7:

> **es** — En conjunto, ¿qué tan fácil o difícil te ha resultado esta tarea?
> (1 muy difícil · 7 muy fácil)
>
> **en** — Overall, how difficult or easy was this task to complete?
> (1 very difficult · 7 very easy)

**SUS**, once, after the five backoffice tasks (Brooke, 1996), each item 1
(strongly disagree) to 5 (strongly agree). The English is the standard wording
with "system" replaced by "backoffice", which is the usual practice. The Spanish is
our translation, not a validated one, and the article says so.

| # | en | es |
|---|---|---|
| 1 | I think that I would like to use this backoffice frequently. | Creo que me gustaría usar este backoffice con frecuencia. |
| 2 | I found the backoffice unnecessarily complex. | El backoffice me ha parecido innecesariamente complejo. |
| 3 | I thought the backoffice was easy to use. | Me ha parecido fácil de usar. |
| 4 | I think that I would need the support of a technical person to be able to use this backoffice. | Creo que necesitaría la ayuda de un técnico para poder usarlo. |
| 5 | I found the various functions in this backoffice were well integrated. | Sus distintas funciones me han parecido bien integradas. |
| 6 | I thought there was too much inconsistency in this backoffice. | Me ha parecido demasiado inconsistente. |
| 7 | I would imagine that most people would learn to use this backoffice very quickly. | Imagino que la mayoría de la gente aprendería a usarlo muy rápido. |
| 8 | I found the backoffice very cumbersome to use. | Me ha resultado muy engorroso de usar. |
| 9 | I felt very confident using the backoffice. | Me he sentido muy seguro al usarlo. |
| 10 | I needed to learn a lot of things before I could get going with this backoffice. | He necesitado aprender muchas cosas antes de poder manejarlo. |

The score: odd items give *answer − 1*, even items *5 − answer*; the sum times 2.5
is 0–100. 68 is the usual average. The page computes it.

## After the session

1. Export the results from the page (a `.json` named by participant and date) and
   put it under `docs/blog/reports/<date>-redesign-baseline/walkthrough/`, or
   `<date>-redesign-after/` at R-6. It holds the code, never a name.
2. If there was a recording, count the interactions from it into the page before
   exporting, then delete the recording.
3. The analysis sets each task beside the baseline's minimum interaction cost
   (`redesign.md`, *The baseline's automated half*). The gap between what the
   interface allows and what a person did is the finding. At n = 2 the numbers
   describe two sessions, not a population.
