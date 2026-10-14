---
name: hand-mutation
description: Prove a test written for existing code by mutating that code by hand — change one expression, build, see the test fail, restore byte for byte — and get the line the commit message lists. Use whenever a test is added beside code that already exists (CLAUDE.md, Testing rules), or to check that a new test can fail at all.
---

# A hand mutation

CLAUDE.md: *a test written for code that already exists earns its place by
failing against a hand-made mutation, listed in the commit message.* This skill
is that, done the same way every time. Every step below is there because doing
it by hand went wrong once:

- the mutant did not compile, so the test ran against the old binaries;
- the file was new and untracked, so `git diff` could not show whether it was
  restored;
- `sed` swallowed a backslash and the regex silently changed;
- the dev server did not recompile, so a browser test ran against the old bundle.

## How

1. **Choose the mutant.** Pick the smallest change that the test claims to
   catch: `<` to `<=`, `Sum` to `Max`, `||` to `&&`, a `return` removed, a
   constant changed. One mutant for each thing the test says it holds.
2. **Run the script** from the repository root:

   ```bash
   node .claude/skills/hand-mutation/mutate.mjs \
     --file src/Ucp/UcpAuthority.cs \
     --find "if (labels.Length < 2 || labels.Any" \
     --replace "if (labels.Length < 2 && labels.Any" \
     --build "dotnet build tests/Ucp.Tests --nologo -v q" \
     --test "./tests/Ucp.Tests/bin/Debug/net11.0/ElGuerre.Tendero.Ucp.Tests.exe -method \"*A_single_label_host*\" -noLogo -noColor" \
     --why "a single-label host binds"
   ```

   - **.NET**: build the *test* project, then run one class or method with the
     test exe (`-class "<FQN>"` or `-method "*Name*"`). With the stack running,
     which holds `src/*/bin` (the guard hook refuses a plain build), build
     beside it: `--build "dotnet build tests/X.Tests --artifacts-path artifacts/m --nologo -v q"`
     and run `artifacts/m/bin/ElGuerre.Tendero.X.Tests/debug/ElGuerre.Tendero.X.Tests.exe`.
     Keep that folder inside the repository: the tests find `Tendero.slnx` by
     walking up from their own folder, and from anywhere else 123 of them failed.
     Delete it afterwards.
   - **Front end**: `--build` and `--test` can be the same command,
     `npx nx test storefront --skip-nx-cache` from `frontend/`: it compiles and
     runs, and a type error fails it like a red test. Positional test names do
     not filter in nx, so the whole project runs.
   - **Browser specs**: not this way. The dev server does not reliably
     recompile under a script; prove the behaviour with a unit test instead.
3. **Read the verdict.**
   - `RED` and `restored: yes`: done. Paste the `for the commit:` line into the
     commit message.
   - `GREEN`: the test does not hold what it claims. Fix the test, not the
     mutant, and run it again.
   - `DID NOT COMPILE`: write the mutant in a form that compiles. A mutant that
     does not compile proves nothing.
   - `restored: NO`: stop and restore the file by hand before anything else.

## Never

- Never cite a mutation in a commit that was not run and seen red, restored.
- Never leave a mutated file behind: the script restores it in a `finally`,
  and says so.
