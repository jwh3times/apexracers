import assert from "node:assert/strict";
import { mkdtempSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import test from "node:test";
import {
  candidateDirs,
  findKey,
  formatTimestamp,
  main,
  parseArgs,
  repoNameFromRemote,
  resolveHandoffsDir,
  updateMap,
} from "./handoff-map.mjs";

const sampleMap = () => ({
  FileName: "handoff_map.json",
  Last_Updated: "09-15-2026 13:54:25",
  Active_Handoffs: { ApexRacers: null, "vcs-lab": "vcs-lab-handoff-2026-09-15.md" },
});

test("parseArgs accepts the three commands and rejects paths as file names", () => {
  assert.equal(parseArgs(["get"]).command, "get");
  assert.equal(parseArgs(["set", "a.md", "--repo", "x"]).file, "a.md");
  assert.equal(parseArgs(["clear", "--dir", "D"]).dir, "D");
  assert.throws(() => parseArgs([]), /Expected a command/u);
  assert.throws(() => parseArgs(["set"]), /needs the handoff file name/u);
  assert.throws(() => parseArgs(["set", "sub/a.md"]), /not a path/u);
  assert.throws(() => parseArgs(["get", "extra"]), /Unknown argument/u);
});

test("findKey matches the user's project names case- and punctuation-insensitively", () => {
  const active = { ApexRacers: null, GuardianTracker: "g.md", "vcs-lab": "v.md" };
  assert.equal(findKey(active, "apexracers"), "ApexRacers");
  assert.equal(findKey(active, "guardian-tracker"), "GuardianTracker");
  assert.equal(findKey(active, "VCS_Lab"), "vcs-lab");
  assert.equal(findKey(active, "leasebook"), null);
});

test("formatTimestamp uses the map's MM-dd-yyyy HH:mm:ss local format", () => {
  assert.equal(formatTimestamp(new Date(2026, 8, 5, 7, 3, 9)), "09-05-2026 07:03:09");
});

test("updateMap sets and clears the matching key without touching others", () => {
  const now = new Date(2026, 8, 15, 14, 0, 0);
  const set = updateMap(sampleMap(), "apexracers", "apexracers-handoff.md", now);
  assert.equal(set.key, "ApexRacers");
  assert.equal(set.map.Active_Handoffs.ApexRacers, "apexracers-handoff.md");
  assert.equal(set.map.Active_Handoffs["vcs-lab"], "vcs-lab-handoff-2026-09-15.md");
  assert.equal(set.map.Last_Updated, "09-15-2026 14:00:00");

  const cleared = updateMap(set.map, "ApexRacers", null, now);
  assert.equal(cleared.map.Active_Handoffs.ApexRacers, null);
});

test("updateMap adds a key only when setting, never when clearing", () => {
  const now = new Date();
  assert.equal(updateMap(sampleMap(), "leasebook", "l.md", now).map.Active_Handoffs.leasebook, "l.md");
  const cleared = updateMap(sampleMap(), "leasebook", null, now);
  assert.equal(cleared.key, null);
  assert.deepEqual(cleared.map, sampleMap());
});

test("repoNameFromRemote handles HTTPS and SSH remotes", () => {
  assert.equal(repoNameFromRemote("https://github.com/jwh3times/apexracers.git\n"), "apexracers");
  assert.equal(repoNameFromRemote("git@github.com:jwh3times/vcs-lab.git"), "vcs-lab");
  assert.equal(repoNameFromRemote("https://github.com/jwh3times/leasebook/"), "leasebook");
});

test("resolveHandoffsDir finds the account-nested Proton Drive folder", () => {
  const home = join("H");
  const nested = join(home, "Proton Drive", "acct", "My files", "Documents", "Handoffs");
  const listDir = () => ["acct"];
  assert.deepEqual(candidateDirs(home, listDir)[0], nested);
  const exists = (path) => path === join(nested, "handoff_map.json");
  assert.equal(resolveHandoffsDir({ env: {}, home, exists, listDir }), nested);
});

test("resolveHandoffsDir prefers --dir, then HANDOFFS_DIR, and explains a miss", () => {
  const exists = () => true;
  const listDir = () => [];
  assert.match(resolveHandoffsDir({ dir: "A", env: { HANDOFFS_DIR: "B" }, home: "H", exists, listDir }), /A$/u);
  assert.match(resolveHandoffsDir({ env: { HANDOFFS_DIR: "B" }, home: "H", exists, listDir }), /B$/u);
  assert.throws(
    () => resolveHandoffsDir({ env: {}, home: "H", exists: () => false, listDir }),
    /set HANDOFFS_DIR/u,
  );
});

test("main round-trips set, get, and clear against a real map file", (context) => {
  const dir = mkdtempSync(join(tmpdir(), "apexracers-handoff-map-"));
  context.after(() => rmSync(dir, { recursive: true, force: true }));
  writeFileSync(join(dir, "handoff_map.json"), JSON.stringify(sampleMap(), null, 2));
  writeFileSync(join(dir, "apexracers-handoff.md"), "# handoff\n");
  const common = ["--dir", dir, "--repo", "apexracers"];

  assert.throws(() => main(["set", "missing.md", ...common]), /Refusing/u);
  assert.equal(main(["set", "apexracers-handoff.md", ...common]).file, "apexracers-handoff.md");

  const got = main(["get", ...common]);
  assert.equal(got.key, "ApexRacers");
  assert.equal(got.path, join(dir, "apexracers-handoff.md"));
  assert.equal(got.exists, true);

  assert.equal(main(["clear", ...common]).file, null);
  const onDisk = JSON.parse(readFileSync(join(dir, "handoff_map.json"), "utf8"));
  assert.equal(onDisk.Active_Handoffs.ApexRacers, null);
  assert.equal(onDisk.Active_Handoffs["vcs-lab"], "vcs-lab-handoff-2026-09-15.md");
});
