import { existsSync } from "node:fs";
import { spawnSync } from "node:child_process";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const knownArguments = new Set(["--check", "--check-private", "--json"]);

export function parseArgs(argv) {
  const unknownArgument = argv.find(
    (argument) => !knownArguments.has(argument),
  );
  if (unknownArgument) throw new Error(`Unknown argument: ${unknownArgument}`);
  return {
    checkAll: argv.includes("--check"),
    checkPrivate: argv.includes("--check-private"),
    json: argv.includes("--json"),
  };
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

function requireGit(root, arguments_, git) {
  const result = git(root, arguments_);
  if (result.status !== 0) {
    throw new Error(
      `Git inspection failed for ${root} without exposing command output.`,
    );
  }
  return result.stdout.trim();
}

function optionalGit(root, arguments_, git) {
  const result = git(root, arguments_);
  return result.status === 0 ? result.stdout.trim() : null;
}

function normalizeRemoteRef(reference) {
  if (!reference?.startsWith("refs/remotes/")) return null;
  return reference.slice("refs/remotes/".length);
}

/**
 * Inspect one repository. A normal branch compares with its upstream. Linked-worktree branches
 * created without an upstream may instead compare with their recorded remote base, which proves
 * whether their commit is recoverable without misrepresenting that base as a push destination.
 */
export function inspectRepository(name, path, git = runGit) {
  const branchName = requireGit(path, ["branch", "--show-current"], git);
  const commit = requireGit(path, ["rev-parse", "--short", "HEAD"], git);
  const upstream = optionalGit(
    path,
    ["rev-parse", "--abbrev-ref", "--symbolic-full-name", "@{upstream}"],
    git,
  );

  let base = null;
  let comparisonRef = upstream;
  if (!comparisonRef && branchName) {
    const configuredBase = optionalGit(
      path,
      ["config", "--get", `branch.${branchName}.base`],
      git,
    );
    const normalizedBase = normalizeRemoteRef(configuredBase);
    if (
      normalizedBase &&
      optionalGit(
        path,
        ["rev-parse", "--verify", "--quiet", configuredBase],
        git,
      )
    ) {
      base = normalizedBase;
      comparisonRef = configuredBase;
    }
  }

  const changes = requireGit(
    path,
    ["status", "--porcelain=v1", "--untracked-files=normal"],
    git,
  )
    .split(/\r?\n/u)
    .filter(Boolean);

  let ahead = null;
  let behind = null;
  if (comparisonRef) {
    const counts = requireGit(
      path,
      ["rev-list", "--left-right", "--count", `${comparisonRef}...HEAD`],
      git,
    ).split(/\s+/u);
    behind = Number(counts[0]);
    ahead = Number(counts[1]);
  }

  return {
    name,
    path,
    branch: branchName || `(detached at ${commit})`,
    upstream,
    base,
    ahead,
    behind,
    dirty: changes.length > 0,
    changeCount: changes.length,
  };
}

export function isSynchronized(state) {
  return (
    !state.dirty &&
    Boolean(state.upstream || state.base) &&
    state.ahead === 0 &&
    state.behind === 0
  );
}

export function describeState(state) {
  const upstream = state.upstream ?? "<none>";
  const base = state.base ? ` base=${state.base}` : "";
  const reference = state.upstream || state.base;
  const divergence = reference
    ? `ahead=${state.ahead} behind=${state.behind}`
    : "ahead=? behind=?";
  return `${state.name}: branch=${state.branch} upstream=${upstream}${base} dirty=${state.dirty ? `yes(${state.changeCount})` : "no"} ${divergence}`;
}

export function main(argv) {
  const options = parseArgs(argv);
  const repositoryRoot = resolve(dirname(fileURLToPath(import.meta.url)), "..");
  const privateRoot = join(repositoryRoot, "private");
  const states = [inspectRepository("public", repositoryRoot)];
  let privateInstallation = "absent";

  if (existsSync(join(privateRoot, ".git"))) {
    states.push(inspectRepository("private", privateRoot));
    privateInstallation = "installed";
  } else if (existsSync(privateRoot)) {
    privateInstallation = "directory-without-git";
  }

  const payload = { privateInstallation, repositories: states };
  if (options.json) {
    console.log(JSON.stringify(payload, null, 2));
  } else {
    for (const state of states) console.log(describeState(state));
    if (privateInstallation === "absent")
      console.log("private: optional companion not installed");
    if (privateInstallation === "directory-without-git") {
      console.log(
        "private: directory exists but is not a companion Git worktree",
      );
    }
  }

  if (options.checkAll || options.checkPrivate) {
    const checkedStates = options.checkPrivate
      ? states.filter((state) => state.name === "private")
      : states;
    if (
      privateInstallation === "directory-without-git" ||
      checkedStates.some((state) => !isSynchronized(state))
    ) {
      return 1;
    }
  }
  return 0;
}

if (
  process.argv[1] &&
  import.meta.url === pathToFileURL(resolve(process.argv[1])).href
) {
  try {
    process.exitCode = main(process.argv.slice(2));
  } catch (error) {
    console.error(error instanceof Error ? error.message : String(error));
    process.exitCode = 1;
  }
}
