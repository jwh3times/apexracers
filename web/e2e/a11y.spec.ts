import { test, expect } from '@playwright/test';
import { formatViolations, type Violation } from './helpers/a11y';

// Minimal fixture shaped like an axe violation — only the fields formatViolations reads.
const sampleViolation = {
  id: 'color-contrast',
  impact: 'serious',
  help: 'Elements must meet minimum color contrast ratio thresholds',
  helpUrl: 'https://dequeuniversity.com/rules/axe/4.10/color-contrast',
  nodes: [{ target: ['.kpi-value'] }],
};

test.describe('a11y helper: formatViolations', () => {
  test('summarizes rule id, impact, help URL, and node targets', () => {
    const message = formatViolations([sampleViolation] as unknown as Violation[]);
    expect(message).toContain('color-contrast');
    expect(message).toContain('serious');
    expect(message).toContain('dequeuniversity.com');
    expect(message).toContain('.kpi-value');
  });

  test('reports a clean message when there are no violations', () => {
    expect(formatViolations([])).toBe('No accessibility violations.');
  });
});
