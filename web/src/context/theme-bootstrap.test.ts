/// <reference types="node" />
import { readFileSync } from 'node:fs';
import { runInNewContext } from 'node:vm';
import { describe, it, expect, vi } from 'vitest';

const bootstrap = readFileSync('public/theme-bootstrap.js', 'utf8');

describe('pre-render theme bootstrap', () => {
  it.each([
    ['light', 'theme-light'],
    ['dark', 'theme-dark'],
    ['auto', 'theme-auto'],
    [null, 'theme-auto'],
    ['unexpected theme', 'theme-auto'],
  ])('applies %s before the application starts', (stored, expected) => {
    const add = vi.fn<(value: string) => void>();
    runInNewContext(bootstrap, {
      localStorage: { getItem: () => stored },
      document: { documentElement: { classList: { add } } },
    });
    expect(add).toHaveBeenCalledExactlyOnceWith(expected);
  });

  it('uses the system theme when storage is unavailable', () => {
    const add = vi.fn<(value: string) => void>();
    runInNewContext(bootstrap, {
      localStorage: {
        getItem: () => {
          throw new Error('Storage unavailable');
        },
      },
      document: { documentElement: { classList: { add } } },
    });
    expect(add).toHaveBeenCalledExactlyOnceWith('theme-auto');
  });
});
