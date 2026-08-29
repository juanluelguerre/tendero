# Blog workflow

Every completed phase ships an article. English is the primary version
(audience reach); Spanish publishes on elguerre.com.

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
