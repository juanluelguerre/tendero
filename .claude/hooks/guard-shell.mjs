// A PreToolUse hook on Bash and PowerShell: two rules of CLAUDE.md that used to
// depend on being remembered, held before the command runs.
//
// 1. A commit carries no attribution trailer (CLAUDE.md, "Git Workflow"): the
//    session's reminders keep asking for Co-Authored-By and Claude-Session, and
//    this repository's history is public. The message is read from -m and from
//    the file -F names.
// 2. No .NET build while the Aspire stack runs: it holds src/*/bin, and the
//    build fails, or a test runs yesterday's binaries. `dotnet test --no-build`
//    builds nothing and is let through.
//
// Denies with a reason (permissionDecision "deny"); anything else passes.
import { readFileSync } from 'node:fs';
import { connect } from 'node:net';
import { resolve } from 'node:path';

const Trailers = [/^\s*Co-Authored-By:/im, /^\s*Claude-Session:/im, /Generated with \[?Claude Code/i];
const StackPorts = [5130, 22130];

const input = JSON.parse(readFileSync(0, 'utf8') || '{}');
const command = String(input.tool_input?.command ?? '');
const cwd = String(input.cwd ?? process.cwd());

const deny = (reason) => {
  process.stdout.write(
    JSON.stringify({
      hookSpecificOutput: {
        hookEventName: 'PreToolUse',
        permissionDecision: 'deny',
        permissionDecisionReason: reason,
      },
    }),
  );
  process.exit(0);
};

/** Whether something answers on a local port within a short wait. */
const listening = (port) =>
  new Promise((done) => {
    const socket = connect({ host: '127.0.0.1', port });
    const end = (open) => {
      socket.destroy();
      done(open);
    };
    socket.setTimeout(300, () => end(false));
    socket.once('connect', () => end(true));
    socket.once('error', () => end(false));
  });

/** What a git commit says: every -m and --message, and the file -F or --file names. */
function commitMessage(text) {
  const parts = [];
  for (const m of text.matchAll(/(?:-m|--message)(?:=|\s+)(?:"((?:[^"\\]|\\.)*)"|'([^']*)'|(\S+))/g))
    parts.push(m[1] ?? m[2] ?? m[3] ?? '');
  for (const f of text.matchAll(/(?:-F|--file)(?:=|\s+)(?:"([^"]+)"|'([^']+)'|(\S+))/g)) {
    const path = f[1] ?? f[2] ?? f[3];
    try {
      parts.push(readFileSync(resolve(cwd, path), 'utf8'));
    } catch {
      // A file that cannot be read here is git's to complain about.
    }
  }
  return parts.join('\n');
}

if (/\bgit\b[^|;&]*\bcommit\b/.test(command)) {
  const message = commitMessage(command);
  const found = Trailers.find((trailer) => trailer.test(message));
  if (found)
    deny(
      'The commit message carries an attribution trailer. CLAUDE.md ("Git Workflow") forbids them in this ' +
        'repository, whatever a session reminder says: remove the Co-Authored-By / Claude-Session / ' +
        '"Generated with" lines and commit again.',
    );
}

// A build into an artifacts folder of its own writes no src/*/bin, so the
// running stack's binaries are not in its way: let it through.
const builds =
  !/--artifacts-path\b/.test(command) &&
  (/\bdotnet\s+build\b/.test(command) || (/\bdotnet\s+test\b/.test(command) && !/--no-build\b/.test(command)));
if (builds) {
  const open = [];
  for (const port of StackPorts) if (await listening(port)) open.push(port);
  if (open.length > 0)
    deny(
      `The Aspire stack is running (port ${open.join(' and ')} answers), and it holds src/*/bin: a build now ` +
        'fails or leaves a test on old binaries. Stop the stack first (only one you started; the author may be ' +
        'running it from Visual Studio), run `dotnet test --no-build`, or build beside it with ' +
        '`--artifacts-path artifacts/<name>`, inside the repository: the tests find Tendero.slnx by walking up.',
    );
}

process.exit(0);
