---
name: phase-close
description: Close a phase of Tendero — Stryker over every project the phase touched, every survivor classified, the baselines ratcheted, the reports filed, the board and the book's map updated (CLAUDE.md, "A phase is closed"). Launched by the author only.
disable-model-invocation: true
argument-hint: "<phase> (e.g. 13c) — and the projects it touched, if not obvious from git"
---

# Closing a phase

CLAUDE.md, *Definition of done*: **a phase is closed** when Stryker has run over
the projects it touched, no `of covered` score fell below its baseline, every
survivor is classified in `docs/testing.md`, and the phase's material is filed
into the book (`docs/book/material-map.md`). This is that list, in the order
that works, with the scripts that ran phase 13b's close. Reply to the author in
Spanish; everything written to the repository is in English.

## 0. Before anything

- **The stack is stopped**, and it is one this session started, or the author
  stopped it. It holds `src/*/bin`, and a mutation run with it up measures
  nothing. Never stop the author's own stack: ask.
- **Nothing under `src/` is edited while Stryker runs.** It reads the tree
  project by project, so an edit lands in the middle of a run. Tests and docs
  can be written meanwhile.
- `git branch --show-current` is the phase's branch.

## 1. What the phase touched

`git diff --stat <previous phase tag>...HEAD -- src/`, grouped by project. For
each project, decide:

- **Whole**: the phase changed it throughout. This run can set a new baseline.
- **Scoped** (`-m "File.cs"`): the phase touched a few files. This sets no
  baseline, because a score over twenty mutants is not the project's.
- **None**: no `stryker-config.json` (`src/Mcp`, `src/ServiceDefaults`, which
  `Api.Tests` reaches only through `src/Api`). The phase's code there is proven
  by the hand mutations in its commits. Say so in the write-up.

Write the plan, one line a project, to a file in the scratchpad (see the
header of `stryker-run.sh`). Check it first with a dry run:

```bash
STRYKER_DRY=1 bash .claude/skills/phase-close/stryker-run.sh <plan> <scratchpad>/stryker
```

## 2. Run it

**In parallel, since phase 14's close** (commit what the run must see first: each
share runs from `HEAD`):

```bash
node .claude/skills/phase-close/stryker-parallel.mjs src/<Project> --shards 8            # whole
node .claude/skills/phase-close/stryker-parallel.mjs src/<Project> --shards 2 --mutate "A.cs,Features/B/B.cs"   # scoped
```

The project's files are dealt into N shares by lines; each share is a Stryker
process with `concurrency: 1` in its own git worktree (Stryker builds in place,
and eight builds in one tree overwrite each other), and the reports are merged
into one under the project's `StrykerOutput`, where `stryker-read.mjs` reads
it. Why processes and not `--concurrency`: the MTP runner is the only one whose
per-test coverage works with xUnit v3, and it under-reports kills above
concurrency 1 (#3832); vstest runs eight at once but sees no coverage, because
v3 runs tests inside the test executable. A worktree has no `models/`, so the
tests that need a model skip by themselves (mark new ones `Category=Model`).
Phase 14's numbers: SearchEval's voice files in 4–6 minutes; a whole run of
`AI` (962 mutants) took as long as its slowest share, the numeric code of
`ParakeetFeatures`, whose loops make mutants time out. Put such a file in a share
of its own, or more than one, with `--mutate` spans.

The serial runner stays for a plan of many scoped projects:

```bash
bash .claude/skills/phase-close/stryker-run.sh <plan> <scratchpad>/stryker   # in the background
```

One project at a time, `concurrency: 1`. A configuration that names
`Integration.Tests` is run with its unit suites only (both runners do this).
With the Postgres suite in the run, Testcontainers starts a database a mutant,
and 13b's Ordering run hung for eight hours. Each project stops at
`STRYKER_MINUTES` (120 by default; `--minutes` for the parallel one). If the
log stops moving long before that, look at it: do not wait on it.

**A static field is out of Stryker's reach**: a `static readonly` table is
initialised once, so a mutant inside it cannot be switched on and off and
survives whatever the tests say (13c's price cues, 14's number words: 89 of
103 survivors). Make it a property before reading those survivors as gaps.

## 3. Read and classify

```bash
node .claude/skills/phase-close/stryker-read.mjs <Project> [file filters] [--detail]
```

**The score that counts is `of covered`.** Compare it with the project's
baseline in `docs/testing.md`. If it fell, the phase is not closed: close gaps
and run again. Classify every survivor in the phase's files as one of these:

- **Real gap**: write the test, then prove it with the `hand-mutation` skill
  against that same mutant. The test lands in the same commit, with the
  mutation listed.
- **Equivalent**: no input tells the two apart. Say why in one line.
- **Noise or runner artefact**: a message's wording, a static initialiser
  Stryker cannot toggle, a validator held by a suite the configuration does
  not include.
- **Unreachable**: say what holds it unreachable (a contract test, a type).

Survivors older than the phase are counted, not classified one by one.

## 4. Write it down

- `docs/testing.md`: a *Phase <n>'s close* block before
  `## Property-based (CsCheck)`. It has the table (project, mutated with,
  run 1, last run, survivors), *What Stryker found, and what closed it* and
  *Left, and what each is*, following the block before it.
- The **baselines** table: a whole run that rose sets the new baseline, with
  the date and the old value beside it.
- **Reports**: each HTML report goes to
  `docs/blog/reports/<date>-phase-<n>-<name>/stryker/<project>-run<k>.html`
  (private). Then delete this run's `StrykerOutput/<timestamp>` folders.
- `docs/delivery-plan.md`: the phase's status line, with its scores.
- `docs/blog/notebook.md`: what the run taught, with the numbers.
- `docs/book/material-map.md`: a row for the close in the phase's section
  (pointers and numbers, not prose).

## 5. Verify, then stop

- `dotnet build -warnaserror`, `dotnet format --no-restore --verify-no-changes --severity error`,
  then the whole `dotnet test --no-build`. In `frontend/`, lint, the tests,
  `format:check` and the builds, as CI runs them.
- **No commit, no push, no tag, no pull request** until the author asks for each
  in that message. Report: the scores against their baselines, the gaps closed
  (each with its mutation), what was left and what it is, and every temporary
  file or container left behind.
