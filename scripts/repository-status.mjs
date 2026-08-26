import { existsSync } from "node:fs";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { spawnSync } from "node:child_process";

const repositoryRoot = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const privateRoot = join(repositoryRoot, "private");
const knownArguments = new Set(["--check", "--check-private", "--json"]);
const unknownArgument = process.argv.slice(2).find((argument) => !knownArguments.has(argument));
if (unknownArgument) throw new Error(`Unknown argument: ${unknownArgument}`);

function git(cwd, arguments_, allowFailure = false) {
  const result = spawnSync("git", ["-C", cwd, ...arguments_], {
    encoding: "utf8",
    windowsHide: true,
  });
  if (result.status !== 0) {
    if (allowFailure) return null;
    throw new Error(`Git inspection failed for ${cwd} without exposing command output.`);
  }
  return result.stdout.trim();
}

function inspect(name, path) {
  const branch = git(path, ["branch", "--show-current"]);
  const commit = git(path, ["rev-parse", "--short", "HEAD"]);
  const upstream = git(path, ["rev-parse", "--abbrev-ref", "--symbolic-full-name", "@{upstream}"], true);
  const changes = git(path, ["status", "--porcelain=v1", "--untracked-files=normal"])
    .split(/\r?\n/u)
    .filter(Boolean);
  let ahead = null;
  let behind = null;
  if (upstream) {
    const counts = git(path, ["rev-list", "--left-right", "--count", `${upstream}...HEAD`]).split(/\s+/u);
    behind = Number(counts[0]);
    ahead = Number(counts[1]);
  }
  return {
    name,
    path,
    branch: branch || `(detached at ${commit})`,
    upstream,
    ahead,
    behind,
    dirty: changes.length > 0,
    changeCount: changes.length,
  };
}

const states = [inspect("public", repositoryRoot)];
let privateInstallation = "absent";
if (existsSync(join(privateRoot, ".git"))) {
  states.push(inspect("private", privateRoot));
  privateInstallation = "installed";
} else if (existsSync(privateRoot)) {
  privateInstallation = "directory-without-git";
}

const payload = { privateInstallation, repositories: states };
if (process.argv.includes("--json")) {
  console.log(JSON.stringify(payload, null, 2));
} else {
  for (const state of states) {
    const upstream = state.upstream ?? "<none>";
    const divergence = state.upstream ? `ahead=${state.ahead} behind=${state.behind}` : "ahead=? behind=?";
    console.log(
      `${state.name}: branch=${state.branch} upstream=${upstream} dirty=${state.dirty ? `yes(${state.changeCount})` : "no"} ${divergence}`,
    );
  }
  if (privateInstallation === "absent") console.log("private: optional companion not installed");
  if (privateInstallation === "directory-without-git") {
    console.log("private: directory exists but is not a companion Git worktree");
  }
}

const checkAll = process.argv.includes("--check");
const checkPrivate = process.argv.includes("--check-private");
if (checkAll || checkPrivate) {
  const checkedStates = checkPrivate ? states.filter((state) => state.name === "private") : states;
  const unsynchronized = checkedStates.filter(
    (state) => state.dirty || !state.upstream || state.ahead !== 0 || state.behind !== 0,
  );
  if (privateInstallation === "directory-without-git" || unsynchronized.length > 0) process.exitCode = 1;
}
