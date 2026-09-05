# Blog workflow

Every completed phase ships an article. English is the primary version
(audience reach); Spanish publishes on elguerre.com.

## Status (2026-09-01): drafts exist, nothing is published

**No Tendero article has been published anywhere.** Not on elguerre.com, not
scheduled, nothing. The blog badge in the README points at the author's existing
site, not at Tendero content.

What changed on 2026-09-01 is that the drafting started. `docs/blog/` now holds
an [`index.md`](blog/index.md) with the publication order and one file per
article — an opening piece about the project itself, eight drafted from shipped
features and real numbers, one half drafted, and outlines for the rest — two of
them writable today (16 and 17), the others waiting on work that does not exist
yet. The table in `blog/index.md` is the count that is maintained. Each drafted file is **nothing
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
5. Translate to Spanish; both versions link the repo tag.
6. Add the article link to README's blog section.
7. Update the row in `docs/blog/index.md` to published, with the URL.
8. Move whatever the notebook entry did not use back into it, or delete it if the
   article superseded it.
