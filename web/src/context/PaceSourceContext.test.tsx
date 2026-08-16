import { renderHook } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { usePaceSource } from './PaceSourceContext';

describe('usePaceSource', () => {
  it('requires the app-lifetime PaceSourceProvider', () => {
    expect(() => renderHook(() => usePaceSource())).toThrow(
      'usePaceSource must be used within PaceSourceProvider'
    );
  });
});
