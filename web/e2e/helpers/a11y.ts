import AxeBuilder from '@axe-core/playwright';
import { expect, type Page } from '@playwright/test';

/** WCAG 2.1 Level A & AA rule tags — the conformance target for this suite. */
const WCAG_TAGS = ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa'];

/** A single axe violation, derived from the AxeBuilder result so we need no extra dependency. */
export type Violation = Awaited<ReturnType<AxeBuilder['analyze']>>['violations'][number];

export type A11yOptions = {
  /** axe rule ids to skip (allowlist). Document every entry with a KNOWN-A11Y comment. */
  disableRules?: string[];
  /** CSS selectors to exclude from the scan (allowlist). Document every entry with a KNOWN-A11Y comment. */
  exclude?: string[];
};

/**
 * Renders axe violations into a readable, actionable failure message: one block
 * per violation with rule id, impact, help URL, and the failing node selectors.
 * Pure — exported for unit testing.
 */
export function formatViolations(violations: Violation[]): string {
  if (violations.length === 0) return 'No accessibility violations.';
  const blocks = violations.map(v => {
    const nodes = v.nodes.map(n => `      ${JSON.stringify(n.target)}`).join('\n');
    return [`  [${v.impact ?? 'unknown'}] ${v.id} — ${v.help}`, `    ${v.helpUrl}`, nodes].join(
      '\n'
    );
  });
  return `${violations.length} accessibility violation(s):\n${blocks.join('\n\n')}`;
}

/**
 * Runs axe-core against the current page state for WCAG 2.1 A/AA and asserts zero
 * violations. On failure the assertion message lists every violation. Pass
 * `disableRules`/`exclude` to allowlist a known issue — always with a
 * `// KNOWN-A11Y(<rule>): <reason> — follow-up: <ref>` comment at the call site.
 */
export async function auditA11y(page: Page, opts: A11yOptions = {}): Promise<void> {
  let builder = new AxeBuilder({ page }).withTags(WCAG_TAGS);
  if (opts.disableRules?.length) builder = builder.disableRules(opts.disableRules);
  for (const selector of opts.exclude ?? []) builder = builder.exclude(selector);
  const { violations } = await builder.analyze();
  expect(violations, formatViolations(violations)).toEqual([]);
}
