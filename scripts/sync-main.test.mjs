import assert from "node:assert/strict";
import { mkdirSync, mkdtempSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import test from "node:test";
import { parseArgs, syncRepository } from "./sync-main.mjs";

function repository() {
  const root = mkdtempSync(join(tmpdir(), "apexracers-sync-main-"));
  mkdirSync(join(root, ".git"));
  return root;
}

function fakeGit(responses) {
  const calls = [];
  const runner = (_root, arguments_) => {
    calls.push(arguments_);
    const response = responses.shift();
    assert.ok(response, `Unexpected Git call: ${arguments_.join(" ")}`);
    return { status: 0, stdout: "", stderr: "", ...response };
  };
  return { calls, runner };
}

test("parseArgs enables both repositories by default", () => {
  assert.deepEqual(parseArgs([]), { syncPrivate: true });
  assert.deepEqual(parseArgs(["--skip-private"]), { syncPrivate: false });
  assert.throws(() => parseArgs(["--branch", "release"]), /Unknown argument/u);
});

test("syncRepository refuses a dirty worktree before fetching", (context) => {
  const root = repository();
  context.after(() => rmSync(root, { recursive: true, force: true }));
  const git = fakeGit([{ stdout: " M README.md\n" }]);

  const result = syncRepository(root, "public", git.runner);

  assert.equal(result.status, "failed");
  assert.match(result.detail, /uncommitted changes/u);
  assert.deepEqual(git.calls, [["status", "--porcelain=v1", "--untracked-files=normal"]]);
});

test("syncRepository switches to an existing main branch and fast-forwards it", (context) => {
  const root = repository();
  context.after(() => rmSync(root, { recursive: true, force: true }));
  const git = fakeGit([
    {},
    {},
    { stdout: "remote-commit\n" },
    { stdout: "feature\n" },
    {},
    {},
    { stdout: "1111111111111111111111111111111111111111\n" },
    {},
    { stdout: "2222222222222222222222222222222222222222\n" },
    { stdout: "3\n" },
  ]);

  const result = syncRepository(root, "public", git.runner);

  assert.deepEqual(result, {
    label: "public",
    status: "updated",
    detail: "main now at 2222222, 3 new commit(s) (was on feature)",
  });
  assert.deepEqual(git.calls[5], ["switch", "main"]);
  assert.deepEqual(git.calls[7], ["merge", "--ff-only", "origin/main"]);
});

test("syncRepository creates main from origin/main when no local branch exists", (context) => {
  const root = repository();
  context.after(() => rmSync(root, { recursive: true, force: true }));
  const git = fakeGit([
    {},
    {},
    { stdout: "remote-commit\n" },
    { stdout: "\n" },
    { status: 1 },
    {},
    { stdout: "abcdef0123456789\n" },
    {},
    { stdout: "abcdef0123456789\n" },
  ]);

  const result = syncRepository(root, "private", git.runner);

  assert.equal(result.status, "current");
  assert.match(result.detail, /was on detached HEAD/u);
  assert.deepEqual(git.calls[5], ["switch", "--create", "main", "--track", "origin/main"]);
});

test("syncRepository reports a fast-forward refusal without changing history", (context) => {
  const root = repository();
  context.after(() => rmSync(root, { recursive: true, force: true }));
  const git = fakeGit([
    {},
    {},
    { stdout: "remote-commit\n" },
    { stdout: "main\n" },
    { stdout: "local-commit\n" },
    { status: 1, stderr: "fatal: Not possible to fast-forward, aborting.\n" },
  ]);

  const result = syncRepository(root, "public", git.runner);

  assert.equal(result.status, "failed");
  assert.equal(result.detail, "fatal: Not possible to fast-forward, aborting.");
});
