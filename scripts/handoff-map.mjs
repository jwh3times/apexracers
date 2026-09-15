/**
 * Read and update the cross-machine handoff map (`handoff_map.json`) kept in the synced Proton
 * Drive Handoffs folder, used by the handoff and lets-go skills.
 *
 * Usage:
 *
 *   node scripts/handoff-map.mjs get                 # {dir, key, file, path} for this repository
 *   node scripts/handoff-map.mjs set <file-name>     # mark <file-name> as this repository's active handoff
 *   node scripts/handoff-map.mjs clear               # mark this repository as having no active handoff
 *
 * Options: `--dir <path>` (else the HANDOFFS_DIR environment variable, else the Proton Drive
 * default under the home directory) and `--repo <name>` (else the origin remote's name).
 *
 * The map's keys are project names chosen by the user ("ApexRacers", "vcs-lab"), so a repository
 * matches a key case- and punctuation-insensitively. `set` adds a key when none matches; `get` and
 * `clear` never do.
 */

import { existsSync, readdirSync, readFileSync, writeFileSync } from "node:fs";
import { spawnSync } from "node:child_process";
import { homedir } from "node:os";
import { basename, join, resolve } from "node:path";
import { pathToFileURL } from "node:url";

export const MAP_FILE_NAME = "handoff_map.json";

export function parseArgs(argv) {
  const [command, ...rest] = argv;
  if (!["get", "set", "clear"].includes(command)) {
    throw new Error(`Expected a command of get, set, or clear; got: ${command ?? "(none)"}`);
  }
  const options = { command, dir: undefined, repo: undefined, file: undefined };
  for (let index = 0; index < rest.length; index += 1) {
    const argument = rest[index];
    if (argument === "--dir" || argument === "--repo") {
      const value = rest[index + 1];
      if (!value) throw new Error(`${argument} needs a value`);
      options[argument.slice(2)] = value;
      index += 1;
    } else if (command === "set" && options.file === undefined && !argument.startsWith("--")) {
      options.file = argument;
    } else {
      throw new Error(`Unknown argument: ${argument}`);
    }
  }
  if (command === "set" && !options.file) throw new Error("set needs the handoff file name");
  if (options.file && basename(options.file) !== options.file) {
    throw new Error(`set takes a file name inside the Handoffs folder, not a path: ${options.file}`);
  }
  return options;
}

export function normalizeKey(name) {
  return name.toLowerCase().replace(/[^a-z0-9]/gu, "");
}

export function findKey(activeHandoffs, repoName) {
  const wanted = normalizeKey(repoName);
  return Object.keys(activeHandoffs).find((key) => normalizeKey(key) === wanted) ?? null;
}

/** `MM-dd-yyyy HH:mm:ss` in local time — the format the map already uses. */
export function formatTimestamp(date) {
  const pad = (value) => String(value).padStart(2, "0");
  return (
    `${pad(date.getMonth() + 1)}-${pad(date.getDate())}-${date.getFullYear()} ` +
    `${pad(date.getHours())}:${pad(date.getMinutes())}:${pad(date.getSeconds())}`
  );
}

/** Returns a new map with `repoName`'s entry set to `file` (null clears it). */
export function updateMap(map, repoName, file, now) {
  const active = map.Active_Handoffs ?? {};
  const key = findKey(active, repoName) ?? (file === null ? null : repoName);
  if (key === null) return { map, key: null };
  return {
    key,
    map: {
      ...map,
      Last_Updated: formatTimestamp(now),
      Active_Handoffs: { ...active, [key]: file },
    },
  };
}

/** Name the origin remote points at, e.g. `apexracers` for `https://github.com/o/apexracers.git`. */
export function repoNameFromRemote(url) {
  const trimmed = url.trim().replace(/\/+$/u, "").replace(/\.git$/u, "");
  const name = trimmed.split(/[/:]/u).pop();
  return name || null;
}

/**
 * Candidate Handoffs folders, most specific first. The Proton Drive client nests the synced tree
 * under an account-named folder (`Proton Drive/<account>/My files/...`), so each child of the Proton
 * Drive root is tried as well as the root itself.
 */
export function candidateDirs(home, listDir) {
  const root = join(home, "Proton Drive");
  const tail = ["My files", "Documents", "Handoffs"];
  const accounts = listDir(root);
  return [...accounts.map((account) => join(root, account, ...tail)), join(root, ...tail)];
}

export function resolveHandoffsDir({ dir, env, home, exists, listDir }) {
  const explicit = dir ?? env.HANDOFFS_DIR;
  const candidates = explicit ? [resolve(explicit)] : candidateDirs(home, listDir);
  const found = candidates.find((candidate) => exists(join(candidate, MAP_FILE_NAME)));
  if (found) return found;
  throw new Error(
    `No ${MAP_FILE_NAME} found in: ${candidates.join(", ")}. ` +
      "Pass --dir or set HANDOFFS_DIR to the synced Proton Drive Handoffs folder.",
  );
}

function listDirectories(path) {
  try {
    return readdirSync(path, { withFileTypes: true })
      .filter((entry) => entry.isDirectory())
      .map((entry) => entry.name);
  } catch {
    return [];
  }
}

function currentRepoName() {
  const remote = spawnSync("git", ["remote", "get-url", "origin"], { encoding: "utf8", windowsHide: true });
  const fromRemote = remote.status === 0 ? repoNameFromRemote(remote.stdout) : null;
  if (fromRemote) return fromRemote;
  const top = spawnSync("git", ["rev-parse", "--show-toplevel"], { encoding: "utf8", windowsHide: true });
  if (top.status === 0 && top.stdout.trim()) return basename(top.stdout.trim());
  throw new Error("Not inside a Git repository; pass --repo <name>.");
}

export function main(argv) {
  const options = parseArgs(argv);
  const dir = resolveHandoffsDir({
    dir: options.dir,
    env: process.env,
    home: homedir(),
    exists: existsSync,
    listDir: listDirectories,
  });
  const mapPath = join(dir, MAP_FILE_NAME);
  const repoName = options.repo ?? currentRepoName();
  const map = JSON.parse(readFileSync(mapPath, "utf8"));

  if (options.command === "get") {
    const key = findKey(map.Active_Handoffs ?? {}, repoName);
    const file = key === null ? null : (map.Active_Handoffs[key] ?? null);
    const path = file === null ? null : join(dir, file);
    return { dir, repo: repoName, key, file, path, exists: path === null ? null : existsSync(path) };
  }

  const file = options.command === "set" ? options.file : null;
  if (file !== null && !existsSync(join(dir, file))) {
    throw new Error(`Refusing to point the map at a file that is not in ${dir}: ${file}`);
  }
  const updated = updateMap(map, repoName, file, new Date());
  if (updated.key !== null) {
    writeFileSync(mapPath, `${JSON.stringify(updated.map, null, 2)}\n`, "utf8");
  }
  const written = JSON.parse(readFileSync(mapPath, "utf8"));
  return {
    dir,
    repo: repoName,
    key: updated.key,
    file: updated.key === null ? null : (written.Active_Handoffs[updated.key] ?? null),
    lastUpdated: written.Last_Updated,
  };
}

if (process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href) {
  try {
    console.log(JSON.stringify(main(process.argv.slice(2)), null, 2));
  } catch (error) {
    console.error(error.message);
    process.exitCode = 1;
  }
}
