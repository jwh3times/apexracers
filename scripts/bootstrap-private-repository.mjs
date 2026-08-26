import { existsSync, readdirSync } from "node:fs";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { spawnSync } from "node:child_process";

const repositoryRoot = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const privateRoot = join(repositoryRoot, "private");
const defaultReference = "op://ApexRacers/ApexRacers Repository Access/CLONE_URL";

let explicitUrl = null;
let reference = defaultReference;
let serviceAccountReference = process.env.APEXRACERS_OP_SERVICE_ACCOUNT_REFERENCE ?? null;
for (let index = 2; index < process.argv.length; index += 1) {
  const argument = process.argv[index];
  if (!["--url", "--op-reference", "--service-account-reference"].includes(argument)) {
    throw new Error(`Unknown argument: ${argument}`);
  }
  const value = process.argv[index + 1];
  if (!value) throw new Error(`${argument} requires a value.`);
  if (argument === "--url") explicitUrl = value;
  else if (argument === "--op-reference") reference = value;
  else serviceAccountReference = value;
  index += 1;
}

if (existsSync(join(privateRoot, ".git"))) {
  console.log("The optional private companion is already installed.");
  process.exit(0);
}
if (existsSync(privateRoot) && readdirSync(privateRoot).length > 0) {
  throw new Error("Refusing to overwrite the non-empty private directory because it is not a Git worktree.");
}

let cloneUrl = explicitUrl;
if (!cloneUrl) {
  let result = spawnSync("op", ["read", reference], { encoding: "utf8", windowsHide: true });
  if ((result.status !== 0 || !result.stdout.trim()) && serviceAccountReference) {
    const tokenResult = spawnSync(
      "op",
      ["read", serviceAccountReference],
      { encoding: "utf8", windowsHide: true },
    );
    let serviceToken = tokenResult.status === 0 ? tokenResult.stdout.trim() : "";
    if (serviceToken) {
      const fullAccessEnvironment = { ...process.env, OP_SERVICE_ACCOUNT_TOKEN: serviceToken };
      result = spawnSync("op", ["read", reference], {
        encoding: "utf8",
        env: fullAccessEnvironment,
        windowsHide: true,
      });
      fullAccessEnvironment.OP_SERVICE_ACCOUNT_TOKEN = "";
      serviceToken = "";
    }
  }
  if (result.status !== 0 || !result.stdout.trim()) {
    throw new Error(
      "Could not retrieve the companion clone URL with the current 1Password identity or the optional service-account reference.",
    );
  }
  cloneUrl = result.stdout.trim();
}
if (/\r|\n/u.test(cloneUrl)) throw new Error("The clone URL must be a single line.");

if (/^https?:\/\//iu.test(cloneUrl)) {
  const parsed = new URL(cloneUrl);
  if (parsed.hostname.toLowerCase() !== "github.com" || parsed.username || parsed.password) {
    throw new Error("The HTTPS clone URL must target github.com and contain no embedded credential.");
  }
} else if (!/^git@github\.com:[A-Za-z0-9_.-]+\/[A-Za-z0-9_.-]+(?:\.git)?$/u.test(cloneUrl)) {
  throw new Error("The clone URL must be a credential-free GitHub HTTPS or SSH URL.");
}

const clone = spawnSync("git", ["clone", cloneUrl, privateRoot], { stdio: "inherit", windowsHide: true });
if (clone.status !== 0 || !existsSync(join(privateRoot, ".git"))) {
  throw new Error("The private companion clone did not complete successfully.");
}
console.log("Private companion installed at private/.");
