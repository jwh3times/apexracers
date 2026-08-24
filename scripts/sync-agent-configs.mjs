#!/usr/bin/env node
// Generates the Claude-Code/Codex agent and skill configuration trees from their authored sources.
//
//   .claude/agents/<name>.md        ->  .codex/agents/<name>.toml
//   .agents/skills/<name>/**        ->  .claude/skills/<name>/**   (whole tree, not just SKILL.md)
//
// Skills are authored under .agents/skills/ — not .claude/skills/ — because that is where
// third-party skill installers write, so installing or updating a skill stays a one-way
// operation with no manual copying. .claude/skills/ is generated: every SKILL.md there gets
// a "GENERATED — do not edit" banner injected as a YAML comment on line 2 (line 1 stays `---`
// so frontmatter still parses); every other file in a skill directory (references, scripts/*.sh,
// agents/*.yaml, ...) is copied byte-for-byte with no banner and no text transformation.
//
// Agent prose stays tool-neutral and is copied VERBATIM — this script never rewrites wording.
//
//   node scripts/sync-agent-configs.mjs           # write the generated trees
//   node scripts/sync-agent-configs.mjs --check   # exit 1 if either tree has drifted (CI)
//   npm run sync:agents                           # same, via the root package.json script
//   npm run sync:agents -- --check                # same, check mode
//
// Run from anywhere; paths resolve against the repo root.
//
// Never reintroduce symlinks in place of this generator (e.g. `.claude/skills/<name>` ->
// `.agents/skills/<name>`). Two independent failure modes rule that out:
//   1. `readdirSync(dir, { withFileTypes: true })` reports a symlink as `isSymbolicLink()`,
//      not `isDirectory()` — a directory-walking generator like this one would see zero skill
//      sources and treat every real generated file as extraneous, deleting it on the next run.
//   2. Git may not even be able to store the link: when `core.symlinks` is `false` (common on
//      Windows), `git add` walks through the symlink and stages the target file's contents
//      under the link's path instead of recording a link — duplicating every file rather than
//      linking it. Check with `git config core.symlinks`; `git add -n .claude/skills/<name>`
//      listing individual files (instead of one link entry) confirms it.

import { readFileSync, writeFileSync, mkdirSync, readdirSync, rmSync, existsSync, statSync } from 'node:fs';
import { join, dirname, relative, sep, posix } from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = join(dirname(fileURLToPath(import.meta.url)), '..');
const CHECK = process.argv.includes('--check');

const AGENT_SRC = join(ROOT, '.claude', 'agents');
const AGENT_OUT = join(ROOT, '.codex', 'agents');
const SKILL_SRC = join(ROOT, '.agents', 'skills');
const SKILL_OUT = join(ROOT, '.claude', 'skills');

const BANNER = '# GENERATED FILE — DO NOT EDIT.\n# Source: .claude/agents/%s.md\n# Regenerate: node scripts/sync-agent-configs.mjs\n';

/**
 * Files this run intends to own, so anything else under a generated dir is pruned.
 * Value is either a string (text content, compared/written as utf8) or a Buffer
 * (binary/byte-for-byte content, compared/written raw).
 */
const produced = new Map(); // absolute output path -> string | Buffer
const srcOf = new Map(); // absolute output path -> absolute source path (for link resolution)

// ---------------------------------------------------------------- TOML encoding

/**
 * TOML forbids raw control characters in strings. Done as a code-point scan rather
 * than a regex so this file never has to contain literal control characters itself.
 */
function escapeControlChars(text, { keepNewlines }) {
  let out = '';
  for (const ch of text) {
    const cp = ch.codePointAt(0);
    const literalOk = ch === '\t' || (keepNewlines && ch === '\n');
    if (!literalOk && (cp < 0x20 || cp === 0x7f)) {
      out += '\\u' + cp.toString(16).padStart(4, '0');
    } else {
      out += ch;
    }
  }
  return out;
}

/** Escape a value for a TOML basic (single-line) string. */
function tomlString(value) {
  const escaped = escapeControlChars(
    value.replace(/\\/g, '\\\\').replace(/"/g, '\\"').replace(/\n/g, '\\n'),
    { keepNewlines: false },
  );
  return `"${escaped}"`;
}

/**
 * Escape a value for a TOML multi-line basic string.
 *
 * Backslashes must be escaped first (else they read as escape sequences and throw on
 * parse). Runs of 3+ quotes, and any trailing quote, would collide with the closing
 * delimiter, so each quote in such a run is emitted as `\"`. The run matched by
 * `/"{3,}/` and `/"+$/` is only ever quote characters — never a backslash — so it is
 * rebuilt directly with `repeat` rather than a nested escape that would imply a
 * backslash could sneak through.
 */
function tomlMultiline(value) {
  const escapedQuoteRun = (run) => '\\"'.repeat(run.length);
  const escaped = escapeControlChars(
    value
      .replace(/\\/g, '\\\\')
      .replace(/"{3,}/g, escapedQuoteRun)
      .replace(/"+$/, escapedQuoteRun),
    { keepNewlines: true },
  );
  return `"""\n${escaped}\n"""`;
}

/**
 * Independent TOML basic-string unescaper (per the TOML spec). Deliberately shares
 * NO code with the escapers above so that a round-trip check using it catches an
 * escaper regression instead of masking one. Used only for validation.
 */
function unescapeTomlBasic(text) {
  return text.replace(/\\(u[0-9a-fA-F]{4}|U[0-9a-fA-F]{8}|[btnfr"\\])/g, (_, esc) => {
    const c = esc[0];
    if (c === 'u' || c === 'U') return String.fromCodePoint(parseInt(esc.slice(1), 16));
    return { b: '\b', t: '\t', n: '\n', f: '\f', r: '\r', '"': '"', '\\': '\\' }[c];
  });
}

/**
 * Re-parse a generated agent TOML with the independent unescaper and confirm every
 * field decodes back to exactly what went in. A silent asymmetry in the encoder
 * (which would ship TOML that Codex can't load) fails here, in both write and check
 * modes, rather than reaching a commit.
 */
function assertAgentRoundTrips(slug, tomlText, expected) {
  const basic = '"((?:[^"\\\\]|\\\\.)*)"';
  const nameM = new RegExp(`^name = ${basic}$`, 'm').exec(tomlText);
  const descM = new RegExp(`^description = ${basic}$`, 'm').exec(tomlText);
  const bodyM = /developer_instructions = """\n([\s\S]*)\n"""\n?$/.exec(tomlText);
  if (!nameM || !descM || !bodyM) {
    throw new Error(`${slug}: generated TOML does not match the expected field layout`);
  }
  const got = {
    name: unescapeTomlBasic(nameM[1]),
    description: unescapeTomlBasic(descM[1]),
    developer_instructions: unescapeTomlBasic(bodyM[1]),
  };
  for (const key of Object.keys(expected)) {
    if (got[key] !== expected[key]) {
      throw new Error(`${slug}: round-trip mismatch on '${key}' — TOML escaping is not reversible`);
    }
  }
}

// ---------------------------------------------------------------- frontmatter

/**
 * Split `---\nkey: value\n---\nbody` into { data, body }.
 * Deliberately minimal: these files only ever use flat `key: value` scalars.
 */
function parseFrontmatter(text, file) {
  const match = /^---\n([\s\S]*?)\n---\n?/.exec(text);
  if (!match) throw new Error(`${file}: missing YAML frontmatter`);

  const data = {};
  for (const line of match[1].split('\n')) {
    if (!line.trim() || line.trimStart().startsWith('#')) continue;
    const kv = /^([A-Za-z0-9_-]+):\s*(.*)$/.exec(line);
    if (!kv) throw new Error(`${file}: cannot parse frontmatter line: ${line}`);
    data[kv[1]] = kv[2].trim().replace(/^["'](.*)["']$/, '$1');
  }
  return { data, body: text.slice(match[0].length) };
}

// ---------------------------------------------------------------- agents

/**
 * Claude's `tools:` list has no Codex equivalent. An agent that was never granted
 * Write or Edit is a read-only agent, which Codex expresses as a sandbox mode.
 * `model:` is intentionally dropped — Claude's model names are not Codex model names,
 * so the Codex session default applies.
 */
function sandboxModeFor(tools) {
  if (!tools) return null;
  const granted = tools.split(',').map((t) => t.trim());
  const mutates = granted.some((t) => t === 'Write' || t === 'Edit' || t === 'NotebookEdit');
  return mutates ? null : 'read-only';
}

function buildAgents() {
  if (!existsSync(AGENT_SRC)) return;

  for (const file of readdirSync(AGENT_SRC).filter((f) => f.endsWith('.md')).sort()) {
    const slug = file.slice(0, -3);
    const raw = readFileSync(join(AGENT_SRC, file), 'utf8').replace(/\r\n/g, '\n');
    const { data, body } = parseFrontmatter(raw, `.claude/agents/${file}`);

    for (const required of ['name', 'description']) {
      if (!data[required]) throw new Error(`.claude/agents/${file}: missing '${required}' in frontmatter`);
    }
    if (data.name !== slug) {
      throw new Error(`.claude/agents/${file}: frontmatter name '${data.name}' must match the filename '${slug}'`);
    }

    const instructions = body.trim();
    if (!instructions) throw new Error(`.claude/agents/${file}: body is empty`);

    const sandbox = sandboxModeFor(data.tools);
    const lines = [
      BANNER.replace('%s', slug),
      `name = ${tomlString(data.name)}`,
      `description = ${tomlString(data.description)}`,
      ...(sandbox ? [`sandbox_mode = ${tomlString(sandbox)}`] : []),
      `developer_instructions = ${tomlMultiline(instructions)}`,
    ];
    const content = lines.join('\n') + '\n';
    assertAgentRoundTrips(slug, content, {
      name: data.name,
      description: data.description,
      developer_instructions: instructions,
    });

    const out = join(AGENT_OUT, `${slug}.toml`);
    produced.set(out, content);
    srcOf.set(out, join(AGENT_SRC, file));
  }
}

// ---------------------------------------------------------------- skills

const SKILL_BANNER_FILE = 'SKILL.md';

/**
 * Insert a "GENERATED — do not edit" YAML comment as line 2 of a SKILL.md, keeping
 * `---` on line 1 so the frontmatter still parses. Text-mode (CRLF normalized) since
 * this file is parsed as YAML + Markdown, not compared byte-for-byte.
 */
function injectSkillBanner(content, relSrcPath) {
  const normalized = content.replace(/\r\n/g, '\n');
  const lines = normalized.split('\n');
  if (lines[0] !== '---') {
    throw new Error(`${relSrcPath}: expected SKILL.md to start with '---' frontmatter`);
  }
  lines.splice(1, 0, `# GENERATED — DO NOT EDIT. Source: ${relSrcPath}. Regenerate: npm run sync:agents`);
  return lines.join('\n');
}

/**
 * Extensions in the skill tree that hold text. Everything authored today is text, but a
 * future binary asset (an image, say) must still copy byte-for-byte, so the list gates
 * which files get line-ending normalization rather than assuming the whole tree is text.
 */
const TEXT_SKILL_EXTENSIONS = new Set(['.md', '.yaml', '.yml', '.sh', '.txt', '.json', '.toml']);

/**
 * Mirrors the whole authored skill tree (.agents/skills/<name>/**) into the generated
 * one (.claude/skills/<name>/**) — not just SKILL.md, so references, scripts/*.sh, and
 * agents/*.yaml stay drift-controlled too. SKILL.md gets the generated banner injected;
 * every other text file is copied verbatim with CRLF normalized, exactly as hook scripts
 * are. An earlier version compared those files as raw Buffers on the reasoning that
 * .gitattributes' `eol=lf` already normalizes both trees identically on checkout. It does
 * — but only at checkout: any tool that rewrites the generated tree afterwards can put
 * CRLF back, and then every one of those files reports as drifted forever while the
 * committed blobs are byte-identical (git normalizes on the way in, so `git status` stays
 * clean and CI, which checks out fresh, stays green — the failure is invisible except to
 * `--check` on a Windows working tree). Normalizing here removes that whole failure mode.
 * Files not on the text list are still copied byte-for-byte as a raw Buffer.
 */
function copySkillTree(srcDir, outDir) {
  if (!existsSync(srcDir)) return;

  for (const entry of readdirSync(srcDir).sort()) {
    const src = join(srcDir, entry);
    if (statSync(src).isDirectory()) {
      copySkillTree(src, join(outDir, entry));
    } else {
      const out = join(outDir, entry);
      const dot = entry.lastIndexOf('.');
      const ext = dot === -1 ? '' : entry.slice(dot).toLowerCase();
      if (entry === SKILL_BANNER_FILE) {
        const relSrcPath = relative(ROOT, src).split(sep).join(posix.sep);
        produced.set(out, injectSkillBanner(readFileSync(src, 'utf8'), relSrcPath));
      } else if (TEXT_SKILL_EXTENSIONS.has(ext)) {
        produced.set(out, readFileSync(src, 'utf8').replace(/\r\n/g, '\n'));
      } else {
        produced.set(out, readFileSync(src));
      }
      srcOf.set(out, src);
    }
  }
}

// ---------------------------------------------------------------- lint

/**
 * Because prose is copied verbatim, the sources must stay tool-neutral. Two checks
 * catch the ways that has actually gone wrong:
 *
 *  1. Blind-substitution artifacts — a global `claude`->`Codex` replace (how the
 *     original files were made) produces wrong-case paths and dead doc links.
 *  2. Depth-fragile relative links — a Markdown link that resolves to a real file
 *     from the SOURCE directory but to nothing from the mirrored OUTPUT directory
 *     (e.g. `../skills/...` from `.claude/agents/` points into `.codex/skills/`,
 *     which does not exist). Root-relative links that land in the same place from
 *     both locations pass, so this never false-positives on a working link.
 *
 * Only agent files and SKILL.md files are linted (Markdown/prose); hook scripts and
 * byte-for-byte skill assets (scripts/*.sh, agents/*.yaml, ...) are not — they are
 * copied as raw Buffers, not prose, so they're skipped here rather than coerced to text.
 */
function lintNeutrality() {
  const problems = [];
  const rel = (p) => relative(ROOT, p).split(sep).join('/');

  for (const [out, content] of produced) {
    if (typeof content !== 'string') continue;
    const isAgent = out.startsWith(AGENT_OUT + sep);
    const isSkill = out.startsWith(SKILL_OUT + sep);
    if (!isAgent && !isSkill) continue;

    for (const artifact of [/\.Codex\//, /code\.Codex\.com/]) {
      if (artifact.test(content)) {
        problems.push(`${rel(out)}: contains "${content.match(artifact)[0]}" — a blind claude->Codex substitution artifact; keep the source tool-neutral`);
      }
    }

    const srcDir = dirname(srcOf.get(out));
    const outDir = dirname(out);
    for (const m of content.matchAll(/\]\((\.\.?\/[^)\s]+)\)/g)) {
      const link = m[1].replace(/#.*$/, '');
      const fromSource = join(srcDir, link);
      const fromOutput = join(outDir, link);
      // Check the in-memory produced set first: on the very first generation (nothing
      // written to the output tree yet, e.g. bootstrapping a new source directory),
      // a sibling file this same run is about to write still exists only in `produced`,
      // not yet on disk — existsSync alone would misreport that as a broken link.
      if (existsSync(fromSource) && !produced.has(fromOutput) && !existsSync(fromOutput)) {
        problems.push(`${rel(out)}: relative link \`${m[1]}\` resolves in the source tree but breaks when mirrored here — write it as a plain repo-root-relative path`);
      }
    }
  }

  if (problems.length) {
    throw new Error('tool-neutrality lint failed:\n  ' + problems.join('\n  '));
  }
}

// ---------------------------------------------------------------- write / check

/** Every file currently under a generated root, so removals are detected too. */
function existingFiles(dir, acc = []) {
  if (!existsSync(dir)) return acc;
  for (const entry of readdirSync(dir)) {
    const p = join(dir, entry);
    if (statSync(p).isDirectory()) existingFiles(p, acc);
    else acc.push(p);
  }
  return acc;
}

/** True if two produced values (each a string or a Buffer) are identical. */
function contentEquals(a, b) {
  if (a === null || b === null) return a === b;
  const aIsBuffer = Buffer.isBuffer(a);
  const bIsBuffer = Buffer.isBuffer(b);
  if (aIsBuffer !== bIsBuffer) return false;
  return aIsBuffer ? a.equals(b) : a === b;
}

function main() {
  buildAgents();
  copySkillTree(SKILL_SRC, SKILL_OUT);
  lintNeutrality();

  const managed = [AGENT_OUT, SKILL_OUT];
  const onDisk = managed.flatMap((d) => existingFiles(d));
  const stale = onDisk.filter((p) => !produced.has(p));

  const drifted = [];
  for (const [path, content] of produced) {
    const isBuffer = Buffer.isBuffer(content);
    const current = existsSync(path)
      ? isBuffer
        ? readFileSync(path)
        : readFileSync(path, 'utf8').replace(/\r\n/g, '\n')
      : null;
    if (!contentEquals(current, content)) drifted.push(path);
  }

  const rel = (p) => relative(ROOT, p).split(sep).join('/');

  if (CHECK) {
    if (!drifted.length && !stale.length) {
      console.log(`Agent config in sync — ${produced.size} generated files match their sources.`);
      return;
    }
    console.error('Generated agent config is out of date.\n');
    for (const p of drifted) console.error(`  stale content  ${rel(p)}`);
    for (const p of stale) console.error(`  orphaned file  ${rel(p)}`);
    console.error('\nRun: node scripts/sync-agent-configs.mjs   (then commit the result)');
    process.exit(1);
  }

  for (const [path, content] of produced) {
    mkdirSync(dirname(path), { recursive: true });
    writeFileSync(path, content, Buffer.isBuffer(content) ? undefined : 'utf8');
  }
  for (const path of stale) rmSync(path);

  console.log(`Wrote ${produced.size} generated files (${drifted.length} changed, ${stale.length} pruned).`);
}

try {
  main();
} catch (err) {
  console.error(`sync-agent-configs: ${err.message}`);
  process.exit(1);
}
