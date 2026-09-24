# Blog workflow

Every completed phase ships an article, written and published **in English only**,
on elguerre.com. The social posts on LinkedIn and X are in English too.

## Status (2026-09-16): the first article is published

**Article 00 is live** on elguerre.com since 2026-09-16
([From CocktailDev to Tendero](https://elguerre.com/2026/09/16/tendero-ai-native-ecommerce-dotnet/)),
with its tag in the public repository. Every other article is still a draft or an
outline; `blog/index.md` keeps the count.

What changed on 2026-09-01 is that the drafting started. `docs/blog/` now holds
an [`index.md`](blog/index.md) with the publication order and one file per
article — an opening piece about the project itself, nine drafted from shipped
features and real numbers, one half drafted, and outlines for the rest, all of
them waiting on work that does not exist yet. The table in `blog/index.md` is the
count that is maintained. Each drafted file is **nothing
but the article**, so it can be pasted straight into WordPress; everything else
(status, slug, excerpt, tags, what a draft is still waiting on) lives in the
index.

The trigger to publish still belongs to the author, and is deliberately not
"phase 1 is done, therefore publish". Having a draft ready is not the same as
deciding it is time.

This distinction is written down because the rest of this document is in the
present tense and reads like a running pipeline. It is not one yet. What exists
is a queue with things in it.

## The two files, and why they are different

`docs/blog/notebook.md` is the **accumulating buffer**: raw material written
**as things happen**, not reconstructed afterwards. Incidents, numbers, decisions
and mistakes, filed under the article they feed. It keeps that job unchanged, and
it is still the most important file of the two — reconstructing a month later
produces the tidy version of events, and the tidy version is exactly what makes
readers feel stupid for struggling.

`docs/blog/NN-*.md` are the **drafts**. A draft should never contain a number the
notebook cannot account for. If a draft needs a figure that is not in the
notebook, that is a signal to go and measure it, not to estimate it.

Keep writing notebook entries as things happen. The delay before publishing is
harmless precisely because of that discipline, and it is what this workflow
exists to protect.

## Checklist per article

1. Run the `blog-draft` skill on the finished feature branch, or extend the
   existing draft in `docs/blog/`.
2. Verify every code snippet compiles against the repo at that tag.
3. Include real numbers (NDCG, latency, cost per operation) — no hand-waving.
4. One diagram max, recreated in the article's own style. The single
   exception is article 00, whose subject is the architecture itself and
   which carries four; diagram sources live in `docs/blog/assets/`.
5. Link the repo tag from the article.
6. **Publish the tag to the public repository before the post goes live**, with
   `docs/blog/publish-public.ps1 -Tag blog/NN-slug` from `tendero-workshop` (see
   *The public repository* in `CLAUDE.md`). The article links that tag, so the
   other order leaves a link that answers 404 until somebody notices. Only the
   tag's code travels: the article itself is never in the public repository.
7. **Send it to WordPress as a draft**, with the private tooling under
   `docs/blog/`. It never publishes: the author reviews the draft and presses
   Publish.
8. Add the article link to README's blog section.
9. Update the row in `docs/blog/index.md` to published, with the URL.
10. Move whatever the notebook entry did not use back into it, or delete it if the
    article superseded it.
11. **Delete the HTML once the article is live**: the article HTML generated for
    WordPress was never kept, and the article's diagram `.html` sources go too,
    leaving their `.svg` and `.png`. A published diagram is what was published.
12. **Redeploy the landing if it changed** (decided 2026-09-25). The landing is
    deployed to Netlify by hand, so nothing does it on its own: with every
    publication to the public repository, diff its inputs — `landing-page/`,
    `design/tokens.css`, `brand/` and `seed/images` — against the last deployed
    commit, and if anything moved run `node landing-page/tools/prepare.mjs` and
    drag `landing-page/dist` onto the site's *Deploys* page.
