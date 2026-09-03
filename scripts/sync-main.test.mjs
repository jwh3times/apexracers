import assert from "node:assert/strict";
import { mkdirSync, mkdtempSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import test from "node:test";
import { isBranchCheckedOut, parseArgs, syncRepository } from "./sync-main.mjs";

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

test("isBranchCheckedOut reads linked-worktree porcelain", () => {
  const worktrees = [
    "worktree C:/primary",
    "HEAD abcdef",
    "branch refs/heads/main",
    "",
    "worktree C:/linked",
    "HEAD 123456",
    "branch refs/heads/feature",
  ].join("\n");

  assert.equal(isBranchCheckedOut(worktrees), true);
  assert.equal(isBranchCheckedOut(worktrees, "release"), false);
});

test("syncRepository refuses a dirty worktree before fetching", (context) => {
  const root = repository();
  context.after(() => rmSync(root, { recursive: true, force: true }));
  const git = fakeGit([{ stdout: " M README.md\n" }]);

  const result = syncRepository(root, "public", git.runner);

  assert.equal(result.status, "failed");
  assert.match(result.detail, /uncommitted changes/u);
  assert.deepEqual(git.calls, [
    ["status", "--porcelain=v1", "--untracked-files=normal"],
  ]);
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
    { stdout: "worktree C:/repo\nHEAD local\nbranch refs/heads/feature\n" },
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
  assert.deepEqual(git.calls[6], ["switch", "main"]);
  assert.deepEqual(git.calls[8], ["merge", "--ff-only", "origin/main"]);
});

test("syncRepository fast-forwards the current branch when another worktree holds main", (context) => {
  const root = repository();
  context.after(() => rmSync(root, { recursive: true, force: true }));
  const git = fakeGit([
    {},
    {},
    { stdout: "remote-commit\n" },
    { stdout: "jwh3times/lingcod\n" },
    {},
    { stdout: "worktree C:/primary\nHEAD remote\nbranch refs/heads/main\n" },
    {},
    { stdout: "1111111111111111111111111111111111111111\n" },
    {},
    { stdout: "2222222222222222222222222222222222222222\n" },
    { stdout: "1\n" },
  ]);

  const result = syncRepository(root, "public", git.runner);

  assert.deepEqual(result, {
    label: "public",
    status: "updated",
    detail:
      "jwh3times/lingcod now matches origin/main at 2222222, 1 new commit(s) (kept jwh3times/lingcod; main is checked out elsewhere)",
  });
  assert.deepEqual(git.calls[6], [
    "merge-base",
    "--is-ancestor",
    "HEAD",
    "origin/main",
  ]);
  assert.equal(
    git.calls.some((call) => call[0] === "switch"),
    false,
  );
  assert.deepEqual(git.calls[8], ["merge", "--ff-only", "origin/main"]);
});

test("syncRepository refuses a linked-worktree branch with local-only commits", (context) => {
  const root = repository();
  context.after(() => rmSync(root, { recursive: true, force: true }));
  const git = fakeGit([
    {},
    {},
    { stdout: "remote-commit\n" },
    { stdout: "feature\n" },
    {},
    { stdout: "worktree C:/primary\nHEAD remote\nbranch refs/heads/main\n" },
    { status: 1 },
  ]);

  const result = syncRepository(root, "public", git.runner);

  assert.deepEqual(result, {
    label: "public",
    status: "failed",
    detail:
      "main is checked out in another worktree and feature contains commits outside origin/main",
  });
  assert.equal(
    git.calls.some((call) => call[0] === "merge"),
    false,
  );
  assert.equal(
    git.calls.some((call) => call[0] === "switch"),
    false,
  );
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
  assert.deepEqual(git.calls[5], [
    "switch",
    "--create",
    "main",
    "--track",
    "origin/main",
  ]);
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
