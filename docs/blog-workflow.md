# Blog workflow

Every completed phase ships an article. English is the primary version
(audience reach); Spanish publishes on elguerre.com.

## Status (2026-09-01): nothing published yet

**No Tendero article exists anywhere.** Not a draft in a branch, not a scheduled
post — nothing. Publishing starts when several more features are in, and that
trigger belongs to the author; it is deliberately not "phase 1 is done, therefore
publish".

This is written down because the rest of this document is in the present tense
and reads like a running pipeline, which invites the assumption that phase 1's
two articles are out there somewhere. They are not. The blog badge in the README
points at the author's existing site, not at Tendero content.

So `docs/blog/notebook.md` is currently an **accumulating buffer, not a backlog
of drafts**. Its entries are raw material — dates, numbers, mistakes — and no
drafting has started. Keep writing entries as things happen: that is precisely
what makes the delay harmless, and reconstructing them later is what this
workflow exists to avoid.

Raw material lives in `docs/blog/notebook.md` and is written **as things
happen**, not reconstructed afterwards: incidents, numbers, decisions and
mistakes, filed under the article they feed. Reconstructing a month later
produces the tidy version of events, and the tidy version is exactly what makes
readers feel stupid for struggling.

Checklist per article:
1. Run the `blog-draft` skill on the finished feature branch.
2. Verify every code snippet compiles against the repo at that tag.
3. Include real numbers (NDCG, latency, cost per operation) — no hand-waving.
4. One diagram max, recreated in the article's own style.
5. Translate to Spanish; both versions link the repo tag.
6. Add the article link to README's blog section.
7. Move whatever the notebook entry did not use back into it, or delete it if
   the article superseded it.
