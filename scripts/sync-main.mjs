/**
 * Move the public repository and its optional private companion to `main`,
 * then fast-forward each one to `origin/main`.
 *
 * Usage:
 *
 *   npm run sync:main
 *   npm run sync:main -- --skip-private
 *
 * A repository with uncommitted changes is refused, and updates are always
 * fast-forward-only. An uninstalled private companion is reported and skipped.
 */

import { existsSync } from "node:fs";
import { spawnSync } from "node:child_process";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

export const TARGET_BRANCH = "main";

export function parseArgs(argv) {
  const unknownArgument = argv.find((argument) => argument !== "--skip-private");
  if (unknownArgument) throw new Error(`Unknown argument: ${unknownArgument}`);
  return { syncPrivate: !argv.includes("--skip-private") };
}

export function isDirty(porcelain) {
  return porcelain.trim().length > 0;
}

const marks = {
  updated: "+",
  current: "=",
  skipped: "-",
  failed: "x",
};

export function describeResult(result) {
  return `${marks[result.status]} ${result.label}: ${result.detail}`;
}

export function runGit(root, arguments_) {
  const result = spawnSync("git", ["-C", root, ...arguments_], {
    encoding: "utf8",
    windowsHide: true,
  });
  return {
    status: result.status ?? 1,
    stdout: result.stdout ?? "",
    stderr: result.stderr || result.error?.message || "",
  };
}

function firstLine(...texts) {
  for (const text of texts) {
    const line = text
      .split(/\r?\n/u)
      .map((value) => value.trim())
      .find(Boolean);
    if (line) return line;
  }
  return "Git command failed without output";
}

/**
 * Synchronize one repository. The runner is injectable so behavior can be
 * tested without touching a developer's checkout or contacting a remote.
 */
export function syncRepository(root, label, git = runGit) {
  const fail = (detail) => ({ label, status: "failed", detail });

  if (!existsSync(join(root, ".git"))) {
    return { label, status: "skipped", detail: `no Git repository at ${root}` };
  }

  const status = git(root, ["status", "--porcelain=v1", "--untracked-files=normal"]);
  if (status.status !== 0) return fail(firstLine(status.stderr, status.stdout));
  if (isDirty(status.stdout)) {
    return fail("uncommitted changes; commit or stash them, then run this again");
  }

  const fetch = git(root, ["fetch", "--prune", "origin"]);
  if (fetch.status !== 0) return fail(firstLine(fetch.stderr, fetch.stdout));

  const remoteBranch = git(root, [
    "rev-parse",
    "--verify",
    "--quiet",
    `refs/remotes/origin/${TARGET_BRANCH}`,
  ]);
  if (remoteBranch.status !== 0) return fail(`origin/${TARGET_BRANCH} does not exist`);

  const branchResult = git(root, ["branch", "--show-current"]);
  if (branchResult.status !== 0) return fail(firstLine(branchResult.stderr, branchResult.stdout));
  const previousBranch = branchResult.stdout.trim() || "detached HEAD";

  if (previousBranch !== TARGET_BRANCH) {
    const localBranch = git(root, ["show-ref", "--verify", "--quiet", `refs/heads/${TARGET_BRANCH}`]);
    const switchArguments =
      localBranch.status === 0
        ? ["switch", TARGET_BRANCH]
        : ["switch", "--create", TARGET_BRANCH, "--track", `origin/${TARGET_BRANCH}`];
    const switchResult = git(root, switchArguments);
    if (switchResult.status !== 0) return fail(firstLine(switchResult.stderr, switchResult.stdout));
  }

  const beforeResult = git(root, ["rev-parse", "HEAD"]);
  if (beforeResult.status !== 0) return fail(firstLine(beforeResult.stderr, beforeResult.stdout));
  const before = beforeResult.stdout.trim();

  const merge = git(root, ["merge", "--ff-only", `origin/${TARGET_BRANCH}`]);
  if (merge.status !== 0) return fail(firstLine(merge.stderr, merge.stdout));

  const afterResult = git(root, ["rev-parse", "HEAD"]);
  if (afterResult.status !== 0) return fail(firstLine(afterResult.stderr, afterResult.stdout));
  const after = afterResult.stdout.trim();
  const switched = previousBranch === TARGET_BRANCH ? "" : ` (was on ${previousBranch})`;

  if (before === after) {
    return {
      label,
      status: "current",
      detail: `${TARGET_BRANCH} already up to date at ${after.slice(0, 7)}${switched}`,
    };
  }

  const count = git(root, ["rev-list", "--count", `${before}..${after}`]);
  const commitCount = count.status === 0 ? count.stdout.trim() : "";
  const commits = commitCount && commitCount !== "0" ? `, ${commitCount} new commit(s)` : "";
  return {
    label,
    status: "updated",
    detail: `${TARGET_BRANCH} now at ${after.slice(0, 7)}${commits}${switched}`,
  };
}

export function main(argv) {
  const options = parseArgs(argv);
  const repositoryRoot = resolve(dirname(fileURLToPath(import.meta.url)), "..");
  const privateRoot = join(repositoryRoot, "private");
  const results = [syncRepository(repositoryRoot, "public")];

  if (options.syncPrivate) {
    if (existsSync(join(privateRoot, ".git"))) {
      results.push(syncRepository(privateRoot, "private"));
    } else {
      results.push({
        label: "private",
        status: "skipped",
        detail: "optional companion not installed; run `npm run bootstrap:private`",
      });
    }
  }

  for (const result of results) console.log(describeResult(result));
  return results.some((result) => result.status === "failed") ? 1 : 0;
}

if (process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href) {
  try {
    process.exitCode = main(process.argv.slice(2));
  } catch (error) {
    console.error(error instanceof Error ? error.message : String(error));
    process.exitCode = 1;
  }
}
