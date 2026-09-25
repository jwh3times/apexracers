import assert from "node:assert/strict";
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import path from "node:path";
import test from "node:test";

import { lintNeutrality } from "./lint-agent-neutrality.mjs";

function withFixture(files, fn) {
  const root = mkdtempSync(path.join(tmpdir(), "lint-agent-neutrality-"));
  try {
    for (const [rel, content] of Object.entries(files)) {
      mkdirSync(path.dirname(path.join(root, rel)), { recursive: true });
      writeFileSync(path.join(root, rel), content);
    }
    fn(root);
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
}

test("clean sources pass, including links that resolve from both locations", () => {
  withFixture(
    {
      ".github/workflows/version.yml": "on: push\n",
      ".agents/skills/ship/SKILL.md": "See [version](../../../.github/workflows/version.yml).\n",
      ".agents/skills/teach/SKILL.md": "See [format](./FORMAT.md).\n",
      ".agents/skills/teach/FORMAT.md": "Format.\n",
      ".claude/agents/docs.md": "See [missing](./nowhere.md) — not a real file, so not flagged.\n",
    },
    (root) => assert.deepEqual(lintNeutrality(root), []),
  );
});

test("substitution artifacts are reported", () => {
  withFixture(
    {
      ".claude/agents/docs.md": "Read .Codex/settings.json and https://code.Codex.com/docs.\n",
      ".agents/skills/x/notes.md": "Put it in .Codex/skills.\n",
    },
    (root) => {
      const problems = lintNeutrality(root);
      assert.equal(problems.length, 3);
      assert.match(problems.join("\n"), /\.claude\/agents\/docs\.md: contains "\.Codex\/"/);
      assert.match(problems.join("\n"), /code\.Codex\.com/);
      assert.match(problems.join("\n"), /\.agents\/skills\/x\/notes\.md/);
    },
  );
});

test("a link that breaks once mirrored is reported", () => {
  withFixture(
    {
      ".claude/skills/ship/SKILL.md": "---\nname: ship\n---\n",
      ".claude/agents/docs.md": "Use [ship](../skills/ship/SKILL.md).\n",
      ".claude/agents/other.md": "See [peer](./docs.md).\n",
    },
    (root) => {
      const problems = lintNeutrality(root);
      assert.equal(problems.length, 2);
      assert.match(problems[0], /docs\.md: relative link `\.\.\/skills\/ship\/SKILL\.md`/);
      assert.match(problems[1], /other\.md: relative link `\.\/docs\.md`/);
    },
  );
});
