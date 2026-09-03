import assert from "node:assert/strict";
import test from "node:test";
import {
  describeState,
  inspectRepository,
  isSynchronized,
  parseArgs,
} from "./repository-status.mjs";

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

function inspectWorktree(divergence) {
  const git = fakeGit([
    { stdout: "jwh3times/lingcod\n" },
    { stdout: "916a515\n" },
    { status: 1 },
    { stdout: "refs/remotes/origin/main\n" },
    { stdout: "916a515ae64dac503aa9c1c28ca4dd7fe845f7d7\n" },
    { stdout: "" },
    { stdout: `${divergence}\n` },
  ]);
  return {
    state: inspectRepository("public", "C:/repo", git.runner),
    calls: git.calls,
  };
}

test("parseArgs recognizes status modes", () => {
  assert.deepEqual(parseArgs([]), {
    checkAll: false,
    checkPrivate: false,
    json: false,
  });
  assert.deepEqual(parseArgs(["--check", "--json"]), {
    checkAll: true,
    checkPrivate: false,
    json: true,
  });
  assert.throws(() => parseArgs(["--branch"]), /Unknown argument/u);
});

test("a no-upstream worktree at its remote base is synchronized", () => {
  const { state, calls } = inspectWorktree("0 0");

  assert.equal(state.upstream, null);
  assert.equal(state.base, "origin/main");
  assert.equal(isSynchronized(state), true);
  assert.match(
    describeState(state),
    /upstream=<none> base=origin\/main.*ahead=0 behind=0/u,
  );
  assert.deepEqual(calls[3], [
    "config",
    "--get",
    "branch.jwh3times/lingcod.base",
  ]);
});

for (const [name, divergence] of [
  ["behind", "1 0"],
  ["ahead", "0 1"],
  ["diverged", "2 3"],
]) {
  test(`a no-upstream worktree that is ${name} its remote base is not synchronized`, () => {
    const { state } = inspectWorktree(divergence);
    assert.equal(isSynchronized(state), false);
  });
}

test("an ordinary upstream remains the comparison reference", () => {
  const git = fakeGit([
    { stdout: "feature\n" },
    { stdout: "abcdef0\n" },
    { stdout: "origin/feature\n" },
    { stdout: " M README.md\n" },
    { stdout: "0 0\n" },
  ]);

  const state = inspectRepository("public", "C:/repo", git.runner);

  assert.equal(state.upstream, "origin/feature");
  assert.equal(state.base, null);
  assert.equal(state.dirty, true);
  assert.equal(isSynchronized(state), false);
  assert.equal(
    git.calls.some((call) => call[0] === "config"),
    false,
  );
});
