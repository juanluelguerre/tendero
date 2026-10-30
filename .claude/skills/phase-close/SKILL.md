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
node .claude/skills/phase-close/stryker-parallel.mjs src/<Project> --since phase/<previous> --shards 4   # the default
node .claude/skills/phase-close/stryker-parallel.mjs src/<Project> --shards 2 --mutate "A.cs,Features/B/B.cs"   # by hand
node .claude/skills/phase-close/stryker-parallel.mjs src/<Project> --shards 4                             # whole
```

**Mutate what the phase changed, not the project** (`--since` the previous phase's
tag). The cost is mutants × the suite: Search whole is 1,437 mutants at about
15 s each, six hours of CPU, and phase 14 had changed one of its files. A whole
run is for a new project or a baseline set on purpose, never what blocks a close.

**The machine is the run's alone.** A video model, a Docker build or the stack
beside it stretched every mutant five to seven times (2026-10-17), and more
shares than the cores can carry make it slower, not faster: **four shares**.
Before mutating, the runner times the suites without `Model` and `Integration`
and **stops on a test over 1 s or suites over 15 s**, naming them
(`--over-budget` runs anyway). Each mutant gets its initial run plus **30 s**
before it is a Timeout (`--timeout-ms`): Stryker's 5 s let a slower share call
140 mutants Timeouts — counted as kills, 63 of them real survivors. A share's log
is written as it runs (`artifacts/stryker-shards/<run>/shard-N.log`).

The project's files are dealt into N shares by lines; each share is a Stryker
process with `concurrency: 1` in its own git worktree (Stryker builds in place,
and eight builds in one tree overwrite each other), with **its own TEMP folder**,
and `coverage-analysis: all` written into the worktree's copy of the config. The
reports are merged into one under the project's `StrykerOutput`, where
`stryker-read.mjs` reads it. Each of those settings is a bug met on phase 14's
close (`docs/testing.md`, *Phase 14's close*):

- **Processes, not `--concurrency`**: the MTP runner is the only one whose
  coverage works with xUnit v3 (vstest's collector cannot see tests that run
  inside the test executable), and it under-reports kills above concurrency 1
  (#3832).
- **A TEMP per share**: shares sharing `%TEMP%` read each other's runner files —
  mutants killed by hand came back as survivors, an endless loop as one.
- **Coverage per mutant, not per test**: per test, a mutant was run against tests
  that never reach it (`tokens.Add` removed in TdtGreedy, "survived").
- **A ceiling that stops the tree** (`taskkill /T`): killing `dotnet` left a share
  running eleven hours past its 90 minutes.

**Control every run with one hand mutation** of a line its tests obviously
cover: if Stryker calls that mutant a survivor, the run is wrong, not the
tests. A worktree has no `models/`, so the tests that need a model skip by
themselves (mark new ones `Category=Model`). Numeric code with loops (Parakeet's
features) makes mutants time out: it sets the run's wall time, so give it a
share of its own.

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

### The frontend's shared libraries

`git diff --stat <previous phase tag>...HEAD -- frontend/libs/shared/`, and for
each library it names:

```bash
cd frontend && npm run mutate -- <library>   # the report: frontend/reports/mutation/<library>/
```

StrykerJS, one library a run, from `frontend/` so the libraries' paths resolve.
A library is seconds to minutes, so it is always run whole and its run is
compared with its baseline in `docs/testing.md`. The same rules apply: `of
covered` is the score, every survivor is classified, and a mutant inside a
store feature's or a module's top-level body is the same static artefact.
`tokens` holds no code, and the applications are not run (angular-cli#32182).

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
- **Reports are never committed** (decided 2026-10-30): the HTML stays in the
  ignored `StrykerOutput/` or `frontend/reports/` until the survivors are
  classified, and then those folders are deleted. What the repository keeps is
  the numbers and the classification above; an article that needs the report
  keeps a screenshot of it in `docs/blog/assets/`.
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
